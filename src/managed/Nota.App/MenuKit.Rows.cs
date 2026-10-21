// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The inline rows a context menu can carry (spec 034): the object header, the colour grid,
// a segmented row and the switch item. A menu shows a row as a MenuItem with the `inline`
// class (no highlight, spans the width — NotaTheme.axaml), so keyboard navigation and
// light-dismiss keep working. A row applies through the item's own Click — the pick the
// pointer is over, or the keyboard cursor — so the menu closes the usual way.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal static partial class MenuKit
{
    /// <summary>The object a menu acts on, named at its top: colour chip (when it has one),
    /// name in the heading weight, and a muted mono badge with its type and position or count
    /// ("Audio · 3", "3 tracks"). Not interactive.</summary>
    public static MenuItem Header(string name, IBrush? colour, string badge)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 28 };
        if (colour is not null)
            grid.Children.Add(new Border
            {
                Width = 8, Height = 8, CornerRadius = NotaRadius.Badge, Background = colour,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            });
        var title = new TextBlock
        {
            Text = name, FontWeight = FontWeight.SemiBold, FontSize = NotaType.Name,
            Foreground = NotaPalette.TextHeading, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (badge.Length > 0)
        {
            var tag = new TextBlock
            {
                Text = badge, FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.Value,
                Foreground = NotaPalette.TextTertiary, Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);
        }
        var mi = new MenuItem { Header = grid, IsHitTestVisible = false, Focusable = false, IsEnabled = false };
        mi.Classes.Add("inline");
        return mi;
    }

    /// <summary>The track palette in one click: 9 bases × (base, light, dark) plus Auto, the
    /// current colour ringed. Click or Enter picks (index, or −1 for Auto) and closes the menu;
    /// arrow keys move the cursor ring.</summary>
    public static MenuItem ColourGrid(int current, Action<int> pick)
    {
        int bases = ArrangementView.PaletteBases, shades = ArrangementView.PaletteShades;
        var row = new PickRow();
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4) };
        for (int c = 0; c < bases; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(18, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < shades; r++) grid.RowDefinitions.Add(new RowDefinition(18, GridUnitType.Pixel));

        // Cursor order: row by row (base, light, dark), Auto last.
        for (int s = 0; s < shades; s++)
            for (int b = 0; b < bases; b++)
            {
                int ci = b * shades + s;
                var swatch = new Border { Width = 12, Height = 12, CornerRadius = NotaRadius.Badge, Background = ArrangementView.TrackBrush(ci) };
                var ring = new Border
                {
                    Width = 16, Height = 16, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1),
                    Child = swatch, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                };
                Grid.SetColumn(ring, b); Grid.SetRow(ring, s);
                grid.Children.Add(ring);
                row.Add(ring, ci, ci == current, (cell, chosen, cursor) =>
                    cell.BorderBrush = chosen ? NotaPalette.TextPrimary : cursor ? NotaPalette.TextMuted : Brushes.Transparent,
                    up: s > 0 ? row.Count - bases : -1, down: s < shades - 1 ? row.Count + bases : -1);
            }

        var auto = new Border
        {
            Height = 52, MinWidth = 40, Margin = new Thickness(6, 0, 0, 0), CornerRadius = NotaRadius.Control,
            BorderThickness = new Thickness(1), Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "Auto", FontSize = NotaType.Caption, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
        };
        Grid.SetColumn(auto, bases); Grid.SetRowSpan(auto, shades);
        grid.Children.Add(auto);
        row.Add(auto, -1, current == -1, (cell, chosen, cursor) =>
        {
            cell.BorderBrush = chosen ? NotaPalette.TextPrimary : NotaPalette.Rule;
            cell.Background = cursor ? NotaPalette.SurfaceHover : Brushes.Transparent;
        });

        return row.Attach(grid, (v, _) => pick(v));
    }

    /// <summary>A captioned row of segmented keys ("Height  S M L"): the chosen key in the
    /// accent. Click or Enter picks; Option / Alt held passes <c>all = true</c>.</summary>
    public static MenuItem Segments<T>(string label, IReadOnlyList<(string Label, T Value)> options, T? chosen,
                                       Action<T, bool> pick) where T : struct
    {
        var row = new PickRow();
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        var group = new Border
        {
            Child = keys, Background = NotaPalette.Seam, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Right,
        };
        for (int i = 0; i < options.Count; i++)
        {
            var (name, value) = options[i];
            var text = new TextBlock { Text = name, FontSize = NotaType.Caption, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var key = new Border { MinWidth = 52, Height = 18, Padding = new Thickness(6, 0), Child = text };
            keys.Children.Add(key);
            bool isChosen = chosen is { } c && EqualityComparer<T>.Default.Equals(c, value);
            row.Add(key, i, isChosen, (cell, ch, cursor) =>
            {
                var k = cell;
                k.Background = ch ? NotaPalette.Accent : cursor ? NotaPalette.SurfaceHover : NotaPalette.SurfaceRaised;
                ((TextBlock)k.Child!).Foreground = ch ? NotaPalette.TextOnAccent : NotaPalette.TextSecondary;
            });
        }
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 28 };
        line.Children.Add(new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.Caption, Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(group, 1);
        group.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(group);
        return row.Attach(line, (i, all) => pick(options[i].Value, all));
    }

    /// <summary>A small colour chip for a menu's value column — a user colour that is data (a
    /// tag's colour), shown beside the label rather than as its icon so a tick can sit left.</summary>
    public static Control Swatch(IBrush colour)
        => new Border { Width = 8, Height = 8, CornerRadius = NotaRadius.Badge, Background = colour, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Appends a "Recent" caption and its items to a submenu (nothing when empty).</summary>
    public static void AddRecent(MenuItem submenu, IReadOnlyList<MenuItem>? recent)
    {
        if (recent is null || recent.Count == 0) return;
        submenu.Items.Add(new Separator());
        submenu.Items.Add(Caption("Recent"));
        foreach (var mi in recent) submenu.Items.Add(mi);
    }

    /// <summary>A small muted caption heading a group inside a menu ("Recent"). Not interactive.</summary>
    public static MenuItem Caption(string text)
    {
        var mi = new MenuItem
        {
            Header = new TextBlock { Text = NotaText.Label(text), FontSize = NotaType.Caption, Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Center },
            IsHitTestVisible = false, Focusable = false, IsEnabled = false, MinHeight = 20,
        };
        mi.Classes.Add("inline");
        return mi;
    }

    /// <summary>An item with an 18 × 10 switch in its value column (accent while on); a click
    /// anywhere on the item toggles it.</summary>
    public static MenuItem SwitchItem(string header, GlyphKind? icon, bool isOn, Action toggle)
    {
        var mi = Item(header, icon, toggle);
        SetTrailing(mi, new SwitchTrack { IsOn = isOn });
        return mi;
    }

    // One inline row of pickable cells: tracks the pointer and a keyboard cursor, paints the
    // cells through the caller's painter, and applies on the hosting item's Click.
    private sealed class PickRow
    {
        private readonly List<(Border Cell, int Value, Action<Border, bool, bool> Paint, int Up, int Down)> _cells = new();
        private int _chosen = -1, _cursor = -1;
        private KeyModifiers _mods;

        public int Count => _cells.Count;

        public void Add(Border cell, int value, bool chosen, Action<Border, bool, bool> paint, int up = -1, int down = -1)
        {
            int i = _cells.Count;
            _cells.Add((cell, value, paint, up, down));
            if (chosen) _chosen = i;
            cell.Background ??= Brushes.Transparent;
            cell.PointerEntered += (_, _) => Move(i);
            paint(cell, chosen, false);
        }

        private void Move(int i)
        {
            int old = _cursor;
            _cursor = i;
            if (old >= 0 && old < _cells.Count) Repaint(old);
            if (i >= 0) Repaint(i);
        }

        private void Repaint(int i) => _cells[i].Paint(_cells[i].Cell, i == _chosen, i == _cursor);

        public MenuItem Attach(Control content, Action<int, bool> apply)
        {
            var mi = new MenuItem { Header = content };
            mi.Classes.Add("inline");
            content.PointerExited += (_, _) => Move(-1);
            // Remember the modifiers of the gesture that will become Click.
            mi.AddHandler(InputElement.PointerReleasedEvent, (_, e) => _mods = e.KeyModifiers, RoutingStrategies.Tunnel);
            mi.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                _mods = e.KeyModifiers;
                if (_cells.Count == 0) return;
                int at = _cursor >= 0 ? _cursor : Math.Max(0, _chosen);
                int next = e.Key switch
                {
                    Key.Left => Math.Max(0, at - 1),
                    Key.Right => Math.Min(_cells.Count - 1, at + 1),
                    Key.Up when _cells[at].Up >= 0 => _cells[at].Up,
                    Key.Down when _cells[at].Down >= 0 => _cells[at].Down,
                    _ => -1,
                };
                if (next >= 0) { Move(next); e.Handled = true; }
            }, RoutingStrategies.Tunnel);
            mi.GotFocus += (_, _) => { if (_cursor < 0) Move(Math.Max(0, _chosen)); };
            mi.Click += (_, _) =>
            {
                if (_cursor < 0 || _cursor >= _cells.Count) return;
                apply(_cells[_cursor].Value, (_mods & KeyModifiers.Alt) != 0);
            };
            return mi;
        }
    }
}
