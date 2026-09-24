// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Phaser views (device kind 24) — the pictures on the Phaser card:
//   PhaserResponseView — the live all-pass response of both channels (L brass, R teal) over 20 Hz …
//                        20 kHz, −36 … +12 dB, the left corner dashed and every notch ticked on
//                        the top edge. Drag left / right moves Center, up / down the Feedback;
//                        double-click resets both.
//   PhaserSweepView    — the corner frequency of both channels over two LFO cycles (20 Hz … 20 kHz,
//                        log), a running head and the dots where each channel is now.
// PhaserMath mirrors Phaser.h so the picture matches the sound. The NOTCH / NULL bars reuse FlangerBar.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class PhaserMath
{
    public static readonly string[] Waves = { "Sine", "Tri", "Saw" };
    // Sync divisions, slowest → fastest (Phaser::kDivBeats / kDivNames).
    public static readonly double[] DivBeats = { 16, 8, 4, 2, 1.5, 1, 2.0 / 3, 0.5, 0.25 };
    public static readonly string[] DivNames = { "4/1", "2/1", "1/1", "1/2", "1/4D", "1/4", "1/4T", "1/8", "1/16" };
    public static readonly int[] StageCounts = { 2, 4, 6, 8, 12 };
    public const double FbMax = 0.95, DepthOct = 3;

    public static int WaveIndex(double v) => Math.Clamp((int)Math.Round(v * 2), 0, 2);
    public static int DivIndex(double v) => Math.Clamp((int)Math.Round(v * (DivBeats.Length - 1)), 0, DivBeats.Length - 1);
    public static double DivNorm(int i) => (double)Math.Clamp(i, 0, DivBeats.Length - 1) / (DivBeats.Length - 1);
    public static int StageIndex(double v) => Math.Clamp((int)Math.Round(v * (StageCounts.Length - 1)), 0, StageCounts.Length - 1);
    public static int StagesOf(double v) => StageCounts[StageIndex(v)];
    public static double FreeHz(double v) => 0.02 * Math.Pow(400, Math.Clamp(v, 0, 1));
    public static double CenterHz(double v) => 50 * Math.Pow(100, Math.Clamp(v, 0, 1));
    public static double Fb(double v) => (Math.Clamp(v, 0, 1) - 0.5) * 2 * FbMax;
    public static double FbNorm(double fb) => Math.Clamp(0.5 + fb / (2 * FbMax), 0, 1);
    public static (double Dry, double Wet) DryWet(double mix) => (Math.Min(1, 2 * (1 - mix)), Math.Min(1, 2 * mix));
    public static int NotchCount(int n, double fb) => fb < 0 ? n / 2 - 1 : n / 2;

    /// <summary>The LFO in [−1, 1] at phase <paramref name="p"/> (cycles, wraps) — all shapes start at 0 rising.</summary>
    public static double Lfo(int wave, double p)
    {
        double f = p - Math.Floor(p);
        if (wave == 0) return Math.Sin(2 * Math.PI * f);
        if (wave == 1) return f < 0.25 ? 4 * f : f < 0.75 ? 2 - 4 * f : 4 * f - 4;
        double g = f + 0.5;
        return 2 * (g - Math.Floor(g)) - 1;
    }

    public static double Fc(double centerHz, double depth, int wave, double p) => centerHz * Math.Pow(2, DepthOct * depth * Lfo(wave, p));

    /// <summary>|dry ± wet·H(θ)| / (dry + wet), H = e^{−jθ} / (1 − fb·e^{−jθ}); the wet polarity
    /// follows the feedback's sign (Phaser.h).</summary>
    public static double Mag(double dry, double wet, double fb, double th)
    {
        double c = Math.Cos(th), s = Math.Sin(th), w = fb < 0 ? -wet : wet;
        double nr = w * c, ni = -w * s, dr = 1 - fb * c, di = fb * s, den = dr * dr + di * di;
        double re = dry + (nr * dr + ni * di) / den, im = (ni * dr - nr * di) / den;
        return Math.Sqrt(re * re + im * im) / Math.Max(dry + wet, 0.001);
    }

    /// <summary>The chain's phase lag at <paramref name="f"/>: N · 2·atan(tan(πf/sr) / tan(πfc/sr)).</summary>
    public static double Lag(double f, double fc, int n, double sr)
    {
        double ny = sr * 0.5 * 0.9999;
        return n * 2 * Math.Atan(Math.Tan(Math.PI * Math.Min(f, ny) / sr) / Math.Max(1e-9, Math.Tan(Math.PI * Math.Min(fc, ny) / sr)));
    }

    /// <summary>Where the notches sit for a corner <paramref name="fc"/> (lag = odd multiples of π, or even with negative feedback).</summary>
    public static List<double> Notches(double fc, int n, double fb, double sr)
    {
        var r = new List<double>();
        double tc = Math.Tan(Math.PI * Math.Min(fc, 0.49999 * sr) / sr);
        if (fb < 0) for (int k = 1; k < n / 2; k++) r.Add(sr / Math.PI * Math.Atan(tc * Math.Tan(Math.PI * k / n)));
        else for (int k = 0; k < n / 2; k++) r.Add(sr / Math.PI * Math.Atan(tc * Math.Tan(Math.PI * (2 * k + 1) / (2 * n))));
        return r;
    }

    public static double FirstNotch(double fc, int n, double fb, double sr)
    {
        var l = Notches(fc, n, fb, sr);
        return l.Count > 0 ? l[0] : 0;
    }

    public static double NullDb(double mix, double fb)
    {
        var (dry, wet) = DryWet(mix);
        return 20 * Math.Log10(Math.Max(1e-4, Mag(dry, wet, fb, fb < 0 ? 2 * Math.PI : Math.PI)));
    }

    public static double PeakDb(double mix, double fb)
    {
        var (dry, wet) = DryWet(mix);
        return 20 * Math.Log10(Math.Max(1e-4, Mag(dry, wet, fb, fb >= 0 ? 0 : Math.PI)));
    }

    public static string HzF(double f) => f >= 10000 ? NotaNum.F($"{f / 1000:0.0}k") : f >= 1000 ? NotaNum.F($"{f / 1000:0.00}k") : NotaNum.F($"{f:0}");
}

