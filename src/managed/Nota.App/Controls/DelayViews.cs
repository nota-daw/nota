// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The custom-drawn views of the Nota Delay card: the echo lanes (L over R in dB on a time
// grid, the first echo of each lane dragged for the time and Feedback) and the Filter tab's band
// (low cut / high cut on the shared filter graph). They draw what the card hands them and own no
// engine state; ⌥-click a filter corner to type its value (the card supplies the field, specs/035).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// The repeats as two lanes in dB — L on top, R below — on a time grid (beats when synced,
// ms when free). The dry hit sits at 0 in both lanes; each echo is a stem up to its level on
// a 0 … −48 dB scale (the first echo is 0 dB, every next one Feedback lower), so how long the
// repeats last reads off the heights. Ping-pong draws the source arriving on L and the repeats
// hopping between the lanes. The first echo of each lane is a handle: drag it sideways for the
// time, up or down for Feedback. Frozen, every repeat stands at full height in an accent frame;
// the repeat that is sounding now is lit. Behind the stems runs your audio (#377): the echoes
// after the latest hit of the input, as a grey wash in dB against that hit, so what the loop
// really does to the repeats (the filter, the tape, the diffuser) reads against the set ones.
internal sealed class DelayTaps : Control
{
    private const int MaxTaps = 32;
    private const double FloorDb = -48, TopRow = 16, AxisRow = 15, X0 = 22;

    private double _fracL = 0.25, _fracR = 0.25;
    private float _fb = 0.4f;
    private bool _ping, _freeze, _linked;
    private string _labelL = "", _labelR = "";
    private (double Frac, string Text)[] _ticks = Array.Empty<(double, string)>();
    private int _litL = -1, _litR = -1;
    // The echoes of your latest hit: (seconds after the hit, dB against the hit); empty = none.
    private readonly System.Collections.Generic.List<(double Sec, double Db)> _live = new();
    private double _liveWin = 1;
    private int _drag = -1, _hover = -1;   // lane: 0 L, 1 R
    private Point _grab;

    /// <summary>Lane (0 L / 1 R) and the pointer's place across the window, 0..1.</summary>
    public event Action<int, double>? TimeDragged;
    /// <summary>Lane, and the vertical travel since the grab in px (up is positive).</summary>
    public event Action<int, double>? FeedbackDragged;
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;

    public bool Dragging => _drag >= 0;

    private static readonly IBrush LiveWash = NotaPalette.Wash(NotaPalette.TextTertiary, 0x40);

    public DelayTaps() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    /// <summary>Feed the window: each lane's first echo as a fraction of its width, the
    /// feedback, ping-pong / freeze / link, the lane labels and the time grid.</summary>
    public void Set(double fracL, double fracR, float feedback, bool ping, bool freeze, bool linked,
                    string labelL, string labelR, (double Frac, string Text)[] ticks)
    {
        _fracL = Math.Clamp(fracL, 0.005, 1.0);
        _fracR = Math.Clamp(fracR, 0.005, 1.0);
        _fb = feedback; _ping = ping; _freeze = freeze; _linked = linked;
        _labelL = labelL; _labelR = labelR; _ticks = ticks;
        InvalidateVisual();
    }

    /// <summary>Feed the level history (at <paramref name="off"/> in the scope; <paramref name="has"/>
    /// false = none) and the window's length in seconds. Returns how long ago the latest hit of
    /// the input was, in seconds, or −1 when there is none on record.</summary>
    public double SetLive(float[] scope, int off, bool has, double windowSec)
    {
        bool had = _live.Count > 0;
        _live.Clear();
        _liveWin = Math.Max(0.01, windowSec);
        int hit = has && scope.Length >= off + SpaceDraw.Floats ? SpaceDraw.LatestHit(scope, off) : -1;
        double age = -1;
        if (hit >= 0)
        {
            age = (SpaceDraw.Points - 1 - hit) * SpaceDraw.PointSec;
            // The hit's level: the loudest input within 50 ms of its start.
            float pk = -120;
            for (int k = hit; k < Math.Min(SpaceDraw.Points, hit + 13); k++) pk = Math.Max(pk, scope[off + k]);
            foreach (var (t, db) in SpaceDraw.Since(scope, off + SpaceDraw.Points, hit))
            {
                if (t > _liveWin) break;
                _live.Add((t, db - pk));
            }
            if (_live.TrueForAll(e => e.Db < FloorDb)) _live.Clear();
        }
        if (had || _live.Count > 0) InvalidateVisual();
        return age;
    }

