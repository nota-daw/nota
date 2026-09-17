// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Small glyphs + a pill toggle switch for the Auto Filter card (mockup 2b): filter-type
// response icons, LFO waveform icons, an envelope-shape glyph, and an on/off switch.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// An icon whose stroke colour tracks its chip's selected state.
internal interface IIconColor { IBrush Color { get; set; } }

// Filter response glyph: 0 LP, 1 BP, 2 HP, 3 Notch. Recolour via Color.
internal sealed class FilterTypeIcon : Control, IIconColor
{
    private readonly int _t;
    private IBrush _c;
    public FilterTypeIcon(int type, IBrush color) { _t = type; _c = color; Width = 22; Height = 15; }
    public IBrush Color { get => _c; set { _c = value; InvalidateVisual(); } }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var pen = new Pen(_c, 1.6, lineJoin: PenLineJoin.Round);
        double lo = h - 3, hi = 3, mid = h / 2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            switch (_t)
            {
                case 0: c.BeginFigure(new Point(2, hi), false); c.LineTo(new Point(w * 0.5, hi)); c.CubicBezierTo(new Point(w * 0.62, hi), new Point(w * 0.66, lo), new Point(w - 2, lo)); break;         // LP
                case 1: c.BeginFigure(new Point(2, lo), false); c.CubicBezierTo(new Point(w * 0.42, lo), new Point(w * 0.44, hi), new Point(w * 0.5, hi)); c.CubicBezierTo(new Point(w * 0.56, hi), new Point(w * 0.58, lo), new Point(w - 2, lo)); break; // BP
                case 2: c.BeginFigure(new Point(2, lo), false); c.CubicBezierTo(new Point(w * 0.34, lo), new Point(w * 0.38, hi), new Point(w * 0.5, hi)); c.LineTo(new Point(w - 2, hi)); break;          // HP
                default: c.BeginFigure(new Point(2, hi), false); c.LineTo(new Point(w * 0.4, hi)); c.LineTo(new Point(w * 0.5, lo)); c.LineTo(new Point(w * 0.6, hi)); c.LineTo(new Point(w - 2, hi)); break; // Notch
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }
}

// LFO waveform glyph: 0 sine, 1 tri, 2 saw, 3 square, 4 S&H. Recolour via Color.
internal sealed class LfoWaveIcon : Control, IIconColor
{
    private readonly int _w;
    private IBrush _c;
    public LfoWaveIcon(int wave, IBrush color) { _w = wave; _c = color; Width = 18; Height = 11; }
    public IBrush Color { get => _c; set { _c = value; InvalidateVisual(); } }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, mid = h / 2, top = 2, bot = h - 2;
        var pen = new Pen(_c, 1.3, lineJoin: PenLineJoin.Round);
        switch (_w)
        {
            case 1: ctx.DrawLine(pen, new Point(2, bot), new Point(w / 2, top)); ctx.DrawLine(pen, new Point(w / 2, top), new Point(w - 2, bot)); break;   // tri
            case 2: ctx.DrawLine(pen, new Point(2, bot), new Point(w - 2, top)); ctx.DrawLine(pen, new Point(w - 2, top), new Point(w - 2, bot)); break;   // saw
            case 3: // square
                ctx.DrawLine(pen, new Point(2, mid), new Point(2, top)); ctx.DrawLine(pen, new Point(2, top), new Point(w / 2, top));
                ctx.DrawLine(pen, new Point(w / 2, top), new Point(w / 2, bot)); ctx.DrawLine(pen, new Point(w / 2, bot), new Point(w - 2, bot));
                ctx.DrawLine(pen, new Point(w - 2, bot), new Point(w - 2, mid)); break;
            case 4: // S&H stepped
                double[] hs = { bot, top + 2, mid, top, bot - 1 };
                double sw = (w - 4) / hs.Length; Point? p = null;
                for (int i = 0; i < hs.Length; i++) { double x0 = 2 + i * sw, x1 = x0 + sw; if (p is { } pp) ctx.DrawLine(pen, pp, new Point(x0, hs[i])); ctx.DrawLine(pen, new Point(x0, hs[i]), new Point(x1, hs[i])); p = new Point(x1, hs[i]); }
                break;
            default: // sine
                Point prev = new(2, mid);
                for (int i = 1; i <= 16; i++) { double t = i / 16.0, x = 2 + (w - 4) * t, y = mid - Math.Sin(t * 2 * Math.PI) * (mid - top); var cur = new Point(x, y); ctx.DrawLine(pen, prev, cur); prev = cur; }
                break;
        }
    }
}

// A small envelope-shape glyph (attack rise → decay/sustain → release fall).
internal sealed class EnvGlyph : Control
{
    private static readonly IBrush Line = NotaPalette.Teal;
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.Ink("#5AC8D8"), 0x1E);
    public EnvGlyph() { }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var pts = new[] { new Point(3, h - 4), new Point(w * 0.22, 5), new Point(w * 0.55, h * 0.55), new Point(w - 4, h - 4) };
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(new Point(3, h), true); foreach (var p in pts) c.LineTo(p); c.LineTo(new Point(w - 4, h)); c.EndFigure(true); }
        ctx.DrawGeometry(Fill, null, g);
        var pen = new Pen(Line, 1.5, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < pts.Length; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
    }
}

// An on/off switch that owns its state and fires Changed — the same 18×10 brass pill as
// SwitchTrack (almanac § Controls), for call sites that want a self-toggling control.
internal sealed class ToggleSwitch : Control
{
    private readonly SwitchTrack _track = new();
    public event Action<bool>? Changed;
    public ToggleSwitch(bool on)
    {
        _track.IsOn = on;
        Width = SwitchTrack.W; Height = SwitchTrack.H;
        Cursor = new Cursor(StandardCursorType.Hand);
        LogicalChildren.Add(_track); VisualChildren.Add(_track);
    }
    public bool IsOn { get => _track.IsOn; set => _track.IsOn = value; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Left button only — right-click bubbles to the CV-modulate / MIDI Learn menu.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _track.IsOn = !_track.IsOn; Changed?.Invoke(_track.IsOn); e.Handled = true;
    }
    protected override Size ArrangeOverride(Size finalSize) { _track.Arrange(new Rect(finalSize)); return finalSize; }
    protected override Size MeasureOverride(Size availableSize) { _track.Measure(availableSize); return new Size(SwitchTrack.W, SwitchTrack.H); }
}
