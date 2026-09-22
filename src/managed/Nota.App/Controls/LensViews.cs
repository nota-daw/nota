// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Lens (device kind 22) visualisers — the three graphs the analyzer card
// swaps between, plus the vertical scale slider of its SCALE rail. All three read
// one LensData snapshot that the card refills from the engine on the 60 Hz tick
// (curve / peak-hold / scope trace / waterfall row / measurements), so the graphs
// never touch the engine themselves.
//
// They are controls, not decoration: the spectrum carries a frequency cursor with
// its note, the scope's trigger level and A/B cursors are dragged straight on the
// trace, and the waterfall follows the same log-frequency axis so a peak lines up
// between the two views.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nota.App;

/// <summary>One analysis frame, refilled by the card from the engine each tick and
/// shared by the three Lens graphs.</summary>
internal sealed class LensData
{
    public const int Curve = 512, TraceN = 512, WfBins = 256, WfRows = 256;

    public readonly float[] Spec = new float[Curve];
    public readonly float[] Peak = new float[Curve];
    public readonly float[] Trace = new float[TraceN];
    public readonly float[] Wf = new float[WfBins * WfRows];   // ring of rows, oldest → newest
    public int WfHead;                                          // rows written (mod WfRows = next slot)
    public int WfFilled;

    /// <summary>Past scope traces for the afterglow, newest first, with their age in seconds.</summary>
    public readonly List<(float[] Pts, double Age)> Ghosts = new();

    public double SampleRate = 48000;
    public double TopDb = -6, RangeDb = 90;     // dB axis
    public double VoltDiv = 0.5, TimeDivSec = 0.002;
    public bool LogFreq = true, NoteGrid, PeakOn = true, Held;
    public double TrigLevel, CursorA = 0.25, CursorB = 0.375;
    public bool CursorsOn, CursorSnap, TrigRising = true, TrigOk;
    public double Bright = 0.66;
    public double WfSpanSec = 12;

    public double FreqLo => 20;
    public double FreqHi => Math.Min(20000, SampleRate * 0.45);

    /// <summary>The curve index (0..Curve−1) a frequency sits at — the curve is log-spaced.</summary>
    public double IndexOf(double hz)
        => Math.Clamp(Math.Log(hz / FreqLo) / Math.Log(FreqHi / FreqLo), 0, 1) * (Curve - 1);

    public double FreqOf(double index)
        => FreqLo * Math.Pow(FreqHi / FreqLo, Math.Clamp(index / (Curve - 1), 0, 1));

    /// <summary>Where a frequency sits on the always-logarithmic analysis curve, 0..1 — the
    /// waterfall's bins are spaced that way whatever axis the view draws.</summary>
    public double LogFrac(double hz)
        => Math.Clamp(Math.Log(hz / FreqLo) / Math.Log(FreqHi / FreqLo), 0, 1);

    /// <summary>Fraction across the graph a frequency sits at (log or linear axis).</summary>
    public double FracOf(double hz) => LogFreq
        ? Math.Clamp(Math.Log(hz / FreqLo) / Math.Log(FreqHi / FreqLo), 0, 1)
        : Math.Clamp((hz - FreqLo) / (FreqHi - FreqLo), 0, 1);

    public double HzAt(double frac) => LogFreq
        ? FreqLo * Math.Pow(FreqHi / FreqLo, Math.Clamp(frac, 0, 1))
        : FreqLo + (FreqHi - FreqLo) * Math.Clamp(frac, 0, 1);

    public double DbAt(double hz)
    {
        double x = IndexOf(hz);
        int i = Math.Min((int)x, Curve - 2);
        double f = x - i;
        return Spec[i] * (1 - f) + Spec[i + 1] * f;
    }

    public void PushWaterfallRow(float[] row)
    {
        Array.Copy(row, 0, Wf, (WfHead % WfRows) * WfBins, Math.Min(row.Length, WfBins));
        WfHead++;
        if (WfFilled < WfRows) WfFilled++;
    }

