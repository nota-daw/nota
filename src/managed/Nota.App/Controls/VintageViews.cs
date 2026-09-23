// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Vintage card windows (the centre panel's three tabs):
//  • VtCurveView — Curve: the static transfer curve the engine runs (input level → output,
//                  brass), what reaches the output with Mix and Output (teal), the dry
//                  line dashed, a dot where the input peak sits on the curve and the dust
//                  of Crackle; drag up / down for the drive;
//  • VtWearView  — Wear: the pitch drift over the last ~4 s in cents — wow in brass,
//                  flutter in teal — from the engine's history; drag up / down for the
//                  depth of the selected source, sideways for its rate;
//  • VtHarmView  — Output: the harmonics the curve adds at the input level (f … 7f,
//                  relative to the fundamental) against the hiss floor; drag for the drive.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// A drag on the whole window: vertical and (optionally) horizontal, both from start values.
internal abstract class VtWindowBase : Control
{
    private bool _drag;
    private Point _start;
    private double _v0, _h0;

    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double>? DragV;
    public event Action<double>? DragH;
    public Func<double>? StartV { get; set; }
    public Func<double>? StartH { get; set; }
    public bool Dragging => _drag;

    protected VtWindowBase() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeAll); MinHeight = 36; }

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
        DragV?.Invoke(Math.Clamp(_v0 + dy * 0.6, 0, 1));
        if (StartH is not null) DragH?.Invoke(Math.Clamp(_h0 + dx * 0.6, 0, 1));
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

internal sealed class VtCurveView : VtWindowBase
{
    private float[] _curve = Array.Empty<float>();
    private int _n;
    private double _mix = 1, _out = 1, _in, _crackle;
    private string _legend = "";
    private uint _rng = 0x51u;

    /// <summary>The curve points for input 0..1 (engine telemetry), the mix, the output gain (linear),
    /// the input peak (0..1, 0 = silent) and the crackle amount for the dust.</summary>
    public void Set(float[] src, int offset, int n, double mix, double outGain, double inPeak, double crackle, string legend)
    {
        if (_curve.Length != n) _curve = new float[n];
        if (n > 0 && offset + n <= src.Length) Array.Copy(src, offset, _curve, 0, n);
        _n = n; _mix = mix; _out = outGain; _in = inPeak; _crackle = crackle; _legend = legend;
        InvalidateVisual();
    }

    private double Rand() { _rng = _rng * 1664525u + 1013904223u; return ((_rng >> 8) & 0xFFFF) / 65536.0; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 4);
        double x0 = 3, x1 = w - 3, top = 14, bot = h - 12;
        // y spans the larger of the curve's peak and unity, so a hot curve is never clipped.
        double peak = 1;
        for (int i = 0; i < _n; i++) peak = Math.Max(peak, Math.Abs(_curve[i]) * Math.Max(1, _out));
        double X(double t) => x0 + t * (x1 - x0);
        double Y(double v) => bot - Math.Clamp(v / peak, -0.05, 1.02) * (bot - top);

        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 3, 5 }, 0)), new Point(X(0), Y(0)), new Point(X(1), Y(1)));
        if (_n > 1)
        {
            StreamGeometry Line(Func<int, double> v)
            {
                var g = new StreamGeometry();
                using var c = g.Open();
                for (int i = 0; i < _n; i++)
                {
                    var p = new Point(X((double)i / (_n - 1)), Y(v(i)));
                    if (i == 0) c.BeginFigure(p, false); else c.LineTo(p);
                }
                c.EndFigure(false);
                return g;
            }
            double Heard(int i) => (1 - _mix) * ((double)i / (_n - 1)) + _mix * _curve[i] * _out;
            bool differs = false;
            for (int i = 0; i < _n && !differs; i++) differs = Math.Abs(Heard(i) - _curve[i]) > 0.01;
            if (differs) ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x90), 1.1), Line(Heard));
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Line(i => _curve[i]));

            // where the input sits on the curve now
            if (_in > 0.002)
            {
                double t = Math.Clamp(_in, 0, 1), f = t * (_n - 1);
                int a = (int)Math.Floor(f), b = Math.Min(_n - 1, a + 1);
                double yv = _curve[a] + (_curve[b] - _curve[a]) * (f - a);
                NotaGraph.Node(ctx, new Point(X(t), Y(yv)), true);
            }
            if (differs) NotaGraph.Legend(ctx, w - 5, 14, ("heard", NotaPalette.TealBright, NotaGraph.Mark.Line));
        }

        // dust: re-sprinkled every frame, as many specks as the crackle asks for
        int dots = (int)(_crackle * 26);
        var dust = NotaPalette.Wash(NotaPalette.Accent, 0x70);
        for (int i = 0; i < dots; i++)
        {
            double rr = 0.6 + Rand() * 0.8;
            ctx.DrawEllipse(dust, null, new Point(x0 + Rand() * (x1 - x0), top + Rand() * (bot - top)), rr, rr);
        }

        NotaGraph.Title(ctx, frame, "Transfer curve");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _legend, NotaPalette.TextTertiary);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−∞");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "0 dBFS");
        var mid = NotaGraph.AxisText("−6 dB");
        ctx.DrawText(mid, new Point(X(0.5) - mid.Width / 2, h - mid.Height - 3));
    }
}