internal sealed class PhaserResponseView : Control
{
    private double _fcL = 800, _fcR = 800, _mix = 0.5, _fb = 0.4, _sr = 48000;
    private int _stages = 4;
    private bool _on = true, _drag;
    private Point _start;
    private double _c0, _f0;

    /// <summary>Center and Feedback (normalized 0..1) at the start of a drag.</summary>
    public Func<(double Center, double Feedback)>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double, double>? Changed;
    public event Action? ResetRequested;

    public PhaserResponseView() { ClipToBounds = true; MinHeight = 40; Cursor = new Cursor(StandardCursorType.SizeAll); }

    public void Set(double fcL, double fcR, int stages, double mix, double fb, double sr, bool on)
    {
        _fcL = fcL; _fcR = fcR; _stages = stages; _mix = mix; _fb = fb; _sr = sr > 0 ? sr : 48000; _on = on;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(); e.Handled = true; return; }
        var v = Value?.Invoke() ?? (0.602, 0.711);
        _c0 = v.Center; _f0 = v.Feedback; _start = e.GetPosition(this); _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        var p = e.GetPosition(this);
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double k = fine ? 0.2 : 1;
        // The corner follows the hand: a share of the 3-decade axis is the same log share of
        // Center's 2-decade range.
        double c = Math.Clamp(_c0 + (p.X - _start.X) * k / Math.Max(120, Bounds.Width) * 1.5, 0, 1);
        double f = Math.Clamp(_f0 - (p.Y - _start.Y) * k / Math.Max(60, Bounds.Height), 0, 1);
        Changed?.Invoke(c, f);
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
        double X(double f) => Math.Log(Math.Max(f, 1e-3) / 20) / Math.Log(1000) * w;
        double Y(double db) => h * (0.07 + (12 - Math.Clamp(db, -36, 12)) / 48 * 0.86);

        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, Y(0)), new Point(w, Y(0)));
        foreach (double db in new[] { 12.0, -24.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(0, Y(db)), new Point(w, Y(db)));
        foreach (double hz in new[] { 100.0, 1000.0, 10000.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(X(hz), 0), new Point(X(hz), h));

        if (_on && _fcL is > 20 and < 20000)
            ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(X(_fcL), 0), new Point(X(_fcL), h));

        // R first so L reads on top where they meet.
        Curve(_fcR, _on ? NotaPalette.TealBright : NotaPalette.TextAxis, 1.2, 0.75);
        Curve(_fcL, _on ? NotaPalette.Accent : NotaPalette.TextAxis, 1.6, 1);

        if (_on)
        {
            Ticks(_fcR, NotaPalette.TealBright);
            Ticks(_fcL, NotaPalette.AccentBright);
        }

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

        void Ticks(double fc, IBrush ink)
        {
            foreach (double f in PhaserMath.Notches(fc, _stages, _fb, _sr))
                if (f is > 20 and < 20000) ctx.FillRectangle(ink, new Rect(Math.Round(X(f)), 0, 1, 5));
        }
        void Curve(double fc, IBrush ink, double width, double opacity)
        {
            var (dry, wet) = PhaserMath.DryWet(_mix);
            double Db(double g) => 20 * Math.Log10(Math.Max(g, 1e-4));
            int n = Math.Max(64, (int)w);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i <= n; i++)
                {
                    double x = i * w / n, f = 20 * Math.Pow(1000, (double)i / n);
                    double y = _on ? Y(Db(PhaserMath.Mag(dry, wet, _fb, PhaserMath.Lag(f, fc, _stages, _sr)))) : Y(0);
                    if (i == 0) g.BeginFigure(new Point(x, y), false); else g.LineTo(new Point(x, y));
                }
                g.EndFigure(false);
            }
            using (ctx.PushOpacity(opacity)) ctx.DrawGeometry(null, NotaGraph.SecondaryPen(ink, width), geo);
        }
    }
}

