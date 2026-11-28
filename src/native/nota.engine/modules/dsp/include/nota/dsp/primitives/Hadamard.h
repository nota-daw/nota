// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hadamard — unitary 4-point and 8-point Fast Walsh-Hadamard Transforms.

#pragma once

#include "nota/dsp/rt/RtAttributes.h"

namespace nota::dsp {

NOTA_ALWAYS_INLINE void hadamard4(float* x) NOTA_RT_SAFE {
    const float a0 = x[0] + x[1];
    const float a1 = x[0] - x[1];
    const float a2 = x[2] + x[3];
    const float a3 = x[2] - x[3];
    x[0] = (a0 + a2) * 0.5f;
    x[1] = (a1 + a3) * 0.5f;
    x[2] = (a0 - a2) * 0.5f;
    x[3] = (a1 - a3) * 0.5f;
}

NOTA_ALWAYS_INLINE void hadamard8(float* x) NOTA_RT_SAFE {
    const float a0 = x[0] + x[1];
    const float a1 = x[0] - x[1];
    const float a2 = x[2] + x[3];
    const float a3 = x[2] - x[3];
    const float a4 = x[4] + x[5];
    const float a5 = x[4] - x[5];
    const float a6 = x[6] + x[7];
    const float a7 = x[6] - x[7];
    const float b0 = a0 + a2;
    const float b1 = a1 + a3;
    const float b2 = a0 - a2;
    const float b3 = a1 - a3;
    const float b4 = a4 + a6;
    const float b5 = a5 + a7;
    const float b6 = a4 - a6;
    const float b7 = a5 - a7;
    constexpr float s = 0.35355339f;
    x[0] = (b0 + b4) * s;
    x[1] = (b1 + b5) * s;
    x[2] = (b2 + b6) * s;
    x[3] = (b3 + b7) * s;
    x[4] = (b0 - b4) * s;
    x[5] = (b1 - b5) * s;
    x[6] = (b2 - b6) * s;
    x[7] = (b3 - b7) * s;
}

} // namespace nota::dsp