    public void ClearWaterfall() { Array.Clear(Wf); WfHead = 0; WfFilled = 0; }

    public void PushGhost(double maxAgeSec, int maxTraces)
    {
        var copy = new float[TraceN];
        Array.Copy(Trace, copy, TraceN);
        Ghosts.Insert(0, (copy, 0));
        while (Ghosts.Count > Math.Max(1, maxTraces)) Ghosts.RemoveAt(Ghosts.Count - 1);
        for (int i = Ghosts.Count - 1; i >= 0; i--)
            if (Ghosts[i].Age > maxAgeSec) Ghosts.RemoveAt(i);
    }

    public void AgeGhosts(double dt)
    {
        for (int i = 0; i < Ghosts.Count; i++) Ghosts[i] = (Ghosts[i].Pts, Ghosts[i].Age + dt);
    }
}

internal static class LensInk
{
    public static IBrush Ground => NotaPalette.BgSunken;
    public static IPen Frame => NotaGraph.FramePen;
    public static IBrush Trace => NotaPalette.Accent;
    public static IBrush TraceLit => NotaPalette.AccentBright;
    public static IBrush Fill => NotaPalette.Wash(NotaPalette.Accent, 0x22);
    public static IBrush Hold => NotaPalette.Teal;
    public static IBrush Axis => NotaPalette.TextAxis;
    public static IBrush Label => NotaPalette.TextTertiary;
    public static IPen GridFaint => new Pen(NotaPalette.GridBeat, 1);
    public static IPen GridStrong => new Pen(NotaPalette.WellGrid, 1);
    public static IPen Zero => new Pen(NotaPalette.GridBar, 1);

    /// <summary>The graph's own title: a caps eyebrow, then its value in mono so a unit
    /// keeps its case ("SCOPE · 2.00 ms/div", never "2.00 MS/DIV").</summary>
    public static void Title(DrawingContext ctx, string caps, string value, double x = 6, double y = 4)
    {
        var head = new FormattedText(caps.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansBold, NotaGraph.TitleSize, Label);
        ctx.DrawText(head, new Point(x, y));
        if (value.Length > 0) Text(ctx, value, x + head.Width + 5, y, Axis, 8);
    }

    public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>"A#3 +4 ¢" for a frequency, or "—" below hearing.</summary>
    public static string Note(double hz)
    {
        if (hz <= 16) return "—";
        double midi = 69 + 12 * Math.Log2(hz / 440.0);
        int n = (int)Math.Round(midi);
        int cents = (int)Math.Round((midi - n) * 100);
        return NotaNum.F($"{NoteNames[((n % 12) + 12) % 12]}{n / 12 - 1} {cents:+0;−0;0} ¢");
    }

    public static void Text(DrawingContext ctx, string s, double x, double y, IBrush ink, double size = 7, bool mono = true)
        => ctx.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? NotaFonts.Mono : NotaFonts.SansBold, size, ink), new Point(x, y));

    public static double TextW(string s, double size = 7, bool mono = true)
        => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? NotaFonts.Mono : NotaFonts.SansBold, size, NotaPalette.TextPrimary).Width;
}

// ===========================================================================
//  Spectrum
// ===========================================================================
internal sealed class LensSpectrumView : Control
{
    private static readonly double[] GridHz = { 30, 50, 100, 200, 300, 500, 1000, 2000, 3000, 5000, 10000, 20000 };
    private static readonly double[] LabelHz = { 100, 1000, 10000 };

    private readonly LensData _d;
    private double _cursorFrac = -1;

    /// <summary>The cursor's frequency (Hz), or 0 when the pointer is away.</summary>
    public double CursorHz => _cursorFrac < 0 ? 0 : _d.HzAt(_cursorFrac);
    public double CursorDb => _cursorFrac < 0 ? 0 : _d.DbAt(CursorHz);
    public event Action? CursorMoved;

