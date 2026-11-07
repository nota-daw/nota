// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The shared filter graph of the built-in instruments and Auto Filter (#531). A subclass draws its own
// response (each instrument's filter model) and keeps its own drag; the base draws the rest
// the same way everywhere:
//   · the window, decade grid and the 0 / −24 dB lines;
//   · the spectrum of what the instrument plays — after its filter — behind the curve
//     (FilterSpectrum, from the engine's output tap while the graph is on screen), or of what
//     an audio effect gets (Auto Filter feeds it from its own scope ring);
//   · the frequency axis under the plot, with the current cutoff lit on it (the labels it
//     would cover step aside) and a dashed rule up to the curve;
//   · a hover marker: a line, a dot on the curve and the frequency, note and gain there.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;

namespace Nota.App;

internal abstract class FilterGraphBase : Control
{
    private static readonly IBrush SpecFill = NotaPalette.Wash(NotaPalette.TextAxis, 0x2E);
    private static readonly IPen SpecEdge = new Pen(NotaPalette.Wash(NotaPalette.TextAxis, 0x8C), 1, lineJoin: PenLineJoin.Round);
    protected const double AxisH = 13;   // the frequency axis strip under the plot

    /// <summary>The frequency span across the width, lo → hi in Hz (log).</summary>
    protected virtual (double Lo, double Hi) Span => (20.0, 18000.0);
    /// <summary>The dB span of the plot, top → bottom.</summary>
    protected virtual (double Top, double Bottom) DbSpan => (12.0, -48.0);
    /// <summary>The response the curve draws, in dB at a frequency (the hover's gain).</summary>
    protected abstract double ResponseDb(double hz);
    /// <summary>The cutoff to light on the axis, in Hz; NaN for none.</summary>
    protected abstract double CutoffHz { get; }
    /// <summary>True while the subclass drags its handle (the cutoff pill lights up).</summary>
    protected bool Dragging { get; set; }

    /// <summary>The instrument's output spectrum; null draws the curve alone (a rack chain).</summary>
    public FilterSpectrum? Spectrum
    {
        get => _spectrum;
        set { _spectrum = value; SyncTimer(); }
    }
    private FilterSpectrum? _spectrum;

    private double? _hoverX;
    private DispatcherTimer? _timer;

    // ---- geometry ------------------------------------------------------------------
    protected Rect PlotRect => new(0, 0, Math.Max(0, Bounds.Width), Math.Max(0, Bounds.Height - AxisH));
    protected double XOf(double hz)
    {
        var (lo, hi) = Span; var r = PlotRect;
        return r.X + Math.Log(Math.Max(hz, 1e-3) / lo) / Math.Log(hi / lo) * r.Width;
    }
    protected double HzAt(double x)
    {
        var (lo, hi) = Span; var r = PlotRect;
        return lo * Math.Pow(hi / lo, Math.Clamp((x - r.X) / Math.Max(1, r.Width), 0, 1));
    }
    protected double YOf(double db)
    {
        var (t, b) = DbSpan; var r = PlotRect;
        double top = r.Y + 4, bot = r.Bottom - 3;
        return top + Math.Clamp((t - db) / (t - b), 0, 1) * (bot - top);
    }

