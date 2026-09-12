// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Amplifier harmonic spectrum (mockup 2m): a unit sine is pushed through the same waveshaper
// the DSP runs (bias + tanh cascade), and a DFT gives the 2nd…7th harmonic levels relative to
// the fundamental, plus the total harmonic distortion — so "how much it clips" is a number.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AmpHarmonics : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static Color Amber => NotaPalette.Accent.Color;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly Typeface Mono = new(new FontFamily("Geist Mono, monospace"));

    private float _drive = 1, _bias;
    private int _stages = 1;
    private readonly double[] _h = new double[7];   // harmonics 1..7 (index 0 = fundamental)
    private double _thd;

    public AmpHarmonics() { MinHeight = 40; }

    public void Set(float drive, int stages, float bias)
    {
        _drive = drive; _stages = Math.Max(1, stages); _bias = bias;
        Compute();
        InvalidateVisual();
    }

    private float Shape(float x)
    {
        float v = _drive * x + _bias;
        for (int s = 0; s < _stages; s++) { v = MathF.Tanh(v); if (s < _stages - 1) v *= 2f; }
        return v - MathF.Tanh(_bias);
    }

    private void Compute()
    {
        const int N = 512;
        for (int k = 0; k < 7; k++)
        {
            double re = 0, im = 0;
            for (int n = 0; n < N; n++)
            {
                double ph = 2 * Math.PI * n / N;
                double y = Shape((float)(0.7 * Math.Sin(ph)));   // ~ -3 dBFS drive test tone
                re += y * Math.Cos((k + 1) * ph); im += y * Math.Sin((k + 1) * ph);
            }
            _h[k] = Math.Sqrt(re * re + im * im) / N;
        }
        double f = Math.Max(1e-9, _h[0]), sum = 0;
        for (int k = 1; k < 7; k++) sum += _h[k] * _h[k];
        _thd = Math.Sqrt(sum) / f;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Well, new Pen(BorderIn, 1), new Rect(0, 0, w, h), 5, 5);
        double padX = 8, top = 13, bot = h - 11;
        ctx.DrawText(new FormattedText("HARMONICS", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 8, Muted), new Point(padX, 2));
        var thd = new FormattedText($"THD {_thd * 100:0.0} %", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Sub);
        ctx.DrawText(thd, new Point(w - padX - thd.Width, 2));

        // Bars for harmonics 2..7, normalised to the fundamental.
        double f = Math.Max(1e-9, _h[0]);
        int nb = 6; double bw = (w - 2 * padX) / nb;
        for (int i = 0; i < nb; i++)
        {
            double rel = Math.Clamp(_h[i + 1] / f, 0, 1);
            double bh = Math.Max(1, rel * (bot - top));
            double cx = padX + i * bw + bw / 2;
            ctx.DrawRectangle(new SolidColorBrush(Amber, 0.4 + 0.6 * rel), null, new Rect(cx - 4, bot - bh, 8, bh), 1, 1);
            var lbl = new FormattedText((i + 2).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, Muted);
            ctx.DrawText(lbl, new Point(cx - lbl.Width / 2, bot + 1));
        }
    }
}
