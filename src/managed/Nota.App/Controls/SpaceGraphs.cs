// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Your audio in the Nota Reverb and Nota Delay graphs (#377). Both devices append a level
// history to their scope (dsp::LevelHistory: input dB and wet dB at 4 ms a point for 6.1 s,
// then the latest 2048 wet samples), and the #531 card graphs lay it over the settings:
//
//   ReverbTailView  — the measured tail after the latest hit, under the set decay line
//   ReverbToneView  — decay time across frequency (HF damp), the cut filters, the tail spectrum
//   DelayTaps       — the measured echoes after the latest hit, behind the repeat stems
//   DelayBandCurve  — the echo spectrum behind the feedback filter (FilterSpectrum.Feed)
//
// This file holds the shared helpers and the Tone graph; the views draw what the card hands
// them and own no engine state.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>Where the level history sits in a scope, and the helpers the graphs share.</summary>
internal static class SpaceDraw
{
    public const int Points = 1536, SpecN = 2048, Floats = 2 * Points + SpecN;
    public const double PointSec = 0.004;
    public const double FMin = 20, FMax = 20000;

    public static IBrush CutWash => NotaPalette.Wash(NotaPalette.TextPrimary, 0x0A);

    /// <summary>The latest hit of the input history at <paramref name="off"/>: a jump of 9 dB within
    /// 12 ms, above −50 dB, walked back to the start of its rise. The point index, or −1 for none.</summary>
    public static int LatestHit(float[] s, int off)
    {
        if (s.Length < off + Points) return -1;
        for (int k = Points - 1; k >= 3; k--)
        {
            float v = s[off + k];
            if (v > -50 && v - s[off + k - 3] > 9 && v >= s[off + k - 1])
            {
                while (k > 3 && s[off + k - 1] - s[off + k - 4] > 9) k--;
                return k;
            }
        }
        return -1;
    }

    /// <summary>A history ring from point <paramref name="from"/> on, as (seconds after it, dB)
    /// pairs, one per <paramref name="stride"/> points with each keeping its loudest point.</summary>
    public static List<(double Sec, double Db)> Since(float[] s, int off, int from, int stride = 2)
    {
        var list = new List<(double, double)>((Points - from) / Math.Max(1, stride) + 1);
        for (int k = Math.Max(0, from); k < Points; k += stride)
        {
            float m = -120;
            for (int j = k; j < k + stride && j < Points; j++) m = Math.Max(m, s[off + j]);
            list.Add(((k - from) * PointSec, m));
        }
        return list;
    }

    public static double LogX(double hz, double x0, double x1)
        => x0 + Math.Log(Math.Clamp(hz, FMin, FMax) / FMin) / Math.Log(FMax / FMin) * (x1 - x0);
    public static double HzAt(double x, double x0, double x1)
        => FMin * Math.Pow(FMax / FMin, Math.Clamp((x - x0) / Math.Max(1, x1 - x0), 0, 1));

    /// <summary>Frequency grid (100 Hz · 1 kHz · 10 kHz) with its labels along the floor.</summary>
    public static void FreqGrid(DrawingContext ctx, Rect r, double x0, double x1, double top, double bot)
    {
        foreach (double hz in new[] { 100.0, 1000, 10000 })
        {
            double x = LogX(hz, x0, x1);
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, top), new Point(x, bot));
            var ft = NotaGraph.AxisText(hz >= 1000 ? NotaNum.F($"{hz / 1000:0}k") : NotaNum.F($"{hz:0}"));
            ctx.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom - ft.Height - 3));
        }
    }
}

/// <summary>The spectrum of the scope's wet samples, smoothed so it falls slowly.</summary>
internal sealed class WetSpectrum
{
    private const int N = SpaceDraw.SpecN;
    private readonly double[] _re = new double[N], _im = new double[N], _win = new double[N], _db = new double[N / 2];
    private double[] _shown = Array.Empty<double>();
    private readonly double _winSum;
    public bool Live { get; private set; }

