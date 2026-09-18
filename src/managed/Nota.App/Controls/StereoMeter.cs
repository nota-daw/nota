// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Horizontal stereo meter for a device card (output rail, card header): two thin bars,
// L over R, on the same scale and zones as MeterBar. No animation — the level is set at
// once and the peak mark holds until the meter is clicked (almanac § level meters).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class StereoMeter : Control
{
    private double _l, _r, _pkL, _pkR;

    public StereoMeter() => ToolTip.SetTip(this, "Clear the peak hold");

    // Push the block's linear peak amplitudes (0..1+).
    public void Set(float peakL, float peakR)
    {
        _l = MeterScale.Norm(peakL); _r = MeterScale.Norm(peakR);
        _pkL = Math.Max(_pkL, _l);
        _pkR = Math.Max(_pkR, _r);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pkL = _l; _pkR = _r; InvalidateVisual(); e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        double barH = Math.Min(4, (h - 2) / 2), gap = h - barH * 2;
        MeterScale.DrawChannel(ctx, new Rect(0, 0, w, barH), _l, _pkL, horizontal: true);
        MeterScale.DrawChannel(ctx, new Rect(0, barH + gap, w, barH), _r, _pkR, horizontal: true);
    }
}
