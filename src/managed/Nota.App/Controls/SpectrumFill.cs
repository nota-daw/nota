// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// How the EQ cards draw the engine's spectrum (specs/046, dsp::SpectrumAnalyzer): a curve of
// log-spaced points from 20 Hz to 20 kHz, in dBFS, drawn as a low-contrast filled area under the
// EQ curve with a faint edge, or as a plain line (the input under Post, the peak hold). 0 dBFS
// is the top of the graph and −range dB the bottom; below that the area stays on the bottom
// edge, so silence draws nothing.

using System;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal static class SpectrumFill
{
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.TextAxis, 0x2E);
    private static readonly IPen Edge = new Pen(NotaPalette.Wash(NotaPalette.TextAxis, 0x8C), 1, lineJoin: PenLineJoin.Round);
    public static readonly IPen PeakPen = new Pen(NotaPalette.Wash(NotaPalette.TextAxis, 0xB0), 1, lineJoin: PenLineJoin.Round);

    private static double Y(float db, double h, double range) => Math.Clamp(-db / range, 0, 1) * h;
    private static double X(int i, int n, double w) => i / (double)(n - 1) * w;

    /// <summary>The spectrum as a filled area with a faint top edge.</summary>
    public static void Draw(DrawingContext ctx, float[] spec, double w, double h, double range, int count = -1)
    {
        int n = count > 0 ? Math.Min(count, spec.Length) : spec.Length;
        if (n < 2) return;
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(0, h), true);
            for (int i = 0; i < n; i++) g.LineTo(new Point(X(i, n, w), Y(spec[i], h, range)));
            g.LineTo(new Point(w, h));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Fill, null, area);
        ctx.DrawGeometry(null, Edge, Polyline(spec, n, w, h, range));
    }

    /// <summary>The spectrum as a line alone.</summary>
    public static void Line(DrawingContext ctx, float[] spec, double w, double h, double range, IPen pen, int count = -1)
    {
        int n = count > 0 ? Math.Min(count, spec.Length) : spec.Length;
        if (n >= 2) ctx.DrawGeometry(null, pen, Polyline(spec, n, w, h, range));
    }

    // A line through the points, broken wherever it would run along the bottom edge (both ends
    // below the range): a fading signal leaves no line there, and silence draws nothing.
    private static StreamGeometry Polyline(float[] spec, int n, double w, double h, double range)
    {
        var sg = new StreamGeometry();
        using var g = sg.Open();
        bool open = false;
        for (int i = 0; i < n; i++)
        {
            bool below = spec[i] <= -range, prevBelow = i > 0 && spec[i - 1] <= -range;
            var p = new Point(X(i, n, w), Y(spec[i], h, range));
            if (open && !(below && prevBelow)) { g.LineTo(p); continue; }
            if (open) g.EndFigure(false);
            g.BeginFigure(p, false);
            open = true;
        }
        if (open) g.EndFigure(false);
        return sg;
    }
}