internal sealed class VtWearView : VtWindowBase
{
    private float[] _wow = Array.Empty<float>(), _flut = Array.Empty<float>();
    private int _n, _show = 2;
    private string _peak = "", _mid = "", _end = "";

    /// <summary>The histories (cents, oldest first), which to show (0 wow, 1 flutter, 2 both), the
    /// peak text and the axis texts.</summary>
    public void Set(float[] src, int wowAt, int flutAt, int n, int show, string peak, string mid, string end)
    {
        if (_wow.Length != n) { _wow = new float[n]; _flut = new float[n]; }
        if (n > 0 && flutAt + n <= src.Length) { Array.Copy(src, wowAt, _wow, 0, n); Array.Copy(src, flutAt, _flut, 0, n); }
        _n = n; _show = show; _peak = peak; _mid = mid; _end = end;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 6);
        double top = 14, bot = h - 12, cy = (top + bot) / 2, half = Math.Max(4, (bot - top) / 2);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 4 }, 0)), new Point(0, cy), new Point(w, cy));
        if (_n < 2) { Labels(); return; }

        bool showW = _show != 1, showF = _show != 0;
        double range = 5;   // ± cents, grows to fit what is drawn
        for (int i = 0; i < _n; i++)
        {
            double v = (showW ? _wow[i] : 0) + (showF ? _flut[i] : 0);
            range = Math.Max(range, Math.Abs(v));
        }
        range *= 1.15;
        double Y(double c) => cy - Math.Clamp(c / range, -1, 1) * half;

        StreamGeometry Line(Func<int, double> v)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            int px = Math.Max(2, (int)w);
            for (int x = 0; x < px; x++)
            {
                int k = Math.Min(_n - 1, (int)((double)x / (px - 1) * (_n - 1)));
                var p = new Point(x * w / (px - 1), Y(v(k)));
                if (x == 0) c.BeginFigure(p, false); else c.LineTo(p);
            }
            c.EndFigure(false);
            return g;
        }
        if (showF) ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xA0), 1), Line(k => _flut[k] + (showW ? _wow[k] : 0)));
        if (showW) ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Line(k => _wow[k]));
        var scale = NotaGraph.AxisText(NotaNum.F($"±{range:0} ¢"));
        ctx.DrawText(scale, new Point(4, top));
        Labels();

        void Labels()
        {
            NotaGraph.Title(ctx, frame, "Pitch drift");
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _peak, NotaPalette.AccentBright);
            if (_show != 0) NotaGraph.Legend(ctx, w - 5, h - 26, ("flutter", NotaPalette.TealBright, NotaGraph.Mark.Line));
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "0");
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, _end);
            var m = NotaGraph.AxisText(_mid);
            ctx.DrawText(m, new Point((w - m.Width) / 2, h - m.Height - 3));
        }
    }
}

internal sealed class VtHarmView : VtWindowBase
{
    private readonly double[] _h = new double[7];
    private double _floor = -120;
    private string _thd = "", _floorText = "";

    /// <summary>Harmonics 1..7 in dB relative to the fundamental, the hiss floor on the same
    /// scale, and the texts.</summary>
    public void Set(float[] src, int at, double floorDb, string thd, string floorText)
    {
        for (int k = 0; k < 7; k++) _h[k] = at + k < src.Length ? src[at + k] : -120;
        _floor = floorDb; _thd = thd; _floorText = floorText;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 6);
        double top = 16, bot = h - 12;
        const double range = 96;   // dB below the fundamental
        double Y(double db) => bot - Math.Clamp((db + range) / range, 0, 1) * (bot - top);
        double Xk(int k) => w * (0.1 + 0.8 * k / 6.0);

        if (_floor > -range)
        {
            double fy = Y(_floor);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                // a gently tilted floor, as hiss rises a little toward the highs
                c.BeginFigure(new Point(0, fy + 4), false);
                c.LineTo(new Point(w * 0.6, fy)); c.LineTo(new Point(w, fy + 3));
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xA0), 1.1), g);
            var ft = NotaGraph.AxisText(_floorText, NotaPalette.TealBright);
            ctx.DrawText(ft, new Point(w - ft.Width - 5, Math.Max(top, fy - ft.Height - 2)));
        }
        var bar = new Pen(NotaPalette.Accent, 3, lineCap: PenLineCap.Round);
        var dim = new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x50), 3, lineCap: PenLineCap.Round);
        for (int k = 0; k < 7; k++)
        {
            double x = Xk(k);
            double db = k == 0 ? 0 : _h[k];
            if (db <= -range) continue;
            ctx.DrawLine(k > 0 && db < _floor ? dim : bar, new Point(x, bot), new Point(x, Y(db)));
        }

        NotaGraph.Title(ctx, frame, "Harmonics");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _thd, NotaPalette.AccentBright);
        string[] names = { "f", "2f", "3f", "4f", "5f", "6f", "7f" };
        for (int k = 0; k < 7; k++)
        {
            var t = NotaGraph.AxisText(names[k]);
            ctx.DrawText(t, new Point(Math.Clamp(Xk(k) - t.Width / 2, 3, w - t.Width - 3), h - t.Height - 2));
        }
    }
}
