// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Rack card — moving, selecting, copying and pasting a chain's devices (specs/042), the way the
// track's device chain does it:
// - drag a card by its header along its section, with a drop bar at the gap, or onto another
//   chain's row to move it there, mappings and all;
// - click a card to select it; Copy / Cut / Paste / Duplicate / Delete then run from its menu
//   and from ⌘C / ⌘X / ⌘V / ⌘D / Delete, which the device panel routes here.
// The clipboard is the engine's one device clipboard, so a copy crosses between the track chain
// and any rack chain. The instrument card is neither dragged nor copied.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed partial class RackCardView
{
    // A lane card: which chain, section and index it shows (for the drag's gaps).
    private sealed record RcCardTag(int Chain, RcSlot Slot, int Index);

    private StackPanel? _rcStrip;                                  // the chosen chain's lane
    private readonly List<(Border Row, int Chain)> _rcRows = new(); // the chain list rows, for drops

    // ---- selection --------------------------------------------------------------------

    private RackDeviceSel? RcSelOf(int c, RcSlot slot, int idx)
        => _ra.RackAddress is int rack && slot != RcSlot.Instrument ? new RackDeviceSel(rack, c, slot == RcSlot.Midi, idx) : null;

    private bool RcIsSelected(int c, RcSlot slot, int idx) => RcSelOf(c, slot, idx) is { } s && _ctx.RackSelection == s;

    // Index −1 is the chosen chain itself with no device selected: the keys' paste target stays
    // in the rack (⌘V lands at the end of that chain); copy, cut, duplicate and delete do nothing.
    private void RcSelect(RackDeviceSel? s)
    {
        if (s is null) { _ctx.SelectRackDevice(null, null); return; }
        if (s.Index < 0) { _ctx.SelectRackDevice(s, new RackDeviceCommands(() => { }, () => { }, () => RcPaste(s.Chain), () => { }, () => { })); return; }
        _ctx.SelectRackDevice(s, new RackDeviceCommands(() => RcCopy(s), () => RcCut(s), () => RcPaste(s.Chain, s), () => RcDuplicate(s), () => RcDelete(s)));
    }

    /// <summary>The user chose chain c of this rack: it becomes the paste target for the keys.</summary>
    private void RcSelectChain(int c)
    {
        if (_ra.RackAddress is int rack) RcSelect(new RackDeviceSel(rack, c, false, -1));
    }

    // A selection on this rack that no longer points at a device of the chosen chain falls back
    // to the chosen chain itself, so ⌘V keeps pasting into the rack.
    private void RcCheckSelection()
    {
        if (_ctx.RackSelection is not { } s || _ra.RackAddress != s.Rack) return;
        int n = s.Midi ? _ra.ChainMidiCount(s.Chain) : _ra.ChainDeviceCount(s.Chain);
        if (s.Chain != Sel || s.Index >= n) RcSelectChain(Sel);
        else RcSelect(s); // a rebuilt card hands the panel fresh commands
    }

    // ---- clipboard commands -----------------------------------------------------------

    private string RcDeviceName(RackDeviceSel s) => s.Midi ? _ra.ChainMidiName(s.Chain, s.Index) : _ra.ChainDeviceName(s.Chain, s.Index);

    private bool RcCopy(RackDeviceSel s)
    {
        bool ok = s.Midi ? E.RackDevChainMidiCopy(T, s.Rack, s.Chain, s.Index) : E.RackDevChainDeviceCopy(T, s.Rack, s.Chain, s.Index);
        if (ok) { _ctx.RackCopied(); _ctx.Status($"Copied {RcDeviceName(s)}"); }
        return ok;
    }

    private void RcCut(RackDeviceSel s)
    {
        E.BeginUndoGroup();
        try { if (RcCopy(s)) RcRemove(s); }
        finally { E.EndUndoGroup(); }
        RcSelect(null);
        DeferRebuild();
    }

    private void RcDelete(RackDeviceSel s)
    {
        RcRemove(s);
        RcSelect(null);
        DeferRebuild();
    }

    private void RcRemove(RackDeviceSel s)
    {
        if (s.Midi) _ra.RemoveChainMidi(s.Chain, s.Index); else _ra.RemoveChainDevice(s.Chain, s.Index);
    }

    private void RcDuplicate(RackDeviceSel s)
    {
        int at = s.Midi ? E.RackDevChainMidiDuplicate(T, s.Rack, s.Chain, s.Index) : E.RackDevChainDeviceDuplicate(T, s.Rack, s.Chain, s.Index);
        if (at < 0) { _ctx.Status("This device can't be duplicated"); return; }
        RcSelect(s with { Index = at });
        DeferRebuild();
    }

    /// <summary>True when the clipboard holds something chain c's sections can take.</summary>
    private bool RcCanPaste(int c)
    {
        int kind = E.DeviceClipboardKind();
        if (_ra.RackAddress is null || kind == 0) return false;
        bool midiOk = _rp.TakesMidiEffects && !(_rp.Kind == RackKind.Instrument && c >= RcMidiChainCount);
        if (kind == 2) return midiOk;
        if (kind == 4) return midiOk || _rp.TakesAudioEffects;   // several devices: whatever of them fits (specs/044)
        return _rp.TakesAudioEffects;
    }
    private const int RcMidiChainCount = 16; // MIDI effects run in an Instrument Rack's first 16 chains

    // Paste into chain c: after `after` when it is in the clipboard's section of that chain,
    // else at the end of that section.
    private void RcPaste(int c, RackDeviceSel? after = null)
    {
        if (_ra.RackAddress is not int rack) return;
        int kind = E.DeviceClipboardKind();
        if (kind == 0) { _ctx.Status("Nothing to paste"); return; }
        if (kind == 4) { RcPasteMany(rack, c, after); return; }
        bool midi = kind == 2;
        int at = after is { Index: >= 0 } s && s.Chain == c && s.Midi == midi ? s.Index + 1 : -1;
        bool keyed = !midi && E.DeviceClipboardSidechain();
        int r = midi ? E.RackDevChainMidiPaste(T, rack, c, at) : E.RackDevChainDevicePaste(T, rack, c, at);
        if (r < 0) { _ctx.Status(RcPasteRefusal(r, midi)); return; }
        var pasted = new RackDeviceSel(rack, c, midi, r);
        _ctx.Status($"Pasted {RcDeviceName(pasted)}{(keyed ? " · sidechain dropped" : "")}");
        if (Sel != c) Sel = c;
        RcSelect(pasted);
        DeferRebuild();
    }

    // Several devices copied (specs/044): each section takes its own, after the selected device
    // of that section or at its end; the rest are skipped. One undo step.
    private void RcPasteMany(int rack, int c, RackDeviceSel? after)
    {
        int midiAt = after is { Index: >= 0, Midi: true } m && m.Chain == c ? m.Index + 1 : -1;
        int fxAt = after is { Index: >= 0, Midi: false } d && d.Chain == c ? d.Index + 1 : -1;
        int n = E.RackDevChainPasteDevices(T, rack, c, midiAt, fxAt, out int skipped, out int firstMidi, out int firstFx);
        if (n < 0) { _ctx.Status(n == -3 ? "Couldn't paste: a device failed to load." : "Nothing to paste here."); return; }
        if (n == 0) { _ctx.Status("This chain can't take the copied devices."); return; }
        _ctx.Status(skipped > 0 ? $"Pasted {n} devices, skipped {skipped} this chain can't take" : $"Pasted {n} devices");
        if (Sel != c) Sel = c;
        RcSelect(firstFx >= 0 ? new RackDeviceSel(rack, c, false, firstFx) : new RackDeviceSel(rack, c, true, firstMidi));
        DeferRebuild();
    }

    private string RcPasteRefusal(int code, bool midi) => code switch
    {
        -2 => _rp.Kind switch
        {
            RackKind.MidiEffect => "A MIDI Effect Rack takes MIDI effects only.",
            RackKind.AudioEffect => "An Audio Effect Rack takes audio effects only.",
            _ => midi ? "This rack's chains take no MIDI effects." : "This rack's chains take no audio effects.",
        },
        -3 => "A rack can't go inside a rack.",
        -5 => "MIDI effects run in the first 16 chains.",
        _ => "Nothing to paste here.",
    };

    // ---- undo for the card's value controls -------------------------------------------
    // A rack's faders, modes and switches write in place; these make each gesture one undo step:
    // a checkpoint forks the rack first (children and held notes carry over), and a drag's moves
    // land inside one undo group, so a zone drag that forks on every move undoes as one.

    private (Action Begin, Action End) RcEditGesture()
    {
        bool open = false;
        return (() => { if (open) return; open = true; E.BeginUndoGroup(); _ra.Checkpoint(); },
                () => { if (!open) return; open = false; E.EndUndoGroup(); });
    }

    private void RcStep(Action set)
    {
        E.BeginUndoGroup();
        try { _ra.Checkpoint(); set(); }
        finally { E.EndUndoGroup(); }
    }

    // ---- menus ----------------------------------------------------------------------

    // The card menu's move and clipboard groups (Open, on/off and the instrument items come first).
    private void RcAddDeviceMenuItems(MenuFlyout f, int c, RcSlot slot, int idx)
    {
        if (RcSelOf(c, slot, idx) is not { } s) return;
        int n = s.Midi ? _ra.ChainMidiCount(c) : _ra.ChainDeviceCount(c);
        MenuKit.AddGroups(f.Items,
            new Control[]
            {
                MenuKit.Item("Move left", GlyphKind.StepLeft, () => RcMove(s, idx - 1), null, idx > 0),
                MenuKit.Item("Move right", GlyphKind.StepRight, () => RcMove(s, idx + 1), null, idx < n - 1),
            },
            new Control[]
            {
                MenuKit.Item("Copy", GlyphKind.Copy, () => RcCopy(s), MenuKit.CopyKey),
                MenuKit.Item("Cut", GlyphKind.Cut, () => RcCut(s), MenuKit.CutKey),
                MenuKit.Item("Paste", GlyphKind.Paste, () => RcPaste(c, s), MenuKit.PasteKey, RcCanPaste(c)),
                MenuKit.Item("Duplicate", GlyphKind.Duplicate, () => RcDuplicate(s), MenuKit.DuplicateKey),
            },
            new Control[] { MenuKit.Danger("Delete", GlyphKind.Trash, () => RcDelete(s), MenuKit.DeleteKey) });
    }

    // A "Paste" item for a chain (its row menu, the lane's add menus): at the end of the section.
    private MenuItem RcPasteItem(int c) => MenuKit.Item("Paste", GlyphKind.Paste, () => RcPaste(c), MenuKit.PasteKey, RcCanPaste(c));

    private void RcMove(RackDeviceSel s, int to)
    {
        if (s.Midi) _ra.MoveChainMidi(s.Chain, s.Index, to); else _ra.MoveChainDevice(s.Chain, s.Index, to);
        RcSelect(s with { Index = to });
        DeferRebuild();
    }

    // ---- drag: along the section, or onto another chain's row ------------------------

    private RcCardTag? _rcDrag;
    private Control? _rcDragCard;
    private bool _rcDragging;
    private Point _rcDragStart;
    private int _rcDragRow = -1;
    private readonly Border _rcDropBar = new()
    {
        Width = 3, Background = NotaPalette.AccentBright, CornerRadius = NotaRadius.Clip,
        Margin = new Thickness(-2, 6, -2, 6), IsHitTestVisible = false,
    };

    private List<Control> RcSectionCards(RcCardTag t)
        => _rcStrip is null ? new() : _rcStrip.Children.OfType<Control>().Where(x => x.Tag is RcCardTag g && g.Chain == t.Chain && g.Slot == t.Slot).ToList();

    private int RcGapAt(RcCardTag t, double x)
    {
        var cards = RcSectionCards(t);
        int gap = 0;
        foreach (var card in cards) { if (x > card.Bounds.X + card.Bounds.Width * 0.5) gap++; else break; }
        return Math.Clamp(gap, 0, cards.Count);
    }

    private void RcShowDropBar(RcCardTag t, int gap)
    {
        if (_rcStrip is null) return;
        _rcStrip.Children.Remove(_rcDropBar);
        if (gap < 0) return;
        var cards = RcSectionCards(t);
        if (cards.Count == 0) return;
        int at = gap < cards.Count ? _rcStrip.Children.IndexOf(cards[gap]) : _rcStrip.Children.IndexOf(cards[^1]) + 1;
        _rcStrip.Children.Insert(Math.Clamp(at, 0, _rcStrip.Children.Count), _rcDropBar);
    }

    // The chain row under the pointer that can take the dragged device, or −1.
    private int RcRowAt(RcCardTag t, PointerEventArgs e)
    {
        foreach (var (row, chain) in _rcRows)
        {
            if (chain == t.Chain) continue;
            var p = e.GetPosition(row);
            if (p.X < 0 || p.Y < 0 || p.X > row.Bounds.Width || p.Y > row.Bounds.Height) continue;
            if (t.Slot == RcSlot.Midi && _rp.Kind == RackKind.Instrument && chain >= RcMidiChainCount) return -1;
            return chain;
        }
        return -1;
    }

    private void RcMarkRow(int chain)
    {
        if (chain == _rcDragRow) return;
        foreach (var (row, c) in _rcRows)
        {
            if (c == _rcDragRow) row.BorderBrush = c == Sel ? NotaPalette.Accent : NotaPalette.BorderDefault;
            if (c == chain) row.BorderBrush = NotaPalette.AccentBright;
        }
        _rcDragRow = chain;
    }

    /// <summary>Header press on a lane card: select it and arm a drag (the grip arms one too).</summary>
    private void RcHookCardDrag(Control handle, Control card, int c, RcSlot slot, int idx)
    {
        var tag = new RcCardTag(c, slot, idx);
        card.Tag = tag;
        handle.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(handle).Properties;
            if (!pt.IsLeftButtonPressed || e.ClickCount > 1) return;
            RcSelect(RcSelOf(c, slot, idx));
            RcMarkSelected();
            _rcDrag = tag; _rcDragCard = card; _rcDragging = false; _rcDragRow = -1;
            _rcDragStart = e.GetPosition(_rcStrip);
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (_rcDrag is not { } t || _rcStrip is null) return;
            var p = e.GetPosition(_rcStrip);
            if (!_rcDragging && Math.Abs(p.X - _rcDragStart.X) < 5 && Math.Abs(p.Y - _rcDragStart.Y) < 5) return;
            _rcDragging = true;
            if (_rcDragCard != null) _rcDragCard.Opacity = 0.55;
            int row = RcRowAt(t, e);
            RcMarkRow(row);
            RcShowDropBar(t, row >= 0 ? -1 : RcGapAt(t, p.X));
        };
        handle.PointerReleased += (_, e) =>
        {
            if (_rcDrag is not { } t) return;
            bool dragged = _rcDragging;
            int row = dragged ? RcRowAt(t, e) : -1;
            int gap = dragged && row < 0 && _rcStrip != null ? RcGapAt(t, e.GetPosition(_rcStrip).X) : -1;
            RcEndDrag();
            e.Pointer.Capture(null);
            if (!dragged) return;
            if (row >= 0) { RcMoveToChain(t, row); return; }
            int n = RcSectionCards(t).Count;
            int to = Math.Clamp(gap > t.Index ? gap - 1 : gap, 0, Math.Max(0, n - 1));
            if (gap < 0 || to == t.Index) return;
            if (RcSelOf(t.Chain, t.Slot, t.Index) is { } s) RcMove(s, to);
        };
        handle.PointerCaptureLost += (_, _) => RcEndDrag();
    }

    private void RcEndDrag()
    {
        if (_rcDragCard != null) _rcDragCard.Opacity = 1.0;
        _rcStrip?.Children.Remove(_rcDropBar);
        RcMarkRow(-1);
        _rcDrag = null; _rcDragCard = null; _rcDragging = false;
    }

    private void RcMoveToChain(RcCardTag t, int to)
    {
        if (_ra.RackAddress is not int rack) return;
        bool midi = t.Slot == RcSlot.Midi;
        int r = midi ? E.RackDevChainMidiMoveTo(T, rack, t.Chain, t.Index, to) : E.RackDevChainDeviceMoveTo(T, rack, t.Chain, t.Index, to);
        if (r < 0) { _ctx.Status(r == -5 ? "MIDI effects run in the first 16 chains." : "The device can't go there."); return; }
        Sel = to;
        RcSelect(new RackDeviceSel(rack, to, midi, r));
        DeferRebuild();
    }

    // Show the lane's selection ring without a rebuild (a click selects, then may drag).
    private readonly List<(Border Ring, RcCardTag Tag)> _rcCards = new();

    private void RcMarkSelected()
    {
        foreach (var (ring, t) in _rcCards) ring.IsVisible = RcIsSelected(t.Chain, t.Slot, t.Index);
    }
}
