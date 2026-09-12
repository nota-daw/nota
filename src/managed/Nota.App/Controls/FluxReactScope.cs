// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Flux "React" scope: a rolling view of what the synth hears on
// its sidechain. Olive = the input level (material), teal = the smoothed envelope follower,
// bright teal ticks = detected transients, and a right-edge bar = the spectral tilt. Fed one
// (env, transient, tilt) sample per UI tick via Push(); keeps its own ring buffer.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class FluxReactScope : Control
{
    private const int N = 150;
    private readonly float[] _env = new float[N];
    private readonly float[] _flw = new float[N];   // smoothed follower
    private readonly float[] _tr = new float[N];
    private int _head;
    private float _tilt = 0.5f, _follow;

    private static Color Inset => NotaPalette.BgSunken.Color;
    private static Color Olive => NotaPalette.SignalIn.Color;
    private static Color Teal => NotaPalette.Teal.Color;
    private static Color TealBright => NotaPalette.TealBright.Color;

    public FluxReactScope() { ClipToBounds = true; }

    public void Push(float env, float transient, float tilt)
    {
        env = Math.Clamp(env, 0, 1);
        _follow += (env - _follow) * 0.20f;
        _env[_head] = env;
        _flw[_head] = _follow;
        _tr[_head] = Math.Clamp(transient, 0, 1);
        _head = (_head + 1) % N;
        _tilt = Math.Clamp(tilt, 0, 1);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        ctx.FillRectangle(new SolidColorBrush(Inset), new Rect(Bounds.Size), 6);

        const double barW = 6, gap = 4;
        double pw = w - barW - gap;               // plot width (leave room for the tilt bar)
        double mid = h / 2;
        double half = h * 0.42;

        // olive "material" envelope (mirrored around the centre line).
        var top = new StreamGeometry();
        var bot = new StreamGeometry();
        using (var tc = top.Open())
        using (var bc = bot.Open())
        {
            for (int i = 0; i < N; i++)
            {
                int idx = (_head + i) % N;
                double x = pw * i / (N - 1);
                double a = _env[idx] * half;
                var pt = new Point(x, mid - a);
                var pb = new Point(x, mid + a);
                if (i == 0) { tc.BeginFigure(pt, false); bc.BeginFigure(pb, false); }
                else { tc.LineTo(pt); bc.LineTo(pb); }
            }
            tc.EndFigure(false); bc.EndFigure(false);
        }
        var olivePen = new Pen(new SolidColorBrush(Olive, 0.85), 1);
        ctx.DrawGeometry(null, olivePen, top);
        ctx.DrawGeometry(null, olivePen, bot);

        // teal envelope follower (top curve, brighter/smoother).
        var flw = new StreamGeometry();
        using (var fc = flw.Open())
        {
            for (int i = 0; i < N; i++)
            {
                int idx = (_head + i) % N;
                double x = pw * i / (N - 1);
                var p = new Point(x, mid - _flw[idx] * half);
                if (i == 0) fc.BeginFigure(p, false); else fc.LineTo(p);
            }
            fc.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Teal), 1.6), flw);

        // transient ticks.
        var tickPen = new Pen(new SolidColorBrush(TealBright, 0.5), 1);
        for (int i = 1; i < N; i++)
        {
            int idx = (_head + i) % N, prev = (_head + i - 1) % N;
            if (_tr[idx] > 0.35f && _tr[idx] >= _tr[prev])   // rising edge past threshold
            {
                double x = pw * i / (N - 1);
                ctx.DrawLine(tickPen, new Point(x, 4), new Point(x, h - 4));
            }
        }

        // right-edge spectral-tilt bar.
        double bx = w - barW;
        ctx.FillRectangle(NotaPalette.SurfaceAbyss, new Rect(bx, 3, barW, h - 6), 3);
        double fillH = (h - 6) * _tilt;
        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        };
        grad.GradientStops.Add(new GradientStop(Teal, 0));
        grad.GradientStops.Add(new GradientStop(TealBright, 1));
        ctx.FillRectangle(grad, new Rect(bx, 3 + (h - 6 - fillH), barW, fillH), 3);
    }
}
