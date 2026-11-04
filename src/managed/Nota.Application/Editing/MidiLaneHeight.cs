// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;

namespace Nota.Application.Editing;

public static class MidiLaneHeight
{
    public const double Minimum = 48, Maximum = 480;
    public static double Normalize(double height, double fallback)
        => double.IsFinite(height) ? Math.Clamp(height, Minimum, Maximum) : fallback;
}
