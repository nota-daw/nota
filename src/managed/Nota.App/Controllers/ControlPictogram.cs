// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The picture of the control the Controller setup wizard is asking for (specs/051 FR-030): a knob,
// an endless encoder, a fader, a jog wheel or a button with its icon. Drawn flat in the shell's
// greys; the part to move is orange while the wizard waits, and a learned control turns green.

using System;
using Avalonia;
using Avalonia.Media;

namespace Nota.App.Controllers;

internal enum PictogramKind { Knob, Encoder, Fader, Jog, Button, Pair }

internal sealed class ControlPictogram : Avalonia.Controls.Control
{
    private PictogramKind _kind;
    private GlyphKind? _icon;
    private string? _text;
    private bool _learned;
    private GlyphKind? _rightIcon;
    private string? _rightText;

    public ControlPictogram()
    {
        Width = 72;
        Height = 72;
    }

    /// <summary>Show <paramref name="kind"/>; a button carries <paramref name="icon"/> or a short
    /// <paramref name="text"/> ("Undo").</summary>
    public void Show(PictogramKind kind, GlyphKind? icon = null, string? text = null)
    {
        _kind = kind; _icon = icon; _text = text; _learned = false;
        InvalidateVisual();
    }

    /// <summary>A pair: an encoder, or the two buttons it can also be (left, right).</summary>
    public void ShowPair(GlyphKind? leftIcon, string? leftText, GlyphKind? rightIcon, string? rightText)
    {
        _kind = PictogramKind.Pair; _icon = leftIcon; _text = leftText; _rightIcon = rightIcon; _rightText = rightText; _learned = false;
        InvalidateVisual();
    }

    /// <summary>The control was learned: the moving part turns green.</summary>
    public bool Learned
    {
        get => _learned;
        set { _learned = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        IBrush body = NotaPalette.SurfaceInset, edge = NotaPalette.BorderStrong;
        IBrush live = _learned ? NotaPalette.Success : NotaPalette.Accent;
        var c = new Point(w / 2, h / 2);
        double r = Math.Min(w, h) / 2 - 4;
        switch (_kind)
        {
            case PictogramKind.Knob:
            {
                // The arc a knob turns along, its cap and pointer.
                ctx.DrawGeometry(null, new Pen(edge, 3, lineCap: PenLineCap.Round), Arc(c, r, 0, 1));
                ctx.DrawGeometry(null, new Pen(live, 3, lineCap: PenLineCap.Round), Arc(c, r, 0, 0.65));
                ctx.DrawEllipse(body, new Pen(edge, 1), c, r - 7, r - 7);
                double a = (135 + 0.65 * 270) * Math.PI / 180;
                ctx.DrawLine(new Pen(NotaPalette.TextPrimary, 2.2, lineCap: PenLineCap.Round), c, c + new Vector(Math.Cos(a), Math.Sin(a)) * (r - 9));
                break;
            }
            case PictogramKind.Encoder:
            {
                // An endless knob: notches all round and the two ways to turn.
                ctx.DrawEllipse(body, new Pen(edge, 1), c, r - 6, r - 6);
                for (int i = 0; i < 16; i++)
                {
                    double a = i * Math.PI / 8;
                    var d = new Vector(Math.Cos(a), Math.Sin(a));
                    ctx.DrawLine(new Pen(edge, 1.4), c + d * (r - 6), c + d * (r - 2));
                }
                ctx.DrawGeometry(null, new Pen(live, 2.4, lineCap: PenLineCap.Round), ArcDeg(c, r - 13, -150, -30));
                Arrow(ctx, c, r - 13, -30, live, clockwise: true);
                Arrow(ctx, c, r - 13, -150, live, clockwise: false);
                break;
            }
            case PictogramKind.Fader:
            {
                // A slot and its cap.
                double x = c.X, top = 6, bottom = h - 6;
                ctx.DrawLine(new Pen(edge, 4, lineCap: PenLineCap.Round), new Point(x, top), new Point(x, bottom));
                ctx.DrawLine(new Pen(live, 4, lineCap: PenLineCap.Round), new Point(x, h * 0.42), new Point(x, bottom));
                var cap = new Rect(x - 13, h * 0.42 - 6, 26, 12);
                ctx.DrawRectangle(body, new Pen(NotaPalette.TextPrimary, 1.2), cap, 3, 3);
                ctx.DrawLine(new Pen(live, 2), new Point(cap.Left + 4, cap.Center.Y), new Point(cap.Right - 4, cap.Center.Y));
                break;
            }
            case PictogramKind.Jog:
            {
                // A big wheel with a finger dimple.
                ctx.DrawEllipse(body, new Pen(edge, 1.5), c, r, r);
                ctx.DrawEllipse(null, new Pen(edge, 1), c, r - 8, r - 8);
                ctx.DrawEllipse(live, null, c + new Vector(r * 0.45, -r * 0.45), 5, 5);
                Arrow(ctx, c, r + 1, -100, live, clockwise: true);
                break;
            }
            case PictogramKind.Pair:
            {
                // An encoder on the left, or two small buttons on the right.
                var ec = new Point(w * 0.28, c.Y);
                double er = Math.Min(w, h) * 0.23;
                ctx.DrawEllipse(body, new Pen(edge, 1), ec, er, er);
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6;
                    var d = new Vector(Math.Cos(a), Math.Sin(a));
                    ctx.DrawLine(new Pen(edge, 1.1), ec + d * er, ec + d * (er + 3));
                }
                ctx.DrawGeometry(null, new Pen(live, 2, lineCap: PenLineCap.Round), ArcDeg(ec, er - 6, -150, -30));
                Arrow(ctx, ec, er - 6, -30, live, clockwise: true);
                Arrow(ctx, ec, er - 6, -150, live, clockwise: false);
                double bs = Math.Min(w, h) * 0.3;
                var b1 = new Rect(w * 0.72 - bs / 2, c.Y - bs - 3, bs, bs);
                var b2 = new Rect(w * 0.72 - bs / 2, c.Y + 3, bs, bs);
                MiniButton(ctx, b1, _icon, _text, live, body);
                MiniButton(ctx, b2, _rightIcon, _rightText, live, body);
                break;
            }
            default:
            {
                // A pad-like button with its icon or word.
                var box = new Rect(c.X - r + 4, c.Y - r + 10, (r - 4) * 2, (r - 10) * 2);
                ctx.DrawRectangle(body, new Pen(live, 1.6), box, 6, 6);
                if (_icon is { } icon)
                {
                    double s = Math.Min(box.Width, box.Height) * 0.5;
                    Glyph.Draw(ctx, icon, new Rect(box.Center.X - s / 2, box.Center.Y - s / 2, s, s), NotaPalette.TextPrimary);
                }
                else if (_text is { Length: > 0 } t)
                {
                    var ft = new NotaFormattedText(t, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        NotaFonts.SansMedium, NotaType.KnobLabel, NotaPalette.TextPrimary);
                    ctx.DrawText(ft, new Point(box.Center.X - ft.Width / 2, box.Center.Y - ft.Height / 2));
                }
                break;
            }
        }
    }

