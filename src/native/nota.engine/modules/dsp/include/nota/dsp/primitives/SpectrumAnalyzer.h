// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SpectrumAnalyzer — Hann-windowed log-frequency spectrum analyzer backed by nota::math::RealFft.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/math/Fft.h"

#include <complex>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace nota::dsp {

class NOTA_DSP_API SpectrumAnalyzer {
  public:
    static constexpr int32_t kPoints = 256;
    static constexpr double kLo = 20.0;
    static constexpr double kHi = 20000.0;
    static constexpr float kFloorDb = -150.0f;
    static constexpr double kHalfOct = 1.0 / 6.0;
    static constexpr double kRiseS = 0.020;
    static constexpr double kFallDbPerS = 20.0 / 0.3;
    static constexpr double kPeakHoldS = 1.5;
    static constexpr double kPeakFallDbPerS = kFallDbPerS * 0.5;
    static constexpr double kStalledMinS = 0.03;
    static constexpr double kStalledMaxS = 0.25;
    static constexpr float kPeakSlopeDb = 2.0f;

    static int32_t fftSizeFor(double sampleRate) noexcept { return sampleRate > 50000.0 ? 8192 : 4096; }

    void prepare(double sampleRate);
    void reset();
    void analyse(const float* ring, uint32_t mask, uint32_t w, double dt, bool peakHold);

    const float* curve() const noexcept { return curve_.data(); }
    const float* peak() const noexcept { return peak_.data(); }
    int32_t fftSize() const noexcept { return n_; }
    bool valid() const noexcept { return valid_; }

  private:
    struct Point {
        int32_t first = 0;
        int32_t count = 0;
        int32_t k0 = 1;
        double x = 1.0;
    };

    double sr_ = 44100.0;
    int32_t n_ = 0;
    math::RealFft fft_;
    std::vector<float> win_;
    std::vector<float> time_;
    std::vector<std::complex<float>> freq_;
    std::vector<double> pow_;
    std::vector<Point> pts_;
    std::vector<float> weights_;
    std::vector<float> curve_ = std::vector<float>(static_cast<std::size_t>(kPoints), kFloorDb);
    std::vector<float> peak_ = std::vector<float>(static_cast<std::size_t>(kPoints), kFloorDb);
    std::vector<float> peakAge_ = std::vector<float>(static_cast<std::size_t>(kPoints), 0.0f);
    double ref_ = 1.0;
    double refBin_ = 1.0;
    uint32_t lastW_ = 0;
    double stalled_ = 0.0;
    double gap_ = 0.0;
    bool primed_ = false;
    bool valid_ = false;
};

} // namespace nota::dsp
