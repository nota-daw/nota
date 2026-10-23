// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DelayLine implementation.

#include "dsp/primitives/DelayLine.h"

#include <algorithm>

namespace nota {
namespace dsp {

void DelayLine::prepare(int32_t maxDelaySamples) {
    int32_t n = 16;
    while (n <= maxDelaySamples + 8) n <<= 1;
    buf_.assign(static_cast<size_t>(n), 0.0f);
    size_ = n;
    mask_ = n - 1;
    pos_ = 0;
    apPrev_ = 0.0f;
}

void DelayLine::reset() noexcept {
    if (!buf_.empty()) std::fill(buf_.begin(), buf_.end(), 0.0f);
    pos_ = 0;
    apPrev_ = 0.0f;
}

} // namespace dsp
} // namespace nota
