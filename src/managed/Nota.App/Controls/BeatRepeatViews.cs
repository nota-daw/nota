// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Beat Repeat (device kind 11) card windows, read from the engine's telemetry
// (BeatRepeat.h scopeRead) so the picture is what the repeater does:
//  · BrTimelineView — the Timeline tab: two intervals as grid steps, each a bar of the input
//    level — dry steps dim, the captured slice in full neutral ink, every repeat a step dimmer as
//    it decays — with the trigger points, the bar numbers and the playhead (accent).
//  · BrSlicesView — the Slices tab: the last interval's input (grey) on the grid, the gate
//    window from the offset shaded, the captured slice in neutral ink. Drag the offset line or the
//    gate's end sideways (16ths of the interval); double-click resets.
//  · BrFilterView — the Filter tab, on the shared filter graph (FilterGraphBase): the repeat
//    filter (LP / BP / HP) for the first repeat and a later one (dashed grey) when the band
//    narrows and follows the pitch. Drag sideways for the frequency, up / down for the width.
// Drag contract from ShDragView (the Slices tab): left button only, a gesture per drag, double-click resets
// the handle, right-click bubbles to the MIDI-learn menu.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class BrTimelineView : Control
{
    private float[] _lvl = Array.Empty<float>(), _kind = Array.Empty<float>(), _gain = Array.Empty<float>();
    private int _n;
    private double _winBeats = 8, _intervalBeats = 4, _gridBeats = 0.25, _barBeats = 4, _offset, _phase;
    private bool _playing;
    private string _title = Loc.T("device.beat_repeat.capture_and_repeat");

    public BrTimelineView() { ClipToBounds = true; MinHeight = 36; }

    /// <summary>The cells (level dB · kind 0 dry / 1 capture / 2 repeat · repeat gain) over the
    /// window, its length and the interval / grid / bar in beats, the trigger offset (0..1 of the
    /// interval) and the playhead (0..1 of the window).</summary>
    public void Set(float[] src, int at, int n, double winBeats, double intervalBeats, double gridBeats, double barBeats,
        double offset, double phase, bool playing, string title)
    {
        if (_lvl.Length != n) { _lvl = new float[n]; _kind = new float[n]; _gain = new float[n]; }
        if (n > 0 && at + 3 * n <= src.Length)
        {
            Array.Copy(src, at, _lvl, 0, n);
            Array.Copy(src, at + n, _kind, 0, n);
            Array.Copy(src, at + 2 * n, _gain, 0, n);
        }
        _n = n; _winBeats = Math.Max(0.25, winBeats); _intervalBeats = Math.Max(0.125, intervalBeats);
        _gridBeats = Math.Max(1.0 / 64, gridBeats); _barBeats = Math.Max(1, barBeats); _offset = offset; _phase = phase;
        _playing = playing; _title = title;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 6, x1 = w - 6, top = NotaGraph.PlotTop, bot = h - NotaGraph.PlotFloor;
        double X(double beats) => x0 + beats / _winBeats * (x1 - x0);

        // Grid steps over the window (at most 64 drawn; finer grids share a column).
        int steps = (int)Math.Clamp(Math.Round(_winBeats / _gridBeats), 1, 64);
        double sw = (x1 - x0) / steps;
        double gap = sw > 8 ? 3 : sw > 4 ? 1 : 0.5;
        if (_n > 0)
            for (int s = 0; s < steps; s++)
            {
                int a = (int)Math.Floor((double)s * _n / steps), b = Math.Max(a + 1, (int)Math.Floor((double)(s + 1) * _n / steps));
                double lvl = -120, g = 0; int kind = 0;
                for (int i = a; i < b && i < _n; i++)
                {
                    lvl = Math.Max(lvl, _lvl[i]);
                    int k = (int)Math.Round(_kind[i]);
                    if (k > kind || k == kind && _gain[i] > g) { kind = Math.Max(kind, k); g = Math.Max(g, _gain[i]); }
                }
                double v = Math.Clamp((lvl + 48) / 48, 0, 1);
                double bh = Math.Max(2, v * (bot - top));
                var r = new Rect(x0 + s * sw + gap / 2, bot - bh, Math.Max(1, sw - gap), bh);
                IBrush fill = kind switch
                {
                    1 => NotaPalette.VizInk,
                    2 => NotaPalette.Wash(NotaPalette.VizInk, (byte)Math.Clamp(60 + 150 * g, 50, 210)),
                    _ => NotaPalette.Wash(NotaPalette.TextTertiary, 0x38),
                };
                ctx.DrawRectangle(fill, null, r, 2, 2);
            }

        // Interval boundaries (dashed) and the trigger points (ticks at the top).
        var dash = new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 3 }, 0));
        for (double b = _intervalBeats; b < _winBeats - 1e-6; b += _intervalBeats) ctx.DrawLine(dash, new Point(X(b), top - 2), new Point(X(b), bot));
        for (double b = _offset * _intervalBeats; b < _winBeats - 1e-6; b += _intervalBeats)
        {
            double x = X(b);
            ctx.DrawLine(new Pen(NotaPalette.VizInk, 1.4), new Point(x, top - 3), new Point(x, top + 3));
        }

        // Playhead.
        if (_playing)
        {
            double px = x0 + Math.Clamp(_phase, 0, 1) * (x1 - x0);
            ctx.DrawLine(new Pen(NotaPalette.AccentBright, 2), new Point(px, top - 4), new Point(px, bot));
        }

        NotaGraph.Title(ctx, frame, _title);
        NotaGraph.Legend(ctx, w - 6, 3, ("capture", NotaPalette.VizInk, NotaGraph.Mark.Area),
            ("repeat · decay", NotaPalette.Wash(NotaPalette.VizInk, 0x80), NotaGraph.Mark.Area), ("dry", NotaPalette.TextTertiary, NotaGraph.Mark.Area));

        // Bar numbers along the bottom (the window starts on a bar when the interval is ≥ a bar).
        int bars = (int)Math.Ceiling(_winBeats / _barBeats - 1e-6);
        if (bars >= 1 && _winBeats >= _barBeats)
            for (int b = 0; b <= bars; b++)
            {
                double bx = X(b * _barBeats);
                if (bx > x1 - 4) { NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, NotaNum.F($"{b + 1}")); break; }
                ctx.DrawText(NotaGraph.AxisText(NotaNum.F($"{b + 1}")), new Point(bx, h - 11));
            }
        else
        {
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "beat 1");
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, NotaNum.F($"{_winBeats:0.##}\u2009beats"));
        }
    }
}

