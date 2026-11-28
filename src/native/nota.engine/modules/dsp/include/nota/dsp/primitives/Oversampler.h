// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Oversampler — 2x/4x/8x polyphase oversampling with HIIR hidden in Pimpl and inline sample loop.

#pragma once

#include "nota/dsp/Export.h"

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota::dsp {

class NOTA_DSP_API Oversampler {
  public:
    static constexpr int32_t kNcSteep = 12;
    static constexpr int32_t kNcMid = 5;
    static constexpr int32_t kNcWide = 3;

    Oversampler();
    ~Oversampler();
    Oversampler(Oversampler&&) = delete;
    Oversampler& operator=(Oversampler&&) = delete;
    Oversampler(const Oversampler&) = delete;
    Oversampler& operator=(const Oversampler&) = delete;

    void prepare(int32_t maxBlockFrames);
    void setActive(int32_t factor) noexcept;
    int32_t factor() const noexcept { return factor_.load(std::memory_order_relaxed); }
    int32_t latencySamples() const noexcept { return 0; }

    template <class Fn> void process(float* buf, int32_t frames, Fn&& fn) {
        if (!buf || frames <= 0) return;
        const int32_t f = factor_.load(std::memory_order_relaxed);
        if (f != active_) {
            clearStages();
            active_ = f;
        }
        if (f <= 1) {
            for (int32_t i = 0; i < frames; ++i) {
                float l = buf[i * 2];
                float r = buf[i * 2 + 1];
                fn(l, r, i);
                buf[i * 2] = l;
                buf[i * 2 + 1] = r;
            }
            return;
        }
        if (maxFrames_ <= 0) return;
        if (frames > maxFrames_) frames = maxFrames_;

        deinterleave(inL_.data(), inR_.data(), buf, frames);
        upsample(0, inL_.data(), topL_.data(), frames, f);
        upsample(1, inR_.data(), topR_.data(), frames, f);

        const std::size_t n = static_cast<std::size_t>(frames) * static_cast<std::size_t>(f);
        float* const tl = topL_.data();
        float* const tr = topR_.data();
        for (std::size_t k = 0; k < n;) {
            const int32_t base = static_cast<int32_t>(k / static_cast<std::size_t>(f));
            for (int32_t sub = 0; sub < f; ++sub, ++k) { fn(tl[k], tr[k], base); }
        }

        downsample(0, topL_.data(), inL_.data(), frames, f);
        downsample(1, topR_.data(), inR_.data(), frames, f);
        interleave(buf, inL_.data(), inR_.data(), frames);
    }

  private:
    struct Stages;

    void clearStages() noexcept;
    void upsample(int32_t ch, const float* in, float* top, int32_t frames, int32_t f) noexcept;
    void downsample(int32_t ch, const float* top, float* out, int32_t frames, int32_t f) noexcept;
    static void deinterleave(float* dstL, float* dstR, const float* src, int32_t frames) noexcept;
    static void interleave(float* dst, const float* srcL, const float* srcR, int32_t frames) noexcept;

    std::unique_ptr<Stages> stages_;
    std::vector<float> inL_, inR_, topL_, topR_, s2_, s4_;
    int32_t maxFrames_ = 0;
    int32_t active_ = 1;
    std::atomic<int32_t> factor_{1};
};

} // namespace nota::dsp
