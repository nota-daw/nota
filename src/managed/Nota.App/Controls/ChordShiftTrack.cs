// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Nota Chord shift (almanac mockups 1a / 1b): a bipolar −12..+12 semitone track centred
// on zero. Horizontal (L): a 3px well groove with a centre tick, a brass fill from the
// centre and a 6×9 handle. Vertical (S): a framed well with a centre rule and a bar
// growing up (positive) or down (negative). Unlike the generic SliderTrack the value is
// positional — press or drag anywhere sets the semitone under the pointer — because the
// track is a pitch ruler. A switched-off slot draws its fill in Border strong.
// GestureBegin / GestureEnd bracket the drag for automation writes; double-click resets
// to 0; right-click bubbles to MIDI Learn.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ChordShiftTrack : Control
{
    private static readonly IBrush Groove = NotaPalette.BgSunken;
    private static readonly IBrush Frame = NotaPalette.GraphBorder;
    private static readonly IBrush Fill = NotaPalette.Accent;
    private static readonly IBrush FillOff = NotaPalette.BorderStrong;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush Centre = NotaPalette.BorderStrong;
    private static readonly IBrush CentreV = NotaPalette.BorderDefault;

    public const int Range = 12;

    private int _value;
    private bool _on = true, _drag;

    public bool Vertical { get; init; }
    public bool Dragging => _drag;

    public int Value { get => _value; set { var v = Math.Clamp(value, -Range, Range); if (v == _value) return; _value = v; InvalidateVisual(); } }
    public bool On { get => _on; set { if (_on == value) return; _on = value; InvalidateVisual(); } }

    public event Action<int>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Cursor = new Cursor(Vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
    }

    private int At(Point p)
    {
        double f = Vertical ? 1 - p.Y / Math.Max(1, Bounds.Height) : p.X / Math.Max(1, Bounds.Width);
        return (int)Math.Round(Math.Clamp(f, 0, 1) * 2 * Range - Range);
    }

    private void Set(Point p)
    {
        int v = At(p);
        if (v == _value && _on) return;
        _value = v; InvalidateVisual();
        Changed?.Invoke(v);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2) { _value = 0; InvalidateVisual(); GestureBegin?.Invoke(); Changed?.Invoke(0); GestureEnd?.Invoke(); return; }
        _drag = true;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        Set(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag) Set(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        e.Pointer.Capture(null);
        GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // hit area
        double f = (_value + Range) / (2.0 * Range);
        var fill = _on ? Fill : FillOff;
        if (Vertical)
        {
            ctx.DrawRectangle(Groove, new Pen(Frame, 1), new Rect(0.5, 0.5, w - 1, h - 1), 3, 3);
            double mid = Math.Round(h / 2);
            ctx.DrawRectangle(CentreV, null, new Rect(1, mid - 0.5, w - 2, 1));
            double bh = Math.Max(Math.Abs(f - 0.5) * (h - 2), 1);
            double top = f >= 0.5 ? mid - bh : mid;
            ctx.DrawRectangle(fill, null, new Rect(3, top, Math.Max(1, w - 6), bh), 1, 1);
        }
        else
        {
            double cy = Math.Round(h / 2), x = f * w, c = w / 2;
            ctx.DrawRectangle(Groove, null, new Rect(0, cy - 1.5, w, 3), 2, 2);
            ctx.DrawRectangle(Centre, null, new Rect(Math.Round(c) - 0.5, cy - 4.5, 1, 9));
            ctx.DrawRectangle(fill, null, new Rect(Math.Min(c, x), cy - 1.5, Math.Abs(x - c), 3), 2, 2);
            ctx.DrawRectangle(Handle, null, new Rect(Math.Clamp(x - 3, 0, w - 6), cy - 4.5, 6, 9), 2, 2);
        }
    }
}