internal sealed class BrSlicesView : ShDragView
{
    public const int HOffset = 0, HGate = 1;
    private float[] _wave = Array.Empty<float>();
    private int _n;
    private double _offset, _gate = 0.5, _slice = 1.0 / 16, _gridFrac = 1.0 / 16;
    private bool _repeating;

    /// <summary>The interval's input peaks, the offset and gate as fractions of the interval
    /// (steps / 16), the slice and grid step as fractions of it, and whether a repeat plays.</summary>
    public void Set(float[] src, int at, int n, double offset, double gate, double slice, double gridFrac, bool repeating)
    {
        if (_wave.Length != n) _wave = new float[n];
        if (n > 0 && at + n <= src.Length) Array.Copy(src, at, _wave, 0, n);
        _n = n; _offset = offset; _gate = gate; _slice = slice; _gridFrac = Math.Max(1.0 / 256, gridFrac); _repeating = repeating;
        InvalidateVisual();
    }

    private double X0 => 4;
    private double W => Math.Max(1, Bounds.Width - 8);
    private double Xf(double frac) => X0 + Math.Clamp(frac, 0, 1) * W;
    private double GateEnd => Math.Min(1, _offset + Math.Max(_gate, _slice));

    protected override int Hit(Point p)
    {
        double dOff = Math.Abs(p.X - Xf(_offset)), dGate = Math.Abs(p.X - Xf(GateEnd));
        if (dGate <= 8 && dGate < dOff) return HGate;
        return HOffset;   // anywhere else: the offset, the window's main handle
    }
    protected override StandardCursorType CursorFor(int handle) => StandardCursorType.SizeWestEast;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double f = Math.Clamp((p.X - X0) / W, 0, 1);
        double v = handle == HOffset ? f : f - _offset;
        return Math.Round(Math.Clamp(v, 0, 1) * 16) / 16;   // 16ths of the interval
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = NotaGraph.PlotTop, bot = h - NotaGraph.PlotFloor, mid = (top + bot) / 2, amp = (bot - top) / 2;

