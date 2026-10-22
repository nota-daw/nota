// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Thin vertical fader (0..1.5) with drag + automation-write gestures.
/// Extracted from MixerView (R2-3); self-contained.</summary>
internal sealed class VFader : Control
{
    private const double CapH = 14, Max = 1.5;
    private static readonly IBrush Track = NotaPalette.BgSunken;
    private static readonly IBrush Cap = NotaPalette.SurfaceHover;
    private static readonly IPen CapBorder = new Pen(NotaPalette.TextDisabled, 1);
    private readonly IBrush _capLine;
    private double _value;
    private bool _drag;
    public event Action<double>? ValueChanged;
    public bool Dragging => _drag;
    public double Value => _value;
    public event Action? GestureBegin;   // M9-C automation write
    public event Action? GestureEnd;

    public VFader(double value, IBrush capLine) { _value = Math.Clamp(value, 0, Max); _capLine = capLine; Width = 30; }

    /// <summary>Sets the value without raising ValueChanged (external sync, e.g. dB field).</summary>
    public void SetValueExternal(double v) { _value = Math.Clamp(v, 0, Max); InvalidateVisual(); }

    /// <summary>The fader as a typeable value; <paramref name="fmt"/> is its read-out at a value
    /// (0..1.5), <paramref name="def"/> the value a double-click restores.</summary>
    internal ValueField EntryField(string name, Func<double, string> fmt, double? def)
        => new(name, () => _value / Max, n => fmt(n * Max), n => Set(n * Max),
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), def is { } d ? Math.Clamp(d, 0, Max) / Max : null);

    private void Set(double v)
    {
        v = Math.Clamp(v, 0, Max);
        if (Math.Abs(v - _value) < 1e-9) return;
        _value = v; InvalidateVisual(); ValueChanged?.Invoke(v);
    }

    // Relative, like every value control (DESIGN.md § Interaction contract): a click never
    // jumps the value — so the first click of a double-click (reset) moves nothing.
    private double _startY, _startV;
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _drag = true; _startY = e.GetPosition(this).Y; _startV = _value;
        e.Pointer.Capture(this); GestureBegin?.Invoke(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double h = Bounds.Height - CapH; if (h <= 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        Set(_startV + (_startY - e.GetPosition(this).Y) / h * Max / (fine ? 10 : 1));
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_drag) GestureEnd?.Invoke(); _drag = false; e.Pointer.Capture(null); }

    // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); } base.OnPointerCaptureLost(e); }


    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, cx = w / 2;
        ctx.DrawRectangle(Track, null, new Rect(cx - 2, 0, 4, h), 2, 2);
        double frac = _value / Max;
        double capY = (h - CapH) * (1 - frac);
        var cap = new Rect(cx - 13, capY, 26, CapH);
        ctx.DrawRectangle(Cap, CapBorder, cap, 4, 4);
        ctx.FillRectangle(_capLine, new Rect(cx - 9, capY + 5, 18, 2));
    }
}
