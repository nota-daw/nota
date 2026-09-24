// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pendulum's windows, drawn from the engine's own telemetry (PendulumModel.Snapshot):
//
//   PendulumLanesView  one lane per ball — x is pitch (low … high), the chord's degree
//                      boundaries as faint ticks, the ball with a short trail showing which
//                      way it travels, the wall it came from (dim brass), the one it strikes
//                      (brass), and in teal how long until it reaches the wall ahead. The
//                      space answers "why this note".
//   PendulumBarView    the bar as a 1/16 strip the generated notes fall into (height =
//                      pitch within the chord), this bar in brass, the last bar's notes
//                      ahead of the playhead in deep brass. It answers "when".
//   PendulumBallList   the rail's ball rows — division · rate · phase · note — sized to fit.
//   PendulumVoiceBars  a tiny level bar per sounding voice.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal static class PendulumInk
{
    public static FormattedText Mono(string t, IBrush ink, double size = 7)
        => new(t, NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    /// <summary>Seconds as the lanes print them: 0.12 s, 1.3 s, or — when never.</summary>
    public static string Secs(double s) => s < 0 || s > 99 ? "—" : s < 10 ? NotaNum.Unit(s, "0.00", "s") : NotaNum.Unit(s, "0.0", "s");

    /// <summary>A moving ball "strikes" while it is this close to a wall (in lane widths)
    /// and within its turning half of the swing.</summary>
    public static bool AtWall(PendulumModel.Ball b)
        => b.Dir != 0 && (b.Pos < 0.04 || b.Pos > 0.96) && Math.Min(Math.Min(b.Phase, Math.Abs(b.Phase - 0.5)), 1.0 - b.Phase) < 0.08;
    /// <summary>A note is "fresh" (lit) for this long after its ball fired.</summary>
    public const double Fresh = 0.16;
}

internal sealed class PendulumLanesView : Control
{
    private PendulumModel.Snapshot _s = new();
    private int _count = 4, _motion = 1;
    private int[] _held = Array.Empty<int>();
    private string _div = "1/2";

    public void Set(PendulumModel.Snapshot s, int count, int motion, int[] held, string divLabel)
    {
        _s = s; _count = Math.Clamp(count, 1, PendulumModel.MaxBalls); _motion = motion;
        _held = held; _div = divLabel;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        const double padX = 6, padTop = 4, axisH = 9, leftW = 22, rightW = 20, gap = 6;
        double laneX0 = padX + leftW + gap, laneX1 = w - padX - rightW - gap, laneW = Math.Max(10, laneX1 - laneX0);
        double top = padTop, bot = h - axisH - 2;
        double rowH = (bot - top) / _count;
        int degrees = Math.Max(0, _held.Length);

        var accent = NotaPalette.Accent;
        var hairPen = new Pen(NotaPalette.GraphBorder, 1);
        var tickPen = new Pen(NotaPalette.GridBeat, 1);
        var leadPen = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x8C), 1);

        for (int b = 0; b < _count; b++)
        {
            var ball = _s.BallState[b];
            double cy = top + rowH * (b + 0.5);
            double wallTop = cy - rowH * 0.36, wallBot = cy + rowH * 0.36;

            // Division (or free cycle) left, the ball's note right.
            var divT = PendulumInk.Mono(_div, NotaPalette.AccentDim);
            ctx.DrawText(divT, new Point(padX, cy - divT.Height / 2));
            bool fresh = ball.Pitch >= 0 && ball.Since < PendulumInk.Fresh;
            var noteT = PendulumInk.Mono(PendulumModel.NoteName(ball.Pitch), fresh ? NotaPalette.AccentBright : ball.Pitch >= 0 ? NotaPalette.TextPrimary : NotaPalette.TextDisabled);
            ctx.DrawText(noteT, new Point(w - padX - noteT.Width, cy - noteT.Height / 2));

            // The lane: centre hairline, degree boundaries, the two walls.
            ctx.DrawLine(hairPen, new Point(laneX0, cy), new Point(laneX1, cy));
            for (int d = 1; d < degrees; d++)
            {
                double tx = laneX0 + laneW * d / degrees;
                ctx.DrawLine(tickPen, new Point(tx, cy - rowH * 0.18), new Point(tx, cy + rowH * 0.18));
            }
            bool striking = PendulumInk.AtWall(ball);
            bool strikeHigh = ball.Pos > 0.5;
            IBrush WallInk(bool high)
            {
                if (striking && strikeHigh == high) return accent;
                bool cameFrom = ball.Dir > 0 ? !high : ball.Dir < 0 && high;
                return cameFrom ? NotaPalette.AccentDim : NotaPalette.BorderStrong;
            }
            ctx.DrawRectangle(WallInk(false), null, new RoundedRect(new Rect(laneX0 - 1, wallTop, 2, wallBot - wallTop), 1));
            ctx.DrawRectangle(WallInk(true), null, new RoundedRect(new Rect(laneX1 - 1, wallTop, 2, wallBot - wallTop), 1));

            double bx = laneX0 + Math.Clamp(ball.Pos, 0, 1) * laneW;

            // Where it is going: a teal lead to the wall ahead and the time to get there.
            if (ball.Dir != 0 && !striking)
            {
                double wx = ball.Dir > 0 ? laneX1 : laneX0;
                ctx.DrawLine(leadPen, new Point(bx, cy), new Point(wx, cy));
                string t = PendulumInk.Secs(ball.ToWall);
                var ft = PendulumInk.Mono(ball.Dir > 0 ? $"→ {t}" : $"{t} ←", NotaPalette.TealBright);
                double ty = Math.Max(cy - rowH * 0.5, cy - ft.Height - 2);
                ctx.DrawText(ft, new Point(ball.Dir > 0 ? laneX1 - 4 - ft.Width : laneX0 + 4, ty));
            }
            else if (striking)
            {
                var ft = PendulumInk.Mono("hit", NotaPalette.AccentBright);
                double ty = Math.Max(cy - rowH * 0.5, cy - ft.Height - 2);
                ctx.DrawText(ft, new Point(strikeHigh ? laneX1 - 4 - ft.Width : laneX0 + 4, ty));
            }

            // Trail: three earlier places along the swing, fading, then the ball itself.
            if (ball.Dir != 0)
            {
                double sign = Math.Sign(ball.Rate);
                for (int k = 3; k >= 1; k--)
                {
                    double ph = ball.Phase - sign * k * 0.018;
                    ph -= Math.Floor(ph);
                    double tx = laneX0 + PendulumModel.Position(ph, _motion) * laneW;
                    double r = 2.5 + (3 - k) * 0.5;
                    byte a = k switch { 3 => 0x38, 2 => 0x66, _ => 0x9E };
                    ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, a), null, new Point(tx, cy), r, r);
                }
            }
            double br = Math.Min(striking ? 5.5 : 4.5, rowH * 0.36);
            if (striking) ctx.DrawEllipse(NotaPalette.AccentBright, new Pen(NotaPalette.AccentSubtle, 2), new Point(bx, cy), br, br);
            else ctx.DrawEllipse(NotaPalette.AccentBright, null, new Point(bx, cy), br, br);
        }

        // Axis: what x means.
        double ay = h - axisH - 1;
        var lo = PendulumInk.Mono("low", NotaPalette.TextAxis);
        var mid = PendulumInk.Mono(degrees > 0 ? $"x = pitch · {degrees} notes held" : "x = pitch · hold a chord", NotaPalette.TextAxis);
        var hi = PendulumInk.Mono("high", NotaPalette.TextAxis);
        ctx.DrawText(lo, new Point(laneX0, ay));
        ctx.DrawText(mid, new Point(laneX0 + (laneW - mid.Width) / 2, ay));
        ctx.DrawText(hi, new Point(laneX1 - hi.Width, ay));
    }
}

