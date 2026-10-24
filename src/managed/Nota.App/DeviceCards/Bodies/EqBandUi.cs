// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The pieces EQ-8 and Dynamic EQ-8 share in their EQ Eight layout (style guide, spec 038):
// the numbered band toggle (orange tint when the band is on, the same control as the band's
// On switch), the filter-shape chooser (a drawn shape and a chevron that opens the six types),
// one band's cell in the row under the graph, and the arrow that folds a side column.

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
    internal const double CellH = 26, BadgeD = 18, ColumnW = 186, FoldedW = 42;

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

    /// <summary>The filter-shape chooser: the band's shape and a chevron; a click opens the six
    /// types (each with its shape) and picks one.</summary>
    internal static Border ShapePicker(string[] typeNames, Func<int> type, Func<bool> on, Action<int> setType, Action? onOpen, out Action sync)
    {
        var shape = new FilterShape();
        var chevron = new Glyph(GlyphKind.ChevronDown, 7) { VerticalAlignment = VerticalAlignment.Center };
        var host = new Border
        {
            Background = Brushes.Transparent, CornerRadius = NotaRadius.Cell, Padding = new Thickness(4, 0), Height = BadgeD,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { shape, chevron } },
        };
        void Paint()
        {
            bool o = on();
            shape.Type = type();
            shape.Ink = o ? TextSecondary : TextDisabled;
            chevron.Foreground = o ? TextTertiary : TextDisabled;
        }
        host.PointerEntered += (_, _) => host.Background = NotaPalette.Well;
        host.PointerExited += (_, _) => host.Background = Brushes.Transparent;
        host.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            onOpen?.Invoke();
            var fly = new MenuFlyout();
            for (int i = 0; i < typeNames.Length; i++)
            {
                int iv = i;
                var mi = new MenuItem
                {
                    Header = typeNames[i], ToggleType = MenuItemToggleType.Radio, IsChecked = type() == i,
                    Icon = new FilterShape { Type = i, Ink = TextSecondary },
                };
                mi.Click += (_, _) => { setType(iv); Paint(); };
                fly.Items.Add(mi);
            }
            fly.ShowAt(host);
        };
        ToolTip.SetTip(host, "Filter type");
        sync = Paint;
        Paint();
        return host;
    }

    /// <summary>One band's cell in the row under the graph: the toggle at the left, the shape
    /// chooser at the right, an orange outline while it is the band being edited, and (Dynamic
    /// EQ-8) the band's dynamics activity as a 2px orange bar along the bottom. A click on the
    /// cell itself picks the band.</summary>
    internal static Border Cell(Border badge, Border picker, Func<bool> selected, Action select, out Action sync, Func<double>? activity = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(badge);
        picker.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(picker, 1);
        grid.Children.Add(picker);
        var bar = new Border { Height = 2, CornerRadius = NotaRadius.Fine, Background = Brass, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, 1) };
        var cell = new Border
        {
            Height = CellH, CornerRadius = NotaRadius.Field, Background = NotaPalette.Well, BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 0, 2, 0), Cursor = new Cursor(StandardCursorType.Hand), ClipToBounds = true,
            Child = activity is null ? grid : new Panel { Children = { grid, bar } },
        };
        if (activity is not null) bar.Margin = new Thickness(1, 0, 1, 1);
        void Paint()
        {
            cell.BorderBrush = selected() ? Brass : Brushes.Transparent;
            if (activity is not null) bar.Width = Math.Clamp(activity(), 0, 1) * Math.Max(0, cell.Bounds.Width - 8);
        }
        cell.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
            select(); e.Handled = true;
        };
        sync = Paint;
        Paint();
        return cell;
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
