// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The pan control outside devices (track header, Session mixer; style guide, spec 038): a
// small 14px knob, its arc growing from 12 o'clock toward the panned side, with the value
// ("C", "12L", "50R", 11 / 400 ink 1) just right of it. Grey at rest, orange only while
// under the pointer or in the hand. Drag vertically to set; double-click the knob centres
// it, a double-click on the value (or ⌥-click) types one. Fires GestureBegin/End so callers can bracket an automation write,
// like MiniFader / the gauge Knob.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class PanBar : Control, IInlineReadout
{
    private static readonly IBrush Track = NotaPalette.KnobTrack;
    private static readonly IBrush Arc = NotaPalette.TextTertiary;     // at rest: neutral
    private static readonly IBrush ArcLive = NotaPalette.Accent;       // under the pointer or in the hand
    private static readonly IBrush Pointer = NotaPalette.TextPrimary;
    private static readonly IBrush TextBrush = NotaPalette.TextPrimary;
    private static readonly Typeface NumFace = NotaFonts.Sans;

    public const double KnobD = 14;
    private const double StartDeg = 135, SweepDeg = 270, Stroke = 2;

    private double _pan;   // -1 (full left) .. +1 (full right), 0 = centre
    private bool _drag;

    public event Action<double>? PanChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    // The value right of the knob is its read-out: a double-click there types (ValueEntry).
    bool IInlineReadout.ReadoutAt(Point p) => p.X >= KnobD + 2;

    /// <summary>True while dragging — callers skip external value writes then.</summary>
    public bool Dragging => _drag;

    public double Pan
    {
        get => _pan;
        set { _pan = Math.Clamp(value, -1, 1); InvalidateVisual(); }
    }

    public PanBar(double pan = 0)
    {
        _pan = Math.Clamp(pan, -1, 1);
        Height = 16;
        Width = 40;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);   // almanac: vertical drag for every value control
    }

    private double _lastY, _raw;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { GestureBegin?.Invoke(); Apply(0); GestureEnd?.Invoke(); e.Handled = true; return; }   // centre
        _drag = true;
        _lastY = e.GetPosition(this).Y;
        _raw = _pan;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y, dy = _lastY - y;   // up = pan right; full sweep over ~140px
        _lastY = y;
        if (dy == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        _raw = Math.Clamp(_raw + dy / (fine ? 700.0 : 70.0), -1, 1);   // accumulate, so the centre detent cannot trap the drag
        Apply(_raw);
    }

    // Under the pointer (or in the hand) the control is live and takes the accent; at rest
    // its value is neutral ink — the accent marks a knob or fader only while focused or dragged.
    private bool _hover;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
        InvalidateVisual();   // a drag that ended off the control leaves no hover to repaint it
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    /// <summary>The bar as a typeable value ("30L", "C", "12R" on its own 50 scale); double-click centres.</summary>
    internal ValueField EntryField(string name)
        => new(name, () => (_pan + 1) / 2, n => Label(Detent(n * 2 - 1)), n => Apply(n * 2 - 1),
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), 0.5);

    private static double Detent(double p) { p = Math.Clamp(p, -1, 1); return Math.Abs(p) < 0.04 ? 0 : p; }   // small centre detent

    private void Apply(double p)
    {
        p = Detent(p);
        if (Math.Abs(p - _pan) < 1e-4) return;
        _pan = p;
        InvalidateVisual();
        PanChanged?.Invoke(_pan);
    }

    public override void Render(DrawingContext ctx)
    {
        double h = Bounds.Height;
        if (Bounds.Width <= 0 || h <= 0) return;
        // A clear fill over the whole bounds, so the gaps around the arc still take presses.
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double r = KnobD / 2 - Stroke / 2, cx = KnobD / 2, cy = h / 2;
        ctx.DrawGeometry(null, new Pen(Track, Stroke, lineCap: PenLineCap.Round), ArcGeo(cx, cy, r, StartDeg, SweepDeg));
        double mid = StartDeg + SweepDeg / 2, at = mid + _pan * SweepDeg / 2;
        if (Math.Abs(_pan) > 1e-3)
            ctx.DrawGeometry(null, new Pen(_drag || _hover ? ArcLive : Arc, Stroke, lineCap: PenLineCap.Round),
                ArcGeo(cx, cy, r, Math.Min(mid, at), Math.Abs(at - mid)));
        double rad = at * Math.PI / 180;
        ctx.DrawLine(new Pen(Pointer, 1.6, lineCap: PenLineCap.Round), new Point(cx, cy),
            new Point(cx + Math.Cos(rad) * (r - 0.5), cy + Math.Sin(rad) * (r - 0.5)));
        var text = new NotaFormattedText(Label(_pan), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, NumFace, NotaType.Caption, TextBrush, this);
        ctx.DrawText(text, new Point(KnobD + 5, (h - text.Height) / 2));
    }

    private static StreamGeometry ArcGeo(double cx, double cy, double r, double fromDeg, double sweepDeg)
    {
        double a0 = fromDeg * Math.PI / 180, a1 = (fromDeg + sweepDeg) * Math.PI / 180;
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(cx + Math.Cos(a0) * r, cy + Math.Sin(a0) * r), false);
        c.ArcTo(new Point(cx + Math.Cos(a1) * r, cy + Math.Sin(a1) * r), new Size(r, r), 0,
            sweepDeg > 180, SweepDirection.Clockwise);
        c.EndFigure(false);
        return g;
    }

    // Bipolar readout: "C" at centre, else 1..50 with an L/R suffix.
    private static string Label(double p)
    {
        int amt = (int)Math.Round(Math.Abs(p) * 50);
        return amt == 0 ? "C" : (p < 0 ? amt + "L" : amt + "R");
    }
}