    // ---- hover + timer ---------------------------------------------------------------
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _hoverX = p.Y <= PlotRect.Bottom ? Math.Clamp(p.X, 0, Bounds.Width) : null;
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null; InvalidateVisual();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); SyncTimer(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _timer?.Stop(); _timer = null; }

    private void SyncTimer()
    {
        if (_spectrum is not { Tapped: true } || VisualRoot is null) { _timer?.Stop(); _timer = null; return; }
        if (_timer is not null) return;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            if (!IsEffectivelyVisible || _spectrum is null) return;   // a hidden tab reads nothing: the tap goes quiet
            var (lo, hi) = Span;
            if (_spectrum.Update(lo, hi)) InvalidateVisual();
        });
        _timer.Start();
    }

    // ---- drawing ---------------------------------------------------------------------
    /// <summary>A response across the plot: the accent line with a light fill under it, or a
    /// secondary line (a second filter, a single tap) in its own ink without a fill.</summary>
    protected void DrawCurve(DrawingContext ctx, Func<double, double> dbAt, IBrush? ink = null, bool fill = true, double width = NotaGraph.PrimaryWidth)
    {
        var r = PlotRect; if (r.Width <= 1) return;
        int n = (int)Math.Clamp(r.Width / 1.5, 24, 420);
        var pts = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double x = r.X + r.Width * i / (n - 1);
            pts[i] = new Point(x, YOf(dbAt(HzAt(x))));
        }
        var line = new StreamGeometry();
        using (var g = line.Open()) { g.BeginFigure(pts[0], false); for (int i = 1; i < n; i++) g.LineTo(pts[i]); g.EndFigure(false); }
        if (fill)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(pts[0].X, r.Bottom), true);
                foreach (var q in pts) g.LineTo(q);
                g.LineTo(new Point(pts[^1].X, r.Bottom)); g.EndFigure(true);
            }
            ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x1E), null, area);
        }
        ctx.DrawGeometry(null, new Pen(ink ?? NotaPalette.AccentBright, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }

    /// <summary>The cutoff handle: hollow in the accent, filled with a halo while dragged.</summary>
    protected void DrawHandle(DrawingContext ctx, Point at)
    {
        if (Dragging) ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, 0x40), null, at, 8, 8);
        ctx.DrawEllipse(Dragging ? NotaPalette.AccentBright : NotaGraph.Ground, new Pen(NotaPalette.AccentBright, 1.6), at, 3.8, 3.8);
    }

    /// <summary>The window, grid and spectrum — under the curve.</summary>
    protected void DrawBack(DrawingContext ctx)
    {
        var r = PlotRect; if (r.Width <= 0 || r.Height <= 0) return;
        NotaGraph.Window(ctx, r);
        var (lo, hi) = Span;
        foreach (var f in new[] { 100.0, 1000, 10000 })
            if (f > lo && f < hi) ctx.DrawLine(NotaGraph.GridPen, new Point(XOf(f), r.Y + 1), new Point(XOf(f), r.Bottom - 1));
        foreach (var db in new[] { 0.0, -24 }) ctx.DrawLine(NotaGraph.GridPen, new Point(r.X + 1, YOf(db)), new Point(r.Right - 1, YOf(db)));

        if (_spectrum is { Live: true } sp)
        {
            // As on EQ-3 / EQ-8: a filled area with a faint top edge, 0 dBFS at the top, −90 at
            // the floor; the line breaks where it runs along the floor.
            var bands = sp.Bands; int n = bands.Length;
            double top = r.Y + 1, bot = r.Bottom - 1;
            double Ys(double db) => top + Math.Clamp(-db / 90.0, 0, 1) * (bot - top);
            var pts = new Point[n];
            for (int i = 0; i < n; i++) pts[i] = new Point(r.X + r.Width * (i + 0.5) / n, Ys(bands[i]));
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(r.X, bot), true);
                g.LineTo(new Point(r.X, pts[0].Y));
                foreach (var q in pts) g.LineTo(q);
                g.LineTo(new Point(r.Right, pts[^1].Y));
                g.LineTo(new Point(r.Right, bot));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(SpecFill, null, area);
            var edge = new StreamGeometry();
            using (var g = edge.Open())
            {
                bool open = false;
                for (int i = 0; i < n; i++)
                {
                    bool floor = pts[i].Y >= bot - 0.5 && (i == n - 1 || pts[i + 1].Y >= bot - 0.5);
                    if (floor) { if (open) { g.EndFigure(false); open = false; } continue; }
                    if (!open) { g.BeginFigure(pts[i], false); open = true; } else g.LineTo(pts[i]);
                }
                if (open) g.EndFigure(false);
            }
            ctx.DrawGeometry(null, SpecEdge, edge);
        }
    }

    /// <summary>The axis, the cutoff and the hover marker — over the curve. <paramref name="cutY"/>
    /// is where the curve meets the cutoff (the dashed rule ends there).</summary>
    protected void DrawFront(DrawingContext ctx, double cutY = double.NaN)
    {
        var r = PlotRect; double w = Bounds.Width, h = Bounds.Height;
        if (r.Width <= 0) return;
        var (lo, hi) = Span;

        // dB marks inside the plot.
        var (dbT, dbB) = DbSpan;
        ctx.DrawText(NotaGraph.AxisText($"+{dbT:0}"), new Point(4, r.Y + 2));
        var fl = NotaGraph.AxisText($"−{-dbB:0} dB");
        ctx.DrawText(fl, new Point(4, r.Bottom - fl.Height - 2));

        // The cutoff: a dashed rule up to the curve and its value lit on the axis.
        double fc = CutoffHz;
        Rect pill = default;
        if (double.IsFinite(fc) && fc > 0)
        {
            double cx = Math.Clamp(XOf(fc), r.X, r.Right);
            var dash = new Pen(NotaPalette.Wash(NotaPalette.Accent, Dragging ? (byte)0x90 : (byte)0x55), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            double yTop = double.IsFinite(cutY) ? cutY : r.Y;
            ctx.DrawLine(dash, new Point(cx, yTop), new Point(cx, r.Bottom));
            var ct = NotaGraph.AxisText(NotaNum.Hz(fc), NotaPalette.TextOnAccent);
            double pw = ct.Width + 8, px = Math.Clamp(cx - pw / 2, 0, w - pw);
            pill = new Rect(px, r.Bottom + 1, pw, AxisH - 1);
            ctx.DrawRectangle(Dragging ? NotaPalette.AccentBright : NotaPalette.Accent, null, pill, 3, 3);
            ctx.DrawText(ct, new Point(px + 4, pill.Y + (pill.Height - ct.Height) / 2));
        }

        // The frequency labels: the decades and their halves, each left out where it would run
        // into a neighbour or the cutoff.
        double lastRight = -10;
        foreach (var f in new[] { lo, 50, 100, 200, 500, 1000, 2000, 5000, 10000, hi })
        {
            if (f < lo * 0.999 || f > hi * 1.001 || (f != lo && f < lo * 1.6) || (f != hi && f > hi / 1.6)) continue;
            var t = NotaGraph.AxisText(NotaNum.Hz(f));
            double x = XOf(f), tx = Math.Clamp(x - t.Width / 2, 0, w - t.Width);
            var box = new Rect(tx - 2, r.Bottom + 1, t.Width + 4, AxisH - 1);
            if (tx <= lastRight + 3 || (pill.Width > 0 && box.Intersects(pill))) continue;
            ctx.DrawText(t, new Point(tx, r.Bottom + 1 + (AxisH - 1 - t.Height) / 2));
            lastRight = tx + t.Width;
        }

        // The hover marker.
        if (_hoverX is double hx)
        {
            double f = HzAt(hx), db = ResponseDb(f), y = YOf(db);
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.TextPrimary, 0x70), 1), new Point(hx, r.Y + 1), new Point(hx, r.Bottom - 1));
            ctx.DrawEllipse(NotaPalette.TextPrimary, new Pen(NotaGraph.Ground, 1.5), new Point(hx, y), 3, 3);
            var t1 = NotaGraph.AxisText($"{NotaNum.Hz(f)} · {NoteName(f)}", NotaPalette.TextPrimary);
            var t2 = NotaGraph.AxisText(NotaNum.Unit(db, "+0.0;−0.0;0.0", "dB"), NotaPalette.TextSecondary);
            double tw = Math.Max(t1.Width, t2.Width) + 10, th = t1.Height + t2.Height + 5;
            double tx = hx + 8 + tw > w ? hx - 8 - tw : hx + 8;
            double ty = Math.Clamp(y - th - 6, r.Y + 2, r.Bottom - th - 2);
            var tip = new Rect(tx, ty, tw, th);
            ctx.DrawRectangle(NotaPalette.SurfaceRaised, new Pen(NotaPalette.BorderDefault, 1), tip, 3, 3);
            ctx.DrawText(t1, new Point(tx + 5, ty + 2));
            ctx.DrawText(t2, new Point(tx + 5, ty + 3 + t1.Height));
        }
    }

    private static readonly string[] Names = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };
    /// <summary>The note nearest a frequency, with its cents off (A4 = 440 Hz): "E7 +12 c".</summary>
    internal static string NoteName(double hz)
    {
        double m = 69 + 12 * Math.Log2(Math.Max(hz, 1e-3) / 440.0);
        int n = (int)Math.Round(m), c = (int)Math.Round((m - n) * 100);
        string name = Names[((n % 12) + 12) % 12] + NotaNum.Str((n / 12) - 1, "0");
        return c == 0 ? name : $"{name} {(c > 0 ? "+" : "−")}{Math.Abs(c)} c";
    }
}

