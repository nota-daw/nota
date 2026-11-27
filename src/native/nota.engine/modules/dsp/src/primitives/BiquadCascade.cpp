// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/BiquadCascade.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <algorithm>
#include <cstdint>

namespace nota::dsp {

void BiquadCascade::processBlock(float* buf, int32_t frames, BiquadChannel ch) NOTA_RT_SAFE {
    if (buf == nullptr || frames <= 0 || stages_ <= 0) return;
    if (ch == BiquadChannel::Stereo) {
        int32_t i = 0;
        while (i < frames) {
            const int32_t m = std::min<int32_t>(kBlock, frames - i);
            for (int32_t k = 0; k < m; ++k) {
                const int32_t o = (i + k) * 2;
                buf[o] = static_cast<float>(runBank(0, static_cast<double>(buf[o])));
                buf[o + 1] = static_cast<float>(runBank(1, static_cast<double>(buf[o + 1])));
            }
            i += m;
        }
        return;
    }
    int32_t i = 0;
    while (i < frames) {
        const int32_t m = std::min<int32_t>(kBlock, frames - i);
        for (int32_t k = 0; k < m; ++k) {
            const int32_t o = (i + k) * 2;
            const float a = buf[o];
            const float b = buf[o + 1];
            if (ch == BiquadChannel::Mid || ch == BiquadChannel::Side) {
                const float mid = (a + b) * 0.5f;
                const float side = (a - b) * 0.5f;
                if (ch == BiquadChannel::Mid) {
                    const float f = static_cast<float>(runBank(0, static_cast<double>(mid)));
                    buf[o] = f + side;
                    buf[o + 1] = f - side;
                } else {
                    const float f = static_cast<float>(runBank(0, static_cast<double>(side)));
                    buf[o] = mid + f;
                    buf[o + 1] = mid - f;
                }
            } else if (ch == BiquadChannel::Left) {
                buf[o] = static_cast<float>(runBank(0, static_cast<double>(a)));
            } else {
                buf[o + 1] = static_cast<float>(runBank(1, static_cast<double>(b)));
            }
        }
        i += m;
    }
}

} // namespace nota::dsp
