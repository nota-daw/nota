// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Rhythm's windows (the almanac's Rhythm card):
//
//   RhythmHitView     the selected synth voice's hit: the amp contour (brass) over the pitch
//                     drop (deep brass) or the attack click, the trigger as a teal tick; the
//                     corner reads the pitch time · the decay. Drag it: across = Decay,
//                     up / down = Tune. Lights when the voice fires.
//   RhythmSampleView  the selected sample voice: the file as bars in the voice's hue, the
//                     played region between a brass Start line and a dashed brass End line
//                     (drag either), the part outside dimmed, the play position in teal.
//   RhythmStepStrip   the sixteen steps of the selected voice and, under them, the velocity
//                     lane. Click toggles, shift-click accents, alt-click makes a quiet step;
//                     drag across paints, drag up / down (or the wheel) sets the velocity;
//                     right-click asks the card for the step menu.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal static class RhythmInk
{
    public static FormattedText Mono(string t, IBrush ink, double size = 7)
        => new(t, NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public static void TopRight(DrawingContext ctx, double w, string t, IBrush ink)
    {
        var ft = Mono(t, ink);
        ctx.DrawText(ft, new Point(w - 5 - ft.Width, 3));
    }

    public static void BottomRight(DrawingContext ctx, double w, double h, string t)
    {
        var ft = Mono(t, NotaPalette.TextAxis);
        ctx.DrawText(ft, new Point(w - 5 - ft.Width, h - 2 - ft.Height));
    }
}

internal sealed class RhythmHitView : Control
{
    private string _name = "Kick", _caption = "";
    private double _decaySec = 0.3, _pitchSec, _windowSec = 1, _punch = 0.5, _flash;

    /// <summary>Drag gesture: begin, a change (Δdecay, Δtune in normalized units), end.</summary>
    public Action? DragBegin, DragEnd;
    public Action<double, double>? Dragged;

    private Point _last;
    private bool _dragging;

    public RhythmHitView()
    {
        Cursor = new Cursor(StandardCursorType.SizeAll);
        ClipToBounds = true;
    }

    public void Set(string name, double decaySec, double pitchSec, double windowSec, double punch, double flash, string caption)
    {
        _name = name; _decaySec = decaySec; _pitchSec = pitchSec; _windowSec = Math.Max(0.05, windowSec);
        _punch = punch; _flash = flash; _caption = caption;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _last = e.GetPosition(this); _dragging = true;
        e.Pointer.Capture(this);
        DragBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        double fine = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 0.25 : 1.0;
        double dx = (p.X - _last.X) / Math.Max(40, Bounds.Width) * fine;
        double dy = -(p.Y - _last.Y) / Math.Max(40, Bounds.Height) * fine;
        _last = p;
        Dragged?.Invoke(dx, dy);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        DragEnd?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        const double x0 = 8, top = 20;
        double baseY = h - 20, x1 = w - 4;
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, baseY + 2), new Point(w - 1, baseY + 2));
        // The trigger.
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x80), 1), new Point(x0, 1), new Point(x0, h - 1));

        double span = x1 - x0 - 2;
        double X(double sec) => x0 + 2 + Math.Min(1.0, sec / _windowSec) * span;

        // The pitch drop (Kick / Tom) — or, for the other voices, the attack click by Punch.
        if (_pitchSec > 0)
        {
            var pg = new StreamGeometry();
            using (var g = pg.Open())
            {
                g.BeginFigure(new Point(x0 + 2, top + 6), false);
                for (double x = x0 + 2; x <= x1; x += 2)
                {
                    double t = (x - x0 - 2) / span * _windowSec;
                    double e = Math.Exp(-6.9 * t / _pitchSec * 0.5);
                    g.LineTo(new Point(x, baseY - (baseY - top - 6) * (0.18 + 0.82 * e)));
                }
            }
            ctx.DrawGeometry(null, NotaGraph.SecondaryPen(NotaPalette.AccentDim, 1.2), pg);
        }
        else if (_punch > 0.02)
        {
            double ch = (baseY - top) * (0.25 + 0.6 * _punch);
            ctx.DrawLine(NotaGraph.SecondaryPen(NotaPalette.AccentDim, 1.2), new Point(x0 + 4, baseY), new Point(x0 + 4, baseY - ch));
        }

        // The amp contour: a fast rise, then the decay (−20 dB at the Decay time, so the tail reads).
        var ag = new StreamGeometry();
        using (var g = ag.Open())
        {
            g.BeginFigure(new Point(x0 - 2, h - 4), false);
            g.LineTo(new Point(x0 + 2, top));
            double end = X(_decaySec * 2.5);
            for (double x = x0 + 3; x <= x1; x += 1.5)
            {
                double t = (x - x0 - 2) / span * _windowSec;
                double e = Math.Exp(-2.3 * t / Math.Max(1e-3, _decaySec));
                g.LineTo(new Point(x, baseY - (baseY - top) * e));
                if (x > end) { g.LineTo(new Point(x1, baseY)); break; }
            }
        }
        var ampInk = _flash > 0.3 ? NotaPalette.AccentBright : NotaPalette.Accent;
        ctx.DrawGeometry(null, new Pen(ampInk, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), ag);

        NotaGraph.Title(ctx, new Rect(0, 0, w, h), "HIT");
        RhythmInk.TopRight(ctx, w, _name, NotaPalette.AccentBright);
        RhythmInk.BottomRight(ctx, w, h, _caption);
    }
}

