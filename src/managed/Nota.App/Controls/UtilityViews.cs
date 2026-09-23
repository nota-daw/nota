// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Utility card windows (the centre panel's three tabs), all read from the engine
// (Utility.h scopeRead), so the picture is what the device measures and runs:
//  • UtFieldView — Field: the stereo field by frequency — lows at the bottom, highs at the
//                  top; where each band's energy sits (brass trace), how wide it is (teal
//                  dots either side) and, dashed, how wide the width setting makes an
//                  uncorrelated signal. Drag sideways for the balance, up / down for the width;
//  • UtWidthView — Mono: the configured width over frequency (brass) — the mono region
//                  shaded — against the output's measured width (teal, dashed). Drag
//                  sideways for the mono cutoff, up / down for the width;
//  • UtLevelView — Levels: input (teal) and output (brass) over the last 8 s in the
//                  meter's unit, the target dashed. Drag up / down for the target.
// UtCorrBar is the −1 … +1 correlation bar of the Routing panel.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class UtFieldView : VtWindowBase
{
    private float[] _pan = Array.Empty<float>(), _wid = Array.Empty<float>(), _lvl = Array.Empty<float>(), _set = Array.Empty<float>();
    private int _n;
    private bool _valid;
    private double _lo = 30, _hi = 16000;
    private string _legend = "";

    /// <summary>The per-band pan / width share / level / configured width (%), the band span and the legend.</summary>
    public void Set(float[] src, int panAt, int widAt, int lvlAt, int setAt, int n, bool valid, double lo, double hi, string legend)
    {
        if (_pan.Length != n) { _pan = new float[n]; _wid = new float[n]; _lvl = new float[n]; _set = new float[n]; }
        if (n > 0 && Math.Max(Math.Max(panAt, widAt), Math.Max(lvlAt, setAt)) + n <= src.Length)
        {
            Array.Copy(src, panAt, _pan, 0, n); Array.Copy(src, widAt, _wid, 0, n);
            Array.Copy(src, lvlAt, _lvl, 0, n); Array.Copy(src, setAt, _set, 0, n);
        }
        _n = n; _valid = valid; _lo = lo > 0 ? lo : 30; _hi = hi > _lo ? hi : 16000; _legend = legend;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = 16, bot = h - 13, cx = w / 2, half = w / 2 - 10;
        for (int i = 1; i < 4; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(0, h * i / 4), new Point(w, h * i / 4));
        ctx.DrawLine(NotaGraph.GridPen, new Point(cx - half / 2, 0), new Point(cx - half / 2, h));
        ctx.DrawLine(NotaGraph.GridPen, new Point(cx + half / 2, 0), new Point(cx + half / 2, h));
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1.2), new Point(cx, 0), new Point(cx, h));

        double Yb(int b) => bot - (b + 0.5) / Math.Max(1, _n) * (bot - top);
        if (_n > 1)
        {
            // The width setting: how far an uncorrelated band would spread.
            var env = new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 3, 5 }, 0));
            var gl = new StreamGeometry(); var gr = new StreamGeometry();
            using (var cl = gl.Open())
            using (var cr = gr.Open())
            {
                for (int b = 0; b < _n; b++)
                {
                    double r = Math.Max(0, _set[b]) / 100.0, e = r / Math.Sqrt(1 + r * r);
                    var pl = new Point(cx - e * half, Yb(b)); var pr = new Point(cx + e * half, Yb(b));
                    if (b == 0) { cl.BeginFigure(pl, false); cr.BeginFigure(pr, false); } else { cl.LineTo(pl); cr.LineTo(pr); }
                }
                cl.EndFigure(false); cr.EndFigure(false);
            }
            ctx.DrawGeometry(null, env, gl); ctx.DrawGeometry(null, env, gr);

            if (_valid)
            {
                double loudest = -120;
                for (int b = 0; b < _n; b++) loudest = Math.Max(loudest, _lvl[b]);
                const double gate = 42;   // bands this far under the loudest are left out
                bool Quiet(int b) => _lvl[b] < loudest - gate || _lvl[b] < -100;
                // Teal dots: each band's spread either side of where it sits.
                for (int b = 0; b < _n; b++)
                {
                    if (Quiet(b)) continue;
                    double a = Math.Clamp((_lvl[b] - (loudest - gate)) / gate, 0.15, 1);
                    var dot = NotaPalette.Wash(NotaPalette.Teal, (byte)(40 + a * 190));
                    double x0 = cx + _pan[b] * half, y = Yb(b), s = Math.Clamp(_wid[b], 0, 1) * half;
                    ctx.DrawEllipse(dot, null, new Point(Math.Clamp(x0 - s, 2, w - 2), y), 1.7, 1.7);
                    ctx.DrawEllipse(dot, null, new Point(Math.Clamp(x0 + s, 2, w - 2), y), 1.7, 1.7);
                }
                // Brass trace: where each band's energy sits.
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    bool open = false;
                    for (int b = 0; b < _n; b++)
                    {
                        if (Quiet(b)) continue;   // joined across: a lone tone still reads as a kink
                        // Power-weighted over the neighbours, so a quiet band between two loud
                        // ones does not throw the trace across the field.
                        double sw = 0, sp = 0;
                        for (int j = Math.Max(0, b - 1); j <= Math.Min(_n - 1, b + 1); j++)
                        {
                            if (Quiet(j)) continue;
                            double wgt = Math.Pow(10, (_lvl[j] - loudest) / 10) * (j == b ? 2 : 1);
                            sw += wgt; sp += wgt * _pan[j];
                        }
                        var p = new Point(cx + Math.Clamp(sw > 0 ? sp / sw : _pan[b], -1, 1) * half, Yb(b));
                        if (!open) { c.BeginFigure(p, false); open = true; } else c.LineTo(p);
                    }
                    if (open) c.EndFigure(false);
                }
                ctx.DrawGeometry(null, NotaGraph.PrimaryPen, g);
            }
        }

        NotaGraph.Title(ctx, frame, "Stereo field");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _legend, NotaPalette.AccentBright);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "L");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "R");
        var c0 = NotaGraph.AxisText("C");
        ctx.DrawText(c0, new Point(cx - c0.Width / 2, h - c0.Height - 3));
        // Frequency marks up the left edge.
        foreach (double hz in new double[] { 100, 1000, 10000 })
        {
            if (hz < _lo || hz > _hi || _n < 2) continue;
            double t = Math.Log(hz / _lo) / Math.Log(_hi / _lo);
            double y = bot - t * (bot - top);
            var ft = NotaGraph.AxisText(hz >= 1000 ? NotaNum.F($"{hz / 1000:0} k") : NotaNum.F($"{hz:0}"));
            ctx.DrawText(ft, new Point(4, y - ft.Height / 2));
        }
        if (!_valid)
        {
            var ns = NotaGraph.AxisText("no signal");
            ctx.DrawText(ns, new Point(cx + 6, (top + bot) / 2 - ns.Height / 2));
        }
        else
        {
            var hint = NotaGraph.AxisText("lows at the bottom · highs at the top", NotaPalette.TealBright);
            ctx.DrawText(hint, new Point(w - hint.Width - 5, h - hint.Height - 14));
        }
    }
}

