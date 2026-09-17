// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

    // Live wavetable-morph display + low-pass response for Nota Aurora, drawn from
    // normalized params. The waveform is reconstructed from the same additive recipe
    // as the engine (representative, not sample-identical), so Position/Warp/Table
    // moves animate the curve.
    internal sealed class AuroraViz : Control
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
        private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
        public enum K { Wave, Filter }
        private static readonly IBrush Grid = NotaGraph.Grid;
        private static readonly Typeface Face = NotaFonts.Mono;
        private readonly K _k;
        private int _bank;
        private double _pos, _warp, _cut, _res;
        public AuroraViz(K k) { _k = k; MinWidth = 180; MinHeight = 40; }
        public void Set(int bank, double pos, double warp, double cut, double res)
        { _bank = bank; _pos = pos; _warp = warp; _cut = cut; _res = res; InvalidateVisual(); }

        private void Label(DrawingContext ctx, string text, double x, double y, IBrush brush, double size = 8)
            => ctx.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush), new Point(x, y));

        // Harmonic amplitude recipe, mirroring WavetableSynth::harmAmp.
        private static double HarmAmp(int bank, double t, int n) => bank switch
        {
            0 => n == 1 ? 1.0 : t * (1.0 / n),
            1 => (n % 2 == 1) ? (n == 1 ? 1.0 : t * (1.0 / n)) : 0.0,
            2 => Math.Exp(-Math.Pow((n - (1.0 + t * 16.0)) / (2.0 + t * 3.0), 2)) + (n == 1 ? 0.15 : 0.0),
            _ => (1.0 / n) * Math.Abs(Math.Cos(Math.PI * n * (0.25 + 0.75 * t))),
        };

        private static double WarpPhase(double phase, double d) => phase < d ? 0.5 * phase / d : 0.5 + 0.5 * (phase - d) / (1.0 - d);

        // One morphed cycle sampled at `samples` points (peak-normalized).
        private double[] Cycle(double frame01, int samples)
        {
            const int nMax = 32;
            double d = Math.Clamp(0.5 + (_warp - 0.5) * 0.9, 0.05, 0.95);
            var buf = new double[samples];
            double peak = 1e-6;
            for (int j = 0; j < samples; j++)
            {
                double ph = WarpPhase((double)j / samples, d);
                double acc = 0;
                for (int n = 1; n <= nMax; n++)
                {
                    double a = HarmAmp(_bank, frame01, n);
                    if (a > 1e-4) acc += a * Math.Sin(2 * Math.PI * n * ph);
                }
                buf[j] = acc;
                peak = Math.Max(peak, Math.Abs(acc));
            }
            for (int j = 0; j < samples; j++) buf[j] /= peak;
            return buf;
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            NotaGraph.Window(ctx, new Rect(0, 0, w, h));
            double pad = 6, x0 = pad, x1 = w - pad, top = pad + 11, bot = h - pad - 9;

            if (_k == K.Wave)
            {
                string[] banks = { "ANALOG", "PULSE", "FORMANT", "CHROMA" };
                Label(ctx, "WAVETABLE", x0, pad - 1, TextTertiary);
                Label(ctx, banks[Math.Clamp(_bank, 0, 3)], x1 - 46, pad - 1, TextTertiary);
                int samples = 128;
                double span = x1 - x0, mid = (top + bot) / 2, amp = (bot - top) * 0.42;
                // Faint stack of neighbouring frames (behind), offset up-left to suggest depth.
                var faint = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x40), 1);
                for (int s = 2; s >= 1; s--)
                {
                    double nf = Math.Clamp(_pos + s * 0.14, 0, 1) * 7.0;
                    var g2 = Cycle(nf, samples);
                    var geo = new StreamGeometry();
                    using (var gc = geo.Open())
                    {
                        double dx = -s * 6, dy = -s * 7;
                        gc.BeginFigure(new Point(x0 + dx, mid + dy - g2[0] * amp), false);
                        for (int j = 1; j < samples; j++)
                            gc.LineTo(new Point(x0 + dx + span * j / (samples - 1), mid + dy - g2[j] * amp));
                    }
                    ctx.DrawGeometry(null, faint, geo);
                }
                // Bright current morphed frame.
                var cur = Cycle(_pos * 7.0, samples);
                var pen = new Pen(AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                var fig = new StreamGeometry();
                using (var gc = fig.Open())
                {
                    gc.BeginFigure(new Point(x0, mid - cur[0] * amp), false);
                    for (int j = 1; j < samples; j++)
                        gc.LineTo(new Point(x0 + span * j / (samples - 1), mid - cur[j] * amp));
                }
                ctx.DrawGeometry(null, pen, fig);
                Label(ctx, $"pos {(int)Math.Round(_pos * 100)}\u2009%", x0, bot + 1, TextSecondary);
            }
            else
            {
                Label(ctx, "FILTER", x0, pad - 1, TextTertiary);
                var gridPen = new Pen(Grid, 1);
                for (int i = 1; i <= 3; i++)
                {
                    double gy = top + (bot - top) * i / 4.0;
                    ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy));
                    double gx = x0 + (x1 - x0) * i / 4.0;
                    ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot));
                }
                var pen = new Pen(AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                double cx = x0 + _cut * (x1 - x0);
                double flatY = top + (bot - top) * 0.35;
                double bumpY = flatY - _res * (flatY - top) * 0.95;
                double kx = Math.Max(x0, cx - 12);
                ctx.DrawLine(pen, new Point(x0, flatY), new Point(kx, flatY));
                ctx.DrawLine(pen, new Point(kx, flatY), new Point(cx, bumpY));
                ctx.DrawLine(pen, new Point(cx, bumpY), new Point(x1, bot));
                ctx.DrawLine(new Pen(TextTertiary, 1) { DashStyle = DashStyle.Dash }, new Point(cx, top), new Point(cx, bot));
                double hz = 20.0 * Math.Pow(900.0, _cut);
                string hzText = hz >= 1000 ? $"{hz / 1000.0:0.0}k" : $"{hz:0}";
                Label(ctx, hzText, Math.Clamp(cx - 8, x0, x1 - 22), bot + 1, TextSecondary);
                Label(ctx, "Hz", x1 - 14, pad - 1, TextTertiary);
            }
        }
    }
