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
    // The synced frame (Lens::L_Sync, specs/050): a header, then SyncCols × (main min, main
    // max, key min, key max, age).
    public const int SyncHead = 8, SyncCols = 1024, SyncCol = 5;
    public const int S_Mode = 0, S_Playhead = 1, S_Bpm = 2, S_BeatsPerBar = 3, S_SpanBeats = 4, S_Start = 5, S_KeyLive = 6;

    public readonly float[] Spec = new float[Curve];
    public readonly float[] Peak = new float[Curve];
    public readonly float[] Trace = new float[TraceN];
    public readonly float[] KeyTrace = new float[TraceN];
    public readonly float[] Sync = new float[SyncHead + SyncCols * SyncCol];
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

    // Tempo sync + the sidechain key (specs/050).
    public bool SyncOn, KeyOn, Stacked;
    public double KeyGain = 1;          // linear, display only
    public string KeyName = "key";

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
    public static IBrush Trace => NotaPalette.VizInk;            // the signal at rest: neutral ink
    public static IBrush TraceLit => NotaPalette.AccentBright;   // live state only: the hover cursor, a locked trigger
    public static IBrush Fill => NotaPalette.Wash(NotaPalette.VizInk, 0x22);
    public static IBrush Hold => NotaPalette.TextTertiary;   // a second source: a darker grey
    public static IBrush Axis => NotaPalette.TextAxis;
    public static IBrush Label => NotaPalette.TextTertiary;
    public static IPen GridFaint => new Pen(NotaPalette.GridBeat, 1);
    public static IPen GridStrong => new Pen(NotaPalette.WellGrid, 1);
    public static IPen Zero => new Pen(NotaPalette.GridBar, 1);

    /// <summary>The graph's own title in sentence case, then its value in mono so a unit
    /// keeps its case ("Scope · 2.00 ms/div").</summary>
    public static void Title(DrawingContext ctx, string caps, string value, double x = 6, double y = 4)
    {
        var head = new NotaFormattedText(NotaText.Label(caps), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansSemiBold, NotaGraph.TitleSize, Label, NotaTextSize.InCard);
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
        => ctx.DrawText(new NotaFormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? NotaFonts.Sans : NotaFonts.SansMedium, size, ink, NotaTextSize.InCard), new Point(x, y));

    public static double TextW(string s, double size = 7, bool mono = true)
        => new NotaFormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? NotaFonts.Sans : NotaFonts.SansMedium, size, NotaPalette.TextPrimary, NotaTextSize.InCard).Width;
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
        const double AxisH = 12, TitleH = 17;   // the title row stays clear of the grid
        double plotH = Math.Max(8, h - AxisH);
        double Y(double db) => Math.Clamp((top - db) / range, 0, 1) * plotH;

        // dB grid: a line every 30 dB, labelled inside on the left.
        for (double db = Math.Ceiling(top / 30) * 30; db > top - range; db -= 30)
        {
            double y = Y(db);
            if (y < TitleH + 2 || y > plotH - 6) continue;   // none on the title row or on the floor
            ctx.DrawLine(LensInk.GridFaint, new Point(0, y), new Point(w, y));
            if (y - 8 >= TitleH) LensInk.Text(ctx, NotaNum.F($"{db:0}"), 4, y - 8, LensInk.Axis);
        }
        // Frequency grid.
        foreach (double hz in GridHz)
        {
            if (hz >= _d.FreqHi) continue;
            double x = _d.FracOf(hz) * w;
            ctx.DrawLine(Array.IndexOf(LabelHz, hz) >= 0 ? LensInk.GridStrong : LensInk.GridFaint, new Point(x, TitleH), new Point(x, plotH));
        }
        if (_d.NoteGrid)
        {
            for (int oct = 1; oct <= 9; oct++)
            {
                double hz = 440.0 * Math.Pow(2, (12 * oct - 57) / 12.0);   // C of each octave
                if (hz < _d.FreqLo || hz >= _d.FreqHi) continue;
                double x = _d.FracOf(hz) * w;
                ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x40), 1), new Point(x, TitleH), new Point(x, plotH));
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
            ctx.DrawGeometry(null, new Pen(NotaPalette.TextTertiary, 1), hold);
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
            LensInk.Title(ctx, "SPECTRUM", "");   // the top and range are on the Top and Rng bars
        }

        // The axis lane: the decade labels sit under the plot, each on its grid line.
        ctx.DrawLine(LensInk.GridFaint, new Point(0, plotH), new Point(w, plotH));
        foreach (double hz in LabelHz)
        {
            if (hz >= _d.FreqHi) continue;
            double x = _d.FracOf(hz) * w;
            string s = hz >= 1000 ? NotaNum.F($"{hz / 1000:0} kHz") : NotaNum.F($"{hz:0} Hz");
            LensInk.Text(ctx, s, Math.Min(x + 3, w - LensInk.TextW(s) - 3), plotH + 2, LensInk.Axis);
        }
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

    // A lane is the band of the graph one trace uses: the whole height, or with the key
    // Stacked, the top half (main) or the bottom half (key).
    private bool StackedNow => _d.KeyOn && _d.Stacked;
    private (double Mid, double Half) MainLane(double h) => StackedNow ? (h / 4, h / 4) : (h / 2, h / 2);
    private (double Mid, double Half) KeyLane(double h) => StackedNow ? (h * 3 / 4, h / 4) : (h / 2, h / 2);
    private double YIn(double v, (double Mid, double Half) lane, double gain = 1)
        => lane.Mid - Math.Clamp(v * gain / Math.Max(1e-6, _d.VoltDiv * (Rows / 2.0)), -1.2, 1.2) * lane.Half;
    private double YOf(double v, double h) => YIn(v, MainLane(h));
    private double VOf(double y, double h) { var l = MainLane(h); return (l.Mid - y) / l.Half * (_d.VoltDiv * (Rows / 2.0)); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _d.SyncOn) return;   // synced: no trigger or cursors
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

    // Capture taken away mid-drag (the window deactivates): end the drag.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { if (_drag != 0) { _drag = 0; InvalidateVisual(); } base.OnPointerCaptureLost(e); }

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
        if (_d.SyncOn) { RenderSync(ctx, w, h); return; }

        for (int c = 1; c < Cols; c++)
        {
            double x = w * c / Cols;
            ctx.DrawLine(c == Cols / 2 ? LensInk.GridStrong : LensInk.GridFaint, new Point(x, 0), new Point(x, h));
        }
        LaneGrid(ctx, w, h);

        // Afterglow: older passes fade out behind the live trace.
        foreach (var (pts, age) in _d.Ghosts)
        {
            double k = Math.Clamp(1 - age / Math.Max(0.05, _ghostLife), 0, 1);
            if (k <= 0.02) continue;
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.VizInk, (byte)(k * 90)), 1.2), Path(pts, w, MainLane(h)));
        }
        ctx.DrawGeometry(null, new Pen(LensInk.Trace, 1.1 + 1.0 * _d.Bright), Path(_d.Trace, w, MainLane(h)));
        // The sidechain key on the same trigger and time base, in the second chroma.
        if (_d.KeyOn) ctx.DrawGeometry(null, new Pen(KeyInk, 1.0 + 0.8 * _d.Bright), Path(_d.KeyTrace, w, KeyLane(h), _d.KeyGain));

        // Trigger level (draggable) + the trigger point, pinned at 25 % like a bench scope.
        double ty = YOf(_d.TrigLevel, h);
        ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1, new DashStyle(new double[] { 3, 4 }, 0)), new Point(0, ty), new Point(w, ty));
        ctx.DrawRectangle(LensInk.Hold, null, new Rect(0, ty - 3.5, 5, 7));
        double tx = w * 0.25;
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.VizInk, 0x80), 1), new Point(tx, 0), new Point(tx, h));
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

        LensInk.Title(ctx, "SCOPE", "");   // the divisions are on the Time/div and Volt/div knobs
        string trig = _d.Held ? "held" : NotaNum.F($"trig {(_d.TrigRising ? "↑" : "↓")} {_d.TrigLevel:0.00}{(_d.TrigOk ? "" : " ·free")}");
        LensInk.Text(ctx, trig, w - LensInk.TextW(trig) - 4, 3, _d.TrigOk || _d.Held ? LensInk.TraceLit : LensInk.Axis);
        LensInk.Text(ctx, "0", 4, h - 10, LensInk.Axis);
        string right = Ms(_d.TimeDivSec * Cols);
        LensInk.Text(ctx, right, w - LensInk.TextW(right) - 4, h - 10, LensInk.Axis);
        Legend(ctx, h, "0");
    }

    private static SolidColorBrush KeyInk => NotaPalette.Teal;

    // Horizontal divisions; Stacked, each half gets its own zero line under a divider.
    private void LaneGrid(DrawingContext ctx, double w, double h)
    {
        if (!StackedNow)
        {
            for (int r = 1; r < Rows; r++)
            {
                double y = h * r / Rows;
                ctx.DrawLine(r == Rows / 2 ? LensInk.Zero : LensInk.GridFaint, new Point(0, y), new Point(w, y));
            }
            return;
        }
        ctx.DrawLine(LensInk.GridStrong, new Point(0, h / 2), new Point(w, h / 2));
        foreach (double y in new[] { h / 4, h * 3 / 4 }) ctx.DrawLine(LensInk.Zero, new Point(0, y), new Point(w, y));
    }

    // "main" and the key track's name, in their trace colours, when the key is drawn: after the
    // bottom-left axis label, side by side (Overlay) or each at the foot of its lane (Stacked).
    private void Legend(DrawingContext ctx, double h, string axisLabel)
    {
        if (!_d.KeyOn) return;
        double x = 4 + LensInk.TextW(axisLabel) + 8;
        string k = _d.KeyName + (Math.Abs(_d.KeyGain - 1) > 0.01 ? NotaNum.F($" {20 * Math.Log10(_d.KeyGain):+0.#;−0.#} dB") : "");
        if (StackedNow)
        {
            LensInk.Text(ctx, "main", 4, h / 2 - 11, LensInk.Trace, 7, false);
            LensInk.Text(ctx, k, x, h - 10, KeyInk, 7, false);
            return;
        }
        LensInk.Text(ctx, "main", x, h - 10, LensInk.Trace, 7, false);
        LensInk.Text(ctx, k, x + LensInk.TextW("main", 7, false) + 8, h - 10, KeyInk, 7, false);
    }

    // ---- tempo sync (specs/050) -----------------------------------------------------

    /// <summary>"1 bar", "2 beats", "1/4 beat" for a span in beats.</summary>
    public static string SpanName(double beats, double beatsPerBar)
    {
        double bars = beats / Math.Max(1, beatsPerBar);
        if (beats > beatsPerBar - 1e-6 && Math.Abs(bars - Math.Round(bars)) < 1e-6)
            return NotaNum.F($"{bars:0} bar{(Math.Round(bars) == 1 ? "" : "s")}");
        if (beats < 1 - 1e-6) return NotaNum.F($"1/{Math.Round(1 / beats):0} beat");
        return NotaNum.F($"{beats:0.##} beat{(Math.Abs(beats - 1) < 1e-6 ? "" : "s")}");
    }

    /// <summary>"5.3" — bar.beat (1-based) of a song beat.</summary>
    public static string BarBeat(double beat, double beatsPerBar)
    {
        double bpb = Math.Max(1, beatsPerBar);
        int bar = (int)Math.Floor(beat / bpb + 1e-9);
        double inBar = beat - bar * bpb;
        return Math.Abs(inBar - Math.Round(inBar)) < 1e-6
            ? NotaNum.F($"{bar + 1}.{Math.Round(inBar) + 1:0}")
            : NotaNum.F($"{bar + 1}.{inBar + 1:0.##}");
    }

    private static bool OnGrid(double beat, double step) => Math.Abs(beat / step - Math.Round(beat / step)) < 1e-6;

    // The synced frame: the span on the song's grid, beat and bar lines, this pass up to the
    // sweep line and the previous pass (faded) after it; the key in its own colour.
    private void RenderSync(DrawingContext ctx, double w, double h)
    {
        var s = _d.Sync;
        bool synced = s[LensData.S_Mode] > 0.5f;
        double play = Math.Clamp(s[LensData.S_Playhead], 0, 1), bpm = s[LensData.S_Bpm];
        double bpb = Math.Max(1, s[LensData.S_BeatsPerBar]), span = Math.Max(1e-3, s[LensData.S_SpanBeats]), start = s[LensData.S_Start];

        // Beat lines (bar lines stronger); spans under a beat get quarters of the span.
        double step = span <= 1 + 1e-6 ? span / 4 : 1;
        int lines = (int)Math.Round(span / step);
        var sub = new Pen(NotaPalette.Wash(NotaPalette.GridBeat, 0x88), 1);
        var bar = new Pen(NotaPalette.GridBar, 1);
        for (int k = 1; k < lines; k++)
        {
            double beat = start + k * step, x = w * k / lines;
            IPen pen = !synced ? LensInk.GridFaint : OnGrid(beat, bpb) ? bar : OnGrid(beat, 1) ? LensInk.GridStrong : sub;
            ctx.DrawLine(pen, new Point(x, 0), new Point(x, h));
        }
        LaneGrid(ctx, w, h);

        Bands(ctx, w, 0, MainLane(h), 1, NotaPalette.VizInk);
        if (_d.KeyOn) Bands(ctx, w, 2, KeyLane(h), _d.KeyGain, KeyInk);

        if (synced)
        {
            double x = play * w;
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xC0), 1.2), new Point(x, 0), new Point(x, h));
            ctx.DrawRectangle(LensInk.TraceLit, null, new Rect(x - 3.5, 0, 7, 5));
        }

        LensInk.Title(ctx, "SCOPE", NotaNum.F($"{SpanName(span, bpb)} · {bpm:0.#} BPM"));
        string mode = synced ? "sync" : "free";
        LensInk.Text(ctx, mode, w - LensInk.TextW(mode) - 4, 3, synced ? LensInk.TraceLit : LensInk.Axis);
        string left = synced ? BarBeat(start, bpb) : NotaNum.F($"−{SpanName(span, bpb)}");
        string right = synced ? BarBeat(start + span, bpb) : "now";
        LensInk.Text(ctx, left, 4, h - 10, LensInk.Axis);
        LensInk.Text(ctx, right, w - LensInk.TextW(right) - 4, h - 10, LensInk.Axis);
        Legend(ctx, h, left);
    }

    // One trace of the synced frame as filled min/max bands, one pixel column at a time,
    // this pass at full strength and the previous one at 45 %.
    private void Bands(DrawingContext ctx, double w, int off, (double Mid, double Half) lane, double gain, SolidColorBrush ink)
    {
        var s = _d.Sync;
        int cols = LensData.SyncCols, px = Math.Max(1, (int)Math.Ceiling(w));
        foreach (int age in new[] { 1, 0 })
        {
            var g = new StreamGeometry();
            bool any = false;
            using (var gc = g.Open())
            {
                var top = new List<Point>();
                var bot = new List<Point>();
                void Flush()
                {
                    if (top.Count == 0) return;
                    if (top.Count == 1) { top.Add(top[0] + new Point(1, 0)); bot.Add(bot[0] + new Point(1, 0)); }
                    gc.BeginFigure(top[0], true);
                    for (int i = 1; i < top.Count; i++) gc.LineTo(top[i]);
                    for (int i = bot.Count - 1; i >= 0; i--) gc.LineTo(bot[i]);
                    gc.EndFigure(true);
                    any = true;
                    top.Clear(); bot.Clear();
                }
                for (int x = 0; x < px; x++)
                {
                    int c0 = (int)((long)x * cols / px), c1 = Math.Max(c0 + 1, (int)((long)(x + 1) * cols / px));
                    float lo = float.MaxValue, hi = float.MinValue;
                    bool have = false;
                    for (int c = c0; c < c1 && c < cols; c++)
                    {
                        int b = LensData.SyncHead + c * LensData.SyncCol;
                        float a = s[b + 4];
                        if (float.IsNaN(a) || (int)a != age) continue;
                        lo = Math.Min(lo, s[b + off]); hi = Math.Max(hi, s[b + off + 1]);
                        have = true;
                    }
                    if (!have) { Flush(); continue; }
                    double yHi = YIn(hi, lane, gain), yLo = YIn(lo, lane, gain);
                    if (yLo - yHi < 1) { double m = (yLo + yHi) / 2; yHi = m - 0.5; yLo = m + 0.5; }   // a flat run stays visible
                    double xx = x * w / px;
                    top.Add(new Point(xx, yHi)); bot.Add(new Point(xx, yLo));
                }
                Flush();
            }
            if (any) ctx.DrawGeometry(age == 0 ? ink : NotaPalette.Wash(ink, 0x73), null, g);
        }
    }

    private double _ghostLife = 0.42;
    public double GhostLife { set => _ghostLife = value; }

    private Geometry Path(float[] pts, double w, (double Mid, double Half) lane, double gain = 1)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(0, YIn(pts[0], lane, gain)), false);
        for (int i = 1; i < pts.Length; i++) c.LineTo(new Point(i * w / (pts.Length - 1), YIn(pts[i], lane, gain)));
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

    // Well → neutral ink ramp: energy reads as brightness, like every other resting value —
    // never a rainbow, and no accent (that is kept for live or chosen state).
    private void BuildRamp()
    {
        int seed = NotaPalette.VizInk.Color.GetHashCode() ^ NotaPalette.BgSunken.Color.GetHashCode();
        if (seed == _rampSeed) return;
        _rampSeed = seed;
        Color a = NotaPalette.BgSunken.Color, b = NotaPalette.TextTertiary.Color,
              c = NotaPalette.VizInk.Color, e = NotaPalette.TextHeading.Color;
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

        // Labels skip the title row and any label they would sit on; the lines stay.
        double lastLab = double.MaxValue;
        foreach (double hz in LabelHz)
        {
            if (hz >= _d.FreqHi) continue;
            double y = (1 - _d.FracOf(hz)) * plotH;
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.GridBeat, 0x88), 1), new Point(0, y), new Point(w, y));
            if (y - 9 < 18 || y + 1 > lastLab) continue;
            lastLab = y - 9;
            LensInk.Text(ctx, hz >= 1000 ? NotaNum.F($"{hz / 1000:0} kHz") : NotaNum.F($"{hz:0} Hz"), 4, y - 9, LensInk.Axis);
        }
        if (_d.NoteGrid)
        {
            for (int oct = 1; oct <= 9; oct++)
            {
                double hz = 440.0 * Math.Pow(2, (12 * oct - 57) / 12.0);   // C of each octave
                if (hz < _d.FreqLo || hz >= _d.FreqHi) continue;
                double y = (1 - _d.FracOf(hz)) * plotH;
                ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x50), 1), new Point(0, y), new Point(w, y));
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
