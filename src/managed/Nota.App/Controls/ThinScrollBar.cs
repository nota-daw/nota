// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The arrangement's horizontal scrollbar: a 5px pill on a bare track, no arrows. It
// keeps the ScrollBar members the arrangement drives (Maximum / ViewportSize / Value and
// a Scroll event raised for user moves only), so the timeline treats it as a drop-in.
// Drag the pill to scroll; press the track elsewhere to centre the pill there and keep
// dragging. Colour is the only state: Border Strong at rest, Ink 5 under the pointer or
// while dragging.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ThinScrollBar : Control
{
    private static readonly IBrush ThumbRest = NotaPalette.BorderStrong;
    private static readonly IBrush ThumbHot = NotaPalette.TextTertiary;

    private const double PadX = 4;       // inset of the track from both ends
    private const double ThumbH = 5;
    private const double MinThumbW = 24;

    private double _max, _viewport, _value;
    private bool _hot, _dragging;
    private double _grabDx;              // pointer offset inside the pill at press

    /// <summary>Raised when the user moves the pill (not for programmatic Value changes).</summary>
    public event EventHandler? Scroll;

    public double Maximum
    {
        get => _max;
        set { _max = Math.Max(0, value); _value = Math.Clamp(_value, 0, _max); InvalidateVisual(); }
    }

    public double ViewportSize
    {
        get => _viewport;
        set { _viewport = Math.Max(0, value); InvalidateVisual(); }
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, _max); InvalidateVisual(); }
    }

    public ThinScrollBar()
    {
        Height = 12;
        ClipToBounds = true;
    }

    private (double x, double w) Thumb()
    {
        double track = Math.Max(0, Bounds.Width - 2 * PadX);
        double total = _max + _viewport;
        double w = total > 0 ? track * _viewport / total : track;
        w = Math.Clamp(w, Math.Min(MinThumbW, track), track);
        double x = PadX + (_max > 0 ? (track - w) * _value / _max : 0);
        return (x, w);
    }

    private void MoveTo(double thumbX)
    {
        double track = Math.Max(0, Bounds.Width - 2 * PadX);
        var (_, w) = Thumb();
        double room = track - w;
        double v = room > 0 ? (thumbX - PadX) / room * _max : 0;
        v = Math.Clamp(v, 0, _max);
        if (Math.Abs(v - _value) < 1e-9) return;
        _value = v;
        InvalidateVisual();
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _max <= 0) return;
        double px = e.GetPosition(this).X;
        var (x, w) = Thumb();
        if (px < x || px > x + w)
        {
            // Off the pill: jump so the pill centres on the press, then drag from its middle.
            MoveTo(px - w / 2);
            (x, w) = Thumb();
        }
        _grabDx = px - x;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        double px = e.GetPosition(this).X;
        if (_dragging) { MoveTo(px - _grabDx); return; }
        var (x, w) = Thumb();
        bool hot = px >= x && px <= x + w;
        if (hot != _hot) { _hot = hot; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_hot) { _hot = false; InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // the whole track takes presses
        if (Bounds.Width <= 2 * PadX) return;
        var (x, w) = Thumb();
        double y = Math.Round((Bounds.Height - ThumbH) / 2);
        ctx.DrawRectangle(_hot || _dragging ? ThumbHot : ThumbRest, null,
            new RoundedRect(new Rect(x, y, w, ThumbH), ThumbH / 2));
    }
}