    /// <summary>Which repeat is sounding now per lane (−1 = none).</summary>
    public void SetLit(int litL, int litR)
    {
        if (litL == _litL && litR == _litR) return;
        _litL = litL; _litR = litR;
        InvalidateVisual();
    }

    // ---- geometry ------------------------------------------------------------------
    private double X1 => Bounds.Width - 10;
    private double XAt(double frac) => X0 + frac * (X1 - X0);
    private (double Top, double Base) Lane(int lane)
    {
        double top = TopRow, bot = Bounds.Height - AxisRow, mid = (top + bot) / 2;
        return lane == 0 ? (top + 4, mid - 3) : (mid + 7, bot - 3);
    }
    private double YOf(int lane, double db)
    {
        var (t, b) = Lane(lane);
        return b - Math.Clamp((db - FloorDb) / -FloorDb, 0, 1) * (b - t);
    }

    // The echoes of a lane: (time as a fraction of the window, level in dB, index for lighting).
    private System.Collections.Generic.List<(double Frac, double Db, int N)> Echoes(int lane)
    {
        var list = new System.Collections.Generic.List<(double, double, int)>();
        double fb = Math.Clamp(_fb, 0.001f, 0.999f);
        double Db(int k) => _freeze ? 0 : 20 * Math.Log10(Math.Pow(fb, k));
        if (_ping)
        {
            // The source on L: L, then R one R-time later, then L one L-time after that …
            double t = 0;
            for (int k = 0; k < MaxTaps; k++)
            {
                t += k % 2 == 0 ? _fracL : _fracR;
                if (t > 1) break;
                if ((k % 2 == 0) == (lane == 0)) list.Add((t, Db(k), list.Count));
            }
        }
        else
        {
            double f = lane == 0 ? _fracL : _fracR;
            for (int k = 0; k < MaxTaps && (k + 1) * f <= 1; k++) list.Add(((k + 1) * f, Db(k), k));
        }
        return list;
    }

    private Point HandlePos(int lane)
    {
        var e = Echoes(lane);
        return e.Count == 0 ? new Point(-100, -100) : new Point(XAt(e[0].Frac), YOf(lane, e[0].Db));
    }
    private int LaneAt(Point p) => p.Y < (TopRow + Bounds.Height - AxisRow) / 2 ? 0 : 1;

