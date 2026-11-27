// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/math/Fft.h"

#include <memory>
#include <cstdint>
#include <complex>
#include <signalsmith-linear/fft.h>

#include <algorithm>
#include <cstddef>

namespace nota::math {

struct RealFft::Impl {
    signalsmith::linear::RealFFT<float> fft;
    int32_t size = 0;

    explicit Impl(int32_t n) : fft(n > 0 ? static_cast<size_t>(n) : 0), size(std::max(0, n)) {}
};

RealFft::RealFft(int32_t size) : impl_(std::make_unique<Impl>(size)) {}
RealFft::~RealFft() = default;
RealFft::RealFft(RealFft&&) noexcept = default;
RealFft& RealFft::operator=(RealFft&&) noexcept = default;

void RealFft::resize(int32_t size) {
    if (!impl_) impl_ = std::make_unique<Impl>(size);
    const int32_t clamped = std::max(0, size);
    impl_->fft.resize(static_cast<size_t>(clamped));
    impl_->size = clamped;
}

int32_t RealFft::size() const noexcept {
    return impl_ ? impl_->size : 0;
}
int32_t RealFft::halfSize() const noexcept {
    return size() / 2;
}

int32_t RealFft::fastSizeAbove(int32_t minSize) noexcept {
    if (minSize <= 0) return 0;
    return static_cast<int32_t>(signalsmith::linear::RealFFT<float>::fastSizeAbove(static_cast<size_t>(minSize)));
}

void RealFft::forward(const float* timeIn, float* realOut, float* imagOut) noexcept {
    if (!impl_ || impl_->size <= 0 || !timeIn || !realOut || !imagOut) return;
    impl_->fft.fft(timeIn, realOut, imagOut);
}

void RealFft::inverse(const float* realIn, const float* imagIn, float* timeOut) noexcept {
    if (!impl_ || impl_->size <= 0 || !realIn || !imagIn || !timeOut) return;
    impl_->fft.ifft(realIn, imagIn, timeOut);
}

void RealFft::forwardPacked(const float* timeIn, std::complex<float>* packedOut) noexcept {
    if (!impl_ || impl_->size <= 0 || !timeIn || !packedOut) return;
    impl_->fft.fft(timeIn, packedOut);
}

void RealFft::inversePacked(const std::complex<float>* packedIn, float* timeOut) noexcept {
    if (!impl_ || impl_->size <= 0 || !packedIn || !timeOut) return;
    impl_->fft.ifft(packedIn, timeOut);
}

} // namespace nota::math