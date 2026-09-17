// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Operator algorithm visuals (mockup 3g, ALGO tab):
//   • OperatorTopo   — shared layered layout of the 4 operators for one of the 11 kAlgo
//                      topologies (mirrors OperatorSynth::kAlgo so picture == sound).
//   • OperatorAlgoMini — a small clickable 4-box sketch of one algorithm (the picker cell).
//   • OperatorRoutingViz — the full-width routing diagram with real connection lines and
//                      drag-an-operator-onto-another to re-route (snaps to a matching algo).
// Carriers (reach the output) are brass; modulators are teal — Nota's audio/modulation code.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// Shared static topology data + layered layout, mirroring OperatorSynth::kAlgo.
internal static class OperatorTopo
{
    public const int Count = 11;
    public static readonly int[][] Algo =
    {
        new[]{1,2,3,4}, new[]{2,2,3,4}, new[]{1,3,3,4}, new[]{1,3,4,4},
        new[]{3,3,3,4}, new[]{1,4,3,4}, new[]{3,4,4,4}, new[]{1,2,4,4},
        new[]{4,3,4,4}, new[]{2,4,4,4}, new[]{4,4,4,4},
    };
    public static readonly string[] Desc =
    {
        "A→B→C→D · single chain", "A·B→C→D · dual stack", "A→B→D, C→D · two mods",
        "A→B→D · bell", "A·B·C→D · triple mod", "A→B, C→D · two voices",
        "A→D · additive + mod", "A→B→C · bright", "B→D · stacked carriers",
        "A→C · additive", "all parallel · organ",
    };
    public static readonly string[] Names = { "A", "B", "C", "D" };

    // Normalised layout (x,y ∈ 0..1; y=0 top / y=1 bottom). Carriers sit low, modulators
    // stack above their targets; carrier[o] = reaches OUT.
    public static void Layout(int[] tgt, out double[] x, out double[] y, out bool[] carrier)
    {
        x = new double[4]; y = new double[4]; carrier = new bool[4];
        var level = new int[4];
        for (int o = 3; o >= 0; o--) { carrier[o] = tgt[o] == 4; level[o] = tgt[o] == 4 ? 0 : level[tgt[o]] + 1; }
        int maxL = 0; for (int o = 0; o < 4; o++) maxL = Math.Max(maxL, level[o]);
        for (int L = 0; L <= maxL; L++)
        {
            int cnt = 0, idx = 0; for (int o = 0; o < 4; o++) if (level[o] == L) cnt++;
            for (int o = 0; o < 4; o++) if (level[o] == L)
            {
                x[o] = cnt == 1 ? 0.5 : 0.14 + idx / (double)(cnt - 1) * 0.72; idx++;
                y[o] = maxL == 0 ? 0.42 : 0.12 + (1.0 - L / (double)maxL) * 0.56;
            }
        }
    }

    // The op nearest a normalised point (for drag hit-testing), or -1.
    public static int Nearest(double[] x, double[] y, double px, double py, double r)
    {
        int best = -1; double bd = r * r;
        for (int o = 0; o < 4; o++) { double dx = x[o] - px, dy = y[o] - py, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = o; } }
        return best;
    }
}

// One clickable algorithm sketch (picker cell).
internal sealed class OperatorAlgoMini : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly Typeface Mono = NotaFonts.Mono;

    private readonly int _algo;
    private bool _active;
    public event Action<int>? Clicked;

    public OperatorAlgoMini(int algo)
    {
        _algo = algo; MinWidth = 30; MinHeight = 34;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, _) => Clicked?.Invoke(_algo);
    }
    public void SetActive(bool a) { if (_active != a) { _active = a; InvalidateVisual(); } }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(_active ? AccentSubtle : Sunken, new Pen(_active ? Brass : BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
        var num = new FormattedText((_algo + 1).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, _active ? AccentBright : TextTertiary);
        ctx.DrawText(num, new Point(3, 2));

        var tgt = OperatorTopo.Algo[_algo];
        OperatorTopo.Layout(tgt, out var nx, out var ny, out var carrier);
        double padX = 5, padT = 11, padB = 4;
        double gx0 = padX, gx1 = w - padX, gy0 = padT, gy1 = h - padB;
        Point Pt(int o) => new(gx0 + nx[o] * (gx1 - gx0), gy0 + ny[o] * (gy1 - gy0));

        var mod = new Pen(_active ? Teal : BorderDef, 1);
        for (int o = 0; o < 4; o++)
            if (tgt[o] < 4) ctx.DrawLine(mod, Pt(o), Pt(tgt[o]));
        double bs = Math.Min(7, (gx1 - gx0) / 4);
        for (int o = 0; o < 4; o++)
        {
            var p = Pt(o);
            var rect = new Rect(p.X - bs / 2, p.Y - bs / 2, bs, bs);
            var pen = new Pen(carrier[o] ? Brass : Teal, _active ? 1.2 : 0.9);
            ctx.DrawRectangle(carrier[o] && _active ? AccentSubtle : Sunken, pen, rect, 1.5, 1.5);
        }
    }
}

