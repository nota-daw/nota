// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

public enum TempoInterpolation
{
    Linear = 0,
    Square = 1,
    Triangle = 2,
    Smooth = 3
}

public readonly record struct TempoPoint(double Beat, double Bpm, TempoInterpolation Interpolation = TempoInterpolation.Linear);
