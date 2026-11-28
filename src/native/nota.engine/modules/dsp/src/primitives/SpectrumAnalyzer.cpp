// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/SpectrumAnalyzer.h"
#include "nota/math/Fft.h"

#include <algorithm>
#include <cmath>
#include <complex>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {
namespace {

constexpr double kPi = 3.14159265358979323846;

} // namespace

void SpectrumAnalyzer::prepare(double sampleRate) {
    sr_ = sampleRate > 0.0 ? sampleRate : 44100.0;
    n_ = fftSizeFor(sr_);
    fft_.resize(n_);
    const std::size_t un = static_cast<std::size_t>(n_);
    win_.assign(un, 0.0f);
    time_.assign(un, 0.0f);
    freq_.assign(un / 2u, {});
    pow_.assign(un / 2u, 0.0);
    math::Windowing::fillHann(win_.data(), n_, true);
    double sum = 0.0;
    for (float w : win_) sum += static_cast<double>(w);
    refBin_ = (sum * 0.5) * (sum * 0.5);
    ref_ = refBin_ * 1.5;

    const int32_t bins = n_ / 2;
    const double binHz = sr_ / static_cast<double>(n_);
    const double edge = std::pow(2.0, kHalfOct);
    pts_.assign(static_cast<std::size_t>(kPoints), {});
    weights_.clear();
    for (int32_t i = 0; i < kPoints; ++i) {
        const double f = kLo * std::pow(kHi / kLo, static_cast<double>(i) / static_cast<double>(kPoints - 1));
        Point& p = pts_[static_cast<std::size_t>(i)];
        p.x = std::clamp(f / binHz, 1.0, static_cast<double>(bins) - 2.0);
        const int32_t k0 = std::clamp(static_cast<int32_t>(std::ceil(f / edge / binHz)), 1, bins - 1);
        const int32_t k1 = std::clamp(static_cast<int32_t>(std::floor(f * edge / binHz)), 1, bins - 1);
        p.k0 = k0;
        p.first = static_cast<int32_t>(weights_.size());
        if (k1 - k0 < 2) continue;
        for (int32_t k = k0; k <= k1; ++k) {
            const double d = std::log2(static_cast<double>(k) * binHz / f) / kHalfOct;
            weights_.push_back(static_cast<float>(0.5 + 0.5 * std::cos(kPi * std::clamp(d, -1.0, 1.0))));
        }
        p.count = k1 - k0 + 1;
    }
    reset();
}

void SpectrumAnalyzer::reset() {
    std::fill(curve_.begin(), curve_.end(), kFloorDb);
    std::fill(peak_.begin(), peak_.end(), kFloorDb);
    std::fill(peakAge_.begin(), peakAge_.end(), 0.0f);
    primed_ = false;
    valid_ = false;
    stalled_ = 0.0;
    gap_ = 0.0;
}

void SpectrumAnalyzer::analyse(const float* ring, uint32_t mask, uint32_t w, double dt, bool peakHold) {
    if (!ring || n_ <= 0 || mask + 1u < static_cast<uint32_t>(n_)) return;
    const bool snap = !primed_ || dt <= 0.0 || dt > 1.2;
    const double elapsed = std::max(0.0, dt);
    dt = snap ? 0.0 : dt;
    if (primed_ && w == lastW_) stalled_ += elapsed;
    else {
        if (primed_) gap_ = std::min(std::max(stalled_ + elapsed, 0.9 * gap_), kStalledMaxS);
        stalled_ = 0.0;
    }
    const bool silent = stalled_ > std::clamp(1.5 * gap_, kStalledMinS, kStalledMaxS);
    if (!silent && w >= static_cast<uint32_t>(n_)) {
        const uint32_t start = w - static_cast<uint32_t>(n_);
        for (int32_t i = 0; i < n_; ++i) {
            const std::size_t idx = static_cast<std::size_t>(i);
            time_[idx] = ring[(start + static_cast<uint32_t>(i)) & mask] * win_[idx];
        }
        fft_.forwardPacked(time_.data(), freq_.data());
        for (std::size_t k = 0; k < pow_.size(); ++k) pow_[k] = static_cast<double>(std::norm(freq_[k]));
    } else {
        std::fill(pow_.begin(), pow_.end(), 0.0);
    }
    lastW_ = w;
    primed_ = true;

    const double rise = snap ? 1.0 : 1.0 - std::exp(-dt / kRiseS);
    const double fall = kFallDbPerS * dt;
    const double peakFall = kPeakFallDbPerS * dt;
    bool any = false;
    for (int32_t i = 0; i < kPoints; ++i) {
        const Point& p = pts_[static_cast<std::size_t>(i)];
        double e = 0.0;
        double ref = ref_;
        if (p.count > 0) {
            const float* wt = weights_.data() + p.first;
            for (int32_t j = 0; j < p.count; ++j) {
                e += static_cast<double>(wt[j]) * pow_[static_cast<std::size_t>(p.k0 + j)];
            }
        } else {
            const int32_t ka = static_cast<int32_t>(p.x);
            const double fr = p.x - static_cast<double>(ka);
            e = pow_[static_cast<std::size_t>(ka)] * (1.0 - fr) + pow_[static_cast<std::size_t>(ka + 1)] * fr;
            ref = refBin_;
        }
        const double t = e > ref * 1e-15 ? std::max(static_cast<double>(kFloorDb), 10.0 * std::log10(e / ref))
                                         : static_cast<double>(kFloorDb);
        float& c = curve_[static_cast<std::size_t>(i)];
        if (snap) c = static_cast<float>(t);
        else if (t >= static_cast<double>(c))
            c = static_cast<float>(static_cast<double>(c) + rise * (t - static_cast<double>(c)));
        else
            c = static_cast<float>(std::max(t, static_cast<double>(c) - fall));
        if (c > kFloorDb + 1.0f) any = true;

        float& pk = peak_[static_cast<std::size_t>(i)];
        float& age = peakAge_[static_cast<std::size_t>(i)];
        if (!peakHold || snap || c >= pk) {
            pk = c;
            age = 0.0f;
        } else {
            age += static_cast<float>(dt);
            if (static_cast<double>(age) > kPeakHoldS) {
                pk = static_cast<float>(std::max(static_cast<double>(c), static_cast<double>(pk) - peakFall));
            }
        }
        if (peakHold && pk > kFloorDb + 1.0f) any = true;
    }
    if (peakHold) {
        for (int32_t i = 1; i < kPoints; ++i) {
            const std::size_t idx = static_cast<std::size_t>(i);
            peak_[idx] = std::max(peak_[idx], peak_[idx - 1u] - kPeakSlopeDb);
        }
        for (int32_t i = kPoints - 2; i >= 0; --i) {
            const std::size_t idx = static_cast<std::size_t>(i);
            peak_[idx] = std::max(peak_[idx], peak_[idx + 1u] - kPeakSlopeDb);
        }
    }
    valid_ = any;
}

} // namespace nota::dsp
