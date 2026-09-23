// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Forge (device kind 17) views, the "Nota Forge" mockup:
//  · FgTransferView — the transfer curve over input −1 … +1: the whole device in brass (in
//    Mid/Side and Multiband the selected stage, since each stage sees its own signal), every
//    stage alone as a faint curve, the LFO-modulated curve as teal dashes and a node where the
//    input sits on the curve now. Drag up / down for Amount; double-click resets it.
//  · FgHarmonicsView — harmonics 2 … 9 of a −6 dB test sine through that curve: odd partials in
//    brass, even ones in ink, with THD and its flavour.
// The curves and the partials come from the engine (Forge.h scopeRead), computed by the same
// shapers the audio runs, so the picture is what is heard.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal static class ForgeMath
{
    // Scope layout (Forge.h S_* / kTele / kHarm / kPts / kCurves).
    public const int S_InPeak = 0, S_OutPeak = 1, S_InRms = 2, S_OutRms = 3, S_SampleRate = 4, S_Cpu = 5, S_Latency = 6,
        S_OsFactor = 7, S_Lfo = 8, S_DriveMod = 9, S_Env = 10, S_ToneNow = 11, S_InLevel = 12, S_ThdChain = 13, S_ThdS1 = 14,
        S_FlavorChain = 17, S_FlavorS1 = 18, S_Signal = 21, S_Alias = 22, S_Routing = 23, S_OnCount = 24, S_LfoHz = 25,
        S_Bpm = 26, S_AmountDb = 27, S_OutputDb = 28;
    public const int kTele = 32, kHarm = 8, kHarmOff = kTele, kPts = 129, kCurves = 8, kCurveOff = kHarmOff + kHarm * 4,
        kScope = kCurveOff + kPts * kCurves;

    public static readonly string[] AlgoNames = { "Tube", "Diode", "Tape", "Fuzz", "Digital", "Fold" };
    // The type chips, in the mockup's order (soft → hard), and the algorithm each one picks.
    public static readonly string[] ChipNames = { "Tube", "Tape", "Diode", "Fuzz", "Fold", "Dig" };
    public static readonly int[] ChipAlgo = { 0, 2, 1, 3, 5, 4 };
    public static readonly string[] Flavors = { "clean", "odd-heavy", "even-heavy", "mixed" };
    public static readonly string[] DivNames = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };

    /// <summary>The role word a stage plays in a routing (Mid/Side, Multiband), or "".</summary>
    public static string Role(int routing, int stage) => routing switch
    {
        2 => stage switch { 0 => "MID", 1 => "SIDE", _ => "M+S" },
        3 => stage switch { 0 => "LOW < 180", 1 => "MID", _ => "HIGH > 2.4k" },
        1 => "PAR",
        _ => "",
    };
}

internal sealed class FgTransferView : ShDragView
{
    public const int HAmount = 0;
    private readonly float[] _main = new float[ForgeMath.kPts], _mod = new float[ForgeMath.kPts];
    private readonly float[][] _stage = { new float[ForgeMath.kPts], new float[ForgeMath.kPts], new float[ForgeMath.kPts] };
    private readonly bool[] _on = new bool[3];
    private int _sel;
    private bool _single, _active = true, _hasMod, _signal, _valid;
    private double _in;
    private string _label = "static";

    /// <summary>Takes the curves from a scope read. `single` draws the selected stage as the main
    /// curve (Mid/Side, Multiband); `hasMod` shows the LFO-modulated curve.</summary>
    public void Set(float[] sc, int n, bool[] on, int sel, bool single, bool active, bool hasMod, string label)
    {
        _valid = n >= ForgeMath.kScope;
        if (_valid)
        {
            int mi = single ? 2 + sel * 2 : 0;
            Array.Copy(sc, ForgeMath.kCurveOff + mi * ForgeMath.kPts, _main, 0, ForgeMath.kPts);
            Array.Copy(sc, ForgeMath.kCurveOff + (mi + 1) * ForgeMath.kPts, _mod, 0, ForgeMath.kPts);
            for (int s = 0; s < 3; s++) Array.Copy(sc, ForgeMath.kCurveOff + (2 + s * 2) * ForgeMath.kPts, _stage[s], 0, ForgeMath.kPts);
            _signal = sc[ForgeMath.S_Signal] > 0.5f;
            _in = Math.Clamp(sc[ForgeMath.S_InLevel], 0, 1);
        }
        for (int s = 0; s < 3; s++) _on[s] = on[s];
        _sel = sel; _single = single; _active = active; _hasMod = hasMod; _label = label;
        InvalidateVisual();
    }

    protected override int Hit(Point p) => HAmount;
    protected override double DragTo(int handle, Point start, Point p, double v0) => v0 + (start.Y - p.Y) / Math.Max(80, Bounds.Height * 1.4);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 1, x1 = w - 1, top = 1, bot = h - 1;
        double X(double x) => x0 + (x + 1) / 2 * (x1 - x0);
        double Y(double y) => top + (1 - (Math.Clamp(y, -1.25, 1.25) / 1.25 + 1) / 2) * (bot - top);