        // Gate window shaded from the offset.
        ctx.FillRectangle(NotaPalette.Wash(NotaPalette.VizInk, 0x20), new Rect(Xf(_offset), 0, Math.Max(1, Xf(GateEnd) - Xf(_offset)), h));

        // Grid steps (dim) and the 16ths of the interval inside the gate (dashed).
        int steps = (int)Math.Clamp(Math.Round(1 / _gridFrac), 1, 128);
        for (int s = 1; s < steps; s++)
        {
            double f = (double)s / steps;
            ctx.DrawLine(NotaGraph.GridPen, new Point(Xf(f), 0), new Point(Xf(f), h));
        }
        ctx.DrawLine(NotaGraph.GridPen, new Point(0, mid), new Point(w, mid));

        // The interval's input (teal), the captured slice (neutral ink).
        if (_n > 1)
        {
            var inPen = new Pen(NotaPalette.TextTertiary, Math.Max(1, Math.Min(2, W / _n * 0.6)));
            var slicePen = new Pen(NotaPalette.VizInk, Math.Max(1.2, Math.Min(2.2, W / _n * 0.7)));
            double s0 = _offset, s1 = Math.Min(1, _offset + _slice);
            for (int i = 0; i < _n; i++)
            {
                double f = (i + 0.5) / _n;
                double a = Math.Clamp(_wave[i], 0, 1.2) / 1.2;
                a = a > 0 ? Math.Max(0.02, Math.Sqrt(a)) : 0;   // perceptual: quiet material stays visible
                if (a <= 0) continue;
                double x = Xf(f);
                bool inSlice = f >= s0 && f < s1;
                ctx.DrawLine(inSlice ? slicePen : inPen, new Point(x, mid - a * amp), new Point(x, mid + a * amp));
            }
        }

        bool hotO = Active == HOffset, hotG = Active == HGate;
        ctx.DrawLine(new Pen(hotO ? NotaPalette.Accent : NotaPalette.VizInk, hotO ? 1.6 : 1.1), new Point(Xf(_offset), 0), new Point(Xf(_offset), h));
        ctx.DrawLine(new Pen(hotG ? NotaPalette.Accent : NotaPalette.TextSecondary, hotG ? 1.6 : 1.1, new DashStyle(new double[] { 3, 3 }, 0)),
            new Point(Xf(GateEnd), 0), new Point(Xf(GateEnd), h));

        NotaGraph.Title(ctx, frame, _repeating ? Loc.T("device.beat_repeat.captured_sound_repeating") : "Captured sound");
        NotaGraph.Legend(ctx, w - 6, 3, ("slice", NotaPalette.VizInk, NotaGraph.Mark.Line), ("input", NotaPalette.TextTertiary, NotaGraph.Mark.Line));
        int off = (int)Math.Round(_offset * 16), gate = (int)Math.Round(_gate * 16);
        // Math.Clamp throws when min > max: an offset near the end (or a narrow window) leaves no
        // room between the two labels, so the bounds are clamped with Min / Max instead.
        double ox = Math.Max(4, Math.Min(Xf(_offset) + 3, w - 110));
        Text(ctx, NotaNum.F($"offset {off}/16"), ox, h - 11, hotO ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
        double gx = Math.Min(Math.Max(Xf(GateEnd) + 3, ox + 62), w - 70);
        if (ox + 62 <= w - 70 && gx < w - 60) Text(ctx, NotaNum.F($"gate {gate}/16"), gx, h - 11, hotG ? NotaPalette.AccentBright : NotaPalette.TextTertiary);
        TextR(ctx, "16/16", w - 4, h - 11);
    }
}

