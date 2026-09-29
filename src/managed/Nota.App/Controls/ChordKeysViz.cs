// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chord RESULT keyboard (almanac mockups 1a / 1b): three octaves from the C at or below
// the chord's lowest note — 21 white keys with the black keys over them. A sounding note is
// lit: the played key Brass Light, an added note Brass Deep. With Keep root off the root is
// only outlined in brass. The owner drives the strum animation by passing the notes lit so
// far each tick; Set() repaints only when something changed.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ChordKeysViz : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush WellEdge = NotaPalette.GraphBorder;
    private static readonly IBrush WhiteKey = NotaPalette.MiniKeyWhite;
    private static readonly IBrush BlackKey = NotaPalette.KeyBlack;
    internal static readonly IBrush Added = NotaPalette.Wash(NotaPalette.Accent, 0xC8);   // Brass Deep in Graphite (≈ #B8862E), clearly lighter than Played in Paper
    private static readonly IBrush Played = NotaPalette.AccentBright;
    private static readonly IBrush Label = NotaPalette.TextAxis;
    private static readonly IPen RootRing = new Pen(NotaPalette.Accent, 1);

    private static readonly int[] WhiteIndex = { 0, -1, 1, -1, 2, 3, -1, 4, -1, 5, -1, 6 };   // pc → white slot
    private static readonly int[] BlackPos = { 0, 1, 0, 2, 0, 0, 4, 0, 5, 0, 6, 0 };           // pc → boundary it sits on

    private int _start = 48, _ring = -1;
    private readonly Dictionary<int, bool> _lit = new();   // pitch → played (true) / added (false)
    private string _sig = "";

    /// <summary>Draw the C labels under each octave (the large card; the mini one has no room).</summary>
    public bool Labels { get; init; } = true;

    /// <summary>Corner radius of the key bottoms (L 2, S square).</summary>
    public double KeyRadius { get; init; } = NotaRadius.ClipValue;

    public ChordKeysViz() { MinHeight = 24; ClipToBounds = true; }

    /// <summary>The first C shown (three octaves from it), the lit notes, and the root to outline
    /// when it is not played (-1 = none).</summary>
    public void Set(int start, IEnumerable<(int Pitch, bool Played)> lit, int ringPitch)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(start).Append('|').Append(ringPitch).Append('|');
        _lit.Clear();
        foreach (var (p, pl) in lit) { _lit[p] = pl; sb.Append(p).Append(pl ? 'p' : 'a').Append(','); }
        string sig = sb.ToString();
        _start = start; _ring = ringPitch;
        if (sig == _sig) return;
        _sig = sig;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        ctx.DrawRectangle(Well, null, frame, NotaRadius.BadgeValue, NotaRadius.BadgeValue);
        var inner = frame.Deflate(1);
        double ww = inner.Width / 21.0;
        var clip = ctx.PushClip(inner);

        IBrush Fill(int p, bool white) => _lit.TryGetValue(p, out bool pl) ? (pl ? Played : Added) : white ? WhiteKey : BlackKey;
        // Only the bottom corners round: the key starts above the clip by its radius.
        void Key(Rect rc, IBrush fill, int p)
        {
            var k = new Rect(rc.X, rc.Y - KeyRadius, rc.Width, rc.Height + KeyRadius);
            ctx.DrawRectangle(fill, null, k, KeyRadius, KeyRadius);
            if (p == _ring) ctx.DrawRectangle(null, RootRing, k.Deflate(0.5), KeyRadius, KeyRadius);
        }

        for (int o = 0; o < 3; o++)
            for (int pc = 0; pc < 12; pc++)
            {
                if (WhiteIndex[pc] < 0) continue;
                int p = _start + o * 12 + pc;
                double x = inner.X + (o * 7 + WhiteIndex[pc]) * ww;
                Key(new Rect(x, inner.Y, Math.Max(1, ww - 1), inner.Height), Fill(p, true), p);   // 1px well gap = the key divider
            }
        for (int o = 0; o < 3; o++)
            for (int pc = 0; pc < 12; pc++)
            {
                if (WhiteIndex[pc] >= 0) continue;
                int p = _start + o * 12 + pc;
                double cx = inner.X + (o * 7 + BlackPos[pc]) * ww;
                Key(new Rect(cx - ww * 0.3, inner.Y, ww * 0.6, inner.Height * 0.58), Fill(p, false), p);
            }
        if (Labels)
            for (int o = 0; o < 3; o++)
            {
                var ft = new FormattedText(DeviceCardKit.NoteName(_start + o * 12), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(NotaFonts.MonoFamily), 6, Label);
                double x = inner.X + o * 7 * ww + (ww - ft.Width) / 2;
                ctx.DrawText(ft, new Point(x, inner.Bottom - ft.Height - 1));
            }
        clip.Dispose();
        ctx.DrawRectangle(null, new Pen(WellEdge, 1), frame.Deflate(0.5), NotaRadius.BadgeValue, NotaRadius.BadgeValue);
    }
}
