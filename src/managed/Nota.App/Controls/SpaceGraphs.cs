// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The live graphs of Nota Reverb and Nota Delay. Each draws the level history the engine
// appends to the device scope (dsp::LevelHistory: input dB and wet dB at 4 ms a point for
// 6.1 s, then the latest 2048 wet samples) with the settings drawn over it, so what you see
// is your audio and what the settings make of it:
//
//   ReverbSpaceView  — the tail of your audio, and the RT60 guide falling from its last peak
//   ReverbToneView   — decay time across frequency (HF Damp), the cut filters, the tail spectrum
//   DelayEchoView    — the echoes of your audio on the beat grid, and where the repeats will land
//   DelayRepeatsView — each repeat's response through the loop filter, and the echo spectrum
//
// They draw what the card hands them and own no engine state.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Where the level history sits in a scope, and the helpers the four graphs share.</summary>
internal static class SpaceDraw
{
    public const int Points = 1536, SpecN = 2048, Floats = 2 * Points + SpecN;
    public const double PointSec = 0.004;
    public const double HistorySec = Points * PointSec;
    public const double FMin = 20, FMax = 20000;

    public static IBrush InputWash => NotaPalette.Wash(NotaPalette.TextTertiary, 0x40);
    public static IBrush CutWash => NotaPalette.Wash(NotaPalette.TextPrimary, 0x0A);

    /// <summary>The newest <paramref name="n"/> points of a ring (oldest first at <paramref name="off"/>),
    /// one point per column, each column keeping its loudest point.</summary>
    public static List<Point> Trace(float[] s, int off, int n, double x0, double x1, Func<double, double> y)
    {
        n = Math.Clamp(n, 2, Points);
        int cols = Math.Max(2, (int)(x1 - x0));
        var pts = new List<Point>(cols);
        int start = off + Points - n;
        for (int c = 0; c < cols; c++)
        {
            int a = start + (int)((long)c * n / cols), b = start + (int)((long)(c + 1) * n / cols);
            if (b <= a) b = a + 1;
            double m = -120;
            for (int k = a; k < b && k < off + Points; k++) m = Math.Max(m, s[k]);
            pts.Add(new Point(x0 + (double)c / (cols - 1) * (x1 - x0), y(m)));
        }
        return pts;
    }

