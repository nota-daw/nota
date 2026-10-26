// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the upright faders and level pills the device cards draw themselves
// (Auto Filter, Compressor, Delay, Reverb, Utility, Valve, Vintage, Auto Gain, Lens, Shutter,
// Crush, Ceiling, Beat Repeat). Each keeps its own gesture (absolute or relative drag, wheel,
// double-click reset) and is drawn as the style guide's upright value bar (UprightBar): 16
// wide, corner 4, in a groove; the soft orange tint fills up to the value and a 3px orange
// line marks it, deepening while dragged. A level pill (ShPill) draws in its own ink.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Draws an upright value bar the way SliderTrack does (Vertical), for the device
/// faders that keep their own gestures.</summary>
internal static class UprightBar
{
    private static readonly IBrush Fill = NotaPalette.AccentSoft;
    private static readonly IBrush FillDrag = NotaPalette.Derived(() => SliderTrack.Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.45));
    private static readonly IBrush Centre = NotaPalette.BorderStrong;

    /// <param name="norm">The value, 0..1 from the bottom (from the top with <paramref name="fromTop"/>).</param>
    /// <param name="live">Where modulation has the value now (a dashed line), or NaN.</param>
    /// <param name="marker">A live level the value works against (a 1px tick), or NaN.</param>
    /// <param name="tick">A threshold (a dashed amber line), or NaN.</param>
    /// <param name="ink">Recolours the fill and line (a level, not a value).</param>
    public static void Draw(DrawingContext ctx, Control owner, double norm, bool drag, double live = double.NaN,
        double marker = double.NaN, double tick = double.NaN, bool fromTop = false, bool bipolar = false, bool dim = false, IBrush? ink = null)
    {
        double w = owner.Bounds.Width, h = owner.Bounds.Height;
        if (w < 2 || h < 4) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));   // the whole box hit-tests
        double bw = Math.Min(SliderTrack.Height0, w), tx = (w - bw) / 2;
        var bar = new RoundedRect(new Rect(tx, 0, bw, h), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, owner, bar);
        norm = Math.Clamp(norm, 0, 1);
        double y = fromTop ? norm * h : (1 - norm) * h;
        double a = bipolar ? Math.Min(h / 2, y) : fromTop ? 0 : y, b = bipolar ? Math.Max(h / 2, y) : fromTop ? y : h;
        IBrush fill = dim ? NotaPalette.Well : ink is SolidColorBrush s ? NotaPalette.Wash(s, 0x48) : drag ? FillDrag : Fill;
        IBrush line = dim ? NotaPalette.TextDisabled : drag ? NotaPalette.Accent : ink ?? NotaPalette.Accent;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5) ctx.FillRectangle(fill, new Rect(tx, a, bw, b - a));
            if (bipolar) ctx.FillRectangle(Centre, new Rect(tx + 3, Math.Round(h / 2) - 0.5, bw - 6, 1));
            if (!double.IsNaN(marker) && !dim)
                ctx.FillRectangle(NotaPalette.TextSecondary, new Rect(tx, Math.Round((1 - Math.Clamp(marker, 0, 1)) * (h - 1)), bw, 1));
            if (!double.IsNaN(live) && Math.Abs(live - norm) > 0.005)
            {
                double ly = Math.Clamp((1 - Math.Clamp(live, 0, 1)) * h, 1, h - 1);
                ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1, new DashStyle(new double[] { 2, 1 }, 0)), new Point(tx + 2, ly), new Point(tx + bw - 2, ly));
            }
            if (!double.IsNaN(tick))
            {
                double ty = (1 - Math.Clamp(tick, 0, 1)) * h;
                ctx.DrawLine(new Pen(NotaPalette.Threshold, 1, new DashStyle(new double[] { 2, 2 }, 0)), new Point(tx, ty), new Point(tx + bw, ty));
            }
            if (ink is null || norm > 0.004)
            {
                double ly = Math.Clamp(y, SliderTrack.LineW / 2, h - SliderTrack.LineW / 2) - SliderTrack.LineW / 2;
                ctx.FillRectangle(line, new Rect(tx, ly, bw, SliderTrack.LineW));
            }
        }
    }
}

