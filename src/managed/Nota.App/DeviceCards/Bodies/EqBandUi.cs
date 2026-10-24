// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The pieces EQ-8 and Dynamic EQ-8 share (style guide, spec 038): the numbered band toggle
// (orange tint when the band is on, the same control as the band's On switch), one band's tab
// in the row over the graph, the strip of six filter shapes that picks a band's type, the head
// of the band panel, and the arrow that folds the side panel.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class EqBandUi
{
    internal const double TabH = 40, BadgeD = 18, FoldedW = 42;

    /// <summary>A filter type drawn as its response, 22 × 12: high-pass, low shelf, bell, notch,
    /// high shelf, low-pass (DynEq.TypeNames order).</summary>
    internal sealed class FilterShape : Control
    {
        private static readonly Geometry[] Shapes = Glyph.FilterShapes;

        private int _type;
        private IBrush _ink = TextSecondary;

        public FilterShape() { Width = 22; Height = 12; VerticalAlignment = VerticalAlignment.Center; }

        public int Type { get => _type; set { int v = Math.Clamp(value, 0, Shapes.Length - 1); if (v == _type) return; _type = v; InvalidateVisual(); } }
        public IBrush Ink { get => _ink; set { if (ReferenceEquals(value, _ink)) return; _ink = value; InvalidateVisual(); } }

        public override void Render(DrawingContext ctx)
            => ctx.DrawGeometry(null, new Pen(_ink, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Shapes[_type]);
    }

    /// <summary>The numbered band toggle: a square in the soft orange tint with an orange number
    /// while the band is on, the knob-track grey with an ink-3 number while it is off.</summary>
    internal static Border Badge(int band, Func<bool> on, Action toggle, out Action sync)
    {
        var num = new TextBlock
        {
            Text = (band + 1).ToString(), FontSize = NotaType.Caption, FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            Width = BadgeD, Height = BadgeD, CornerRadius = NotaRadius.Cell, VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand), Child = num,
        };
        void Paint()
        {
            bool o = on();
            badge.Background = o ? NotaPalette.AccentSoft : NotaPalette.WellTrack;
            num.Foreground = o ? AccentBright : TextTertiary;
        }
        badge.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(badge).Properties.IsLeftButtonPressed) return;
            toggle(); Paint(); e.Handled = true;
        };
        ToolTip.SetTip(badge, NotaNum.F($"Band {band + 1} on / off"));
        sync = Paint;
        Paint();
        return badge;
    }

    /// <summary>One band's tab in the row over the graph: the numbered toggle and the band's
    /// filter shape on top, its frequency under them (10, ink 2; ink 4 while the band is off), an
    /// orange outline while it is the band being edited and (Dynamic EQ-8) the band's dynamics
    /// activity as a 2px orange bar along the bottom. A click on the tab itself picks the band.</summary>
    internal static Border Tab(Border badge, Func<int> type, Func<bool> on, Func<string> freq, Func<bool> selected, Action select,
        out Action sync, Func<double>? activity = null)
    {
        var shape = new FilterShape { HorizontalAlignment = HorizontalAlignment.Right };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        top.Children.Add(badge);
        Grid.SetColumn(shape, 1);
        top.Children.Add(shape);
        var hz = new TextBlock
        {
            FontFamily = NotaFonts.UiFamily, FontSize = NotaType.Axis, LineHeight = 12, Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.None,
        };
        var stack = new StackPanel { Children = { top, hz } };
        var bar = new Border
        {
            Height = 2, CornerRadius = NotaRadius.Fine, Background = Brass, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -3),
        };
        var tab = new Border
        {
            Height = TabH, CornerRadius = NotaRadius.Field, Background = NotaPalette.Well, BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 3, 3, 3), Cursor = new Cursor(StandardCursorType.Hand), ClipToBounds = true,
            Child = activity is null ? stack : new Panel { Children = { stack, bar } },
        };
        void Paint()
        {
            bool o = on(), sel = selected();
            tab.BorderBrush = sel ? Brass : Brushes.Transparent;
            shape.Type = type();
            shape.Ink = o ? TextSecondary : TextDisabled;
            hz.Text = freq();
            hz.Foreground = !o ? TextDisabled : sel ? TextPrimary : TextSecondary;
            if (activity is not null) bar.Width = Math.Clamp(activity(), 0, 1) * Math.Max(0, tab.Bounds.Width - 8);
        }
        tab.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
            select(); e.Handled = true;
        };
        sync = Paint;
        Paint();
        return tab;
    }

    /// <summary>The filter type as a strip of the six shapes (DynEq.TypeNames order) in a
    /// groove: the picked shape rises out of it in the soft tint, drawn in orange; the others are
    /// ink 3. Each shape's name is its tooltip. Left button picks.</summary>
    internal static Border TypeRow(string[] typeNames, Func<int> type, Action<int> pick, out Action sync)
    {
        int n = typeNames.Length;
        var cells = new Border[n];
        var shapes = new FilterShape[n];
        var row = new Grid();
        for (int i = 0; i < n; i++)
        {
            int iv = i;
            var sh = new FilterShape { Type = i, HorizontalAlignment = HorizontalAlignment.Center };
            var c = new Border
            {
                Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand), Child = sh,
            };
            ToolTip.SetTip(c, typeNames[i]);
            c.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                pick(iv); Paint(); e.Handled = true;
            };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(c, i);
            row.Children.Add(c);
            cells[i] = c; shapes[i] = sh;
        }
        void Paint()
        {
            int cur = type();
            for (int i = 0; i < n; i++)
            {
                bool picked = i == cur;
                cells[i].Background = picked ? NotaPalette.AccentSoft : Brushes.Transparent;
                if (picked) cells[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
                else cells[i].BoxShadow = default;
                shapes[i].Ink = picked ? AccentBright : TextTertiary;
            }
        }
        sync = Paint;
        Paint();
        var host = new Border
        {
            Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center, Child = row,
        };
        host.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        return host;
    }

    /// <summary>The head of the band panel: the numbered toggle, the type's name (13 / 600, ink 1;
    /// ink 3 while the band is off) and the band's On switch at the right.</summary>
    internal static DockPanel Head(Border badge, Border onSwitch, Func<string> name, Func<bool> on, out Action sync)
    {
        var title = Title("");
        title.FontSize = NotaType.Value;
        title.Margin = new Thickness(6, 0, 6, 0);
        DockPanel.SetDock(badge, Dock.Left);
        DockPanel.SetDock(onSwitch, Dock.Right);
        sync = () => { title.Text = name(); title.Foreground = on() ? TextPrimary : TextTertiary; };
        sync();
        return new DockPanel { Height = 20, Children = { badge, onSwitch, title } };
    }

    /// <summary>The arrow that folds a side column to a thin strip and back: a 20px raised
    /// square, pointing right while open (fold toward the edge) and left while folded.</summary>
    internal static Border FoldButton(bool folded, Action toggle)
    {
        var btn = new Border
        {
            Width = 20, Height = 20, CornerRadius = NotaRadius.Cell, Background = NotaPalette.SurfaceHover,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new Glyph(folded ? GlyphKind.ChevronLeft : GlyphKind.ChevronRight, 7)
            {
                Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        btn.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        btn.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
            toggle(); e.Handled = true;
        };
        ToolTip.SetTip(btn, folded ? "Show the band and global settings" : "Fold the band and global settings away");
        return btn;
    }

    /// <summary>A title inside a section: 12 / 600, ink 1.</summary>
    internal static TextBlock Title(string text) => new()
    {
        Text = text, FontSize = NotaType.Body, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A row label inside a section: 10.5, ink 3 (ink 4 while inactive).</summary>
    internal static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = NotaType.RowLabel, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };
}
