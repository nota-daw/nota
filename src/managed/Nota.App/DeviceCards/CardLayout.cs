// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The shared pieces of a built-in effect card laid out the #531 way (DESIGN.md § Devices ›
// Card layout: graph, tabs and output; Nota Auto Filter is the reference):
//   · L — a centre panel of tabbed views (each a graph filling it, its knobs in one column on
//     its right, a selector row under it) and a 186 right panel with no tabs of its own that
//     follows the centre tab: a 20 title bar (the section's switch or name left, its mode right)
//     over rows of controls and read-outs, and the output knobs docked under it on every tab;
//   · S — a 260 card: the main graph over an optional selector row and one row of knobs;
//   · the S / L toggle in the header (the device's appended View param, DeviceView).
// The body keeps its own params, knobs and graphs; this file only arranges them.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class CardLayout
{
    public const double RightWidth = 186, MiniWidth = 260;

    static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
    static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
    static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

    // ---- S / L -----------------------------------------------------------------------------
    public static double WidthFor(IAudioEngine engine, int trackId, int deviceIndex, double large)
        => DeviceView.IsMini(engine, trackId, deviceIndex) ? MiniWidth : large;

    public static Control? SizeToggle(DeviceCardContext ctx, int deviceIndex)
        => DeviceView.Index(ctx.Engine, ctx.TrackId, deviceIndex) < 0 ? null
            : MidiCardHeader.SizeToggle(ctx, () => DeviceView.IsMini(ctx.Engine, ctx.TrackId, deviceIndex),
                m => DeviceView.SetMini(ctx.Engine, ctx.TrackId, deviceIndex, m));

    public static bool IsMini(DeviceCardContext ctx, int deviceIndex) => DeviceView.IsMini(ctx.Engine, ctx.TrackId, deviceIndex);

    // ---- centre views ----------------------------------------------------------------------
    /// <summary>A column of knob cells spread down the graph's right side.</summary>
    public static Control KnobColumn(params Control[] cells)
    {
        var g = new Grid { Width = 52 };
        for (int i = 0; i < cells.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            cells[i].VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(GRow(cells[i], i));
        }
        return g;
    }

    /// <summary>A centre view: the graph fills it, its knobs on the right, a selector row under it.</summary>
    public static Control View(Control graph, Control? knobs = null, Control? bottom = null)
    {
        var d = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 6, 6, 5) };
        if (bottom != null)
        {
            bottom.HorizontalAlignment = HorizontalAlignment.Center;
            d.Children.Add(Docked(new Border { Margin = new Thickness(0, 5, 0, 0), Child = bottom }, Dock.Bottom));
        }
        if (knobs != null) d.Children.Add(Docked(new Border { Margin = new Thickness(6, 0, 0, 0), Child = knobs }, Dock.Right));
        d.Children.Add(graph);
        return d;
    }

    /// <summary>Controls side by side (a selector row).</summary>
    public static StackPanel Line(double spacing, params Control[] cs)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in cs) s.Children.Add(c);
        return s;
    }

    // ---- right panel -----------------------------------------------------------------------
    /// <summary>The panel's title bar content: its switch or name on the left, its mode on the right.</summary>
    public static Control Head(Control left, Control? right = null)
    {
        var d = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
        if (right != null) d.Children.Add(Docked(right, Dock.Right));
        d.Children.Add(Docked(left, Dock.Left));
        return d;
    }

    /// <summary>A section's name in its title bar (when it has no switch).</summary>
    public static TextBlock Title(string t) => new()
    { Text = t, FontSize = 9, FontWeight = FontWeight.Medium, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Rows spread evenly down the panel; rules and read-out boxes keep their height.</summary>
    public static Control Rows(params Control[] rows)
    {
        var grid = new Grid { RowSpacing = 2 };
        for (int r = 0; r < rows.Length; r++)
        {
            bool fixedH = rows[r] is Border { Height: 1 } || rows[r] is Border { Background: not null };
            grid.RowDefinitions.Add(new RowDefinition(fixedH ? GridLength.Auto : GridLength.Star));
            grid.Children.Add(GRow(rows[r], r));
        }
        return new Border { Padding = new Thickness(8, 5, 8, 5), Child = grid };
    }

    public static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };

    /// <summary>A caps label in a fixed column, then the control (left-aligned, or stretched — a slider).</summary>
    public static Control LabelRow(string label, Control c, double labW = 40, bool stretch = false)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        c.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        c.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(Col(c, 1));
        return g;
    }

    /// <summary>Two controls side by side, each half the panel.</summary>
    public static Control Pair(Control a, Control b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(a); g.Children.Add(Col(b, 1));
        return g;
    }

    /// <summary>A control on the left and one pushed to the right edge.</summary>
    public static Control Ends(Control a, Control b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(a); g.Children.Add(Col(b, 1));
        return g;
    }

    /// <summary>A sunken read-out box: a caps title over lines (what the device measures).</summary>
    public static Border ReadoutBox(Control content) => new()
    {
        Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
        Padding = new Thickness(6, 4), Child = content,
    };

    // ---- output ----------------------------------------------------------------------------
    /// <summary>The output knobs under every panel: the main one (Dry/wet, 44) in the middle,
    /// what comes before the core on its left, the level after it on its right; cells line up
    /// along the bottom. <paramref name="above"/> is an output-stage switch over them.</summary>
    public static Control Output(Control? left, Control main, Control? right, Control? above = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(-4, 5, -4, 4) };
        void Cell(Control? c, int col)
        {
            if (c is null) return;
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.VerticalAlignment = VerticalAlignment.Bottom;
            g.Children.Add(Col(c, col));
        }
        Cell(left, 0); Cell(main, 1); Cell(right, 2);
        Control child = g;
        if (above != null)
        {
            above.Margin = new Thickness(0, 5, 0, 0);
            child = new StackPanel { Children = { above, g } };
        }
        return new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(8, 0), Child = child };
    }

    // ---- frames ----------------------------------------------------------------------------
    /// <summary>The L body: tabbed centre views and the right panel that follows them over the
    /// output. <paramref name="changed"/> runs after a tab switch (the body refreshes there).</summary>
    public static Control Large(string[] tabs, int initial, Func<int, Control> centre, Func<int, (Control Head, Control Body)> panel,
        Control? extras, Control output, Action<int> changed)
    {
        var centreHost = new ContentControl();
        var panelHost = new ContentControl();
        var headHost = new ContentControl();
        var centres = new Control?[tabs.Length];
        var panels = new (Control Head, Control Body)?[tabs.Length];
        void Show(int t)
        {
            centreHost.Content = centres[t] ??= centre(t);
            var (head, body) = panels[t] ??= panel(t);
            headHost.Content = head;
            panelHost.Content = body;
        }

        int sel = initial;
        var btns = new Border[tabs.Length];
        void Hi()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = i == sel;
                btns[i].Background = on ? Card2 : Brushes.Transparent;
                btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                var tb = (TextBlock)btns[i].Child!;
                tb.Foreground = on ? NotaPalette.TextPrimary : TextTertiary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < tabs.Length; i++)
        {
            int iv = i;
            var b = new Border
            {
                Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
            };
            b.PointerPressed += (_, _) => { sel = iv; Hi(); Show(iv); changed(iv); };
            btns[i] = b;
            bar.Children.Add(b);
        }
        var barDock = new DockPanel { Height = 20, LastChildFill = false };
        if (extras != null) barDock.Children.Add(Docked(new Border { Padding = new Thickness(0, 0, 8, 0), Child = extras }, Dock.Right));
        barDock.Children.Add(Docked(bar, Dock.Left));
        var tabBar = Docked(new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock }, Dock.Top);
        Hi();
        Show(sel);

        var centreFrame = new DockPanel { LastChildFill = true, Children = { tabBar, centreHost } };
        var headBar = new Border { Height = 20, Padding = new Thickness(8, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = headHost };
        var rightCol = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), Docked(output, Dock.Bottom), panelHost } };
        var right = Docked(new RuleSection { Width = RightWidth, Child = rightCol }, Dock.Right);
        var centreBox = new RuleSection { Margin = new Thickness(0, 0, 5, 0), Child = centreFrame };
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, centreBox } };
        return new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };
    }

    /// <summary>The S body: the main graph over an optional selector row and one row of knobs.</summary>
    public static Control Mini(Control graph, Control? selector, params Control[] knobs)
    {
        var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var k in knobs) { k.VerticalAlignment = VerticalAlignment.Bottom; row.Children.Add(k); }
        var body = new DockPanel { Margin = new Thickness(6), Children = { Docked(row, Dock.Bottom) } };
        if (selector != null)
        {
            selector.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(Docked(new Border { Margin = new Thickness(0, 5, 0, 0), Child = selector }, Dock.Bottom));
        }
        body.Children.Add(graph);
        return new Border { Background = NotaPalette.SurfaceCard, Child = new RuleSection { Margin = new Thickness(5), Child = body } };
    }
}
