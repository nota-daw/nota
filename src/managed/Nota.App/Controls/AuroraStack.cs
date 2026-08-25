// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Aurora wavetable display (mockup 2j): the 16 single-cycle frames drawn as a
// skewed 3-D stack, the position-selected frame lit accent-bright and the rest faint —
// "position is a place in a stack, not a number". Reconstructs each frame's waveform
// from the same additive recipe as WavetableSynth::harmAmp (low harmonic count → a
// representative shape, not sample-exact).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AuroraStack : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Accent = NotaPalette.AccentBright;
    private static readonly IBrush Faint = new SolidColorBrush(Color.FromArgb(0x42, 0xD8, 0xA0, 0x3D));
    private const int Frames = 16, N = 72, HarmMax = 28;

    private int _bank;
    private double _pos;

    public AuroraStack() { ClipToBounds = true; }

    public void Set(int bank, double pos) { _bank = Math.Clamp(bank, 0, 3); _pos = Math.Clamp(pos, 0, 1); InvalidateVisual(); }

    private static double HarmAmp(int bank, double t, int n) => bank switch
    {
        0 => n == 1 ? 1.0 : t / n,
        1 => (n % 2 == 1) ? (n == 1 ? 1.0 : t / n) : 0.0,
        2 => Math.Exp(-Sq((n - (1 + t * 16.0)) / (2.0 + t * 3.0))) + (n == 1 ? 0.15 : 0.0),
        _ => (1.0 / n) * Math.Abs(Math.Cos(Math.PI * n * (0.25 + 0.75 * t))),
    };
    private static double Sq(double x) => x * x;

    private static double[] Frame(int bank, double t)
    {
        var buf = new double[N]; double peak = 1e-6;
        for (int j = 0; j < N; j++)
        {
            double ph = (double)j / N, acc = 0;
            for (int n = 1; n <= HarmMax; n++) { double a = HarmAmp(bank, t, n); if (a > 1e-4) acc += a * Math.Sin(2 * Math.PI * n * ph); }
            buf[j] = acc; peak = Math.Max(peak, Math.Abs(acc));
        }
        for (int j = 0; j < N; j++) buf[j] /= peak;
        return buf;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);
        int active = Math.Clamp((int)Math.Round(_pos * (Frames - 1)), 0, Frames - 1);

        // Skewed stack sized so every frame (centre ± amplitude) stays inside the bounds:
        // frame 0 bottom-front, frame 15 top-back; each shifted up-right.
        const double padX = 10, padY = 8;
        double waveW = (w - padX * 2) * 0.70, skewX = (w - padX * 2) * 0.30 / (Frames - 1);
        double amp = (h - padY * 2) * 0.16;
        double cyBottom = h - padY - amp, cyTop = padY + amp;

        // Draw back-to-front so nearer frames overlap farther ones.
        for (int f = Frames - 1; f >= 0; f--)
        {
            bool on = f == active;
            var pen = new Pen(on ? Accent : Faint, on ? 1.7 : 1.0, lineJoin: PenLineJoin.Round);
            var buf = Frame(_bank, (double)f / (Frames - 1));
            double ox = padX + f * skewX;
            double cy = cyBottom - (cyBottom - cyTop) * f / (Frames - 1);
            Point? prev = null;
            for (int j = 0; j <= N; j++)
            {
                double s = buf[j % N];
                var p = new Point(ox + (double)j / N * waveW, cy - s * amp);
                if (prev is { } pp) ctx.DrawLine(pen, pp, p);
                prev = p;
            }
        }
    }
}