internal sealed class UtWidthView : VtWindowBase
{
    private float[] _resp = Array.Empty<float>(), _meas = Array.Empty<float>(), _lvl = Array.Empty<float>();
    private int _nr, _nb;
    private double _rLo = 20, _rHi = 20000, _bLo = 30, _bHi = 16000, _monoHz = double.NaN;
    private bool _valid;
    private string _legend = "";

    public const double LoHz = 20, HiHz = 18000;

    /// <summary>The configured width (%, nr points on a log grid rLo…rHi), the output's width share and
    /// level per band (nb bands, bLo…bHi), the mono cutoff (NaN = off) and the legend.</summary>
    public void Set(float[] src, int respAt, int nr, double rLo, double rHi, int measAt, int lvlAt, int nb, double bLo, double bHi, bool valid, double monoHz, string legend)
    {
        if (_resp.Length != nr) _resp = new float[nr];
        if (_meas.Length != nb) { _meas = new float[nb]; _lvl = new float[nb]; }
        if (nr > 0 && respAt + nr <= src.Length) Array.Copy(src, respAt, _resp, 0, nr);
        if (nb > 0 && Math.Max(measAt, lvlAt) + nb <= src.Length) { Array.Copy(src, measAt, _meas, 0, nb); Array.Copy(src, lvlAt, _lvl, 0, nb); }
        _nr = nr; _nb = nb; _rLo = rLo > 0 ? rLo : 20; _rHi = rHi > _rLo ? rHi : 20000; _bLo = bLo > 0 ? bLo : 30; _bHi = bHi > _bLo ? bHi : 16000;
        _valid = valid; _monoHz = monoHz; _legend = legend;
        InvalidateVisual();
    }

