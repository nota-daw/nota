// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared oversampling for the built-in nonlinear devices (saturators, amp sims,
// degraders). A device wraps only its per-sample nonlinear stage in
// Oversampler::process(); the framework up-samples the block, runs the callback
// at the higher rate, then down-samples back — killing the aliasing a hard
// nonlinearity would otherwise fold back into the audible band.
//
// Backend: HIIR (Laurent de Soras, WTFPL) polyphase half-band IIR — the "Fast"
// minimum-phase path: near-zero latency, cheap, phase not linear. Factors 1/2/4/8
// via a cascade of 2× half-band stages, each with a hand-picked HIIR coefficient
// set (steep at the base rate, progressively wider up top where there's nothing
// left to alias). A future linear-phase ("Clean") kernel would report real
// latency; this one reports 0.
//
// RT contract: prepare() (message thread) does the only allocation, sizing all
// scratch to maxBlock*8. setActive() just stores an atomic — switching factor on
// the audio thread is allocation-free; the stage state is cleared on the audio
// thread when the factor actually changes, so there's no click or data race.

#pragma once

#include "hiir/Upsampler2xFpu.h"
#include "hiir/Downsampler2xFpu.h"

#include <atomic>
#include <cstdint>
#include <vector>

namespace nota {

class Oversampler {
public:
    // HIIR half-band coefficient sets (from HIIR's oversampling.txt). Attenuation
    // ~150 dB at the steep base stage, relaxing to ~133 dB up top.
    static constexpr int kNcSteep = 12, kNcMid = 5, kNcWide = 3;

    void prepare(int32_t maxBlockFrames) {
        maxFrames_ = maxBlockFrames > 0 ? maxBlockFrames : 1;
        static const double steep[kNcSteep] = {
            0.017347915108876406, 0.067150480426919179, 0.14330738338179819, 0.23745131944299824,
            0.34085550201503761, 0.44601111310335906, 0.54753112652956148, 0.6423859124721446,
            0.72968928615804163, 0.81029959388029904, 0.88644514917318362, 0.96150605146543733 };
        static const double mid[kNcMid] = {
            0.029113887601773612, 0.11638402872809682, 0.26337786480329456, 0.47885453461538624,
            0.78984065611473109 };
        static const double wide[kNcWide] = {
            0.056028689580145966, 0.2438952031065017, 0.64749960965360798 };
        for (int c = 0; c < 2; ++c) {
            up0_[c].set_coefs(steep); dn0_[c].set_coefs(steep);
            up1_[c].set_coefs(mid);   dn1_[c].set_coefs(mid);
            up2_[c].set_coefs(wide);  dn2_[c].set_coefs(wide);
        }
        inL_.assign(maxFrames_, 0.0f);      inR_.assign(maxFrames_, 0.0f);
        topL_.assign(maxFrames_ * 8, 0.0f); topR_.assign(maxFrames_ * 8, 0.0f);
        s2_.assign(maxFrames_ * 2, 0.0f);   s4_.assign(maxFrames_ * 4, 0.0f);
        clearStages();
        active_ = 1;
    }

    // 1, 2, 4 or 8 (others clamp to the nearest power of two ≤ 8). Lock-free.
    void setActive(int32_t factor) {
        int f = factor >= 8 ? 8 : factor >= 4 ? 4 : factor >= 2 ? 2 : 1;
        factor_.store(f, std::memory_order_relaxed);
    }

    int32_t factor() const { return factor_.load(std::memory_order_relaxed); }
    int32_t latencySamples() const { return 0; }   // Fast (minimum-phase) kernel

