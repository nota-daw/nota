// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Convolution — zero-latency uniformly-partitioned FFT convolution (64-tap FIR head + FFT stages).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/math/ComplexOps.h"
#include "nota/math/Export.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace nota::dsp {

inline void accumulatePackedSpectrum(float* NOTA_RESTRICT ar, float* NOTA_RESTRICT ai, const float* NOTA_RESTRICT xr,
    const float* NOTA_RESTRICT xi, const float* NOTA_RESTRICT hr, const float* NOTA_RESTRICT hi,
    int32_t bins) noexcept {
    math::accumulatePackedSpectrum(ar, ai, xr, xi, hr, hi, bins);
}

class NOTA_DSP_API ConvKernel {
  public:
    struct Path {
        int32_t in = 0;
        int32_t out = 0;
        int32_t k = 0;
    };

    double sampleRate = 0.0;
    int32_t latency = 0;
    int32_t block = 0;
    float seconds = 0.0f;

    [[nodiscard]] static std::unique_ptr<ConvKernel> build(const std::vector<std::vector<float>>& h,
        const std::vector<Path>& paths, int32_t nIn, bool monoIn, bool zeroLatency, int32_t bigB, double sr);

    void process(const float* inL, const float* inR, float* outL, float* outR, int32_t n);
    void clear();

    bool compatible(const ConvKernel& o) const;
    void transplant(const ConvKernel& o);
    int32_t warmup() const;
    bool silent() const;

    ConvKernel();
    ~ConvKernel();
    ConvKernel(ConvKernel&&) noexcept;
    ConvKernel& operator=(ConvKernel&&) noexcept;
    ConvKernel(const ConvKernel&) = delete;
    ConvKernel& operator=(const ConvKernel&) = delete;

  private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace nota::dsp