    public LensSpectrumView(LensData d) { _d = d; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Cross); }

    public void Tick() => InvalidateVisual();

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _cursorFrac = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1);
        CursorMoved?.Invoke();
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { _cursorFrac = -1; CursorMoved?.Invoke(); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        double top = _d.TopDb, range = Math.Max(6, _d.RangeDb);
        // The frequency labels get their own lane under the plot, so the noise floor never
        // sits on top of "1k" (the mockup's axis row).
        const double AxisH = 11;
        double plotH = Math.Max(8, h - AxisH);
        double Y(double db) => Math.Clamp((top - db) / range, 0, 1) * plotH;

        // dB grid: a line every 30 dB, labelled inside on the left.
        for (double db = Math.Ceiling(top / 30) * 30; db > top - range; db -= 30)
        {
            double y = Y(db);
            if (y < 2 || y > plotH - 2) continue;
            ctx.DrawLine(LensInk.GridFaint, new Point(0, y), new Point(w, y));
            LensInk.Text(ctx, NotaNum.F($"{db:0}"), 4, y - 8, LensInk.Axis);
        }
        // Frequency grid.
        foreach (double hz in GridHz)
        {
            if (hz >= _d.FreqHi) continue;
            double x = _d.FracOf(hz) * w;
            ctx.DrawLine(Array.IndexOf(LabelHz, hz) >= 0 ? LensInk.GridStrong : LensInk.GridFaint, new Point(x, 0), new Point(x, plotH));
        }
        if (_d.NoteGrid)
        {
            for (int oct = 1; oct <= 9; oct++)
            {
                double hz = 440.0 * Math.Pow(2, (12 * oct - 57) / 12.0);   // C of each octave
                if (hz < _d.FreqLo || hz >= _d.FreqHi) continue;
                double x = _d.FracOf(hz) * w;
                ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x22), 1), new Point(x, 0), new Point(x, plotH));
            }
        }

        // The curve: a filled body under an amber trace.
        var fill = new StreamGeometry();
        var line = new StreamGeometry();
        using (var fc = fill.Open())
        using (var lc = line.Open())
        {
            fc.BeginFigure(new Point(0, plotH), true);
            bool started = false;
            for (int px = 0; px <= (int)w; px++)
            {
                double frac = px / Math.Max(1.0, w);
                double hz = _d.HzAt(frac);
                double y = Y(_d.DbAt(hz));
                fc.LineTo(new Point(px, y));
                if (!started) { lc.BeginFigure(new Point(px, y), false); started = true; }
                else lc.LineTo(new Point(px, y));
            }
            fc.LineTo(new Point(w, plotH));
            fc.EndFigure(true);
            if (started) lc.EndFigure(false);
        }
        ctx.DrawGeometry(LensInk.Fill, null, fill);

        // Peak hold behind the live trace, in the second chroma.
        if (_d.PeakOn)
        {
            var hold = new StreamGeometry();
            using (var hc = hold.Open())
            {
                bool started = false;
                for (int px = 0; px <= (int)w; px++)
                {
                    double x = _d.LogFreq ? px / Math.Max(1.0, w) * (LensData.Curve - 1) : _d.IndexOf(_d.HzAt(px / Math.Max(1.0, w)));
                    int i = Math.Clamp((int)x, 0, LensData.Curve - 2);
                    double f = x - i;
                    double y = Y(_d.Peak[i] * (1 - f) + _d.Peak[i + 1] * f);
                    if (!started) { hc.BeginFigure(new Point(px, y), false); started = true; } else hc.LineTo(new Point(px, y));
                }
                if (started) hc.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xB4), 1), hold);
        }
        ctx.DrawGeometry(null, new Pen(LensInk.Trace, NotaGraph.PrimaryWidth * (0.6 + 0.6 * _d.Bright)), line);

        // Cursor with its note readout.
        if (_cursorFrac >= 0)
        {
            double x = _cursorFrac * w, hz = CursorHz, db = CursorDb;
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x99), 1), new Point(x, 0), new Point(x, plotH));
            double y = Y(db);
            ctx.DrawEllipse(LensInk.TraceLit, null, new Point(x, y), 2.5, 2.5);
            string s = NotaNum.F($"{LensInk.Note(hz)} · {Fmt(hz)} · {db:0.0} dB");
            double tw = LensInk.TextW(s, 8);
            LensInk.Text(ctx, s, Math.Clamp(x + 5, 3, w - tw - 3), 3, LensInk.TraceLit, 8);
        }
        else
        {
            LensInk.Title(ctx, "SPECTRUM", NotaNum.F($"{_d.TopDb:0} … {_d.TopDb - range:0} dB"));
        }

        // The axis lane: the decade labels sit under the plot, each on its grid line.
        ctx.DrawLine(LensInk.GridFaint, new Point(0, plotH), new Point(w, plotH));
        foreach (double hz in LabelHz)
        {
            if (hz >= _d.FreqHi) continue;
            double x = _d.FracOf(hz) * w;
            string s = hz >= 1000 ? NotaNum.F($"{hz / 1000:0} kHz") : NotaNum.F($"{hz:0} Hz");
            LensInk.Text(ctx, s, Math.Min(x + 3, w - LensInk.TextW(s) - 3), plotH + 1, LensInk.Axis);
        }
        LensInk.Text(ctx, NotaNum.F($"{top - range:0} dB"), w - LensInk.TextW(NotaNum.F($"{top - range:0} dB")) - 4, plotH - 9, LensInk.Axis);
    }

    private static string Fmt(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.00} kHz") : NotaNum.F($"{hz:0} Hz");
}

