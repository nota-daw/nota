// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Auto Filter card views (besides the response curve):
//  • AfFader     — the SHAPE column's vertical faders (FRQ / RES): the set value as a filled
//                  well with a handle, plus a thin marker where modulation has it now;
//  • AfEnvView   — the Envelope tab's window: the input envelope (teal) and the cutoff it
//                  drives (brass) over the last few hundred ms, with the base cutoff dashed;
//                  drag up / down for the amount;
//  • AfLfoView   — the LFO tab's window: the LFO's cutoff motion over two bars (or a few
//                  cycles in free time), raw shape dashed teal, the smoothed motion in brass,
//                  a playhead dot at the live phase; drag up / down for the amount, sideways
//                  for the start phase.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AfFader : Control
{
    private double _v, _live = double.NaN;
    private bool _drag;

    public IBrush Ink { get; init; } = NotaPalette.Accent;
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;

    public event Action<double>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public AfFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    /// <summary>The set value, and where modulation has it now (NaN = no marker).</summary>
    public void Set(double v, double live)
    {
        if (!_drag) _v = Math.Clamp(v, 0, 1);
        _live = double.IsNaN(live) ? live : Math.Clamp(live, 0, 1);
        InvalidateVisual();
    }

    private void Apply(PointerEventArgs e)
    {
        double h = Bounds.Height; if (h <= 2) return;
        _v = Math.Clamp(1 - e.GetPosition(this).Y / h, 0, 1);
        Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); _v = Default; Changed?.Invoke(_v); GestureEnd?.Invoke();
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; GestureBegin?.Invoke(); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 4) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        double r = Math.Min(8, w / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, r, r);
        double y = (1 - _v) * h;
        using (ctx.PushClip(new RoundedRect(rect, r)))
        {
            ctx.FillRectangle(NotaPalette.Wash((SolidColorBrush)Ink, 0x48), new Rect(0, y, w, h - y));
            if (!double.IsNaN(_live) && Math.Abs(_live - _v) > 0.005)
            {
                double ly = Math.Clamp((1 - _live) * h, 1, h - 1);
                ctx.DrawLine(new Pen(NotaPalette.Wash((SolidColorBrush)Ink, 0xB0), 1, new DashStyle(new double[] { 2, 1 }, 0)), new Point(2, ly), new Point(w - 2, ly));
            }
        }
        ctx.DrawRectangle(null, new Pen(NotaPalette.BorderDefault, 1), rect, r, r);
        double hy = Math.Clamp(y, 1.5, h - 2.5);
        ctx.DrawRectangle(Ink, null, new Rect(1.5, hy - 1, w - 3, 2), 1, 1);
    }
}

// Shared by the two time windows: a vertical drag gesture on the whole window.
internal abstract class AfWindowBase : Control
{
    private bool _drag;
    private Point _start;
    private double _v0, _h0;

    public event Action? GestureBegin;
    public event Action? GestureEnd;
    /// <summary>Vertical drag: the new value for the dragged param (from DragStart's start value).</summary>
    public event Action<double>? DragV;
    /// <summary>Horizontal drag: the new value (0..1, wraps) — only raised when HorizontalDrag.</summary>
    public event Action<double>? DragH;
    public Func<double>? StartV { get; set; }
    public Func<double>? StartH { get; set; }
    public bool Dragging => _drag;

    protected AfWindowBase() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeAll); MinHeight = 36; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _drag = true; _start = e.GetPosition(this);
        _v0 = StartV?.Invoke() ?? 0; _h0 = StartH?.Invoke() ?? 0;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        var p = e.GetPosition(this);
        double dy = (_start.Y - p.Y) / Math.Max(40, Bounds.Height), dx = (p.X - _start.X) / Math.Max(40, Bounds.Width);
        DragV?.Invoke(Math.Clamp(_v0 + dy * 0.5, 0, 1));
        if (StartH is not null) { double h = _h0 + dx; DragH?.Invoke(h - Math.Floor(h)); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected static void Grid(DrawingContext ctx, double w, double h, int cols)
    {
        for (int i = 1; i < 4; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(0, h * i / 4), new Point(w, h * i / 4));
        for (int i = 1; i < cols; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(w * i / cols, 0), new Point(w * i / cols, h));
    }
}

internal sealed class AfEnvView : AfWindowBase
{
    private float[] _env = Array.Empty<float>(), _cut = Array.Empty<float>();
    private int _n;
    private double _base = 0.5, _peakHz, _windowMs = 600;
    private string _baseText = "", _winText = "";

    /// <summary>The last <paramref name="n"/> history points (1 ms each), oldest first.</summary>
    public void Set(float[] env, float[] cut, int n, double baseNorm, string baseText, double windowMs, double peakHz)
    {
        _env = env; _cut = cut; _n = n; _base = baseNorm; _baseText = baseText; _windowMs = windowMs; _peakHz = peakHz;
        _winText = windowMs >= 1000 ? NotaNum.F($"{windowMs / 1000:0.#} s") : NotaNum.F($"{windowMs:0} ms");
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 6);
        double top = 14, bot = h - 12, span = Math.Max(1, bot - top);
        double YCut(double norm) => bot - Math.Clamp(norm, 0, 1) * span;
        double YEnv(double lin) { double db = lin > 1e-6 ? 20 * Math.Log10(lin) : -120; return bot - Math.Clamp((db + 48) / 48, 0, 1) * span; }

