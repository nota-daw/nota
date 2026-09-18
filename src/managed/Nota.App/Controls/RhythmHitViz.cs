// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Rhythm — the "HIT" preview in the voice engine panel: an amp-decay envelope with a
// decaying oscillation whose pitch drops at the attack, drawn from the selected voice's
// Decay / Tune / Punch. Flashes brass when the voice triggers (fed from the scope). Purely
// a visual; the card sets the four values each tick.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class RhythmHitViz : Control
{
    private double _decay = 0.5, _tune = 0.4, _punch = 0.5, _flash;

    private static readonly IBrush EnvBrush = NotaPalette.AccentDim;
    private static Color Osc => NotaPalette.AccentBright.Color;
    private static readonly IPen ClickPen = new Pen(NotaPalette.Wash(NotaPalette.TealBright, 0x80), 1);

    public void Set(double decay, double tune, double punch, double flash)
    {
        _decay = decay; _tune = tune; _punch = punch; _flash = flash;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        double mid = h * 0.5;
        // How far across the amp decay reaches (longer Decay = wider tail).
        double span = w * (0.25 + _decay * 0.72);
        double k = 4.5 / span;                       // exp falloff so the tail lands near `span`

        // amp-decay envelope (upper half mirror).
        var env = new StreamGeometry();
        using (var g = env.Open())
        {
            g.BeginFigure(new Point(2, mid), false);
            for (double x = 2; x <= w; x += 3)
            {
                double e = System.Math.Exp(-k * (x - 2));
                g.LineTo(new Point(x, mid - e * (mid - 3)));
            }
        }
        ctx.DrawGeometry(null, new Pen(EnvBrush, 1), env);

        // decaying oscillation with a pitch drop at the attack (kick-like).
        double baseCyc = 2.0 + _tune * 10.0;         // visible cycles across the span
        var osc = new StreamGeometry();
        using (var g = osc.Open())
        {
            g.BeginFigure(new Point(2, mid), false);
            double phase = 0;
            double prevX = 2;
            for (double x = 2; x <= w; x += 1.5)
            {
                double t = (x - 2) / System.Math.Max(1, span);
                double e = System.Math.Exp(-k * (x - 2));
                double freq = baseCyc * (1.0 + 2.5 * System.Math.Exp(-6.0 * t));   // drop
                phase += freq * (x - prevX) / System.Math.Max(1, span) * System.Math.PI * 2 * 0.15;
                prevX = x;
                double y = mid - System.Math.Sin(phase) * e * (mid - 4);
                g.LineTo(new Point(x, y));
            }
        }
        var oscCol = Osc;
        if (_flash > 0.01) oscCol = Color.FromArgb(255,
            (byte)System.Math.Min(255, Osc.R + _flash * (255 - Osc.R)),
            (byte)System.Math.Min(255, Osc.G + _flash * (255 - Osc.G)),
            (byte)System.Math.Min(255, Osc.B + _flash * (255 - Osc.B)));
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(oscCol), 1.5), osc);

        // attack click marker.
        double cx = 2 + _punch * 6;
        ctx.DrawLine(ClickPen, new Point(cx, 3), new Point(cx, h - 3));
    }
}
