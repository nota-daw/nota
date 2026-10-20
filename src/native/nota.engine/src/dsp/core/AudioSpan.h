// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioSpan — zero-overhead non-owning views over interleaved stereo float
// buffers (L0,R0,L1,R1,...). AR-6 safe: no allocation, trivially copyable.

#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

namespace nota {
namespace dsp {

class StereoSpan {
  public:
    constexpr StereoSpan() noexcept = default;
    constexpr StereoSpan(float* interleaved, int32_t frames) noexcept
        : data_(interleaved), frames_(frames > 0 ? frames : 0) {}

    [[nodiscard]] constexpr float* data() const noexcept { return data_; }
    [[nodiscard]] constexpr int32_t frames() const noexcept { return frames_; }
    [[nodiscard]] constexpr int32_t samples() const noexcept { return frames_ * 2; }
    [[nodiscard]] constexpr bool empty() const noexcept { return frames_ <= 0 || data_ == nullptr; }

    [[nodiscard]] constexpr std::span<float> span() const noexcept {
        return {data_, static_cast<std::size_t>(frames_ * 2)};
    }
    [[nodiscard]] constexpr StereoSpan subframes(int32_t offset, int32_t count) const noexcept {
        return {data_ != nullptr ? data_ + offset * 2 : nullptr, count};
    }
    void clear() const noexcept {
        if (data_ == nullptr) return;
        const int32_t n = frames_ * 2;
        for (int32_t i = 0; i < n; ++i) data_[i] = 0.0f;
    }

  private:
    float* data_ = nullptr;
    int32_t frames_ = 0;
};

class ConstStereoSpan {
  public:
    constexpr ConstStereoSpan() noexcept = default;
    constexpr ConstStereoSpan(const float* interleaved, int32_t frames) noexcept
        : data_(interleaved), frames_(frames > 0 ? frames : 0) {}

    [[nodiscard]] constexpr const float* data() const noexcept { return data_; }
    [[nodiscard]] constexpr int32_t frames() const noexcept { return frames_; }
    [[nodiscard]] constexpr int32_t samples() const noexcept { return frames_ * 2; }
    [[nodiscard]] constexpr bool empty() const noexcept { return frames_ <= 0 || data_ == nullptr; }

    [[nodiscard]] constexpr std::span<const float> span() const noexcept {
        return {data_, static_cast<std::size_t>(frames_ * 2)};
    }

  private:
    const float* data_ = nullptr;
    int32_t frames_ = 0;
};

class PlanarAudioSpan {
  public:
    constexpr PlanarAudioSpan() noexcept = default;
    constexpr PlanarAudioSpan(float* left, float* right, int32_t frames) noexcept
        : left_(left), right_(right), frames_(frames > 0 ? frames : 0) {}

    [[nodiscard]] constexpr float* left() const noexcept { return left_; }
    [[nodiscard]] constexpr float* right() const noexcept { return right_; }
    [[nodiscard]] constexpr int32_t frames() const noexcept { return frames_; }

  private:
    float* left_ = nullptr;
    float* right_ = nullptr;
    int32_t frames_ = 0;
};

} // namespace dsp
} // namespace nota
