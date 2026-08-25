// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

    // Compressor transfer curve: input dB (x, -60..0) -> output dB (y). Unity below
    // threshold (+ makeup), ratio slope above. Drawn in the app style like SynthViz.
    internal sealed class CompViz : Control
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
        private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x36, 0x2D));
        private static readonly Typeface Face = new(FontFamily.Default);
        private float _thr = -18f, _ratio = 3f, _makeup;
        public CompViz() { MinWidth = 120; MinHeight = 70; }
        public void Set(float thr, float ratio, float makeup)
        { _thr = thr; _ratio = Math.Max(1f, ratio); _makeup = makeup; InvalidateVisual(); }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
            double pad = 7, x0 = pad, x1 = w - pad, top = pad + 10, bot = h - pad - 9;
            const double lo = -60, hi = 0;   // dB range on both axes
            double X(double db) => x0 + (db - lo) / (hi - lo) * (x1 - x0);
            double Y(double db) => bot - (db - lo) / (hi - lo) * (bot - top);

            var gridPen = new Pen(Grid, 1);
            for (int i = 1; i <= 3; i++)
            {
                double gy = top + (bot - top) * i / 4.0;
                ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy));
                double gx = x0 + (x1 - x0) * i / 4.0;
                ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot));
            }
            ctx.DrawLine(new Pen(Grid, 1) { DashStyle = DashStyle.Dash }, new Point(X(lo), Y(lo)), new Point(X(hi), Y(hi))); // unity

            double OutDb(double x) => (x <= _thr ? x : _thr + (x - _thr) / _ratio) + _makeup;
            var pen = new Pen(AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(X(lo), Y(Math.Clamp(OutDb(lo), lo, 6))), false);
                int n = 48;
                for (int i = 1; i <= n; i++)
                {
                    double x = lo + (hi - lo) * i / n;
                    gc.LineTo(new Point(X(x), Y(Math.Clamp(OutDb(x), lo, 6))));
                }
            }
            ctx.DrawGeometry(null, pen, g);
            ctx.DrawLine(new Pen(TextTertiary, 1) { DashStyle = DashStyle.Dash }, new Point(X(_thr), top), new Point(X(_thr), bot)); // threshold
            ctx.DrawText(new FormattedText("COMP", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, TextTertiary), new Point(x0, pad - 2));
        }
    }
