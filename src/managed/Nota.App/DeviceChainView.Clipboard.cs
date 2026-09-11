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
    private sealed class DeviceClip
    {
        public ChainKind Kind;
        public int BuiltinKind;      // effect: >=0 built-in, <0 hosted plugin
        public string PluginId = ""; // hosted-plugin identity (recreated via the catalog)
        public byte[]? State;        // built-in device state / plugin state blob
        public int MidiKind;         // MIDI effect kind
        public float[]? MidiParams;  // MIDI effect param snapshot
        public bool Bypassed;
        public string Preset = "", PresetId = "";   // the source card's preset label follows the paste
    }
    private DeviceClip? _clip;

    private void CopySelectedDevice()
    {
        if (!HasDeviceSelection) return;
        int di = _selDeviceIndex;
        if (_selChainKind == ChainKind.Effect)
        {
            int kind = _engine.TrackDeviceBuiltinKind(_trackId, di);
            var c = new DeviceClip { Kind = ChainKind.Effect, BuiltinKind = kind, Bypassed = _engine.DeviceBypassed(_trackId, di) };
            if (kind >= 0) c.State = _engine.DeviceGetState(_trackId, di);
            else { c.PluginId = _engine.TrackDevicePluginId(_trackId, di); c.State = _engine.GetPluginState(_trackId, di); }
            _clip = c;
        }
        else if (_selChainKind == ChainKind.Midi)
        {
            int n = _engine.MidiEffectParamCount(_trackId, di);
            var p = new float[n];
            for (int i = 0; i < n; i++) p[i] = _engine.MidiEffectGetParam(_trackId, di, i);
            _clip = new DeviceClip { Kind = ChainKind.Midi, MidiKind = _engine.MidiEffectKind(_trackId, di), MidiParams = p, Bypassed = _engine.MidiEffectBypassed(_trackId, di) };
        }
        if (_clip != null) { var ex = Extra(_selChainKind, di); _clip.Preset = ex.Preset; _clip.PresetId = ex.PresetId; }
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
        int newIdx = -1;
        if (c.Kind == ChainKind.Effect)
        {
            if (c.BuiltinKind >= 0)
            {
                newIdx = _engine.AddBuiltinDevice(_trackId, c.BuiltinKind);
                if (newIdx >= 0 && c.State != null) _engine.DeviceSetState(_trackId, newIdx, c.State);
            }
            else if (_catalog != null && c.PluginId.Length > 0)
            {
                int cat = _catalog.IndexOfId(c.PluginId);
                if (cat >= 0)
                {
                    newIdx = _engine.AddTrackEffectPlugin(_trackId, cat);
                    if (newIdx >= 0 && c.State != null) _engine.SetPluginState(_trackId, newIdx, c.State);
                }
            }
            if (newIdx < 0) return;
            if (c.Bypassed) _engine.SetDeviceBypassed(_trackId, newIdx, true);
            TrackExtras().Remove(ExtraKey(ChainKind.Effect, newIdx));   // a fresh slot: drop any stale entry
            // Drop it right after the selected effect, else leave it appended.
            if (_selChainKind == ChainKind.Effect && _selDeviceIndex >= 0 && _selDeviceIndex < newIdx)
            { int to = _selDeviceIndex + 1; _engine.MoveDevice(_trackId, newIdx, to); ExtrasMoved(ChainKind.Effect, newIdx, to); newIdx = to; }
            _selChainKind = ChainKind.Effect;
        }
        else if (c.Kind == ChainKind.Midi)
        {
            newIdx = _engine.AddMidiEffect(_trackId, c.MidiKind);
            if (newIdx < 0) return;
            if (c.MidiParams != null)
            {
                int n = Math.Min(c.MidiParams.Length, _engine.MidiEffectParamCount(_trackId, newIdx));
                for (int i = 0; i < n; i++) _engine.MidiEffectSetParam(_trackId, newIdx, i, c.MidiParams[i]);
            }
            if (c.Bypassed) _engine.SetMidiEffectBypassed(_trackId, newIdx, true);
            TrackExtras().Remove(ExtraKey(ChainKind.Midi, newIdx));
            if (_selChainKind == ChainKind.Midi && _selDeviceIndex >= 0 && _selDeviceIndex < newIdx)
            { int to = _selDeviceIndex + 1; _engine.MoveMidiEffect(_trackId, newIdx, to); ExtrasMoved(ChainKind.Midi, newIdx, to); newIdx = to; }
            _selChainKind = ChainKind.Midi;
        }
        else return;
        var pasted = Extra(c.Kind, newIdx); pasted.Preset = c.Preset; pasted.PresetId = c.PresetId;
        _selDeviceIndex = newIdx;
        Rebuild(); Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool mod = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        if (mod && e.Key == Key.C && HasDeviceSelection) { CopySelectedDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.X && HasDeviceSelection) { CutSelectedDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.V && _clip != null) { PasteDevice(); e.Handled = true; return; }
        if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && HasDeviceSelection) { DeleteSelectedDevice(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    // ---- context menu (header right-click) --------------------------------
    // Adds Copy / Cut / Paste / Delete above the existing "Save preset" item.
    private void ShowDeviceContextMenu(Control anchor, ChainKind kind, int di)
    {
        SelectDevice(kind, di);
        var flyout = new MenuFlyout();
        void Add(string header, bool enabled, Action act)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            flyout.Items.Add(mi);
        }
        bool selectable = SelectableKind(kind);
        Add("Copy", selectable, CopySelectedDevice);
        Add("Cut", selectable, CutSelectedDevice);
        Add("Paste", _clip != null, PasteDevice);
        Add("Delete", selectable, DeleteSelectedDevice);
        flyout.Items.Add(new Separator());
        Add("Save preset", true, () => PresetSaveRequested?.Invoke(di));
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // ---- drag-reorder (grab a card's ⠿ handle) ----------------------------
    // Tag stamped on each device card so a drag can find its same-domain siblings in _row.
    private sealed record DevTag(ChainKind Kind, int Index);

    private ChainKind _devDragKind;
    private int _devDragFrom = -1;
    private bool _devDragging;
    private Point _devDragStart;
    private Control? _devDragCard;
    private readonly Border _devDropBar = new()
    {
        Width = 3, Background = AccentBright, CornerRadius = new CornerRadius(2),
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
