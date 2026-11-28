// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Fft — Pimpl RealFFT wrapper (isolates Signalsmith/Accelerate/PFFFT) and DSP window generators.

#pragma once

#include "nota/math/Export.h"

#include <complex>
#include <cstdint>
#include <memory>

namespace nota::math {

class NOTA_MATH_API RealFft {
  public:
    RealFft() noexcept;
    explicit RealFft(int32_t size);
    ~RealFft();

    RealFft(RealFft&&) noexcept;
    RealFft& operator=(RealFft&&) noexcept;
    RealFft(const RealFft&) = delete;
    RealFft& operator=(const RealFft&) = delete;

    void resize(int32_t size);
    int32_t size() const noexcept;
    int32_t halfSize() const noexcept;

    static int32_t fastSizeAbove(int32_t minSize) noexcept;

    // Split real/imag packed layout: bin 0 stores DC (real) & Nyquist (imag), bins 1..N/2-1 complex.
    void forward(const float* timeIn, float* realOut, float* imagOut) noexcept;
    void inverse(const float* realIn, const float* imagIn, float* timeOut) noexcept;

    // Interleaved complex packed layout: N/2 std::complex<float> elements, bin 0 = {DC, Nyquist}.
    void forwardPacked(const float* timeIn, std::complex<float>* packedOut) noexcept;
    void inversePacked(const std::complex<float>* packedIn, float* timeOut) noexcept;

  private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

struct NOTA_MATH_API Windowing {
    static void fillHann(float* dst, int32_t count, bool periodic = true) noexcept;
    static void fillBlackman(float* dst, int32_t count, bool periodic = true) noexcept;
    static double besselI0(double x) noexcept;
    static void fillKaiser(float* dst, int32_t count, float beta, bool symmetric = true) noexcept;
    static void fillKaiserSinc(float* dst, int32_t taps, double cutoffRatio, double beta) noexcept;
};

} // namespace nota::math