    /// <summary>A trace drawn as a filled area down to <paramref name="floor"/>.</summary>
    public static void Area(DrawingContext ctx, IBrush fill, List<Point> pts, double floor)
    {
        if (pts.Count < 2) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(pts[0].X, floor), true);
            foreach (var p in pts) g.LineTo(p);
            g.LineTo(new Point(pts[^1].X, floor));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(fill, null, geo);
    }

    /// <summary>The loudest point in the newest <paramref name="n"/> points of a ring.</summary>
    public static float Max(float[] s, int off, int n)
    {
        float m = -120;
        for (int k = off + Points - Math.Clamp(n, 1, Points); k < off + Points; k++) m = Math.Max(m, s[k]);
        return m;
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

    public static FormattedText Text(string t, IBrush ink) => CompDraw.Text(t, ink);

    /// <summary>A hint in the middle of an empty window.</summary>
    public static void Hint(DrawingContext ctx, Rect r, string text)
    {
        var ft = Text(text, NotaPalette.TextTertiary);
        ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
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
/// (log Hz). Drag a handle to move it, double-click (or Alt-click) to reset it.</summary>
internal abstract class CutHandleGraph : Control
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
// Reverb — Space: the tail of your audio
// ============================================================================
/// <summary>The last six seconds of the input (grey area) and the reverb's tail before the mix
/// (the curve), dB against time, now at the right. A dashed guide falls from the tail's latest
/// peak at the set RT60, so a tail that rings longer or shorter than the setting shows at once.
/// Drag to lengthen or shorten the decay (right or up = longer), double-click to reset it.</summary>
internal sealed class ReverbSpaceView : Control
{
    private float[] _s = Array.Empty<float>();
    private int _off;
    private bool _has, _freeze;
    private double _rt60 = 2;
    private bool _drag;
    private double _dragX, _dragY, _dragV;

    public event Action<double>? Changed;   // the Decay param, normalised
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action? Reset;
    public Func<double>? DecayValue { get; set; }
    public bool Dragging => _drag;

    public ReverbSpaceView()
    {
        ClipToBounds = true;
        MinHeight = 40;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    /// <summary>The scope (history at <paramref name="off"/>, or <paramref name="has"/> false), the RT60 in
    /// seconds and whether the tail is frozen.</summary>
    public void Set(float[] scope, int off, bool has, double rt60, bool freeze)
    {
        _s = scope; _off = off; _has = has && scope.Length >= off + SpaceDraw.Floats; _rt60 = rt60; _freeze = freeze;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) { Reset?.Invoke(); e.Pointer.Capture(null); e.Handled = true; return; }   // as CutHandleGraph
        var p = e.GetPosition(this);
        _drag = true; _dragX = p.X; _dragY = p.Y; _dragV = DecayValue?.Invoke() ?? 0.5;
        GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        var p = e.GetPosition(this);
        double w = Math.Max(40, Bounds.Width), h = Math.Max(40, Bounds.Height);
        Changed?.Invoke(Math.Clamp(_dragV + (p.X - _dragX) / w - (p.Y - _dragY) / h * 0.5, 0, 1));
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (!_drag) return;
        _drag = false; GestureEnd?.Invoke();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_freeze)
        {
            ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x0D), rect);
            ctx.DrawRectangle(null, new Pen(NotaPalette.BorderBrass, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));
        }
        double x0 = 6, x1 = w - 6, top = NotaGraph.PlotTop, bot = h - NotaGraph.PlotFloor, span = bot - top;
        if (span < 8) return;
        double Y(double db) => bot - Math.Clamp((db + 60) / 60, 0, 1) * span;
        double XAge(double sec) => x1 - sec / SpaceDraw.HistorySec * (x1 - x0);

        // One line a second, and −20 / −40 dB.
        for (int s = 1; s < 6; s++) ctx.DrawLine(NotaGraph.GridPen, new Point(XAge(s), top), new Point(XAge(s), bot));
        foreach (double db in new[] { -20.0, -40 }) ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(db)), new Point(x1, Y(db)));

        bool any = _has && Math.Max(SpaceDraw.Max(_s, _off, SpaceDraw.Points), SpaceDraw.Max(_s, _off + SpaceDraw.Points, SpaceDraw.Points)) > -90;
        if (any)
        {
            SpaceDraw.Area(ctx, SpaceDraw.InputWash, SpaceDraw.Trace(_s, _off, SpaceDraw.Points, x0, x1, Y), bot);
            var wet = SpaceDraw.Trace(_s, _off + SpaceDraw.Points, SpaceDraw.Points, x0, x1, Y);
            CompDraw.Polyline(ctx, NotaGraph.PrimaryPen, wet);
            DrawGuide(ctx, x0, x1, top, bot, Y, XAge);
        }
        else SpaceDraw.Hint(ctx, new Rect(x0, top, x1 - x0, span), "Play audio to see its tail");

        if (_freeze) ctx.DrawText(new NotaFormattedText("Tail held · no decay", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansMedium, NotaGraph.TitleSize, NotaPalette.AccentBright, this), new Point(6, 4));
        else NotaGraph.Title(ctx, rect, "Tail");
        NotaGraph.Legend(ctx, x1 - 2, 4, ("tail", NotaPalette.VizInk, NotaGraph.Mark.Line), ("input", NotaPalette.TextTertiary, NotaGraph.Mark.Area),
            (_freeze ? "RT60 ∞" : NotaNum.F($"RT60 {_rt60:0.00} s"), NotaPalette.Accent, NotaGraph.Mark.Dashed));
        NotaGraph.EdgeLabels(ctx, rect, false, (Y(0) + 6, "0 dB"), (Y(-20), "−20"), (Y(-40), "−40"));
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomLeft, NotaNum.F($"−{SpaceDraw.HistorySec:0} s"));
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomRight, "now");
    }

    // The RT60 the knob names, drawn from the tail's latest peak: a straight fall of 60 dB in RT60.
    private void DrawGuide(DrawingContext ctx, double x0, double x1, double top, double bot, Func<double, double> Y, Func<double, double> XAge)
    {
        // The latest peak within 12 dB of the loudest point on screen: walk back from now to the first
        // point that loud, then up its slope to the top.
        int wetOff = _off + SpaceDraw.Points, pk = -1;
        float loud = SpaceDraw.Max(_s, wetOff, SpaceDraw.Points);
        if (loud < -54) return;
        for (int k = SpaceDraw.Points - 1; k >= 0; k--)
            if (_s[wetOff + k] >= loud - 12) { pk = k; break; }
        if (pk < 0) return;
        while (pk > 0 && _s[wetOff + pk - 1] >= _s[wetOff + pk]) pk--;
        for (int k = pk - 1; k >= Math.Max(0, pk - 25); k--) if (_s[wetOff + k] > _s[wetOff + pk]) pk = k;   // ride over small dips
        float pkDb = _s[wetOff + pk];
        double age = (SpaceDraw.Points - 1 - pk) * SpaceDraw.PointSec;
        double xs = XAge(age), ys = Y(pkDb);
        double xe, ye;
        if (_freeze) { xe = x1; ye = ys; }
        else
        {
            double toFloor = (pkDb + 60) / 60 * _rt60;          // seconds until the guide reaches −60 dB
            double sec = Math.Min(toFloor, age);
            xe = XAge(age - sec); ye = Y(pkDb - 60 * sec / Math.Max(0.05, _rt60));
        }
        ctx.DrawLine(new Pen(NotaPalette.Accent, 1.2, new DashStyle(new double[] { 3, 3 }, 0)), new Point(xs, ys), new Point(xe, ye));
        ctx.DrawEllipse(NotaPalette.Accent, null, new Point(xs, ys), 2.5, 2.5);
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

        NotaGraph.Title(ctx, rect, "Decay by frequency");
        NotaGraph.Legend(ctx, x1 - 2, 4, ("decay", NotaPalette.VizInk, NotaGraph.Mark.Line), ("tail spectrum", NotaPalette.TextTertiary, NotaGraph.Mark.Line));
        if (_freeze) NotaGraph.EdgeLabels(ctx, rect, false, (top + 8, "held"));
        else NotaGraph.EdgeLabels(ctx, rect, false, (YT(full) + 6, Secs(full)), (YT(full / 2), Secs(full / 2)));
    }

    private static string Secs(double s) => s < 1 ? NotaNum.F($"{s * 1000:0} ms") : NotaNum.F($"{s:0.#} s");
}

