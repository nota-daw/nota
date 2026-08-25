// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Length gate viz (mockup 3b): the forced note length drawn as a bar on a 0…2 s time
// axis, so "620 ms" is a length you can see. Two bars show the min/max the modifiers
// (velocity · key · random) spread the length across; when no modifier is active they match.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class LengthGateViz : Control
{
    private static readonly IBrush Well = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush BorderIn = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#171613"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberDim = new SolidColorBrush(Color.FromArgb(0x88, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Axis = new SolidColorBrush(Color.Parse("#4A463D"));
    private static readonly IBrush Sub = new SolidColorBrush(Color.Parse("#A39D8F"));
    private static readonly Typeface Mono = new(new FontFamily("Geist Mono, monospace"));
    private const double FullMs = 2000.0;

    private double _minMs, _maxMs, _bpm = 120;

    public LengthGateViz() { MinHeight = 60; }

    public void Set(double minMs, double maxMs, double bpm)
    {
        _minMs = minMs; _maxMs = maxMs; _bpm = bpm > 0 ? bpm : 120;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Well, new Pen(BorderIn, 1), new Rect(0, 0, w, h), 6, 6);
        double padX = 8, padTop = 6, padBot = 14;
        // Header.
        ctx.DrawText(new FormattedText("GATE", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 8, Muted), new Point(padX, padTop));
        double bars = _bpm > 0 ? 4.0 * 60000.0 / _bpm : 2000;   // one 4/4 bar in ms
        var hdr = new FormattedText($"1 bar at {_bpm:0} BPM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Muted);
        ctx.DrawText(hdr, new Point(w - padX - hdr.Width, padTop));

        double labelW = 26;
        double x0 = padX + labelW, x1 = w - padX, top = padTop + 14, bot = h - padBot;
        double rowH = (bot - top) / 2 - 3;

        void Bar(int idx, string label, double ms)
        {
            double y = top + idx * (rowH + 6);
            var lt = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Muted);
            ctx.DrawText(lt, new Point(padX, y + rowH / 2 - lt.Height / 2));
            ctx.DrawRectangle(Track, null, new Rect(x0, y, x1 - x0, rowH), 3, 3);
            double frac = Math.Clamp(ms / FullMs, 0, 1);
            if (frac > 0.001)
                ctx.DrawRectangle(idx == 0 ? Amber : AmberDim, null, new Rect(x0, y, Math.Max(2, frac * (x1 - x0)), rowH), 3, 3);
            var vt = new FormattedText(ms >= 1000 ? $"{ms / 1000:0.00} s" : $"{ms:0} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Sub);
            double vx = x0 + frac * (x1 - x0) + 5; if (vx + vt.Width > x1) vx = x1 - vt.Width - 5;
            ctx.DrawText(vt, new Point(vx, y + rowH / 2 - vt.Height / 2));
        }
        bool spread = Math.Abs(_maxMs - _minMs) > 1;
        if (spread) { Bar(0, "max", _maxMs); Bar(1, "min", _minMs); }
        else Bar(0, "len", _minMs);

        // Axis ticks 0 / 500ms / 1.0s / 2.0s.
        void Tick(double ms, string lbl) { double tx = x0 + Math.Clamp(ms / FullMs, 0, 1) * (x1 - x0); var ft = new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, Axis); ctx.DrawText(ft, new Point(Math.Min(tx, x1 - ft.Width), bot + 2)); }
        Tick(0, "0"); Tick(500, "500 ms"); Tick(1000, "1.0 s"); Tick(2000, "2.0 s");
    }
}
