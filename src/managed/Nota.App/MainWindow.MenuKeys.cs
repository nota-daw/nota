// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The menu runs registered commands (CP-24): each NativeMenuItem in MainWindow.axaml names a
// command id (app:Commands.Id); here it gets the command's shortcut from the catalog and its
// click runs the command. On macOS AppKit fires the shortcuts as menu key equivalents. On
// Windows / Linux the in-window menu bar (NativeMenuBar) only draws a gesture — it doesn't
// bind it — so HandleKeyDown dispatches them itself once the focused view has passed on the
// key, with ⌘ read as Ctrl.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Nota.Application.Palette;

namespace Nota.App;

public partial class MainWindow
{
    private readonly List<NativeMenuItem> _menuShortcuts = new();
    private readonly Dictionary<string, NativeMenuItem> _menuItems = new(StringComparer.Ordinal);

    private void InitMenuShortcuts()
    {
        BindCommands();
        ToolTip.SetTip(AutomationToggle, MenuKit.Keys("Show parameter-automation lanes (⌘⇧A)"));
        if (NativeMenu.GetMenu(this) is not { } menu) return;
        Collect(menu);

        void Collect(NativeMenu m)
        {
            foreach (var item in m.Items.OfType<NativeMenuItem>())
            {
                if (Commands.GetId(item) is { } id && _commands.Get(id) is { } cmd)
                {
                    _menuItems[id] = item;
                    item.Click += (_, _) => _commands.Run(id);
                    // Settings… keeps ⌘, on the app menu (App.axaml) on macOS; one key, one item.
                    bool appMenuOwnsIt = OperatingSystem.IsMacOS() && id == "prefs.open";
                    if (Commands.IsChord(cmd.Gesture) && !appMenuOwnsIt && Commands.Parse(cmd.Gesture) is { } g)
                    {
                        item.Gesture = g;
                        if (!OperatingSystem.IsMacOS()) _menuShortcuts.Add(item);
                    }
                }
                if (item.Menu is { } sub) Collect(sub);
            }
        }
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