internal sealed class PhaserSweepView : Control
{
    private double _center = 800, _depth = 0.7, _off = 0.25, _head, _fcL = 800, _fcR = 800, _periodMs = 2500;
    private int _wave;
    private bool _on = true;

    public PhaserSweepView() { ClipToBounds = true; Height = 34; }

    public void Set(double centerHz, double depth, int wave, double offCycles, double head, double fcL, double fcR, double periodMs, bool on)
    {
        _center = centerHz; _depth = depth; _wave = wave; _off = offCycles; _head = head; _fcL = fcL; _fcR = fcR; _periodMs = periodMs; _on = on;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double X(double p) => p / 2 * w;
        double Y(double f) => h * (34 - Math.Clamp(Math.Log(Math.Max(f, 1e-3) / 20) / Math.Log(1000), 0, 1) * 28) / 40;
        ctx.DrawLine(new Pen(NotaPalette.GraphBorder, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(X(1), 0), new Point(X(1), h));

        Curve(_off, _on ? NotaPalette.TealBright : NotaPalette.TextAxis, 1.2, 0.75);
        Curve(0, _on ? NotaPalette.Accent : NotaPalette.TextAxis, 1.4, 1);

        var tl = NotaGraph.AxisText("fc 20k", size: 6);
        ctx.DrawText(tl, new Point(5, 1));
        var bl = NotaGraph.AxisText("20", size: 6);
        ctx.DrawText(bl, new Point(5, h - 1 - bl.Height));
        string span = "2 × " + (_periodMs >= 1000 ? NotaNum.F($"{_periodMs / 1000:0.0} s") : NotaNum.F($"{_periodMs:0} ms"));
        var br = NotaGraph.AxisText(span, size: 6);
        ctx.DrawText(br, new Point(w - 5 - br.Width, h - 1 - br.Height));

        if (_on)
        {
            double hx = X(Math.Clamp(_head, 0, 2));
            ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1), new Point(hx, 0), new Point(hx, h));
            DrawDot(new Point(hx, Y(_fcR)), NotaPalette.TealBright);
            DrawDot(new Point(hx, Y(_fcL)), NotaPalette.AccentBright);
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
                    double f = _on ? PhaserMath.Fc(_center, _depth, _wave, p + off) : _center;
                    var pt = new Point(X(p), Y(f));
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            using (ctx.PushOpacity(opacity)) ctx.DrawGeometry(null, NotaGraph.SecondaryPen(ink, width), geo);
        }
    }
}
