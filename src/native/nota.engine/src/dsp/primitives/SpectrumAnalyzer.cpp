// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "dsp/primitives/SpectrumAnalyzer.h"

#include <algorithm>
#include <cmath>

namespace nota::dsp {
namespace {

constexpr double kPi = 3.14159265358979323846;

} // namespace

void SpectrumAnalyzer::prepare(double sampleRate) {
    sr_ = sampleRate > 0 ? sampleRate : 44100.0;
    n_ = fftSizeFor(sr_);
    fft_.resize((size_t)n_);
    win_.assign((size_t)n_, 0.0f);
    time_.assign((size_t)n_, 0.0f);
    freq_.assign((size_t)n_ / 2, {});
    pow_.assign((size_t)n_ / 2, 0.0);
    double sum = 0.0;
    for (int i = 0; i < n_; ++i) {
        win_[(size_t)i] = (float)(0.5 - 0.5 * std::cos(2.0 * kPi * i / n_));
        sum += win_[(size_t)i];
    }
    // A full-scale sine: its peak bin's power is (Σw / 2)², and the Hann main lobe (the bins
    // either side at half the amplitude) holds 1.5 times that.
    refBin_ = (sum * 0.5) * (sum * 0.5);
    ref_ = refBin_ * 1.5;

    const int bins = n_ / 2;
    const double binHz = sr_ / n_;
    const double edge = std::pow(2.0, kHalfOct);
    pts_.assign(kPoints, {});
    weights_.clear();
    for (int i = 0; i < kPoints; ++i) {
        const double f = kLo * std::pow(kHi / kLo, i / (double)(kPoints - 1));
        Point& p = pts_[(size_t)i];
        p.x = std::clamp(f / binHz, 1.0, bins - 2.0);
        const int k0 = std::clamp((int)std::ceil(f / edge / binHz), 1, bins - 1);
        const int k1 = std::clamp((int)std::floor(f * edge / binHz), 1, bins - 1);
        p.k0 = k0;
        p.first = (int)weights_.size();
        // Fewer than three bins under the window can't resolve a lobe: interpolate instead.
        if (k1 - k0 < 2) continue;
        for (int k = k0; k <= k1; ++k) {
            const double d = std::log2(k * binHz / f) / kHalfOct; // −1 … 1 across the window
            weights_.push_back((float)(0.5 + 0.5 * std::cos(kPi * std::clamp(d, -1.0, 1.0))));
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
    if (!ring || n_ <= 0 || mask + 1 < (uint32_t)n_) return;
    const bool snap = !primed_ || dt <= 0.0 || dt > 1.2;
    const double elapsed = std::max(0.0, dt); // a long gap still counts as a pause
    dt = snap ? 0.0 : dt;
    // No new samples for longer than a callback: the input has stopped, so let the curve fall.
    // A shorter pause is a read between two callbacks: the same window measures again.
    if (primed_ && w == lastW_) stalled_ += elapsed;
    else {
        if (primed_) gap_ = stalled_ + elapsed;
        stalled_ = 0.0;
    }
    const bool silent = stalled_ > std::clamp(1.5 * gap_, kStalledMinS, kStalledMaxS);
    if (!silent && w >= (uint32_t)n_) {
        const uint32_t start = w - (uint32_t)n_;
        for (int i = 0; i < n_; ++i) time_[(size_t)i] = ring[(start + (uint32_t)i) & mask] * win_[(size_t)i];
        fft_.fft(time_.data(), freq_.data());
        for (size_t k = 0; k < pow_.size(); ++k) pow_[k] = std::norm(freq_[k]);
    } else {
        std::fill(pow_.begin(), pow_.end(), 0.0);
    }
    lastW_ = w;
    primed_ = true;

    const double rise = snap ? 1.0 : 1.0 - std::exp(-dt / kRiseS);
    const double fall = kFallDbPerS * dt, peakFall = kPeakFallDbPerS * dt;
    bool any = false;
    for (int i = 0; i < kPoints; ++i) {
        const Point& p = pts_[(size_t)i];
        double e = 0.0, ref = ref_;
        if (p.count > 0) {
            const float* wt = weights_.data() + p.first;
            for (int j = 0; j < p.count; ++j) e += wt[j] * pow_[(size_t)(p.k0 + j)];
        } else {
            const int ka = (int)p.x;
            const double fr = p.x - ka;
            e = pow_[(size_t)ka] * (1.0 - fr) + pow_[(size_t)ka + 1] * fr;
            ref = refBin_;
        }
        const double t = e > ref * 1e-15 ? std::max((double)kFloorDb, 10.0 * std::log10(e / ref)) : kFloorDb;
        float& c = curve_[(size_t)i];
        if (snap) c = (float)t;
        else if (t >= c) c = (float)(c + rise * (t - c));
        else c = (float)std::max(t, c - fall);
        if (c > kFloorDb + 1.0f) any = true;

        float& pk = peak_[(size_t)i];
        float& age = peakAge_[(size_t)i];
        if (!peakHold || snap || c >= pk) {
            pk = c;
            age = 0.0f;
        } else {
            age += (float)dt;
            if (age > kPeakHoldS) pk = (float)std::max((double)c, pk - peakFall);
        }
        if (peakHold && pk > kFloorDb + 1.0f) any = true; // a held peak still shows after the curve has gone
    }
    if (peakHold) { // keep the held line a line: limit its slope between neighbouring points
        for (int i = 1; i < kPoints; ++i) peak_[(size_t)i] = std::max(peak_[(size_t)i], peak_[(size_t)i - 1] - kPeakSlopeDb);
        for (int i = kPoints - 2; i >= 0; --i) peak_[(size_t)i] = std::max(peak_[(size_t)i], peak_[(size_t)i + 1] - kPeakSlopeDb);
    }
    valid_ = any;
}

} // namespace nota::dsp
