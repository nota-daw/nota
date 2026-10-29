// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The small drawn controls of the shared rack card (specs/040):
// - RackRangeBar: a chain's key, velocity or select range (two draggable edges), or the
//   Select-mode selector (one handle); a teal probe line marks the key under the pointer.
// - RackRuler: tick labels over the range bars.
// - RackCurvePreview: a macro's response curve with its current position (a graph window).
// - RackMapRangeBar: a macro target's min/max (min above max inverts) and where the macro
//   drives it now.
// Colours are read from the palette slots while rendering, so a theme switch repaints them.
// Left button only: a right-click bubbles to the card's menus.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class RackRangeBar : Control
{
    private const double EdgeGrab = 6;

    public int Lo { get; set; }
    public int Hi { get; set; } = 127;
    public int Max { get; init; } = 127;
    /// <summary>One handle at <see cref="Lo"/> (the selector) instead of a range.</summary>
    public bool Single { get; init; }
    public IBrush? Hue { get; set; }
    public bool Highlight { get; set; }
    public bool Dim { get; set; }
    public int Probe { get; set; } = -1;
    public Func<int, int, string>? Label { get; init; }

    /// <summary>The range (or the selector) moved under the pointer.</summary>
    public event Action<int, int>? Changed;
    /// <summary>The drag ended.</summary>
    public event Action? Committed;
    /// <summary>The pointer is over value v (−1 when it leaves).</summary>
    public event Action<int>? Hover;

    private int _dragEdge = -1; // 0 lo, 1 hi
    private bool _hot;

    public RackRangeBar()
    {
        Height = 12;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        ClipToBounds = false;
    }

    private double X(int v) => Math.Clamp(v / (double)Max, 0, 1) * Bounds.Width;
    private int V(double x) => (int)Math.Round(Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Max);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        double x = e.GetPosition(this).X;
        if (Single) _dragEdge = 0;
        else
        {
            double dl = Math.Abs(x - X(Lo)), dh = Math.Abs(x - X(Hi));
            _dragEdge = dl <= dh ? 0 : 1;
            if (Math.Min(dl, dh) > EdgeGrab && Lo == Hi) _dragEdge = x < X(Lo) ? 0 : 1;
        }
        _hot = true;
        e.Pointer.Capture(this);
        Move(x);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        double x = e.GetPosition(this).X;
        if (_dragEdge >= 0) Move(x);
        else Hover?.Invoke(V(x));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragEdge < 0) return;
        _dragEdge = -1;
        _hot = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        Committed?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragEdge < 0) return;
        _dragEdge = -1;
        _hot = false;
        InvalidateVisual();
        Committed?.Invoke();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragEdge < 0) Hover?.Invoke(-1);
    }

    private void Move(double x)
    {
        int v = V(x);
        if (Single) { Lo = Hi = v; }
        else if (_dragEdge == 0) Lo = Math.Min(v, Hi); // an edge never crosses the other
        else Hi = Math.Max(v, Lo);
        InvalidateVisual();
        Changed?.Invoke(Lo, Hi);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var well = new Rect(size).Deflate(0.5);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.Rule, 1), new RoundedRect(well, NotaRadius.ClipValue));
        var hue = Hue ?? NotaPalette.VizInk;
        if (Single)
        {
            double x = X(Lo);
            var ink = _hot ? NotaPalette.Accent : NotaPalette.VizInk; // the accent only while in the hand
            ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(x - 3, -2, 6, size.Height + 4), NotaRadius.BarValue));
        }
        else
        {
            double x0 = X(Lo), x1 = X(Hi);
            var span = new Rect(x0, 1, Math.Max(2, x1 - x0), size.Height - 2);
            var color = (hue as ISolidColorBrush)?.Color ?? Colors.Transparent;
            byte alpha = Dim ? (byte)0x22 : Highlight ? (byte)0x88 : (byte)0x4C;
            ctx.DrawRectangle(new SolidColorBrush(color, alpha / 255.0), null, span);
            var edgeInk = Dim ? NotaPalette.TextDisabled : hue;
            ctx.DrawRectangle(edgeInk, null, new Rect(x0, 1, 2, size.Height - 2));
            ctx.DrawRectangle(edgeInk, null, new Rect(Math.Max(x0, x1 - 2), 1, 2, size.Height - 2));
            if (Label is not null)
            {
                var ft = NotaGraph.AxisText(Label(Lo, Hi), Dim ? NotaPalette.TextDisabled : NotaPalette.TextPrimary, 7);
                bool right = x1 / Math.Max(1, size.Width) > 0.7;
                double tx = right ? x1 - ft.Width - 4 : x0 + 4;
                if (tx < 2) tx = 2;
                ctx.DrawText(ft, new Point(tx, (size.Height - ft.Height) / 2));
            }
        }
        if (Probe >= 0)
        {
            double px = X(Probe);
            ctx.DrawRectangle(NotaPalette.Teal, null, new Rect(px - 0.5, -1, 1, size.Height + 2));
        }
    }
}

