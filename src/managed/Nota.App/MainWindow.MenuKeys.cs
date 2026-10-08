// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Menu shortcuts on Windows / Linux. MainWindow.axaml writes them as "Cmd+…", which is the
// Meta (Win) key off macOS, and the in-window menu bar (NativeMenuBar) only draws a
// NativeMenuItem's gesture — it doesn't bind it. So there the gestures are rewritten to Ctrl
// and HandleKeyDown dispatches them itself once the focused view has passed on the key,
// the way AppKit fires the menu's key equivalents on macOS.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;

namespace Nota.App;

public partial class MainWindow
{
    private readonly List<NativeMenuItem> _menuShortcuts = new();

    private void InitMenuShortcuts()
    {
        ToolTip.SetTip(AutomationToggle, MenuKit.Keys("Show parameter-automation lanes (⌘⇧A)"));
        if (OperatingSystem.IsMacOS() || NativeMenu.GetMenu(this) is not { } menu) return;
        Collect(menu);

        void Collect(NativeMenu m)
        {
            foreach (var item in m.Items.OfType<NativeMenuItem>())
            {
                if (item.Gesture is { } g)
                {
                    if ((g.KeyModifiers & KeyModifiers.Meta) != 0)
                        item.Gesture = new KeyGesture(g.Key, (g.KeyModifiers & ~KeyModifiers.Meta) | KeyModifiers.Control);
                    _menuShortcuts.Add(item);
                }
                if (item.Menu is { } sub) Collect(sub);
            }
        }
    }

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
