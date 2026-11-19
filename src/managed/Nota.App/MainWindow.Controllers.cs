// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hardware controllers in the main window (specs/051): the indicator beside the MIDI button —
// which controller is active and what its knobs do (Device / Volume / Pan / Send A, B, …), a
// click to switch the knob mode from Nota (FR-016a) — and the overlay that marks the focused
// device's page on its card and the controls waiting to pick up (FR-017, FR-020).

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
        _devicePages.Changed += () => _controllers.RefreshFocus(force: true);
        if (_deviceChain is not null)
            _deviceChain.ControllerPagesRequested += device =>
                _ = new DevicePageEditor(App.Services.GetRequiredService<Nota.Application.IAudioEngine>(), _devicePages, device, _devicePages).ShowDialog(this);
        var applier = App.Services.GetRequiredService<ControlApplier>();

        _controllerOverlay = NewControllerOverlay();
        if (LearnOverlay.Parent is Panel host) host.Children.Insert(host.Children.IndexOf(LearnOverlay), _controllerOverlay);

        _controllerLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _controllerBtn = new Button
        {
            Classes = { "chip" }, IsVisible = false, Content = _controllerLabel,
            [ToolTip.TipProperty] = Loc.T("controllers.indicator_tip"),
        };
        _controllerBtn.Click += (_, _) => ShowKnobModeMenu();
        if (RemotePanel.Children.IndexOf(MidiLearnBtn) is int at and >= 0) RemotePanel.Children.Insert(at + 1, _controllerBtn);

        _controllers.ControllersChanged += SyncControllerIndicator;
        _controllers.FocusChanged += SyncControllerIndicator;
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
        _controllerLabel.Text = lead.HasKnobs ? Loc.F("controllers.indicator", name, ModeLabel(lead.State.KnobMode)) : name;
    }

    private static string ModeLabel(KnobMode m) => ControllerService.ModeLabel(m);

    private void ShowKnobModeMenu()
    {
        if (_controllers is null || _controllerBtn is null) return;
        var current = _controllers.Controllers.FirstOrDefault(c => c.Stage == ControllerStage.Active && c.HasKnobs)?.State.KnobMode;
        var modes = new List<KnobMode> { KnobMode.Device, new(KnobModeKind.Volume), new(KnobModeKind.Pan) };
        for (int k = 0; k < vmReturnCount(); k++) modes.Add(new KnobMode(KnobModeKind.Send, k));
        var menu = new ContextMenu();
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
