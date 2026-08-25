// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Horizontal stereo peak meter (mockup 2e output rail): two thin bars (L over R),
// each a green level fill with a yellow peak-hold cap on a dark inset track. Feed it
// linear peak amplitudes per block via Set(); it maps to dB and decays the peak hold.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class StereoMeter : Control
{
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#58B368"));
    private static readonly IBrush Peak = new SolidColorBrush(Color.Parse("#D9C34C"));
    private float _l, _r, _pkL, _pkR;

    private static float Norm(float peak) => peak > 1e-4f ? (float)Math.Clamp((20 * Math.Log10(peak) + 60) / 60.0, 0, 1) : 0f;

    // Push the block's linear peak amplitudes (0..1+). Green tracks the level, the
    // yellow cap holds the recent maximum and decays toward it.
    public void Set(float peakL, float peakR)
    {
        _l = Norm(peakL); _r = Norm(peakR);
        _pkL = Math.Max(_pkL * 0.94f, _l);
        _pkR = Math.Max(_pkR * 0.94f, _r);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        double barH = Math.Min(4, (h - 2) / 2), gap = h - barH * 2;
        void Bar(double y, float lvl, float pk)
        {
            ctx.DrawRectangle(Inset, null, new Rect(0, y, w, barH), 2, 2);
            if (lvl > 0.001f) ctx.DrawRectangle(Green, null, new Rect(0, y, w * lvl, barH), 2, 2);
            if (pk > 0.02f) { double px = Math.Clamp(w * pk - 2, 0, w - 2); ctx.FillRectangle(Peak, new Rect(px, y, 2, barH)); }
        }
        Bar(0, _l, _pkL);
        Bar(barH + gap, _r, _pkR);
    }
}