// ============================================================================
// Delay — Time: the echoes of your audio
// ============================================================================
/// <summary>The input (grey area) and the echoes before the mix (the curve) over the last few seconds,
/// now at the right; in Sync, on the project's beat grid while the transport rolls. Dots mark where
/// the repeats of the latest hit land and how loud the feedback leaves them (L in the viz ink, R in
/// teal when its time differs), joined by the dashed decay the feedback sets.</summary>
internal sealed class DelayEchoView : Control
{
    private float[] _s = Array.Empty<float>();
    private int _off;
    private bool _has, _freeze, _grid;
    private double _windowSec = 2, _msL = 500, _msR = 500, _fb = 0.4, _beat, _bpm = 120;
    private static readonly IPen BeatPen = new Pen(NotaPalette.GridBeat, 1), BarPen = new Pen(NotaPalette.GridBar, 1);

    public DelayEchoView()
    {
        ClipToBounds = true;
        MinHeight = 40;
    }

    /// <summary>The scope and the settings: the window (≤ 6.1 s), the tap times, the feedback, and the
    /// beat grid (the transport's beat now and the tempo; <paramref name="grid"/> false hides it).</summary>
    public void Set(float[] scope, int off, bool has, double windowSec, double msL, double msR, double feedback, bool freeze,
        bool grid, double beatNow, double bpm)
    {
        _s = scope; _off = off; _has = has && scope.Length >= off + SpaceDraw.Floats;
        _windowSec = Math.Clamp(windowSec, 0.2, SpaceDraw.HistorySec); _msL = msL; _msR = msR; _fb = feedback; _freeze = freeze;
        _grid = grid && bpm > 1; _beat = beatNow; _bpm = bpm;
        InvalidateVisual();
    }

