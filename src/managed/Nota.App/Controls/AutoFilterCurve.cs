// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Auto Filter response curve (the Filter tab), on the shared filter graph of the built-in
// instruments (FilterGraphBase, #531): the window and grid, the spectrum of the signal coming
// into the filter (fed by the card from the device's scope ring) as a filled line, the cutoff
// lit on the frequency axis with a dashed rule up to the curve, and the hover marker
// (frequency, note, gain). Over that:
//  · the shaded range the modulation can walk the cutoff over (from the settings);
//  · the response at the LIVE modulated cutoff and resonance, dashed, so you see the
//    envelope / LFO / sidechain working;
//  · the response at the BASE cutoff in the accent with a light fill and a hollow handle —
//    drag anywhere: sideways for the cutoff, up / down for the resonance, recording an
//    automation gesture.
// Frequency axis == the Freq param range (30..18000 Hz) so handle-X ↔ normalized cutoff.

using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AutoFilterCurve : FilterGraphBase
{
    private static readonly IPen ModPen = new Pen(NotaPalette.TextSecondary, 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }; // dashed grey = at modulation
    private static readonly IPen RangePen = new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x8C), 1);
    private static readonly IPen RangePen2 = new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x4C), 1);
    private static readonly IBrush RangeFill = NotaPalette.Wash(NotaPalette.TextTertiary, 0x10);

    private const double FMin = 30.0, FMax = 18000.0;
    public const int FftN = FilterSpectrum.N;

    private double _fcNorm = 0.5, _res;
    private int _type;
    private float _morph;
    private bool _slope24;
    private Point _grab;
    private double _fc0, _res0;                    // the settings when the drag began
    private double _liveCut = 0.5, _liveRes;       // live modulated cutoff / resonance (normalized)
    private double _rangeLo = 0.5, _rangeHi = 0.5; // where the modulation can take the cutoff
    private bool _modAssigned;
    private readonly FilterSpectrum _input = new();

    public event Action<double>? CutoffChanged;
    public event Action<double>? ResChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public AutoFilterCurve()
    {
        MinHeight = 60;
        MinWidth = 120;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        Spectrum = _input;
    }

    protected override (double Lo, double Hi) Span => (FMin, FMax);
    protected override (double Top, double Bottom) DbSpan => (18.0, -36.0);
    protected override double CutoffHz => HzOf(_fcNorm);
    protected override double ResponseDb(double hz) => MagDb(hz, HzOf(_fcNorm), _res);

    public new bool Dragging => base.Dragging;

    // Base (user-set) filter params — drives the drawn response + the handle.
    public void Set(double freqNorm, double res, int type, float morph, bool slope24)
    {
        _fcNorm = Math.Clamp(freqNorm, 0, 1);
        _res = Math.Clamp(res, 0, 1);
        _type = type;
        _morph = morph;
        _slope24 = slope24;
        InvalidateVisual();
    }

    // Live modulation: where the cutoff / resonance are now, and the range the settings allow.
    public void SetLive(double cutNorm, double resNorm, double rangeLo, double rangeHi, bool assigned)
    {
        _liveCut = Math.Clamp(cutNorm, 0, 1);
        _liveRes = Math.Clamp(resNorm, 0, 1);
        _rangeLo = Math.Clamp(rangeLo, 0, 1);
        _rangeHi = Math.Clamp(rangeHi, 0, 1);
        _modAssigned = assigned;
        InvalidateVisual();
    }

    // The pre-filter ring (oldest → newest), FftN samples from `offset`; n < FftN clears it.
    public void FeedSpectrum(float[] buf, int offset, int n, double sampleRate)
    {
        if (_input.Feed(buf, offset, n, sampleRate, FMin, FMax)) InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        base.Dragging = true;
        _grab = e.GetPosition(this); _fc0 = _fcNorm; _res0 = _res;
        GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (base.Dragging) Apply(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!base.Dragging) return;
        base.Dragging = false; InvalidateVisual();
        GestureEnd?.Invoke();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (base.Dragging) { base.Dragging = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    // The drag moves the settings from where they were when grabbed, so a click changes nothing.
    // The handle sits on the curve's peak, which isn't linear in resonance (and doesn't move at
    // all for band-pass), so resonance can't come from the pointer's height as it used to: a grab
    // at 10 % jumped it to ~80 %, and dragging down to where a low peak would be still left it
    // above 50 % — the resonance looked stuck (#260). Up raises it, the full range over 80 % of
    // the height; sideways moves the cutoff with the pointer, as the handle's x is linear in it.
    private void Apply(Point p)
    {
        var r = PlotRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        _fcNorm = Math.Clamp(_fc0 + (p.X - _grab.X) / r.Width, 0, 1);
        _res = Math.Clamp(_res0 + (_grab.Y - p.Y) / (r.Height * 0.8), 0, 1);
        CutoffChanged?.Invoke(_fcNorm);
        ResChanged?.Invoke(_res);
        InvalidateVisual();
    }

    private static double HzOf(double norm) => FMin * Math.Pow(FMax / FMin, norm);

    private double Mag(int type, double f, double fc, double res)
    {
        double q = 0.5 + res * 14.5;
        double w = f / fc, w2 = w * w;
        var den = Math.Sqrt((1 - w2) * (1 - w2) + w / q * (w / q));
        if (den < 1e-9) den = 1e-9;
        var m = type switch { 0 => 1.0 / den, 1 => w / q / den, 2 => w2 / den, _ => Math.Abs(1 - w2) / den };
        if (_slope24) m *= m;
        return m;
    }

    private double MagDb(double f, double fc, double res)
    {
        var m = Mag(_type, f, fc, res);
        if (_morph > 0f) m = m * (1 - _morph) + Mag((_type + 1) & 3, f, fc, res) * _morph;
        return 20.0 * Math.Log10(Math.Max(m, 1e-5));
    }

    public override void Render(DrawingContext ctx)
    {
        var r = PlotRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        DrawBack(ctx);

        // The range the modulation can walk the cutoff over (two grey rules).
        if (_modAssigned && _rangeHi - _rangeLo > 0.004)
        {
            double x0 = XOf(HzOf(_rangeLo)), x1 = XOf(HzOf(_rangeHi));
            ctx.FillRectangle(RangeFill, new Rect(x0, r.Y + 1, x1 - x0, r.Height - 2));
            ctx.DrawLine(RangePen, new Point(x0, r.Y + 1), new Point(x0, r.Bottom - 1));
            ctx.DrawLine(RangePen2, new Point(x1, r.Y + 1), new Point(x1, r.Bottom - 1));
        }

        // The response at the live modulated cutoff / resonance, dashed, under the base curve.
        bool moving = Math.Abs(_liveCut - _fcNorm) > 0.004 || Math.Abs(_liveRes - _res) > 0.01;
        if (moving)
        {
            double fc = HzOf(_liveCut), res = _liveRes;
            int n = (int)Math.Clamp(r.Width / 1.5, 24, 420);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i < n; i++)
                {
                    double x = r.X + r.Width * i / (n - 1);
                    var p = new Point(x, YOf(MagDb(HzAt(x), fc, res)));
                    if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, ModPen, geo);
        }

        // The base response with its handle, which sits on the drawn curve at the cutoff (a
        // notch's bottom, which only the exact cutoff reaches, would pull it off the line).
        double baseFc = HzOf(_fcNorm);
        DrawCurve(ctx, f => MagDb(f, baseFc, _res));
        double cx = XOf(baseFc), dx = Math.Max(1.5, r.Width / 420);
        double cy = Math.Min(YOf(MagDb(HzAt(cx - dx), baseFc, _res)), YOf(MagDb(HzAt(cx + dx), baseFc, _res)));
        DrawFront(ctx, cy);
        DrawHandle(ctx, new Point(cx, cy));

        if (moving || _modAssigned)
            NotaGraph.Legend(ctx, r.Right - 5, r.Y + 3, ("modulated", NotaPalette.TextSecondary, NotaGraph.Mark.Dashed));
    }
}
