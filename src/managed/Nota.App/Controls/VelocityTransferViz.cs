// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Velocity TRANSFER plot (almanac mockups 1a / 1b): input velocity across, output up.
// In a sunken well: the Out range as two dashed lines, the band Random can spread a note over
// (a brass wash around the curve, clipped to the range), the transfer curve in brass and the
// last 12 notes as dots — older ones fainter, the newest a Brass Light dot with a halo. The
// full view adds the centre cross and the dashed identity diagonal.
//
// Motion: the curve and band glide to a new setting (a mode switch or a preset morphs rather
// than jumps), and each new note pops in — the dot grows with a little overshoot while a ring
// expands and fades. A bypassed device greys the curve and dots. The model is handed in by
// the card (VelocityModel), which mirrors MidiVelocity.h, so the picture is what it plays.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VelocityTransferViz : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush WellEdge = NotaPalette.GraphBorder;
    private static readonly IBrush Cross = NotaPalette.GridBeat;
    private static readonly IBrush Limit = NotaPalette.BorderStrong;
    private static readonly IBrush Diag = NotaPalette.BorderDefault;
    private static readonly IBrush Band = NotaPalette.Wash(NotaPalette.Accent, 0x1F);
    private static readonly IBrush Curve = NotaPalette.Accent;
    private static readonly IBrush CurveOff = NotaPalette.BorderStrong;
    private static readonly IBrush Latest = NotaPalette.AccentBright;
    private static readonly IBrush Halo = NotaPalette.Wash(NotaPalette.AccentBright, 0x40);

    private const int N = 41;   // curve samples across the input
    private const double PopMs = 220, RingMs = 650;

    /// <summary>The full (L) view: centre cross + identity diagonal, bigger dots with a halo.</summary>
    public bool Full { get; init; }

    // Shown (animated) and target shapes, output 0..1 per input sample.
    private readonly double[] _curve = new double[N], _top = new double[N], _bot = new double[N];
    private readonly double[] _tCurve = new double[N], _tTop = new double[N], _tBot = new double[N];
    private double _lo, _hi = 1, _tLo, _tHi = 1;
    private bool _primed, _bypassed;
    private readonly List<(double In, double Out)> _dots = new();
    private long _count = -1;
    private long _popAt;   // Stopwatch timestamp of the newest note's arrival (0 = none animating)

    public VelocityTransferViz() { MinHeight = 40; ClipToBounds = true; }

    /// <summary>Hand in the model. <paramref name="shape"/> maps input 0..1 to the output
    /// before Random (already in the Out range); <paramref name="spread"/> is Random's
    /// (down, up) offset, 0..1 units; <paramref name="dots"/> are the recent (in, out) notes,
    /// oldest first; <paramref name="count"/> — notes shaped so far (a rise pops the newest).</summary>
    public void Set(Func<double, double> shape, double lo, double hi, (double Down, double Up) spread,
                    IReadOnlyList<(double In, double Out)> dots, long count, bool bypassed)
    {
        for (int i = 0; i < N; i++)
        {
            double y = shape(i / (N - 1.0));
            _tCurve[i] = y;
            _tTop[i] = Math.Min(hi, y + spread.Up);
            _tBot[i] = Math.Max(lo, y - spread.Down);
        }
        _tLo = lo; _tHi = hi;
        if (!_primed)
        {
            Array.Copy(_tCurve, _curve, N); Array.Copy(_tTop, _top, N); Array.Copy(_tBot, _bot, N);
            _lo = lo; _hi = hi; _primed = true;
        }
        else
        {
            // Glide ~40 % of the way per tick (60 Hz): settles in about a tenth of a second.
            const double k = 0.4;
            for (int i = 0; i < N; i++)
            {
                _curve[i] += (_tCurve[i] - _curve[i]) * k;
                _top[i] += (_tTop[i] - _top[i]) * k;
                _bot[i] += (_tBot[i] - _bot[i]) * k;
            }
            _lo += (_tLo - _lo) * k; _hi += (_tHi - _hi) * k;
        }

        _dots.Clear();
        foreach (var d in dots) _dots.Add(d);
        if (_count >= 0 && count > _count) _popAt = Stopwatch.GetTimestamp();
        _count = count;
        if (_popAt != 0 && Stopwatch.GetElapsedTime(_popAt).TotalMilliseconds > RingMs) _popAt = 0;
        _bypassed = bypassed;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2) return;
        var well = new Rect(0, 0, w, h);
        ctx.DrawRectangle(Well, new Pen(WellEdge, 1), new RoundedRect(well.Deflate(0.5), NotaRadius.ControlValue));
        using var clip = ctx.PushClip(new RoundedRect(well.Deflate(1), NotaRadius.ControlValue));
        double iw = w - 2, ih = h - 2;
        Point P(double xn, double yn) => new(1 + xn * iw, 1 + (1 - Math.Clamp(yn, 0, 1)) * ih);

        if (Full)
        {
            var cross = new Pen(Cross, 1);
            ctx.DrawLine(cross, new Point(Math.Round(w / 2) + 0.5, 0), new Point(Math.Round(w / 2) + 0.5, h));
            ctx.DrawLine(cross, new Point(0, Math.Round(h / 2) + 0.5), new Point(w, Math.Round(h / 2) + 0.5));
        }
        var dash = new Pen(Limit, 1, dashStyle: new DashStyle(new double[] { 3, 3 }, 0));
        double yHi = Math.Round(P(0, _hi).Y) + 0.5, yLo = Math.Round(P(0, _lo).Y) + 0.5;
        ctx.DrawLine(dash, new Point(0, yHi), new Point(w, yHi));
        ctx.DrawLine(dash, new Point(0, yLo), new Point(w, yLo));

        // Random's band.
        bool any = false;
        for (int i = 0; i < N && !any; i++) any = _top[i] - _bot[i] > 0.002;
        if (any)
        {
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.BeginFigure(P(0, _top[0]), true);
                for (int i = 1; i < N; i++) g.LineTo(P(i / (N - 1.0), _top[i]));
                for (int i = N - 1; i >= 0; i--) g.LineTo(P(i / (N - 1.0), _bot[i]));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(Band, null, band);
        }

        if (Full) ctx.DrawLine(new Pen(Diag, 1, dashStyle: new DashStyle(new double[] { 3, 3 }, 0)), P(0, 0), P(1, 1));

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(P(0, _curve[0]), false);
            for (int i = 1; i < N; i++) g.LineTo(P(i / (N - 1.0), _curve[i]));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(_bypassed ? CurveOff : Curve, 2, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round), geo);

        // Notes: oldest faint → newest bright; the newest pops in.
        int n = _dots.Count;
        double popT = _popAt == 0 ? 1 : Stopwatch.GetElapsedTime(_popAt).TotalMilliseconds;
        for (int i = 0; i < n; i++)
        {
            var (xi, yo) = _dots[i];
            var p = P(xi, yo);
            bool latest = i == n - 1;
            if (!latest)
            {
                double a = 0.25 + 0.5 * (i + 12 - n) / 12.0;
                var ink = _bypassed ? CurveOff : NotaPalette.Wash(NotaPalette.Accent, (byte)Math.Round(a * 255));
                double r = Full ? 2.5 : 2;
                ctx.DrawEllipse(ink, null, p, r, r);
                continue;
            }
            double r0 = Full ? 4.5 : 3.5;
            double grow = _popAt == 0 ? 1 : EaseOutBack(Math.Clamp(popT / PopMs, 0, 1));
            if (_popAt != 0 && !_bypassed)
            {
                double u = Math.Clamp(popT / RingMs, 0, 1);
                double rr = r0 + 2 + u * (Full ? 12 : 8);
                ctx.DrawEllipse(null, new Pen(NotaPalette.Wash(NotaPalette.AccentBright, (byte)Math.Round((1 - u) * 0.6 * 255)), 1.5), p, rr, rr);
            }
            if (Full && !_bypassed) ctx.DrawEllipse(Halo, null, p, (r0 + 3) * grow, (r0 + 3) * grow);
            ctx.DrawEllipse(_bypassed ? CurveOff : Latest, null, p, r0 * grow, r0 * grow);
        }
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