internal sealed class RackRuler : Control
{
    public IReadOnlyList<(int Value, string Label)> Ticks { get; init; } = [];
    public int Max { get; init; } = 127;

    public RackRuler() => Height = 10;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        foreach (var (v, label) in Ticks)
        {
            var ft = NotaGraph.AxisText(label, NotaPalette.TextAxis, 7);
            double x = Math.Clamp(v / (double)Max * w - ft.Width / 2, 0, Math.Max(0, w - ft.Width));
            ctx.DrawText(ft, new Point(x, Bounds.Height - ft.Height));
        }
    }
}

internal sealed class RackCurvePreview : Control
{
    /// <summary>0 Lin, 1 Exp, 2 Log, 3 S; −1 = mixed (the macro's targets differ).</summary>
    public int Curve { get; set; }
    public double Position { get; set; }

    public RackCurvePreview()
    {
        Width = 30;
        Height = 22;
    }

    public static double Apply(int curve, double v) => curve switch
    {
        1 => v * v,
        2 => Math.Sqrt(v),
        3 => v * v * (3 - 2 * v),
        _ => v,
    };

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        NotaGraph.Window(ctx, r);
        if (Curve < 0) return;
        var plot = r.Deflate(new Thickness(3, 3, 3, 3));
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int k = 0; k <= 16; k++)
            {
                double x = k / 16.0;
                var p = new Point(plot.X + x * plot.Width, plot.Bottom - Apply(Curve, x) * plot.Height);
                if (k == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, geo);
        double pos = Math.Clamp(Position, 0, 1);
        NotaGraph.Node(ctx, new Point(plot.X + pos * plot.Width, plot.Bottom - Apply(Curve, pos) * plot.Height), true);
    }
}

internal sealed class RackMapRangeBar : Control
{
    /// <summary>The target's range as fractions of its full span; Min above Max inverts.</summary>
    public double Min { get; set; }
    public double Max { get; set; } = 1;
    /// <summary>Where the macro drives the target now, as a fraction.</summary>
    public double Current { get; set; }

    public event Action<double, double>? Changed;
    public event Action? Began;
    public event Action? Committed;

    private int _drag = -1; // 0 min, 1 max

    public RackMapRangeBar()
    {
        Height = 8;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    private double F(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        double f = F(e.GetPosition(this).X);
        _drag = Math.Abs(f - Min) <= Math.Abs(f - Max) ? 0 : 1;
        Began?.Invoke();
        e.Pointer.Capture(this);
        Move(f);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag >= 0) Move(F(e.GetPosition(this).X));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        _drag = -1;
        e.Pointer.Capture(null);
        Committed?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag < 0) return;
        _drag = -1;
        Committed?.Invoke();
    }

    private void Move(double f)
    {
        if (_drag == 0) Min = f; else Max = f; // min may pass max: the response inverts
        InvalidateVisual();
        Changed?.Invoke(Min, Max);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width <= 0) return;
        var well = new Rect(size).Deflate(0.5);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.Rule, 1), new RoundedRect(well, NotaRadius.ClipValue));
        double w = size.Width, h = size.Height;
        double a = Math.Min(Min, Max) * w, b = Math.Max(Min, Max) * w;
        var teal = NotaPalette.Teal.Color;
        ctx.DrawRectangle(new SolidColorBrush(teal, 0x55 / 255.0), null, new Rect(a, 1, Math.Max(1, b - a), h - 2));
        var handle = NotaPalette.TealBright;
        ctx.DrawRectangle(handle, null, new RoundedRect(new Rect(Min * w - 2, -3, 4, h + 6), NotaRadius.BarValue));
        ctx.DrawRectangle(handle, null, new RoundedRect(new Rect(Max * w - 2, -3, 4, h + 6), NotaRadius.BarValue));
        ctx.DrawRectangle(NotaPalette.Accent, null, new Rect(Math.Clamp(Current, 0, 1) * w - 1, -2, 2, h + 4)); // live position
    }
}
