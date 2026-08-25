// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Browser tag manager: create / rename / recolour / delete the tags used to
// organise instruments, effects and MIDI effects. Edits are live — each change
// persists through IBrowserLibrary immediately (which fires Changed so the
// browser chips/rows refresh). Opened from the browser context menu / chip strip.
// When constructed with a device key, it seeds a fresh tag already assigned to
// that device ("New tag…").

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class TagEditorWindow : NotaWindow
{
    // A small, dark-friendly palette; the first entry is the brass brand accent.
    private static readonly string[] Palette =
    {
        "#C8A24B", "#E0554E", "#E08A3C", "#E0C24E", "#6FB86F",
        "#4EA6C8", "#5A7DE0", "#9A6FE0", "#D06FB0", "#8A8F98",
    };

    private readonly IBrowserLibrary _lib;
    private readonly string? _assignKey;
    private readonly StackPanel _rows;

    public TagEditorWindow(IBrowserLibrary library, string? assignToKey = null)
    {
        _lib = library;
        _assignKey = assignToKey;

        Title = "Edit Tags";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.TryFindResource("Brush.BgApp", out var bg) && bg is IBrush b ? b : Brushes.Magenta;

        _rows = new StackPanel { Spacing = 8 };

        var add = new Button { Content = "Add tag", Classes = { "ghost" } };
        add.Click += (_, _) => { CreateTag(); RebuildRows(); };

        var close = new Button { Content = "Close", Classes = { "primary" } };
        close.Click += (_, _) => Close();

        SetBody(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Tags — title & colour", Classes = { "Caption" } },
                new ScrollViewer { MaxHeight = 360, Content = _rows },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right, Children = { close },
                },
            },
        });

        // "New tag…" for a device: seed one, already attached, ready to rename.
        if (_assignKey is not null) CreateTag();
        RebuildRows();
    }

    private void CreateTag()
    {
        var tag = _lib.CreateTag("New Tag", Palette[0]);
        if (_assignKey is not null) _lib.AssignTag(_assignKey, tag.Id, true);
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        foreach (var tag in _lib.Tags) _rows.Children.Add(BuildRow(tag));
        if (_lib.Tags.Count == 0)
            _rows.Children.Add(new TextBlock { Text = "No tags yet — add one below.", Opacity = 0.6, FontSize = 11 });
    }

    private Control BuildRow(BrowserTag tag)
    {
        var title = new TextBox { Text = tag.Title, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        void CommitTitle()
        {
            var t = title.Text?.Trim();
            _lib.UpdateTag(tag.Id, string.IsNullOrEmpty(t) ? tag.Title : t, tag.Color);
        }
        title.LostFocus += (_, _) => CommitTitle();
        title.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitTitle(); };

        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var hex in Palette)
        {
            var c = hex;
            bool sel = string.Equals(c, tag.Color, StringComparison.OrdinalIgnoreCase);
            var sw = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse(c)),
                BorderThickness = new Thickness(sel ? 2 : 0),
                BorderBrush = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            sw.PointerPressed += (_, _) => { _lib.UpdateTag(tag.Id, tag.Title, c); RebuildRows(); };
            swatches.Children.Add(sw);
        }

        var del = new Button { Content = "✕", Classes = { "ghost" }, Width = 32 };
        del.Click += (_, _) => { _lib.DeleteTag(tag.Id); RebuildRows(); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(swatches, 1);
        Grid.SetColumn(del, 2);
        grid.Children.Add(title);
        grid.Children.Add(swatches);
        grid.Children.Add(del);
        return grid;
    }
}
