// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Flanger views (device kind 23) — the pictures on the Flanger card:
//   FlangerCombView  — the live comb response of both channels (L brass, R teal) over 20 Hz …
//                      20 kHz, −36 … +12 dB, with the first notch marked. Where the teeth get
//                      denser than a pixel the curve draws each column's max…min, so the comb
//                      stays honest at high frequencies. Drag left / right moves the notch (Delay),
//                      up / down the Feedback; double-click resets both.
//   FlangerSweepView — the delay τ of both channels over two LFO cycles, a running head and
//                      the dots where each channel is now.
//   FlangerBar       — a thin range / level bar (the NOTCH sweep and the NULL depth).
// FlangerMath mirrors Flanger.h so the picture matches the sound.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class FlangerMath
{
    public static readonly string[] Waves = { "Sine", "Tri", "Saw" };
    public static readonly string[] WaveNames = { "SINE", "TRIANGLE", "SAW" };
    // Sync divisions, slowest → fastest (Flanger::kDivBeats / kDivNames).
    public static readonly double[] DivBeats = { 8, 4, 2, 1.5, 1, 2.0 / 3, 0.5, 1.0 / 3, 0.25 };
    public static readonly string[] DivNames = { "2/1", "1/1", "1/2", "1/4D", "1/4", "1/4T", "1/8", "1/8T", "1/16" };
    public const double DepthScale = 0.92, FbMax = 0.95;

    public static int WaveIndex(double v) => Math.Clamp((int)Math.Round(v * 2), 0, 2);
    public static int DivIndex(double v) => Math.Clamp((int)Math.Round(v * (DivBeats.Length - 1)), 0, DivBeats.Length - 1);
    public static double DivNorm(int i) => (double)Math.Clamp(i, 0, DivBeats.Length - 1) / (DivBeats.Length - 1);
    public static double FreeHz(double v) => 0.02 * Math.Pow(400, Math.Clamp(v, 0, 1));
    public static double BaseMs(double v) => 0.1 * Math.Pow(80, Math.Clamp(v, 0, 1));
    public static double Fb(double v) => (Math.Clamp(v, 0, 1) - 0.5) * 2 * FbMax;
    public static double FbNorm(double fb) => Math.Clamp(0.5 + fb / (2 * FbMax), 0, 1);
    public static (double Dry, double Wet) DryWet(double mix) => (Math.Min(1, 2 * (1 - mix)), Math.Min(1, 2 * mix));

    /// <summary>The LFO in [−1, 1] at phase <paramref name="p"/> (cycles, wraps) — all shapes start at 0 rising.</summary>
    public static double Lfo(int wave, double p)
    {
        double f = p - Math.Floor(p);
        if (wave == 0) return Math.Sin(2 * Math.PI * f);
        if (wave == 1) return f < 0.25 ? 4 * f : f < 0.75 ? 2 - 4 * f : 4 * f - 4;
        double g = f + 0.5;
        return 2 * (g - Math.Floor(g)) - 1;
    }

    public static double Tau(double baseMs, double depth, int wave, double p) => baseMs * (1 + DepthScale * depth * Lfo(wave, p));

    /// <summary>|dry ± wet·H(θ)| / (dry + wet), H = e^{−jθ} / (1 − fb·e^{−jθ}), θ = ωτ; the wet
    /// polarity follows the feedback's sign (Flanger.h).</summary>
    public static double Mag(double dry, double wet, double fb, double th)
    {
        double c = Math.Cos(th), s = Math.Sin(th), wetAbs = wet;
        if (fb < 0) wet = -wet;
        double nr = wet * c, ni = -wet * s, dr = 1 - fb * c, di = fb * s, den = dr * dr + di * di;
        double re = dry + (nr * dr + ni * di) / den, im = (ni * dr - nr * di) / den;
        return Math.Sqrt(re * re + im * im) / Math.Max(dry + wetAbs, 0.001);
    }

    /// <summary>The first notch's phase θ in (0, 2π] and its depth (linear) — π for positive feedback, 2π for negative.</summary>
    public static (double Theta, double Mag) Notch(double mix, double fb)
    {
        var (dry, wet) = DryWet(mix);
        double best = Math.PI, bm = double.MaxValue;
        for (int i = 1; i <= 180; i++)
        {
            double th = i / 180.0 * 2 * Math.PI, m = Mag(dry, wet, fb, th);
            if (m < bm) { bm = m; best = th; }
        }
        return (best, bm);
    }

    public static double PeakDb(double mix, double fb)
    {
        var (dry, wet) = DryWet(mix);
        return 20 * Math.Log10(Math.Max(1e-4, Mag(dry, wet, fb, fb >= 0 ? 0 : Math.PI)));
    }

    public static string Ms(double t) => t >= 10 ? NotaNum.F($"{t:0.0}") : NotaNum.F($"{t:0.00}");
    public static string HzF(double f) => f >= 10000 ? NotaNum.F($"{f / 1000:0.0}k") : f >= 1000 ? NotaNum.F($"{f / 1000:0.00}k") : NotaNum.F($"{f:0}");
}

