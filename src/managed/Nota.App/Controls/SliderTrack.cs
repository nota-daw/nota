// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The value bar, the slider inside devices (style guide, spec 038): 16 high, corner 4, in a
// groove; the soft orange tint fills up to the value and a 3px orange line marks it. While
// dragged the fill deepens toward orange. Bipolar bars fill from the centre, with a 1px
// centre mark. An inactive bar (IsDim) keeps its position in ink 4, so the number next to
// it stays meaningful. A role chroma (Ink) recolours the line.
//
// It works in normalised 0..1; the caller maps to the parameter (linear, log, stepped).
// Interaction follows the almanac (§ Interaction contract), with the one exception it allows:
// a slider drags along its own axis (#115) — right increases and the handle follows the
// pointer — and a vertical drag still works as on a knob (up increases, full range over
// ~140px). Shift (or Ctrl/⌘) is fine on both; ew-resize; a click never jumps the value.
// Double-click resets when a reset is supplied; ⌥-click types a value (ValueEntry, attached
// by the row); right-click opens the parameter menu.
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
    private static readonly IBrush Fill = NotaPalette.AccentSoft;
    private static readonly IBrush FillDrag = NotaPalette.Derived(() => Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.45));
    private static readonly IBrush Line = NotaPalette.Accent;
    private static readonly IBrush FillDim = NotaPalette.Well;
    private static readonly IBrush LineDim = NotaPalette.TextDisabled;
    private static readonly IBrush Mark = NotaPalette.TextSecondary;   // a live level against the value
    private static readonly IBrush Centre = NotaPalette.BorderStrong;

    public const double LineW = 3, Height0 = 16;

    private static Color Mix(Color a, Color b, double t)
        => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private double _norm;
    private bool _dim, _drag;
    private double _lastX, _lastY;

    public bool Bipolar { get; init; }

    /// <summary>A modulation depth. Drawn like any value bar: the guide keeps one orange for
    /// every value inside a device.</summary>
    public bool Modulation { get; init; }

    /// <summary>A role chroma for the value line (rose for a destructive amount), overriding
    /// orange. May change live.</summary>
    public IBrush? Ink { get => _ink; set { if (ReferenceEquals(_ink, value)) return; _ink = value; InvalidateVisual(); } }
    private IBrush? _ink;

    /// <summary>A 1px mark at this 0..1 position (a live level against the value); NaN hides it.</summary>
    public double Marker { get => _marker; set { if (value.Equals(_marker)) return; _marker = value; InvalidateVisual(); } }
    private double _marker = double.NaN;

    /// <summary>Double-click handler (restore the default). Null disables reset. ⌥-click types a
    /// value once the row attaches ValueEntry (DeviceCardKit.SliderRow does).</summary>
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
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && Reset is not null)
        {
            GestureBegin?.Invoke();
            Reset();
            GestureEnd?.Invoke();
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
        double bh = Math.Min(Height0, h), ty = (h - bh) / 2;
        var bar = new RoundedRect(new Rect(0, ty, w, bh), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, this, bar);

        double x = _norm * w;
        double a = Bipolar ? Math.Min(w / 2, x) : 0, b = Bipolar ? Math.Max(w / 2, x) : x;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5) ctx.FillRectangle(_dim ? FillDim : _drag ? FillDrag : Fill, new Rect(a, ty, b - a, bh));
            if (Bipolar) ctx.FillRectangle(Centre, new Rect(Math.Round(w / 2) - 0.5, ty + 3, 1, bh - 6));
            if (!double.IsNaN(_marker) && !_dim)
                ctx.FillRectangle(Mark, new Rect(Math.Round(Math.Clamp(_marker, 0, 1) * (w - 1)), ty, 1, bh));
            double lx = Math.Clamp(x, LineW / 2, w - LineW / 2) - LineW / 2;
            ctx.FillRectangle(_dim ? LineDim : _ink ?? Line, new Rect(lx, ty, LineW, bh));
        }
    }
}
