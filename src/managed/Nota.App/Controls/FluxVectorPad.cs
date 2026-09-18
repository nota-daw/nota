// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Flux vector field: a draggable XY control whose four corners
// are "timbre worlds" (WARM / GLASS / MOOG / GRAIN). It is a graph window (NotaGraph): well,
// hairline, radius 4, a crosshair in the grid colour, and the corner names in the four role
// chromas — brass, steel, rose, teal — the only place on the card they appear. The brass
// dot is the vector the parameters hold; a dashed teal ring, reached by a dashed arc, is
// where Motion and React pull it right now.
// X = 0..1 left→right, Y = 0..1 top→bottom. Reports drags via ValueChanged; Begin/End frame
// an automation-write gesture on both axes. Left button only; double-click resets.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class FluxVectorPad : Control
{
    private double _x = 0.34, _y = 0.28;         // base vector (params)
    private double _gx = 0.34, _gy = 0.28;       // effective (Motion + React) ghost
    private bool _ghost;
    private bool _drag;

    public Action<double, double>? ValueChanged;
    public Action? GestureBegin;
    public Action? GestureEnd;
    /// <summary>Double-click: put the vector back on its default.</summary>
    public Action? Reset;

    public FluxVectorPad()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        DoubleTapped += (_, e) => { Reset?.Invoke(); e.Handled = true; };
    }

    public bool Dragging => _drag;

    /// <summary>Set the base vector position (from params) without firing ValueChanged.</summary>
    public void SetValue(double x, double y)
    {
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        if (Math.Abs(x - _x) < 1e-4 && Math.Abs(y - _y) < 1e-4) return;
        _x = x; _y = y; InvalidateVisual();
    }

    /// <summary>Set the effective (Motion + React) ghost; show=false hides it.</summary>
    public void SetGhost(double x, double y, bool show)
    {
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        if (show == _ghost && Math.Abs(x - _gx) < 3e-3 && Math.Abs(y - _gy) < 3e-3) return;
        _gx = x; _gy = y; _ghost = show; InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles
        if (e.ClickCount > 1) return;                                           // DoubleTapped resets
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
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); }
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
        if (r.Width < 8 || r.Height < 8) return;
        NotaGraph.Window(ctx, r);

        // crosshair.
        ctx.DrawLine(NotaGraph.GridPen, new Point(Math.Round(r.Width / 2) + 0.5, 1), new Point(Math.Round(r.Width / 2) + 0.5, r.Height - 1));
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, Math.Round(r.Height / 2) + 0.5), new Point(r.Width - 1, Math.Round(r.Height / 2) + 0.5));

        // corner worlds, in the graph chromas.
        Label(ctx, "WARM",  NotaPalette.Accent, 6, 4, false);
        Label(ctx, "GLASS", NotaPalette.Steel,  r.Width - 6, 4, true);
        Label(ctx, "MOOG",  NotaPalette.Rose,   6, r.Height - 13, false);
        Label(ctx, "GRAIN", NotaPalette.Teal,   r.Width - 6, r.Height - 13, true);

        var b = new Point(_x * r.Width, _y * r.Height);
        if (_ghost)
        {
            // A dashed arc from the vector to where it is pulled, then a dashed ring there.
            var g = new Point(_gx * r.Width, _gy * r.Height);
            var d = g - b;
            var bend = new Point((b.X + g.X) / 2 - d.Y * 0.18, (b.Y + g.Y) / 2 + d.X * 0.18);
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(b, false);
                gc.QuadraticBezierTo(bend, g);
                gc.EndFigure(false);
            }
            var dash = new Pen(NotaPalette.Teal, 1.4, new DashStyle(new double[] { 2, 2 }, 0));
            ctx.DrawGeometry(null, dash, geo);
            ctx.DrawEllipse(null, new Pen(NotaPalette.Teal, 1.5, new DashStyle(new double[] { 2, 1.5 }, 0)), g, 6, 6);
        }

        // the vector: Brass Light with a ground-coloured ring.
        ctx.DrawEllipse(NotaPalette.AccentBright, new Pen(NotaPalette.AccentSubtle, 2), b, 7, 7);
    }

    private static void Label(DrawingContext ctx, string text, IBrush ink, double x, double y, bool rightAlign)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansBold, NotaType.KnobLabel, ink);
        ctx.DrawText(ft, new Point(rightAlign ? x - ft.Width : x, y));
    }
}