    private static void MiniButton(DrawingContext ctx, Rect box, GlyphKind? icon, string? text, IBrush live, IBrush body)
    {
        ctx.DrawRectangle(body, new Pen(live, 1.3), box, 4, 4);
        if (icon is { } g)
        {
            double s = box.Width * 0.5;
            Glyph.Draw(ctx, g, new Rect(box.Center.X - s / 2, box.Center.Y - s / 2, s, s), NotaPalette.TextPrimary);
        }
        else if (text is { Length: > 0 } t)
        {
            var ft = new NotaFormattedText(t, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                NotaFonts.SansMedium, NotaType.KnobLabel * 0.85, NotaPalette.TextPrimary);
            ctx.DrawText(ft, new Point(box.Center.X - ft.Width / 2, box.Center.Y - ft.Height / 2));
        }
    }

    private static void Arrow(DrawingContext ctx, Point c, double r, double deg, IBrush ink, bool clockwise)
    {
        double a = deg * Math.PI / 180;
        var p = c + new Vector(Math.Cos(a), Math.Sin(a)) * r;
        var tangent = new Vector(-Math.Sin(a), Math.Cos(a)) * (clockwise ? 1 : -1);
        var normal = new Vector(Math.Cos(a), Math.Sin(a));
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(p + tangent * 5, true);
            x.LineTo(p + normal * 4);
            x.LineTo(p - normal * 4);
            x.EndFigure(true);
        }
        ctx.DrawGeometry(ink, null, g);
    }

    private static StreamGeometry Arc(Point c, double r, double t0, double t1) => ArcDeg(c, r, 135 + t0 * 270, 135 + t1 * 270);

    private static StreamGeometry ArcDeg(Point c, double r, double d0, double d1)
    {
        double a0 = d0 * Math.PI / 180, a1 = d1 * Math.PI / 180;
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(c + new Vector(Math.Cos(a0), Math.Sin(a0)) * r, false);
            x.ArcTo(c + new Vector(Math.Cos(a1), Math.Sin(a1)) * r, new Size(r, r), 0, d1 - d0 > 180, SweepDirection.Clockwise);
            x.EndFigure(false);
        }
        return g;
    }
}
