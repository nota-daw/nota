// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Velocity LAST 12 NOTES (almanac mockup 1a): one column per note, oldest left. The
// outline is the velocity that came in, the fill the velocity that went out — the newest in
// Brass Light, the rest in Brass Dim. Columns ease to their new heights as notes shift left,
// so a stream of notes scrolls rather than flickers. A bypassed device greys the fills.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VelocityHistoryViz : Control
{
    private static readonly IBrush Outline = NotaPalette.BorderStrong;
    private static readonly IBrush Fill = NotaPalette.AccentDim;
    private static readonly IBrush FillNew = NotaPalette.AccentBright;
    private static readonly IBrush FillOff = NotaPalette.BorderStrong;

    public const int Slots = 12;
    private const double Gap = 2;

    private readonly double[] _in = new double[Slots], _out = new double[Slots];
    private readonly double[] _tIn = new double[Slots], _tOut = new double[Slots];
    private int _filled;
    private long _count = -1;
    private bool _bypassed;

    public VelocityHistoryViz() { Height = 30; }

    /// <summary>The recent (in, out) velocities 0..1, oldest first (at most 12), and the
    /// running note count (a rise shifts the columns left before they ease in).</summary>
    public void Set(IReadOnlyList<(double In, double Out)> notes, long count, bool bypassed)
    {
        int n = Math.Min(Slots, notes.Count);
        // A new note: shift the shown columns left so the newcomer grows from nothing.
        if (_count >= 0 && count > _count)
        {
            int shift = (int)Math.Min(Slots, count - _count);
            for (int i = 0; i < Slots; i++)
            {
                int from = i + shift;
                _in[i] = from < Slots ? _in[from] : 0;
                _out[i] = from < Slots ? _out[from] : 0;
            }
        }
        _count = count;
        for (int i = 0; i < Slots; i++)
        {
            int k = i - (Slots - n);
            _tIn[i] = k >= 0 ? notes[k].In : 0;
            _tOut[i] = k >= 0 ? notes[k].Out : 0;
        }
        _filled = n;
        const double ease = 0.35;
        for (int i = 0; i < Slots; i++)
        {
            _in[i] += (_tIn[i] - _in[i]) * ease;
            _out[i] += (_tOut[i] - _out[i]) * ease;
        }
        _bypassed = bypassed;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double cw = (w - Gap * (Slots - 1)) / Slots;
        var pen = new Pen(Outline, 1);
        for (int i = 0; i < Slots; i++)
        {
            if (i < Slots - _filled && _in[i] < 0.004 && _out[i] < 0.004) continue;
            double x = i * (cw + Gap);
            double hi = Math.Round(_in[i] * h), ho = Math.Round(_out[i] * h);
            if (hi >= 1)
            {
                // Outline without a bottom edge: left, top, right.
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x + 0.5, h), false);
                    c.LineTo(new Point(x + 0.5, h - hi + 0.5));
                    c.LineTo(new Point(x + cw - 0.5, h - hi + 0.5));
                    c.LineTo(new Point(x + cw - 0.5, h));
                    c.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, g);
            }
            if (ho >= 1)
            {
                var fill = _bypassed ? FillOff : i == Slots - 1 ? FillNew : Fill;
                ctx.DrawRectangle(fill, null, new RoundedRect(new Rect(x + 2, h - ho, Math.Max(1, cw - 4), ho), 1, 1, 0, 0));
            }
        }
    }
}
