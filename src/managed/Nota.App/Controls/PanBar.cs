// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// A compact bipolar pan bar for the arrangement track header: a
// rounded field with a centre tick and a brass fill that grows from the centre
// toward the panned side, "50L … C … 50R" text over it. Drag horizontally to set;
// double-click resets to centre. Fires GestureBegin/End so callers can bracket an
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
    private static readonly IBrush Border = NotaPalette.BorderDefault;
    private static readonly IBrush Fill = NotaPalette.Accent;         // brass
    private static readonly IBrush CentreTick = NotaPalette.BorderStrong;
    private static readonly IBrush TextBrush = NotaPalette.TextSecondary;
    private static readonly Typeface Mono = new("monospace");

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
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2) { Apply(0); e.Handled = true; return; }   // reset to centre
        _drag = true;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        SetFromX(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag) SetFromX(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
    }

    private void SetFromX(double x)
    {
        double w = Bounds.Width;
        if (w <= 0) return;
        Apply(Math.Clamp(x / w, 0, 1) * 2 - 1);
    }

    private void Apply(double p)
    {
        p = Math.Clamp(p, -1, 1);
        if (Math.Abs(p) < 0.04) p = 0;   // small centre detent
        if (Math.Abs(p - _pan) < 1e-4) return;
        _pan = p;
        InvalidateVisual();
        PanChanged?.Invoke(_pan);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var rrect = new RoundedRect(new Rect(0, 0, w, h), 4);
        ctx.DrawRectangle(Bg, new Pen(Border, 1), rrect);
        double cx = w / 2, px = cx + _pan * (w / 2);
        using (ctx.PushClip(rrect))
        {
            if (px >= cx) ctx.FillRectangle(Fill, new Rect(cx, 0, px - cx, h));
            else ctx.FillRectangle(Fill, new Rect(px, 0, cx - px, h));
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
