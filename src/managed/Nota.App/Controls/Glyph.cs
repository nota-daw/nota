// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Small UI glyphs drawn as geometry. The almanac bans emoji and icon fonts: a shape is
// built from geometry or a thin stroke, never typed as a character — a character takes
// whatever face the OS falls back to (Geist has no ■ ▾ ✓), so its size and weight drift
// from the text around it. Use Glyph as a control, or Glyph.Draw inside a Render.
//
// Transport shapes follow the almanac exactly: Stop a 10px square with radius 1, Record an
// 11px circle, Play an 11×14 triangle. Everything else is a stroke of 1.6 units on the 24 grid.
//
// This is the app's one icon set (spec 012): no icon is drawn outside this file. Kinds below
// "Path kinds" are path data on a 24-unit grid, scaled as a whole to the requested size (never
// stretched to their own bounds, so a wide and a tall icon keep their optical balance). Their
// stroke parts take the shared pen — round caps and joins, max(1, size × 1.6/24) — and
// their fill parts take the ink.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Nota.App;

internal enum GlyphKind
{
    Play, Stop, Record, RecordRing, Close, Check, Cross,
    ChevronDown, ChevronUp, ChevronLeft, ChevronRight,
    StepLeft, StepRight,           // filled small triangles (◀ ▶ as "move")
    Edit, Freeze, PopOut, Cycle, Dot, Bypass, Plus, Grip, Minus,
    Headphones,                    // input monitoring
    // Context-menu icons
    Copy, Cut, Paste, Trash, Duplicate, Split, Consolidate, Stems, Reverse, Folder, Ungroup,
    Palette, Note, Input, Arrow, Grid, Select, Save, List, Compare, Flatten,
    // Path kinds (24-unit grid, see Paths)
    Arrangement, Session, Modular, Keyboard, Sliders, Downloads, Star, StarFilled, Bookmark, Doc, Midi, Map, History, Sidebar, Options, Search, CaretRight, CaretDown, Levels, Gamepad, Phone, Plug, Library, Appearance, Shortcuts, MidiPort, Action, Track, Device, Preset, Patch, Loop, Overdub, Replace, Speaker, SpeakerMute, SpeakerLow, SpeakerHigh, Metronome, Follow, Snap, Automation, Apple, Windows, Linux, ChainLink, Update,
    Lock, LockOpen,                // a reference (read-only) clip in the multi-clip editor, and the unlocked toggle
    DockIn,                        // PopOut's opposite: bring a floating window back into the main one
}

internal sealed class Glyph : Control
{
    /// <summary>The six filter types drawn as their response in a 22 × 12 box (EQ-8 and
    /// Dynamic EQ-8 band row): high-pass, low shelf, bell, notch, high shelf, low-pass.</summary>
    internal static readonly Geometry[] FilterShapes =
    {
        Geometry.Parse("M1,11 C4,4 6,3 10,3 L21,3"),
        Geometry.Parse("M1,3 L5,3 C9,3 11,9 15,9 L21,9"),
        Geometry.Parse("M1,9 L5,9 C8,9 9,3 11,3 C13,3 14,9 17,9 L21,9"),
        Geometry.Parse("M1,3 L7,3 C9,3 10,11 11,11 C12,11 13,3 15,3 L21,3"),
        Geometry.Parse("M1,9 L7,9 C11,9 13,3 17,3 L21,3"),
        Geometry.Parse("M1,3 L12,3 C16,3 18,4 21,11"),
    };

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    private GlyphKind _kind;
    private double _size;

