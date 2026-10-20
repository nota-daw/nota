// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Small UI glyphs drawn as geometry. The almanac bans emoji and icon fonts: a shape is
// built from geometry or a thin stroke, never typed as a character — a character takes
// whatever face the OS falls back to (Geist has no ■ ▾ ✓), so its size and weight drift
// from the text around it. Use Glyph as a control, or Glyph.Draw inside a Render.
//
// Transport shapes follow the almanac exactly: Stop a 10px square with radius 1, Record an
// 11px circle, Play an 11×14 triangle. Everything else is a 1.5px stroke on the same grid.
//
// This is the app's one icon set (spec 012): no icon is drawn outside this file. Kinds below
// "Path kinds" are path data on a 16-unit grid, scaled as a whole to the requested size (never
// stretched to their own bounds, so a wide and a tall icon keep their optical balance). Their
// stroke parts take the shared pen — round caps and joins, max(1.2, size × 1.5/16) — and
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
    // Path kinds (16-unit grid, see Paths)
    Arrangement, Session, Modular, Keyboard, Sliders, Downloads, Star, StarFilled, Bookmark, Doc, Midi, Map, History, Sidebar, Options, Search, CaretRight, CaretDown, Levels, Gamepad, Phone, Plug, Library, Appearance, Shortcuts, MidiPort, Action, Track, Device, Preset, Patch, Loop, Speaker, SpeakerMute, SpeakerLow, SpeakerHigh, Metronome, Follow, Snap, Automation, Apple, Windows, Linux, ChainLink, Update,
    Lock, LockOpen,                // a reference (read-only) clip in the multi-clip editor, and the unlocked toggle
}

