// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Drum Rack's pad oscillogram: a pad's one-shot as mirrored peak bars in the pad's
// own hue, inside the almanac graph window (well, hairline, radius 4), with the live
// playback position as a Brass Light line while the pad sounds. Read-only — trimming a
// sample is the Sampler's job.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class PadWave : Control
{
    private const double BarW = 2, BarGap = 1, Pad = 4;
    private static readonly IPen MidLine = new Pen(NotaPalette.GridBeat, 1);
    private static readonly IPen PlayPen = new Pen(NotaPalette.AccentBright, 1.2);

    private float[] _peaks = Array.Empty<float>();
    private double _play = -1;

    /// <summary>The bar colour — the pad's hue slot, so it re-tints with the theme.</summary>
    public IBrush Hue { get; set; } = NotaPalette.Accent;

    /// <summary>Normalised peaks (0..1), one per bucket, drawn left to right.</summary>
    public float[] Peaks { get => _peaks; set { _peaks = value ?? Array.Empty<float>(); InvalidateVisual(); } }

    /// <summary>Space kept clear above the bars for the window's caption line.</summary>
    public double TopInset { get; set; }

    /// <summary>Playback position 0..1, or −1 while the pad is silent.</summary>
    public double Play
    {
        get => _play;
        set { if (Math.Abs(value - _play) < 1e-4) return; _play = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        NotaGraph.Window(ctx, r);
        double mid = Math.Round(TopInset + (r.Height - TopInset) / 2) + 0.5;
        ctx.DrawLine(MidLine, new Point(1, mid), new Point(r.Width - 1, mid));

        int n = _peaks.Length;
        if (n > 0)
        {
            // As many bars as fit, each the loudest of the peaks it covers.
            double inner = r.Width - Pad * 2;
            int bars = Math.Max(1, (int)((inner + BarGap) / (BarW + BarGap)));
            double half = (r.Height - TopInset) / 2 - Pad;
            var pen = new Pen(Hue, BarW);
            for (int b = 0; b < bars; b++)
            {
                int lo = b * n / bars, hi = Math.Max(lo + 1, (b + 1) * n / bars);
                float p = 0;
                for (int i = lo; i < hi && i < n; i++) p = Math.Max(p, _peaks[i]);
                double h = Math.Max(0.5, p * half);
                double x = Pad + b * (BarW + BarGap) + BarW / 2;
                ctx.DrawLine(pen, new Point(x, mid - h), new Point(x, mid + h));
            }
        }

        if (_play >= 0)
        {
            double x = Math.Round(Pad + _play * (r.Width - Pad * 2)) + 0.5;
            ctx.DrawLine(PlayPen, new Point(x, TopInset + 1), new Point(x, r.Height - 1));
        }
    }
}
