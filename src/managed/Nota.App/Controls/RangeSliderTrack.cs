// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A two-ended value bar in the SliderTrack look (style guide): a 16-high groove, the soft
// orange fill between the ends and a 3px orange line at each end. Works in
// normalised 0..1 (the caller maps to its units and keeps a minimum gap). A press picks the
// nearer handle and a horizontal drag moves it by the pointer's travel — a click never jumps
// a value; Shift (or Ctrl/⌘) drags fine. Double-click resets. GestureBegin / GestureEnd
// (with the handle: 0 = low, 1 = high) bracket the drag for automation writes.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class RangeSliderTrack : Control
{
    private static readonly IBrush Fill = NotaPalette.AccentSoft;
    private static readonly IBrush Line = NotaPalette.Accent;
    private static readonly IBrush LineHot = NotaPalette.AccentBright;   // an end under the pointer or in the hand

    private const double LineW = SliderTrack.LineW;

    private double _lo, _hi = 1, _lastX;
    private int _drag = -1, _hover = -1;

    public double Lo { get => _lo; set { value = Math.Clamp(value, 0, 1); if (value == _lo) return; _lo = value; InvalidateVisual(); } }
    public double Hi { get => _hi; set { value = Math.Clamp(value, 0, 1); if (value == _hi) return; _hi = value; InvalidateVisual(); } }

    /// <summary>Double-click handler (restore the defaults). Null disables double-click.</summary>
    public Action? Reset { get; init; }

    /// <summary>A handle moved: (handle 0 = low / 1 = high, its proposed 0..1 position).</summary>
    public event Action<int, double>? Changed;
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;

    public bool Dragging => _drag >= 0;

    public RangeSliderTrack()
    {
        Height = SliderTrack.Height0;
        MinWidth = 40;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    private int Nearer(double x)
    {
        double w = Math.Max(1, Bounds.Width), dl = Math.Abs(x - _lo * w), dh = Math.Abs(x - _hi * w);
        return dl < dh || (dl == dh && x < _lo * w) ? 0 : 1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && Reset is not null)
        {
            Reset();
            e.Handled = true;
            return;
        }
        _lastX = e.GetPosition(this).X;
        _drag = Nearer(_lastX);
        e.Pointer.Capture(this);
        GestureBegin?.Invoke(_drag);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        double x = e.GetPosition(this).X;
        if (_drag < 0)
        {
            int h = IsPointerOver ? Nearer(x) : -1;
            if (h != _hover) { _hover = h; InvalidateVisual(); }
            return;
        }
        double dx = x - _lastX;
        _lastX = x;
        if (dx == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double cur = _drag == 0 ? _lo : _hi;
        double v = Math.Clamp(cur + dx / Math.Max(1, Bounds.Width) * (fine ? 0.1 : 1), 0, 1);
        if (Math.Abs(v - cur) < 1e-9) return;
        Changed?.Invoke(_drag, v);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover != -1) { _hover = -1; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        int d = _drag; _drag = -1;
        e.Pointer.Capture(null);
        GestureEnd?.Invoke(d);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag >= 0) { int d = _drag; _drag = -1; InvalidateVisual(); GestureEnd?.Invoke(d); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        double bh = Math.Min(SliderTrack.Height0, h), ty = (h - bh) / 2;
        var bar = new RoundedRect(new Rect(0, ty, w, bh), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, this, bar);
        double a = _lo * w, b = _hi * w;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5) ctx.FillRectangle(Fill, new Rect(a, ty, b - a, bh));
            for (int k = 0; k < 2; k++)
            {
                double x = k == 0 ? a : b;
                double lx = Math.Clamp(x, LineW / 2, w - LineW / 2) - LineW / 2;
                bool hot = _drag == k || _hover == k;
                ctx.FillRectangle(hot ? LineHot : Line, new Rect(lx, ty, LineW, bh));
            }
        }
    }
}
