// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "NoteExpression.h"

namespace nota {

void GlobalExpr::offsets(float bendRangeSemis, float out[kExprDims]) const {
    out[ExprBend] = wheel * bendRangeSemis;
    out[ExprPressure] = pressure;
    out[ExprSlide] = slide - 0.5f;
}

void VoiceExpr::set(int32_t dim, float v, const float add[kExprDims]) {
    if (dim < 0 || dim >= kExprDims) return;
    tgt[dim] = v;
    if (fresh) cur[dim] = v + add[dim];
}

void VoiceExpr::tick(const float add[kExprDims], float coef) {
    fresh = false;
    for (int d = 0; d < kExprDims; ++d) cur[d] += (tgt[d] + add[d] - cur[d]) * coef;
}

} // namespace nota
