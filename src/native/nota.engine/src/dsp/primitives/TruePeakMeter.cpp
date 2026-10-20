// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TruePeakMeter implementation — coefficients match Ceiling.h's proven table.

#include "dsp/primitives/TruePeakMeter.h"

#include <cmath>

#include "dsp/math/Simd.h"

namespace nota {
namespace dsp {

namespace {

constexpr double kPi = 3.14159265358979323846;

std::array<std::array<float, TruePeakMeter::kTaps>, 3> makeCoefs() {
    std::array<std::array<float, TruePeakMeter::kTaps>, 3> k{};
    for (int p = 0; p < 3; ++p) {
        const double t = (p + 1) * 0.25;
        double sum = 0.0;
        for (int i = 0; i < TruePeakMeter::kTaps; ++i) {
            const double u = i - (TruePeakMeter::kDelay - 1) - t;
            const double sinc = std::fabs(u) < 1e-9 ? 1.0 : std::sin(kPi * u) / (kPi * u);
            const double win = std::fabs(u) < 8.5 ? 0.5 * (1.0 + std::cos(kPi * u / 8.5)) : 0.0;
            k[p][i] = static_cast<float>(sinc * win);
            sum += sinc * win;
        }
        for (auto& v : k[p]) v = static_cast<float>(v / sum);
    }
    return k;
}

} // namespace

const std::array<std::array<float, TruePeakMeter::kTaps>, 3>& TruePeakMeter::coefs() noexcept {
    static const auto c = makeCoefs();
    return c;
}

void TruePeakMeter::reset() noexcept {
    hist_.fill(0.0f);
    w_ = 0;
}

float TruePeakMeter::push(float x) NOTA_RT_SAFE {
    hist_[w_] = x;
    hist_[w_ + 16] = x;
    w_ = (w_ + 1) & 15;
    // Window of the last 16 samples ending at the newest: hist_[w_ .. w_+15].
    const float* win = hist_.data() + w_;
    const auto& c = coefs();
    float pk = win[kDelay - 1] < 0.0f ? -win[kDelay - 1] : win[kDelay - 1];
    for (int p = 0; p < 3; ++p) {
        const float acc = simd::dot16(c[p].data(), win);
        const float a = acc < 0.0f ? -acc : acc;
        if (a > pk) pk = a;
    }
    return pk;
}

float TruePeakMeter::detect(float x) NOTA_RT_SAFE {
    const float a = x < 0.0f ? -x : x;
    const float tp = push(x);
    return a > tp ? a : tp;
}

} // namespace dsp
} // namespace nota