internal sealed class RhythmSampleView : Control
{
    private float[] _peaks = Array.Empty<float>();
    private string _name = "", _caption = "";
    private double _start, _end = 1, _pos = -1;
    private bool _reverse, _has;
    private IBrush _hue = NotaPalette.Teal;

    /// <summary>A handle drag: which (0 Start, 1 End), then its new place 0..1 of the file.</summary>
    public Action<int>? DragBegin, DragEnd;
    public Action<int, double>? Dragged;
    /// <summary>Double-click: load a file.</summary>
    public Action? LoadRequested;

    private int _drag = -1;

    public RhythmSampleView() { ClipToBounds = true; }

    public void SetSamples(float[] interleaved, int channels)
    {
        const int cols = 64;
        var p = new float[cols];
        channels = Math.Max(1, channels);
        long frames = interleaved.Length / channels;
        if (frames > 0)
            for (int c = 0; c < cols; c++)
            {
                long a = frames * c / cols, b = Math.Max(a + 1, frames * (c + 1) / cols);
                float mx = 0;
                for (long f = a; f < b && f < frames; f++)
                    for (int ch = 0; ch < channels; ch++) mx = Math.Max(mx, Math.Abs(interleaved[f * channels + ch]));
                p[c] = Math.Min(1f, mx);
            }
        _peaks = p;
        _has = frames > 0;
        InvalidateVisual();
    }

    public void Set(string name, IBrush hue, double start, double end, bool reverse, double pos, string caption)
    {
        _name = name; _hue = hue; _start = start; _end = end; _reverse = reverse; _pos = pos; _caption = caption;
        InvalidateVisual();
    }

