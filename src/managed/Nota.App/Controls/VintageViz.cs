// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Vintage character viz: the memoryless saturation transfer curve (input→output,
// mirroring Vintage.h per Mode + Drive + Tone) over a faint grid + unity reference,
// with a speckle "grain" overlay whose density tracks Noise + Crackle + Wear and a
// bandwidth bar showing how far the highs are rolled off. Animates its grain on Tick.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VintageViz : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x36, 0x2D));
    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly string[] ModeNames = { "VINYL", "CASSETTE", "REEL", "VHS", "TUBE", "ANALOG" };
    // Mirrors Vintage.h kMode: shape, driveMul, bias, bandHz.
    private static readonly int[] Shape = { 0, 2, 2, 0, 1, 0 };
    private static readonly float[] DriveMul = { 3.0f, 4.0f, 6.0f, 3.5f, 8.0f, 5.0f };
    private static readonly float[] Bias = { 0.05f, 0.08f, 0.06f, 0.10f, 0.18f, 0.03f };
    private static readonly double[] BandHz = { 13000, 10000, 15000, 7000, 16000, 18000 };

    private int _mode;
    private float _drive = 0.35f, _tone = 0.5f, _grain, _wear;
    private uint _rng = 0x1234u;

    public VintageViz() { MinWidth = 150; MinHeight = 70; }

    // grain = combined Noise+Crackle density (0..1); wear narrows the bandwidth bar.
    public void Set(int mode, float drive, float tone, float grain, float wear)
    { _mode = Math.Clamp(mode, 0, 5); _drive = drive; _tone = tone; _grain = grain; _wear = wear; InvalidateVisual(); }

    public void Tick() => InvalidateVisual();   // re-speckle the grain

    private float Shaped(float x)
    {
        float g = 1f + DriveMul[_mode] * _drive * 3f;
        float bias = Bias[_mode] * (0.3f + _drive);
        float pre = x * g;
        return Shape[_mode] switch
        {
            1 => MathF.Tanh(pre + bias) - MathF.Tanh(bias),        // tube
            2 => pre / (1f + MathF.Abs(pre)) * 1.2f,               // tape
            _ => MathF.Tanh(pre),                                  // soft
        };
    }

    private float Rand() { _rng = _rng * 1664525u + 1013904223u; return ((_rng >> 8) & 0xFFFF) / 65536f; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
        double pad = 7, x0 = pad, x1 = w - pad, top = pad + 10, bot = h - pad - 13;

        var gridPen = new Pen(Grid, 1);
        for (int i = 1; i <= 3; i++)
        {
            double gy = top + (bot - top) * i / 4.0;
            ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy));
            double gx = x0 + (x1 - x0) * i / 4.0;
            ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot));
        }
        ctx.DrawLine(new Pen(Grid, 1) { DashStyle = DashStyle.Dash }, new Point(x0, bot), new Point(x1, top)); // unity ref

        // Transfer curve.
        float norm = Math.Max(1e-3f, Math.Abs(Shaped(1f)));
        var pen = new Pen(AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            int n = 64;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                float xin = (float)(t * 2.0 - 1.0);
                double y = Shaped(xin) / norm;
                var pt = new Point(x0 + t * (x1 - x0), bot - (y * 0.5 + 0.5) * (bot - top));
                if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
            }
        }
        ctx.DrawGeometry(null, pen, g);

        // Grain speckle: dots scattered across the panel, count ∝ Noise+Crackle.
        int dots = (int)(_grain * 120);
        var dotBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xF0, 0xC0, 0x60));
        for (int i = 0; i < dots; i++)
        {
            double dx = x0 + Rand() * (x1 - x0);
            double dy = top + Rand() * (bot - top);
            double rad = 0.5 + Rand() * 1.1;
            ctx.DrawEllipse(dotBrush, null, new Point(dx, dy), rad, rad);
        }

        // Bandwidth bar along the bottom: filled portion = retained highs (narrows with Wear).
        double frac = Math.Clamp((BandHz[_mode] * (1.0 - 0.55 * _wear)) / 20000.0, 0.05, 1.0);
        double by = bot + 6, bh = 3;
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D)), null, new Rect(x0, by, x1 - x0, bh), 1.5, 1.5);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xB0, 0x5A, 0xC8, 0xB0)), null, new Rect(x0, by, (x1 - x0) * frac, bh), 1.5, 1.5);

        ctx.DrawText(new FormattedText("DRIVE", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, TextTertiary), new Point(x0, pad - 2));
        var t2 = new FormattedText(ModeNames[_mode], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, AccentBright);
        ctx.DrawText(t2, new Point(x1 - t2.Width, pad - 2));
    }
}
