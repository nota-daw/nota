// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Valve card windows: VlResponseView — a magnitude response on a log-frequency grid,
// read from the engine (so the picture is the filters the amp runs): the curve now in brass,
// a reference dashed in teal (the tone stack flat, or the cabinet on axis) and an optional
// node. The Amp tab uses it for the tone stack (drag: sideways = the middle frequency, up /
// down = the middle), the Cab tab for the cabinet (sideways = off-axis, up / down = the mic
// distance). The Harmonics tab reuses VtHarmView.

using System;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VlResponseView : VtWindowBase
{
    private float[] _now = Array.Empty<float>(), _ref = Array.Empty<float>();
    private int _n;
    private double _gridLo = 30, _gridHi = 16000, _nodeHz = double.NaN;
    private string _title = "", _legend = "";

    /// <summary>Visible band (Hz) and dB span; the frequency labels under the window.</summary>
    public double LoHz { get; init; } = 60;
    public double HiHz { get; init; } = 12000;
    public double TopDb { get; init; } = 15;
    public double BottomDb { get; init; } = -15;
    public double[] Ticks { get; init; } = Array.Empty<double>();

    /// <summary>The two responses (dB, n points from gridLo to gridHi on a log scale), the node's
    /// frequency (NaN = none; it sits on the curve), the title and the top-right legend.</summary>
    public void Set(float[] src, int nowAt, int refAt, int n, double gridLo, double gridHi, double nodeHz, string title, string legend)
    {
        if (_now.Length != n) { _now = new float[n]; _ref = new float[n]; }
        if (n > 0 && nowAt + n <= src.Length && refAt + n <= src.Length) { Array.Copy(src, nowAt, _now, 0, n); Array.Copy(src, refAt, _ref, 0, n); }
        _n = n; _gridLo = gridLo > 0 ? gridLo : 30; _gridHi = gridHi > _gridLo ? gridHi : 16000;
        _nodeHz = nodeHz; _title = title; _legend = legend;
        InvalidateVisual();
    }

    private double At(float[] a, double hz)
    {
        if (_n < 2) return 0;
        double t = Math.Log(hz / _gridLo) / Math.Log(_gridHi / _gridLo) * (_n - 1);
        t = Math.Clamp(t, 0, _n - 1);
        int i = (int)Math.Floor(t), j = Math.Min(_n - 1, i + 1);
        return a[i] + (a[j] - a[i]) * (t - i);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = 14, bot = h - 12;
        double X(double hz) => Math.Log(hz / LoHz) / Math.Log(HiHz / LoHz) * w;
        double Y(double db) => top + (TopDb - Math.Clamp(db, BottomDb - 2, TopDb + 2)) / (TopDb - BottomDb) * (bot - top);

        foreach (double t in Ticks) ctx.DrawLine(NotaGraph.GridPen, new Point(X(t), 0), new Point(X(t), h));
        for (double db = Math.Ceiling(BottomDb / 6) * 6; db <= TopDb; db += 6)
            if (db > BottomDb) ctx.DrawLine(db == 0 ? new Pen(NotaPalette.BorderDefault, 1) : NotaGraph.GridPen, new Point(0, Y(db)), new Point(w, Y(db)));

        if (_n > 1)
        {
            StreamGeometry Line(float[] a)
            {
                var g = new StreamGeometry();
                using var c = g.Open();
                int px = Math.Max(2, (int)(w / 2));
                for (int x = 0; x <= px; x++)
                {
                    double hz = LoHz * Math.Pow(HiHz / LoHz, (double)x / px);
                    var p = new Point(x * w / px, Y(At(a, hz)));
                    if (x == 0) c.BeginFigure(p, false); else c.LineTo(p);
                }
                c.EndFigure(false);
                return g;
            }
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x90), 1.1, new DashStyle(new double[] { 4, 4 }, 0)), Line(_ref));
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Line(_now));
            if (!double.IsNaN(_nodeHz)) NotaGraph.Node(ctx, new Point(X(_nodeHz), Y(At(_now, _nodeHz))), true);
        }

        NotaGraph.Title(ctx, frame, _title);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _legend, NotaPalette.TealBright);
        for (int i = 0; i < Ticks.Length; i++)
        {
            double t = Ticks[i];
            bool near = !double.IsNaN(_nodeHz) && Math.Abs(Math.Log(t / _nodeHz)) < 0.25;
            var ft = NotaGraph.AxisText(t >= 1000 ? NotaNum.F($"{t / 1000:0.#} k") : NotaNum.F($"{t:0}"), near ? NotaPalette.AccentBright : null);
            double x = Math.Clamp(X(t) - ft.Width / 2, 4, Math.Max(4, w - ft.Width - 4));
            ctx.DrawText(ft, new Point(x, h - ft.Height - 2));
        }
    }
}
