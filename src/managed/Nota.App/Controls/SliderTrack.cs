// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The horizontal slider from the almanac's slider row: a 3px track in the well colour, a
// brass fill, and a 6×7 Brass Light handle. Bipolar sliders fill from the centre and show
// a 1px centre mark. An inactive slider (IsDim) loses brass — fill Border strong, handle
// Ink 5 — but keeps its position, so the number next to it stays meaningful. A
// modulation-depth slider (Modulation) draws in teal instead of brass, like the teal
// arc of a modulation knob.
//
// It works in normalised 0..1; the caller maps to the parameter (linear, log, stepped).
// Interaction follows the almanac (§ Interaction contract), with the one exception it allows:
// a slider drags along its own axis (#115) — right increases and the handle follows the
// pointer — and a vertical drag still works as on a knob (up increases, full range over
// ~140px). Shift (or Ctrl/⌘) is fine on both; ew-resize; a click never jumps the value.
// Double-click resets when a reset is supplied; right-click bubbles to the CV-modulate /
// MIDI Learn menu.
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
    private static readonly IBrush Fill = NotaPalette.VizInk;          // at rest: neutral
    private static readonly IBrush FillLive = NotaPalette.Accent;      // under the pointer or in the hand
    private static readonly IBrush FillDim = NotaPalette.Rule;
    private static readonly IBrush Handle = NotaPalette.TextPrimary;
    private static readonly IBrush HandleLive = NotaPalette.AccentBright;
    private static readonly IBrush ModFill = NotaPalette.Teal;
    private static readonly IBrush ModHandle = NotaPalette.TealBright;
    private static readonly IBrush HandleDim = NotaPalette.TextTertiary;
    private static readonly IBrush Centre = NotaPalette.BorderStrong;

    public const double TrackH = 3, HandleW = 6, HandleH = 7, Height0 = 9;

    private double _norm;
    private bool _dim, _drag;
    private double _lastX, _lastY;

    public bool Bipolar { get; init; }

    /// <summary>A modulation depth: teal fill and handle instead of brass.</summary>
    public bool Modulation { get; init; }

    /// <summary>A role chroma for the fill (teal / rose), overriding brass or Modulation; its
    /// light tone draws the handle. May change live.</summary>
    public IBrush? Ink { get => _ink; set { if (ReferenceEquals(_ink, value)) return; _ink = value; InvalidateVisual(); } }
    private IBrush? _ink;

    /// <summary>A 1px teal mark at this 0..1 position (a live level against the value); NaN hides it.</summary>
    public double Marker { get => _marker; set { if (value.Equals(_marker)) return; _marker = value; InvalidateVisual(); } }
    private double _marker = double.NaN;

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
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && Reset is not null)
        {
            if (_drag) { _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); InvalidateVisual(); }
            Reset();
            e.Handled = true;
            return;
        }
        _drag = true;
        var p = e.GetPosition(this);
        _lastX = p.X;
        _lastY = p.Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        var p = e.GetPosition(this);
        double dx = p.X - _lastX, dy = _lastY - p.Y;   // right = increase, up = increase
        _lastX = p.X;
        _lastY = p.Y;
        if (dx == 0 && dy == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double span = Math.Max(Bounds.Width, 24);   // along the slider the handle tracks the pointer
        Nudge((dx / span + dy / 140.0) / (fine ? 10.0 : 1.0));
    }

    // Under the pointer (or in the hand) the control is live and takes the accent; at rest
    // its value is neutral ink — the accent marks a knob or fader only while focused or dragged.
    private bool _hover;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        e.Pointer.Capture(null);
        GestureEnd?.Invoke();
        InvalidateVisual();   // a drag that ended off the control leaves no hover to repaint it
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Never leave an automation session dangling.
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
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
            ctx.DrawRectangle(_dim ? FillDim : _ink ?? (Modulation ? ModFill : _drag || _hover ? FillLive : Fill), null, new RoundedRect(new Rect(a, ty, b - a, TrackH), NotaRadius.ClipValue));
        if (Bipolar)
            ctx.FillRectangle(Centre, new Rect(Math.Round(w / 2) - 0.5, cy - 3, 1, 6));

        if (!double.IsNaN(_marker) && !_dim)
            ctx.FillRectangle(ModHandle, new Rect(Math.Round(Math.Clamp(_marker, 0, 1) * (w - 1)), 0, 1, h));

        double hx = Math.Clamp(x, HandleW / 2, w - HandleW / 2) - HandleW / 2;
        IBrush inkHandle = ReferenceEquals(_ink, NotaPalette.Rose) ? NotaPalette.RoseBright
            : ReferenceEquals(_ink, NotaPalette.Teal) ? ModHandle : _ink ?? (Modulation ? ModHandle : _drag || _hover ? HandleLive : Handle);
        ctx.DrawRectangle(_dim ? HandleDim : inkHandle, null,
            new RoundedRect(new Rect(hx, cy - HandleH / 2, HandleW, HandleH), NotaRadius.ClipValue));
    }
}