internal sealed class Glyph : Control
{
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
        var pen = new Pen(ink, Math.Max(1.2, s * 1.5 / 16), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
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

// 16-unit path kinds: (stroke, fill). Stroke parts use the shared pen; fill parts the ink.
    private static readonly Dictionary<GlyphKind, (string? Stroke, string? Fill)> Paths = new()
    {
        // Clip blocks on three lanes (the Arrangement view), solid.
        [GlyphKind.Arrangement] = (null, "M1.5 2.5 H8 V5.5 H1.5 Z M10 2.5 H14.5 V5.5 H10 Z M4 7 H12 V10 H4 Z M1.5 11.5 H6 V14.5 H1.5 Z M8 11.5 H13 V14.5 H8 Z"),
        // A grid of clip cells, three tracks by two scenes (the Session view), solid.
        [GlyphKind.Session] = (null, "M1.5 2.5 H5 V7 H1.5 Z M6.25 2.5 H9.75 V7 H6.25 Z M11 2.5 H14.5 V7 H11 Z M1.5 9 H5 V13.5 H1.5 Z M6.25 9 H9.75 V13.5 H6.25 Z M11 9 H14.5 V13.5 H11 Z"),
        // Three nodes joined by cables (the Modular view).
        [GlyphKind.Modular] = ("M1.7 4 A1.8 1.8 0 1 0 5.3 4 A1.8 1.8 0 1 0 1.7 4 Z M1.7 12 A1.8 1.8 0 1 0 5.3 12 A1.8 1.8 0 1 0 1.7 12 Z M10.7 8 A1.8 1.8 0 1 0 14.3 8 A1.8 1.8 0 1 0 10.7 8 Z M5.2 4.8 L10.8 7.2 M5.2 11.2 L10.8 8.8", null),
        // A piano keyboard: case, key dividers, three black keys.
        [GlyphKind.Keyboard] = ("M2.333 4 h11.333 v8 h-11.333 zM6.133 4 v8 M9.867 4 v8 M4.2 4 v3.333 M8 4 v3.333 M11.8 4 v3.333", null),
        // Three faders with offset handles (audio effects).
        [GlyphKind.Sliders] = ("M4.667 3 v10 M8 3 v10 M11.333 3 v10 M3.333 6 h2.667 M6.667 9.667 h2.667 M10 5 h2.667", null),
        // An arrow down onto a line.
        [GlyphKind.Downloads] = ("M8 2.667 V10 M4.667 6.667 L8 10 L11.333 6.667 M3.333 12.667 H12.667", null),
        // A five-point star, outlined.
        [GlyphKind.Star] = ("M8 2 L9.667 6 L14 6.2 L10.667 9 L11.8 13.333 L8 10.8 L4.2 13.333 L5.333 9 L2 6.2 L6.333 6 Z", null),
        // A five-point star, solid (a favourite).
        [GlyphKind.StarFilled] = (null, "M8 2 L9.667 6 L14 6.2 L10.667 9 L11.8 13.333 L8 10.8 L4.2 13.333 L5.333 9 L2 6.2 L6.333 6 Z"),
        // A bookmark (a saved preset).
        [GlyphKind.Bookmark] = ("M4.333 2.667 h7.333 v10.667 l-3.667 -2.933 l-3.667 2.933 z", null),
        // A document with a dog-ear and two lines (a project).
        [GlyphKind.Doc] = ("M4 2.333 h5.067 l3.2 3.2 v8.133 h-8.267 zM9.067 2.333 v3.2 h3.2 M6.133 8.933 h4 M6.133 11.2 h4", null),
        // An eighth note (MIDI effects).
        [GlyphKind.Midi] = ("M4 11.467 a1.733 1.6 0 1 0 3.467 0 a1.733 1.6 0 1 0 -3.467 0 M7.467 11.467 V3.333 c2 0.6 3.733 1.6 3.733 3.733", null),
        // Two nodes joined by a link (a MIDI-learn mapping).
        [GlyphKind.Map] = ("M2.267 11.733 a1.733 1.733 0 1 0 3.467 0 a1.733 1.733 0 1 0 -3.467 0 M10.267 4.267 a1.733 1.733 0 1 0 3.467 0 a1.733 1.733 0 1 0 -3.467 0 M5.267 10.467 L10.733 5.533", null),
        // A clock with a counter-clockwise tail (version history).
        [GlyphKind.History] = ("M3.067 6.133 A5.333 5.333 0 1 1 2.667 8 M3.067 6.133 L2.4 3.867 M3.067 6.133 L5.333 5.733 M8 5.067 V8 L10 9.333", null),
        // A window with its left sidebar ruled off.
        [GlyphKind.Sidebar] = ("M2.333 3.667 h11.333 v8.667 h-11.333 zM6.333 3.667 v8.667", null),
        // Three centred rules, shortening downward (view options).
        [GlyphKind.Options] = ("M2.667 4.667 H13.333 M4.667 8 H11.333 M6.667 11.333 H9.333", null),
        // A magnifier.
        [GlyphKind.Search] = ("M7 2.667 A4.333 4.333 0 1 0 7 11.333 A4.333 4.333 0 1 0 7 2.667 M10.333 10.333 L14 14", null),
        // A small solid triangle pointing right (a closed disclosure).
        [GlyphKind.CaretRight] = (null, "M5.5 3 L11.5 8 L5.5 13 Z"),
        // A small solid triangle pointing down (an open disclosure).
        [GlyphKind.CaretDown] = (null, "M3 5.5 L13 5.5 L8 11.5 Z"),
        // Level bars of different heights (audio).
        [GlyphKind.Levels] = ("M2.667 6.667 V9.333 M5.333 4.667 V11.333 M8 2.667 V13.333 M10.667 5.333 V10.667 M13.333 7.333 V8.667", null),
        // A game controller.
        [GlyphKind.Gamepad] = ("M4.667 5.333 H11.333 A2.667 2.667 0 0 1 14 8 V8.667 A2.667 2.667 0 0 1 9.333 10.667 H6.667 A2.667 2.667 0 0 1 2 8.667 V8 A2.667 2.667 0 0 1 4.667 5.333 ZM4.667 7.333 V8.667 M4 8 H5.333 M10.667 7.667 H10.673 M12 8.333 H12.007", null),
        // A phone (Remote).
        [GlyphKind.Phone] = ("M5.333 2 H10.667 A1 1 0 0 1 11.667 3 V13 A1 1 0 0 1 10.667 14 H5.333 A1 1 0 0 1 4.333 13 V3 A1 1 0 0 1 5.333 2 ZM7.333 12 H8.667", null),
        // A plug with two prongs (plug-ins).
        [GlyphKind.Plug] = ("M6 2 V4.667 M10 2 V4.667 M4 4.667 H12 V7.333 A4 4 0 0 1 4 7.333 ZM8 11.333 V14", null),
        // A folder with rounded corners (the content library).
        [GlyphKind.Library] = ("M2 4.667 A0.667 0.667 0 0 1 2.667 4 H6 L7.333 5.333 H13.333 A0.667 0.667 0 0 1 14 6 V12 A0.667 0.667 0 0 1 13.333 12.667 H2.667 A0.667 0.667 0 0 1 2 12 Z", null),
        // A half-filled circle (appearance).
        [GlyphKind.Appearance] = ("M8 2.667 A5.333 5.333 0 1 0 8 13.333 ZM8 2.667 A5.333 5.333 0 0 1 8 13.333", null),
        // A keycap row on a keyboard (shortcuts).
        [GlyphKind.Shortcuts] = ("M2 4.667 H14 V11.333 H2 ZM4.667 7.333 H4.673 M7.333 7.333 H7.34 M10 7.333 H10.007 M5.333 9.333 H10.667", null),
        // A circular arrow (a new version: Settings → Updates).
        [GlyphKind.Update] = ("M13 8 A5 5 0 1 1 11.536 4.464 M11.6 1.6 V4.5 H8.7", null),
        // A controller with keys (MIDI devices).
        [GlyphKind.MidiPort] = ("M2.667 3.333 H13.333 V12.667 H2.667 ZM5.333 3.333 V8.667 M8 3.333 V8.667 M10.667 3.333 V8.667", null),
        // A prompt: chevron and cursor (a command).
        [GlyphKind.Action] = ("M3.333 4.667 L6.667 8 L3.333 11.333 M8 11.333 H12.667", null),
        // Three rules, the last short (a track).
        [GlyphKind.Track] = ("M2.667 4.667 H13.333 M2.667 8 H13.333 M2.667 11.333 H8.667", null),
        // A box with a knob (a device).
        [GlyphKind.Device] = ("M2.667 3.333 H13.333 V12.667 H2.667 ZM6 8 A2 2 0 1 0 10 8 A2 2 0 1 0 6 8", null),
        // A bookmark (a preset).
        [GlyphKind.Preset] = ("M4.667 2.667 H11.333 V13.333 L8 10.667 L4.667 13.333 Z", null),
        // Two nodes joined by a cable (a modular patch).
        [GlyphKind.Patch] = ("M2.667 4.667 A1.667 1.667 0 1 0 6 4.667 A1.667 1.667 0 1 0 2.667 4.667 M10 11.333 A1.667 1.667 0 1 0 13.333 11.333 A1.667 1.667 0 1 0 10 11.333 M5.667 5.667 L10.333 10.333", null),
        // Two arrows chasing each other (loop).
        [GlyphKind.Loop] = ("M11.333 2 l2 2 l-2 2 M13.333 4 H5.333 a2.667 2.667 0 0 0 -2.667 2.667 v0.667 M4.667 14 l-2 -2 l2 -2 M2.667 12 h8 a2.667 2.667 0 0 0 2.667 -2.667 v-0.667", null),
        // A speaker cone; layer a Speaker* wave kind over it.
        [GlyphKind.Speaker] = ("M2.667 6 h2.667 l3.333 -2.667 v9.333 l-3.333 -2.667 h-2.667 z", null),
        // A cross beside the speaker (muted).
        [GlyphKind.SpeakerMute] = ("M10.667 6 l3.333 4 M14 6 l-3.333 4", null),
        // One wave beside the speaker (quiet).
        [GlyphKind.SpeakerLow] = ("M10.667 6.333 a2.333 2.333 0 0 1 0 3.333", null),
        // Two waves beside the speaker (loud).
        [GlyphKind.SpeakerHigh] = ("M10.667 6.333 a2.333 2.333 0 0 1 0 3.333 M12.333 4.667 a4.667 4.667 0 0 1 0 6.667", null),
        // A metronome: body, rod and weight.
        [GlyphKind.Metronome] = ("M6.533 3.667 H9.467 L12.667 13.333 H3.333 ZM8 12.667 L9.267 2.133", "M7.553 7.333 A1.067 1.067 0 1 0 9.687 7.333 A1.067 1.067 0 1 0 7.553 7.333 Z"),
        // The playhead flag standing on its ruler (follow playhead).
        [GlyphKind.Follow] = ("M2.333 11.867 H13.667 M6.333 8.333 V11.867", "M4.333 3.333 H8.333 V6 L6.333 8.333 L4.333 6 Z"),
        // A horseshoe magnet with banded poles (snap to grid).
        [GlyphKind.Snap] = (null, "M2.667 11.333 V8.333 A5.333 5.333 0 0 1 13.333 8.333 V11.333 H10.333 V8.333 A2.333 2.333 0 0 0 5.667 8.333 V11.333 ZM2.667 12.667 H5.667 V14.333 H2.667 ZM10.333 12.667 H13.333 V14.333 H10.333 Z"),
        // A breakpoint envelope with two handles.
        [GlyphKind.Automation] = ("M2 11.667 H6 L9.667 5 H14", "M4.733 11.667 A1.267 1.267 0 1 0 7.267 11.667 A1.267 1.267 0 1 0 4.733 11.667 ZM8.4 5 A1.267 1.267 0 1 0 10.933 5 A1.267 1.267 0 1 0 8.4 5 Z"),
        // The Apple mark.
        [GlyphKind.Apple] = (null, "F1 M8 4.8 C6.4 3.733 3 3.867 3 7.867 C3 11.067 4.933 14 6.2 14 C7.067 14 7.333 13.533 8 13.533 C8.667 13.533 8.933 14 9.8 14 C10.8 14 12.267 12.133 12.867 10.267 C11.733 9.8 11.067 8.8 11.067 7.667 C11.067 6.6 11.667 5.733 12.533 5.267 C11.467 4 9.333 3.933 8 4.8 ZM8.133 4.2 C8.133 2.867 9.067 1.867 10.4 1.8 C10.4 3.133 9.467 4.133 8.133 4.2 Z"),
        // The Windows mark.
        [GlyphKind.Windows] = (null, "M2.333 2.333 H7.333 V7.333 H2.333 ZM8.667 2.333 H13.667 V7.333 H8.667 ZM2.333 8.667 H7.333 V13.667 H2.333 ZM8.667 8.667 H13.667 V13.667 H8.667 Z"),
        // The Linux penguin (even-odd, so the belly is a hole).
        [GlyphKind.Linux] = (null, "F1 M8 1.333 C9.333 1.333 10.133 2.4 10.133 3.867 C10.133 4.933 9.667 5.467 10 6.267 C11.067 7.733 12.667 9.6 12.267 11.867 L13.2 14 H2.8 L3.733 11.867 C3.333 9.6 4.933 7.733 6 6.267 C6.333 5.467 5.867 4.933 5.867 3.867 C5.867 2.4 6.667 1.333 8 1.333 ZM8 6.8 C6.733 6.8 5.867 8.667 5.867 10.533 C5.867 12.2 6.8 13.2 8 13.2 C9.2 13.2 10.133 12.2 10.133 10.533 C10.133 8.667 9.267 6.8 8 6.8 Z"),
        // Two interlocked chain links (a live-freeze link).
        // A padlock, shut and open: a clip locked as reference in the multi-clip editor (specs/029).
        [GlyphKind.Lock] = ("M3.333 7.333 h9.333 v6.667 h-9.333 z M5.333 7.333 v-2 a2.667 2.667 0 0 1 5.333 0 v2", null),
        [GlyphKind.LockOpen] = ("M3.333 7.333 h9.333 v6.667 h-9.333 z M5.333 7.333 v-2 a2.667 2.667 0 0 1 5.2 -0.8", null),
        [GlyphKind.ChainLink] = ("M2.711 5.619 L6.266 9.175 A2.057 2.057 0 0 0 9.175 6.266 L5.619 2.711 A2.057 2.057 0 0 0 2.711 5.619 ZM6.825 9.734 L10.381 13.289 A2.057 2.057 0 0 0 13.289 10.381 L9.734 6.825 A2.057 2.057 0 0 0 6.825 9.734 Z", null),
    };

    private static readonly Dictionary<GlyphKind, (Geometry? Stroke, Geometry? Fill)> Parsed = new();

    // A path kind in the s×s box at (x, y): the 16-unit grid scaled as a whole, the stroke
    // kept at the shared on-screen weight.
    private static void DrawPath(DrawingContext ctx, GlyphKind kind, double x, double y, double s, IBrush ink, double strokePx)
    {
        if (!Parsed.TryGetValue(kind, out var g))
        {
            var (st, fi) = Paths[kind];
            Parsed[kind] = g = (st is null ? null : Geometry.Parse(st), fi is null ? null : Geometry.Parse(fi));
        }
        double k = s / 16;
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
