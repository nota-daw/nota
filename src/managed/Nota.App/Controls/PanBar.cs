// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A compact bipolar pan bar for the arrangement track header: a
// rounded field with a centre tick and a brass fill that grows from the centre
// toward the panned side, "50L … C … 50R" text over it. Drag to set; double-click types
// a value, ⌥-click centres it. Fires GestureBegin/End so callers can bracket an
// automation write, like MiniFader / the gauge Knob.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class PanBar : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush Border = NotaPalette.Rule;
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.VizInk, 0x40);   // at rest: neutral
    private static readonly IBrush FillLive = NotaPalette.Wash(NotaPalette.Accent, 0x8C); // live
    private static readonly IBrush CentreTick = NotaPalette.BorderStrong;
    private static readonly IBrush TextBrush = NotaPalette.TextSecondary;
    private static readonly Typeface Mono = NotaFonts.Mono;

    private double _pan;   // -1 (full left) .. +1 (full right), 0 = centre
    private bool _drag;

    public event Action<double>? PanChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    /// <summary>True while dragging — callers skip external value writes then.</summary>
    public bool Dragging => _drag;

    public double Pan
    {
        get => _pan;
        set { _pan = Math.Clamp(value, -1, 1); InvalidateVisual(); }
    }

    public PanBar(double pan = 0)
    {
        _pan = Math.Clamp(pan, -1, 1);
        Height = 13;
        MinWidth = 34;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);   // almanac: vertical drag for every value control
    }

    private double _lastY, _raw;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0) { GestureBegin?.Invoke(); Apply(0); GestureEnd?.Invoke(); e.Handled = true; return; }   // centre
        _drag = true;
        _lastY = e.GetPosition(this).Y;
        _raw = _pan;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y, dy = _lastY - y;   // up = pan right; full sweep over ~140px
        _lastY = y;
        if (dy == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        _raw = Math.Clamp(_raw + dy / (fine ? 700.0 : 70.0), -1, 1);   // accumulate, so the centre detent cannot trap the drag
        Apply(_raw);
    }

    // Under the pointer (or in the hand) the control is live and takes the accent; at rest
    // its value is neutral ink — the accent marks a knob or fader only while focused or dragged.
    private bool _hover;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
        InvalidateVisual();   // a drag that ended off the control leaves no hover to repaint it
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    /// <summary>The bar as a typeable value ("30L", "C", "12R" on its own 50 scale); ⌥-click centres.</summary>
    internal ValueField EntryField(string name)
        => new(name, () => (_pan + 1) / 2, n => Label(Detent(n * 2 - 1)), n => Apply(n * 2 - 1),
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), 0.5);

    private static double Detent(double p) { p = Math.Clamp(p, -1, 1); return Math.Abs(p) < 0.04 ? 0 : p; }   // small centre detent

    private void Apply(double p)
    {
        p = Detent(p);
        if (Math.Abs(p - _pan) < 1e-4) return;
        _pan = p;
        InvalidateVisual();
        PanChanged?.Invoke(_pan);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var rrect = new RoundedRect(new Rect(0, 0, w, h), NotaRadius.ControlValue);
        ctx.DrawRectangle(Bg, new Pen(Border, 1), rrect);
        double cx = w / 2, px = cx + _pan * (w / 2);
        var fill = _drag || _hover ? FillLive : Fill;
        using (ctx.PushClip(rrect))
        {
            if (px >= cx) ctx.FillRectangle(fill, new Rect(cx, 0, px - cx, h));
            else ctx.FillRectangle(fill, new Rect(px, 0, cx - px, h));
            ctx.DrawLine(new Pen(CentreTick, 1), new Point(cx, 0), new Point(cx, h));
        }
        var text = new FormattedText(Label(_pan), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Mono, 8.5, TextBrush);
        ctx.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
    }

    // Bipolar readout: "C" at centre, else 1..50 with an L/R suffix.
    private static string Label(double p)
    {
        int amt = (int)Math.Round(Math.Abs(p) * 50);
        return amt == 0 ? "C" : (p < 0 ? amt + "L" : amt + "R");
    }
}