    /// <summary>0 % at the bottom, 100 % in the middle, 400 % at the top (octaves above 100 %).</summary>
    private static double Norm(double pct) => pct <= 100 ? Math.Max(0, pct) / 200 : 0.5 + Math.Min(2, Math.Log2(pct / 100)) / 4;

    private double At(float[] a, int n, double lo, double hi, double hz)
    {
        if (n < 2) return 100;
        double t = Math.Clamp(Math.Log(hz / lo) / Math.Log(hi / lo) * (n - 1), 0, n - 1);
        int i = (int)Math.Floor(t), j = Math.Min(n - 1, i + 1);
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
        double Y(double pct) => bot - Norm(pct) * (bot - top);
        foreach (double t in new double[] { 100, 1000, 10000 }) ctx.DrawLine(NotaGraph.GridPen, new Point(X(t), 0), new Point(X(t), h));
        foreach (double p in new double[] { 50, 200 }) ctx.DrawLine(NotaGraph.GridPen, new Point(0, Y(p)), new Point(w, Y(p)));
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, Y(100)), new Point(w, Y(100)));

        if (!double.IsNaN(_monoHz))
        {
            double mx = X(_monoHz);
            ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Teal, 0x1A), new Rect(0, 0, Math.Max(0, mx), h));
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xB0), 1, new DashStyle(new double[] { 3, 4 }, 0)), new Point(mx, 0), new Point(mx, h));
            var mono = NotaGraph.AxisText("mono", NotaPalette.TealBright);
            if (mx > mono.Width + 10) ctx.DrawText(mono, new Point(6, bot - mono.Height - 2));
        }

        // Measured: the output's side / mid ratio per band (dashed teal; gaps where it is quiet).
        if (_valid && _nb > 1)
        {
            double loudest = -120;
            for (int b = 0; b < _nb; b++) loudest = Math.Max(loudest, _lvl[b]);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                bool open = false;
                for (int b = 0; b < _nb; b++)
                {
                    double hz = _bLo * Math.Pow(_bHi / _bLo, (b + 0.5) / _nb);
                    if (hz < LoHz || hz > HiHz || _lvl[b] < loudest - 50 || _lvl[b] < -100) { if (open) { c.EndFigure(false); open = false; } continue; }
                    double s = Math.Clamp(_meas[b], 0, 0.9999), pct = Math.Min(400, 100 * s / Math.Sqrt(1 - s * s));
                    var p = new Point(X(hz), Y(pct));
                    if (!open) { c.BeginFigure(p, false); open = true; } else c.LineTo(p);
                }
                if (open) c.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x90), 1.1, new DashStyle(new double[] { 4, 4 }, 0)), g);
        }
        // Configured.
        if (_nr > 1)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                int px = Math.Max(2, (int)(w / 2));
                for (int x = 0; x <= px; x++)
                {
                    double hz = LoHz * Math.Pow(HiHz / LoHz, (double)x / px);
                    var p = new Point(x * w / px, Y(At(_resp, _nr, _rLo, _rHi, hz)));
                    if (x == 0) c.BeginFigure(p, false); else c.LineTo(p);
                }
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, g);
            if (!double.IsNaN(_monoHz)) NotaGraph.Node(ctx, new Point(X(_monoHz), Y(At(_resp, _nr, _rLo, _rHi, _monoHz))), true);
        }

        NotaGraph.Title(ctx, frame, "Width by frequency");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _legend, NotaPalette.AccentBright);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "20");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "18 k");
        foreach (double t in new double[] { 2000 })
        {
            var ft = NotaGraph.AxisText("2 k");
            ctx.DrawText(ft, new Point(X(t) - ft.Width / 2, h - ft.Height - 2));
        }
        if (!double.IsNaN(_monoHz))
        {
            var ft = NotaGraph.AxisText(_monoHz >= 1000 ? NotaNum.F($"{_monoHz / 1000:0.#} k") : NotaNum.F($"{_monoHz:0}"), NotaPalette.AccentBright);
            ctx.DrawText(ft, new Point(Math.Clamp(X(_monoHz) - ft.Width / 2, 22, Math.Max(22, w - ft.Width - 30)), h - ft.Height - 2));
        }
        var scale = NotaGraph.AxisText("100 %");
        ctx.DrawText(scale, new Point(w - scale.Width - 5, Y(100) - scale.Height - 1));
    }
}

