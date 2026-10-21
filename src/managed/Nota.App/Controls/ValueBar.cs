// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A draggable value box: a rounded field showing the
// value text centered over a horizontal fill that tracks the normalized value. Drag
// vertically to change (up = increase); double-click types a value, ⌥-click resets to the default. Fires
// GestureBegin/End so callers can bracket an automation write, like the gauge Knob.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class ValueBar : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush Border = NotaPalette.Rule;
    private static readonly IBrush FillAccent = NotaPalette.Teal;                          // modulation
    private static readonly IBrush FillBrass = NotaPalette.Wash(NotaPalette.VizInk, 0x40); // at rest: neutral
    private static readonly IBrush FillLive = NotaPalette.Wash(NotaPalette.Accent, 0x8C);  // live
    private static readonly IBrush TextBrush = NotaPalette.TextPrimary;
    private static readonly Typeface Mono = NotaFonts.Mono;

    private double _value;                       // normalized 0..1
    private readonly Func<double, string> _fmt;  // formats the normalized value for display
    private bool _drag;
    private double _lastY;

    /// <summary>Teal fill (modulation-ish) instead of the brass default.</summary>
    public bool Accent { get; init; }
    /// <summary>Value change per pixel of vertical drag.</summary>
    public double Sensitivity { get; init; } = 0.006;
    /// <summary>⌥-click target (normalized), or negative to disable reset.</summary>
    public double Default { get; init; } = -1;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    /// <summary>True while the user is dragging — callers skip live-follow writes then.</summary>
    public bool Dragging => _drag;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public ValueBar(double value, Func<double, string> fmt)
    {
        _value = Math.Clamp(value, 0, 1);
        _fmt = fmt;
        ValueEntry.Attach(this, new ValueField("Value", () => _value, fmt, Apply, () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(),
            Default >= 0 ? Default : null));
        Height = 18;
        MinWidth = 46;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0 && Default >= 0) { GestureBegin?.Invoke(); Apply(Default); GestureEnd?.Invoke(); e.Handled = true; return; }
        _drag = true;
        _lastY = e.GetPosition(this).Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y;
        double dv = (_lastY - y) * Sensitivity;   // drag up → increase
        _lastY = y;
        if (dv != 0) Apply(_value + dv);
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

    private void Apply(double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - _value) < 1e-5) return;
        _value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var rrect = new RoundedRect(new Rect(0, 0, w, h), NotaRadius.ControlValue);
        ctx.DrawRectangle(Bg, new Pen(Border, 1), rrect);
        double fw = _value * w;
        if (fw > 1)
        {
            using (ctx.PushClip(new RoundedRect(new Rect(0, 0, fw, h), NotaRadius.ControlValue)))
                ctx.DrawRectangle(Accent ? FillAccent : _drag || _hover ? FillLive : FillBrass, null, new Rect(0, 0, w, h), NotaRadius.ControlValue, NotaRadius.ControlValue);
        }
        var text = new FormattedText(_fmt(_value), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Mono, 9.5, TextBrush);
        ctx.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
    }
}