internal sealed class AfFader : Control
{
    private double _v, _live = double.NaN;
    private bool _drag;

    public IBrush Ink { get; init; } = NotaPalette.VizInk;
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;

    public event Action<double>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public AfFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    /// <summary>The set value, and where modulation has it now (NaN = no marker).</summary>
    public void Set(double v, double live)
    {
        if (!_drag) _v = Math.Clamp(v, 0, 1);
        _live = double.IsNaN(live) ? live : Math.Clamp(live, 0, 1);
        InvalidateVisual();
    }

    private void Apply(PointerEventArgs e)
    {
        double h = Bounds.Height; if (h <= 2) return;
        _v = Math.Clamp(1 - e.GetPosition(this).Y / h, 0, 1);
        Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); _v = Default; Changed?.Invoke(_v); GestureEnd?.Invoke();
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; GestureBegin?.Invoke(); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; InvalidateVisual(); e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
        => UprightBar.Draw(ctx, this, _v, _drag, live: _live);
}

// A vertical fader in the LEVEL column: a groove, a wash fill in its chroma up to the value
// and a bright handle, plus a hairline tick at the live level it works against (the input
// for the threshold). Drag moves the value relative to where it was — a click never jumps it;
// Shift / Ctrl / ⌘ drag fine; double-click restores the default.
internal sealed class CompFader : Control
{
    private double _v, _live = double.NaN, _startV, _startY;
    private bool _drag;

    public IBrush Ink { get; set; } = NotaPalette.VizInk;      // resting fill: neutral ink
    public IBrush Handle { get; set; } = NotaPalette.VizInk;   // turns accent while dragged
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;

    public event Action<double>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public CompFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double v, double live = double.NaN)
    {
        double n = Math.Clamp(v, 0, 1);
        bool dirty = !_drag && Math.Abs(n - _v) > 1e-4;
        if (!_drag) _v = n;
        if (double.IsNaN(live) != double.IsNaN(_live) || Math.Abs(live - _live) > 1e-3) { _live = live; dirty = true; }
        if (dirty) InvalidateVisual();
    }

    public void Recolor(IBrush ink, IBrush handle) { Ink = ink; Handle = handle; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); _v = Default; Changed?.Invoke(_v); GestureEnd?.Invoke();
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; _startV = _v; _startY = e.GetPosition(this).Y;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        double h = Math.Max(20, Bounds.Height);
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double v = Math.Clamp(_startV + (_startY - e.GetPosition(this).Y) / h * (fine ? 0.2 : 1.0), 0, 1);
        if (Math.Abs(v - _v) < 1e-6) return;
        _v = v; Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; InvalidateVisual(); e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
        => UprightBar.Draw(ctx, this, _v, _drag, marker: _live > 0.001 ? _live : double.NaN);
}

// A vertical fader in the LOOP column: a groove, a wash fill in the parameter's own chroma
// and a bright handle. Drag up to raise, double-click restores the default.
internal sealed class DelayFader : Control
{
    private double _v;
    private bool _drag;

    /// <summary>The fill chroma — neutral ink for feedback, rose for spread. The handle turns
    /// accent while dragged.</summary>
    public IBrush Ink { get; init; } = NotaPalette.VizInk;
    public IBrush Handle { get; init; } = NotaPalette.VizInk;
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;

