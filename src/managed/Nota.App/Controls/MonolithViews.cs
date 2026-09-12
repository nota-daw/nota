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
    public IBrush Stroke { get; set; } = Brushes.Gray;
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
internal sealed class MonolithFilterCurve : Control
{
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Grid = NotaPalette.SurfaceCard;
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.Accent, 0x1E);
    private static readonly Typeface Mono = new("ui-monospace, monospace");
    private double _cut = 0.5, _reso;
    private bool _drag;

    // (cutoffNorm, resoNorm) while dragging; DragStarted/Ended bracket an automation gesture.
    public Action<double, double>? Changed;
    public event Action? DragStarted;
    public event Action? DragEnded;

    public MonolithFilterCurve() { Cursor = new Cursor(StandardCursorType.Hand); }

    public void Set(double cutoffNorm, double resoNorm) { _cut = cutoffNorm; _reso = resoNorm; }

    private void Apply(PointerEventArgs e)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        var p = e.GetPosition(this);
        _cut = Math.Clamp(p.X / w, 0, 1);
        _reso = Math.Clamp(1 - p.Y / h, 0, 1);
        Changed?.Invoke(_cut, _reso); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); _drag = true; DragStarted?.Invoke(); Apply(e); e.Pointer.Capture(this); }
    protected override void OnPointerMoved(PointerEventArgs e)
    { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = false; DragEnded?.Invoke(); e.Pointer.Capture(null); } }

    private double Y(double x, double w, double h)
    {
        double d = x / w - _cut;                     // fraction of width past cutoff
        double oct = d * 10.0;                        // ~10 octaves across the view
        double atten = oct > 0 ? 24.0 * oct : 0.0;    // 24 dB/oct roll-off
        double peak = _reso * 22.0 * Math.Exp(-(d * d) / 0.0022);
        double db = peak - atten;
        double y = h * 0.34 - db * (h * 0.016);
        return Math.Clamp(y, 2, h - 2);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));   // ensure hit-testing
        var gp = new Pen(Grid, 1);
        for (int i = 1; i < 3; i++) ctx.DrawLine(gp, new Point(w * i / 3, 0), new Point(w * i / 3, h));
        ctx.DrawLine(gp, new Point(0, h / 2), new Point(w, h / 2));

        var line = new StreamGeometry(); var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            lc.BeginFigure(new Point(0, Y(0, w, h)), false);
            fc.BeginFigure(new Point(0, h), true);
            fc.LineTo(new Point(0, Y(0, w, h)));
            for (double x = 0; x <= w; x += 2) { var p = new Point(x, Y(x, w, h)); lc.LineTo(p); fc.LineTo(p); }
            fc.LineTo(new Point(w, h));
        }
        ctx.DrawGeometry(Fill, null, fill);
        ctx.DrawGeometry(null, new Pen(Amber, 1.5), line);

        // freq · Q readout (top-right)
        double hz = 16 * Math.Pow(1250, _cut), q = _reso * 10;
        string txt = (hz >= 1000 ? $"{hz / 1000:0.0} kHz" : $"{hz:0} Hz") + $" · Q {q:0.0}";
        var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, AmberLit);
        ctx.DrawText(ft, new Point(w - ft.Width - 4, 3));

        // cutoff handle (filled, dark ring) at the resonance peak
        double hx = Math.Clamp(_cut * w, 4, w - 4), hy = Math.Clamp(Y(_cut * w, w, h), 4, h - 4);
        ctx.DrawEllipse(AmberLit, new Pen(Inset, 2), new Point(hx, hy), 4, 4);
    }
}

// ADS contour (Decay doubles as release): attack ramp → decay to sustain → hold → release.
internal sealed class MonolithEnvCurve : Control
{
    private double _a, _d, _s = 0.8;
    public IBrush Accent { get; set; } = NotaPalette.Accent;

    public void Set(double a, double d, double s) { _a = a; _d = d; _s = s; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        double top = 2, bot = h - 2;
        double xa = w * (0.04 + _a * 0.30);
        double xd = xa + w * (0.04 + _d * 0.34);
        double xs = w * 0.72;
        double ys = bot - (bot - top) * _s;
        if (xd > xs) xd = xs;
        var line = new StreamGeometry(); var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            lc.BeginFigure(new Point(0, bot), false);
            lc.LineTo(new Point(xa, top)); lc.LineTo(new Point(xd, ys)); lc.LineTo(new Point(xs, ys)); lc.LineTo(new Point(w, bot));
            fc.BeginFigure(new Point(0, bot), true);
            fc.LineTo(new Point(xa, top)); fc.LineTo(new Point(xd, ys)); fc.LineTo(new Point(xs, ys)); fc.LineTo(new Point(w, bot));
        }
        var col = ((SolidColorBrush)Accent).Color;
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x22, col.R, col.G, col.B)), null, fill);
        ctx.DrawGeometry(null, new Pen(Accent, 1.5), line);
    }
}