    // ---- pointer ---------------------------------------------------------------------
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _grab = e.GetPosition(this);
        _drag = _linked && !_ping ? 0 : LaneAt(_grab);   // linked: both lanes move L's time
        GestureBegin?.Invoke(_drag);
        e.Pointer.Capture(this);
        TimeDragged?.Invoke(_drag, Math.Clamp((_grab.X - X0) / Math.Max(1, X1 - X0), 0, 1));
        e.Handled = true;
        InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0)
        {
            TimeDragged?.Invoke(_drag, Math.Clamp((p.X - X0) / Math.Max(1, X1 - X0), 0, 1));
            FeedbackDragged?.Invoke(_drag, _grab.Y - p.Y);
            return;
        }
        int h = LaneAt(p);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    { base.OnPointerExited(e); if (_hover >= 0) { _hover = -1; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        GestureEnd?.Invoke(_drag); _drag = -1; e.Pointer.Capture(null); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag >= 0) { GestureEnd?.Invoke(_drag); _drag = -1; InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    private static FormattedText Text(string t, IBrush ink, double size = NotaType.Axis)
        => new NotaFormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Measure, size, ink, NotaTextSize.InCard);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 60) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_freeze)
            ctx.DrawRectangle(null, new Pen(NotaPalette.Accent, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));

        double bot = h - AxisRow;
        // The time grid and its labels along the bottom.
        foreach (var (frac, text) in _ticks)
        {
            double x = XAt(frac);
            if (x < X0 + 2 || x > X1 + 1) continue;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, TopRow), new Point(x, bot));
            var ft = Text(text, NotaGraph.AxisInk);
            ctx.DrawText(ft, new Point(Math.Min(x - ft.Width / 2, w - 4 - ft.Width), bot + 2));
        }
        ctx.DrawText(Text("0", NotaGraph.AxisInk), new Point(X0 - 3, bot + 2));

        var dash = new Pen(NotaPalette.VizGrid, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
        for (int lane = 0; lane < 2; lane++)
        {
            var (t, b) = Lane(lane);
            // The lane's floor (−48 dB) and its −24 dB line.
            ctx.DrawLine(new Pen(NotaPalette.GridBeat, 1), new Point(X0, b), new Point(X1, b));
            ctx.DrawLine(dash, new Point(X0, YOf(lane, -24)), new Point(X1, YOf(lane, -24)));
            ctx.DrawText(Text(lane == 0 ? "L" : "R", NotaPalette.TextSecondary, NotaType.Axis + 1), new Point(6, (t + b) / 2 - 6));
            if (lane == 0)
            {
                var m = Text("−24", NotaGraph.AxisInk);
                ctx.DrawText(m, new Point(X1 - m.Width, YOf(0, -24) - m.Height - 1));
            }

            // Your echoes behind the stems, in this lane's dB scale.
            if (_live.Count > 1)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(X0, b), true);
                    foreach (var (sec, db) in _live) g.LineTo(new Point(XAt(sec / _liveWin), YOf(lane, db)));
                    g.LineTo(new Point(XAt(_live[^1].Sec / _liveWin), b));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(LiveWash, null, geo);
            }

            // The dry hit at 0.
            ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 2), new Point(X0, b), new Point(X0, YOf(lane, 0)));

            int lit = lane == 0 ? _litL : _litR;
            var echoes = Echoes(lane);
            double lastLabel = -1e9;
            for (int i = 0; i < echoes.Count; i++)
            {
                var (frac, db, n) = echoes[i];
                double x = XAt(frac), y = Math.Min(YOf(lane, db), b - 2);
                bool sounding = n == lit;
                bool handle = i == 0;
                bool hot = handle && (_drag == lane || (_drag < 0 && _hover == lane));
                var ink = sounding || hot ? NotaPalette.AccentBright : lane == 0 ? NotaPalette.VizInk : NotaPalette.TextSecondary;
                ctx.DrawLine(new Pen(ink, sounding ? 2.4 : 2), new Point(x, b), new Point(x, y));
                if (handle)
                {
                    if (_drag == lane) ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, 0x40), null, new Point(x, y), 8, 8);
                    ctx.DrawEllipse(_drag == lane ? NotaPalette.AccentBright : NotaGraph.Ground, new Pen(NotaPalette.AccentBright, 1.6), new Point(x, y), 3.8, 3.8);
                }
                else ctx.DrawEllipse(ink, null, new Point(x, y), sounding ? 3 : 2.4, sounding ? 3 : 2.4);
                // The level over the first few echoes, where there is room.
                if (i < 5 && x - lastLabel >= 30 && db > FloorDb + 2)
                {
                    var ft = Text(i == 0 && !_ping ? "0 dB" : NotaNum.F($"{db:0}"), hot ? NotaPalette.AccentBright : NotaGraph.AxisInk);
                    ctx.DrawText(ft, new Point(x + 4, Math.Max(t - 3, y - ft.Height - 1)));
                    lastLabel = x;
                }
            }

            string label = lane == 0 ? _labelL : _labelR;
            if (label.Length > 0)
            {
                var ft = Text(label, _freeze && lane == 0 ? NotaPalette.AccentBright : NotaGraph.AxisInk);
                ctx.DrawText(ft, new Point(X1 - ft.Width, lane == 0 ? 3 : (t - ft.Height - 1)));
            }
        }
    }
}