    /// <summary>The shape. Setting it also sets the glyph's natural size (transport shapes
    /// have fixed almanac sizes; the rest are square at the size given).</summary>
    public GlyphKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            (Width, Height) = _size > 0 ? (_size, _size) : value switch
            {
                GlyphKind.Play => (11, 14),
                GlyphKind.Stop => (10, 10),
                GlyphKind.Record or GlyphKind.RecordRing => (11, 11),
                _ when Paths.ContainsKey(value) => (16, 16),
                _ => (10, 10),
            };
            InvalidateVisual();
        }
    }

    /// <summary>A square box of this size (0 = the natural size) — the ctor's size, settable
    /// from XAML. Set it after <see cref="Kind"/>.</summary>
    public double Size
    {
        get => _size;
        set { _size = value; Kind = _kind; }
    }

    /// <summary>The ink. Inherits the text foreground, so a glyph inside a button follows its state.</summary>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    static Glyph() => AffectsRender<Glyph>(ForegroundProperty);

    public Glyph() : this(GlyphKind.Dot) { }

    /// <param name="size">0 = the natural (almanac) size; otherwise a square box of this size.</param>
    public Glyph(GlyphKind kind, double size = 0)
    {
        _size = size;
        Kind = kind;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext ctx)
        => Draw(ctx, Kind, new Rect(Bounds.Size), Foreground ?? NotaPalette.TextStrong);

    /// <summary>Draw <paramref name="kind"/> into <paramref name="r"/> — for custom-drawn views.</summary>
    public static void Draw(DrawingContext ctx, GlyphKind kind, Rect r, IBrush ink)
    {
        double w = r.Width, h = r.Height, x = r.X, y = r.Y, s = Math.Min(w, h);
        double cx = x + w / 2, cy = y + h / 2;
        var pen = new Pen(ink, Math.Max(1.0, s * 1.6 / 24), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (Paths.ContainsKey(kind)) { DrawPath(ctx, kind, cx - s / 2, cy - s / 2, s, ink, pen.Thickness); return; }
        switch (kind)
        {
            case GlyphKind.Play:
            {
                // Keep the almanac's 11:14 proportion inside whatever box it is given.
                double ph = Math.Min(h, w * 14 / 11), pw = ph * 11 / 14, px = cx - pw / 2, py = cy - ph / 2;
                Fill(ctx, ink, new Point(px, py), new Point(px + pw, cy), new Point(px, py + ph));
                break;
            }
            case GlyphKind.Stop:
            {
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - s / 2, cy - s / 2, s, s), 1));
                break;
            }
            case GlyphKind.Record:
                ctx.DrawEllipse(ink, null, new Point(cx, cy), s / 2, s / 2);
                break;
            case GlyphKind.RecordRing:
                ctx.DrawEllipse(null, new Pen(ink, 1.2), new Point(cx, cy), s / 2 - 0.6, s / 2 - 0.6);
                break;
            case GlyphKind.Bypass:
            {
                // A ring with a slash: signal passes by.
                double rr = s * 0.36;
                ctx.DrawEllipse(null, pen, new Point(cx, cy), rr, rr);
                ctx.DrawLine(pen, new Point(cx - rr * 0.7, cy + rr * 0.7), new Point(cx + rr * 0.7, cy - rr * 0.7));
                break;
            }
            case GlyphKind.Plus:
            {
                double d = s * 0.36;
                ctx.DrawLine(pen, new Point(cx - d, cy), new Point(cx + d, cy));
                ctx.DrawLine(pen, new Point(cx, cy - d), new Point(cx, cy + d));
                break;
            }
            case GlyphKind.Minus:
            {
                double d = s * 0.36;
                ctx.DrawLine(pen, new Point(cx - d, cy), new Point(cx + d, cy));
                break;
            }
            case GlyphKind.Grip:
            {
                // Two columns of three dots: a drag handle.
                double dr = Math.Max(0.8, s * 0.09), dx = s * 0.16, dy = s * 0.28;
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j += 2)
                        ctx.DrawEllipse(ink, null, new Point(cx + j * dx, cy + i * dy), dr, dr);
                break;
            }
            case GlyphKind.Headphones:
            {
                // A headband arc over two ear cups.
                double rr = s * 0.36, top = cy + s * 0.06;
                var band = new StreamGeometry();
                using (var c = band.Open())
                {
                    c.BeginFigure(new Point(cx - rr, top), false);
                    c.ArcTo(new Point(cx + rr, top), new Size(rr, rr), 0, false, SweepDirection.Clockwise);
                    c.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, band);
                double cw = s * 0.2, ch = s * 0.32;
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - rr - cw * 0.5, top, cw, ch), cw * 0.35));
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx + rr - cw * 0.5, top, cw, ch), cw * 0.35));
                break;
            }
            case GlyphKind.Dot:
                ctx.DrawEllipse(ink, null, new Point(cx, cy), s * 0.3, s * 0.3);
                break;
            case GlyphKind.Close:
            case GlyphKind.Cross:
            {
                double d = s * 0.32;
                ctx.DrawLine(pen, new Point(cx - d, cy - d), new Point(cx + d, cy + d));
                ctx.DrawLine(pen, new Point(cx + d, cy - d), new Point(cx - d, cy + d));
                break;
            }
            case GlyphKind.Check:
                Stroke(ctx, pen, new Point(cx - s * 0.36, cy + s * 0.02), new Point(cx - s * 0.1, cy + s * 0.28), new Point(cx + s * 0.38, cy - s * 0.28));
                break;
            case GlyphKind.ChevronDown:
                Stroke(ctx, pen, new Point(cx - s * 0.3, cy - s * 0.14), new Point(cx, cy + s * 0.16), new Point(cx + s * 0.3, cy - s * 0.14));
                break;
            case GlyphKind.ChevronUp:
                Stroke(ctx, pen, new Point(cx - s * 0.3, cy + s * 0.14), new Point(cx, cy - s * 0.16), new Point(cx + s * 0.3, cy + s * 0.14));
                break;
            case GlyphKind.ChevronRight:
                Stroke(ctx, pen, new Point(cx - s * 0.14, cy - s * 0.3), new Point(cx + s * 0.16, cy), new Point(cx - s * 0.14, cy + s * 0.3));
                break;
            case GlyphKind.ChevronLeft:
                Stroke(ctx, pen, new Point(cx + s * 0.14, cy - s * 0.3), new Point(cx - s * 0.16, cy), new Point(cx + s * 0.14, cy + s * 0.3));
                break;
            case GlyphKind.StepLeft:
                Fill(ctx, ink, new Point(cx + s * 0.28, cy - s * 0.34), new Point(cx - s * 0.3, cy), new Point(cx + s * 0.28, cy + s * 0.34));
                break;
            case GlyphKind.StepRight:
                Fill(ctx, ink, new Point(cx - s * 0.28, cy - s * 0.34), new Point(cx + s * 0.3, cy), new Point(cx - s * 0.28, cy + s * 0.34));
                break;
            case GlyphKind.Edit:
            {
                // A pencil: a slanted shaft and its tip.
                Stroke(ctx, pen, new Point(cx - s * 0.34, cy + s * 0.34), new Point(cx - s * 0.2, cy + s * 0.06), new Point(cx + s * 0.2, cy - s * 0.34),
                    new Point(cx + s * 0.34, cy - s * 0.2), new Point(cx - s * 0.06, cy + s * 0.2), new Point(cx - s * 0.34, cy + s * 0.34));
                break;
            }
            case GlyphKind.Freeze:
            {
                // Three crossing strokes: frozen.
                double d = s * 0.4;
                for (int i = 0; i < 3; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 3;
                    ctx.DrawLine(pen, new Point(cx + Math.Cos(a) * d, cy + Math.Sin(a) * d), new Point(cx - Math.Cos(a) * d, cy - Math.Sin(a) * d));
                }
                break;
            }
            case GlyphKind.PopOut:
            {
                double d = s * 0.36;
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - d, cy - d * 0.6, d * 1.4, d * 1.6), 1));
                Stroke(ctx, pen, new Point(cx, cy - d), new Point(cx + d, cy - d), new Point(cx + d, cy));
                ctx.DrawLine(pen, new Point(cx + d, cy - d), new Point(cx + d * 0.1, cy - d * 0.1));
                break;
            }
            case GlyphKind.DockIn:
            {
                // PopOut's frame with the arrow turned around: it points into the frame.
                double d = s * 0.36;
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - d, cy - d * 0.6, d * 1.4, d * 1.6), 1));
                Stroke(ctx, pen, new Point(cx + d * 0.1, cy - d * 0.75), new Point(cx + d * 0.1, cy - d * 0.1), new Point(cx + d * 0.75, cy - d * 0.1));
                ctx.DrawLine(pen, new Point(cx + d, cy - d), new Point(cx + d * 0.1, cy - d * 0.1));
                break;
            }
            case GlyphKind.Cycle:
            {
                double rr = s * 0.34;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(cx + rr, cy), false);
                    c.ArcTo(new Point(cx, cy - rr), new Size(rr, rr), 0, true, SweepDirection.Clockwise);
                    c.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, g);
                Stroke(ctx, pen, new Point(cx - s * 0.12, cy - rr - s * 0.14), new Point(cx, cy - rr), new Point(cx - s * 0.12, cy - rr + s * 0.14));
                break;
            }
            case GlyphKind.Copy:
            {
                // A sheet in front of another whose top-left edges peek out.
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - s * 0.12, cy - s * 0.12, s * 0.5, s * 0.5), 1));
                Stroke(ctx, pen, new Point(cx - s * 0.38, cy + s * 0.14), new Point(cx - s * 0.38, cy - s * 0.38), new Point(cx + s * 0.14, cy - s * 0.38));
                break;
            }
            case GlyphKind.Cut:
            {
                // Scissors: two crossing blades over two finger rings.
                double rr = s * 0.13;
                ctx.DrawEllipse(null, pen, new Point(cx - s * 0.24, cy + s * 0.28), rr, rr);
                ctx.DrawEllipse(null, pen, new Point(cx + s * 0.24, cy + s * 0.28), rr, rr);
                ctx.DrawLine(pen, new Point(cx - s * 0.16, cy + s * 0.16), new Point(cx + s * 0.22, cy - s * 0.42));
                ctx.DrawLine(pen, new Point(cx + s * 0.16, cy + s * 0.16), new Point(cx - s * 0.22, cy - s * 0.42));
                break;
            }
            case GlyphKind.Paste:
            {
                // A clipboard: the board and its clip.
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - s * 0.34, cy - s * 0.3, s * 0.68, s * 0.74), 1));
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - s * 0.2, cy - s * 0.46, s * 0.4, s * 0.28), 1));
                break;
            }
            case GlyphKind.Trash:
            {
                ctx.DrawLine(pen, new Point(cx - s * 0.4, cy - s * 0.24), new Point(cx + s * 0.4, cy - s * 0.24));
                Stroke(ctx, pen, new Point(cx - s * 0.12, cy - s * 0.26), new Point(cx - s * 0.12, cy - s * 0.4), new Point(cx + s * 0.12, cy - s * 0.4), new Point(cx + s * 0.12, cy - s * 0.26));
                Stroke(ctx, pen, new Point(cx - s * 0.28, cy - s * 0.2), new Point(cx - s * 0.22, cy + s * 0.4), new Point(cx + s * 0.22, cy + s * 0.4), new Point(cx + s * 0.28, cy - s * 0.2));
                break;
            }
            case GlyphKind.Duplicate:
            {
                // A clip and its filled twin right after it.
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - s * 0.42, cy - s * 0.22, s * 0.36, s * 0.44), 1));
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx + s * 0.04, cy - s * 0.26, s * 0.42, s * 0.52), 1));
                break;
            }
            case GlyphKind.Split:
            {
                // Two clip halves either side of a cut line.
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - s * 0.46, cy - s * 0.22, s * 0.3, s * 0.44), 1));
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx + s * 0.16, cy - s * 0.22, s * 0.3, s * 0.44), 1));
                ctx.DrawLine(pen, new Point(cx, cy - s * 0.44), new Point(cx, cy + s * 0.44));
                break;
            }
            case GlyphKind.Consolidate:
            {
                // Converging strokes folding into one block.
                Stroke(ctx, pen, new Point(cx - s * 0.42, cy - s * 0.32), new Point(cx - s * 0.1, cy), new Point(cx - s * 0.42, cy + s * 0.32));
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx + s * 0.04, cy - s * 0.28, s * 0.4, s * 0.56), 1));
                break;
            }
            case GlyphKind.Stems:
            {
                // Consolidate the other way: one block fanning out into three strokes.
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - s * 0.44, cy - s * 0.28, s * 0.3, s * 0.56), 1));
                var fork = new Point(cx - s * 0.04, cy);
                ctx.DrawLine(pen, fork, new Point(cx + s * 0.44, cy - s * 0.34));
                ctx.DrawLine(pen, fork, new Point(cx + s * 0.44, cy));
                ctx.DrawLine(pen, fork, new Point(cx + s * 0.44, cy + s * 0.34));
                break;
            }
            case GlyphKind.Reverse:
            {
                // ⇄: one arrow each way.
                double y1 = cy - s * 0.2, y2 = cy + s * 0.2, d = s * 0.4, hd = s * 0.15;
                ctx.DrawLine(pen, new Point(cx - d, y1), new Point(cx + d, y1));
                Stroke(ctx, pen, new Point(cx + d - hd, y1 - hd), new Point(cx + d, y1), new Point(cx + d - hd, y1 + hd));
                ctx.DrawLine(pen, new Point(cx + d, y2), new Point(cx - d, y2));
                Stroke(ctx, pen, new Point(cx - d + hd, y2 - hd), new Point(cx - d, y2), new Point(cx - d + hd, y2 + hd));
                break;
            }
            case GlyphKind.Folder:
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(cx - s * 0.42, cy + s * 0.34), false);
                    c.LineTo(new Point(cx - s * 0.42, cy - s * 0.34));
                    c.LineTo(new Point(cx - s * 0.1, cy - s * 0.34));
                    c.LineTo(new Point(cx + s * 0.02, cy - s * 0.2));
                    c.LineTo(new Point(cx + s * 0.42, cy - s * 0.2));
                    c.LineTo(new Point(cx + s * 0.42, cy + s * 0.34));
                    c.EndFigure(true);
                }
                ctx.DrawGeometry(null, pen, g);
                break;
            }
            case GlyphKind.Ungroup:
            {
                // Two blocks pulled apart.
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - s * 0.42, cy - s * 0.42, s * 0.36, s * 0.36), 1));
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx + s * 0.06, cy + s * 0.06, s * 0.36, s * 0.36), 1));
                break;
            }
            case GlyphKind.Palette:
            {
                // Three swatches.
                double dr = s * 0.15;
                ctx.DrawEllipse(ink, null, new Point(cx, cy - s * 0.24), dr, dr);
                ctx.DrawEllipse(ink, null, new Point(cx - s * 0.26, cy + s * 0.2), dr, dr);
                ctx.DrawEllipse(ink, null, new Point(cx + s * 0.26, cy + s * 0.2), dr, dr);
                break;
            }
            case GlyphKind.Note:
            {
                // An eighth note: head, stem, flag.
                ctx.DrawEllipse(ink, null, new Point(cx - s * 0.12, cy + s * 0.26), s * 0.18, s * 0.14);
                Stroke(ctx, pen, new Point(cx + s * 0.05, cy + s * 0.24), new Point(cx + s * 0.05, cy - s * 0.42), new Point(cx + s * 0.34, cy - s * 0.2));
                break;
            }
            case GlyphKind.Input:
            {
                // An arrow entering a bracket.
                ctx.DrawLine(pen, new Point(cx - s * 0.44, cy), new Point(cx + s * 0.08, cy));
                Stroke(ctx, pen, new Point(cx - s * 0.1, cy - s * 0.18), new Point(cx + s * 0.08, cy), new Point(cx - s * 0.1, cy + s * 0.18));
                Stroke(ctx, pen, new Point(cx + s * 0.08, cy - s * 0.38), new Point(cx + s * 0.38, cy - s * 0.38), new Point(cx + s * 0.38, cy + s * 0.38), new Point(cx + s * 0.08, cy + s * 0.38));
                break;
            }
            case GlyphKind.Arrow:
            {
                ctx.DrawLine(pen, new Point(cx - s * 0.4, cy), new Point(cx + s * 0.38, cy));
                Stroke(ctx, pen, new Point(cx + s * 0.12, cy - s * 0.26), new Point(cx + s * 0.38, cy), new Point(cx + s * 0.12, cy + s * 0.26));
                break;
            }
            case GlyphKind.Grid:
            {
                // 2×2 slots: the session grid.
                double q = s * 0.34, o = s * 0.04;
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                        ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx + (i == 0 ? -q - o : o), cy + (j == 0 ? -q - o : o), q, q), 1));
                break;
            }
            case GlyphKind.Select:
            {
                // Four corner marks framing a range.
                double d = s * 0.4, l = s * 0.22;
                Stroke(ctx, pen, new Point(cx - d, cy - d + l), new Point(cx - d, cy - d), new Point(cx - d + l, cy - d));
                Stroke(ctx, pen, new Point(cx + d - l, cy - d), new Point(cx + d, cy - d), new Point(cx + d, cy - d + l));
                Stroke(ctx, pen, new Point(cx + d, cy + d - l), new Point(cx + d, cy + d), new Point(cx + d - l, cy + d));
                Stroke(ctx, pen, new Point(cx - d + l, cy + d), new Point(cx - d, cy + d), new Point(cx - d, cy + d - l));
                break;
            }
            case GlyphKind.Save:
            {
                // An arrow dropping into a tray.
                ctx.DrawLine(pen, new Point(cx, cy - s * 0.42), new Point(cx, cy + s * 0.12));
                Stroke(ctx, pen, new Point(cx - s * 0.2, cy - s * 0.08), new Point(cx, cy + s * 0.12), new Point(cx + s * 0.2, cy - s * 0.08));
                Stroke(ctx, pen, new Point(cx - s * 0.4, cy + s * 0.1), new Point(cx - s * 0.4, cy + s * 0.38), new Point(cx + s * 0.4, cy + s * 0.38), new Point(cx + s * 0.4, cy + s * 0.1));
                break;
            }
            case GlyphKind.List:
            {
                for (int i = -1; i <= 1; i++)
                    ctx.DrawLine(pen, new Point(cx - s * 0.38, cy + i * s * 0.3), new Point(cx + s * 0.38, cy + i * s * 0.3));
                break;
            }
            case GlyphKind.Compare:
            {
                // A / B: a square, half filled.
                double d = s * 0.38;
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - d, cy - d, d * 2, d * 2), 1));
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - d, cy - d, d, d * 2), 1));
                break;
            }
            case GlyphKind.Flatten:
            {
                // A wave above the flat line it becomes.
                Stroke(ctx, pen, new Point(cx - s * 0.42, cy - s * 0.1), new Point(cx - s * 0.21, cy - s * 0.38), new Point(cx, cy - s * 0.1),
                    new Point(cx + s * 0.21, cy - s * 0.38), new Point(cx + s * 0.42, cy - s * 0.1));
                ctx.DrawLine(pen, new Point(cx - s * 0.42, cy + s * 0.3), new Point(cx + s * 0.42, cy + s * 0.3));
                break;
            }
        }
    }