// ===========================================================================
//  Scope
// ===========================================================================
internal sealed class LensScopeView : Control
{
    private const int Cols = 8, Rows = 4;

    private readonly LensData _d;
    private int _drag;   // 0 none · 1 trigger level · 2 cursor A · 3 cursor B

    public event Action<double>? TrigLevelChanged;   // −1..1
    public event Action<int, double>? CursorChanged; // (0=A / 1=B, 0..1)

    public LensScopeView(LensData d) { _d = d; ClipToBounds = true; }

    public void Tick() => InvalidateVisual();

    private double YOf(double v, double h) => h / 2 - Math.Clamp(v / Math.Max(1e-6, _d.VoltDiv * (Rows / 2.0)), -1.2, 1.2) * (h / 2);
    private double VOf(double y, double h) => (h / 2 - y) / (h / 2) * (_d.VoltDiv * (Rows / 2.0));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        _drag = 0;
        if (_d.CursorsOn)
        {
            if (Math.Abs(p.X - _d.CursorA * w) < 6) _drag = 2;
            else if (Math.Abs(p.X - _d.CursorB * w) < 6) _drag = 3;
        }
        if (_drag == 0 && Math.Abs(p.Y - YOf(_d.TrigLevel, h)) < 8) _drag = 1;
        if (_drag == 0) return;
        e.Pointer.Capture(this); e.Handled = true;
        Apply(p);
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag != 0) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = 0; e.Pointer.Capture(null); }

    private void Apply(Point p)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (_drag == 1) TrigLevelChanged?.Invoke(Math.Clamp(VOf(p.Y, h), -1, 1));
        else
        {
            double frac = Math.Clamp(p.X / Math.Max(1, w), 0, 1);
            if (_d.CursorSnap) frac = SnapToPeak(frac);
            CursorChanged?.Invoke(_drag - 2, frac);
        }
        InvalidateVisual();
    }

    /// <summary>Snap a cursor to the strongest turning point of the trace within ~2.5 % of
    /// the window — "Snap to peaks", so a Δt lands on the waveform and not between samples.</summary>
    private double SnapToPeak(double frac)
    {
        var t = _d.Trace;
        int n = t.Length, c = Math.Clamp((int)Math.Round(frac * (n - 1)), 1, n - 2);
        int win = Math.Max(2, n / 40);
        int best = c; double bestV = -1;
        for (int i = Math.Max(1, c - win); i <= Math.Min(n - 2, c + win); i++)
        {
            double v = Math.Abs(t[i]);
            if (v < Math.Abs(t[i - 1]) || v < Math.Abs(t[i + 1])) continue;   // a turning point only
            if (v > bestV) { bestV = v; best = i; }
        }
        return best / (double)(n - 1);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        for (int c = 1; c < Cols; c++)
        {
            double x = w * c / Cols;
            ctx.DrawLine(c == Cols / 2 ? LensInk.GridStrong : LensInk.GridFaint, new Point(x, 0), new Point(x, h));
        }
        for (int r = 1; r < Rows; r++)
        {
            double y = h * r / Rows;
            ctx.DrawLine(r == Rows / 2 ? LensInk.Zero : LensInk.GridFaint, new Point(0, y), new Point(w, y));
        }

        // Afterglow: older passes fade out behind the live trace.
        foreach (var (pts, age) in _d.Ghosts)
        {
            double k = Math.Clamp(1 - age / Math.Max(0.05, _ghostLife), 0, 1);
            if (k <= 0.02) continue;
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Accent, (byte)(k * 90)), 1.2), Path(pts, w, h));
        }
        ctx.DrawGeometry(null, new Pen(LensInk.Trace, 1.1 + 1.0 * _d.Bright), Path(_d.Trace, w, h));

        // Trigger level (draggable) + the trigger point, pinned at 25 % like a bench scope.
        double ty = YOf(_d.TrigLevel, h);
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xC0), 1, new DashStyle(new double[] { 3, 4 }, 0)), new Point(0, ty), new Point(w, ty));
        ctx.DrawRectangle(LensInk.Hold, null, new Rect(0, ty - 3.5, 5, 7));
        double tx = w * 0.25;
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x80), 1), new Point(tx, 0), new Point(tx, h));
        ctx.DrawRectangle(LensInk.Trace, null, new Rect(tx - 3.5, 0, 7, 5));

        if (_d.CursorsOn)
        {
            foreach (var (frac, name) in new[] { (_d.CursorA, "A"), (_d.CursorB, "B") })
            {
                double x = frac * w;
                ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xAA), 1), new Point(x, 0), new Point(x, h));
                LensInk.Text(ctx, name, x + 2, h / 2 - 12, LensInk.TraceLit, 8, false);
            }
            double dt = Math.Abs(_d.CursorB - _d.CursorA) * _d.TimeDivSec * Cols;
            string s = NotaNum.F($"Δt {dt * 1000:0.000} ms · {(dt > 1e-9 ? 1 / dt : 0):0.0} Hz");
            LensInk.Text(ctx, s, w - LensInk.TextW(s, 8) - 4, h - 11, LensInk.TraceLit, 8);
        }

        LensInk.Title(ctx, "SCOPE", NotaNum.F($"{Ms(_d.TimeDivSec)}/div · {_d.VoltDiv:0.00}/div"));
        string trig = _d.Held ? "HELD" : NotaNum.F($"trig {(_d.TrigRising ? "↑" : "↓")} {_d.TrigLevel:0.00}{(_d.TrigOk ? "" : " ·free")}");
        LensInk.Text(ctx, trig, w - LensInk.TextW(trig) - 4, 3, _d.TrigOk || _d.Held ? LensInk.TraceLit : LensInk.Axis);
        LensInk.Text(ctx, "0", 4, h - 10, LensInk.Axis);
        string right = Ms(_d.TimeDivSec * Cols);
        LensInk.Text(ctx, right, w - LensInk.TextW(right) - 4, h - 10, LensInk.Axis);
    }

    private double _ghostLife = 0.42;
    public double GhostLife { set => _ghostLife = value; }

    private Geometry Path(float[] pts, double w, double h)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(0, YOf(pts[0], h)), false);
        for (int i = 1; i < pts.Length; i++) c.LineTo(new Point(i * w / (pts.Length - 1), YOf(pts[i], h)));
        c.EndFigure(false);
        return g;
    }

    private static string Ms(double sec) => sec >= 0.001 ? NotaNum.F($"{sec * 1000:0.00} ms") : NotaNum.F($"{sec * 1e6:0} µs");
}