internal sealed class PendulumBarView : Control
{
    private PendulumModel.Snapshot _s = new();
    private string _quant = "1/16", _sort = "up";

    public void Set(PendulumModel.Snapshot s, string quant, string sort)
    {
        _s = s; _quant = quant; _sort = sort;
        InvalidateVisual();
    }

    /// <summary>The generated notes the strip shows (this bar and the last bar's tail).</summary>
    public static int Visible(PendulumModel.Snapshot s)
    {
        int n = 0;
        for (int i = 0; i < s.StepCount; i++) if (s.Steps[i].Pitch >= 0) n++;
        return n;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        const double padX = 6;
        var cap = new FormattedText("NOTES", NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.SansBold, 7, NotaPalette.TextTertiary);
        ctx.DrawText(cap, new Point(padX, 3));
        var sub = PendulumInk.Mono($"bar · quantize {_quant.ToLowerInvariant()}", NotaPalette.TextAxis);
        ctx.DrawText(sub, new Point(padX + cap.Width + 6, 3));
        int shown = Visible(_s);
        var right = PendulumInk.Mono($"{_sort} · {shown} {(shown == 1 ? "note" : "notes")}", NotaPalette.TealBright);
        ctx.DrawText(right, new Point(w - padX - right.Width, 3));

        int n = Math.Max(1, _s.StepCount);
        double gx0 = padX, gx1 = w - padX, gy0 = 4 + cap.Height + 2, gy1 = h - 3;
        double cw = (gx1 - gx0 + 1) / n;
        int lo = int.MaxValue, hi = int.MinValue;
        for (int i = 0; i < n; i++) if (_s.Steps[i].Pitch >= 0) { lo = Math.Min(lo, _s.Steps[i].Pitch); hi = Math.Max(hi, _s.Steps[i].Pitch); }

        // The freshest note of this bar: the last filled step at or before the playhead.
        int play = Math.Clamp((int)(_s.BarPos * n), 0, n - 1), latest = -1;
        for (int k = 0; k < n; k++)
        {
            int i = ((play - k) % n + n) % n;
            if (_s.Steps[i].Pitch >= 0 && _s.Steps[i].Age == 0) { latest = i; break; }
        }

        double fullH = gy1 - gy0;
        for (int i = 0; i < n; i++)
        {
            var st = _s.Steps[i];
            double x = gx0 + i * cw;
            double frac; IBrush ink;
            if (st.Pitch < 0) { frac = 0.18; ink = NotaPalette.GridBeat; }
            else
            {
                frac = hi > lo ? 0.5 + 0.5 * (st.Pitch - lo) / (double)(hi - lo) : 0.75;
                ink = i == latest ? NotaPalette.AccentBright : st.Age == 0 ? NotaPalette.Accent : NotaPalette.AccentDeep;
            }
            double bh = Math.Max(2, fullH * frac);
            ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(x, gy1 - bh, Math.Max(1, cw - 1), bh), 1));
        }
        double px = gx0 + Math.Clamp(_s.BarPos, 0, 1) * (gx1 - gx0);
        ctx.DrawLine(new Pen(NotaPalette.AccentDim, 1), new Point(px, gy0 - 2), new Point(px, gy1 + 2));
    }
}

