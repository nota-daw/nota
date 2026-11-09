// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a device in its own window (specs/085, #540). A device that opens in XL
// (IDeviceBody.SupportsXl: Nota EQ-8, Dynamic EQ-8, Lens) also has a pop-out key in its card
// header: it opens the device's L body in a resizable DeviceWindow, with the graph taking the
// room. The editor lives in one place at a time, so while the window is open the card in the
// chain is a 260 placeholder (and a device in XL goes back to L first). The window follows its
// device (by its engine id) when this view moves devices or an undo puts them elsewhere, and
// closes when the device is gone or a project is opened.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    // One open window per device. Slot is mutable: the window follows its device's index.
    private sealed class WindowSlot
    {
        public int Track, Index;
        public long Id;   // the device's engine id: survives edits, moves and undo
        public DeviceWindow? Window;
    }

    private readonly List<WindowSlot> _windows = new();

    private WindowSlot? WindowOf(int track, int index) => _windows.FirstOrDefault(w => w.Track == track && w.Index == index);

    /// <summary>True when the track's device at <paramref name="index"/> is open in its own window.</summary>
    private bool InWindow(int index) => WindowOf(_trackId, index) is not null;

    /// <summary>Close every device window (a project opened or a new one started: track ids are reused).</summary>
    public void CloseDeviceWindows()
    {
        foreach (var w in _windows.ToList()) w.Window?.Close();
        _windows.Clear();
    }

    // The pop-out key of a device that has a window. In the placeholder it brings the window forward.
    private Control PopOutKey(int index)
        => IconKey(GlyphKind.PopOut, InWindow(index) ? Loc.T("device.window.show_window") : Loc.T("device.window.open_in_window"),
            () => OpenDeviceWindow(index));

    // The header's extra controls side by side (the size toggle, then the pop-out key).
    private static Control HeaderKeys(Control? first, Control key)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (first != null) sp.Children.Add(first);
        sp.Children.Add(key);
        return sp;
    }

    // Keep each window on its device when this view reorders / removes devices on track t.
    private void RemapWindows(int t, Func<int, int> map)
    {
        foreach (var w in _windows.Where(w => w.Track == t).ToList())
        {
            int ni = map(w.Index);
            if (ni == w.Index) continue;
            if (ni < 0) { w.Window?.Close(); continue; }
            w.Index = ni;
            // The body's closures hold the old index: build it again over the new one.
            if (w.Window is { } win) Dispatcher.UIThread.Post(win.Refill);
        }
    }

    private void OpenDeviceWindow(int index)
    {
        if (_trackId <= 0 || index < 0 || index >= _engine.TrackDeviceCount(_trackId)) return;
        if (WindowOf(_trackId, index) is { Window: { } open }) { open.Activate(); return; }
        int kind = _engine.TrackDeviceBuiltinKind(_trackId, index);
        if (kind < 0) return;
        var strategy = _cardFactory.Resolve(kind);
        // The editor lives in one place: a device in XL leaves the panel for the window.
        if (DeviceView.Size(_engine, _trackId, index) == CardSize.XL) DeviceView.SetSize(_engine, _trackId, index, CardSize.L);

        var slot = new WindowSlot { Track = _trackId, Index = index, Id = _engine.TrackDeviceId(_trackId, index) };
        DeviceWindow win = null!;
        int checkTick = 0;
        win = new DeviceWindow(_engine.DeviceName(_trackId, index), TrackNames.Of(_engine, _trackId), strategy.Subtitle ?? "", tickReg =>
        {
            int t = slot.Track, di = slot.Index;
            // Another device in the slot (an undo, an edit from elsewhere): follow the window's own
            // device to where it went, or close when it is gone. By id, not by kind and name: two
            // EQ-8s look the same.
            tickReg(() =>
            {
                if (++checkTick % 15 != 0 || slot.Index != di) return;   // moved: a refill is on its way
                if (_engine.TrackDeviceId(t, di) == slot.Id && slot.Id != 0) return;
                int n = _engine.TrackDeviceCount(t), ni = -1;
                for (int i = 0; i < n && slot.Id != 0; i++)
                    if (_engine.TrackDeviceId(t, i) == slot.Id) { ni = i; break; }
                if (ni < 0) { win.Close(); return; }
                slot.Index = ni;
                Dispatcher.UIThread.Post(win.Refill);   // the body's closures hold the old index
                if (_trackId == t) Rebuild();             // the placeholder moves with it
            });
            var ctx = new DeviceCardContext(_engine, t, tickReg, () => Changed?.Invoke(), (_, _, _, _) => { }, _ => { },
                i => PresetSaveRequested?.Invoke(i), () => 0, _ => { }, tickReg, () => { },
                () => Dispatcher.UIThread.Post(win.Refill), _ => { }, () => { }, _factory, s => StatusMessage?.Invoke(s), popout: true);
            var body = strategy.Build(ctx, di);
            var page = strategy.FullBleed ? body : new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body };
            return new DeviceWindowPage(page, double.NaN, double.NaN, WindowPresets(t, di, kind, () => win.Refill()));
        }, new DeviceWindowBypass(
            () => slot.Index < _engine.TrackDeviceCount(slot.Track) && _engine.DeviceBypassed(slot.Track, slot.Index),
            b => { _engine.SetDeviceBypassed(slot.Track, slot.Index, b); if (_trackId == slot.Track) Rebuild(); Changed?.Invoke(); }),
            resizable: true) { ContextTrackId = _trackId };
        slot.Window = win;
        _windows.Add(slot);
        win.Closed += (_, _) =>
        {
            _windows.Remove(slot);
            if (_trackId == slot.Track) Rebuild();   // the card gets its editor back
        };
        // After the click has finished (the key that opened it is rebuilt away).
        var anchor = (Control)this;
        Dispatcher.UIThread.Post(() => { win.ShowFrom(anchor); win.Activate(); }, DispatcherPriority.Background);
        Rebuild();
    }

    // The device's factory presets for the window's title bar; picking one is the same undo
    // step and tag as the card's picker, and the card follows.
    private DeviceWindowPresets? WindowPresets(int t, int di, int kind, Action refill)
    {
        var list = _factory.All().Where(p => p.BuiltinKind == kind && !p.IsInstrument && !p.IsMidiEffect).Select(p => (p.Id, p.DisplayName)).ToList();
        if (list.Count == 0) return null;
        int tag = _engine.DevicePresetTag(t, PresetSurface.Effect, di);
        string curId = tag >= 0 ? _engine.PresetTagInfo(tag)?.Id ?? "" : "";
        int cur = list.FindIndex(p => p.Id == curId);
        return new DeviceWindowPresets(list.Select(p => p.DisplayName).ToList(), cur, i =>
        {
            var (id, name) = list[i];
            bool init = name == "Init";
            string warn = Nota.Application.Editing.PresetEdit.Apply(_engine, t, PresetSurface.Effect, di, init ? "" : id, init ? "" : name,
                () => _factory.ApplyInPlace(_engine, id, t, di));
            if (warn.Length > 0) StatusMessage?.Invoke(warn);
            if (TrackExtras(t).TryGetValue(ExtraKey(ChainKind.Effect, di), out var x)) { x.PresetId = warn.Length == 0 ? id : x.PresetId; x.Preset = warn.Length == 0 ? name : x.Preset; }
            refill();
            if (_trackId == t) Rebuild();
            Changed?.Invoke();
        });
    }

    // The device's card in the chain while its window is open: 260 × 260, the header with the
    // pop-out key (brings the window forward) and power, and a body that says where it went.
    private Control WindowPlaceholder(int index, int count, int kind, string name, bool bypassed)
    {
        var strategy = _cardFactory.Resolve(kind);
        var caption = new TextBlock
        {
            Text = Loc.T("device.window.open_in_its_own_window"), FontSize = NotaType.Caption, Foreground = TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
        };
        var body = new Border { Background = Brushes.Transparent, Child = Section(caption), Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(body, Loc.T("device.window.the_editor_is_open_in"));
        body.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(body).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            SelectDevice(ChainKind.Effect, index, e.KeyModifiers);
            OpenDeviceWindow(index);   // brings it forward
        };
        var spec = new ShellSpec(
            Name: name, Subtitle: strategy.Subtitle ?? "BUILT-IN", DeviceIndex: index, Count: count, Bypassed: bypassed, Bypassable: true,
            CanMove: true, CanDelete: true, PresetKind: kind, IsInstrument: false, Width: CardLayout.MiniWidth, Kind: ChainKind.Effect,
            HeaderExtra: PopOutKey(index), Compact: true, Placeholder: true);
        return BuildCardShell(spec, new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body });
    }
}
