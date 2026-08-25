// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Radial pan knob (−1..1), vertical-drag; raises PanChanged + write gestures.
/// Extracted from MixerView (R2-3); self-contained.</summary>
internal sealed class PanKnob : Control
{
    private static readonly IBrush Face = NotaPalette.BgSunken;
    private static readonly IPen Ring = new Pen(NotaPalette.BorderStrong, 1.5);
    private static readonly IPen Ind = new Pen(NotaPalette.TextSecondary, 2) { LineCap = PenLineCap.Round };
    private double _pan;
    private bool _drag;
    private double _startY, _startPan;
    public event Action<double>? PanChanged;
    public event Action? GestureBegin;   // M9-C automation write
    public event Action? GestureEnd;

    public PanKnob(double pan) { _pan = Math.Clamp(pan, -1, 1); Width = 26; Height = 26; }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { _drag = true; _startY = e.GetPosition(this).Y; _startPan = _pan; e.Pointer.Capture(this); GestureBegin?.Invoke(); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double dp = (_startY - e.GetPosition(this).Y) / 60.0;   // drag up → pan right
        _pan = Math.Clamp(_startPan + dp, -1, 1);
        InvalidateVisual(); PanChanged?.Invoke(_pan);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_drag) GestureEnd?.Invoke(); _drag = false; e.Pointer.Capture(null); }

    public override void Render(DrawingContext ctx)
    {
        double cx = Bounds.Width / 2, cy = Bounds.Height / 2, r = 11;
        ctx.DrawEllipse(Face, Ring, new Point(cx, cy), r, r);
        double ang = _pan * 140 * Math.PI / 180.0;   // ±140° sweep
        var tip = new Point(cx + Math.Sin(ang) * (r - 2), cy - Math.Cos(ang) * (r - 2));
        ctx.DrawLine(Ind, new Point(cx, cy), tip);
    }
}
