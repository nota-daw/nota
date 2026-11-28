// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/math/Fft.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <numbers>

namespace nota::math {

void Windowing::fillHann(float* dst, int32_t count, bool periodic) noexcept {
    if (!dst || count <= 0) return;
    if (count == 1) {
        dst[0] = 1.0f;
        return;
    }
    const double denom = static_cast<double>(periodic ? count : count - 1);
    const double twoPi = 2.0 * std::numbers::pi;
    for (int32_t i = 0; i < count; ++i) {
        const double phase = twoPi * static_cast<double>(i) / denom;
        dst[i] = static_cast<float>(0.5 * (1.0 - std::cos(phase)));
    }
}

void Windowing::fillBlackman(float* dst, int32_t count, bool periodic) noexcept {
    if (!dst || count <= 0) return;
    if (count == 1) {
        dst[0] = 1.0f;
        return;
    }
    const double denom = static_cast<double>(periodic ? count : count - 1);
    const double twoPi = 2.0 * std::numbers::pi;
    for (int32_t i = 0; i < count; ++i) {
        const double p = twoPi * static_cast<double>(i) / denom;
        dst[i] = static_cast<float>(0.42 - 0.5 * std::cos(p) + 0.08 * std::cos(2.0 * p));
    }
}

double Windowing::besselI0(double x) noexcept {
    double sum = 1.0;
    double term = 1.0;
    const double halfX = 0.5 * x;
    for (int32_t k = 1; k <= 32; ++k) {
        const double r = halfX / static_cast<double>(k);
        term *= r * r;
        sum += term;
        if (term <= sum * 1.0e-15) break;
    }
    return sum;
}

void Windowing::fillKaiser(float* dst, int32_t count, float beta, bool symmetric) noexcept {
    if (!dst || count <= 0) return;
    if (count == 1) {
        dst[0] = 1.0f;
        return;
    }
    const double denom = 0.5 * static_cast<double>(symmetric ? count - 1 : count);
    const double invI0 = 1.0 / besselI0(static_cast<double>(beta));
    for (int32_t i = 0; i < count; ++i) {
        const double norm = (static_cast<double>(i) - denom) / denom;
        const double arg = std::max(0.0, 1.0 - norm * norm);
        dst[i] = static_cast<float>(besselI0(static_cast<double>(beta) * std::sqrt(arg)) * invI0);
    }
}

void Windowing::fillKaiserSinc(float* dst, int32_t taps, double cutoffRatio, double beta) noexcept {
    if (!dst || taps <= 0) return;
    if (taps == 1) {
        dst[0] = 1.0f;
        return;
    }
    const double mid = 0.5 * static_cast<double>(taps - 1);
    const double invI0 = 1.0 / besselI0(beta);
    const double pi = std::numbers::pi;
    double sum = 0.0;
    for (int32_t i = 0; i < taps; ++i) {
        const double x = static_cast<double>(i) - mid;
        const double norm = x / mid;
        const double win = besselI0(beta * std::sqrt(std::max(0.0, 1.0 - norm * norm))) * invI0;
        const double sinc = (std::abs(x) < 1.0e-12) ? cutoffRatio : std::sin(pi * cutoffRatio * x) / (pi * x);
        const double v = sinc * win;
        dst[i] = static_cast<float>(v);
        sum += v;
    }
    if (std::abs(sum) > 1.0e-15) {
        const double invSum = 1.0 / sum;
        for (int32_t i = 0; i < taps; ++i) dst[i] = static_cast<float>(dst[i] * invSum);
    }
}

} // namespace nota::math