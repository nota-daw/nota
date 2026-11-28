// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/TruePeakMeter.h"

#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {
namespace {

constexpr double kPi = 3.14159265358979323846;

std::array<std::array<float, TruePeakMeter::kTaps>, 3> makeCoefs() noexcept {
    std::array<std::array<float, TruePeakMeter::kTaps>, 3> k{};
    for (std::size_t p = 0; p < 3u; ++p) {
        const double t = static_cast<double>(p + 1u) * 0.25;
        double sum = 0.0;
        for (int32_t i = 0; i < TruePeakMeter::kTaps; ++i) {
            const double u = static_cast<double>(i - (TruePeakMeter::kDelay - 1)) - t;
            const double sinc = std::fabs(u) < 1e-9 ? 1.0 : std::sin(kPi * u) / (kPi * u);
            const double win = std::fabs(u) < 8.5 ? 0.5 * (1.0 + std::cos(kPi * u / 8.5)) : 0.0;
            k[p][static_cast<std::size_t>(i)] = static_cast<float>(sinc * win);
            sum += sinc * win;
        }
        for (auto& v : k[p]) v = static_cast<float>(static_cast<double>(v) / sum);
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

} // namespace nota::dsp
