// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The custom-drawn view of the Nota Reverb card: the decay-tail window. The impulse flashes
// at the pre-delay (teal), the early reflections light one after another, then the RT60
// curve draws itself and fades — a loop while the reverb is sounding, a still picture when
// it is not. Frozen, the tail is a held line under a brass frame. It is also a control:
// drag the pre-delay marker to set the pre-delay, drag anywhere else to lengthen or shorten
// the decay; double-click either to reset. It draws what the card hands it and owns no
// engine state.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ReverbTailView : Control
{
    public const double AxisSec = 4.0;
    // The pre-delay marker sits between 3 % and 33 % of the width (0 … 200 ms); the early
    // reflections are drawn magnified — 100 ms spans 35 % — or they would pile onto the marker.
    private const double PreLo = 0.03, PreSpan = 0.30, ErSpanPer100Ms = 0.35;

    private double _rt60 = 2, _preNorm = 0.1, _preMs = 20;
    private double[] _erMs = Array.Empty<double>();
    private float[] _erGain = Array.Empty<float>();
    private bool _freeze;
    private double _phase = -1;       // 0..1 while sounding, −1 = still
    private int _drag;                // 0 none, 1 decay, 2 pre-delay
    private double _dragX, _dragY, _dragV;

    public event Action<int, double>? Changed;   // target (0 decay / 1 pre-delay), new normalised value
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int>? Reset;
    public Func<double>? DecayValue { get; set; }  // the Decay param now, for the relative drag

    public bool Dragging => _drag != 0;

    public ReverbTailView()
    {
        ClipToBounds = true;
        MinHeight = 40;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    /// <summary>Feed the window: RT60 in seconds (≤ 0 = frozen), the pre-delay (normalised,
    /// and in ms for the label), and the early reflections (ms after the pre-delay, level 0..1;
    /// empty = off).</summary>
    public void Set(double rt60, bool freeze, double preNorm, double preMs, double[] erMs, float[] erGain)
    {
        _rt60 = rt60; _freeze = freeze; _preNorm = Math.Clamp(preNorm, 0, 1); _preMs = preMs;
        _erMs = erMs; _erGain = erGain;
        InvalidateVisual();
    }

    /// <summary>Where the loop is (0..1), or −1 for the still picture.</summary>
    public void SetPhase(double phase)
    {
        if (phase < 0 && _phase < 0) return;
        _phase = phase;
        InvalidateVisual();
    }

    private (double x0, double x1, double top, double bot) Geo() => (8, Bounds.Width - 8, 15, Bounds.Height - 12);
    private double PreX() { var (x0, x1, _, _) = Geo(); return x0 + (PreLo + PreSpan * _preNorm) * (x1 - x0); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        int target = Math.Abs(p.X - PreX()) <= 8 ? 2 : 1;
        if (e.ClickCount == 2) { Reset?.Invoke(target - 1); e.Handled = true; return; }
        _drag = target; _dragX = p.X; _dragY = p.Y; _dragV = DecayValue?.Invoke() ?? 0.5;
        GestureBegin?.Invoke(target - 1);
        e.Pointer.Capture(this);
        Apply(p);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag != 0) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        GestureEnd?.Invoke(_drag - 1); _drag = 0; e.Pointer.Capture(null);
    }

    private void Apply(Point p)
    {
        var (x0, x1, top, bot) = Geo();
        if (x1 <= x0) return;
        if (_drag == 2)
            Changed?.Invoke(1, Math.Clamp(((p.X - x0) / (x1 - x0) - PreLo) / PreSpan, 0, 1));
        else
            // Right or up = a longer tail; one window width spans the whole range.
            Changed?.Invoke(0, Math.Clamp(_dragV + (p.X - _dragX) / (x1 - x0) - (p.Y - _dragY) / Math.Max(20, bot - top) * 0.5, 0, 1));
    }

    private static FormattedText Text(string t, IBrush ink, double size = 7.5)
        => new(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 16 || h < 16) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_freeze)
        {
            ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x0D), rect);
            ctx.DrawRectangle(null, new Pen(NotaPalette.BorderBrass, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));
        }
        var (x0, x1, top, bot) = Geo();
        if (x1 <= x0 || bot <= top) return;
        double span = bot - top;

        // Quarter-second grid across the window, a mid line and the floor.
        for (int i = 1; i < 4; i++)
        {
            double x = x0 + (x1 - x0) * i / 4.0;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, 4), new Point(x, h - 4));
        }
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, top + span * 0.5), new Point(x1, top + span * 0.5));
        ctx.DrawLine(new Pen(NotaPalette.GridBeat, 1.2), new Point(x0, bot), new Point(x1, bot));

        double preX = PreX();
        bool live = _phase >= 0;
        double ph = live ? _phase : 1;

        // ---- the tail: exp(−6.9 t / RT60) on a linear amplitude scale over 4 s --------
        double Env(double t) => _freeze ? 0.92 : Math.Exp(-6.9 * t / Math.Max(0.05, _rt60));
        double YAt(double x) => bot - span * Env((x - preX) / Math.Max(1, x1 - preX) * AxisSec);
        var full = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var g = full.Open())
        using (var f = fill.Open())
        {
            g.BeginFigure(new Point(x0, bot), false);
            g.LineTo(new Point(preX, bot));
            f.BeginFigure(new Point(preX, bot), true);
            for (double x = preX; x <= x1 + 0.1; x += 2)
            {
                var pt = new Point(Math.Min(x, x1), YAt(Math.Min(x, x1)));
                g.LineTo(pt); f.LineTo(pt);
            }
            f.LineTo(new Point(x1, bot));
            f.EndFigure(true);
        }
        ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x12), null, fill);
        ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x55), 1.4, lineJoin: PenLineJoin.Round), full);

        // The bright stroke draws itself over the first half of the loop, then fades a little.
        double reach = _freeze || !live ? 1 : Math.Clamp(ph / 0.52, 0, 1);
        double tailOp = !live ? 1 : _freeze ? 0.75 + 0.25 * Math.Sin(ph * Math.PI) : ph < 0.06 ? 0.2 + ph / 0.06 * 0.8 : ph < 0.86 ? 1 : 1 - (ph - 0.86) / 0.14 * 0.8;
        if (reach > 0)
        {
            double xEnd = preX + (x1 - preX) * reach;
            var bright = new StreamGeometry();
            using (var g = bright.Open())
            {
                g.BeginFigure(new Point(x0, bot), false);
                g.LineTo(new Point(preX, bot));
                for (double x = preX; x <= xEnd + 0.1; x += 2) g.LineTo(new Point(Math.Min(x, xEnd), YAt(Math.Min(x, xEnd))));
            }
            using (ctx.PushOpacity(tailOp))
                ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), bright);
        }

        // Where the tail has fallen 60 dB — the decay the knob names.
        if (!_freeze)
        {
            double rx = preX + _rt60 / AxisSec * (x1 - preX);
            if (rx <= x1) ctx.DrawEllipse(NotaPalette.AccentBright, null, new Point(rx, bot), 2.2, 2.2);
        }

        // ---- the impulse at the pre-delay and the early reflections ----------------
        double burstOp = !live ? 0.9 : ph < 0.1 ? 1 : ph < 0.4 ? 1 - (ph - 0.1) / 0.3 * 0.6 : 0.4 - (ph - 0.4) / 0.6 * 0.18;
        using (ctx.PushOpacity(burstOp))
            ctx.DrawLine(new Pen(NotaPalette.Teal, 1.2), new Point(preX, top - 2), new Point(preX, bot));

        int n = Math.Min(_erMs.Length, _erGain.Length);
        for (int k = 0; k < n; k++)
        {
            double x = preX + _erMs[k] / 100.0 * ErSpanPer100Ms * (x1 - x0);
            if (x > x1) break;
            double fire = 0.08 + 0.1 * k / Math.Max(1, n - 1);        // each lights a beat after the last
            double since = ph - fire;
            double op, sc;
            if (_freeze) { op = 0.6 + 0.4 * Math.Max(0, Math.Cos(since * Math.PI * 2)); sc = 0.9; }
            else if (!live) { op = 0.75; sc = 1; }
            else if (since < 0) { op = 0.15; sc = 0.5; }
            else if (since < 0.36) { op = 1 - since / 0.36 * 0.7; sc = 1 - since / 0.36 * 0.2; }
            else { op = 0.3 - Math.Min(1, (since - 0.36) / 0.6) * 0.15; sc = 0.8 - Math.Min(1, (since - 0.36) / 0.6) * 0.3; }
            double len = Math.Max(3, span * 0.52 * _erGain[k] * sc);
            using (ctx.PushOpacity(op))
                ctx.DrawLine(new Pen(NotaPalette.Teal, 1.2), new Point(x, bot), new Point(x, bot - len));
        }

        // ---- labels ------------------------------------------------------------------
        var title = _freeze ? Text("TAIL HELD · no decay", NotaPalette.AccentBright) : Text("DECAY TAIL", NotaGraph.TitleInk);
        ctx.DrawText(title, new Point(x0 - 3, 3));
        var rt = Text(_freeze ? "RT60 ∞" : NotaNum.F($"RT60 {_rt60:0.00} s"), NotaPalette.AccentBright);
        ctx.DrawText(rt, new Point(x1 + 3 - rt.Width, 3));
        var pre = Text(NotaNum.F($"pre {_preMs:0} ms"), NotaPalette.TealBright);
        ctx.DrawText(pre, new Point(Math.Min(preX + 3, x1 - pre.Width - 24), h - pre.Height - 2));
        var ax = Text("4 s", NotaGraph.AxisInk);
        ctx.DrawText(ax, new Point(x1 + 3 - ax.Width, h - ax.Height - 2));
    }
}
