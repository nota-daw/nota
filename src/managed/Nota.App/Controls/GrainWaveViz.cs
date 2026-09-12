// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Grain sample view — drawn in the app's standard waveform style (like
// SamplerWaveform): brass min/max waveform on the sunken surface, a centre line and a
// time grid. The Spray region is a faint amber band around the read Position, and the
// live grain read positions scan across as bright playheads (green, like the Sampler
// cursor) so you see the granular motion. Peaks/duration are set on load; position,
// spray and the live playheads update every UI tick.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class GrainWaveViz : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush Wave = NotaPalette.Accent;
    private static readonly IPen MidLine = new Pen(NotaPalette.GridBar, 1);
    private static readonly IPen GridPen = new Pen(NotaPalette.Wash(NotaPalette.BorderStrong, 0x55), 1);
    private static readonly IBrush SprayFill = NotaPalette.Wash(NotaPalette.Accent, 0x16);
    private static readonly IPen SprayEdge = new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x55), 1);
    private static readonly IPen PosPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x9A), 1) { DashStyle = DashStyle.Dash };
    private static readonly IBrush PlayCol = NotaPalette.Success;
    private static readonly IBrush GridText = NotaPalette.TextTertiary;
    private static readonly Typeface Mono = new("monospace");

    private float[] _peaks = Array.Empty<float>();
    private double _dur, _pos, _spray, _grPerSec;
    private int _mode;
    private float[] _heads = Array.Empty<float>();
    private int _headN;

    public GrainWaveViz() { MinHeight = 60; ClipToBounds = true; }

    public void SetPeaks(float[] peaks, double durationSec) { _peaks = peaks; _dur = durationSec; InvalidateVisual(); }
    public void SetState(double pos, double spray, int mode, double grPerSec) { _pos = pos; _spray = spray; _mode = mode; _grPerSec = grPerSec; InvalidateVisual(); }
    public void SetPlayheads(float[] heads, int n) { _heads = heads; _headN = n; InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, null, new Rect(0, 0, w, h), 4, 4);
        double cy = h / 2;

        // Time grid + labels behind the waveform.
        if (_dur > 0)
        {
            double step = _dur <= 0.5 ? 0.05 : _dur <= 1.5 ? 0.1 : _dur <= 5 ? 0.5 : _dur <= 15 ? 1.0 : 5.0;
            for (double tt = step; tt < _dur; tt += step)
            {
                double x = tt / _dur * w;
                ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
                string lbl = _dur >= 1 ? $"{tt:0.##}s" : $"{(int)Math.Round(tt * 1000)}";
                ctx.DrawText(new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, GridText), new Point(x + 2, h - 11));
            }
        }

        // Spray band around the read position (grains scatter here).
        if (_mode != 2)
        {
            double s0 = Math.Clamp(_pos - _spray * 0.21, 0, 1), s1 = Math.Clamp(_pos + _spray * 0.21, 0, 1);
            if (s1 > s0) { ctx.FillRectangle(SprayFill, new Rect(s0 * w, 0, (s1 - s0) * w, h)); ctx.DrawLine(SprayEdge, new Point(s0 * w, 0), new Point(s0 * w, h)); ctx.DrawLine(SprayEdge, new Point(s1 * w, 0), new Point(s1 * w, h)); }
        }

        // Brass min/max waveform.
        int n = _peaks.Length / 2;
        for (int i = 0; i < n; i++)
        {
            double x = (double)i / n * w;
            double y0 = cy - _peaks[i * 2 + 1] * (cy - 2);
            double y1 = cy - _peaks[i * 2] * (cy - 2);
            ctx.DrawLine(new Pen(Wave, 1), new Point(x, y0), new Point(x, y1));
        }
        ctx.DrawLine(MidLine, new Point(0, cy), new Point(w, cy));

        // The set read Position (dashed amber) — where scanning centres / grains seed.
        double px = Math.Clamp(_pos, 0, 1) * w;
        ctx.DrawLine(PosPen, new Point(px, 0), new Point(px, h));

        // Live grain read positions — bright playheads that scan as it plays.
        for (int i = 0; i < _headN && i < _heads.Length; i++)
        {
            double hx = Math.Clamp(_heads[i], 0, 1) * w;
            ctx.DrawLine(new Pen(PlayCol, 1.6), new Point(hx, 0), new Point(hx, h));
            var tri = new StreamGeometry();
            using (var g = tri.Open()) { g.BeginFigure(new Point(hx - 3, 0), true); g.LineTo(new Point(hx + 3, 0)); g.LineTo(new Point(hx, 4)); g.EndFigure(true); }
            ctx.DrawGeometry(PlayCol, null, tri);
        }

        // Grains/sec readout.
        ctx.DrawText(new FormattedText($"{_grPerSec:0} gr/s", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, GridText), new Point(4, 2));
    }
}
