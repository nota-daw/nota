// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/Export.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <string>
#include <utility>
#include <vector>

namespace nota::media {

struct StreamRuntimeStats {
    int64_t cacheBytes = 0;
    int64_t pagesFaulted = 0;
    int64_t pagesServedAsync = 0;
    int64_t pagesServedSync = 0;
    int64_t rtUnderruns = 0;
};

class NOTA_MEDIA_API AudioSource {
  public:
    virtual ~AudioSource() = default;

    virtual int32_t channels() const noexcept = 0;
    virtual int64_t frames() const noexcept = 0;
    virtual double sampleRate() const noexcept = 0;
    virtual bool isStreaming() const noexcept = 0;
    virtual int64_t memoryBytes() const noexcept = 0;
    virtual const std::string& path() const noexcept = 0;

    virtual float at(int64_t frame, int32_t ch) const noexcept = 0;
    virtual void readStereo(int64_t frame, float& l, float& r) const noexcept = 0;

    virtual void readStereoHinted(
        int64_t frame, int32_t& slotHint, bool allowSyncFill, float& l, float& r) const noexcept {
        (void)slotHint;
        (void)allowSyncFill;
        readStereo(frame, l, r);
    }

    virtual void preparePlaybackWindow(
        int64_t minFrame, int64_t maxFrame, bool reverse, bool allowSyncFill) const noexcept {
        (void)minFrame;
        (void)maxFrame;
        (void)reverse;
        (void)allowSyncFill;
    }

    virtual void primeAround(int64_t frame, bool reverse = false) const {
        preparePlaybackWindow(frame, frame, reverse, true);
    }

    virtual int64_t readAllInterleaved(float* out, int64_t capFloats) const = 0;

    virtual StreamRuntimeStats runtimeStats() const noexcept { return {}; }
    virtual void setPlaybackActive(bool /*active*/) const noexcept {}
};

class NOTA_MEDIA_API MemoryAudioSource final : public AudioSource {
  public:
    MemoryAudioSource(
        std::vector<float> samples, int32_t channels, int64_t frames, double sampleRate, std::string sourcePath = {})
        : samples_(std::move(samples)), channels_(channels), frames_(frames), sampleRate_(sampleRate),
          path_(std::move(sourcePath)) {}

    int32_t channels() const noexcept override { return channels_; }
    int64_t frames() const noexcept override { return frames_; }
    double sampleRate() const noexcept override { return sampleRate_; }
    bool isStreaming() const noexcept override { return false; }
    int64_t memoryBytes() const noexcept override { return static_cast<int64_t>(samples_.capacity() * sizeof(float)); }
    const std::string& path() const noexcept override { return path_; }

    float at(int64_t frame, int32_t ch) const noexcept override {
        if (frame < 0 || frame >= frames_ || ch < 0 || ch >= channels_ || samples_.empty()) return 0.0f;
        return samples_[static_cast<size_t>(frame) * static_cast<size_t>(channels_) + static_cast<size_t>(ch)];
    }

    void readStereo(int64_t frame, float& l, float& r) const noexcept override {
        if (frame < 0 || frame >= frames_ || channels_ <= 0 || samples_.empty()) {
            l = 0.0f;
            r = 0.0f;
            return;
        }
        const size_t base = static_cast<size_t>(frame) * static_cast<size_t>(channels_);
        l = samples_[base];
        r = channels_ >= 2 ? samples_[base + 1] : l;
    }

    int64_t readAllInterleaved(float* out, int64_t capFloats) const override {
        const int64_t total = static_cast<int64_t>(samples_.size());
        if (out && capFloats > 0) {
            const int64_t n = std::min(total, capFloats);
            std::copy_n(samples_.data(), n, out);
        }
        return total;
    }

    const std::vector<float>& samples() const noexcept { return samples_; }
    std::vector<float>& writableSamples() noexcept { return samples_; }

  private:
    std::vector<float> samples_;
    int32_t channels_ = 0;
    int64_t frames_ = 0;
    double sampleRate_ = 0.0;
    std::string path_;
};

} // namespace nota::media
