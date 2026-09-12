// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Compressor gain-reduction history (mockup 2n): a scrolling meter that hangs from the
// top — depth = gain reduction — drawn in record red (red is reserved for reduction,
// brass stays signal). Push() the current reduction each UI tick; the control tracks a
// decaying peak. The current/peak numbers are shown by the card beside it.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class CompGrHistory : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Grid = NotaPalette.Wash(NotaPalette.BorderStrong, 0x30);
    private static readonly IBrush Red = NotaPalette.Danger;                                   // design-system red
    private static readonly IBrush RedFill = NotaPalette.Wash(NotaPalette.Danger, 0x55);
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly Typeface Face = new(FontFamily.Default);
    private const int N = 220;
    private const double MaxDb = 24.0;

    private readonly double[] _h = new double[N];
    private int _w;
    public double Current { get; private set; }
    public double Peak { get; private set; }

    public void Push(double grDb)
    {
        _h[_w % N] = grDb; _w++;
        Current = grDb;
        Peak = Math.Max(grDb, Peak * 0.995);
        InvalidateVisual();
    }

    private const double HeadH = 14.0;   // reserved top band for the labels — the plot starts below it

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);
        // The plot lives below a reserved header band so the labels stay readable when it fills.
        double top0 = HeadH, plotH = h - HeadH - 1;
        // Horizontal dB gridlines at −6/−12/−18.
        for (int d = 6; d < 24; d += 6) { double gy = top0 + d / MaxDb * plotH; ctx.DrawLine(new Pen(Grid, 1), new Point(2, gy), new Point(w - 2, gy)); }

        int count = Math.Min(N, _w);
        if (count > 1)
        {
            var top = new System.Collections.Generic.List<Point>();
            for (int i = 0; i < count; i++)
            {
                int idx = (_w - count + i) % N; if (idx < 0) idx += N;
                double x = 2 + (double)i / (count - 1) * (w - 4);
                double depth = Math.Clamp(_h[idx] / MaxDb, 0, 1) * plotH;
                top.Add(new Point(x, top0 + depth));
            }
            var geo = new StreamGeometry();
            using (var gc = geo.Open()) { gc.BeginFigure(new Point(top[0].X, top0), true); foreach (var p in top) gc.LineTo(p); gc.LineTo(new Point(top[^1].X, top0)); gc.EndFigure(true); }
            ctx.DrawGeometry(RedFill, null, geo);
            var pen = new Pen(Red, 1.4, lineJoin: PenLineJoin.Round);
            for (int i = 1; i < top.Count; i++) ctx.DrawLine(pen, top[i - 1], top[i]);
        }
        // Peak-hold line.
        if (Peak > 0.05) { double py = top0 + Math.Clamp(Peak / MaxDb, 0, 1) * plotH; ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Danger, 0x99), 1) { DashStyle = DashStyle.Dash }, new Point(2, py), new Point(w - 2, py)); }
        // Labels drawn in the reserved header band: title top-left, current · peak top-right (in red).
        ctx.DrawText(new FormattedText("GAIN REDUCTION", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, Axis), new Point(5, 2));
        string cur = Current <= 0.05 ? "0.0" : $"−{Current:0.0}", pk = Peak <= 0.05 ? "0.0" : $"−{Peak:0.0}";
        var ft = new FormattedText($"{cur} dB · peak {pk}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, Red);
        ctx.DrawText(ft, new Point(w - ft.Width - 5, 2));
    }
}
