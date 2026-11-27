// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/rt/ExpressionTypes.h"
#include <cstdint>

namespace nota::dsp {

void GlobalExpr::offsets(float bendRangeSemis, float out[kExprDims]) const noexcept {
    out[ExprBend] = wheel * bendRangeSemis;
    out[ExprPressure] = pressure;
    out[ExprSlide] = slide - 0.5f;
}

void VoiceExpr::set(int32_t dim, float v, const float add[kExprDims]) noexcept {
    if (dim < 0 || dim >= kExprDims) return;
    tgt[dim] = v;
    if (fresh) cur[dim] = v + add[dim];
}

} // namespace nota::dsp
