// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Small custom-drawn views for the Nota Monolith editor card: the six-position waveform
// glyph (triangle / shark-tooth / saw / square / wide pulse / narrow pulse, with Osc3's
// reverse-saw variant), the ladder-filter response curve and the ADS contour curve.
// All are display-only — the surrounding knobs/sliders drive their values.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// Six waveform shapes. osc3 == true swaps shark-tooth (index 1) for a reverse saw.
internal sealed class MonolithWaveIcon : Control
{
    private readonly int _w; private readonly bool _osc3;
    public IBrush Stroke { get; set; } = NotaPalette.TextTertiary;
    public MonolithWaveIcon(int wave, bool osc3) { _w = wave; _osc3 = osc3; Width = 14; Height = 8; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, top = 1, bot = h - 1, mid = h / 2;
        var pen = new Pen(Stroke, 1.3, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            switch (_w)
            {
                case 0: // triangle
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(w * 0.25, top)); c.LineTo(new Point(w * 0.75, bot)); c.LineTo(new Point(w, top));
                    break;
                case 1 when !_osc3: // shark-tooth (tri up, saw drop)
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(w * 0.5, top)); c.LineTo(new Point(w * 0.5, bot)); c.LineTo(new Point(w, top)); c.LineTo(new Point(w, bot));
                    break;
                case 1: // reverse saw (osc3)
                    c.BeginFigure(new Point(0, top), false);
                    c.LineTo(new Point(w, bot)); c.LineTo(new Point(w, top));
                    break;
                case 2: // saw
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(w, top)); c.LineTo(new Point(w, bot));
                    break;
                case 3: // square
                    c.BeginFigure(new Point(0, mid), false);
                    c.LineTo(new Point(0, top)); c.LineTo(new Point(w * 0.5, top)); c.LineTo(new Point(w * 0.5, bot)); c.LineTo(new Point(w, bot)); c.LineTo(new Point(w, mid));
                    break;
                case 4: // wide pulse (~30%)
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(0, top)); c.LineTo(new Point(w * 0.32, top)); c.LineTo(new Point(w * 0.32, bot)); c.LineTo(new Point(w, bot));
                    break;
                default: // narrow pulse (~12%)
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(0, top)); c.LineTo(new Point(w * 0.15, top)); c.LineTo(new Point(w * 0.15, bot)); c.LineTo(new Point(w, bot));
                    break;
            }
        }
        ctx.DrawGeometry(null, pen, g);
    }
}

// Ladder low-pass magnitude response: flat passband, resonance peak at cutoff, 24 dB/oct
// roll-off. Draggable — X sets cutoff, Y sets emphasis (via the Changed callback).
internal sealed class MonolithFilterCurve : FilterGraphBase
{
    private double _cut = 0.5, _reso;
    private bool _drag;

    // (cutoffNorm, resoNorm) while dragging; DragStarted/Ended bracket an automation gesture.
    public Action<double, double>? Changed;
    public event Action? DragStarted;
    public event Action? DragEnded;

    public MonolithFilterCurve() { Cursor = new Cursor(StandardCursorType.Hand); }

    public void Set(double cutoffNorm, double resoNorm) { _cut = cutoffNorm; _reso = resoNorm; }

    // The shared filter graph (#531) over the cutoff's own map, 16 Hz … 20 kHz.
    protected override (double Lo, double Hi) Span => (16.0, 20000.0);
    protected override (double Top, double Bottom) DbSpan => (24.0, -36.0);
    protected override double CutoffHz => 16 * Math.Pow(1250, _cut);
    protected override double ResponseDb(double hz)
    {
        double d = Math.Log(hz / 16) / Math.Log(1250) - _cut;   // fraction of width past cutoff
        double oct = d * 10.0;                                  // ~10 octaves across the view
        double atten = oct > 0 ? 24.0 * oct : 0.0;              // 24 dB/oct roll-off
        double peak = _reso * 22.0 * Math.Exp(-(d * d) / 0.0022);
        return peak - atten;
    }

    private void Apply(PointerEventArgs e)
    {
        var r = PlotRect; if (r.Width <= 0 || r.Height <= 0) return;
        var p = e.GetPosition(this);
        _cut = Math.Clamp((p.X - r.X) / r.Width, 0, 1);
        _reso = Math.Clamp(1 - (p.Y - r.Y) / r.Height, 0, 1);
        Changed?.Invoke(_cut, _reso); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; _drag = Dragging = true; DragStarted?.Invoke(); Apply(e); e.Pointer.Capture(this); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = Dragging = false; InvalidateVisual(); DragEnded?.Invoke(); e.Pointer.Capture(null); } }

    // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { if (_drag) { _drag = Dragging = false; InvalidateVisual(); DragEnded?.Invoke(); } base.OnPointerCaptureLost(e); }

    public override void Render(DrawingContext ctx)
    {
        if (Bounds.Width < 2 || Bounds.Height < 2) return;
        DrawBack(ctx);
        DrawCurve(ctx, ResponseDb);
        var at = new Point(XOf(CutoffHz), YOf(ResponseDb(CutoffHz)));
        DrawFront(ctx, at.Y);
        DrawHandle(ctx, at);
    }
}

