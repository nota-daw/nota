// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shift + wheel over a note in the piano roll steps its velocity (specs/063): one MIDI step
// per wheel notch at 1×, scaled by the user's multiplier (0–2×) and flipped by the direction
// setting. A trackpad sends fractions of a notch; they carry over, so a slow swipe still steps.

using System;

namespace Nota.Application.Editing;

public sealed class VelocityWheel
{
    public const double MaxMultiplier = 2.0;
    // One event never moves more than this many notches (a trackpad's momentum burst).
    private const double MaxNotchesPerEvent = 2.0;

    private double _carry;

    public static double ClampMultiplier(double m) => double.IsFinite(m) ? Math.Clamp(m, 0, MaxMultiplier) : 1.0;

    /// <summary>Whether the gesture does anything: a 0× multiplier turns it off.</summary>
    public static bool Enabled(double multiplier) => ClampMultiplier(multiplier) > 0;

    /// <summary>Whole velocity steps (1/127) for a wheel move of <paramref name="notches"/>
    /// (+ up); the fraction left over waits for the next move.</summary>
    public int Steps(double notches, bool reversed, double multiplier)
    {
        if (!double.IsFinite(notches)) return 0;
        double n = Math.Clamp(notches, -MaxNotchesPerEvent, MaxNotchesPerEvent);
        _carry += n * ClampMultiplier(multiplier) * (reversed ? -1 : 1);
        int steps = (int)Math.Truncate(_carry + Math.Sign(_carry) * 1e-9);
        _carry -= steps;
        return steps;
    }

    /// <summary>Drops the carried fraction: a new burst starts from a whole step.</summary>
    public void Reset() => _carry = 0;
}