// 24-unit path kinds: (stroke, fill). Stroke parts use the shared pen; fill parts the ink.
    private static readonly Dictionary<GlyphKind, (string? Stroke, string? Fill)> Paths = new()
    {
        // Clip blocks on three lanes (the Arrangement view), solid.
        [GlyphKind.Arrangement] = (null, "M2.25 3.75 H12 V8.25 H2.25 Z M15 3.75 H21.75 V8.25 H15 Z M6 10.5 H18 V15 H6 Z M2.25 17.25 H9 V21.75 H2.25 Z M12 17.25 H19.5 V21.75 H12 Z"),
        // A grid of clip cells, three tracks by two scenes (the Session view), solid.
        [GlyphKind.Session] = (null, "M2.25 3.75 H7.5 V10.5 H2.25 Z M9.375 3.75 H14.625 V10.5 H9.375 Z M16.5 3.75 H21.75 V10.5 H16.5 Z M2.25 13.5 H7.5 V20.25 H2.25 Z M9.375 13.5 H14.625 V20.25 H9.375 Z M16.5 13.5 H21.75 V20.25 H16.5 Z"),
        // Three nodes joined by cables (the Modular view).
        [GlyphKind.Modular] = ("M2.55 6 A2.7 2.7 0 1 0 7.95 6 A2.7 2.7 0 1 0 2.55 6 Z M2.55 18 A2.7 2.7 0 1 0 7.95 18 A2.7 2.7 0 1 0 2.55 18 Z M16.05 12 A2.7 2.7 0 1 0 21.45 12 A2.7 2.7 0 1 0 16.05 12 Z M7.8 7.2 L16.2 10.8 M7.8 16.8 L16.2 13.2", null),
        // A piano keyboard: case, key dividers, three black keys.
        [GlyphKind.Keyboard] = ("M3.5 6 h17 v12 h-17 zM9.2 6 v12 M14.801 6 v12 M6.3 6 v5 M12 6 v5 M17.7 6 v5", null),
        // Three faders with offset handles (audio effects).
        [GlyphKind.Sliders] = ("M7 4.5 v15 M12 4.5 v15 M17 4.5 v15 M5 9 h4 M10 14.5 h4 M15 7.5 h4", null),
        // An arrow down onto a line.
        [GlyphKind.Downloads] = ("M12 4 V15 M7 10 L12 15 L17 10 M5 19 H19", null),
        // A five-point star, outlined.
        [GlyphKind.Star] = ("M12 3 L14.5 9 L21 9.3 L16 13.5 L17.7 20 L12 16.2 L6.3 20 L8 13.5 L3 9.3 L9.5 9 Z", null),
        // A five-point star, solid (a favourite).
        [GlyphKind.StarFilled] = (null, "M12 3 L14.5 9 L21 9.3 L16 13.5 L17.7 20 L12 16.2 L6.3 20 L8 13.5 L3 9.3 L9.5 9 Z"),
        // A bookmark (a saved preset).
        [GlyphKind.Bookmark] = ("M6.5 4 h11 v16 l-5.5 -4.399 l-5.5 4.399 z", null),
        // A document with a dog-ear and two lines (a project).
        [GlyphKind.Doc] = ("M6 3.5 h7.601 l4.8 4.8 v12.199 h-12.4 zM13.601 3.5 v4.8 h4.8 M9.2 13.399 h6 M9.2 16.8 h6", null),
        // An eighth note (MIDI effects).
        [GlyphKind.Midi] = ("M6 17.201 a2.599 2.4 0 1 0 5.2 0 a2.599 2.4 0 1 0 -5.2 0 M11.2 17.201 V5 c3 0.9 5.599 2.4 5.599 5.599", null),
        // Two nodes joined by a link (a MIDI-learn mapping).
        [GlyphKind.Map] = ("M3.401 17.599 a2.599 2.599 0 1 0 5.2 0 a2.599 2.599 0 1 0 -5.2 0 M15.4 6.401 a2.599 2.599 0 1 0 5.2 0 a2.599 2.599 0 1 0 -5.2 0 M7.901 15.701 L16.099 8.3", null),
        // A clock with a counter-clockwise tail (version history).
        [GlyphKind.History] = ("M4.601 9.2 A8 8 0 1 1 4 12 M4.601 9.2 L3.6 5.8 M4.601 9.2 L8 8.599 M12 7.601 V12 L15 14", null),
        // A window with its left sidebar ruled off.
        [GlyphKind.Sidebar] = ("M3.5 5.5 h17 v13 h-17 zM9.5 5.5 v13", null),
        // Three centred rules, shortening downward (view options).
        [GlyphKind.Options] = ("M4 7 H20 M7 12 H17 M10 17 H14", null),
        // A magnifier.
        [GlyphKind.Search] = ("M10.5 4 A6.5 6.5 0 1 0 10.5 17 A6.5 6.5 0 1 0 10.5 4 M15.5 15.5 L21 21", null),
        // A small solid triangle pointing right (a closed disclosure).
        [GlyphKind.CaretRight] = (null, "M8.25 4.5 L17.25 12 L8.25 19.5 Z"),
        // A small solid triangle pointing down (an open disclosure).
        [GlyphKind.CaretDown] = (null, "M4.5 8.25 L19.5 8.25 L12 17.25 Z"),
        // Level bars of different heights (audio).
        [GlyphKind.Levels] = ("M4 10 V14 M8 7 V17 M12 4 V20 M16 8 V16 M20 11 V13", null),
        // A game controller.
        [GlyphKind.Gamepad] = ("M7 8 H17 A4 4 0 0 1 21 12 V13 A4 4 0 0 1 14 16 H10 A4 4 0 0 1 3 13 V12 A4 4 0 0 1 7 8 ZM7 11 V13 M6 12 H8 M16 11.5 H16.009 M18 12.5 H18.011", null),
        // A phone (Remote).
        [GlyphKind.Phone] = ("M8 3 H16 A1.5 1.5 0 0 1 17.5 4.5 V19.5 A1.5 1.5 0 0 1 16 21 H8 A1.5 1.5 0 0 1 6.5 19.5 V4.5 A1.5 1.5 0 0 1 8 3 ZM11 18 H13", null),
        // A plug with two prongs (plug-ins).
        [GlyphKind.Plug] = ("M9 3 V7 M15 3 V7 M6 7 H18 V11 A6 6 0 0 1 6 11 ZM12 17 V21", null),
        // A folder with rounded corners (the content library).
        [GlyphKind.Library] = ("M3 7 A1.001 1.001 0 0 1 4 6 H9 L11 8 H20 A1.001 1.001 0 0 1 21 9 V18 A1.001 1.001 0 0 1 20 19 H4 A1.001 1.001 0 0 1 3 18 Z", null),
        // A half-filled circle (appearance).
        [GlyphKind.Appearance] = ("M12 4 A8 8 0 1 0 12 20 ZM12 4 A8 8 0 0 1 12 20", null),
        // A keycap row on a keyboard (shortcuts).
        [GlyphKind.Shortcuts] = ("M3 7 H21 V17 H3 ZM7 11 H7.01 M11 11 H11.01 M15 11 H15.011 M8 14 H16", null),
        // A circular arrow (a new version: Settings → Updates).
        [GlyphKind.Update] = ("M19.5 12 A7.5 7.5 0 1 1 17.304 6.696 M17.4 2.4 V6.75 H13.05", null),
        // A controller with keys (MIDI devices).
        [GlyphKind.MidiPort] = ("M4 5 H20 V19 H4 ZM8 5 V13 M12 5 V13 M16 5 V13", null),
        // A prompt: chevron and cursor (a command).
        [GlyphKind.Action] = ("M5 7 L10 12 L5 17 M12 17 H19", null),
        // Three rules, the last short (a track).
        [GlyphKind.Track] = ("M4 7 H20 M4 12 H20 M4 17 H13", null),
        // A box with a knob (a device).
        [GlyphKind.Device] = ("M4 5 H20 V19 H4 ZM9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12", null),
        // A bookmark (a preset).
        [GlyphKind.Preset] = ("M7 4 H17 V20 L12 16 L7 20 Z", null),
        // Two nodes joined by a cable (a modular patch).
        [GlyphKind.Patch] = ("M4 7 A2.501 2.501 0 1 0 9 7 A2.501 2.501 0 1 0 4 7 M15 17 A2.501 2.501 0 1 0 20 17 A2.501 2.501 0 1 0 15 17 M8.5 8.5 L15.5 15.5", null),
        // Two arrows chasing each other (loop).
        [GlyphKind.Loop] = ("M17 3 l3 3 l-3 3 M20 6 H8 a4 4 0 0 0 -4 4 v1.001 M7 21 l-3 -3 l3 -3 M4 18 h12 a4 4 0 0 0 4 -4 v-1.001", null),
        // Two stacked layers (loop recording that adds every pass).
        [GlyphKind.Overdub] = ("M4 10 H14 V20 H4 Z M9 10 V5 H20 V15 H14", null),
        // One layer, a turn arrow over it (loop recording that keeps the latest pass).
        [GlyphKind.Replace] = ("M4 10 H14 V20 H4 Z M9 7 A6 6 0 0 1 20 9.5 V14 M17.5 11.5 L20 14 L22.5 11.5", null),
        // A speaker cone; layer a Speaker* wave kind over it.
        [GlyphKind.Speaker] = ("M4 9 h4 l5 -4 v14 l-5 -4 h-4 z", null),
        // A cross beside the speaker (muted).
        [GlyphKind.SpeakerMute] = ("M16 9 l5 6 M21 9 l-5 6", null),
        // One wave beside the speaker (quiet).
        [GlyphKind.SpeakerLow] = ("M16 9.5 a3.5 3.5 0 0 1 0 5", null),
        // Two waves beside the speaker (loud).
        [GlyphKind.SpeakerHigh] = ("M16 9.5 a3.5 3.5 0 0 1 0 5 M18.5 7 a7 7 0 0 1 0 10", null),
        // A metronome: body, rod and weight.
        [GlyphKind.Metronome] = ("M9.8 5.5 H14.201 L19 20 H5 ZM12 19 L13.9 3.2", "M11.329 11 A1.6 1.6 0 1 0 14.53 11 A1.6 1.6 0 1 0 11.329 11 Z"),
        // A playhead with a short arrow pointing right (follow playhead: the view keeps up with it).
        [GlyphKind.Follow] = ("M7 7.5 V20 M12 13.5 H20 M17 10.5 L20 13.5 L17 16.5", "M4.5 4 H9.5 L7 7.5 Z"),
        // A horseshoe magnet with banded poles (snap to grid).
        [GlyphKind.Snap] = (null, "M4 17 V12.5 A8 8 0 0 1 20 12.5 V17 H15.5 V12.5 A3.5 3.5 0 0 0 8.5 12.5 V17 ZM4 19 H8.5 V21.5 H4 ZM15.5 19 H20 V21.5 H15.5 Z"),
        // A breakpoint envelope with two handles.
        [GlyphKind.Automation] = ("M3 17.5 H9 L14.5 7.5 H21", "M7.099 17.5 A1.9 1.9 0 1 0 10.901 17.5 A1.9 1.9 0 1 0 7.099 17.5 ZM12.6 7.5 A1.9 1.9 0 1 0 16.399 7.5 A1.9 1.9 0 1 0 12.6 7.5 Z"),
        // The Apple mark.
        [GlyphKind.Apple] = (null, "F1 M8 4.8 C6.4 3.733 3 3.867 3 7.867 C3 11.067 4.933 14 6.2 14 C7.067 14 7.333 13.533 8 13.533 C8.667 13.533 8.933 14 9.8 14 C10.8 14 12.267 12.133 12.867 10.267 C11.733 9.8 11.067 8.8 11.067 7.667 C11.067 6.6 11.667 5.733 12.533 5.267 C11.467 4 9.333 3.933 8 4.8 ZM8.133 4.2 C8.133 2.867 9.067 1.867 10.4 1.8 C10.4 3.133 9.467 4.133 8.133 4.2 Z"),
        // The Windows mark.
        [GlyphKind.Windows] = (null, "M3.5 3.5 H11 V11 H3.5 ZM13 3.5 H20.5 V11 H13 ZM3.5 13 H11 V20.5 H3.5 ZM13 13 H20.5 V20.5 H13 Z"),
        // The Linux penguin (even-odd, so the belly is a hole).
        [GlyphKind.Linux] = (null, "F1 M8 1.333 C9.333 1.333 10.133 2.4 10.133 3.867 C10.133 4.933 9.667 5.467 10 6.267 C11.067 7.733 12.667 9.6 12.267 11.867 L13.2 14 H2.8 L3.733 11.867 C3.333 9.6 4.933 7.733 6 6.267 C6.333 5.467 5.867 4.933 5.867 3.867 C5.867 2.4 6.667 1.333 8 1.333 ZM8 6.8 C6.733 6.8 5.867 8.667 5.867 10.533 C5.867 12.2 6.8 13.2 8 13.2 C9.2 13.2 10.133 12.2 10.133 10.533 C10.133 8.667 9.267 6.8 8 6.8 Z"),
        // Two interlocked chain links (a live-freeze link).
        // A padlock, shut and open: a clip locked as reference in the multi-clip editor (specs/029).
        [GlyphKind.Lock] = ("M5 11 h14 v10 h-14 z M8 11 v-3 a4 4 0 0 1 8 0 v3", null),
        [GlyphKind.LockOpen] = ("M5 11 h14 v10 h-14 z M8 11 v-3 a4 4 0 0 1 7.8 -1.2", null),
        [GlyphKind.ChainLink] = ("M4.066 8.428 L9.399 13.763 A3.085 3.085 0 0 0 13.763 9.399 L8.428 4.066 A3.085 3.085 0 0 0 4.066 8.428 ZM10.238 14.601 L15.572 19.933 A3.085 3.085 0 0 0 19.933 15.572 L14.601 10.238 A3.085 3.085 0 0 0 10.238 14.601 Z", null),
    };

    private static readonly Dictionary<GlyphKind, (Geometry? Stroke, Geometry? Fill)> Parsed = new();

    // A path kind in the s×s box at (x, y): the 24-unit grid scaled as a whole, the stroke
    // kept at the shared on-screen weight.
    private static void DrawPath(DrawingContext ctx, GlyphKind kind, double x, double y, double s, IBrush ink, double strokePx)
    {
        if (!Parsed.TryGetValue(kind, out var g))
        {
            var (st, fi) = Paths[kind];
            Parsed[kind] = g = (st is null ? null : Geometry.Parse(st), fi is null ? null : Geometry.Parse(fi));
        }
        double k = s / 24;
        using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(x, y)))
        {
            if (g.Fill is not null) ctx.DrawGeometry(ink, null, g.Fill);
            if (g.Stroke is not null)
                ctx.DrawGeometry(null, new Pen(ink, strokePx / k, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g.Stroke);
        }
    }

    private static void Fill(DrawingContext ctx, IBrush ink, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(true);
        }
        ctx.DrawGeometry(ink, null, g);
    }

    private static void Stroke(DrawingContext ctx, IPen pen, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    /// <summary>An existing caption followed by a drawn chevron whose ink follows the text's
    /// foreground — for dropdown captions that used to end in a typed "▾".</summary>
    public static StackPanel WithChevron(TextBlock text, double glyphSize = 8)
    {
        var chev = new Glyph(GlyphKind.ChevronDown, glyphSize);
        chev.Bind(ForegroundProperty, text.GetObservable(TextBlock.ForegroundProperty));
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5,
            HorizontalAlignment = text.HorizontalAlignment, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { text, chev },
        };
    }

    /// <summary>A label followed by a glyph (a "Volume ▾" style dropdown caption).</summary>
    public static StackPanel Labeled(string text, GlyphKind kind, double fontSize = 11, double glyphSize = 8, double spacing = 5)
        => new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = spacing,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { new TextBlock { Text = text, FontSize = fontSize, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, new Glyph(kind, glyphSize) },
        };
}
