// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// "What's New" — shown once after the first launch on a newer app version (see
// MainWindow.OnOpenedWhatsNew). Lists the changelog entries the user hasn't seen
// yet, newest first. Reachable any time from the Help menu too.

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class WhatsNewWindow : NotaWindow
{
    public WhatsNewWindow(IReadOnlyList<ChangelogEntry> entries)
    {
        Title = "What's New in Nota";
        Width = 460;
        Height = 556;   // + title-bar band
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Brush.BgApp");

        // Fixed content width so long bullets wrap to the window instead of
        // overflowing (the window is a fixed 460 and non-resizable): 460 − 28px
        // side padding × 2 − room for the vertical scrollbar.
        var stack = new StackPanel { Spacing = 20, Width = 396 };
        foreach (var entry in entries)
            stack.Children.Add(EntryBlock(entry));

        if (entries.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Classes = { "Caption" },
                Text = "No release notes available.",
            });

        var scroll = new ScrollViewer
        {
            Content = stack,
            Padding = new Thickness(28, 8, 28, 8),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var header = new StackPanel { Margin = new Thickness(28, 24, 28, 4), Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = "What's New",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextPrimary"),
        });
        header.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = $"Nota {AppInfo.Version}",
        });

        var ok = new Button
        {
            Content = "Got it",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(28, 8, 28, 24),
            Classes = { "primary" },
        };
        ok.Click += (_, _) => Close();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(ok, 2);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(ok);
        SetBody(root);
    }

    private Control EntryBlock(ChangelogEntry entry)
    {
        var panel = new StackPanel { Spacing = 8 };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock
        {
            Text = "Version " + entry.Version,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.Accent"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrWhiteSpace(entry.Date))
            title.Children.Add(new TextBlock
            {
                Classes = { "Caption" },
                Text = entry.Date,
                VerticalAlignment = VerticalAlignment.Center,
            });
        panel.Children.Add(title);

        foreach (var section in entry.Sections)
        {
            panel.Children.Add(new TextBlock
            {
                Classes = { "SectionLabel" },
                Text = section.Title,
                Margin = new Thickness(0, 4, 0, 0),
            });
            foreach (var item in section.Items)
                panel.Children.Add(Bullet(item));
        }
        return panel;
    }

    private Control Bullet(string text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*") };
        var dot = new TextBlock
        {
            Text = "•",
            Foreground = Brush("Brush.TextTertiary"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var body = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Brush.TextSecondary"),
        };
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(body, 1);
        row.Children.Add(dot);
        row.Children.Add(body);
        return row;
    }

    private IBrush Brush(string key)
        => this.TryFindResource(key, out var v) && v is IBrush b ? b : Brushes.Magenta;
}