    // The latest hit whose first repeat has already come round: a jump of 9 dB within 12 ms, above −50 dB.
    // The hit whose repeats to mark: the latest one old enough for a few of the longer channel's
    // repeats to fit (up to most of the window), or failing that the latest with one in the past.
    private int Onset(int n, double firstMs, double lastMs)
    {
        double want = Math.Max(firstMs, Math.Min(3 * lastMs, n * SpaceDraw.PointSec * 1000 * 0.6));
        foreach (double ms in want > firstMs ? new[] { want, firstMs } : new[] { firstMs })
        {
            int minAge = (int)(ms / 1000 / SpaceDraw.PointSec);
            for (int k = SpaceDraw.Points - 1 - minAge; k >= Math.Max(3, SpaceDraw.Points - n); k--)
            {
                float v = _s[_off + k];
                if (v > -50 && v - _s[_off + k - 3] > 9 && v >= _s[_off + k - 1]) return k;
            }
        }
        return -1;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_freeze)
        {
            ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x0D), rect);
            ctx.DrawRectangle(null, new Pen(NotaPalette.BorderBrass, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));
        }
        double x0 = 6, x1 = w - 6, top = NotaGraph.PlotTop, bot = h - NotaGraph.PlotFloor, span = bot - top;
        if (span < 8) return;
        int n = Math.Clamp((int)Math.Round(_windowSec / SpaceDraw.PointSec), 10, SpaceDraw.Points);
        double win = n * SpaceDraw.PointSec;
        double Y(double db) => bot - Math.Clamp((db + 60) / 60, 0, 1) * span;
        double XAge(double sec) => x1 - sec / win * (x1 - x0);

        // Grid: the project's beats (every fourth a little stronger), drawn over the input so a
        // beat that lands on a hit still shows; without them, four even divisions.
        void BeatGrid()
        {
            double beatSec = 60.0 / _bpm, frac = _beat - Math.Floor(_beat);
            long b0 = (long)Math.Floor(_beat);
            for (int j = 0; ; j++)
            {
                double age = (frac + j) * beatSec;
                if (age > win) break;
                bool bar = (b0 - j) % 4 == 0;
                ctx.DrawLine(bar ? BarPen : BeatPen, new Point(XAge(age), top), new Point(XAge(age), bot));
            }
        }
        if (!_grid) for (int i = 1; i < 4; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(XAge(win * i / 4), top), new Point(XAge(win * i / 4), bot));
        foreach (double db in new[] { -20.0, -40 }) ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(db)), new Point(x1, Y(db)));

        bool any = _has && Math.Max(SpaceDraw.Max(_s, _off, n), SpaceDraw.Max(_s, _off + SpaceDraw.Points, n)) > -90;
        if (any)
        {
            SpaceDraw.Area(ctx, SpaceDraw.InputWash, SpaceDraw.Trace(_s, _off, n, x0, x1, Y), bot);
            if (_grid) BeatGrid();
            CompDraw.Polyline(ctx, NotaGraph.PrimaryPen, SpaceDraw.Trace(_s, _off + SpaceDraw.Points, n, x0, x1, Y));
            if (!_freeze) DrawRepeats(ctx, n, x0, x1, Y, XAge);
        }
        else
        {
            if (_grid) BeatGrid();
            SpaceDraw.Hint(ctx, new Rect(x0, top, x1 - x0, span), "Play audio to see its echoes");
        }

        if (_freeze) ctx.DrawText(new NotaFormattedText("Loop held · no decay", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansMedium, NotaGraph.TitleSize, NotaPalette.AccentBright, this), new Point(6, 4));
        else NotaGraph.Title(ctx, rect, "Echoes");
        NotaGraph.Legend(ctx, x1 - 2, 4, ("echoes", NotaPalette.VizInk, NotaGraph.Mark.Line), ("input", NotaPalette.TextTertiary, NotaGraph.Mark.Area),
            ("repeats", NotaPalette.Accent, NotaGraph.Mark.Dashed));
        NotaGraph.EdgeLabels(ctx, rect, false, (Y(0) + 6, "0 dB"), (Y(-20), "−20"), (Y(-40), "−40"));
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomLeft, win >= 1 ? NotaNum.F($"−{win:0.#} s") : NotaNum.F($"−{win * 1000:0} ms"));
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomRight, "now");
    }

    private void DrawRepeats(DrawingContext ctx, int n, double x0, double x1, Func<double, double> Y, Func<double, double> XAge)
    {
        double first = Math.Min(_msL, _msR);
        if (first < 5) return;
        int k0 = Onset(n, first, Math.Max(_msL, _msR));
        if (k0 < 0) return;
        float pk = -120;
        for (int k = k0; k < Math.Min(SpaceDraw.Points, k0 + 5); k++) pk = Math.Max(pk, _s[_off + k]);
        double onsetAge = (SpaceDraw.Points - 1 - k0) * SpaceDraw.PointSec;
        double stepDb = 20 * Math.Log10(Math.Clamp(_fb, 1e-4, 0.999));
        var dash = new Pen(NotaPalette.Wash(NotaPalette.Accent, 0xA0), 1, new DashStyle(new double[] { 3, 3 }, 0));
        bool both = Math.Abs(_msL - _msR) > 1;
        foreach (var (ms, ink) in both ? new[] { (_msL, (IBrush)NotaPalette.Accent), (_msR, NotaPalette.Teal) } : new[] { (_msL, (IBrush)NotaPalette.Accent) })
        {
            Point? last = new Point(XAge(onsetAge), Y(pk));
            for (int r = 1; r < 64; r++)
            {
                double age = onsetAge - r * ms / 1000;
                double db = pk + (r - 1) * stepDb;
                if (age < 0 || db < -60) break;
                var p = new Point(XAge(age), Y(db));
                if (last is { } l) ctx.DrawLine(dash, l, p);
                ctx.DrawEllipse(ink, new Pen(NotaGraph.Ground, 1.5), p, 3, 3);
                last = p;
            }
        }
    }
}