    public WetSpectrum()
    {
        double s = 0;
        for (int i = 0; i < N; i++) { _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)); s += _win[i]; }
        _winSum = s;
        Array.Fill(_db, -120);
    }

    /// <summary>Analyse the latest wet samples (at <paramref name="off"/> in the scope).</summary>
    public void Feed(float[] scope, int off)
    {
        double e = 0;
        for (int i = 0; i < N; i++) { double v = scope[off + i]; e += v * v; _re[i] = v * _win[i]; _im[i] = 0; }
        Live = e > 1e-9;
        if (!Live) { for (int k = 0; k < N / 2; k++) _db[k] = Math.Max(-120, _db[k] - 6); return; }
        Fft.Transform(_re, _im);
        for (int k = 0; k < N / 2; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * 2 / _winSum;
            double db = mag > 1e-7 ? 20 * Math.Log10(mag) : -140;
            // Rise at once, fall at about 40 dB a second at the card's refresh rate.
            _db[k] = db > _db[k] ? db : Math.Max(db, _db[k] - 1.2);
        }
    }

    /// <summary>One dB value per column between x0 and x1 (log frequency), each the loudest bin in it.</summary>
    public double[] Columns(int cols, double sampleRate)
    {
        if (_shown.Length != cols) _shown = new double[cols];
        double binHz = sampleRate / N;
        for (int c = 0; c < cols; c++)
        {
            double fa = SpaceDraw.FMin * Math.Pow(SpaceDraw.FMax / SpaceDraw.FMin, (double)c / cols);
            double fb = SpaceDraw.FMin * Math.Pow(SpaceDraw.FMax / SpaceDraw.FMin, (double)(c + 1) / cols);
            int a = (int)Math.Ceiling(fa / binHz), b = (int)Math.Floor(fb / binHz);
            if (b < a)
            {
                // No bin inside a narrow low column: interpolate between the bins either side.
                double pos = Math.Clamp(Math.Sqrt(fa * fb) / binHz, 1, N / 2 - 2);
                int i0 = (int)pos;
                _shown[c] = _db[i0] + (_db[i0 + 1] - _db[i0]) * (pos - i0);
                continue;
            }
            a = Math.Clamp(a, 1, N / 2 - 1); b = Math.Clamp(b, a, N / 2 - 1);
            double m = -140;
            for (int k = a; k <= b; k++) m = Math.Max(m, _db[k]);
            _shown[c] = m;
        }
        return _shown;
    }
}

/// <summary>A frequency graph with a low-cut and a high-cut handle, each over its own parameter range
/// (log Hz). Drag a handle to move it, double-click to reset it; ⌥-click a handle to type its value
/// (the owner supplies the fields, specs/035 — without them ⌥-click resets, as a double-click does).</summary>
internal abstract class CutHandleGraph : Control, IValueHandles
{
    protected double LowHz = 20, HighHz = 20000;
    protected bool LowOn = true, HighOn = true;
    private int _drag, _hover;   // 0 none, 1 low, 2 high

    public double LowLo { get; init; } = 20;
    public double LowHi { get; init; } = 1000;
    public double HighLo { get; init; } = 1000;
    public double HighHi { get; init; } = 20000;

    public event Action<int, double>? Changed;   // handle (0 low / 1 high), new normalised value
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int>? Reset;
    public bool Dragging => _drag != 0;

