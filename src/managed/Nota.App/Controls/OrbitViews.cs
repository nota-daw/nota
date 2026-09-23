// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Orbit views (device kind 9) — the pictures on the Orbit card:
//   OrbitGainView — both channels' gain over two LFO cycles (L brass, R teal, R offset by Phase),
//                   the floor the depth reaches, and a running head with the gain dots on it.
//                   Drag up / down for Amount, left / right for Phase; double-click resets both.
//   OrbitPanBar   — the stereo position now on an L—R line, over the swing the LFO covers.
//   OrbitMeter    — a thin output meter in its channel's chroma.
// OrbitMath mirrors AutoPan.h's LFO so the picture matches the sound; the S&H steps come from
// the engine's telemetry (the four cycles around the window).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class OrbitMath
{
    public static readonly string[] Waves = { "Sine", "Tri", "Saw", "Sqr", "S&H" };
    public static readonly string[] WaveNames = { "SINE", "TRIANGLE", "SAW", "SQUARE", "S&H" };
    // Sync divisions, slowest → fastest (AutoPan::kDivBeats / kDivNames).
    public static readonly double[] DivBeats = { 16, 8, 4, 3, 2, 4.0 / 3, 1.5, 1, 2.0 / 3, 0.75, 0.5, 1.0 / 3, 0.375, 0.25, 1.0 / 6, 0.125 };
    public static readonly string[] DivNames = { "4/1", "2/1", "1/1", "1/2D", "1/2", "1/2T", "1/4D", "1/4", "1/4T", "1/8D", "1/8", "1/8T", "1/16D", "1/16", "1/16T", "1/32" };

    public static int DivIndex(double v) => Math.Clamp((int)Math.Round(v * (DivBeats.Length - 1)), 0, DivBeats.Length - 1);
    public static double DivNorm(int i) => (double)Math.Clamp(i, 0, DivBeats.Length - 1) / (DivBeats.Length - 1);
    public static int WaveIndex(double v) => Math.Clamp((int)Math.Round(v * 4), 0, 4);
    public static double FreeHz(double v) => 0.01 * Math.Pow(4000, Math.Clamp(v, 0, 1));

    /// <summary>The LFO in [−1, 1] at window position <paramref name="p"/> (cycles, may run past 2);
    /// <paramref name="sh"/> holds the S&amp;H steps of cycles −1, 0, 1, 2 of the window.</summary>
    public static double Lfo(int wave, double shape, double p, ReadOnlySpan<float> sh)
    {
        int k = (int)Math.Floor(p);
        double f = p - k, v;
        switch (wave)
        {
            case 1: v = 4.0 * Math.Abs(f - 0.5) - 1.0; break;
            case 2: v = 2.0 * f - 1.0; break;
            case 3: return f < 0.5 ? 1 : -1;
            case 4:
            {
                double a = Step(sh, k - 1), b = Step(sh, k);
                double t = shape > 1e-3 ? Math.Min(1, f / shape) : 1;
                t = t * t * (3 - 2 * t);
                return a + (b - a) * t;
            }
            default: v = Math.Sin(2 * Math.PI * f); break;
        }
        if (shape > 1e-3)
        {
            double kk = 1 + shape * 12;
            v = v * (1 - shape) + Math.Tanh(kk * v) / Math.Tanh(kk) * shape;
        }
        return v;
    }

    private static double Step(ReadOnlySpan<float> sh, int k) => sh.Length >= 4 ? sh[Math.Clamp(k + 1, 0, 3)] : 0;

    /// <summary>A channel's gain for an LFO value (AutoPan.h: Mix blends toward 1).</summary>
    public static double Gain(double lfo, double amount, double mix) => 1 - mix * amount * 0.5 * (1 - lfo);

    public static string Db(double g) => g < 0.001 ? "−∞" : g >= 0.9995 ? "0.0" : NotaNum.F($"{20 * Math.Log10(g):0.0}");
}

internal sealed class OrbitGainView : Control
{
    private int _wave;
    private double _shape, _amount = 0.7, _phase = 0.5, _mix = 1, _head, _gl = 1, _gr = 1, _periodMs = 690;
    private bool _on = true, _drag;
    private readonly float[] _sh = new float[4];
    private Point _start;
    private double _a0, _p0;

    /// <summary>Amount and Phase (0..1) at the start of a drag.</summary>
    public Func<(double Amount, double Phase)>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double, double>? Changed;
    public event Action? ResetRequested;
    public bool Dragging => _drag;

    public OrbitGainView() { ClipToBounds = true; MinHeight = 40; Cursor = new Cursor(StandardCursorType.SizeAll); }

