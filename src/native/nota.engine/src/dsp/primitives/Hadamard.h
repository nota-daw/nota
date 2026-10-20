// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hadamard — branchless unrolled Fast Walsh-Hadamard Transforms (
// ). Energy-preserving (unitary): H4 scales by 1/2, H8 by 1/sqrt(8),
// matching Chamber's diffuser normalization. Shared by Chamber, PhysicalSynth
// and Reverb. Header-only: small inline math (per FR-002).

#pragma once

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

// In-place 4-point FWHT, normalized by 1/2 (unitary).
NOTA_ALWAYS_INLINE void hadamard4(float* x) NOTA_RT_SAFE {
    const float a0 = x[0] + x[1], a1 = x[0] - x[1];
    const float a2 = x[2] + x[3], a3 = x[2] - x[3];
    x[0] = (a0 + a2) * 0.5f;
    x[1] = (a1 + a3) * 0.5f;
    x[2] = (a0 - a2) * 0.5f;
    x[3] = (a1 - a3) * 0.5f;
}

// In-place 8-point FWHT, normalized by 1/sqrt(8) (unitary, matches Chamber).
NOTA_ALWAYS_INLINE void hadamard8(float* x) NOTA_RT_SAFE {
    float a0 = x[0] + x[1], a1 = x[0] - x[1];
    float a2 = x[2] + x[3], a3 = x[2] - x[3];
    float a4 = x[4] + x[5], a5 = x[4] - x[5];
    float a6 = x[6] + x[7], a7 = x[6] - x[7];
    float b0 = a0 + a2, b1 = a1 + a3, b2 = a0 - a2, b3 = a1 - a3;
    float b4 = a4 + a6, b5 = a5 + a7, b6 = a4 - a6, b7 = a5 - a7;
    constexpr float s = 0.35355339f; // 1/sqrt(8)
    x[0] = (b0 + b4) * s;
    x[1] = (b1 + b5) * s;
    x[2] = (b2 + b6) * s;
    x[3] = (b3 + b7) * s;
    x[4] = (b0 - b4) * s;
    x[5] = (b1 - b5) * s;
    x[6] = (b2 - b6) * s;
    x[7] = (b3 - b7) * s;
}

} // namespace dsp
} // namespace nota
