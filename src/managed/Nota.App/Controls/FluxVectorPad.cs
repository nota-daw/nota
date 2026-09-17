// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Flux vector pad: a draggable XY control whose four corners
// are "timbre worlds" (WARM / GLASS / MOOG / GRAIN). The background is a four-corner radial
// gradient (one glow per world) over the sunken inset, matching the mockup. A bright dot is
// the current vector; a teal dashed "ghost" shows where Motion + React are pulling it.
// X = 0..1 left→right, Y = 0..1 top→bottom. Reports drags via ValueChanged; Begin/End frame
// an automation-write gesture on both axes.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class FluxVectorPad : Control
{
    private static Color Inset   => NotaPalette.BgSunken.Color;
    private static Color GridCol  => NotaPalette.PadGrid.Color;
    private static Color BorderCol => NotaPalette.GraphBorder.Color;
    private static Color Warm    => NotaPalette.Accent.Color;   // top-left
    private static Color Glass   => NotaPalette.InkColor("#6D8FB5");   // top-right
    private static Color Moog    => NotaPalette.InkColor("#C4756A");   // bottom-left
    private static Color Grain   => NotaPalette.InkColor("#7E8A6A");   // bottom-right
    private static Color DotCol  => NotaPalette.AccentBright.Color;
    private static Color Teal    => NotaPalette.Teal.Color;
    private static readonly IBrush Muted  = NotaPalette.TextTertiary;

    private double _x = 0.34, _y = 0.28;         // base vector (params)
    private double _gx = 0.34, _gy = 0.28;       // effective (Motion + React) ghost
    private bool _ghost;
    private bool _drag;

    public Action<double, double>? ValueChanged;
    public Action? GestureBegin;
    public Action? GestureEnd;

    public FluxVectorPad() { ClipToBounds = true; }

    /// <summary>Set the base vector position (from params) without firing ValueChanged.</summary>
    public void SetValue(double x, double y)
    {
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        if (Math.Abs(x - _x) < 1e-4 && Math.Abs(y - _y) < 1e-4) return;
        _x = x; _y = y; InvalidateVisual();
    }

    /// <summary>Set the effective (Motion + React) ghost dot; show=false hides it.</summary>
    public void SetGhost(double x, double y, bool show)
    {
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        if (show == _ghost && Math.Abs(x - _gx) < 3e-3 && Math.Abs(y - _gy) < 3e-3) return;
        _gx = x; _gy = y; _ghost = show; InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _drag = true; GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        Apply(e.GetPosition(this));
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag) Apply(e.GetPosition(this));
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) { _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); }
    }

    private void Apply(Point p)
    {
        double w = Math.Max(1, Bounds.Width), h = Math.Max(1, Bounds.Height);
        double x = Math.Clamp(p.X / w, 0, 1), y = Math.Clamp(p.Y / h, 0, 1);
        _x = x; _y = y; InvalidateVisual();
        ValueChanged?.Invoke(x, y);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        // base + four-corner radial glows.
        ctx.FillRectangle(new SolidColorBrush(Inset), r, 8);

        // crosshair grid.
        var grid = new Pen(new SolidColorBrush(GridCol), 1);
        ctx.DrawLine(grid, new Point(r.Width / 2, 0), new Point(r.Width / 2, r.Height));
        ctx.DrawLine(grid, new Point(0, r.Height / 2), new Point(r.Width, r.Height / 2));

        // corner labels.
        Label(ctx, "WARM",  Warm,  6, 4, false);
        Label(ctx, "GLASS", Glass, r.Width - 6, 4, true);
        Label(ctx, "MOOG",  Moog,  6, r.Height - 15, false);
        Label(ctx, "GRAIN", Grain, r.Width - 6, r.Height - 15, true);

        // React/Motion ghost: dashed line from base to effective + hollow teal dot.
        var bx = _x * r.Width; var by = _y * r.Height;
        if (_ghost)
        {
            var gx = _gx * r.Width; var gy = _gy * r.Height;
            var dash = new Pen(new SolidColorBrush(Teal, 0.75), 1, new DashStyle(new double[] { 3, 3 }, 0));
            ctx.DrawLine(dash, new Point(bx, by), new Point(gx, gy));
            var ring = new Pen(new SolidColorBrush(Teal, 0.85), 1.3, new DashStyle(new double[] { 3, 2 }, 0));
            ctx.DrawEllipse(null, ring, new Point(gx, gy), 6, 6);
        }

        // current vector dot (glow + bright core).
        ctx.DrawEllipse(new SolidColorBrush(DotCol, 0.28), null, new Point(bx, by), 11, 11);
        ctx.DrawEllipse(new SolidColorBrush(DotCol), new Pen(NotaPalette.BgSunken, 1), new Point(bx, by), 6, 6);

        // 1px inner border to match the mockup pad frame.
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(BorderCol), 1), r, 8, 8);
    }


    private void Label(DrawingContext ctx, string text, Color col, double x, double y, bool rightAlign)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansBold, 9, new SolidColorBrush(col));
        double ox = rightAlign ? x - ft.Width : x;
        ctx.DrawText(ft, new Point(ox, y));
    }
}
