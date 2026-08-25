// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A rotary knob for device / instrument parameters (mockup style): a faint ring,
// a brass value arc over a 270° sweep, and a pointer line. Drag vertically to
// change (up = increase). Same public API as MiniFader (Value / max / Accent /
// ValueChanged / GestureBegin / GestureEnd) so it drops into the plugin editors.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class Knob : Control
{
    private static readonly IBrush Groove = NotaPalette.BgSunken;     // dark track groove
    private static readonly IBrush TrackArc = NotaPalette.BorderStrong;   // Brush.BorderStrong (neutral value arc)
    private static readonly IBrush Brass = NotaPalette.Accent;      // Brush.Accent
    private static readonly IBrush Pointer = NotaPalette.TextPrimary;    // Brush.TextPrimary

    private const double StartDeg = 135.0;   // down-left
    private const double SweepDeg = 270.0;   // clockwise to down-right

    /// <summary>Brass value arc instead of neutral grey.</summary>
    public bool Accent { get; init; }

    /// <summary>Overrides the value-arc colour (e.g. teal for modulation knobs).</summary>
    public IBrush? ArcColor { get; init; }

    /// <summary>Value restored on double-click (NaN = no reset).</summary>
    public double Default { get; set; } = double.NaN;

    private readonly double _max;
    private double _value;
    private bool _drag;
    private double _lastY;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    /// <summary>True while the user is dragging — live-follow refreshers skip it then.</summary>
    public bool Dragging => _drag;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, _max); InvalidateVisual(); }
    }

    public Knob(double value = 1.0, double max = 1.0)
    {
        _max = max <= 0 ? 1.0 : max;
        _value = Math.Clamp(value, 0, _max);
        Width = 38;
        Height = 38;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Left button only — let right-click bubble to a context menu (e.g. CV modulate).
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Double-click restores the parameter's default.
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            double d = Math.Clamp(Default, 0, _max);
            if (Math.Abs(d - _value) > 1e-6)
            {
                GestureBegin?.Invoke();
                _value = d;
                InvalidateVisual();
                ValueChanged?.Invoke(_value);
                GestureEnd?.Invoke();
            }
            e.Handled = true;
            return;
        }
        _drag = true;
        _lastY = e.GetPosition(this).Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y;
        double dy = _lastY - y;                       // up = increase
        _lastY = y;
        if (dy == 0) return;
        double v = Math.Clamp(_value + dy / 140.0 * _max, 0, _max);
        if (Math.Abs(v - _value) < 1e-6) return;
        _value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // Transparent fill makes the whole knob area hit-testable (not just the arc).
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        double cx = w / 2, cy = h / 2;
        double r = Math.Min(w, h) / 2 - 3;
        if (r <= 1) return;
        double frac = _max > 0 ? _value / _max : 0;

        var arcBrush = ArcColor ?? (Accent ? Brass : TrackArc);
        double aw = Math.Max(2.5, r * 0.22);   // arc thickness scales with size (≈ mockup 4/17)

        // Dark 270° track groove, then the value arc over it (gauge style).
        DrawArc(ctx, cx, cy, r, 0, 1, new Pen(Groove, aw, lineCap: PenLineCap.Round));
        if (frac > 0.001) DrawArc(ctx, cx, cy, r, 0, frac, new Pen(arcBrush, aw, lineCap: PenLineCap.Round));

        // Pointer line from the centre out toward the rim at the value angle.
        double a = (StartDeg + frac * SweepDeg) * Math.PI / 180.0;
        double dx = Math.Cos(a), dy = Math.Sin(a);
        ctx.DrawLine(new Pen(Pointer, 2, lineCap: PenLineCap.Round),
            new Point(cx, cy),
            new Point(cx + dx * r * 0.66, cy + dy * r * 0.66));
    }

    // Draws the sweep arc from fraction t0..t1 as a short polyline (robust, cheap).
    private static void DrawArc(DrawingContext ctx, double cx, double cy, double r, double t0, double t1, IPen pen)
    {
        const int seg = 40;
        int i0 = (int)Math.Floor(t0 * seg), i1 = (int)Math.Ceiling(t1 * seg);
        Point? prev = null;
        for (int i = i0; i <= i1; i++)
        {
            double t = Math.Clamp((double)i / seg, t0, t1);
            double a = (StartDeg + t * SweepDeg) * Math.PI / 180.0;
            var p = new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            if (prev is { } pp) ctx.DrawLine(pen, pp, p);
            prev = p;
        }
    }
}
