// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Amp waveshaper transfer curve (input -> output) for the built-in Amp device.
// Redraws when the model or Gain moves so you see how hard the preamp is
// clipping — a near-diagonal line stays clean, an S/square shape is saturated.
// Drawn in the app style like CompViz (faint grid + unity reference + label).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AmpCurve : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x36, 0x2D));
    private static readonly Typeface Face = new(FontFamily.Default);

    private float _drive = 1f, _bias;
    private int _stages = 1;
    private string _model = "";

    public AmpCurve() { MinWidth = 120; MinHeight = 70; }

    public void Set(float drive, int stages, float bias, string model)
    { _drive = drive; _stages = Math.Max(1, stages); _bias = bias; _model = model; InvalidateVisual(); }

    // The Amp's memoryless waveshaper (mirrors Amp.h: bias + tanh cascade, DC-recentered).
    private float Shape(float x)
    {
        float v = _drive * x + _bias;
        for (int s = 0; s < _stages; s++) { v = MathF.Tanh(v); if (s < _stages - 1) v *= 2f; }
        return v - MathF.Tanh(_bias);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
        double pad = 7, x0 = pad, x1 = w - pad, top = pad + 10, bot = h - pad - 9;

        var gridPen = new Pen(Grid, 1);
        for (int i = 1; i <= 3; i++)
        {
            double gy = top + (bot - top) * i / 4.0;
            ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy));
            double gx = x0 + (x1 - x0) * i / 4.0;
            ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot));
        }
        ctx.DrawLine(new Pen(Grid, 1) { DashStyle = DashStyle.Dash }, new Point(x0, bot), new Point(x1, top)); // unity ref

        float norm = Math.Max(1e-3f, Math.Abs(Shape(1f)));   // fit the S-curve to the box
        var pen = new Pen(AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            int n = 64;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;               // 0..1 across x = -1..+1
                float xin = (float)(t * 2.0 - 1.0);
                double y = Shape(xin) / norm;            // -1..+1
                var pt = new Point(x0 + t * (x1 - x0), bot - (y * 0.5 + 0.5) * (bot - top));
                if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
            }
        }
        ctx.DrawGeometry(null, pen, g);

        ctx.DrawText(new FormattedText("TRANSFER", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, TextTertiary), new Point(x0, pad - 2));
        if (_model.Length > 0)
        {
            var t2 = new FormattedText(_model, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, AccentBright);
            ctx.DrawText(t2, new Point(x1 - t2.Width, pad - 2));
        }
        // input-drive axis (−40 dB … 0 dB).
        var axisB = new SolidColorBrush(Color.Parse("#4A463D"));
        ctx.DrawText(new FormattedText("in −40 dB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 7, axisB), new Point(x0, bot + 2));
        var zt = new FormattedText("0 dB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 7, axisB);
        ctx.DrawText(zt, new Point(x1 - zt.Width, bot + 2));
    }
}