    public event Action<double>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public DelayFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double v)
    {
        if (_drag) return;
        double n = Math.Clamp(v, 0, 1);
        if (Math.Abs(n - _v) < 1e-4) return;
        _v = n; InvalidateVisual();
    }

    private void Apply(PointerEventArgs e)
    {
        double h = Bounds.Height; if (h <= 2) return;
        _v = Math.Clamp(1 - e.GetPosition(this).Y / h, 0, 1);
        Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); _v = Default; Changed?.Invoke(_v); GestureEnd?.Invoke();
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; GestureBegin?.Invoke(); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; InvalidateVisual(); e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
        => UprightBar.Draw(ctx, this, _v, _drag);
}

internal sealed class LvGainFader : Control
{
    private double _db, _max = 12;
    private bool _manual, _on = true, _drag;
    private double _y0, _v0;

    /// <summary>The manual gain (dB) at the start of a drag.</summary>
    public Func<double>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double>? Changed;   // new gain in dB
    public event Action? ResetRequested;
    public bool Dragging => _drag;

    public LvGainFader() { Width = 14; }

    public void Set(double db, double maxDb, bool manual, bool on)
    {
        _db = db; _max = Math.Max(0.01, maxDb); _manual = manual; _on = on;
        Cursor = manual ? new Cursor(StandardCursorType.SizeNorthSouth) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_manual || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { ResetRequested?.Invoke(); e.Handled = true; return; }
        _v0 = Value?.Invoke() ?? _db; _y0 = e.GetPosition(this).Y; _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        // Full range (−max … +max) over ~140 px, fine steps with a modifier.
        double d = (_y0 - e.GetPosition(this).Y) / 140.0 * 2 * _max * (fine ? 0.1 : 1);
        Changed?.Invoke(Math.Round(Math.Clamp(_v0 + d, -_max, _max) * 10) / 10);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; InvalidateVisual(); e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        // A bipolar bar: it fills from 0 dB toward the gain, in ink 4 while off and in the
        // tertiary ink when the gain is automatic rather than set by hand.
        double n = 0.5 + Math.Clamp(_db / _max, -1, 1) / 2;
        UprightBar.Draw(ctx, this, n, _drag, bipolar: true, dim: !_on,
            ink: _manual ? null : NotaPalette.TextTertiary);
    }
}

/// <summary>The thin vertical slider of the Lens Scale rail: a sunken groove with a
/// filled column and a bright cap (accent while dragged). Drag or wheel; double-click resets.</summary>
internal sealed class LensVSlider : Control
{
    private readonly SolidColorBrush _ink;
    private double _value;
    private bool _drag;
    private double _lastY;

    public double Default { get; set; } = -1;
    public bool Dragging => _drag;
    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public double Value { get => _value; set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); } }

    public LensVSlider(double value, SolidColorBrush ink)
    {
        _value = Math.Clamp(value, 0, 1);
        _ink = ink;
        Width = 16;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && Default >= 0) { Value = Default; ValueChanged?.Invoke(_value); e.Handled = true; return; }
        _drag = true; _lastY = e.GetPosition(this).Y;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y;
        Value = _value + (_lastY - y) / Math.Max(24, Bounds.Height);
        _lastY = y;
        ValueChanged?.Invoke(_value);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); e.Pointer.Capture(null); }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!WheelInput.IsVertical(e)) return;
        Value = _value + WheelInput.Notches(e.Delta.Y) * 0.02;
        ValueChanged?.Invoke(_value);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
        => UprightBar.Draw(ctx, this, _value, _drag);
}

internal sealed class ShPill : Control
{
    private double _v, _tick = double.NaN;
    public bool FromTop { get; init; }
    public IBrush Ink { get; init; } = NotaPalette.VizInk;

    public ShPill() { Width = 16; }

    /// <summary>The fill 0..1 and the threshold tick 0..1 (NaN = none).</summary>
    public void Set(double v, double tick)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - _v) < 1e-3 && (tick.Equals(_tick) || Math.Abs(tick - _tick) < 1e-3)) return;
        _v = v; _tick = tick; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
        => UprightBar.Draw(ctx, this, _v, false, fromTop: FromTop, ink: Ink, tick: _tick);
}