/// <summary>The spectrum behind a filter graph in log bands (#531): either read from an
/// instrument's output tap (which runs only while someone reads) or fed by its owner (an audio
/// effect's own scope ring), Hann-windowed, FFT'd and smoothed — quick to rise, slow to fall.</summary>
internal sealed class FilterSpectrum
{
    public const int N = 2048;
    private const int BandCount = 128;
    private readonly IAudioEngine? _engine;
    private readonly int _trackId;
    private readonly float[] _buf = new float[N];
    private readonly double[] _re = new double[N], _im = new double[N];
    private static readonly double[] Hann = BuildHann();
    public double[] Bands { get; } = Fill(new double[BandCount], -120);
    public bool Live { get; private set; }
    /// <summary>True when the graph reads the instrument's output tap on its own timer.</summary>
    public bool Tapped => _engine is not null;

    /// <summary>Reads an instrument's output tap.</summary>
    public FilterSpectrum(IAudioEngine engine, int trackId) { _engine = engine; _trackId = trackId; }
    /// <summary>Fed by its owner through <see cref="Feed"/>.</summary>
    public FilterSpectrum() { }

    private static double[] BuildHann() { var w = new double[N]; for (int i = 0; i < N; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)); return w; }
    private static double[] Fill(double[] a, double v) { Array.Fill(a, v); return a; }

    /// <summary>Read and analyse; false when nothing changed worth a redraw.</summary>
    public bool Update(double lo, double hi)
    {
        if (_engine is null) return false;
        int n = _engine.InstrumentOutput(_trackId, _buf);
        if (n < N) { if (!Live) return false; Live = false; return true; }
        return Analyse(lo, hi, _engine.SampleRate > 0 ? _engine.SampleRate : 48000);
    }

    /// <summary>Analyse N samples of the owner's ring from <paramref name="offset"/>; fewer goes quiet.</summary>
    public bool Feed(float[] src, int offset, int n, double sampleRate, double lo, double hi)
    {
        if (n < N || offset < 0 || offset + N > src.Length) { bool was = Live; Live = false; return was; }
        Array.Copy(src, offset, _buf, 0, N);
        return Analyse(lo, hi, sampleRate > 0 ? sampleRate : 48000);
    }

    private bool Analyse(double lo, double hi, double sr)
    {
        double peak = 0;
        for (int i = 0; i < N; i++) { _re[i] = _buf[i] * Hann[i]; _im[i] = 0; peak = Math.Max(peak, Math.Abs(_buf[i])); }
        bool sounding = peak > 1e-5;
        if (sounding) Fft.Transform(_re, _im);
        double norm = 4.0 / N;   // Hann's coherent gain 0.5, both sides of the spectrum
        bool any = false;
        for (int b = 0; b < BandCount; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, (double)b / BandCount), f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / BandCount);
            double db = -120;
            if (sounding)
            {
                int k0 = Math.Max(1, (int)Math.Floor(f0 / sr * N)), k1 = Math.Min(N / 2 - 1, Math.Max(k0, (int)Math.Ceiling(f1 / sr * N)));
                double m = 0;
                for (int k = k0; k <= k1; k++) m = Math.Max(m, Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * norm);
                db = 20 * Math.Log10(Math.Max(m, 1e-9));
            }
            double prev = Bands[b];
            Bands[b] = db > prev ? prev + (db - prev) * 0.6 : prev + (db - prev) * 0.15;
            any |= Bands[b] > -88;
        }
        bool was = Live;
        Live = any;
        return any || was;
    }
}