// ============================================================================
// Delay — Loop · Wow: how the repeats change
// ============================================================================
/// <summary>The response of the first six repeats: each goes round the loop once more, through the low
/// cut, the high cut and (in Tape mode) the head loss, and loses the feedback each time — so each curve
/// sits lower and narrower than the last. The echoes' live spectrum runs underneath. Drag a handle to
/// move a cut, double-click it to open it.</summary>
internal sealed class DelayRepeatsView : CutHandleGraph
{
    private const int Repeats = 6;
    private readonly WetSpectrum _spec = new();
    private double _fb = 0.4, _sr = 48000;
    private bool _tape, _freeze, _specLive;

    public DelayRepeatsView() { LowLo = 20; LowHi = 2000; HighLo = 200; HighHi = 20000; }

    public void Set(double feedback, double lowHz, double highHz, bool lowOn, bool highOn, bool tape, bool freeze,
        float[] scope, int specOff, bool has, double sampleRate)
    {
        _fb = feedback; LowHz = lowHz; HighHz = highHz; LowOn = lowOn; HighOn = highOn; _tape = tape; _freeze = freeze;
        _sr = sampleRate > 1000 ? sampleRate : 48000;
        _specLive = has && scope.Length >= specOff + SpaceDraw.SpecN;
        if (_specLive) _spec.Feed(scope, specOff);
        InvalidateVisual();
    }

    private double OnePole(double fc) => 1 - Math.Exp(-2 * Math.PI * Math.Clamp(fc, 5, _sr * 0.45) / _sr);