// The Filter tab: the band the repeats live in, on the shared filter graph — a one-pole low cut
// and high cut. Drag near either corner to move it (its own range: 20 Hz … 2 kHz, 200 Hz …
// 20 kHz); double-click or ⌥-click resets it.
internal sealed class DelayBandCurve : FilterGraphBase, IValueHandles
{
    private double _lowHz = 20, _highHz = 20000;
    private bool _lowOff = true, _highOff = true;   // a cut at its end stop is off: no filter there
    private int _drag, _hover;   // 0 none, 1 low, 2 high

    public event Action<int, double>? Changed;   // handle (0 low / 1 high), new normalised value
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int>? Reset;

    public new bool Dragging => _drag != 0;

    public DelayBandCurve() { MinHeight = 60; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeWestEast); }

    protected override (double Lo, double Hi) Span => (20.0, 20000.0);
    protected override (double Top, double Bottom) DbSpan => (6.0, -36.0);
    protected override double CutoffHz
    {
        get
        {
            bool low = _drag == 1 || (_drag == 0 && _hover == 1);
            if (low) return _lowOff ? double.NaN : _lowHz;
            return _highOff ? double.NaN : _highHz;
        }
    }
    protected override double ResponseDb(double f)
    {
        double mag = 1;
        if (!_lowOff) { double rl = f / _lowHz; mag *= rl / Math.Sqrt(1 + rl * rl); }
        if (!_highOff) { double rh = f / _highHz; mag *= 1 / Math.Sqrt(1 + rh * rh); }
        return 20 * Math.Log10(Math.Max(mag, 1e-5));
    }

    /// <summary>The two corners in Hz; an off cut sits at its end of the axis.</summary>
    public void Set(double lowHz, double highHz, bool lowOff, bool highOff)
    {
        _lowHz = lowHz; _highHz = highHz; _lowOff = lowOff; _highOff = highOff;
        InvalidateVisual();
    }

    private int Nearest(double x) => Math.Abs(x - XOf(_lowHz)) <= Math.Abs(x - XOf(_highHz)) ? 1 : 2;
    public int HandleAt(Point p) => Nearest(p.X) - 1;
    public Rect HandleRect(int handle) => ValueEntry.Near(new Point(XOf(handle == 0 ? _lowHz : _highHz), YOf(-3)), Bounds.Size);

    private void Apply(Point p)
    {
        double hz = HzAt(p.X);
        double norm = _drag == 1
            ? Math.Log(Math.Clamp(hz, 20, 2000) / 20) / Math.Log(2000.0 / 20)
            : Math.Log(Math.Clamp(hz, 200, 20000) / 200) / Math.Log(20000.0 / 200);
        Changed?.Invoke(_drag - 1, Math.Clamp(norm, 0, 1));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int h = Nearest(e.GetPosition(this).X);
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) { Reset?.Invoke(h - 1); e.Handled = true; return; }
        _drag = h; base.Dragging = true; GestureBegin?.Invoke(h - 1); e.Pointer.Capture(this); Apply(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != 0) { Apply(e.GetPosition(this)); return; }
        int h = Nearest(e.GetPosition(this).X);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = 0; InvalidateVisual(); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        GestureEnd?.Invoke(_drag - 1); _drag = 0; base.Dragging = false; e.Pointer.Capture(null); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag != 0) { GestureEnd?.Invoke(_drag - 1); _drag = 0; base.Dragging = false; InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = PlotRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        DrawBack(ctx);
        // The band between the corners, lightly washed.
        double xl = XOf(_lowHz), xh = XOf(_highHz);
        if (xh > xl) ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x0C), new Rect(xl, r.Y + 1, xh - xl, r.Height - 2));
        DrawCurve(ctx, ResponseDb);
        DrawFront(ctx, YOf(ResponseDb(CutoffHz)));
        bool saved = base.Dragging;
        foreach (var (hz, idx) in new[] { (_lowHz, 1), (_highHz, 2) })
        {
            base.Dragging = _drag == idx;
            DrawHandle(ctx, new Point(Math.Clamp(XOf(hz), r.X + 5, r.Right - 5), YOf(ResponseDb(hz))));
        }
        base.Dragging = saved;
    }
}
