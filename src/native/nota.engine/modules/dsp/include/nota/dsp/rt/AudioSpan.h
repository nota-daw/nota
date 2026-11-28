// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioSpan — zero-overhead non-owning views over interleaved stereo / planar buffers.

#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

namespace nota::dsp {

class StereoSpan {
  public:
    constexpr StereoSpan() noexcept = default;
    constexpr StereoSpan(float* interleaved, int32_t frames) noexcept
        : data_(interleaved), frames_(frames > 0 ? frames : 0) {}

    constexpr float* data() const noexcept { return data_; }
    constexpr int32_t frames() const noexcept { return frames_; }
    constexpr int32_t samples() const noexcept { return frames_ * 2; }
    constexpr bool empty() const noexcept { return frames_ <= 0 || data_ == nullptr; }

    constexpr std::span<float> span() const noexcept {
        return data_ ? std::span<float>{data_, static_cast<std::size_t>(frames_) * 2u} : std::span<float>{};
    }

    constexpr StereoSpan subframes(int32_t offset, int32_t count) const noexcept {
        if (data_ == nullptr || offset < 0 || offset >= frames_) return {};
        const int32_t avail = frames_ - offset;
        const int32_t clamped = count < avail ? count : avail;
        return {data_ + static_cast<std::ptrdiff_t>(offset) * 2, clamped};
    }

    void clear() const noexcept {
        if (data_ == nullptr || frames_ <= 0) return;
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

    constexpr const float* data() const noexcept { return data_; }
    constexpr int32_t frames() const noexcept { return frames_; }
    constexpr int32_t samples() const noexcept { return frames_ * 2; }
    constexpr bool empty() const noexcept { return frames_ <= 0 || data_ == nullptr; }

    constexpr std::span<const float> span() const noexcept {
        return data_ ? std::span<const float>{data_, static_cast<std::size_t>(frames_) * 2u} : std::span<const float>{};
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

    constexpr float* left() const noexcept { return left_; }
    constexpr float* right() const noexcept { return right_; }
    constexpr int32_t frames() const noexcept { return frames_; }
    constexpr bool empty() const noexcept { return frames_ <= 0 || left_ == nullptr || right_ == nullptr; }

    constexpr std::span<float> leftSpan() const noexcept {
        return left_ ? std::span<float>{left_, static_cast<std::size_t>(frames_)} : std::span<float>{};
    }
    constexpr std::span<float> rightSpan() const noexcept {
        return right_ ? std::span<float>{right_, static_cast<std::size_t>(frames_)} : std::span<float>{};
    }

  private:
    float* left_ = nullptr;
    float* right_ = nullptr;
    int32_t frames_ = 0;
};

} // namespace nota::dsp