// ===========================================================================
//  Waterfall
// ===========================================================================
internal sealed class LensWaterfallView : Control
{
    private static readonly double[] LabelHz = { 100, 1000, 10000 };

    private readonly LensData _d;
    private readonly WriteableBitmap _bmp;
    private readonly int[] _px = new int[LensData.WfRows * LensData.WfBins];
    private readonly int[] _bin = new int[LensData.WfBins];
    private readonly int[] _ramp = new int[256];
    private int _rampSeed = -1;
    private double _cursorFrac = -1, _cursorTime;

    public double CursorHz => _cursorFrac < 0 ? 0 : _d.HzAt(1 - _cursorFrac);
    public double CursorAgeSec => _cursorTime;
    public event Action? CursorMoved;

    public LensWaterfallView(LensData d)
    {
        _d = d;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        _bmp = new WriteableBitmap(new PixelSize(LensData.WfRows, LensData.WfBins), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    public void Tick() => InvalidateVisual();

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        _cursorFrac = Math.Clamp(p.Y / Math.Max(1, Bounds.Height - 11), 0, 1);
        _cursorTime = (1 - Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1)) * _d.WfSpanSec;
        CursorMoved?.Invoke();
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { _cursorFrac = -1; CursorMoved?.Invoke(); InvalidateVisual(); }

    // Well → brass ramp: the same warmth as every other graph, never a rainbow.
    private void BuildRamp()
    {
        int seed = NotaPalette.Accent.Color.GetHashCode() ^ NotaPalette.BgSunken.Color.GetHashCode();
        if (seed == _rampSeed) return;
        _rampSeed = seed;
        Color a = NotaPalette.BgSunken.Color, b = NotaPalette.AccentDeep.Color,
              c = NotaPalette.Accent.Color, e = NotaPalette.AccentPale.Color;
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            Color lo, hi; double k;
            if (t < 0.45) { lo = a; hi = b; k = t / 0.45; }
            else if (t < 0.8) { lo = b; hi = c; k = (t - 0.45) / 0.35; }
            else { lo = c; hi = e; k = (t - 0.8) / 0.2; }
            byte R = (byte)(lo.R + (hi.R - lo.R) * k), G = (byte)(lo.G + (hi.G - lo.G) * k), B = (byte)(lo.B + (hi.B - lo.B) * k);
            _ramp[i] = unchecked((int)0xFF000000) | (R << 16) | (G << 8) | B;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        BuildRamp();

        // Time runs left (oldest) → right (now); frequency runs bottom → top. The rows a
        // frame carries are log-spaced, so a linear axis re-maps them rather than stretching.
        int rows = LensData.WfRows, bins = LensData.WfBins;
        for (int y = 0; y < bins; y++)
        {
            double hz = _d.HzAt(1 - y / (double)(bins - 1));
            _bin[y] = Math.Clamp((int)Math.Round(_d.LogFrac(hz) * (bins - 1)), 0, bins - 1);
        }
        for (int x = 0; x < rows; x++)
        {
            int age = rows - 1 - x;                       // 0 = newest column
            int slot = ((_d.WfHead - 1 - age) % rows + rows) % rows;
            bool have = age < _d.WfFilled;
            for (int y = 0; y < bins; y++)
            {
                float v = have ? _d.Wf[slot * bins + _bin[y]] : 0f;
                _px[y * rows + x] = _ramp[Math.Clamp((int)(v * 255), 0, 255)];
            }
        }
        using (var fb = _bmp.Lock())
        {
            for (int y = 0; y < bins; y++)
                Marshal.Copy(_px, y * rows, IntPtr.Add(fb.Address, y * fb.RowBytes), rows);
        }
        // The spectrogram, then its own time lane under it — a bright low band would
        // otherwise swallow the "−12 s" caption.
        const double AxisH = 11;
        double plotH = Math.Max(8, h - AxisH);
        ctx.DrawImage(_bmp, new Rect(0, 0, rows, bins), new Rect(1, 1, w - 2, plotH - 1));

        foreach (double hz in LabelHz)
        {
            if (hz >= _d.FreqHi) continue;
            double y = (1 - _d.FracOf(hz)) * plotH;
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.GridBeat, 0x88), 1), new Point(0, y), new Point(w, y));
            LensInk.Text(ctx, hz >= 1000 ? NotaNum.F($"{hz / 1000:0} kHz") : NotaNum.F($"{hz:0} Hz"), 4, y - 9, LensInk.Axis);
        }
        if (_d.NoteGrid)
        {
            for (int oct = 1; oct <= 9; oct++)
            {
                double hz = 440.0 * Math.Pow(2, (12 * oct - 57) / 12.0);   // C of each octave
                if (hz < _d.FreqLo || hz >= _d.FreqHi) continue;
                double y = (1 - _d.FracOf(hz)) * plotH;
                ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x33), 1), new Point(0, y), new Point(w, y));
            }
        }
        if (_cursorFrac >= 0)
        {
            double y = Math.Min(_cursorFrac * plotH, plotH);
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x99), 1), new Point(0, y), new Point(w, y));
        }
        ctx.DrawLine(LensInk.GridFaint, new Point(0, plotH), new Point(w, plotH));
        LensInk.Title(ctx, "WATERFALL", NotaNum.F($"{_d.WfSpanSec:0} s"));
        string span = NotaNum.F($"−{_d.WfSpanSec:0} s");
        LensInk.Text(ctx, span, 4, plotH + 1, LensInk.Axis);
        LensInk.Text(ctx, "now", w - LensInk.TextW("now") - 4, plotH + 1, LensInk.Axis);
    }
}