internal sealed class FlangerCombView : Control
{
    private double _tauL = 2.5, _tauR = 2.5, _mix = 0.5, _fb = 0.7;
    private bool _on = true, _drag;
    private Point _start;
    private double _d0, _f0;

    /// <summary>Delay and Feedback (normalized 0..1) at the start of a drag.</summary>
    public Func<(double Delay, double Feedback)>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double, double>? Changed;
    public event Action? ResetRequested;

    public FlangerCombView() { ClipToBounds = true; MinHeight = 40; Cursor = new Cursor(StandardCursorType.SizeAll); }

    public void Set(double tauL, double tauR, double mix, double fb, bool on)
    {
        _tauL = tauL; _tauR = tauR; _mix = mix; _fb = fb; _on = on;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(); e.Handled = true; return; }
        var v = Value?.Invoke() ?? (0.735, 0.868);
        _d0 = v.Delay; _f0 = v.Feedback; _start = e.GetPosition(this); _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        var p = e.GetPosition(this);
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double k = fine ? 0.2 : 1;
        // The notch follows the hand: it sits at θ / 2πτ, so moving it right by a share of the
        // 3-decade axis shortens τ by the same log share of Delay's 80:1 range.
        double d = Math.Clamp(_d0 - (p.X - _start.X) * k / Math.Max(120, Bounds.Width) * Math.Log(1000) / Math.Log(80), 0, 1);
        double f = Math.Clamp(_f0 - (p.Y - _start.Y) * k / Math.Max(60, Bounds.Height), 0, 1);
        Changed?.Invoke(d, f);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double X(double f) => Math.Log(f / 20) / Math.Log(1000) * w;
        double Y(double db) => h * (0.07 + (12 - Math.Clamp(db, -36, 12)) / 48 * 0.86);

        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, Y(0)), new Point(w, Y(0)));
        foreach (double db in new[] { 12.0, -24.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(0, Y(db)), new Point(w, Y(db)));
        foreach (double hz in new[] { 100.0, 1000.0, 10000.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(X(hz), 0), new Point(X(hz), h));

        if (_on)
        {
            var (th, _) = FlangerMath.Notch(_mix, _fb);
            double nf = th / (2 * Math.PI) / (_tauL / 1000);
            if (nf is > 20 and < 20000)
                ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(X(nf), 0), new Point(X(nf), h));
        }

        // R first so L reads on top where they meet.
        Curve(_tauR, _on ? NotaPalette.TealBright : NotaPalette.TextAxis, 1.2, 0.75);
        Curve(_tauL, _on ? NotaPalette.Accent : NotaPalette.TextAxis, 1.6, 1);

        void Label(string s, double x, double y)
        {
            var ft = NotaGraph.AxisText(s);
            ctx.DrawText(ft, new Point(x, y - ft.Height / 2));
        }
        Label("+12", 4, Y(12) + 6);
        Label("0 dB", 4, Y(0) - 6);
        Label("−24", 4, Y(-24) - 6);
        foreach (var (hz, s) in new[] { (100.0, "100"), (1000.0, "1k"), (10000.0, "10k") })
        {
            var ft = NotaGraph.AxisText(s);
            ctx.DrawText(ft, new Point(X(hz) + 3, h - 3 - ft.Height));
        }

        void Curve(double tauMs, IBrush ink, double width, double opacity)
        {
            double t = tauMs / 1000, k = 2 * Math.PI * t;
            var (dry, wet) = FlangerMath.DryWet(_mix);
            double Db(double g) => 20 * Math.Log10(Math.Max(g, 1e-4));
            int n = Math.Max(64, (int)w);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i <= n; i++)
                {
                    double x = i * w / n, f0 = 20 * Math.Pow(1000, (double)i / n);
                    if (!_on) { Pt(g, i, x, Y(0)); continue; }
                    double f1 = 20 * Math.Pow(1000, (double)(i + 1) / n), span = k * (f1 - f0);
                    if (span <= 1.2 || i == n) { Pt(g, i, x, Y(Db(FlangerMath.Mag(dry, wet, _fb, k * f0)))); continue; }
                    double mn = double.MaxValue, mx = double.MinValue;
                    int m = Math.Min(64, (int)Math.Ceiling(span / 0.4));
                    for (int j = 0; j <= m; j++)
                    {
                        double v = FlangerMath.Mag(dry, wet, _fb, k * (f0 + (f1 - f0) * j / m));
                        if (v < mn) mn = v; if (v > mx) mx = v;
                    }
                    Pt(g, i, x, Y(Db(mx)));
                    g.LineTo(new Point(x, Y(Db(mn))));
                }
                g.EndFigure(false);
            }
            using (ctx.PushOpacity(opacity)) ctx.DrawGeometry(null, NotaGraph.SecondaryPen(ink, width), geo);
        }
        static void Pt(StreamGeometryContext g, int i, double x, double y)
        {
            if (i == 0) g.BeginFigure(new Point(x, y), false); else g.LineTo(new Point(x, y));
        }
    }
}

