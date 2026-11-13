// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The menu runs registered commands (CP-24): each NativeMenuItem in MainWindow.axaml names a
// command id (app:Commands.Id); here it gets the command's key from the user's bindings
// (specs/096 — re-applied whenever a key changes) and its click runs the command. On macOS AppKit fires the shortcuts as menu key equivalents. On
// Windows / Linux the in-window menu bar (NativeMenuBar) only draws a gesture — it doesn't
// bind it — so HandleKeyDown dispatches them itself once the focused view has passed on the
// key, with ⌘ read as Ctrl.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Nota.Application.Palette;
using Nota.Application.Input;
using Nota.App.Input;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private readonly List<NativeMenuItem> _menuShortcuts = new();
    private readonly Dictionary<string, NativeMenuItem> _menuItems = new(StringComparer.Ordinal);

    private void InitMenuShortcuts()
    {
        BindCommands();
        // Tooltips that name a key follow its binding (specs/096) and are spelled Ctrl+… on
        // Windows / Linux (#10).
        Keys.SetTip(AutomationToggle, Loc.T("main.menu_keys.show_parameter_automation_lanes_a"), "view.automation");
        foreach (var (c, id) in new (Control, string)[]
                 {
                     (RecordBtn, "transport.record"), (LoopRecordCell, "transport.loopRecordMode"), (MetronomeBtn, "transport.metronome"),
                     (LoopCell, "transport.loop"),
                 })
            if (ToolTip.GetTip(c) is string tip) Keys.SetTip(c, tip, id);
        if (NativeMenu.GetMenu(this) is not { } menu) return;
        // macOS quits from the app menu (Quit Nota, ⌘Q); File → Exit is for Windows / Linux (#316).
        // Settings… lives in the app menu there too (Nota → Settings…, ⌘,), so File doesn't repeat it (#444).
        if (OperatingSystem.IsMacOS())
        {
            RemoveWithSeparatorAbove(menu, "file.exit");
            RemoveWithSeparatorAbove(menu, "prefs.open");
        }
        if (!HasMenuKey) RemoveWithSeparatorAbove(menu, "view.menuBar");   // macOS: the menu is in the system bar (#518)
        Collect(menu);
        // A key a menu item carries reaches every scope on macOS: the conflict check reads which.
        // macOS: Settings… lives in the app menu (App.axaml), which carries its key too.
        Keys.Bindings.SetMenuCommands(OperatingSystem.IsMacOS() ? _menuItems.Keys.Append("prefs.open") : _menuItems.Keys);
        ApplyMenuGestures();
        Keys.Bindings.Changed += ApplyMenuGestures;

        void Collect(NativeMenu m)
        {
            foreach (var item in m.Items.OfType<NativeMenuItem>())
            {
                if (Commands.GetId(item) is { } id && _commands.Get(id) is not null)
                {
                    _menuItems[id] = item;
                    item.Click += (_, _) => _commands.Run(id);
                }
                if (item.Menu is { } sub) Collect(sub);
            }
        }
    }

    private bool _menuGesturesSuspended;

    /// <summary>Takes the keys off the menu while the Shortcuts editor records one (macOS fires
    /// menu key equivalents before any window sees the key), and puts them back.</summary>
    internal void SuspendMenuGestures(bool suspend)
    {
        _menuGesturesSuspended = suspend;
        ApplyMenuGestures();
    }

    // Each menu item shows (and on macOS fires) its command's current key: a chord or a bare
    // function key — a bare letter or digit as a key equivalent would swallow typing.
    private void ApplyMenuGestures()
    {
        _menuShortcuts.Clear();
        foreach (var (id, item) in _menuItems)
        {
            var a = Keys.Bindings.Get(id);
            var key = a is null || a.NoMenuKey || _menuGesturesSuspended ? null : Keys.Bindings.Shown(id);
            item.Gesture = key is { CanMenuGesture: true } k ? k.ToGesture() : null;
            if (item.Gesture is not null && !OperatingSystem.IsMacOS()) _menuShortcuts.Add(item);
        }
        // The macOS app menu's Settings… (App.axaml) follows Settings' binding too.
        if (OperatingSystem.IsMacOS() && Avalonia.Application.Current is { } app && NativeMenu.GetMenu(app) is { } appMenu)
            foreach (var item in appMenu.Items.OfType<NativeMenuItem>())
                if (item.Header == Loc.T("app.settings"))
                    item.Gesture = !_menuGesturesSuspended && Keys.Bindings.Shown("prefs.open") is { CanMenuGesture: true } k ? k.ToGesture() : null;
    }

    // Drops the item bound to commandId (searching submenus) and the separator just above it.
    private static bool RemoveWithSeparatorAbove(NativeMenu m, string commandId)
    {
        for (int i = 0; i < m.Items.Count; i++)
        {
            if (m.Items[i] is not NativeMenuItem item) continue;
            if (Commands.GetId(item) == commandId)
            {
                m.Items.RemoveAt(i);
                if (i > 0 && m.Items[i - 1] is NativeMenuItemSeparator) m.Items.RemoveAt(i - 1);
                return true;
            }
            if (item.Menu is { } sub && RemoveWithSeparatorAbove(sub, commandId)) return true;
        }
        return false;
    }

    private NativeMenuItem? MenuItemFor(string commandId) => _menuItems.GetValueOrDefault(commandId);

    /// <summary>Runs the menu command whose gesture the key matches (Windows / Linux only).</summary>
    private bool TryMenuShortcut(KeyEventArgs e)
    {
        foreach (var item in _menuShortcuts)
            if (item.IsEnabled && item.Gesture!.Matches(e))
            {
                ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
                return true;
            }
        return false;
    }
}
