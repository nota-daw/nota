// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Menu key's menu (#518): a copy of the application menu — the NativeMenu in
// MainWindow.axaml, which the in-window menu bar draws on Windows / Linux — made afresh each
// time the menu opens. Everything the app keeps for the menu lives on the NativeMenuItems
// (labels, the catalogue's shortcut gestures, check marks set through MenuItemFor, the Open
// recent list rebuilt in place), so a copy made at opening is never stale and nothing is listed
// twice. Picking a copied item raises the source item's click — the path TryMenuShortcut takes —
// so it runs exactly what the menu bar would.
//
// Avalonia base only (no window, no app): the smoke test compiles this file in and compares a
// menu with its copy through Describe.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;

namespace Nota.App;

internal static class MenuMirror
{
    /// <summary>Fresh menu entries for <paramref name="source"/>: a MenuItem per NativeMenuItem
    /// (submenus included), a Separator per NativeMenuItemSeparator.</summary>
    public static List<Control> Build(NativeMenu source)
    {
        var items = new List<Control>(source.Items.Count);
        foreach (var entry in source.Items)
        {
            switch (entry)
            {
                case NativeMenuItemSeparator:
                    items.Add(new Separator());
                    break;
                case NativeMenuItem src:
                    items.Add(Copy(src));
                    break;
            }
        }
        return items;
    }

    private static MenuItem Copy(NativeMenuItem src)
    {
        var item = new MenuItem
        {
            Header = src.Header,
            InputGesture = src.Gesture,       // shown only: the window dispatches the shortcut (MenuKeys)
            ToggleType = src.ToggleType,
            IsChecked = src.IsChecked,
            IsEnabled = src.IsEnabled,
            IsVisible = src.IsVisible,
        };
        if (!string.IsNullOrEmpty(src.ToolTip)) ToolTip.SetTip(item, src.ToolTip);
        if (src.Menu is { } sub)
        {
            foreach (var child in Build(sub)) item.Items.Add(child);
        }
        else
        {
            item.Click += (_, _) => ((INativeMenuItemExporterEventsImplBridge)src).RaiseClicked();
        }
        return item;
    }

    /// <summary>A plain-text outline of a menu — one line per entry, indented by depth — so a
    /// menu and its copy can be compared in a test.</summary>
    public static string Describe(NativeMenu menu)
    {
        var sb = new StringBuilder();
        Walk(menu, 0);
        return sb.ToString();

        void Walk(NativeMenu m, int depth)
        {
            foreach (var entry in m.Items)
            {
                if (entry is NativeMenuItemSeparator) { Line(depth, nameof(Separator)); continue; }
                if (entry is not NativeMenuItem i) continue;
                Line(depth, Entry(i.Header, i.Gesture, i.ToggleType, i.IsChecked, i.IsEnabled, i.IsVisible, i.ToolTip, i.Menu is not null));
                if (i.Menu is { } sub) Walk(sub, depth + 1);
            }
        }

        void Line(int depth, string text) => sb.Append(' ', depth * 2).Append(text).Append('\n');
    }

    /// <inheritdoc cref="Describe(NativeMenu)"/>
    public static string Describe(IEnumerable<Control> items)
    {
        var sb = new StringBuilder();
        Walk(items, 0);
        return sb.ToString();

        void Walk(IEnumerable<Control> list, int depth)
        {
            foreach (var entry in list)
            {
                if (entry is Separator) { Line(depth, nameof(Separator)); continue; }
                if (entry is not MenuItem i) continue;
                var children = i.Items.OfType<Control>().ToList();
                bool hasSub = children.Count > 0;
                Line(depth, Entry(i.Header as string, i.InputGesture, i.ToggleType, i.IsChecked, i.IsEnabled, i.IsVisible,
                    ToolTip.GetTip(i) as string, hasSub));
                if (hasSub) Walk(children, depth + 1);
            }
        }

        void Line(int depth, string text) => sb.Append(' ', depth * 2).Append(text).Append('\n');
    }

    // Every field, '|'-separated: header, gesture, toggle type, checked, enabled, visible,
    // tooltip, has a submenu.
    private static string Entry(string? header, KeyGesture? gesture, MenuItemToggleType toggle, bool isChecked, bool enabled,
        bool visible, string? tip, bool submenu) =>
        string.Join('|', header, gesture, toggle, isChecked, enabled, visible, tip, submenu);
}