        // base cutoff, dashed
        double by = YCut(_base);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 4 }, 0)), new Point(0, by), new Point(w, by));

        if (_n > 1)
        {
            StreamGeometry Line(float[] src, Func<double, double> y)
            {
                var g = new StreamGeometry();
                using var c = g.Open();
                c.BeginFigure(new Point(0, y(src[0])), false);
                // at most one vertex per pixel: the peak of the points under it keeps transients
                int px = Math.Max(2, (int)w);
                for (int x = 1; x < px; x++)
                {
                    int a = (int)((double)(x - 1) / px * _n), b = Math.Max(a + 1, (int)((double)x / px * _n));
                    double best = src[Math.Min(a, _n - 1)];
                    for (int k = a; k < b && k < _n; k++) best = Math.Max(best, src[k]);
                    c.LineTo(new Point(x * w / (px - 1), y(best)));
                }
                c.EndFigure(false);
                return g;
            }
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xC0), 1.1), Line(_env, v => YEnv(v)));
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Line(_cut, v => YCut(v)));
        }

        NotaGraph.Title(ctx, frame, "Cutoff over time");
        string pk = NotaNum.Hz(_peakHz);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, pk, NotaPalette.AccentBright);
        NotaGraph.Legend(ctx, w - 5, 16, ("input", NotaPalette.TealBright, NotaGraph.Mark.Line));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "0");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, _winText);
        var bt = NotaGraph.AxisText(_baseText);
        ctx.DrawText(bt, new Point((w - bt.Width) / 2, h - bt.Height - 3));
    }
}

internal sealed class AfLfoView : AfWindowBase
{
    private int _wave;
    private double _morph, _amount, _offset, _stereo, _smoothCycles, _cycles = 8, _phase = double.NaN;
    private bool _on = true;
    private string _title = "LFO → Freq", _amt = "", _morphText = "", _mid = "", _end = "";
    private readonly Random _rng = new(7);
    private readonly double[] _sh = new double[64];

    public AfLfoView() { for (int i = 0; i < _sh.Length; i++) _sh[i] = _rng.NextDouble() * 2 - 1; }

    /// <summary>
    /// Shape, depth (0..1), start offset and stereo offset (cycles), the smoothing time in
    /// cycles, how many cycles the window shows, and the live phase within the current
    /// cycle (NaN hides the playhead). Labels are pre-formatted by the card.
    /// </summary>
    public void Set(bool on, int wave, double morph, double amount, double offset, double stereo, double smoothCycles,
        double cycles, double livePhase, string title, string amt, string morphText, string mid, string end)
    {
        _on = on; _wave = wave; _morph = morph; _amount = amount; _offset = offset; _stereo = stereo;
        _smoothCycles = smoothCycles; _cycles = Math.Max(0.25, cycles); _phase = livePhase;
        _title = title; _amt = amt; _morphText = morphText; _mid = mid; _end = end;
        InvalidateVisual();
    }

    private double Shape(double ph, int cycle)
    {
        ph -= Math.Floor(ph);
        return _wave switch
        {
            0 => Math.Sin(2 * Math.PI * ph),
            1 => 4 * Math.Abs(ph - 0.5) - 1,
            2 => 2 * ph - 1,
            3 => ph < 0.5 + _morph * 0.49 ? 1 : -1,
            _ => _sh[((cycle % _sh.Length) + _sh.Length) % _sh.Length],
        };
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        int cols = _cycles <= 16 && Math.Abs(_cycles - Math.Round(_cycles)) < 1e-6 ? (int)Math.Round(_cycles) : 8;
        Grid(ctx, w, h, Math.Clamp(cols, 2, 16));
        double mid = h / 2, amp = (h / 2 - 12) * Math.Max(0.04, _amount);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 4 }, 0)), new Point(0, mid), new Point(w, mid));

        int px = Math.Max(8, (int)w);
        double a = _smoothCycles <= 1e-4 ? 0 : Math.Exp(-(_cycles / px) / _smoothCycles);   // one-pole per pixel
        StreamGeometry Trace(double extra, bool smooth)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            // run the smoother over one cycle first so the drawn part starts settled
            double s = 0;
            if (smooth) for (int x = -px; x < 0; x++) { double ph = x * _cycles / px + _offset + extra; s = Shape(ph, (int)Math.Floor(ph)) + a * (s - Shape(ph, (int)Math.Floor(ph))); }
            for (int x = 0; x <= px; x++)
            {
                double ph = x * _cycles / px + _offset + extra;
                double v = Shape(ph, (int)Math.Floor(ph));
                if (smooth) { s = v + a * (s - v); v = s; }
                var p = new Point(x * w / px, mid - v * amp);
                if (x == 0) c.BeginFigure(p, false); else c.LineTo(p);
            }
            c.EndFigure(false);
            return g;
        }
        if (_on)
        {
            var dashed = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x99), 1.1, new DashStyle(new double[] { 4, 4 }, 0));
            ctx.DrawGeometry(null, dashed, Trace(0, false));
            if (_stereo > 1e-4) ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Rose, 0x90), 1.1), Trace(_stereo, a > 0));
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Trace(0, a > 0));

            if (!double.IsNaN(_phase))
            {
                // the playhead sits in the first cycle of the window at the live phase
                double rel = _phase - _offset; rel -= Math.Floor(rel);
                double x = rel / _cycles * w;
                double v = Shape(_phase, 0);
                ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x80), 1), new Point(x, 0), new Point(x, h));
                ctx.DrawEllipse(NotaPalette.Accent, null, new Point(x, mid - v * amp), 3, 3);
            }
        }

        NotaGraph.Title(ctx, frame, _title);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _amt, NotaPalette.AccentBright);
        if (_morphText.Length > 0)
        {
            var t = NotaGraph.AxisText(_morphText, NotaPalette.TealBright);
            ctx.DrawText(t, new Point(Math.Min(86, w / 2), 3));
        }
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "1");
        var m = NotaGraph.AxisText(_mid);
        ctx.DrawText(m, new Point(w / 2 + 2, h - m.Height - 3));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, _end);
    }
}