    /// <summary>One pass through the loop filter at <paramref name="hz"/>, in dB (Delay.cpp's one-poles).</summary>
    public double PassDb(double hz)
    {
        if (_freeze) return 0;
        double wv = 2 * Math.PI * hz / _sr, cos = Math.Cos(wv), mag = 1;
        double Den(double a) => Math.Sqrt(1 - 2 * a * cos + a * a);
        if (LowOn) { double a = 1 - OnePole(LowHz); mag *= a * Math.Sqrt(2 - 2 * cos) / Den(a); }
        if (HighOn) { double c = OnePole(HighHz); mag *= c / Den(1 - c); }
        if (_tape) { double c = OnePole(8000); mag *= c / Den(1 - c); }
        return mag > 1e-9 ? 20 * Math.Log10(mag) : -180;
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
        const double floorDb = -48;
        double Y(double db) => top + Math.Clamp(-db / -floorDb, 0, 1) * span;
        foreach (double db in new[] { -12.0, -24, -36 }) ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(db)), new Point(x1, Y(db)));

        if (_specLive || _spec.Live)
        {
            int cols = Math.Max(2, (int)(x1 - x0));
            var db = _spec.Columns(cols, _sr);
            var pts = new List<Point>(cols);
            for (int c = 0; c < cols; c++) pts.Add(new Point(x0 + (double)c / (cols - 1) * (x1 - x0), bot - Math.Clamp((db[c] + 96) / 96, 0, 1) * span));
            CompDraw.Polyline(ctx, NotaGraph.SecondaryPen(NotaPalette.TextTertiary, 1.1), pts);
        }

        double stepDb = _freeze ? 0 : 20 * Math.Log10(Math.Clamp(_fb, 1e-4, 1));
        DrawCuts(ctx, top, bot, x => Y(PassDb(SpaceDraw.HzAt(x, x0, x1))));
        for (int r = Repeats; r >= 1; r--)
        {
            var pts = new List<Point>();
            double best = -999;
            for (double x = x0; x <= x1 + 0.1; x += 2)
            {
                double xx = Math.Min(x, x1), db = (r - 1) * stepDb + r * PassDb(SpaceDraw.HzAt(xx, x0, x1));
                pts.Add(new Point(xx, Y(db)));
                best = Math.Max(best, db);
            }
            // Each repeat's number sits over the middle of the band the cuts leave.
            double midHz = Math.Sqrt((LowOn ? LowHz : SpaceDraw.FMin) * (HighOn ? HighHz : SpaceDraw.FMax));
            double bestX = SpaceDraw.LogX(midHz, x0, x1);
            best = (r - 1) * stepDb + r * PassDb(midHz);
            if (best < floorDb) continue;
            IPen pen = r == 1 ? NotaGraph.PrimaryPen : new Pen(NotaPalette.Wash(NotaPalette.VizInk, (byte)(0xC0 - r * 0x18)), 1.2, lineJoin: PenLineJoin.Round);
            CompDraw.Polyline(ctx, pen, pts);
            if (_freeze) break;   // a held loop: every repeat the same, one line
            var num = SpaceDraw.Text(NotaNum.F($"{r}"), r == 1 ? NotaPalette.TextSecondary : NotaPalette.TextTertiary);
            double ny = Y(best) - num.Height - 1;
            if (ny > top - 4) ctx.DrawText(num, new Point(Math.Clamp(bestX - num.Width / 2, x0 + 2, x1 - num.Width - 2), ny));
        }

        if (_freeze) ctx.DrawText(new NotaFormattedText("Loop held · filters bypassed", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansMedium, NotaGraph.TitleSize, NotaPalette.AccentBright, this), new Point(6, 4));
        else NotaGraph.Title(ctx, rect, "Repeats");
        NotaGraph.Legend(ctx, x1 - 2, 4, (_freeze ? "every repeat" : NotaNum.F($"repeats 1–{Repeats}"), NotaPalette.VizInk, NotaGraph.Mark.Line),
            ("echo spectrum", NotaPalette.TextTertiary, NotaGraph.Mark.Line));
        NotaGraph.EdgeLabels(ctx, rect, false, (Y(0) + 6, "0 dB"), (Y(-24), "−24"));
    }
}