// Full routing diagram: labelled boxes + real lines + an OUT bus, drag to re-route.
internal sealed class OperatorRoutingViz : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush TealSubtle = NotaPalette.Wash(NotaPalette.Teal, 0x2E);
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly Typeface Bold = NotaFonts.SansBold;
    private static readonly Typeface Mono = NotaFonts.Mono;

    private int _algo;
    private double[] _x = new double[4], _y = new double[4];
    private bool[] _carrier = new bool[4];
    private Rect[] _boxRect = { default, default, default, default };
    private int _drag = -1;
    private Point _dragPt;
    public event Action<int>? AlgoPicked;

    public OperatorRoutingViz() { MinHeight = 70; ClipToBounds = true; }
    public void Set(int algo)
    {
        _algo = Math.Clamp(algo, 0, OperatorTopo.Count - 1);
        OperatorTopo.Layout(OperatorTopo.Algo[_algo], out _x, out _y, out _carrier);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        for (int o = 0; o < 4; o++)
            if (_boxRect[o].Contains(p)) { _drag = o; _dragPt = p; e.Pointer.Capture(this); InvalidateVisual(); return; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag < 0) return; _dragPt = e.GetPosition(this); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        int src = _drag; _drag = -1; e.Pointer.Capture(null);
        double w = Bounds.Width, h = Bounds.Height;
        var p = e.GetPosition(this);
        // Drop target: another box (must be a valid forward edge) or the OUT bus (bottom).
        int newTgt = -1;
        for (int o = 0; o < 4; o++) if (o != src && _boxRect[o].Contains(p)) newTgt = o;
        if (newTgt < 0 && p.Y > h * 0.78) newTgt = 4;   // dropped on the OUT lane
        if (newTgt >= 0) TryReroute(src, newTgt);
        InvalidateVisual();
    }

    // Change one operator's target, then snap to the algorithm that best matches.
    private void TryReroute(int src, int newTgt)
    {
        var cur = (int[])OperatorTopo.Algo[_algo].Clone();
        if (newTgt != 4 && newTgt <= src) return;   // must feed a higher-index op or OUT
        cur[src] = newTgt;
        int best = -1, bestScore = -1;
        for (int a = 0; a < OperatorTopo.Count; a++)
        {
            int score = 0; for (int o = 0; o < 4; o++) if (OperatorTopo.Algo[a][o] == cur[o]) score++;
            if (score > bestScore) { bestScore = score; best = a; }
        }
        if (best >= 0 && best != _algo && bestScore >= 3) { Set(best); AlgoPicked?.Invoke(best); }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        var tgt = OperatorTopo.Algo[_algo];

        double padX = 10, padT = 14, padB = 4;
        double gx0 = padX, gx1 = w - padX, gy0 = padT, gy1 = h - padB;
        double bw = Math.Min(38, (gx1 - gx0) / 4.6), bh = Math.Min(20, (gy1 - gy0) / 3.2);
        double outY = gy1 - bh * 0.4;
        Point Ctr(int o)
        {
            if (o == _drag) return _dragPt;
            return new(gx0 + _x[o] * (gx1 - gx0), gy0 + _y[o] * (gy1 - gy0));
        }

        // Header caption.
        ctx.DrawText(new FormattedText("ROUTING", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 8, TextTertiary), new Point(padX, 2));
        var hint = new FormattedText("drag an operator onto another to re-route", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, TextTertiary);
        ctx.DrawText(hint, new Point(gx1 - hint.Width, 2));

        // Connection lines (under the boxes).
        for (int o = 0; o < 4; o++)
        {
            var a = Ctr(o);
            if (tgt[o] < 4) { var b = Ctr(tgt[o]); ctx.DrawLine(new Pen(Teal, 1.4), a, b); }
            else ctx.DrawLine(new Pen(Brass, NotaGraph.PrimaryWidth), new Point(a.X, a.Y + bh / 2), new Point(a.X, outY));
        }
        // OUT bus.
        ctx.DrawLine(new Pen(Brass, NotaGraph.PrimaryWidth), new Point(gx0 + 4, outY), new Point(gx1 - 4, outY));
        ctx.DrawText(new FormattedText("OUT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, TextTertiary), new Point(gx1 - 22, outY + 1));

        // Boxes.
        for (int o = 0; o < 4; o++)
        {
            var c = Ctr(o);
            var rect = new Rect(c.X - bw / 2, c.Y - bh / 2, bw, bh);
            _boxRect[o] = rect;
            bool carr = _carrier[o];
            ctx.DrawRectangle(carr ? AccentSubtle : TealSubtle, new Pen(carr ? Brass : Teal, 1.3), rect, 3, 3);
            var ft = new FormattedText(OperatorTopo.Names[o], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 10, carr ? AccentBright : Teal);
            ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }
    }
}
