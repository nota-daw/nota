// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The pieces EQ-8 and Dynamic EQ-8 share (style guide, spec 038): the numbered band toggle
// (orange tint when the band is on, the same control as the band's On switch), one band's chip
// in the slim row over the graph, the strip of six filter shapes that picks a band's type, the head
// of the band panel, and the arrow that folds the side panel.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class EqBandUi
{
    internal const double ChipH = 20, BadgeD = 18;

    // Each EQ card's view state by (track, effect slot): whether its right panel is folded and
    // which band is picked, so a rebuild of the chain (reselecting the track, adding a return)
    // keeps both. The chain view remaps it with the cards' other state when devices move or go
    // (RemapViews), so it stays with its device rather than its slot.
    private static readonly Dictionary<(int Track, int Slot), (bool Folded, int Band)> Views = new();
    internal static bool IsFolded(int track, int slot) => Views.TryGetValue((track, slot), out var v) && v.Folded;
    internal static void SetFolded(int track, int slot, bool folded) => Views[(track, slot)] = (folded, PickedBand(track, slot) ?? -1);
    internal static int? PickedBand(int track, int slot) => Views.TryGetValue((track, slot), out var v) && v.Band >= 0 ? v.Band : null;
    internal static void SetPickedBand(int track, int slot, int band) => Views[(track, slot)] = (IsFolded(track, slot), band);
    internal static void RemapViews(int track, Func<int, int> map)
    {
        var mine = Views.Where(kv => kv.Key.Track == track).ToList();
        foreach (var kv in mine) Views.Remove(kv.Key);
        foreach (var kv in mine) { int ni = map(kv.Key.Slot); if (ni >= 0) Views[(track, ni)] = kv.Value; }
    }

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

        // The shapes are drawn on a 22 × 12 grid; a smaller control (the band chips) scales them.
        public override void Render(DrawingContext ctx)
        {
            double k = Math.Min(Bounds.Width / 22, Bounds.Height / 12);
            if (k <= 0) return;
            using var _ = ctx.PushTransform(Matrix.CreateScale(k, k));
            ctx.DrawGeometry(null, new Pen(_ink, 1.4 / Math.Max(k, 0.75), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Shapes[_type]);
        }
    }

    /// <summary>The numbered band toggle: a square in the soft orange tint with an orange number
    /// while the band is on, the knob-track grey with an ink-3 number while it is off.</summary>
    internal static Border Badge(int band, Func<bool> on, Action toggle, out Action sync)
    {
        var num = new TextBlock
        {
            Text = (band + 1).ToString(), FontSize = NotaType.DeviceCaption, FontWeight = FontWeight.SemiBold,
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

    /// <summary>One band's chip in the slim row over the graph (the classic EQ Eight strip): its
    /// number, its filter shape and its frequency on one line (10; ink 2, the number ink 1).
    /// The band being edited has an orange outline and an orange number; a band that is off
    /// draws everything in ink 4. A click picks the band, a double-click switches it on or off;
    /// (Dynamic EQ-8) the band's dynamics activity runs as a 2px orange bar along the bottom.</summary>
    internal static Border Chip(int band, Func<int> type, Func<bool> on, Func<string> freq, Func<bool> selected, Action select, Action toggle,
        out Action sync, Func<double>? activity = null)
    {
        var num = new TextBlock
        {
            Text = (band + 1).ToString(), FontFamily = NotaFonts.UiFamily, FontSize = NotaType.Axis, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The shape sits centred in the space between the number and the frequency.
        var shape = new FilterShape { Width = 15, Height = 8, Margin = new Thickness(2, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var hz = new TextBlock
        {
            FontFamily = NotaFonts.UiFamily, FontSize = NotaType.Axis, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.None,
        };
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        line.Children.Add(num);
        Grid.SetColumn(shape, 1); line.Children.Add(shape);
        Grid.SetColumn(hz, 2); line.Children.Add(hz);
        var bar = new Border
        {
            Height = 2, CornerRadius = NotaRadius.Fine, Background = NotaPalette.Accent, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1), IsHitTestVisible = false,
        };
        var chip = new Border
        {
            Height = ChipH, CornerRadius = NotaRadius.Cell, Background = NotaPalette.Well, BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 0, 3, 0), Cursor = new Cursor(StandardCursorType.Hand), ClipToBounds = true,
            Child = activity is null ? line : new Panel { Children = { line, bar } },
        };
        void Paint()
        {
            bool o = on(), sel = selected();
            chip.BorderBrush = sel ? Brass : Brushes.Transparent;
            num.Foreground = !o ? TextDisabled : sel ? AccentBright : TextPrimary;
            shape.Type = type();
            shape.Ink = o ? TextSecondary : TextDisabled;
            hz.Text = freq();
            hz.Foreground = o ? TextSecondary : TextDisabled;
            if (activity is not null) bar.Width = Math.Clamp(activity(), 0, 1) * Math.Max(0, line.Bounds.Width);
        }
        chip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) toggle();
            select(); e.Handled = true;
        };
        sync = Paint;
        Paint();
        return chip;
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
        title.FontSize = NotaType.DeviceValue;
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
        ToolTip.SetTip(btn, folded ? "Show the band settings" : "Fold the band settings away");
        return btn;
    }

    /// <summary>A title inside a section: 12 / 600, ink 1.</summary>
    internal static TextBlock Title(string text) => new()
    {
        Text = text, FontSize = NotaType.DeviceBody, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A row label inside a section: 10.5, ink 3 (ink 4 while inactive).</summary>
    internal static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = NotaType.RowLabel, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };
}