/// <summary>The Filter tab on the shared filter graph (FilterGraphBase, #531), as Auto Filter
/// draws its own: the repeat filter (LP / BP / HP) for the first repeat in the accent with a light
/// fill and a hollow handle, the cutoff lit on the frequency axis, the hover marker; a later
/// repeat's response dashed grey when the band narrows and follows the pitch. Drag anywhere:
/// sideways for the frequency, up / down for the width (up is narrower); double-click resets both.
/// Off, the response is a grey line with no handle. Frequency axis == the Freq param range
/// (50..18000 Hz), so the handle's x is linear in it.</summary>
internal sealed class BrFilterView : FilterGraphBase
{
    private static readonly IPen LaterPen = new Pen(NotaPalette.TextSecondary, 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private const double FMin = 50, FMax = 18000, OctMin = 0.5, OctSpan = 3;
    private double _hz = 949, _oct = 2, _laterHz = 949, _laterOct = 2;
    private int _type = 1, _laterRepeat;
    private bool _on;
    private Point _grab;
    private double _f0, _w0;   // freq / width (normalized) when the drag began

    public event Action<double>? FreqChanged;
    public event Action<double>? WidthChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action? ResetRequested;

    public BrFilterView()
    {
        MinHeight = 60; MinWidth = 120; ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public new bool Dragging => base.Dragging;
    protected override (double Lo, double Hi) Span => (FMin, FMax);
    protected override (double Top, double Bottom) DbSpan => (12.0, -36.0);
    protected override double CutoffHz => _on ? _hz : double.NaN;
    protected override double ResponseDb(double hz) => Mag(hz, _hz, _oct, _type);

    /// <summary>The filter for the first repeat (Hz, octaves), the type 0 LP / 1 BP / 2 HP,
    /// and a later repeat's band (<paramref name="laterRepeat"/> = 0 hides it).</summary>
    public void Set(bool on, int type, double hz, double oct, int laterRepeat, double laterHz, double laterOct)
    {
        _on = on; _type = type; _hz = hz; _oct = oct; _laterRepeat = laterRepeat; _laterHz = laterHz; _laterOct = laterOct;
        Cursor = new Cursor(on ? StandardCursorType.Cross : StandardCursorType.Arrow);
        InvalidateVisual();
    }

    private static double Norm(double hz) => Math.Log(hz / FMin) / Math.Log(FMax / FMin);
    private static double HzOf(double n) => FMin * Math.Pow(FMax / FMin, Math.Clamp(n, 0, 1));

    private static double Q(double oct, int type)
    {
        double n2 = Math.Pow(2, oct), q = Math.Sqrt(n2) / (n2 - 1);
        return type == 1 ? q : Math.Max(0.5, q);
    }
    private static double Mag(double f, double fc, double oct, int type)
    {
        double x = f / fc, q = Q(oct, type);
        double den = Math.Sqrt(Math.Pow(1 - x * x, 2) + Math.Pow(x / q, 2));
        double m = type switch { 0 => 1 / den, 2 => x * x / den, _ => x / q / den };
        return 20 * Math.Log10(Math.Max(1e-6, m));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!_on || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(); InvalidateVisual(); return; }
        base.Dragging = true;
        _grab = e.GetPosition(this); _f0 = Norm(_hz); _w0 = (_oct - OctMin) / OctSpan;
        GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!base.Dragging) return;
        var r = PlotRect; var p = e.GetPosition(this);
        if (r.Width <= 0 || r.Height <= 0) return;
        // From where it was grabbed, so a click changes nothing: sideways the frequency, down wider.
        double f = Math.Clamp(_f0 + (p.X - _grab.X) / r.Width, 0, 1);
        double wv = Math.Clamp(_w0 + (p.Y - _grab.Y) / (r.Height * 0.8), 0, 1);
        _hz = HzOf(f); _oct = OctMin + wv * OctSpan;
        FreqChanged?.Invoke(f); WidthChanged?.Invoke(wv);
        InvalidateVisual();
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
        if (base.Dragging) { base.Dragging = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = PlotRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        DrawBack(ctx);
        if (!_on)
        {
            DrawCurve(ctx, f => Mag(f, _hz, _oct, _type), NotaPalette.BorderStrong, fill: false);
            DrawFront(ctx);
            return;
        }
        bool later = _laterRepeat > 1;
        if (later)
        {
            int n = (int)Math.Clamp(r.Width / 1.5, 24, 420);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i < n; i++)
                {
                    double x = r.X + r.Width * i / (n - 1);
                    var pt = new Point(x, YOf(Mag(HzAt(x), _laterHz, _laterOct, _type)));
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, LaterPen, geo);
        }
        DrawCurve(ctx, f => Mag(f, _hz, _oct, _type));
        double cx = XOf(_hz), cy = YOf(Mag(_hz, _hz, _oct, _type));
        DrawFront(ctx, cy);
        DrawHandle(ctx, new Point(cx, cy));
        if (later)
            NotaGraph.Legend(ctx, r.Right - 5, r.Y + 3, (NotaNum.F($"repeat {_laterRepeat}"), NotaPalette.TextSecondary, NotaGraph.Mark.Dashed));
    }
}
