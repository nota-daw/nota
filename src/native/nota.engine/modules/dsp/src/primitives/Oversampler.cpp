// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/Oversampler.h"

#include "nota/math/StereoOps.h"

#include <cstdint>
#include <atomic>
#include <hiir/Downsampler2xFpu.h>
#include <hiir/Upsampler2xFpu.h>

#include <cstddef>
#include <memory>

namespace nota::dsp {

struct Oversampler::Stages {
    hiir::Upsampler2xFpu<kNcSteep> up0[2];
    hiir::Upsampler2xFpu<kNcMid> up1[2];
    hiir::Upsampler2xFpu<kNcWide> up2[2];
    hiir::Downsampler2xFpu<kNcSteep> dn0[2];
    hiir::Downsampler2xFpu<kNcMid> dn1[2];
    hiir::Downsampler2xFpu<kNcWide> dn2[2];
};

Oversampler::Oversampler() : stages_(std::make_unique<Stages>()) {}
Oversampler::~Oversampler() = default;

void Oversampler::prepare(int32_t maxBlockFrames) {
    if (!stages_) stages_ = std::make_unique<Stages>();
    maxFrames_ = maxBlockFrames > 0 ? maxBlockFrames : 1;
    static constexpr double kSteep[kNcSteep] = {
        0.017347915108876406,
        0.067150480426919179,
        0.14330738338179819,
        0.23745131944299824,
        0.34085550201503761,
        0.44601111310335906,
        0.54753112652956148,
        0.6423859124721446,
        0.72968928615804163,
        0.81029959388029904,
        0.88644514917318362,
        0.96150605146543733,
    };
    static constexpr double kMid[kNcMid] = {
        0.029113887601773612,
        0.11638402872809682,
        0.26337786480329456,
        0.47885453461538624,
        0.78984065611473109,
    };
    static constexpr double kWide[kNcWide] = {
        0.056028689580145966,
        0.2438952031065017,
        0.64749960965360798,
    };
    for (int c = 0; c < 2; ++c) {
        stages_->up0[c].set_coefs(kSteep);
        stages_->dn0[c].set_coefs(kSteep);
        stages_->up1[c].set_coefs(kMid);
        stages_->dn1[c].set_coefs(kMid);
        stages_->up2[c].set_coefs(kWide);
        stages_->dn2[c].set_coefs(kWide);
    }
    const std::size_t n = static_cast<std::size_t>(maxFrames_);
    inL_.assign(n, 0.0f);
    inR_.assign(n, 0.0f);
    topL_.assign(n * 8u, 0.0f);
    topR_.assign(n * 8u, 0.0f);
    s2_.assign(n * 2u, 0.0f);
    s4_.assign(n * 4u, 0.0f);
    clearStages();
    active_ = 1;
}

void Oversampler::setActive(int32_t factor) noexcept {
    const int32_t f = factor >= 8 ? 8 : (factor >= 4 ? 4 : (factor >= 2 ? 2 : 1));
    factor_.store(f, std::memory_order_relaxed);
}

void Oversampler::clearStages() noexcept {
    if (!stages_) return;
    for (int c = 0; c < 2; ++c) {
        stages_->up0[c].clear_buffers();
        stages_->up1[c].clear_buffers();
        stages_->up2[c].clear_buffers();
        stages_->dn0[c].clear_buffers();
        stages_->dn1[c].clear_buffers();
        stages_->dn2[c].clear_buffers();
    }
}

void Oversampler::upsample(int32_t ch, const float* in, float* top, int32_t frames, int32_t f) noexcept {
    if (!stages_ || (ch != 0 && ch != 1)) return;
    if (f == 2) {
        stages_->up0[ch].process_block(top, in, frames);
        return;
    }
    if (f == 4) {
        stages_->up0[ch].process_block(s2_.data(), in, frames);
        stages_->up1[ch].process_block(top, s2_.data(), frames * 2);
        return;
    }
    stages_->up0[ch].process_block(s2_.data(), in, frames);
    stages_->up1[ch].process_block(s4_.data(), s2_.data(), frames * 2);
    stages_->up2[ch].process_block(top, s4_.data(), frames * 4);
}

void Oversampler::downsample(int32_t ch, const float* top, float* out, int32_t frames, int32_t f) noexcept {
    if (!stages_ || (ch != 0 && ch != 1)) return;
    if (f == 2) {
        stages_->dn0[ch].process_block(out, top, frames);
        return;
    }
    if (f == 4) {
        stages_->dn1[ch].process_block(s2_.data(), top, frames * 2);
        stages_->dn0[ch].process_block(out, s2_.data(), frames);
        return;
    }
    stages_->dn2[ch].process_block(s4_.data(), top, frames * 4);
    stages_->dn1[ch].process_block(s2_.data(), s4_.data(), frames * 2);
    stages_->dn0[ch].process_block(out, s2_.data(), frames);
}

void Oversampler::deinterleave(float* dstL, float* dstR, const float* src, int32_t frames) noexcept {
    math::deinterleave(dstL, dstR, src, frames);
}

void Oversampler::interleave(float* dst, const float* srcL, const float* srcR, int32_t frames) noexcept {
    math::interleave(dst, srcL, srcR, frames);
}

} // namespace nota::dsp
