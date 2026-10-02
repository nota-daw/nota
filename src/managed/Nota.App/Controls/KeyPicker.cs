// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The 24-key picker: a MAJOR and a MINOR row of twelve segments, plus a way out ("No key"
// for the project, "Any key" for the browser filter). Shared by the transport's KEY cell
// and the Files tab's key filter so both read the same.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Nota.Application.Samples;

namespace Nota.App;

internal static class KeyPicker
{
    /// <summary>Opens the picker under <paramref name="anchor"/>. <paramref name="pick"/> gets the
    /// chosen key, or null for <paramref name="noneLabel"/>; <paramref name="extras"/> are
    /// shortcut buttons (label, key) shown before it — "Project key · Am".</summary>
    public static void Show(Control anchor, MusicalKey? current, string noneLabel, Action<MusicalKey?> pick,
                            IReadOnlyList<(string Label, MusicalKey Key)>? extras = null)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Content = Build(current, noneLabel, k => { flyout.Hide(); pick(k); }, extras);
        flyout.ShowAt(anchor);
    }

    /// <summary>The picker's content alone (what <see cref="Show"/> puts in its flyout).</summary>
    internal static Control Build(MusicalKey? current, string noneLabel, Action<MusicalKey?> choose,
                                  IReadOnlyList<(string Label, MusicalKey Key)>? extras = null)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(2) };
        foreach (var mode in new[] { KeyMode.Major, KeyMode.Minor })
        {
            panel.Children.Add(new TextBlock { Text = mode == KeyMode.Major ? "MAJOR" : "MINOR", Classes = { "SectionLabel" } });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (int t = 0; t < 12; t++)
            {
                var key = new MusicalKey(t, mode);
                var b = new ToggleButton
                {
                    Content = key.Short, Classes = { "seg" }, IsChecked = current == key,
                    Width = 34, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                ToolTip.SetTip(b, key.Long);
                b.Click += (_, _) => choose(key);
                row.Children.Add(b);
            }
            panel.Children.Add(new Border { Classes = { "segmented" }, Child = row, HorizontalAlignment = HorizontalAlignment.Left });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (label, key) in extras ?? Array.Empty<(string, MusicalKey)>())
        {
            var b = new Button { Content = label, Classes = { "ghost" } };
            b.Click += (_, _) => choose(key);
            actions.Children.Add(b);
        }
        var none = new Button { Content = noneLabel, Classes = { "ghost" } };
        none.Click += (_, _) => choose(null);
        actions.Children.Add(none);
        panel.Children.Add(actions);
        return panel;
    }
}
