// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — selection + clipboard + drag-reorder for the device chain.
// A device card can be selected (accent border), copied/cut/pasted (Ctrl+C/X/V or the
// header context menu) and deleted (Delete). The clipboard survives track switches so a
// device can be pasted onto another track. Reorder is also possible by dragging a card's
// ⠿ handle; an accent bar marks the drop gap within the same domain (MIDI FX / audio FX).

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    // ---- selection --------------------------------------------------------
    private ChainKind _selChainKind;
    private int _selDeviceIndex = -1;   // -1 = nothing selected. Paired with _selChainKind.

    // Only audio effects and MIDI effects participate in copy/cut/paste/delete (the
    // instrument is the track's, not a movable device; sends/add aren't devices).
    private bool SelectableKind(ChainKind k) => k == ChainKind.Effect || k == ChainKind.Midi;
    private bool HasDeviceSelection => _selDeviceIndex >= 0 && SelectableKind(_selChainKind);

    private void SelectDevice(ChainKind kind, int index)
    {
        if (!SelectableKind(kind)) return;
        Focus();   // route Ctrl+C/X/V + Delete here
        if (_selChainKind == kind && _selDeviceIndex == index) return;
        _selChainKind = kind; _selDeviceIndex = index;
        Rebuild();
    }

    private bool IsSelected(ChainKind kind, int index)
        => HasDeviceSelection && _selChainKind == kind && _selDeviceIndex == index;

    // ---- clipboard --------------------------------------------------------
    // The device itself is copied by the engine (#238): a full clone with every parameter,
    // its state blob, bypass and sidechain key (a rack or plug-in copies itself), so a paste
    // has the source's settings even after the source was cut or changed. Only what the UI
    // owns rides here: the domain and the card's preset label.
    private sealed class DeviceClip
    {
        public ChainKind Kind;
        public string Preset = "", PresetId = "";   // the source card's preset label follows the paste
    }
    private DeviceClip? _clip;

    private void CopySelectedDevice()
    {
        if (!HasDeviceSelection) return;
        int di = _selDeviceIndex;
        bool ok = _selChainKind switch
        {
            ChainKind.Effect => _engine.CopyDevice(_trackId, di),
            ChainKind.Midi => _engine.CopyMidiEffect(_trackId, di),
            _ => false,
        };
        if (!ok) return;
        var ex = Extra(_selChainKind, di);
        _clip = new DeviceClip { Kind = _selChainKind, Preset = ex.Preset, PresetId = ex.PresetId };
    }

    private void DeleteSelectedDevice()
    {
        if (!HasDeviceSelection) return;
        int di = _selDeviceIndex; var k = _selChainKind;
        if (k == ChainKind.Effect) _engine.RemoveDevice(_trackId, di);
        else if (k == ChainKind.Midi) _engine.RemoveMidiEffect(_trackId, di);
        else return;
        ExtrasRemoved(k, di);
        _selDeviceIndex = -1;
        Rebuild(); Changed?.Invoke();
    }

    private void CutSelectedDevice() { CopySelectedDevice(); DeleteSelectedDevice(); }

    private void PasteDevice()
    {
        if (_clip is null || _trackId <= 0) return;
        var c = _clip;
        if (c.Kind != ChainKind.Effect && c.Kind != ChainKind.Midi) return;
        // Drop it right after the selected device of the same domain, else at the end.
        int at = _selChainKind == c.Kind && _selDeviceIndex >= 0 ? _selDeviceIndex + 1 : -1;
        int end = c.Kind == ChainKind.Midi ? _engine.TrackMidiEffectCount(_trackId) : _engine.TrackDeviceCount(_trackId);
        if (at > end) at = -1;
        int newIdx = c.Kind == ChainKind.Midi ? _engine.PasteMidiEffect(_trackId, at) : _engine.PasteDevice(_trackId, at);
        if (newIdx < 0) return;
        // A fresh slot: drop any stale entry, and let the extras after it shift up with their devices.
        if (newIdx < end) ExtrasMoved(c.Kind, end, newIdx);
        TrackExtras().Remove(ExtraKey(c.Kind, newIdx));
        var pasted = Extra(c.Kind, newIdx); pasted.Preset = c.Preset; pasted.PresetId = c.PresetId;
        _selChainKind = c.Kind;
        _selDeviceIndex = newIdx;
        Rebuild(); Changed?.Invoke();
    }

    // ---- ⌘G / ⌘⇧G: wrap the selected device in a rack / unwrap it (#257) ----
    // With the Devices panel focused these act on the device, never on the tracks: grouping
    // tracks stays ⌘G in the arrangement and the track headers.
    private bool SelectedIsRack => _selChainKind == ChainKind.Effect && _selDeviceIndex >= 0
        && _engine.TrackDeviceBuiltinKind(_trackId, _selDeviceIndex) == 5;

    private void GroupSelectedDevice()
    {
        if (!HasDeviceSelection) { StatusMessage?.Invoke("Select an audio effect to put it in a rack"); return; }
        if (_selChainKind == ChainKind.Midi) { StatusMessage?.Invoke("MIDI effects can't go in a rack"); return; }
        int di = _selDeviceIndex;
        string name = _engine.DeviceName(_trackId, di);
        if (_engine.WrapDeviceInRack(_trackId, di) < 0)
        {
            StatusMessage?.Invoke($"{name} can't go in a rack: it has a sidechain key, automation, a modulation link or an Arp's CC lane");
            return;
        }
        // The device's A/B snapshots and preset label live only here: the rack keeps them for
        // when ⌘⇧G (or undo) brings the device back out.
        TrackExtras().Remove(ExtraKey(ChainKind.Effect, di), out var own);
        if (own != null) Extra(ChainKind.Effect, di).Wrapped = own;
        StatusMessage?.Invoke($"Put {name} in an Audio Effect Rack");
        Rebuild(); Changed?.Invoke();
    }

    private void UngroupSelectedDevice()
    {
        if (!SelectedIsRack) { StatusMessage?.Invoke("Select an Audio Effect Rack to take its devices out"); return; }
        int di = _selDeviceIndex;
        int end = _engine.TrackDeviceCount(_trackId);
        var wrapped = Extra(ChainKind.Effect, di).Wrapped;
        int n = _engine.UnwrapRack(_trackId, di);
        if (n < 0)
        {
            StatusMessage?.Invoke("Only a rack with one chain can be ungrouped, and not while it is automated, modulated or driven by an Arp's CC lane");
            return;
        }
        // The rack's slot becomes its n devices; the cards after it shift with them.
        if (n == 0) ExtrasRemoved(ChainKind.Effect, di);
        else
        {
            TrackExtras().Remove(ExtraKey(ChainKind.Effect, di));
            for (int i = 1; i < n; i++) ExtrasMoved(ChainKind.Effect, end - 1 + i, di + i);
            if (wrapped != null && wrapped.Sig == CardSig(_trackId, ChainKind.Effect, di))
                TrackExtras()[ExtraKey(ChainKind.Effect, di)] = wrapped;
        }
        _selDeviceIndex = n > 0 ? di : -1;
        StatusMessage?.Invoke(n == 1 ? "Took the device out of the rack" : $"Took {n} devices out of the rack");
        Rebuild(); Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool mod = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        if (mod && e.Key == Key.G && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) UngroupSelectedDevice(); else GroupSelectedDevice();
            e.Handled = true; return;
        }
        if (mod && e.Key == Key.C && HasDeviceSelection) { CopySelectedDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.X && HasDeviceSelection) { CutSelectedDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.V && _clip != null) { PasteDevice(); e.Handled = true; return; }
        if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && HasDeviceSelection) { DeleteSelectedDevice(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    // ---- context menu (header right-click) --------------------------------
    // The device named at the top (spec 034), then what the card's header hands in (preset,
    // compare, move, docs, fold), Save preset, the clipboard group, and Delete last.
    private void ShowDeviceContextMenu(Control anchor, ChainKind kind, int di, Action<MenuFlyout>? header = null)
    {
        SelectDevice(kind, di);
        var flyout = new MenuFlyout();
        string name = kind switch
        {
            ChainKind.Instrument => _engine.DeviceName(_trackId, -1),
            ChainKind.Midi => _engine.MidiEffectName(_trackId, di),
            _ => _engine.DeviceName(_trackId, di),
        };
        string badge = kind switch
        {
            ChainKind.Instrument => "Instrument",
            ChainKind.Midi => $"MIDI effect · {di + 1}",
            _ => $"Audio effect · {di + 1}",
        };
        flyout.Items.Add(MenuKit.Header(name.Length > 0 ? name : "Device", null, badge));
        flyout.Items.Add(new Separator());
        header?.Invoke(flyout);   // ends with its own separator
        bool selectable = SelectableKind(kind);
        MenuKit.AddGroups(flyout.Items,
            new Control[] { MenuKit.Item("Save preset", GlyphKind.Save, () => PresetSaveRequested?.Invoke(di)) },
            new Control[]
            {
                MenuKit.Item("Copy", GlyphKind.Copy, CopySelectedDevice, MenuKit.CopyKey, selectable),
                MenuKit.Item("Cut", GlyphKind.Cut, CutSelectedDevice, MenuKit.CutKey, selectable),
                MenuKit.Item("Paste", GlyphKind.Paste, PasteDevice, MenuKit.PasteKey, _clip != null),
            },
            kind == ChainKind.Effect
                ? new Control[]
                {
                    _engine.TrackDeviceBuiltinKind(_trackId, di) == 5
                        ? MenuKit.Item("Ungroup rack", GlyphKind.Ungroup, UngroupSelectedDevice, MenuKit.UngroupKey, _engine.RackDevChainCount(_trackId, di) == 1)
                        : MenuKit.Item("Group into rack", GlyphKind.Folder, GroupSelectedDevice, MenuKit.GroupKey),
                }
                : Array.Empty<Control>(),
            new Control[] { MenuKit.Danger("Delete", GlyphKind.Trash, DeleteSelectedDevice, MenuKit.DeleteKey, selectable) });
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // ---- drag-reorder (grab a card by its header) ---------------------------
    // Tag stamped on each device card so a drag can find its same-domain siblings in _row.
    private sealed record DevTag(ChainKind Kind, int Index);

    private ChainKind _devDragKind;
    private int _devDragFrom = -1;
    private bool _devDragging;
    private Point _devDragStart;
    private Control? _devDragCard;
    private readonly Border _devDropBar = new()
    {
        Width = 3, Background = AccentBright, CornerRadius = NotaRadius.Clip,
        Margin = new Thickness(-4, 6, 1, 6), IsHitTestVisible = false,
    };

    // The device cards in _row that belong to the given domain, in visual order.
    private System.Collections.Generic.List<Control> DomainCards(ChainKind kind)
        => _row.Children.OfType<Control>().Where(c => c.Tag is DevTag t && t.Kind == kind).ToList();

    private void HookDeviceDrag(Control handle, Control card, ChainKind kind, int di)
    {
        card.Tag = new DevTag(kind, di);
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            _devDragKind = kind; _devDragFrom = di; _devDragging = false; _devDragCard = card;
            _devDragStart = e.GetPosition(_row);
            e.Pointer.Capture(handle); e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (_devDragFrom < 0) return;
            var p = e.GetPosition(_row);
            if (!_devDragging && Math.Abs(p.X - _devDragStart.X) < 5) return;
            _devDragging = true;
            if (_devDragCard != null) _devDragCard.Opacity = 0.55;
            PositionDropBar(GapAt(p.X));
        };
        handle.PointerReleased += (_, e) =>
        {
            if (_devDragFrom < 0) return;
            var kindL = _devDragKind; int from = _devDragFrom; bool dragged = _devDragging;
            int gap = _devDragging ? GapAt(e.GetPosition(_row).X) : -1;
            if (_devDragCard != null) _devDragCard.Opacity = 1.0;
            _devDragFrom = -1; _devDragging = false; _devDragCard = null;
            _row.Children.Remove(_devDropBar);
            e.Pointer.Capture(null);
            if (!dragged || gap < 0) return;
            int m = DomainCards(kindL).Count;
            int to = gap > from ? gap - 1 : gap;
            to = Math.Clamp(to, 0, m - 1);
            if (to == from) return;
            if (kindL == ChainKind.Midi) _engine.MoveMidiEffect(_trackId, from, to);
            else _engine.MoveDevice(_trackId, from, to);
            ExtrasMoved(kindL, from, to);
            _selChainKind = kindL; _selDeviceIndex = to;
            Rebuild(); Changed?.Invoke();
        };
    }

    // Insertion gap (0..m) among the dragged domain's cards for a pointer X in _row space.
    private int GapAt(double x)
    {
        var cards = DomainCards(_devDragKind);
        int gap = 0;
        foreach (var c in cards) { if (x > c.Bounds.X + c.Bounds.Width * 0.5) gap++; else break; }
        return Math.Clamp(gap, 0, cards.Count);
    }

    private void PositionDropBar(int gap)
    {
        _row.Children.Remove(_devDropBar);
        var cards = DomainCards(_devDragKind);
        if (cards.Count == 0) return;
        int childIndex = gap < cards.Count
            ? _row.Children.IndexOf(cards[gap])
            : _row.Children.IndexOf(cards[cards.Count - 1]) + 1;
        childIndex = Math.Clamp(childIndex, 0, _row.Children.Count);
        _row.Children.Insert(childIndex, _devDropBar);
    }
}
