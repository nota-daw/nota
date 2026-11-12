// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Menu key (#518, specs/093). On Windows / Linux the in-window menu row costs a full row
// for something used now and then, so it is hidden by default and the transport bar starts
// with a Menu key instead: one ☰ cell whose menu is the whole application menu — a copy of the
// NativeMenu the row draws, made afresh at every opening (MenuMirror), so the two never differ.
// View ▸ Show menu bar and Settings → Appearance bring the row back (Settings.ShowMenuBar); the
// key then steps aside, so one menu is on screen.
//
// Keyboard: with the row hidden, F10 or Alt pressed and released on its own opens the key's
// menu. Any other key, click or wheel turn while Alt is down (Alt+drag to move freely,
// Alt+wheel, ⌥ chords) cancels that, so those keep working. The hidden row is taken out of the
// visual tree, not just hidden: an attached menu bar stays the window's main menu for Avalonia's
// access keys and would take Alt / F10 while invisible.
//
// macOS: none of this — the menu lives in the system menu bar.

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private ContextMenu? _menuKeyMenu;
    private bool _menuBarShown;
    private bool _altTapArmed;
    private bool _menuFromKeyboard;
    private IInputElement? _focusBeforeMenu;
    private DateTime _menuKeyClosedAt;

    /// <summary>Windows / Linux: the in-window menu can be swapped for the Menu key, and
    /// Settings / View offer "Show menu bar". The one place this platform rule is decided;
    /// settable only so a headless harness on a Mac can draw the Windows / Linux chrome.</summary>
    internal static bool HasMenuKey { get; set; } = !OperatingSystem.IsMacOS();

    private void InitMenuButton()
    {
        if (!HasMenuKey) { MenuKeyWell.IsVisible = false; return; }

        var menu = new ContextMenu
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            PlacementTarget = MenuBtn,
        };
        // Opening fires for a right-click only; OpenMenuKeyMenu fills the menu itself.
        menu.Opening += (_, _) => FillMenuKeyMenu();
        menu.Opened += (_, _) =>
        {
            MenuBtn.IsChecked = true;
            if (!_menuFromKeyboard) return;
            // From the keyboard the first entry is highlighted, so the arrows, Enter and Escape act at once.
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var entry in menu.Items)
                    if (entry is MenuItem { IsVisible: true, IsEnabled: true } first)
                    {
                        first.Focus(NavigationMethod.Directional);
                        break;
                    }
            }, DispatcherPriority.Input);
        };
        menu.Closed += (_, _) =>
        {
            MenuBtn.IsChecked = false;
            _menuKeyClosedAt = DateTime.UtcNow;
            // Back to where the keyboard was — unless what was picked moved it (a dialog, a field).
            var back = _focusBeforeMenu;
            _focusBeforeMenu = null;
            Dispatcher.UIThread.Post(() =>
            {
                var now = FocusManager?.GetFocusedElement();
                if (back is not null && (now is null || ReferenceEquals(now, MenuBtn) || now is MenuItem))
                    back.Focus();
            }, DispatcherPriority.Input);
        };
        // A ContextMenu opens only on the control it belongs to; right-clicking the key opens it too.
        MenuBtn.ContextMenu = _menuKeyMenu = menu;

        AddHandler(KeyDownEvent, OnMenuKeyKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnMenuKeyKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => _altTapArmed = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => _altTapArmed = false, RoutingStrategies.Tunnel, handledEventsToo: true);

        ApplyMenuBar(App.Services.GetRequiredService<ISettingsService>().Current.ShowMenuBar);
    }

    /// <summary>Shows or hides the in-window menu row; the Menu key takes its place when it is
    /// hidden. Called at start-up and whenever the setting changes.</summary>
    private void ApplyMenuBar(bool shown)
    {
        if (!HasMenuKey) return;
        if (MenuItemFor("view.menuBar") is { } item) item.IsChecked = shown;
        if (_menuBarShown == shown && MenuKeyWell.IsVisible == !shown) return;
        _menuBarShown = shown;
        if (shown) _menuKeyMenu?.Close();
        MenuBarRow.Child = shown ? MenuBarHost : null;
        MenuBarRow.IsVisible = shown;
        MenuKeyWell.IsVisible = !shown;
        _altTapArmed = false;
        RequestTransportFit();
    }

    private void OnMenuKeyClick(object? sender, RoutedEventArgs e)
    {
        // The press that closed the open menu (light dismiss) is also this click: leave it closed.
        if ((DateTime.UtcNow - _menuKeyClosedAt).TotalMilliseconds < 300) { MenuBtn.IsChecked = false; return; }
        OpenMenuKeyMenu(fromKeyboard: false);
    }

    private void OpenMenuKeyMenu(bool fromKeyboard)
    {
        if (_menuKeyMenu is null || _menuBarShown) return;
        if (_menuKeyMenu.IsOpen) { _menuKeyMenu.Close(); return; }
        _menuFromKeyboard = fromKeyboard;
        _focusBeforeMenu = FocusManager?.GetFocusedElement();
        FillMenuKeyMenu();
        _menuKeyMenu.Open(MenuBtn);
    }

    // A fresh copy of the application menu at every opening: never stale (FR-005).
    private void FillMenuKeyMenu()
    {
        if (_menuKeyMenu is null) return;
        _menuKeyMenu.Items.Clear();
        if (NativeMenu.GetMenu(this) is { } source)
            foreach (var entry in MenuMirror.Build(source)) _menuKeyMenu.Items.Add(entry);
    }

    private void OnMenuKeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (_menuBarShown || _menuKeyMenu is null) return;
        if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            // Alt alone (not AltGr, which arrives as Ctrl+Alt, nor Alt under another modifier).
            _altTapArmed = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) == 0;
            return;
        }
        _altTapArmed = false;
        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None && !_menuKeyMenu.IsOpen)
        {
            OpenMenuKeyMenu(fromKeyboard: true);
            e.Handled = true;
        }
    }

    private void OnMenuKeyKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftAlt or Key.RightAlt)) return;
        bool tap = _altTapArmed;
        _altTapArmed = false;
        if (!tap || _menuBarShown || _menuKeyMenu is null || _menuKeyMenu.IsOpen) return;
        OpenMenuKeyMenu(fromKeyboard: true);
        e.Handled = true;
    }

    // View ▸ Show menu bar: a saved preference shared with Settings → Appearance; Settings.Changed
    // applies it (ApplyMenuBar), as for the track-header side.
    private void ToggleMenuBar()
    {
        if (_vm is null || !HasMenuKey) return;
        bool on = !_vm.Settings.Current.ShowMenuBar;
        _vm.Settings.Current.ShowMenuBar = on;
        _vm.Settings.Save();
        _vm.StatusText = on ? Loc.T("main.commands.menu_bar_shown") : Loc.T("main.commands.menu_bar_hidden");
    }
}
