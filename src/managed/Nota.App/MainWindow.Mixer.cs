// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Where the Mixer lives (specs/054): docked as its own bottom panel in the main window (under
// the main view, above the Detail panel), or in its own window for a second screen. The
// single MixerView instance is moved between MixerHost and the window (reparented, not
// rebuilt), so meters, scroll and the Stereo Map carry over. View → Mixer / ⌘⇧M shows or hides
// it in the remembered placement; the panel's pop-out button, the window's dock button and
// View → Mixer in its own window switch placement. Shown / placement / docked height are
// per-user settings, restored on launch. Groups, selection and compact strips: specs/070.

using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    private MixerWindow? _mixerWindow;
    // Closing the window to move the Mixer into the panel — not the user hiding it.
    private bool _mixerSwitching;
    // The app is quitting: windows closing now must not record the Mixer as hidden.
    private bool _quitRequested;

    // The docked panel's toolbar (32) + its 1px seam margin, around the MixerView.
    private const double MixerPanelChrome = 33;

    private static ISettingsService MixerSettings => App.Services.GetRequiredService<ISettingsService>();
    private static bool MixerFloating => MixerSettings.Current.MixerFloating;
    internal bool IsMixerShown => _mixerDocked || _mixerWindow is not null;
    // On screen: a docked Mixer folds away while the clip editor fills the window (#516).
    private bool IsMixerVisible => _mixerWindow is not null || (_mixerDocked && !_detailMaximized);

    private double MixerPanelMin => MixerPanelChrome + (_mixer?.MinContentHeight ?? 0);
    private double MixerPanelNatural => MixerPanelChrome + (_mixer?.NaturalContentHeight ?? 0);

    // specs/070: one fold state for the Arrangement and the Mixer, the Mixer follows the selected
    // track (wired here, beside the rest of the Mixer), and its compact layout is a user setting.
    private void WireMixerGroups()
    {
        if (_mixer is null) return;
        var fold = new GroupFold();
        Timeline.Fold = fold;
        _mixer.Fold = fold;
        _mixer.Compact = MixerSettings.Current.MixerCompact;
        _mixer.CompactChanged += compact =>
        {
            var settings = MixerSettings;
            settings.Current.MixerCompact = compact;
            settings.Save();
        };
        Timeline.TrackSelected += id => _mixer?.FollowSelection(id);
        Timeline.SelectionCleared += () => _mixer?.FollowSelection(-1);
        if (_modular is not null) _modular.TrackActivated += id => _mixer?.FollowSelection(id);
    }

    private void OnMenuMixer(object? sender, EventArgs e) => ToggleMixer();

    // The toolbar's Mixer button (#516) runs view.mixer like View → Mixer and ⌘⇧M. A click
    // flips the button by itself; put it back to what is true, since the Mixer may not open.
    private void OnToggleMixerButton(object? sender, RoutedEventArgs e)
    {
        _commands.Run("view.mixer");
        SyncMixerToggle();
    }

    /// <summary>The toolbar's Mixer button reads as on while the Mixer is on screen, docked or in
    /// its own window — however it was opened or closed (button, menu, ⌘⇧M, the panel's or
    /// the window's close button, a switch of placement, launch): docking, undocking, the
    /// window opening and closing, and the clip editor filling the window all end here.</summary>
    private void SyncMixerToggle() => MixerToggle.IsChecked = IsMixerVisible;

    /// <summary>Show the Mixer in its remembered placement, or hide it (⌘⇧M / View → Mixer).
    /// A docked Mixer hidden by the clip editor filling the window comes back instead.</summary>
    internal void ToggleMixer()
    {
        if (_mixerDocked && _detailMaximized) SetDetailMaximized(false);
        else if (IsMixerShown) HideMixer();
        else ShowMixer();
    }

    internal void ShowMixer()
    {
        if (_mixer is null || IsMixerShown) return;
        if (MixerFloating) OpenMixerWindow();
        else DockMixer();
        RememberMixerShown(true);
    }

    internal void HideMixer()
    {
        if (_mixerWindow is not null) { _mixerWindow.Close(); return; }   // its close records it
        if (!_mixerDocked) return;
        UndockMixer();
        RememberMixerShown(false);
    }

    /// <summary>Docked ↔ own window. Moves a shown Mixer; a hidden one only changes where it
    /// opens next.</summary>
    internal void SetMixerFloating(bool floating)
    {
        var settings = MixerSettings;
        if (settings.Current.MixerFloating != floating)
        {
            settings.Current.MixerFloating = floating;
            settings.Save();
            if (floating && _mixerDocked) { UndockMixer(); OpenMixerWindow(); }
            else if (!floating && _mixerWindow is not null)
            {
                _mixerSwitching = true;
                try { _mixerWindow.Close(); }
                finally { _mixerSwitching = false; }
                DockMixer();
            }
        }
        SyncMixerMenu();
    }

    // Once the main window is up: bring the Mixer back the way the user left it.
    private void RestoreMixer()
    {
        SyncMixerMenu();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownRequested += (_, _) =>
            {
                _quitRequested = true;
                // A cancelled quit (unsaved changes → Cancel) returns here: count closes again.
                Dispatcher.UIThread.Post(() => _quitRequested = false, DispatcherPriority.Background);
            };
        if (MixerSettings.Current.MixerShown) ShowMixer();
    }

    private void RememberMixerShown(bool shown)
    {
        var settings = MixerSettings;
        if (settings.Current.MixerShown == shown) return;
        settings.Current.MixerShown = shown;
        settings.Save();
    }

    private void SyncMixerMenu()
    {
        if (MenuItemFor("view.mixerFloat") is { } mi) mi.IsChecked = MixerFloating;
    }

    private static MixerViewMode MixerViewFromSettings()
        => MixerSettings.Current.MixerView == "map" ? MixerViewMode.StereoMap : MixerViewMode.Strips;

    private void PickMixerView(MixerViewMode view)
    {
        if (_mixer is not null) _mixer.View = view;
        var settings = MixerSettings;
        settings.Current.MixerView = view == MixerViewMode.StereoMap ? "map" : "strips";
        settings.Save();
        SyncMixerViewButtons();
    }

    // ---- docked panel ---------------------------------------------------------------------

    private void DockMixer()
    {
        if (_mixer is null) return;
        SetDetailMaximized(false);   // asked for, the Mixer must be seen
        _mixer.View = MixerViewFromSettings();
        SyncMixerViewButtons();
        _mixerDocked = true;
        _mixer.IsVisible = true;       // so the tick loop updates its meters
        SetHost(MixerHost, _mixer);
        _mixerRow.MinHeight = MixerPanelMin;
        _mixerWanted = DockedMixerHeight();
        LayoutBottomPanels();
        _mixer.Refresh();
        SyncMixerToggle();
    }

    // The height the user dragged it to; until they have, full strips but no more than half
    // of the body, so the main view keeps its share on a laptop screen.
    private double DockedMixerHeight()
    {
        double h = MixerSettings.Current.MixerDockHeight;
        if (h > 0) return h;
        double body = BodyGrid.Bounds.Height;
        return Math.Max(MixerPanelMin, body > 0 ? Math.Min(MixerPanelNatural, body / 2) : MixerPanelNatural);
    }

    private void UndockMixer()
    {
        _mixerDocked = false;
        SetHost(MixerHost, null);
        if (_mixer is not null && _mixerWindow is null) _mixer.IsVisible = false;
        LayoutBottomPanels();
        SyncMixerToggle();
    }

    // A finished drag of either splitter next to the docked Mixer sets its height.
    private void OnMixerHeightDragged()
    {
        if (!_mixerDocked) return;
        double h = Math.Round(_mixerRow.ActualHeight);
        if (h <= 0) return;
        _mixerWanted = h;
        _mixerRow.Height = new Avalonia.Controls.GridLength(h);   // a splitter may leave it in stars
        var settings = MixerSettings;
        settings.Current.MixerDockHeight = h;
        settings.Save();
    }

    // Double-clicking its top edge forgets the dragged height: back to the first-dock default.
    private void ResetMixerHeight()
    {
        if (!_mixerDocked) return;
        var settings = MixerSettings;
        settings.Current.MixerDockHeight = 0;
        settings.Save();
        _mixerWanted = DockedMixerHeight();
        FitBottomPanels();
    }

    private void OnMixerMinHeightChanged()
    {
        if (_mixerDocked)
        {
            _mixerRow.MinHeight = MixerPanelMin;
            if (MixerSettings.Current.MixerDockHeight <= 0) _mixerWanted = DockedMixerHeight();   // measured now
            FitBottomPanels();
        }
        _mixerWindow?.FitHeightLimits(_mixer!);
    }

    private void SyncMixerViewButtons()
    {
        bool map = (_mixer?.View ?? MixerViewFromSettings()) == MixerViewMode.StereoMap;
        MixerStripsBtn.IsChecked = !map;
        MixerMapBtn.IsChecked = map;
    }

    private void OnMixerStrips(object? sender, RoutedEventArgs e) => PickMixerView(MixerViewMode.Strips);
    private void OnMixerMap(object? sender, RoutedEventArgs e) => PickMixerView(MixerViewMode.StereoMap);
    private void OnMixerPopOut(object? sender, RoutedEventArgs e) => SetMixerFloating(true);
    private void OnCloseMixer(object? sender, RoutedEventArgs e) => HideMixer();

    // ---- own window -------------------------------------------------------------------------

    private void OpenMixerWindow()
    {
        if (_mixer is null || _mixerWindow is not null) return;
        var view = MixerViewFromSettings();
        _mixer.View = view;
        var win = new MixerWindow(this, view);
        var mixer = _mixer;
        // The window is as wide as its strips; the Stereo Map keeps whatever width it has.
        void Fit() { if (mixer.View == MixerViewMode.Strips) win.FitWidth(mixer.StripsWidth); }
        win.ViewPicked += v => { PickMixerView(v); Fit(); };
        win.DockRequested += () => SetMixerFloating(false);
        win.EnableMidiLearn(_learn);   // faders/knobs stay MIDI-mappable in the window
        win.WindowClosed += () =>
        {
            mixer.StripsChanged -= Fit;
            if (!_mixerDocked) mixer.IsVisible = false;
            _mixerWindow = null;
            // Closing it by hand hides the Mixer; a switch to docked or quitting the app doesn't.
            if (!_mixerSwitching && !_shuttingDown && !_quitRequested) RememberMixerShown(false);
            SyncMixerToggle();
        };
        _mixerWindow = win;
        SyncMixerToggle();

        _mixer.IsVisible = true;       // so the tick loop updates its meters
        SetHost(win.Host, _mixer);
        win.FitHeightLimits(_mixer);
        _mixer.StripsChanged += Fit;
        _mixer.Refresh();
        win.Opened += (_, _) => Fit();   // again once shown: only now is it on the owner's screen
        win.Show(this);
    }
}
