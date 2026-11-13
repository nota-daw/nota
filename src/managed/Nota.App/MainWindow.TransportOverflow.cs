// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport bar's ⋯ key (specs/097, #520). A narrow bar sheds parts (TransportFit); this
// menu lists every part it shed, in bar order, each doing what the part does — so nothing on
// the bar is ever out of reach — and then a few quick settings that would otherwise be a trip
// to Settings (the interface scale). It is an ordinary Nota menu, built afresh on each open
// from the current fit and state: Escape, a click outside or a pick closes it, and a click
// outside does nothing else. Toggles run the same commands as their keys and menu twins.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    // The signatures the menu offers; the bar's own fields still reach any other.
    private static readonly (int Num, int Den)[] CommonTimeSignatures =
        { (2, 4), (3, 4), (4, 4), (5, 4), (6, 8), (7, 8), (9, 8), (12, 8) };

    private SnapChip? _snapChip;
    private MenuFlyout? _overflowMenu;
    private DateTime _overflowClosedAt;

    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        // The press that closed the open menu (light dismiss) is also this click: leave it closed.
        if ((DateTime.UtcNow - _overflowClosedAt).TotalMilliseconds < 300) return;
        OpenOverflowMenu(fromKeyboard: false);
    }

    /// <summary>Space / Return on ⋯ while it has keyboard focus (Tab) opens the menu; the
    /// window's transport keys would take them first (Space = Play everywhere), so they ask
    /// here. A ⋯ merely clicked keeps Space as Play.</summary>
    internal bool TakesOverflowKey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Space or Key.Enter)) return false;
        if (!ReferenceEquals(e.Source, OverflowBtn) || !OverflowBtn.Classes.Contains(":focus-visible")) return false;
        OpenOverflowMenu(fromKeyboard: true);
        return true;
    }

    private void OpenOverflowMenu(bool fromKeyboard)
    {
        if (_vm is null) return;
        if (_overflowMenu is { IsOpen: true } open) { open.Hide(); return; }

        var focusBefore = FocusManager?.GetFocusedElement();
        var menu = _overflowMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        MenuKit.AddGroups(menu.Items, ShedEntries(_vm), QuickEntries());
        menu.Opened += (_, _) =>
        {
            if (!fromKeyboard) return;
            // From the keyboard the first entry is highlighted, so the arrows, Enter and Escape act at once.
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var entry in menu.Items)
                    if (entry is MenuItem { IsVisible: true, IsEnabled: true } first) { first.Focus(NavigationMethod.Directional); break; }
            }, DispatcherPriority.Input);
        };
        menu.Closed += (_, _) =>
        {
            _overflowClosedAt = DateTime.UtcNow;
            if (ReferenceEquals(_overflowMenu, menu)) _overflowMenu = null;
            // Back to where the keyboard was — unless what was picked moved it (a dialog, a picker).
            Dispatcher.UIThread.Post(() =>
            {
                var now = FocusManager?.GetFocusedElement();
                if (focusBefore is not null && (now is null || ReferenceEquals(now, OverflowBtn) || now is MenuItem))
                    focusBefore.Focus();
            }, DispatcherPriority.Input);
        };
        menu.ShowAt(OverflowBtn);
    }

    // One entry per shed part, in bar order (ToolbarOverflow.Entries).
    private IEnumerable<Control> ShedEntries(MainWindowViewModel vm)
    {
        foreach (var part in ToolbarOverflow.Entries(_shedSteps))
        {
            switch (part)
            {
                case ToolbarPart.Loop:
                {
                    // The bar's Loop key: a plain on / off (⌘L also sets the loop to a selection).
                    var mi = Toggle(Loc.T("common.loop"), vm.Transport.LoopOn, () => vm.Transport.LoopOn = !vm.Transport.LoopOn, MenuKit.LoopKey);
                    MenuKit.SetTrailing(mi, vm.Transport.LoopRangeText);
                    yield return mi;
                    break;
                }
                case ToolbarPart.LoopRecord:
                    yield return Check(Loc.T("main.transport_overflow.loop_record_replace"), vm.Transport.LoopRecordReplace,
                                       "transport.loopRecordMode", MenuKit.Bound("transport.loopRecordMode"));
                    break;
                case ToolbarPart.Tap:
                    yield return TapEntry(vm);
                    break;
                case ToolbarPart.TimeSignature:
                    yield return TimeSignatureEntry(vm);
                    break;
                case ToolbarPart.Key:
                {
                    var mi = MenuKit.Item(Loc.T("main.transport_overflow.key"), click: () =>
                        // After the menu is gone, so the picker opens on its own under ⋯.
                        Dispatcher.UIThread.Post(() => PickProjectKey(OverflowBtn)));
                    MenuKit.SetTrailing(mi, KeyLabel.Text);
                    yield return mi;
                    break;
                }
                case ToolbarPart.Grid when _snapChip is not null:
                {
                    var mi = MenuKit.ValueItem(Loc.T("common.grid"), null, SnapLabel.Text ?? "");
                    foreach (var item in _snapChip.BuildItems()) mi.Items.Add(item);
                    yield return mi;
                    break;
                }
                case ToolbarPart.Follow:
                    // As the bar's key: a paused Follow is picked up again rather than turned off.
                    yield return Toggle(Loc.T("main.follow_playhead"), FollowBtn.IsChecked == true,
                                        () => SetFollowPlayhead(Timeline.FollowSuspended || !Timeline.FollowPlayhead), MenuKit.Bound("view.follow"));
                    break;
                case ToolbarPart.Snap:
                {
                    bool on = EditorGrids.Shared.Get(EditorKind.Arrangement).SnapOn;
                    var mi = new MenuItem
                    {
                        Header = Loc.T("snap_chip.snap_to_grid"), ToggleType = MenuItemToggleType.CheckBox,
                        IsChecked = on, InputGesture = MenuKit.Bound("grid.snap"),
                    };
                    mi.Click += (_, _) => SetSnapEnabled(!on);
                    yield return mi;
                    break;
                }
                case ToolbarPart.Automation:
                    yield return Check(Loc.T("main.transport_overflow.automation"), AutomationToggle.IsChecked == true,
                                       "view.automation", MenuKit.Bound("view.automation"));
                    if (ReenableAutoBtn.IsVisible)
                        yield return MenuKit.Item(Loc.T("main.transport_overflow.reenable_automation"),
                                                  click: () => OnReenableAutomation(this, new RoutedEventArgs()));
                    break;
                case ToolbarPart.Mixer:
                    yield return Check(Loc.T("common.mixer"), MixerToggle.IsChecked == true, "view.mixer", MenuKit.Bound("view.mixer"));
                    break;
                case ToolbarPart.MidiLearn:
                    yield return Check(Loc.T("main.transport_overflow.midi_learn"), MidiLearnBtn.IsChecked == true,
                                       "view.midiLearn", MenuKit.Bound("view.midiLearn"));
                    break;
                case ToolbarPart.Remote:
                {
                    var mi = MenuKit.Item(Loc.T("main.connect_phone"), click: ShowConnectPhone);
                    MenuKit.SetTrailing(mi, RemoteCount.Text);
                    yield return mi;
                    break;
                }
                case ToolbarPart.DspMeter:
                {
                    // Read-only, and live while the menu is open: it mirrors the bar's read-out.
                    var mi = new MenuItem { IsEnabled = false };
                    mi[!MenuItem.HeaderProperty] = CpuText[!TextBlock.TextProperty];
                    yield return mi;
                    break;
                }
                case ToolbarPart.Master:
                    yield return MasterEntry(vm);
                    break;
            }
        }
    }

    // The quick settings, shown at every width.
    private static IEnumerable<Control> QuickEntries()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        int current = InterfaceScale.Normalize(settings.Current.InterfaceScale);
        var scale = MenuKit.ValueItem(Loc.T("preferences.interface_scale"), null, $"{current} %");
        foreach (var percent in InterfaceScale.Presets)
        {
            int p = percent;
            var mi = new MenuItem { Header = $"{p} %", ToggleType = MenuItemToggleType.Radio, IsChecked = current == p };
            mi.Click += (_, _) =>
            {
                if (settings.Current.InterfaceScale == p) return;
                settings.Current.InterfaceScale = p;   // the same setting as Settings → Appearance
                settings.Save();
                NotaUiScale.Set(p);
            };
            scale.Items.Add(mi);
        }
        yield return scale;
    }

    // A toggle that runs the part's command, so the bar, the app menu and its key stay one path.
    private MenuItem Check(string header, bool on, string command, KeyGesture? gesture)
        => Toggle(header, on, () => _commands.Run(command), gesture);

    private static MenuItem Toggle(string header, bool on, Action flip, KeyGesture? gesture)
    {
        var mi = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on, InputGesture = gesture };
        mi.Click += (_, _) => flip();
        return mi;
    }

    // Tap stays open: a tempo takes several taps. The tapped tempo shows beside it.
    private MenuItem TapEntry(MainWindowViewModel vm)
    {
        var mi = new MenuItem { Header = Loc.T("main.transport_overflow.tap_tempo"), StaysOpenOnClick = true };
        void Show() => MenuKit.SetTrailing(mi, Loc.F("browser.bpm", NotaNum.Bpm((double)vm.Transport.Bpm)));
        Show();
        mi.Click += (_, _) => { TapTempoBeat(); Show(); };
        return mi;
    }

    private static MenuItem TimeSignatureEntry(MainWindowViewModel vm)
    {
        int num = vm.Transport.TimeSigNumerator, den = vm.Transport.TimeSigDenominator;
        var mi = MenuKit.ValueItem(Loc.T("main.transport_overflow.time_signature"), null, $"{num}/{den}");
        var list = new List<(int Num, int Den)>(CommonTimeSignatures);
        if (!list.Contains((num, den))) list.Insert(0, (num, den));   // an uncommon one, still marked
        foreach (var (n, d) in list)
        {
            var item = new MenuItem { Header = $"{n}/{d}", ToggleType = MenuItemToggleType.Radio, IsChecked = n == num && d == den };
            item.Click += (_, _) =>
            {
                vm.Transport.TimeSigNumerator = n;
                vm.Transport.TimeSigDenominator = d;
            };
            mi.Items.Add(item);
        }
        return mi;
    }

    // The master fader in a row of its own; it stays open so a drag doesn't close the menu.
    private static MenuItem MasterEntry(MainWindowViewModel vm)
    {
        var fader = CreateMasterFader(vm);
        // Follow the level while shown (a phone, a controller, undo), except mid-drag.
        void Sync(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(vm.Transport.MasterVolume) && !fader.Dragging) fader.Value = vm.Transport.MasterVolume;
        }
        fader.AttachedToVisualTree += (_, _) => { fader.Value = vm.Transport.MasterVolume; vm.Transport.PropertyChanged += Sync; };
        fader.DetachedFromVisualTree += (_, _) => vm.Transport.PropertyChanged -= Sync;
        fader.Width = 120;
        fader.Height = 16;
        fader.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock { Text = Loc.T("common.master"), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(fader);
        return new MenuItem { Header = row, StaysOpenOnClick = true };
    }
}
