// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// A thin horizontal volume fader matching the arrangement track-header mockup
// (HANDOFF 1b): 3px track, filled portion, and an 8×9 draggable cap. Avalonia's
// stock Slider is too chunky for the 64px row, so this is a purpose-built control.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class MiniFader : Control
{
    private static readonly IBrush Track = NotaPalette.BgSunken;  // Brush.BgSunken
    private static readonly IBrush Fill = NotaPalette.BorderStrong;   // Brush.BorderStrong
    private static readonly IBrush Cap = NotaPalette.TextSecondary;    // Brush.TextSecondary
    private static readonly IBrush FillAccent = NotaPalette.Accent; // Brush.Accent (brass)
    private static readonly IBrush CapAccent = NotaPalette.AccentBright;  // Brush.AccentBright

    /// <summary>Brass fill + cap instead of the neutral grey (used by the synth editor).</summary>
    public bool Accent { get; init; }
    /// <summary>Double-click target value, or negative to disable reset.</summary>
    public double Default { get; init; } = -1;

    private readonly double _max;
    private double _value;
    private bool _drag;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;   // pointer down (M9-C automation write)
    public event Action? GestureEnd;     // pointer up

    /// <summary>True while the user is dragging — callers skip external value writes then.</summary>
    public bool Dragging => _drag;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, _max); InvalidateVisual(); }
    }

    public MiniFader(double value = 1.0, double max = 1.5)
    {
        _max = max;
        _value = Math.Clamp(value, 0, max);
        Height = 12;
        MinWidth = 40;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Double-click resets to the default value (e.g. 0 dB for a track volume).
        if (e.ClickCount == 2 && Default >= 0)
        {
            double d = Math.Clamp(Default, 0, _max);
            if (Math.Abs(d - _value) > 1e-6) { _value = d; InvalidateVisual(); ValueChanged?.Invoke(_value); }
            e.Handled = true;
            return;
        }
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
        double v = Math.Clamp(x / w, 0, 1) * _max;
        if (Math.Abs(v - _value) < 1e-4) return;
        _value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0) return;
        double cy = h / 2;
        double frac = _max > 0 ? _value / _max : 0;
        double fx = frac * w;

        ctx.DrawRectangle(Track, null, new Rect(0, cy - 1.5, w, 3), 2, 2);
        if (fx > 0) ctx.DrawRectangle(Accent ? FillAccent : Fill, null, new Rect(0, cy - 1.5, fx, 3), 2, 2);
        ctx.DrawRectangle(Accent ? CapAccent : Cap, null, new Rect(Math.Clamp(fx - 4, 0, w - 8), cy - 4.5, 8, 9), 2, 2);
    }
}