internal sealed class FlangerSweepView : Control
{
    private double _base = 2.5, _depth = 0.8, _off = 0.25, _head, _tauL = 2.5, _tauR = 2.5, _periodMs = 5000;
    private int _wave = 1;
    private bool _on = true;

    public FlangerSweepView() { ClipToBounds = true; Height = 34; }

    public void Set(double baseMs, double depth, int wave, double offCycles, double head, double tauL, double tauR, double periodMs, bool on)
    {
        _base = baseMs; _depth = depth; _wave = wave; _off = offCycles; _head = head; _tauL = tauL; _tauR = tauR; _periodMs = periodMs; _on = on;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = _base * 1.95;
        double X(double p) => p / 2 * w;
        double Y(double t) => h * (34 - Math.Clamp(t / top, 0, 1.2) * 28) / 40;
        ctx.DrawLine(new Pen(NotaPalette.GraphBorder, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(X(1), 0), new Point(X(1), h));

        Curve(_off, _on ? NotaPalette.TealBright : NotaPalette.TextAxis, 1.2, 0.75);
        Curve(0, _on ? NotaPalette.Accent : NotaPalette.TextAxis, 1.4, 1);

        var tl = NotaGraph.AxisText("τ " + FlangerMath.Ms(top) + " ms", size: 6);
        ctx.DrawText(tl, new Point(5, 1));
        var bl = NotaGraph.AxisText("0 ms", size: 6);
        ctx.DrawText(bl, new Point(5, h - 1 - bl.Height));
        string span = "2 × " + (_periodMs >= 1000 ? NotaNum.F($"{_periodMs / 1000:0.0} s") : NotaNum.F($"{_periodMs:0} ms"));
        var br = NotaGraph.AxisText(span, size: 6);
        ctx.DrawText(br, new Point(w - 5 - br.Width, h - 1 - br.Height));

        if (_on)
        {
            double hx = X(Math.Clamp(_head, 0, 2));
            ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1), new Point(hx, 0), new Point(hx, h));
            DrawDot(new Point(hx, Y(_tauR)), NotaPalette.TealBright);
            DrawDot(new Point(hx, Y(_tauL)), NotaPalette.AccentBright);
        }

        void DrawDot(Point p, IBrush ink) => ctx.DrawEllipse(ink, new Pen(NotaGraph.Ground, 2), p, 3, 3);
        void Curve(double off, IBrush ink, double width, double opacity)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                int n = Math.Max(64, (int)w);
                for (int i = 0; i <= n; i++)
                {
                    double p = 2.0 * i / n;
                    double t = _on ? FlangerMath.Tau(_base, _depth, _wave, p + off) : _base;
                    var pt = new Point(X(p), Y(t));
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            using (ctx.PushOpacity(opacity)) ctx.DrawGeometry(null, NotaGraph.SecondaryPen(ink, width), geo);
        }
    }
}

internal sealed class FlangerBar : Control
{
    private double _l, _w;
    public IBrush Ink { get; set; } = NotaPalette.BorderStrong;

    public FlangerBar() { Height = 4; }

    public void Set(double left, double width)
    {
        left = Math.Clamp(left, 0, 1); width = Math.Clamp(width, 0, 1 - left);
        if (Math.Abs(left - _l) < 1e-4 && Math.Abs(width - _w) < 1e-4) return;
        _l = left; _w = width; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new RoundedRect(new Rect(0, 0, w, h), h / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, r);
        if (_w > 0)
            using (ctx.PushClip(r)) ctx.DrawRectangle(Ink, null, new Rect(w * _l, 0, Math.Max(1, w * _w), h));
    }
}