internal sealed class UtLevelView : VtWindowBase
{
    private float[] _in = Array.Empty<float>(), _out = Array.Empty<float>();
    private int _n;
    private double _target = double.NaN, _sec = 8;
    private string _legend = "", _targetText = "";

    public const double RangeDb = 48;   // 0 … −48 in the meter's unit

    /// <summary>The histories (dB, oldest first; −120 = silence), the target line (NaN = none),
    /// the window length and the texts.</summary>
    public void Set(float[] src, int inAt, int outAt, int n, double target, double seconds, string legend, string targetText)
    {
        if (_in.Length != n) { _in = new float[n]; _out = new float[n]; }
        if (n > 0 && Math.Max(inAt, outAt) + n <= src.Length) { Array.Copy(src, inAt, _in, 0, n); Array.Copy(src, outAt, _out, 0, n); }
        _n = n; _target = target; _sec = seconds; _legend = legend; _targetText = targetText;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 4);
        double top = 14, bot = h - 12;
        double Y(double db) => top + Math.Clamp(-db / RangeDb, 0, 1) * (bot - top);
        if (!double.IsNaN(_target))
            ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1, new DashStyle(new double[] { 2, 4 }, 0)), new Point(0, Y(_target)), new Point(w, Y(_target)));

        StreamGeometry Line(float[] a)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            bool open = false;
            for (int k = 0; k < _n; k++)
            {
                if (a[k] <= -100) { if (open) { c.EndFigure(false); open = false; } continue; }
                var p = new Point(_n > 1 ? k * w / (_n - 1) : 0, Y(a[k]));
                if (!open) { c.BeginFigure(p, false); open = true; } else c.LineTo(p);
            }
            if (open) c.EndFigure(false);
            return g;
        }
        if (_n > 1)
        {
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xA0), 1.1), Line(_in));
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Line(_out));
            if (_in[_n - 1] > -100)
            {
                var lab = NotaGraph.AxisText("in", NotaPalette.TealBright);
                ctx.DrawText(lab, new Point(w - lab.Width - 5, Math.Clamp(Y(_in[_n - 1]) + 2, top, bot - lab.Height)));
            }
        }

        NotaGraph.Title(ctx, frame, "Input and output");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _legend, NotaPalette.AccentBright);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, NotaNum.F($"−{_sec:0} s"));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "now");
        var tt = NotaGraph.AxisText(_targetText);
        ctx.DrawText(tt, new Point((w - tt.Width) / 2, h - tt.Height - 3));
        foreach (double db in new double[] { -12, -24, -36 })
        {
            var ft = NotaGraph.AxisText(NotaNum.F($"{db:0}"));
            ctx.DrawText(ft, new Point(4, Y(db) - ft.Height / 2));
        }
    }
}

/// <summary>Correlation −1 … +1: the negative half tinted red, a needle at the value.</summary>
internal sealed class UtCorrBar : Control
{
    private double _c = 1;
    public UtCorrBar() { Height = 6; }
    public void Set(double c) { _c = Math.Clamp(c, -1, 1); InvalidateVisual(); }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 1) return;
        var rr = new RoundedRect(new Rect(0.5, 0.5, w - 1, h - 1), 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.GraphBorder, 1), rr);
        using (ctx.PushClip(rr)) ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Danger, 0x55), new Rect(0, 0, w / 2, h));
        double x = (_c + 1) / 2 * w;
        var ink = _c < 0 ? NotaPalette.Danger : _c < 0.3 ? NotaPalette.Accent : NotaPalette.Success;
        ctx.FillRectangle(ink, new Rect(Math.Clamp(x - 1.5, 0, w - 3), -1, 3, h + 2));
    }
}
