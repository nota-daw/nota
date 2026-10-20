// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Oversampler implementation — coefficient sets from HIIR's oversampling.txt.

#include "dsp/primitives/Oversampler.h"

#include "dsp/math/SimdAudio.h"

namespace nota {
namespace dsp {

void Oversampler::prepare(int32_t maxBlockFrames) {
    maxFrames_ = maxBlockFrames > 0 ? maxBlockFrames : 1;
    static const double steep[kNcSteep] = {0.017347915108876406, 0.067150480426919179, 0.14330738338179819,
        0.23745131944299824, 0.34085550201503761, 0.44601111310335906, 0.54753112652956148, 0.6423859124721446,
        0.72968928615804163, 0.81029959388029904, 0.88644514917318362, 0.96150605146543733};
    static const double mid[kNcMid] = {
        0.029113887601773612, 0.11638402872809682, 0.26337786480329456, 0.47885453461538624, 0.78984065611473109};
    static const double wide[kNcWide] = {0.056028689580145966, 0.2438952031065017, 0.64749960965360798};
    for (int c = 0; c < 2; ++c) {
        up0_[c].set_coefs(steep);
        dn0_[c].set_coefs(steep);
        up1_[c].set_coefs(mid);
        dn1_[c].set_coefs(mid);
        up2_[c].set_coefs(wide);
        dn2_[c].set_coefs(wide);
    }
    inL_.assign(maxFrames_, 0.0f);
    inR_.assign(maxFrames_, 0.0f);
    topL_.assign(maxFrames_ * 8, 0.0f);
    topR_.assign(maxFrames_ * 8, 0.0f);
    s2_.assign(maxFrames_ * 2, 0.0f);
    s4_.assign(maxFrames_ * 4, 0.0f);
    clearStages();
    active_ = 1;
}

void Oversampler::setActive(int32_t factor) {
    int f = factor >= 8 ? 8 : factor >= 4 ? 4 : factor >= 2 ? 2 : 1;
    factor_.store(f, std::memory_order_relaxed);
}

void Oversampler::clearStages() {
    for (int c = 0; c < 2; ++c) {
        up0_[c].clear_buffers();
        up1_[c].clear_buffers();
        up2_[c].clear_buffers();
        dn0_[c].clear_buffers();
        dn1_[c].clear_buffers();
        dn2_[c].clear_buffers();
    }
}

void Oversampler::upsample(int ch, const float* in, float* top, int32_t frames, int f) {
    if (f == 2) {
        up0_[ch].process_block(top, in, frames);
        return;
    }
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

void Oversampler::downsample(int ch, const float* top, float* out, int32_t frames, int f) {
    if (f == 2) {
        dn0_[ch].process_block(out, top, frames);
        return;
    }
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

void Oversampler::deinterleave(float* dstL, float* dstR, const float* src, int32_t frames) {
    simd_audio::deinterleaveStereo(dstL, dstR, src, frames);
}

void Oversampler::interleave(float* dst, const float* srcL, const float* srcR, int32_t frames) {
    simd_audio::interleaveStereo(dst, srcL, srcR, frames);
}

} // namespace dsp
} // namespace nota