    private const double Pad = 6;
    private double X(double f) => Pad + Math.Clamp(f, 0, 1) * Math.Max(1, Bounds.Width - 2 * Pad);
    private double F(double x) => Math.Clamp((x - Pad) / Math.Max(1, Bounds.Width - 2 * Pad), 0, 1);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag >= 0) { Dragged?.Invoke(_drag, F(p.X)); return; }
        bool near = _has && (Math.Abs(p.X - X(_start)) < 6 || Math.Abs(p.X - X(_end)) < 6);
        Cursor = near ? new Cursor(StandardCursorType.SizeWestEast) : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount >= 2 || !_has) { LoadRequested?.Invoke(); return; }
        var p = e.GetPosition(this);
        _drag = Math.Abs(p.X - X(_start)) <= Math.Abs(p.X - X(_end)) ? 0 : 1;
        e.Pointer.Capture(this);
        DragBegin?.Invoke(_drag);
        Dragged?.Invoke(_drag, F(p.X));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        int d = _drag; _drag = -1;
        e.Pointer.Capture(null);
        DragEnd?.Invoke(d);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double mid = (14 + h - 12) / 2, amp = (h - 26) / 2;
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, mid), new Point(w - 1, mid));
        NotaGraph.Title(ctx, new Rect(0, 0, w, h), "SAMPLE");
        if (!_has)
        {
            var t = RhythmInk.Mono("double-click or drop a file", NotaPalette.TextTertiary);
            ctx.DrawText(t, new Point((w - t.Width) / 2, mid - t.Height / 2));
            return;
        }

        var inPen = new Pen(_hue, 2.2, lineCap: PenLineCap.Round);
        var outPen = new Pen(NotaPalette.BorderStrong, 2.2, lineCap: PenLineCap.Round);
        int n = _peaks.Length, bars = Math.Max(4, (int)((w - 2 * Pad) / 5));
        for (int i = 0; i < bars; i++)
        {
            double f = (i + 0.5) / bars, x = X(f);
            double a = Math.Max(0.5, _peaks[Math.Min(n - 1, (int)(f * n))] * amp);
            bool inside = f >= _start && f <= _end;
            ctx.DrawLine(inside ? inPen : outPen, new Point(x, mid - a), new Point(x, mid + a));
        }

        var brass = new Pen(NotaPalette.Accent, 1);
        var dashed = new Pen(NotaPalette.Accent, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        double xs = X(_start), xe = X(_end);
        ctx.DrawLine(_reverse ? dashed : brass, new Point(xs, 1), new Point(xs, h - 1));
        ctx.DrawLine(_reverse ? brass : dashed, new Point(xe, 1), new Point(xe, h - 1));
        if (_pos >= 0)
        {
            double xp = X(_pos);
            ctx.DrawLine(new Pen(NotaPalette.TealBright, 1), new Point(xp, 12), new Point(xp, h - 12));
        }

        RhythmInk.TopRight(ctx, w, _name, NotaPalette.AccentBright);
        RhythmInk.BottomRight(ctx, w, h, _caption);
    }
}

internal sealed class RhythmStepStrip : Control
{
    private const int N = RhythmModel.Steps;
    private const double Gap = 2, LaneH = 4, LaneGap = 3;

    public readonly bool[] On = new bool[N], Acc = new bool[N];
    public readonly float[] Vel = new float[N];
    private int _head = -1;

    /// <summary>A step changed (its on / velocity / accent now in the arrays).</summary>
    public Action<int>? Commit;
    /// <summary>Right-click on a step: the card shows its menu.</summary>
    public Action<int>? StepMenu;

    private int _pressStep = -1, _lastPaint = -1;
    private bool _paintOn, _velMode;
    private Point _pressAt;
    private float _pressVel;

