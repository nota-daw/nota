// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Amplifier tone-stack EQ response (mockup 2m): the combined magnitude of the Bass low-shelf,
// Middle peak, Treble + Presence high-shelves — the same RBJ biquads the DSP runs — over a
// log-frequency axis, so the tone knobs draw their own curve.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AmpToneCurve : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush Grid = NotaPalette.SurfaceCard;
    private static readonly IBrush Ref = NotaPalette.SurfaceRaised;
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly Typeface Mono = new(new FontFamily("Geist Mono, monospace"));

    private double _bass, _mid, _treb, _pres;   // dB
    private const double Sr = 44100, FLo = 40, FHi = 12000, DbSpan = 15;

    public AmpToneCurve() { MinHeight = 40; }

    // Params are the amp's 0..10 knobs; convert to dB with the DSP's mapping.
    public void Set(float bass, float middle, float treble, float presence)
    {
        _bass = (bass - 5) / 5.0 * 12; _mid = (middle - 5) / 5.0 * 10;
        _treb = (treble - 5) / 5.0 * 12; _pres = (presence - 5) / 5.0 * 8;
        InvalidateVisual();
    }

    private double DbAt(double f)
    {
        return 20 * Math.Log10(Math.Max(1e-6,
            Mag(BiLowShelf(110, _bass), f) * Mag(BiPeak(650, _mid, 0.70), f)
          * Mag(BiHighShelf(3000, _treb), f) * Mag(BiHighShelf(4500, _pres), f)));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Well, new Pen(BorderIn, 1), new Rect(0, 0, w, h), 5, 5);
        double padB = 10, bot = h - padB, top = 3;
        double Y(double db) => top + (bot - top) * (0.5 - Math.Clamp(db / DbSpan, -1, 1) * 0.5);
        double X(double f) => w * (Math.Log(f / FLo) / Math.Log(FHi / FLo));

        ctx.DrawLine(new Pen(Ref, 1, dashStyle: new DashStyle(new double[] { 3, 3 }, 0)), new Point(0, Y(0)), new Point(w, Y(0)));
        foreach (var (f, lbl) in new[] { (80.0, "80"), (400.0, "400"), (1500.0, "1.5 k"), (6000.0, "6 k") })
        {
            double gx = X(f);
            ctx.DrawLine(new Pen(Grid, 1), new Point(gx, top), new Point(gx, bot));
            var ft = new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, Axis);
            ctx.DrawText(ft, new Point(Math.Min(gx + 2, w - ft.Width - 1), bot + 1));
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i <= 96; i++)
            {
                double f = FLo * Math.Pow(FHi / FLo, i / 96.0);
                var pt = new Point(X(f), Y(DbAt(f)));
                if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(Amber, 1.6, lineJoin: PenLineJoin.Round), geo);
    }

    // --- RBJ biquad coefficients + magnitude (mirrors Amp.h) ---
    private readonly record struct Coef(double b0, double b1, double b2, double a1, double a2);
    private static double W0(double f) => 2 * Math.PI * Math.Clamp(f, 10, Sr * 0.45) / Sr;
    private static Coef Norm(double b0, double b1, double b2, double a0, double a1, double a2)
        => new(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    private static Coef BiPeak(double f, double dB, double q)
    {
        double w = W0(f), cw = Math.Cos(w), a = Math.Sin(w) / (2 * q), A = Math.Pow(10, dB / 40);
        return Norm(1 + a * A, -2 * cw, 1 - a * A, 1 + a / A, -2 * cw, 1 - a / A);
    }
    private static Coef BiLowShelf(double f, double dB)
    {
        double w = W0(f), cw = Math.Cos(w), a = Math.Sin(w) / (2 * 0.707), A = Math.Pow(10, dB / 40), s = 2 * Math.Sqrt(A) * a;
        return Norm(A * ((A + 1) - (A - 1) * cw + s), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - s),
                    (A + 1) + (A - 1) * cw + s, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - s);
    }
    private static Coef BiHighShelf(double f, double dB)
    {
        double w = W0(f), cw = Math.Cos(w), a = Math.Sin(w) / (2 * 0.707), A = Math.Pow(10, dB / 40), s = 2 * Math.Sqrt(A) * a;
        return Norm(A * ((A + 1) + (A - 1) * cw + s), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - s),
                    (A + 1) - (A - 1) * cw + s, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - s);
    }
    private static double Mag(Coef c, double f)
    {
        double w = W0(f), cw = Math.Cos(w), c2 = Math.Cos(2 * w), sw = Math.Sin(w), s2 = Math.Sin(2 * w);
        double nr = c.b0 + c.b1 * cw + c.b2 * c2, ni = -(c.b1 * sw + c.b2 * s2);
        double dr = 1 + c.a1 * cw + c.a2 * c2, di = -(c.a1 * sw + c.a2 * s2);
        return Math.Sqrt((nr * nr + ni * ni) / Math.Max(1e-12, dr * dr + di * di));
    }
}
