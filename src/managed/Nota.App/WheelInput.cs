// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Cross-device mouse-wheel normalization. Avalonia hands us a wheel Delta that a mouse
// reports as roughly ±1 per notch, while a macOS trackpad reports many events per gesture
// with much larger (pixel-scale) deltas plus a momentum tail. Our custom scroll/zoom
// handlers used a fixed step per event (e.g. "±2 beats", "×1.25 zoom"), so on a trackpad
// every flick summed to a huge jump and flew straight to the extremes. This maps any delta
// to a consistent on-screen pixel amount and caps it per event, so mouse and trackpad both
// feel proportional and can't teleport.

using System;

namespace Nota.App;

internal static class WheelInput
{
    // A mouse notch (|delta| ≈ 1) should move about this many pixels.
    private const double NotchPixels = 50.0;
    // At/above this magnitude the delta is already pixel-scale (a trackpad), so it's used
    // as-is rather than multiplied up like a discrete notch.
    private const double NotchThreshold = 4.0;
    // Ceiling per event so a single fast/momentum event can't leap across the whole view.
    private const double MaxPixelsPerEvent = 90.0;
    // Scrolling feels a touch better slightly faster than 1:1 finger travel; zoom keeps 1:1.
    private const double ScrollGain = 1.5;

    // Device-normalized, capped base pixels for one wheel-delta component.
    private static double BasePixels(double delta)
    {
        double px = Math.Abs(delta) >= NotchThreshold ? delta : delta * NotchPixels;
        return Math.Clamp(px, -MaxPixelsPerEvent, MaxPixelsPerEvent);
    }

    /// <summary>On-screen pixels to scroll for one wheel-delta component (device-normalized).</summary>
    public static double Pixels(double delta) => BasePixels(delta) * ScrollGain;

    /// <summary>Normalized notch count for stepping zoom (mouse notch ≈ 1, capped for trackpad).</summary>
    public static double Notches(double delta) => BasePixels(delta) / NotchPixels;

    /// <summary>Per-event zoom multiplier: <paramref name="perNotch"/> applied ^notches, so a
    /// mouse notch gives exactly <paramref name="perNotch"/> and a trackpad scales smoothly.</summary>
    public static double ZoomFactor(double delta, double perNotch)
        => Math.Pow(perNotch, Notches(delta));
}