    // Runs `fn(float& l, float& r, int baseIndex)` per sample at the oversampled
    // rate over the whole interleaved-stereo block, in place. baseIndex is the
    // originating base-rate sample (0..frames-1), so a device can hold its
    // control-rate modulation constant across the `factor` sub-samples. fn is
    // pure per-sample DSP.
    template <class Fn>
    void process(float* buf, int32_t frames, Fn&& fn) {
        int f = factor_.load(std::memory_order_relaxed);
        if (f != active_) { clearStages(); active_ = f; }

        if (f <= 1) {
            for (int32_t i = 0; i < frames; ++i) {
                float l = buf[i * 2], r = buf[i * 2 + 1];
                fn(l, r, i);
                buf[i * 2] = l; buf[i * 2 + 1] = r;
            }
            return;
        }
        if (frames > maxFrames_) frames = maxFrames_;   // guard (prepare sized to max)

        for (int32_t i = 0; i < frames; ++i) { inL_[i] = buf[i * 2]; inR_[i] = buf[i * 2 + 1]; }
        upsample(0, inL_.data(), topL_.data(), frames, f);
        upsample(1, inR_.data(), topR_.data(), frames, f);

        const int32_t n = frames * f;
        for (int32_t k = 0, base = 0, sub = 0; k < n; ++k) {
            fn(topL_[k], topR_[k], base);
            if (++sub == f) { sub = 0; ++base; }
        }

        downsample(0, topL_.data(), inL_.data(), frames, f);
        downsample(1, topR_.data(), inR_.data(), frames, f);
        for (int32_t i = 0; i < frames; ++i) { buf[i * 2] = inL_[i]; buf[i * 2 + 1] = inR_[i]; }
    }

private:
    void clearStages() {
        for (int c = 0; c < 2; ++c) {
            up0_[c].clear_buffers(); up1_[c].clear_buffers(); up2_[c].clear_buffers();
            dn0_[c].clear_buffers(); dn1_[c].clear_buffers(); dn2_[c].clear_buffers();
        }
    }

    // base → top (factor). Intermediate rates land in s2_/s4_.
    void upsample(int ch, const float* in, float* top, int32_t frames, int f) {
        if (f == 2) { up0_[ch].process_block(top, in, frames); return; }
        if (f == 4) {
            up0_[ch].process_block(s2_.data(), in, frames);
            up1_[ch].process_block(top, s2_.data(), frames * 2);
            return;
        }
        // f == 8
        up0_[ch].process_block(s2_.data(), in, frames);
        up1_[ch].process_block(s4_.data(), s2_.data(), frames * 2);
        up2_[ch].process_block(top, s4_.data(), frames * 4);
    }

    // top (factor) → base. Down-sampling runs the stages in reverse (widest first).
    void downsample(int ch, const float* top, float* out, int32_t frames, int f) {
        if (f == 2) { dn0_[ch].process_block(out, top, frames); return; }
        if (f == 4) {
            dn1_[ch].process_block(s2_.data(), top, frames * 2);
            dn0_[ch].process_block(out, s2_.data(), frames);
            return;
        }
        // f == 8
        dn2_[ch].process_block(s4_.data(), top, frames * 4);
        dn1_[ch].process_block(s2_.data(), s4_.data(), frames * 2);
        dn0_[ch].process_block(out, s2_.data(), frames);
    }

    // [0] = left, [1] = right. 2× half-band stages: 0 = base↔2× (steep), 1 = 2×↔4×,
    // 2 = 4×↔8× (widest).
    hiir::Upsampler2xFpu<kNcSteep>   up0_[2];
    hiir::Upsampler2xFpu<kNcMid>     up1_[2];
    hiir::Upsampler2xFpu<kNcWide>    up2_[2];
    hiir::Downsampler2xFpu<kNcSteep> dn0_[2];
    hiir::Downsampler2xFpu<kNcMid>   dn1_[2];
    hiir::Downsampler2xFpu<kNcWide>  dn2_[2];

    std::vector<float> inL_, inR_, topL_, topR_, s2_, s4_;
    int32_t maxFrames_ = 0;
    int active_ = 1;                       // audio-thread view of the applied factor
    std::atomic<int> factor_{1};
};

} // namespace nota
