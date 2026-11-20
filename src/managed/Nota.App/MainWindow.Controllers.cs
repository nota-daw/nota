// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hardware controllers in the main window (specs/051): the indicator beside the MIDI button —
// which controller is active and what its knobs do (Device / Volume / Pan / Send A, B, …), a
// click to switch the knob mode from Nota (FR-016a) — and the overlay that marks the focused
// device's page on its card and the controls waiting to pick up (FR-017, FR-020).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Nota.App.Controllers;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private Button? _controllerBtn;
    private TextBlock? _controllerLabel;
    private ControllerFocusOverlay? _controllerOverlay;
    private DevicePagesStore? _devicePages;
    private ControllerContextMenu? _controllerMenu;

    // The knob numbers and pickup hints over the Devices panel and the Mixer: one per window
    // they can sit in — the main window, the floating Mixer, the popped-out Devices.
    private ControllerFocusOverlay? NewControllerOverlay()
    {
        if (_controllers is null) return null;
        var applier = App.Services.GetRequiredService<ControlApplier>();
        return new ControllerFocusOverlay
        {
            Roots = () => new Visual?[] { _deviceChain, _mixer },
            ValueOf = t => applier.CurrentNormalized(t),
            Service = _controllers,
        };
    }

    private void InitControllerUi()
    {
        if (_controllers is null) return;
        // Edited device pages (FR-018): the user's global file now, the project's copy on open.
        _devicePages = App.Services.GetRequiredService<DevicePagesStore>();
        _devicePages.LoadGlobal();
        _controllers.EditedPages = _devicePages;
        var overrides = App.Services.GetRequiredService<ControllerOverrides>();
        overrides.LoadGlobal();
        _controllers.Overrides = overrides;
        _controllerMenu = new ControllerContextMenu(_controllers, _devicePages, App.Services.GetRequiredService<Nota.Application.IAudioEngine>());
        _controllerMenu.Attach(this);   // the floating Mixer and the popped-out Devices attach on open
        _controllers.RunCommand = id => _commands.Run(id, onlyIfAvailable: true);
        _controllers.SessionShown = () => _session is { IsEffectivelyVisible: true };
        // Track ◀ / ▶ select the track (specs/107): in the Session its slot in the same row, so the
        // Clip editor follows as on a click; elsewhere the track as a header click, scrolled to.
        _controllers.SelectTrack = id =>
        {
            if (_session is { IsEffectivelyVisible: true } s) s.SelectSlot(id, Math.Max(0, s.CurrentSelection.Scene));
            if (Timeline.SelectedTrackId != id) Timeline.SelectTrackFromOutside(id);
            Timeline.RevealTrack(id);
        };
        _controllers.GroupCollapsed = id => Timeline.IsGroupCollapsed(id);
        _controllers.VisibleTrackOrder = () => _session is { IsEffectivelyVisible: true } s ? s.ColumnTrackIds() : null;
        _controllers.StepDevice = StepControllerDevice;
        _controllers.SessionSelection = () =>
        {
            var sel = ((Nota.Remote.IRemoteHost)this).SessionSelection;
            return sel.Slot ? ("slot", sel.TrackId, sel.Scene) : sel.Row ? ("scene", 0, sel.Scene) : ("none", 0, 0);
        };
        _devicePages.Changed += () => _controllers.RefreshFocus(force: true);
        if (_deviceChain is not null)
            _deviceChain.ControllerPagesRequested += device =>
                _ = new DevicePageEditor(App.Services.GetRequiredService<Nota.Application.IAudioEngine>(), _devicePages, device, _devicePages).ShowDialog(this);
        var applier = App.Services.GetRequiredService<ControlApplier>();

        _controllerOverlay = NewControllerOverlay();
        if (LearnOverlay.Parent is Panel host) host.Children.Insert(host.Children.IndexOf(LearnOverlay), _controllerOverlay);

        _controllerLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        _controllerBtn = new Button
        {
            Classes = { "chip" }, IsVisible = false, Content = _controllerLabel,
            [ToolTip.TipProperty] = Loc.T("controllers.indicator_tip"),
        };
        _controllerBtn.Click += (_, _) => ShowKnobModeMenu();
        if (RemotePanel.Children.IndexOf(MidiLearnBtn) is int at and >= 0) RemotePanel.Children.Insert(at + 1, _controllerBtn);

        _controllers.ControllersChanged += SyncControllerIndicator;
        _controllers.FocusChanged += SyncControllerIndicator;
        _controllers.FocusSummaryChanged += SyncControllerIndicator;
        SyncControllerIndicator();
    }

    private void SyncControllerIndicator()
    {
        if (_controllers is null || _controllerBtn is null || _controllerLabel is null) return;
        var active = _controllers.Controllers.Where(c => c.Stage == ControllerStage.Active).ToList();
        _controllerBtn.IsVisible = active.Count > 0;
        if (active.Count == 0) return;
        var lead = active.FirstOrDefault(c => c.HasKnobs) ?? active[0];
        string name = active.Count == 1 ? lead.Profile?.Name ?? lead.Name : Loc.Plural("controllers.count", active.Count);
        // specs/107: what the knobs are on — track · device · page, or the mode and its bank.
        string text = _controllers.CurrentSummary is { } sum ? Readout(sum) : name;
        _controllerLabel.Text = text;
        ToolTip.SetTip(_controllerBtn, text + "\n" + Loc.T("controllers.indicator_tip"));
    }

    /// <summary>The readout of a controller's focus as the title bar (and Remote) shows it.</summary>
    internal static string Readout(ControllerFocusSummary s)
    {
        if (s.Mode.Kind != KnobModeKind.Device)
            return Loc.F("controllers.readout_bank", s.Controller, ModeLabel(s.Mode), s.BankFirst, s.BankLast);
        if (s.TrackId <= 0) return Loc.F("controllers.readout_no_track", s.Controller);
        if (s.Device is null) return Loc.F("controllers.readout_no_device", s.Controller, s.TrackName);
        string page = s.PageCount > 1 ? " · " + Loc.F("controllers.page", s.Page + 1, s.PageCount) : "";
        return Loc.F("controllers.readout_device", s.Controller, s.TrackName, s.Device) + page;
    }

    private static string ModeLabel(KnobMode m) => ControllerService.ModeLabel(m);

    // Next / Previous device (specs/107 FR-010): step the focus along the track's chain by selecting
    // the device in the Devices panel's content — whether or not the panel is on screen, it isn't
    // opened (FR-005) — so the knobs, the card mark and the readout all follow as on a click.
    internal bool StepControllerDevice(int delta)
    {
        var engine = Engine;
        int track = _controllers?.Focused?.Address.TrackId ?? Timeline.SelectedTrackId;
        if (track <= 0 || _deviceChain is null) return false;
        var current = _controllers?.Focused?.Address ?? FocusedDevice.DefaultFor(engine, track);
        if (ChainOrder.Step(ChainOrder.Of(engine, track), current, delta) is not { } to) return false;
        if (current is not null && ChainOrder.TopLevel(current) == to && current.Rack is null) return false;
        if (_deviceChain.TrackId != track) ShowDevices(track, takeOverPanel: false);
        return _deviceChain.SelectAddress(to);
    }

    // "Show device" (specs/107 FR-003): the Devices panel on the focused track with the focused
    // device selected. A device inside a rack shows its rack.
    private void ShowControllerDevice(DeviceAddress a)
    {
        if (a.TrackId <= 0) return;
        ShowDevices(a.TrackId);
        // A device inside a rack: showing the panel drops the rack's inner selection, so select and
        // reveal the rack itself — the knobs then follow the rack card the user can see.
        _deviceChain?.SelectAddress(a.Rack is null ? a : ChainOrder.TopLevel(a));
    }

    private void ShowKnobModeMenu()
    {
        if (_controllers is null || _controllerBtn is null) return;
        var current = _controllers.Controllers.FirstOrDefault(c => c.Stage == ControllerStage.Active && c.HasKnobs)?.State.KnobMode;
        var modes = new List<KnobMode> { KnobMode.Device, new(KnobModeKind.Volume), new(KnobModeKind.Pan) };
        for (int k = 0; k < vmReturnCount(); k++) modes.Add(new KnobMode(KnobModeKind.Send, k));
        var menu = new ContextMenu();
        // specs/107 FR-003: the explicit way to see the focused device (a controller never takes
        // the lower panel by itself).
        if (_controllers.Focused is { } f)
        {
            var show = new MenuItem { Header = Loc.T("controllers.show_device") };
            show.Click += (_, _) => ShowControllerDevice(f.Address);
            menu.Items.Add(show);
            menu.Items.Add(new Separator());
        }
        foreach (var m in modes)
        {
            var item = new MenuItem { Header = ModeLabel(m), ToggleType = MenuItemToggleType.Radio, IsChecked = current == m };
            item.Click += (_, _) => _controllers.SetKnobMode(m);
            menu.Items.Add(item);
        }
        menu.Open(_controllerBtn);

        int vmReturnCount() => _vm?.Engine.ReturnTrackCount ?? 0;
    }
}
