// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The horizontal slider from the almanac's slider row: a 3px track in the well colour, a
// brass fill, and a 6×7 Brass Light handle. Bipolar sliders fill from the centre and show
// a 1px centre mark. An inactive slider (IsDim) loses brass — fill Border strong, handle
// Ink 5 — but keeps its position, so the number next to it stays meaningful.
//
// It works in normalised 0..1; the caller maps to the parameter (linear, log, stepped).
// Interaction follows the almanac (§ States · cursors): ns-resize, a vertical drag — up
// increases, full range over ~140px, Shift (or Ctrl/⌘) for fine steps — so a slider and a
// knob handle the same; a click never jumps the value. Double-click resets when a reset is
// supplied; right-click bubbles to the CV-modulate / MIDI Learn menu.
// GestureBegin / GestureEnd bracket the drag so automation writes are recorded, and the
// row's live-follow refresher should skip while Dragging.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SliderTrack : Control
{
    private static readonly IBrush Groove = NotaPalette.BgSunken;
    private static readonly IBrush Fill = NotaPalette.Accent;
    private static readonly IBrush FillDim = NotaPalette.BorderStrong;
    private static readonly IBrush Handle = NotaPalette.AccentBright;
    private static readonly IBrush HandleDim = NotaPalette.TextTertiary;
    private static readonly IBrush Centre = NotaPalette.BorderStrong;

    public const double TrackH = 3, HandleW = 6, HandleH = 7, Height0 = 9;

    private double _norm;
    private bool _dim, _drag;
    private double _lastY;

    public bool Bipolar { get; init; }

    /// <summary>Double-click handler (restore the default). Null disables double-click.</summary>
    public Action? Reset { get; init; }

    public event Action<double>? Changed;   // new normalised value, from the pointer
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public bool Dragging => _drag;

    public double Norm { get => _norm; set { var v = Math.Clamp(value, 0, 1); if (v == _norm) return; _norm = v; InvalidateVisual(); } }
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }

    public SliderTrack()
    {
        Height = Height0;
        MinWidth = 24;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && Reset is not null)
        {
            if (_drag) { _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); }
            Reset();
            e.Handled = true;
            return;
        }
        _drag = true;
        _lastY = e.GetPosition(this).Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y, dy = _lastY - y;   // up = increase
        _lastY = y;
        if (dy == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        Nudge(dy / (fine ? 1400.0 : 140.0));
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
        // Never leave an automation session dangling.
        if (_drag) { _drag = false; GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    private void Nudge(double delta)
    {
        double v = Math.Clamp(_norm + delta, 0, 1);
        if (Math.Abs(v - _norm) < 1e-6) return;
        _norm = v;
        InvalidateVisual();
        Changed?.Invoke(v);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));   // whole row height hit-tests
        double cy = h / 2, ty = cy - TrackH / 2;
        ctx.DrawRectangle(Groove, null, new RoundedRect(new Rect(0, ty, w, TrackH), NotaRadius.ClipValue));

        double x = _norm * w;
        double a = Bipolar ? Math.Min(w / 2, x) : 0, b = Bipolar ? Math.Max(w / 2, x) : x;
        if (b - a > 0.5)
            ctx.DrawRectangle(_dim ? FillDim : Fill, null, new RoundedRect(new Rect(a, ty, b - a, TrackH), NotaRadius.ClipValue));
        if (Bipolar)
            ctx.FillRectangle(Centre, new Rect(Math.Round(w / 2) - 0.5, cy - 3, 1, 6));

        double hx = Math.Clamp(x, HandleW / 2, w - HandleW / 2) - HandleW / 2;
        ctx.DrawRectangle(_dim ? HandleDim : Handle, null,
            new RoundedRect(new Rect(hx, cy - HandleH / 2, HandleW, HandleH), NotaRadius.ClipValue));
    }
}
