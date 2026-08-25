// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

    // Stylized resonator glyph + exponential decay curve for Nota Physical, drawn from
    // normalized param values in the app style (faint grid + labels).
    internal sealed class PhysViz : Control
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
        public enum K { Glyph, Decay }
        private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x36, 0x2D));
        private static readonly Typeface Face = new(FontFamily.Default);
        private readonly K _k;
        private int _st;
        private double _exc, _pos, _damp, _tone, _inh, _body;
        public PhysViz(K k) { _k = k; MinWidth = 180; MinHeight = 40; }
        public void Set(int st, double exc, double pos, double damp, double tone, double inh, double body)
        { _st = st; _exc = exc; _pos = pos; _damp = damp; _tone = tone; _inh = inh; _body = body; InvalidateVisual(); }

        private void Label(DrawingContext ctx, string text, double x, double y, IBrush brush, double size = 8)
            => ctx.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush), new Point(x, y));

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
            double pad = 6, x0 = pad, x1 = w - pad, top = pad + 11, bot = h - pad - 9;
            var pen = new Pen(AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            if (_k == K.Glyph)
            {
                string[] names = { "STRING", "MEMBRANE", "TUBE", "BODY" };
                Label(ctx, names[Math.Clamp(_st, 0, 3)], x0, pad - 1, TextTertiary);
                double cx = (x0 + x1) / 2, cy = (top + bot) / 2, rw = (x1 - x0) / 2, rh = (bot - top) / 2;
                var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xF0, 0xC0, 0x60)), 1.2, lineCap: PenLineCap.Round);
                switch (Math.Clamp(_st, 0, 3))
                {
                    case 0: // String: a plucked line pulled aside at the pluck position.
                    {
                        double px = x0 + _pos * (x1 - x0);
                        double amp = (bot - top) * 0.34 * (0.4 + 0.6 * _exc);
                        var fig = new StreamGeometry();
                        using (var g = fig.Open())
                        {
                            g.BeginFigure(new Point(x0, cy), false);
                            g.LineTo(new Point(px, cy - amp));
                            g.LineTo(new Point(x1, cy), false);
                        }
                        ctx.DrawGeometry(null, pen, fig);
                        ctx.DrawEllipse(AccentBright, null, new Point(px, cy - amp), 2.2, 2.2);
                        break;
                    }
                    case 1: // Membrane: concentric ripples.
                    {
                        for (int i = 1; i <= 4; i++)
                            ctx.DrawEllipse(null, i == 1 ? pen : faint, new Point(cx, cy), rw * i / 4.4, rh * i / 4.4);
                        break;
                    }
                    case 2: // Tube: a pipe with a standing wave inside.
                    {
                        double ty = top + (bot - top) * 0.18, by = bot - (bot - top) * 0.18;
                        ctx.DrawLine(faint, new Point(x0, ty), new Point(x1, ty));
                        ctx.DrawLine(faint, new Point(x0, by), new Point(x1, by));
                        var wave = new StreamGeometry();
                        using (var g = wave.Open())
                        {
                            g.BeginFigure(new Point(x0, cy), false);
                            int n = 48;
                            double amp = (by - ty) * 0.42;
                            for (int i = 1; i <= n; i++)
                            {
                                double t = (double)i / n, x = x0 + t * (x1 - x0);
                                double env = Math.Sin(Math.PI * t);               // node at both ends
                                double y = cy - env * amp * Math.Sin(t * Math.PI * 3);
                                g.LineTo(new Point(x, y));
                            }
                        }
                        ctx.DrawGeometry(null, pen, wave);
                        break;
                    }
                    default: // Body: a rounded resonator box with rings.
                    {
                        var box = new Rect(x0 + rw * 0.18, top + rh * 0.28, (x1 - x0) * 0.64, (bot - top) * 0.62);
                        ctx.DrawRectangle(null, pen, box, 8, 8);
                        ctx.DrawEllipse(null, faint, new Point(cx, cy + rh * 0.28), rw * 0.16, rw * 0.16);
                        ctx.DrawEllipse(null, faint, new Point(cx, cy + rh * 0.28), rw * 0.28, rw * 0.28);
                        break;
                    }
                }
            }
            else
            {
                Label(ctx, "DECAY", x0, pad - 1, TextTertiary);
                var gridPen = new Pen(Grid, 1);
                for (int i = 1; i <= 3; i++)
                {
                    double gy = top + (bot - top) * i / 4.0;
                    ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy));
                    double gx = x0 + (x1 - x0) * i / 4.0;
                    ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot));
                }
                // Exponential amplitude decay: higher Damping → faster fall. A brighter
                // Tone shows as a faint denser oscillation under the envelope.
                double k = 2.0 + 7.0 * _damp;      // decay rate
                var env = new StreamGeometry();
                var osc = new StreamGeometry();
                int n = 96;
                using var ge = env.Open();
                using var go = osc.Open();
                ge.BeginFigure(new Point(x0, bot), false);
                go.BeginFigure(new Point(x0, cyMid(top, bot)), false);
                for (int i = 0; i <= n; i++)
                {
                    double t = (double)i / n, x = x0 + t * (x1 - x0);
                    double a = Math.Exp(-k * t);
                    double y = bot - a * (bot - top);
                    ge.LineTo(new Point(x, y));
                    double f = 6 + 40 * _tone;
                    double oy = cyMid(top, bot) - a * (bot - top) * 0.42 * Math.Sin(t * f);
                    go.LineTo(new Point(x, oy));
                }
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xF0, 0xC0, 0x60)), 1), osc);
                ctx.DrawGeometry(null, pen, env);
            }
        }

        private static double cyMid(double top, double bot) => (top + bot) / 2;
    }