// ===========================================================================
//  SCALE rail slider
// ===========================================================================
/// <summary>The thin vertical slider of the Lens SCALE rail: a sunken groove with a
/// filled column and a bright cap. Drag or wheel; double-click resets.</summary>
internal sealed class LensVSlider : Control
{
    private readonly SolidColorBrush _ink;
    private double _value;
    private bool _drag;
    private double _lastY;

    public double Default { get; set; } = -1;
    public bool Dragging => _drag;
    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public double Value { get => _value; set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); } }

    public LensVSlider(double value, SolidColorBrush ink)
    {
        _value = Math.Clamp(value, 0, 1);
        _ink = ink;
        Width = 16;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && Default >= 0) { Value = Default; ValueChanged?.Invoke(_value); e.Handled = true; return; }
        _drag = true; _lastY = e.GetPosition(this).Y;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y;
        Value = _value + (_lastY - y) / Math.Max(24, Bounds.Height);
        _lastY = y;
        ValueChanged?.Invoke(_value);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); e.Pointer.Capture(null); }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        Value = _value + e.Delta.Y * 0.02;
        ValueChanged?.Invoke(_value);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new Rect(0, 0, w, h);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), new RoundedRect(r.Deflate(0.5), w / 2));
        double fillH = _value * (h - 4);
        ctx.DrawRectangle(NotaPalette.Wash(_ink, 0x4C), null,
            new RoundedRect(new Rect(1.5, h - 2 - fillH, w - 3, fillH), (w - 3) / 2));
        double capY = Math.Clamp(h - 2 - fillH, 2, h - 4);
        ctx.DrawRectangle(_ink, null, new RoundedRect(new Rect(1, capY - 1, w - 2, 2.5), 1.5));
    }
}