    protected CutHandleGraph()
    {
        ClipToBounds = true;
        MinHeight = 40;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    // The left inset leaves the scale labels clear of the plot and the low-cut handle.
    protected (double x0, double x1, double top, double bot) Geo() => (30, Bounds.Width - 6, NotaGraph.PlotTop, Bounds.Height - NotaGraph.PlotFloor);
    protected bool Hot(int handle) => _drag == handle || (_drag == 0 && _hover == handle);

    private int Nearest(double x)
    {
        var (x0, x1, _, _) = Geo();
        return Math.Abs(x - SpaceDraw.LogX(LowHz, x0, x1)) <= Math.Abs(x - SpaceDraw.LogX(HighHz, x0, x1)) ? 1 : 2;
    }

    // Typed entry: handle 0 = low cut, 1 = high cut (the nearer one, as a drag picks it).
    public int HandleAt(Point p) => Nearest(p.X) - 1;
    public Rect HandleRect(int handle)
    {
        var (x0, x1, top, _) = Geo();
        return ValueEntry.Near(new Point(SpaceDraw.LogX(handle == 0 ? LowHz : HighHz, x0, x1), top), Bounds.Size);
    }

    private void Apply(PointerEventArgs e)
    {
        var (x0, x1, _, _) = Geo();
        if (x1 <= x0 || _drag == 0) return;
        double hz = SpaceDraw.HzAt(e.GetPosition(this).X, x0, x1);
        double norm = _drag == 1
            ? Math.Log(Math.Clamp(hz, LowLo, LowHi) / LowLo) / Math.Log(LowHi / LowLo)
            : Math.Log(Math.Clamp(hz, HighLo, HighHi) / HighLo) / Math.Log(HighHi / HighLo);
        Changed?.Invoke(_drag - 1, Math.Clamp(norm, 0, 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int h = Nearest(e.GetPosition(this).X);
        // A reset takes no drag; the press's implicit capture goes too, or the next click
        // anywhere would still come here.
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) { Reset?.Invoke(h - 1); e.Pointer.Capture(null); e.Handled = true; return; }
        _drag = h; GestureBegin?.Invoke(h - 1); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != 0) { Apply(e); return; }
        int h = Nearest(e.GetPosition(this).X);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    { base.OnPointerExited(e); if (_hover != 0) { _hover = 0; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (_drag == 0) return;
        GestureEnd?.Invoke(_drag - 1); _drag = 0; InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag != 0) { GestureEnd?.Invoke(_drag - 1); _drag = 0; InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    /// <summary>Shade what each cut removes and draw its handle (a line, a node at <paramref name="nodeY"/>).</summary>
    protected void DrawCuts(DrawingContext ctx, double top, double bot, Func<double, double> nodeY)
    {
        var (x0, x1, _, _) = Geo();
        double xl = SpaceDraw.LogX(LowHz, x0, x1), xh = SpaceDraw.LogX(HighHz, x0, x1);
        if (LowOn) ctx.FillRectangle(SpaceDraw.CutWash, new Rect(x0, top, Math.Max(0, xl - x0), bot - top));
        if (HighOn) ctx.FillRectangle(SpaceDraw.CutWash, new Rect(xh, top, Math.Max(0, x1 - xh), bot - top));
        foreach (var (x, h, on) in new[] { (xl, 1, LowOn), (xh, 2, HighOn) })
        {
            var ink = Hot(h) ? NotaPalette.Accent : on ? NotaPalette.TextSecondary : NotaPalette.TextDisabled;
            ctx.DrawLine(new Pen(ink, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(x, top), new Point(x, bot));
            NotaGraph.Node(ctx, new Point(x, nodeY(x)), Hot(h), Hot(h) ? null : ink);
        }
    }
}

// ============================================================================
// Reverb — Tone: decay across frequency
// ============================================================================
/// <summary>How long the tail rings at each frequency (the curve, seconds up the left): HF Damp makes
/// the highs die sooner. The low-cut and high-cut filters on the tail are the shaded edges with their
/// handles, and the tail's live spectrum runs underneath (a grey line, dB). Drag a handle to move the
/// cut, double-click it to reset.</summary>
internal sealed class ReverbToneView : CutHandleGraph
{
    // Mirrors Reverb.h: comb tuning (mean), the algorithm scale, the size range.
    private const double MeanComb = 1378.0, MaxSize = 1.6;
    private static readonly double[] AlgoScale = { 1.0, 0.62, 0.82, 0.9 };

    private readonly WetSpectrum _spec = new();
    private double _rt60 = 2, _damp = 0.5, _size = 0.6, _sr = 48000;
    private int _algo;
    private bool _freeze, _specLive;

    public ReverbToneView() { LowLo = 20; LowHi = 1000; HighLo = 1000; HighHi = 20000; }

    public void Set(double rt60, double damp, double sizeNorm, int algo, bool freeze, double lowHz, double highHz, bool lowOn,
        float[] scope, int specOff, bool has, double sampleRate)
    {
        _rt60 = rt60; _damp = damp; _size = sizeNorm; _algo = Math.Clamp(algo, 0, 3); _freeze = freeze;
        LowHz = lowHz; HighHz = highHz; LowOn = lowOn; HighOn = true;
        _sr = sampleRate > 1000 ? sampleRate : 48000;
        _specLive = has && scope.Length >= specOff + SpaceDraw.SpecN;
        if (_specLive) _spec.Feed(scope, specOff);
        InvalidateVisual();
    }

    /// <summary>RT60 at <paramref name="hz"/>: the comb loop loses 60 dB in RT60 at DC; the damping
    /// low-pass in the loop takes a little more of the highs on every pass.</summary>
    public double DecayAt(double hz)
    {
        if (_freeze) return double.PositiveInfinity;
        double span = (0.55 + _size * (MaxSize - 0.55)) * AlgoScale[_algo];
        double loopSec = MeanComb * span / 44100.0;
        double fb = Math.Min(0.995, Math.Pow(10, -3 * loopSec / Math.Max(0.05, _rt60)));
        double d1 = _damp * 0.4, d2 = 1 - d1, wv = 2 * Math.PI * hz / _sr;
        double mag = d2 / Math.Sqrt(1 - 2 * d1 * Math.Cos(wv) + d1 * d1);
        double g = fb * mag;
        return g >= 1 ? double.PositiveInfinity : -3 * loopSec / Math.Log10(g);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        var (x0, x1, top, bot) = Geo();
        double span = bot - top;
        if (span < 8 || x1 <= x0) return;
        SpaceDraw.FreqGrid(ctx, rect, x0, x1, top, bot);

        // Seconds scale: a round number above the longest decay.
        double longest = _freeze ? 1 : DecayAt(SpaceDraw.FMin);
        double full = 0.5;
        foreach (double s in new[] { 0.5, 1, 2, 4, 8, 12, 16 }) { full = s; if (s >= longest * 1.12) break; }
        double YT(double sec) => bot - Math.Clamp(sec / full, 0, 1) * span;
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, YT(full / 2)), new Point(x1, YT(full / 2)));

        // The tail's spectrum, −96 … 0 dB.
        if (_specLive || _spec.Live)
        {
            int cols = Math.Max(2, (int)(x1 - x0));
            var db = _spec.Columns(cols, _sr);
            var pts = new List<Point>(cols);
            for (int c = 0; c < cols; c++) pts.Add(new Point(x0 + (double)c / (cols - 1) * (x1 - x0), bot - Math.Clamp((db[c] + 96) / 96, 0, 1) * span));
            CompDraw.Polyline(ctx, NotaGraph.SecondaryPen(NotaPalette.TextTertiary, 1.1), pts);
        }

        double CurveY(double x) => _freeze ? top + 2 : YT(DecayAt(SpaceDraw.HzAt(x, x0, x1)));
        DrawCuts(ctx, top, bot, CurveY);

        var decay = new List<Point>();
        for (double x = x0; x <= x1 + 0.1; x += 2) decay.Add(new Point(Math.Min(x, x1), CurveY(Math.Min(x, x1))));
        CompDraw.Polyline(ctx, _freeze ? new Pen(NotaPalette.AccentBright, NotaGraph.PrimaryWidth) : NotaGraph.PrimaryPen, decay);

        NotaGraph.Title(ctx, rect, Loc.T("control.space_graphs.decay_by_frequency"));
        NotaGraph.Legend(ctx, x1 - 2, 4, ("decay", NotaPalette.VizInk, NotaGraph.Mark.Line), (Loc.T("control.space_graphs.tail_spectrum"), NotaPalette.TextTertiary, NotaGraph.Mark.Line));
        if (_freeze) NotaGraph.EdgeLabels(ctx, rect, false, (top + 8, "held"));
        else NotaGraph.EdgeLabels(ctx, rect, false, (YT(full) + 6, Secs(full)), (YT(full / 2), Secs(full / 2)));
    }

    private static string Secs(double s) => s < 1 ? NotaNum.F($"{s * 1000:0} ms") : NotaNum.F($"{s:0.#} s");
}