    public RhythmStepStrip()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Click: step on / off · Shift-click: accent · Alt-click: quiet step\nDrag across to paint · drag up / down or scroll to set the velocity · right-click for more");
    }

    public void SetHead(int head) { if (head != _head) { _head = head; InvalidateVisual(); } }
    public void Changed() => InvalidateVisual();

    private double CellW => (Bounds.Width - Gap * (N - 1)) / N;
    private int StepAt(double x) => Math.Clamp((int)Math.Floor(x / (CellW + Gap)), 0, N - 1);

    private void Put(int s, bool on, float vel, bool acc)
    {
        if (On[s] == on && Math.Abs(Vel[s] - vel) < 1e-4f && Acc[s] == acc) return;
        On[s] = on; Vel[s] = vel; Acc[s] = acc && on;
        Commit?.Invoke(s);
        InvalidateVisual();
    }

    private float OnVel(int s) => Vel[s] > 0.01f ? Vel[s] : RhythmModel.NormalVel;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        int s = StepAt(pt.Position.X);
        e.Handled = true;
        if (pt.Properties.IsRightButtonPressed) { StepMenu?.Invoke(s); return; }
        if (!pt.Properties.IsLeftButtonPressed) return;
        var mods = e.KeyModifiers;
        if ((mods & KeyModifiers.Shift) != 0)
        {
            bool acc = !(On[s] && Acc[s]);
            Put(s, true, acc ? Math.Max(OnVel(s), RhythmModel.NormalVel) : OnVel(s), acc);
            return;
        }
        if ((mods & KeyModifiers.Alt) != 0)
        {
            bool quiet = On[s] && !Acc[s] && Vel[s] < RhythmModel.QuietBelow;
            Put(s, true, quiet ? RhythmModel.NormalVel : RhythmModel.QuietVel, false);
            return;
        }
        _paintOn = !On[s];
        Put(s, _paintOn, OnVel(s), Acc[s]);
        _pressStep = s; _lastPaint = s; _velMode = false;
        _pressAt = pt.Position; _pressVel = Vel[s];
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_pressStep < 0) return;
        var p = e.GetPosition(this);
        double dx = p.X - _pressAt.X, dy = p.Y - _pressAt.Y;
        if (!_velMode && _lastPaint == _pressStep && Math.Abs(dy) > 4 && Math.Abs(dy) > Math.Abs(dx) && On[_pressStep])
            _velMode = true;
        if (_velMode)
        {
            float v = (float)Math.Clamp(_pressVel - dy / 60.0, 0.05, 1.0);
            Put(_pressStep, true, v, Acc[_pressStep]);
            return;
        }
        int s = StepAt(p.X);
        if (s == _lastPaint) return;
        int dir = s > _lastPaint ? 1 : -1;
        for (int k = _lastPaint + dir; k != s + dir; k += dir) Put(k, _paintOn, OnVel(k), Acc[k]);
        _lastPaint = s;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pressStep < 0) return;
        _pressStep = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        int s = StepAt(e.GetPosition(this).X);
        if (!On[s]) return;
        Put(s, true, (float)Math.Clamp(Vel[s] + (e.Delta.Y > 0 ? 0.05 : -0.05), 0.05, 1.0), Acc[s]);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < N * 4 || h < 12) return;
        double cw = CellW, cellH = h - LaneH - LaneGap;
        var offPen = new Pen(NotaPalette.GraphBorder, 1);
        var beatPen = new Pen(NotaPalette.BorderDefault, 1);
        var quietPen = new Pen(NotaPalette.BorderBrass, 1);
        var headPen = new Pen(NotaPalette.TealBright, 1);
        for (int s = 0; s < N; s++)
        {
            double x = s * (cw + Gap);
            var r = new Rect(x + 0.5, 0.5, cw - 1, cellH - 1);
            bool on = On[s], acc = on && Acc[s], quiet = on && !acc && Vel[s] < RhythmModel.QuietBelow;
            if (!on) ctx.DrawRectangle(NotaPalette.BgSunken, s % 4 == 0 ? beatPen : offPen, r, 2, 2);
            else if (quiet) ctx.DrawRectangle(NotaPalette.AccentSubtle, quietPen, r, 2, 2);
            else ctx.DrawRectangle(acc ? NotaPalette.AccentBright : NotaPalette.Accent, null, r, 2, 2);
            if (on && !quiet)
            {
                var ft = RhythmInk.Mono((s + 1).ToString(NotaNum.Culture), NotaPalette.TextOnAccent);
                ctx.DrawText(ft, new Point(x + (cw - ft.Width) / 2, 1));
            }
            if (s == _head) ctx.DrawRectangle(null, headPen, r, 2, 2);

            // The velocity lane: teal by strength — accent light, normal teal, quiet deep.
            var lane = new Rect(x, h - LaneH, cw, LaneH);
            IBrush li = !on ? NotaPalette.SurfaceRaised
                : acc ? NotaPalette.TealBright
                : NotaPalette.Wash(NotaPalette.Teal, (byte)Math.Clamp(70 + Vel[s] * 185, 70, 255));
            ctx.DrawRectangle(li, null, lane, 1, 1);
        }
    }
}