        using (ctx.PushClip(new RoundedRect(frame.Deflate(1), NotaGraph.Radius)))
        {
            ctx.DrawLine(NotaGraph.GridPen, new Point(X(-0.5), top), new Point(X(-0.5), bot));
            ctx.DrawLine(NotaGraph.GridPen, new Point(X(0.5), top), new Point(X(0.5), bot));
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(1)), new Point(x1, Y(1)));
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(-1)), new Point(x1, Y(-1)));
            var axis = new Pen(NotaPalette.BorderDefault, 1);
            ctx.DrawLine(axis, new Point(X(0), top), new Point(X(0), bot));
            ctx.DrawLine(axis, new Point(x0, Y(0)), new Point(x1, Y(0)));
            ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(X(-1), Y(-1)), new Point(X(1), Y(1)));

            if (_valid)
            {
                StreamGeometry Path(float[] ys)
                {
                    var g = new StreamGeometry();
                    using var gc = g.Open();
                    for (int i = 0; i < ys.Length; i++)
                    {
                        double x = -1 + 2.0 * i / (ys.Length - 1);
                        var pt = new Point(X(x), Y(ys[i]));
                        if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
                    }
                    gc.EndFigure(false);
                    return g;
                }
                // Every stage alone, faint; the selected one lit. Hidden when the device is bypassed,
                // and the selected one when it already is the main curve.
                if (_active)
                    for (int s = 0; s < 3; s++)
                    {
                        bool isSel = s == _sel;
                        if (_single && isSel) continue;
                        double op = _on[s] ? (isSel ? 0.45 : 0.7) : 0.25;
                        var ink = isSel ? NotaPalette.AccentBright : NotaPalette.TextDisabled;
                        using (ctx.PushOpacity(op))
                            ctx.DrawGeometry(null, new Pen(ink, 1), Path(_stage[s]));
                    }
                if (_active && _hasMod)
                    ctx.DrawGeometry(null, new Pen(NotaPalette.TealBright, 1.2, new DashStyle(new double[] { 4, 3 }, 0)), Path(_mod));
                ctx.DrawGeometry(null, new Pen(_active ? NotaPalette.Accent : NotaPalette.TextDisabled, Dragging ? 2.2 : NotaGraph.PrimaryWidth,
                    lineJoin: PenLineJoin.Round), Path(_main));

                // Where the input sits on the curve now.
                if (_active && _signal && _in > 1e-4)
                {
                    double fi = (_in + 1) / 2 * (ForgeMath.kPts - 1);
                    int i0 = Math.Clamp((int)Math.Floor(fi), 0, ForgeMath.kPts - 2);
                    double fr = fi - i0, y = _main[i0] + (_main[i0 + 1] - _main[i0]) * fr;
                    NotaGraph.Node(ctx, new Point(X(_in), Y(y)), true, NotaPalette.AccentBright);
                }
            }
        }

        NotaGraph.Title(ctx, frame, "Transfer");
        if (_active && _hasMod)
            NotaGraph.Legend(ctx, w - 6, 3, (_label, NotaPalette.AccentBright, NotaGraph.Mark.Line), ("modulated", NotaPalette.TealBright, NotaGraph.Mark.Dashed));
        else
            NotaGraph.Legend(ctx, w - 6, 3, (_active ? _label : "bypass", _active ? NotaPalette.AccentBright : NotaPalette.TextTertiary, NotaGraph.Mark.Line));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "in −1");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "+1");
        var o = NotaGraph.AxisText("out +1");
        ctx.DrawText(o, new Point(X(0) + 3, Y(1) - o.Height / 2));
    }
}

internal sealed class FgHarmonicsView : Control
{
    private readonly float[] _db = new float[ForgeMath.kHarm];
    private string _label = "";
    private bool _valid;

    public FgHarmonicsView() { ClipToBounds = true; }

    public void Set(float[] sc, int n, int set, string label)
    {
        _valid = n >= ForgeMath.kCurveOff;
        if (_valid) Array.Copy(sc, ForgeMath.kHarmOff + set * ForgeMath.kHarm, _db, 0, ForgeMath.kHarm);
        _label = label;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        NotaGraph.Title(ctx, frame, "Harmonics");
        var lbl = NotaGraph.AxisText(_label, NotaPalette.TextSecondary);
        ctx.DrawText(lbl, new Point(w - 6 - lbl.Width, 3));
        if (!_valid) return;

        double x0 = 8, x1 = w - 8, top = 15, bot = h - 10, gap = 6;
        int n = ForgeMath.kHarm;
        double bw = (x1 - x0 - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            int k = i + 2;
            double hh = Math.Clamp((_db[i] + 72) / 72, 0, 1);
            double x = x0 + i * (bw + gap);
            IBrush ink = hh > 0.02 ? (k % 2 == 1 ? NotaPalette.Accent : NotaPalette.TextSecondary) : NotaPalette.SurfaceRaised;
            double bh = Math.Max(1, hh * (bot - top));
            ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(x, bot - bh, bw, bh), 1, 1, 0, 0));
            var kt = NotaGraph.AxisText(k.ToString(), NotaPalette.TextDisabled);
            ctx.DrawText(kt, new Point(x + bw / 2 - kt.Width / 2, bot + 1));
        }
    }
}
