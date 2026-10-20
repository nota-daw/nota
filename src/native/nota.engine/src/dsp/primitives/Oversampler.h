// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Oversampler — HIIR 2x/4x/8x polyphase half-band oversampling (
// ). Moved from src/Oversampler.h into the shared primitive: setup and
// stage routing live in the .cpp, the per-sample callback loop stays a header
// template (FR-002). Stereo (de)interleaving is vectorized via SimdAudio.
// Backend: HIIR minimum-phase ("Fast") path — near-zero latency, reports 0.
//
// RT contract: prepare() (message thread) does the only allocation, sizing all
// scratch to maxBlock*8. setActive() just stores an atomic — switching factor
// on the audio thread is allocation-free.

#pragma once

#include <hiir/Upsampler2xFpu.h>
#include <hiir/Downsampler2xFpu.h>

#include <atomic>
#include <cstdint>
#include <vector>

namespace nota {
namespace dsp {

class Oversampler {
  public:
    static constexpr int kNcSteep = 12, kNcMid = 5, kNcWide = 3;

    Oversampler() = default;

    void prepare(int32_t maxBlockFrames);
    // 1, 2, 4 or 8 (others clamp to the nearest power of two ≤ 8). Lock-free.
    void setActive(int32_t factor);
    int32_t factor() const { return factor_.load(std::memory_order_relaxed); }
    int32_t latencySamples() const { return 0; }

    // Runs `fn(float& l, float& r, int baseIndex)` per sample at the oversampled
    // rate over the whole interleaved-stereo block, in place. baseIndex is the
    // originating base-rate sample, so control-rate modulation stays constant
    // across the `factor` sub-samples.
    template <class Fn> void process(float* buf, int32_t frames, Fn&& fn) {
        int f = factor_.load(std::memory_order_relaxed);
        if (f != active_) {
            clearStages();
            active_ = f;
        }
        if (f <= 1) {
            for (int32_t i = 0; i < frames; ++i) {
                float l = buf[i * 2], r = buf[i * 2 + 1];
                fn(l, r, i);
                buf[i * 2] = l;
                buf[i * 2 + 1] = r;
            }
            return;
        }
        if (frames > maxFrames_) frames = maxFrames_; // guard (prepare sized to max)

        deinterleave(inL_.data(), inR_.data(), buf, frames);
        upsample(0, inL_.data(), topL_.data(), frames, f);
        upsample(1, inR_.data(), topR_.data(), frames, f);

        const int32_t n = frames * f;
        for (int32_t k = 0, base = 0, sub = 0; k < n; ++k) {
            fn(topL_[k], topR_[k], base);
            if (++sub == f) {
                sub = 0;
                ++base;
            }
        }

        downsample(0, topL_.data(), inL_.data(), frames, f);
        downsample(1, topR_.data(), inR_.data(), frames, f);
        interleave(buf, inL_.data(), inR_.data(), frames);
    }

  private:
    void clearStages();
    void upsample(int ch, const float* in, float* top, int32_t frames, int f);
    void downsample(int ch, const float* top, float* out, int32_t frames, int f);
    void deinterleave(float* dstL, float* dstR, const float* src, int32_t frames);
    void interleave(float* dst, const float* srcL, const float* srcR, int32_t frames);

    hiir::Upsampler2xFpu<kNcSteep> up0_[2];
    hiir::Upsampler2xFpu<kNcMid> up1_[2];
    hiir::Upsampler2xFpu<kNcWide> up2_[2];
    hiir::Downsampler2xFpu<kNcSteep> dn0_[2];
    hiir::Downsampler2xFpu<kNcMid> dn1_[2];
    hiir::Downsampler2xFpu<kNcWide> dn2_[2];

    std::vector<float> inL_, inR_, topL_, topR_, s2_, s4_;
    int32_t maxFrames_ = 0;
    int active_ = 1; // audio-thread view of the applied factor
    std::atomic<int> factor_{1};
};

} // namespace dsp
} // namespace nota