internal sealed class PendulumBallList : Control
{
    private PendulumModel.Snapshot _s = new();
    private int _count = 4;
    private string _div = "1/2";
    private double _gap = 4, _maxRow = 17;

    public double Gap { get => _gap; set { _gap = value; InvalidateVisual(); } }
    public double MaxRow { get => _maxRow; set { _maxRow = value; InvalidateVisual(); } }

    public void Set(PendulumModel.Snapshot s, int count, string divLabel)
    {
        _s = s; _count = Math.Clamp(count, 1, PendulumModel.MaxBalls); _div = divLabel;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // Rows keep their natural height while they fit; when they don't, the gaps close to
        // 1px first and then the rows shrink — every ball keeps its row.
        double gap = _gap;
        double rowH = Math.Min(_maxRow, (h - gap * (_count - 1)) / _count);
        if (rowH < 11) { gap = 1; rowH = Math.Min(_maxRow, (h - gap * (_count - 1)) / _count); }
        rowH = Math.Max(6, rowH);
        for (int b = 0; b < _count; b++)
        {
            var ball = _s.BallState[b];
            double y = b * (rowH + gap);
            bool sel = b == _s.LastBall;
            var rr = new RoundedRect(new Rect(0.5, y + 0.5, w - 1, rowH - 1), NotaRadius.BadgeValue);
            ctx.DrawRectangle(sel ? NotaPalette.AccentSubtle : NotaPalette.BgSunken, new Pen(sel ? NotaPalette.BorderBrass : NotaPalette.GraphBorder, 1), rr);
            double cy = y + rowH / 2, x = 6;
            ctx.DrawEllipse(sel ? NotaPalette.AccentBright : NotaPalette.Accent, null, new Point(x + 3, cy), 3, 3);
            x += 11;
            void Col(string t, IBrush ink, double width)
            {
                var ft = PendulumInk.Mono(t, ink);
                ctx.DrawText(ft, new Point(x, cy - ft.Height / 2));
                x += width;
            }
            Col(_div, NotaPalette.TextPrimary, 25);
            Col(NotaNum.Unit(ball.Rate * 100, "+0;−0;0", "%"), sel ? NotaPalette.AccentBright : NotaPalette.TextSecondary, 33);
            Col(NotaNum.Unit(ball.Phase * 100, "0", "%"), sel ? NotaPalette.TextMuted : NotaPalette.TextTertiary, 30);
            bool fresh = ball.Pitch >= 0 && ball.Since < PendulumInk.Fresh;
            var note = PendulumInk.Mono(PendulumModel.NoteName(ball.Pitch), fresh ? NotaPalette.AccentBright : ball.Pitch >= 0 ? NotaPalette.TextPrimary : NotaPalette.TextDisabled);
            ctx.DrawText(note, new Point(w - 6 - note.Width, cy - note.Height / 2));
        }
    }
}

internal sealed class PendulumVoiceBars : Control
{
    private readonly double[] _lv = new double[PendulumModel.MaxLevels];
    private int _n;

    public PendulumVoiceBars() { Width = PendulumModel.MaxLevels * 3; Height = 11; }

    public void Set(double[] levels)
    {
        _n = 0;
        foreach (var l in levels) if (l > 0.002 && _n < _lv.Length) _lv[_n++] = l;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double h = Bounds.Height;
        if (h <= 0) return;
        double max = 0;
        for (int i = 0; i < _n; i++) max = Math.Max(max, _lv[i]);
        double x = Bounds.Width - _n * 3;
        for (int i = 0; i < _n; i++)
        {
            double lh = Math.Max(1, h * Math.Clamp(Math.Sqrt(_lv[i]), 0, 1));
            IBrush ink = _lv[i] >= max - 1e-6 ? NotaPalette.AccentBright : _lv[i] > 0.3 ? NotaPalette.Accent : NotaPalette.AccentDeep;
            ctx.DrawRectangle(ink, null, new Rect(x + i * 3, h - lh, 2, lh));
        }
    }
}
