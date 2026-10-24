// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;

namespace Nota.Application.Editing;

public static class PianoRollVelocityWheel
{
    public static int ComputeDelta(int wheelDirection, int baseStep, bool reverseDirection, double multiplier)
    {
        if (wheelDirection == 0 || baseStep <= 0 || !double.IsFinite(multiplier)) return 0;
        double scale = Math.Clamp(multiplier, 0.0, 2.0);
        if (scale <= 0.0) return 0;
        int amount = (int)Math.Round(baseStep * scale, MidpointRounding.AwayFromZero);
        if (amount <= 0) return 0;
        int direction = Math.Sign(wheelDirection) * (reverseDirection ? -1 : 1);
        return direction * amount;
    }
}
