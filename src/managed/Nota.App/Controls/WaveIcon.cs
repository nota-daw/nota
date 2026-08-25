// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

    // A tiny waveform glyph (saw/square/triangle/sine) for the oscillator combo-boxes.
    internal sealed class WaveIcon : Control
    {
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private readonly int _w;
        // Stroke colour (settable so selected/unselected chips can retint the glyph).
        public IBrush Stroke { get; set; } = AccentBright;
        public WaveIcon(int wave) { _w = wave; Width = 22; Height = 12; }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height, mid = h / 2;
            var pen = new Pen(Stroke, 1.3, lineJoin: PenLineJoin.Round);
            switch (_w)
            {
                case 1: // square
                    ctx.DrawLine(pen, new Point(1, mid), new Point(1, 2));
                    ctx.DrawLine(pen, new Point(1, 2), new Point(w / 2, 2));
                    ctx.DrawLine(pen, new Point(w / 2, 2), new Point(w / 2, h - 2));
                    ctx.DrawLine(pen, new Point(w / 2, h - 2), new Point(w - 1, h - 2));
                    ctx.DrawLine(pen, new Point(w - 1, h - 2), new Point(w - 1, mid));
                    break;
                case 2: // triangle
                    ctx.DrawLine(pen, new Point(1, h - 2), new Point(w / 2, 2));
                    ctx.DrawLine(pen, new Point(w / 2, 2), new Point(w - 1, h - 2));
                    break;
                case 3: // sine
                    Point prev = new(1, mid);
                    for (int i = 1; i <= 16; i++)
                    {
                        double t = i / 16.0, x = 1 + (w - 2) * t, y = mid - Math.Sin(t * 2 * Math.PI) * (h / 2 - 2);
                        var cur = new Point(x, y); ctx.DrawLine(pen, prev, cur); prev = cur;
                    }
                    break;
                default: // saw
                    ctx.DrawLine(pen, new Point(1, h - 2), new Point(w - 1, 2));
                    ctx.DrawLine(pen, new Point(w - 1, 2), new Point(w - 1, h - 2));
                    break;
            }
        }
    }
