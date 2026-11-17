// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The shape of a curved automation / envelope / controller segment (#591), mirroring native
// AutomationLane::shape. A point's Curve (−1..1) bends the segment to the next point:
// expm1(k·t)/expm1(k) with k = −Curve·Steepness — 0 is a straight line, >0 eases out, <0 eases
// in, and the two mirror each other. Both ends have a finite slope, so no curve jumps.
// The family is shift-invariant: any piece [t0, t1] of a segment is the same shape with
// Curve·(t1 − t0), so cutting a segment never moves its values.

using System;

namespace Nota.Application.Editing;

public static class AutomationCurve
{
    /// <summary>Steepness of Curve ±1. Matches native <c>AutomationLane::kCurveSteepness</c>.</summary>
    public const double Steepness = 8.0;

    /// <summary>The shaped 0..1 fraction at <paramref name="t"/> (0..1) of a segment.</summary>
    public static double Shape(double t, float curve)
    {
        double k = -curve * Steepness;
        if (Math.Abs(k) < 1e-6) return t;
        return double.ExpM1(k * t) / double.ExpM1(k);
    }

    /// <summary>The value at <paramref name="t"/> (0..1) between <paramref name="v0"/> and <paramref name="v1"/>.</summary>
    public static double Lerp(double v0, double v1, double t, float curve) => v0 + (v1 - v0) * Shape(t, curve);

    /// <summary>The curve whose segment passes the middle at <paramref name="frac"/> of the way
    /// (the bend handle): Shape(½) = 1/(e^{k/2}+1), so k = 2·ln(1/frac − 1).</summary>
    public static float FromMidpoint(double frac)
    {
        frac = Math.Clamp(frac, 1e-4, 1 - 1e-4);
        double k = 2.0 * Math.Log(1.0 / frac - 1.0);
        return (float)Math.Clamp(-k / Steepness, -1.0, 1.0);
    }

    /// <summary>The curve of a piece spanning <paramref name="fraction"/> (0..1) of a segment.
    /// Matches native <c>AutomationLane::subCurve</c>.</summary>
    public static float Sub(float curve, double fraction) => (float)(curve * Math.Clamp(fraction, 0.0, 1.0));

    /// <summary>Polyline steps for drawing a curved segment <paramref name="pixels"/> wide:
    /// about one every 3 px, so a steep end stays smooth at any zoom.</summary>
    public static int DrawSteps(double pixels) => Math.Clamp((int)Math.Ceiling(Math.Abs(pixels) / 3.0), 8, 256);
}
