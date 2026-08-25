// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

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
    public event Action? GestureBegin;   // M9-C automation write
    public event Action? GestureEnd;

    public VFader(double value, Color capLine) { _value = Math.Clamp(value, 0, Max); _capLine = new SolidColorBrush(capLine); Width = 30; }

    /// <summary>Sets the value without raising ValueChanged (external sync, e.g. dB field).</summary>
    public void SetValueExternal(double v) { _value = Math.Clamp(v, 0, Max); InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { _drag = true; e.Pointer.Capture(this); GestureBegin?.Invoke(); SetY(e.GetPosition(this).Y); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag) SetY(e.GetPosition(this).Y); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_drag) GestureEnd?.Invoke(); _drag = false; e.Pointer.Capture(null); }

    private void SetY(double y)
    {
        double h = Bounds.Height; if (h <= CapH) return;
        double frac = Math.Clamp(1 - (y - CapH / 2) / (h - CapH), 0, 1);
        double v = frac * Max;
        if (Math.Abs(v - _value) < 1e-4) return;
        _value = v; InvalidateVisual(); ValueChanged?.Invoke(v);
    }

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