    public void Set(int wave, double shape, double amount, double phase, double mix, double head, double gl, double gr,
        ReadOnlySpan<float> sh, double periodMs, bool on)
    {
        _wave = wave; _shape = shape; _amount = amount; _phase = phase; _mix = mix; _head = head;
        _gl = gl; _gr = gr; _periodMs = periodMs; _on = on;
        for (int i = 0; i < 4; i++) _sh[i] = i < sh.Length ? sh[i] : 0f;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(); e.Handled = true; return; }
        var v = Value?.Invoke() ?? (0.7, 0.5);
        _a0 = v.Amount; _p0 = v.Phase; _start = e.GetPosition(this); _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        var p = e.GetPosition(this);
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double k = fine ? 0.2 : 1;
        // The depth follows the floor line: dragging down lowers the floor.
        double a = Math.Clamp(_a0 + (p.Y - _start.Y) * k / Math.Max(60, Bounds.Height * 0.88), 0, 1);
        // Phase: one window width = 360°.
        double ph = Math.Clamp(_p0 + (p.X - _start.X) * k / Math.Max(120, Bounds.Width), 0, 1);
        Changed?.Invoke(a, ph);
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
        double x0 = 1, x1 = w - 1, top = h * 0.06, bot = h * 0.94;
        double X(double p) => x0 + p / 2 * (x1 - x0);
        double Y(double g) => top + (1 - Math.Clamp(g, 0, 1)) * (bot - top);

        for (int i = 1; i <= 3; i++)
        {
            double gy = Y(1 - i * 0.25);
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, gy), new Point(x1, gy));
        }
        ctx.DrawLine(new Pen(NotaPalette.GraphBorder, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(X(1), 0), new Point(X(1), h));

        double floor = 1 - _amount * _mix;
        double fy = Y(floor);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 4, 3 }, 0)), new Point(x0, fy), new Point(x1, fy));

        // R first so L reads on top where they meet (tremolo).
        IBrush inkL = _on ? NotaPalette.Accent : NotaPalette.TextAxis;
        IBrush inkR = _on ? NotaPalette.TealBright : NotaPalette.TextAxis;
        Curve(ctx, _phase, inkR);
        Curve(ctx, 0, inkL);

        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopLeft, "0\u2009dB");
        // The floor's level over its line; near the bottom it steps right of the 0° corner label.
        var fl = NotaGraph.AxisText(OrbitMath.Db(floor) + "\u2009dB");
        var zero = NotaGraph.AxisText("0°");
        double flX = fy > h - 4 - zero.Height - fl.Height ? 4 + zero.Width + 8 : 4;
        if (fy - fl.Height - 1 > fl.Height + 4) ctx.DrawText(fl, new Point(flX, fy - fl.Height - 1));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "0°");
        var mid = NotaGraph.AxisText("360°");
        ctx.DrawText(mid, new Point(X(1) - mid.Width / 2, h - 4 - mid.Height + 1));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, NotaNum.F($"{_periodMs * 2:0}\u2009ms"));

        if (_on)
        {
            double hx = X(Math.Clamp(_head, 0, 2));
            ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1), new Point(hx, 0), new Point(hx, h));
            NotaGraph.Node(ctx, new Point(hx, Y(_gr)), true, NotaPalette.TealBright);
            NotaGraph.Node(ctx, new Point(hx, Y(_gl)), true, NotaPalette.AccentBright);
        }

        void Curve(DrawingContext dc, double off, IBrush ink)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                int n = Math.Max(64, (int)(x1 - x0));
                for (int i = 0; i <= n; i++)
                {
                    double p = 2.0 * i / n;
                    double gain = _on ? OrbitMath.Gain(OrbitMath.Lfo(_wave, _shape, p + off, _sh), _amount, _mix) : 1;
                    var pt = new Point(X(p), Y(gain));
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            dc.DrawGeometry(null, NotaGraph.SecondaryPen(ink, 1.6), geo);
        }
    }
}

internal sealed class OrbitPanBar : Control
{
    private double _pan, _min, _max;

    public OrbitPanBar() { Height = 9; }

    public void Set(double pan, double min, double max)
    {
        if (Math.Abs(pan - _pan) < 1e-4 && Math.Abs(min - _min) < 1e-4 && Math.Abs(max - _max) < 1e-4) return;
        _pan = pan; _min = min; _max = max;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 8) return;
        double x0 = 4.5, x1 = w - 4.5, cy = h / 2;
        double X(double v) => x0 + (Math.Clamp(v, -1, 1) + 1) / 2 * (x1 - x0);
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, cy), new Point(w, cy));
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1), new Point(Math.Round(w / 2) + 0.5, cy - 3.5), new Point(Math.Round(w / 2) + 0.5, cy + 3.5));
        if (_max > _min)
            ctx.DrawRectangle(NotaPalette.TrackOff, null, new RoundedRect(new Rect(X(_min), cy - 1.5, Math.Max(1, X(_max) - X(_min)), 3), 1.5));
        ctx.DrawEllipse(NotaPalette.TextPrimary, new Pen(NotaPalette.SurfaceCard, 2), new Point(X(_pan), cy), 3.5, 3.5);
    }
}

internal sealed class OrbitMeter : Control
{
    private double _norm;
    public IBrush Ink { get; set; } = NotaPalette.Accent;

    public OrbitMeter() { Height = 4; }

    public void Set(double norm)
    {
        norm = Math.Clamp(norm, 0, 1);
        if (Math.Abs(norm - _norm) < 1e-4) return;
        _norm = norm; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new RoundedRect(new Rect(0, 0, w, h), h / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, r);
        if (_norm > 0)
            using (ctx.PushClip(r)) ctx.DrawRectangle(Ink, null, new Rect(0, 0, w * _norm, h));
    }
}
