// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Auto Pan viz: the per-channel amplitude curves over one LFO cycle — left
// (amber) and right (teal), offset by Phase — so you see tremolo (curves overlap,
// Phase 0°) vs auto-pan (curves opposite, Phase 180°). A live L—R bar with a moving
// dot (fed by the device's published pan position) tracks the current stereo place.
// Mirrors AutoPan.h's LFO/shape math so the picture matches the sound.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AutoPanViz : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x36, 0x2D));
    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly string[] WaveNames = { "SINE", "TRI", "SAW", "SQUARE", "S&H" };

    private int _wave;
    private float _amount = 0.7f, _shape, _phase = 0.5f, _pan = 0.5f;

    public AutoPanViz() { MinWidth = 150; MinHeight = 70; }

    public void Set(int wave, float amount, float shape, float phase, float pan)
    { _wave = Math.Clamp(wave, 0, 4); _amount = amount; _shape = shape; _phase = phase; _pan = pan; InvalidateVisual(); }

    private static double Frac(double x) => x - Math.Floor(x);

    // Mirrors AutoPan::lfo (S&H is shown as a flat mid-line — it has no fixed shape).
    private float Lfo(double ph)
    {
        double v = _wave switch
        {
            1 => 4.0 * Math.Abs(ph - 0.5) - 1.0,   // triangle
            2 => 2.0 * ph - 1.0,                   // saw
            3 => ph < 0.5 ? 1.0 : -1.0,            // square
            4 => 0.0,                              // S&H (random at runtime)
            _ => Math.Sin(2.0 * Math.PI * ph),     // sine
        };
        if (_wave < 3 && _shape > 1e-3f)
        {
            double k = 1.0 + _shape * 12.0;
            double sharp = Math.Tanh(k * v) / Math.Tanh(k);
            v = v * (1.0 - _shape) + sharp * _shape;
        }
        return (float)v;
    }

    private float GainAt(double ph) => 1f - _amount * 0.5f * (1f - Lfo(ph));

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
        double pad = 7, x0 = pad, x1 = w - pad, top = pad + 11, bot = h - pad - 15;

        var gridPen = new Pen(Grid, 1);
        for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy)); }

        // Per-channel gain curves over one cycle (gain 1 at top, 0 at bottom).
        void Curve(double phOff, IBrush col)
        {
            var pen = new Pen(col, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                int n = 96;
                for (int i = 0; i <= n; i++)
                {
                    double t = (double)i / n;
                    float g = GainAt(Frac(t + phOff));
                    var pt = new Point(x0 + t * (x1 - x0), bot - g * (bot - top));
                    if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
                }
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        Curve(0.0, AccentBright);      // L
        Curve(_phase, Teal);           // R (phase-offset)

        // Live L—R position bar with a moving dot.
        double by = bot + 8, bh = 4;
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D)), null, new Rect(x0, by, x1 - x0, bh), 2, 2);
        ctx.DrawLine(new Pen(Grid, 1), new Point((x0 + x1) / 2, by - 1), new Point((x0 + x1) / 2, by + bh + 1));
        double dotX = x0 + Math.Clamp(_pan, 0f, 1f) * (x1 - x0);
        ctx.DrawEllipse(AccentBright, null, new Point(dotX, by + bh / 2), 3, 3);

        ctx.DrawText(new FormattedText("L", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, AccentBright), new Point(x0, pad - 2));
        var tR = new FormattedText("R", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, Teal);
        ctx.DrawText(tR, new Point(x0 + 12, pad - 2));
        var tw = new FormattedText(WaveNames[_wave], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, TextTertiary);
        ctx.DrawText(tw, new Point(x1 - tw.Width, pad - 2));
    }
}
