// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Scale NOTE MAP (almanac mockups 1a / 1b): the twelve pitch classes C → B as a row of
// key-shaped cells — the black keys shorter and hung from the top, the white ones standing on
// the bottom. Scale notes are filled Brass Deep with a brass edge, the root gets a Brass Light
// edge and "root", every other note names where Fold sends it ("→ D"). Clicking a cell toggles
// it (the owner switches the scale to Custom). The note sounding now lights up: its input cell
// brightens under a Brass Light ring, the cell it folded onto gets a fainter ring — and both
// fade out over a few frames after the key is released (the owner feeds Hit / Held each tick).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ScaleNoteMap : Control
{
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly bool[] Black = { false, true, false, true, false, false, true, false, true, false, true, false };
    private const double FadePerSecond = 4.5;   // a released note's glow is gone in ≈ ¼ s

    private readonly double[] _in = new double[12], _out = new double[12];
    private int _hover = -1;
    private DateTime _last = DateTime.UtcNow;

    public ScaleNoteMap()
    {
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>The mini card's map: tighter gaps, smaller type, "R" for the root.</summary>
    public bool Mini { get; init; }

    public int Root { get; private set; }
    public int Mask { get; private set; }   // 12 bits relative to Root
    public int Fold { get; private set; }   // 0 Nearest / 1 Down / 2 Up

    /// <summary>A pitch class was clicked (absolute, 0 = C).</summary>
    public event Action<int>? Toggled;

    /// <summary>The scale shown. Repaints only on a change.</summary>
    public void SetScale(int root, int mask, int fold)
    {
        if (root == Root && mask == Mask && fold == Fold) return;
        Root = root; Mask = mask; Fold = fold;
        InvalidateVisual();
    }

    /// <summary>One animation tick: <paramref name="heldIn"/> / <paramref name="heldOut"/> are the
    /// pitch classes sounding now (12-bit), <paramref name="hitIn"/> / <paramref name="hitOut"/>
    /// those that started since the last tick — a note shorter than a frame still flashes.</summary>
    public void Animate(int heldIn, int heldOut, int hitIn, int hitOut)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0, 0.25);
        _last = now;
        bool dirty = false;
        void Step(double[] a, int held, int hit)
        {
            for (int pc = 0; pc < 12; pc++)
            {
                double v = ((held | hit) >> pc & 1) != 0 ? 1.0 : Math.Max(0, a[pc] - dt * FadePerSecond);
                if (Math.Abs(v - a[pc]) > 1e-4) { a[pc] = v; dirty = true; }
            }
        }
        Step(_in, heldIn, hitIn);
        Step(_out, heldOut, hitOut);
        if (dirty) InvalidateVisual();
    }

    public bool InScale(int pc) => (Mask >> Rel(pc) & 1) != 0;
    private int Rel(int pc) => ((pc - Root) % 12 + 12) % 12;

    /// <summary>Where Fold sends an out-of-scale pitch class (absolute); itself when in scale / empty.</summary>
    public int Target(int pc) => FoldTarget(pc, Root, Mask, Fold);

    public static int FoldTarget(int pc, int root, int mask, int fold)
    {
        int rel = ((pc - root) % 12 + 12) % 12;
        bool Has(int x) => (mask >> ((x % 12 + 12) % 12) & 1) != 0;
        if (mask == 0 || Has(rel)) return pc;
        int d = 0;
        if (fold == 1) { for (int k = 1; k < 12; k++) if (Has(rel - k)) { d = -k; break; } }
        else if (fold == 2) { for (int k = 1; k < 12; k++) if (Has(rel + k)) { d = k; break; } }
        else { for (int k = 1; k < 7; k++) { if (Has(rel + k)) { d = k; break; } if (Has(rel - k)) { d = -k; break; } } }
        return ((pc + d) % 12 + 12) % 12;
    }

    // ---- geometry --------------------------------------------------------------------------------
    private double Gap => Mini ? 2 : 3;
    private Rect CellRect(int pc)
    {
        double w = Bounds.Width, h = Bounds.Height, gap = Gap;
        double cw = (w - gap * 11) / 12.0;
        double x = pc * (cw + gap);
        double ch = h * (Black[pc] ? 0.78 : 0.92);
        double y = Black[pc] ? 0 : h - ch;
        return new Rect(x, y, Math.Max(1, cw), ch);
    }
    private int HitCell(Point p)
    {
        double w = Bounds.Width, gap = Gap, cw = (w - gap * 11) / 12.0;
        if (cw <= 0) return -1;
        int pc = (int)Math.Floor(p.X / (cw + gap));
        return pc is >= 0 and < 12 ? pc : -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int pc = HitCell(e.GetPosition(this));
        if (pc < 0) return;
        e.Handled = true;
        Toggled?.Invoke(pc);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int pc = HitCell(e.GetPosition(this));
        if (pc != _hover) { _hover = pc; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover != -1) { _hover = -1; InvalidateVisual(); }
    }

    // ---- paint -------------------------------------------------------------------------------------
    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
    private static Color Over(Color top, byte alpha, Color under) => Mix(under, Color.FromRgb(top.R, top.G, top.B), alpha / 255.0);

    public override void Render(DrawingContext ctx)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var card = NotaPalette.SurfaceCard.Color;
        var brass = NotaPalette.Accent.Color;
        var lit = NotaPalette.AccentBright.Color;
        var deep = Over(brass, 0x1A, card);          // Brass Deep fill (#2A2317 in Graphite)
        var deepLive = Over(brass, 0x44, card);      // …while it sounds (#4A3A1C)
        Color offWhite = NotaPalette.GridRow.Color, offBlack = NotaPalette.BgSunken.Color, offLive = NotaPalette.TrackOff.Color;
        double r = Mini ? 2 : 3;
        double nameFs = Mini ? 7 : 9, subFs = 7, padB = Mini ? 4 : 6, gapT = Mini ? 2 : 3;
        var sans = NotaFonts.SansSemiBold;
        var mono = NotaFonts.Mono;

        for (int pc = 0; pc < 12; pc++)
        {
            var rc = CellRect(pc);
            bool inS = InScale(pc), isRoot = Rel(pc) == 0;
            double a = _in[pc], o = _out[pc];
            var baseFill = inS ? deep : Black[pc] ? offBlack : offWhite;
            var fill = Mix(baseFill, inS ? deepLive : offLive, Math.Max(a, o * 0.55));
            IBrush edge = isRoot ? NotaPalette.AccentBright : inS ? NotaPalette.AccentDim : pc == _hover ? NotaPalette.BorderStrong : NotaPalette.BorderDefault;
            ctx.DrawRectangle(new SolidColorBrush(fill), new Pen(edge, 1), rc.Deflate(0.5), r, r);
            if (pc == _hover && inS) ctx.DrawRectangle(null, new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x60), 1), rc.Deflate(1.5), r - 1, r - 1);

            // The live ring: 1px outside the edge (the mockup's box-shadow), full for the key
            // played, fainter for the note it folded onto.
            double ring = Math.Max(a, o * 0.5);
            if (ring > 0.01)
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * ring), lit.R, lit.G, lit.B)), 1),
                    rc.Inflate(0.5), r + 1, r + 1);

            // Name + what happens to it, standing on the cell's bottom.
            var nameInk = inS ? NotaPalette.TextPrimary : NotaPalette.TextTertiary;
            string sub = isRoot ? (Mini ? "R" : "root") : inS ? "·" : Mini ? Names[Target(pc)] : "→ " + Names[Target(pc)];
            IBrush subInk = isRoot ? NotaPalette.AccentBright : inS ? NotaPalette.Accent : NotaPalette.TextSecondary;
            if (Mask == 0) { sub = Mini ? "" : "thru"; subInk = NotaPalette.TextAxis; }
            var subFt = new FormattedText(sub, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono, subFs, subInk);
            var nameFt = new FormattedText(Names[pc], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, sans, nameFs, nameInk);
            double yb = rc.Bottom - padB;
            if (subFt.Width <= rc.Width) ctx.DrawText(subFt, new Point(rc.X + (rc.Width - subFt.Width) / 2, yb - subFt.Height));
            ctx.DrawText(nameFt, new Point(rc.X + (rc.Width - nameFt.Width) / 2, yb - subFt.Height - gapT - nameFt.Height + 2));
        }
    }
}
