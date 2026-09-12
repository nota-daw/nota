// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pendulum field (mockup 2l) — the instrument drawn as itself. Balls swing across a
// triangular ∧∨ field; horizontal position = pitch (the held chord's degrees, labelled
// along the top with lane lines). Both swing paths are shown (∧ solid teal, ∨ dimmed);
// a dashed trigger line runs through the apex with a ring where a hit lands. Each ball
// drags a fading motion trail (the trail) showing which way it travels, and the ball
// currently crossing a degree lights accent-bright. Illustrative: it self-animates from
// the current Rate/Balls/Motion/Spread params via an internal timer (so it lives even
// when the transport is stopped) and mirrors the DSP's motion curve, but is not
// sample-locked to the audio.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PendulumViz : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush FieldBorder = NotaPalette.GraphBorder;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush Lane = NotaPalette.SurfaceCard;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static Color TrailColor => NotaPalette.Accent.Color;
    private static readonly IBrush RailUp = NotaPalette.Teal;                 // ∧ solid teal
    private static readonly IBrush RailDn = NotaPalette.Wash(NotaPalette.Teal, 0x8C); // ∨ dimmed
    private static readonly Pen TriggerPen = new(NotaPalette.Wash(NotaPalette.AccentBright, 0x47), 1) { DashStyle = DashStyle.Dash };
    private static readonly Pen ApexPen = new(NotaPalette.Wash(NotaPalette.AccentBright, 0x59), 1);
    private static readonly Typeface Face = new(FontFamily.Default);

    private const int MaxBalls = 6;
    private const int TrailLen = 14;
    private readonly double[] _phase = new double[MaxBalls];
    private readonly double[,] _trailX = new double[MaxBalls, TrailLen];   // stored 0..1 field position
    private readonly double[,] _trailB = new double[MaxBalls, TrailLen];   // stored -1..1 vertical bob
    private int _trailHead;
    private readonly int[] _lastDeg = new int[MaxBalls];

    private int _count = 4, _degrees = 5, _motion = 1;
    private double _cyclesPerSec = 0.8, _spread = 0.25;
    private int[] _held = Array.Empty<int>();
    private string _lastTrig = "";
    private readonly DispatcherTimer _timer;
    private readonly bool _mini;

    public PendulumViz(bool mini = false)
    {
        _mini = mini;
        MinWidth = 200; MinHeight = mini ? 40 : 80;
        for (int i = 0; i < MaxBalls; i++) { _phase[i] = (double)i / MaxBalls; _lastDeg[i] = -1; }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) =>
        {
            const double dt = 0.033;
            _trailHead = (_trailHead + 1) % TrailLen;
            for (int i = 0; i < _count; i++)
            {
                _phase[i] += _cyclesPerSec * (1.0 + _spread * i * 0.37) * dt;
                _phase[i] -= Math.Floor(_phase[i]);
                double pos = Position(_phase[i], _motion);
                _trailX[i, _trailHead] = pos;
                _trailB[i, _trailHead] = Math.Sin(2 * Math.PI * _phase[i]);
                if (_degrees > 0)
                {
                    int deg = Math.Clamp((int)(pos * _degrees), 0, _degrees - 1);
                    if (deg != _lastDeg[i]) { _lastDeg[i] = deg; if (_held.Length > 0) _lastTrig = NoteName(_held[Math.Clamp(deg, 0, _held.Length - 1)]); }
                }
            }
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _timer.Stop(); base.OnDetachedFromVisualTree(e); }

    // Live per-ball state, for the editor's per-ball list to stay in step with the field.
    public double BallPhase(int i) => (i >= 0 && i < MaxBalls) ? _phase[i] : 0;
    public int BallDegree(int i) => (i >= 0 && i < MaxBalls) ? _lastDeg[i] : -1;

    // count = balls, motion 0..3 curve, cyclesPerSec (signed → direction), spread 0..1,
    // held = the currently sounding chord pitches (empty ⇒ show placeholder lanes).
    public void Set(int count, int motion, double cyclesPerSec, double spread, int[] held)
    {
        _count = Math.Clamp(count, 1, MaxBalls); _motion = Math.Clamp(motion, 0, 3);
        _cyclesPerSec = cyclesPerSec; _spread = spread;
        _held = held ?? Array.Empty<int>();
        _degrees = _held.Length > 0 ? _held.Length : 5;
        if (_held.Length == 0) _lastTrig = "";
    }

    private static double Position(double ph, int type)
    {
        double tri = 1.0 - 2.0 * Math.Abs(ph - 0.5);
        return type switch
        {
            1 => 0.5 - 0.5 * Math.Cos(2 * Math.PI * ph),
            2 => tri * tri * (3.0 - 2.0 * tri),
            3 => Math.Clamp(1.0 - Math.Pow(1.0 - tri, 2.0) + 0.06 * Math.Sin(2 * Math.PI * 3.0 * tri) * (1.0 - tri), 0, 1),
            _ => tri,
        };
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(FieldBorder, 1), new Rect(0, 0, w, h), 6, 6);

        double labelH = _mini ? 2 : 14, capH = _mini ? 2 : 12;
        double pad = 8, x0 = pad, x1 = w - pad;
        double top = labelH + 2, bot = h - capH - 2;
        double W = x1 - x0, midY = (top + bot) / 2, cx = (x0 + x1) / 2;
        double amp = (bot - top) * 0.34;

        double LaneX(int d) => x0 + (_degrees <= 1 ? 0.5 : (double)d / (_degrees - 1)) * W;

        // Pitch lane lines + note-name labels along the top.
        for (int d = 0; d < _degrees; d++)
        {
            double gx = LaneX(d);
            ctx.DrawLine(new Pen(Lane, 1), new Point(gx, top), new Point(gx, bot));
            if (!_mini && d < _held.Length)
            {
                var ft = new FormattedText(NoteName(_held[d]), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9, TextTertiary);
                ctx.DrawText(ft, new Point(Math.Clamp(gx - ft.Width / 2, 1, w - ft.Width - 1), 2));
            }
        }

        // Both swing paths: ∧ solid, ∨ dimmed — the generative rails (teal = modulation).
        var up = new StreamGeometry(); var dn = new StreamGeometry();
        using (var gu = up.Open())
        using (var gd = dn.Open())
        {
            gu.BeginFigure(new Point(x0, bot), false); gu.LineTo(new Point(cx, top)); gu.LineTo(new Point(x1, bot));
            gd.BeginFigure(new Point(x0, top), false); gd.LineTo(new Point(cx, bot)); gd.LineTo(new Point(x1, top));
        }
        ctx.DrawGeometry(null, new Pen(RailDn, 1.4, lineJoin: PenLineJoin.Round), dn);
        ctx.DrawGeometry(null, new Pen(RailUp, 1.6, lineJoin: PenLineJoin.Round), up);

        // Trigger line through the apex + a ring where the hit lands.
        ctx.DrawLine(TriggerPen, new Point(cx, top - 2), new Point(cx, bot + 2));
        if (!_mini) ctx.DrawEllipse(null, ApexPen, new Point(cx, top + 2), 8, 8);

        // Balls with fading motion trails (the trail): oldest → newest.
        for (int i = 0; i < _count; i++)
        {
            double pos = Position(_phase[i], _motion);
            double bx = x0 + pos * W;
            double by = midY - Math.Sin(2 * Math.PI * _phase[i]) * amp;
            int deg = Math.Clamp((int)(pos * _degrees), 0, _degrees - 1);
            double slotX = LaneX(deg);
            bool near = Math.Abs(bx - slotX) < W / Math.Max(1, _degrees * 3);

            for (int t = 1; t < TrailLen; t++)
            {
                int idx = ((_trailHead - t) % TrailLen + TrailLen) % TrailLen;
                double frac = 1.0 - (double)t / TrailLen;          // 1 = freshest trail sample
                double tx = x0 + _trailX[i, idx] * W;
                double ty = midY - _trailB[i, idx] * amp;
                byte a = (byte)(frac * frac * 150);
                double rr = 1.2 + frac * (_mini ? 1.6 : 2.6);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, TrailColor.R, TrailColor.G, TrailColor.B)), null, new Point(tx, ty), rr, rr);
            }

            if (near && !_mini) ctx.DrawLine(new Pen(AccentBright, 1), new Point(slotX, top), new Point(slotX, bot));
            double br = _mini ? 3.0 : 4.5;
            ctx.DrawEllipse(near ? AccentBright : Brass, null, new Point(bx, by), br, br);
        }

        if (_mini) return;

        // Captions: legend bottom-left, live trigger note bottom-right.
        ctx.DrawText(new FormattedText("x = pitch · ∧ ∨ = swing paths", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, TextTertiary), new Point(x0, bot + 2));
        if (_lastTrig.Length > 0)
        {
            var ft = new FormattedText($"trigger at apex → {_lastTrig}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, AccentBright);
            ctx.DrawText(ft, new Point(x1 - ft.Width, bot + 2));
        }
    }
}
