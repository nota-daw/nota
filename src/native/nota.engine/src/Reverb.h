// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in reverb (mockup 2h) — an extended Freeverb (Schroeder-Moorer): 8 damped comb
// filters + 4 allpasses per channel on a variable-size, modulated fractional delay line.
// Two honest bands: SPACE (pre-delay · size · diffusion) and TONE (low cut · high cut ·
// width), plus decay (RT60), HF damping, a chorus-like tail modulation, four algorithms
// (Hall/Room/Plate/Chamber), Freeze (infinite tail) and dry/wet + output trim.
//
// All params are normalized 0..1 (denormalized in process()); persist/clone/automation
// are generic through the base Device. Buffers are allocated at max size on the message
// thread in setSampleRate, so process() never allocates. Freeverb is public-domain
// (Jezar at Dreampoint); the extensions & tuning are Nota's own.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Reverb : public Device {
public:
    enum {
        Decay = 0, HFDamp, PreDelay, Size, Diffusion, LowCut, HighCut, Width,
        ModRate, ModDepth, Algorithm, Freeze, DryWet, Output, kNumParams
    };

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        const double scale = sr_ / 44100.0;
        srScale_ = scale;
        for (int c = 0; c < kCombs; ++c) {
            int maxN = static_cast<int>(kCombTuning[c] * scale * kMaxSize) + 4;
            combL_[c].init(maxN); combR_[c].init(maxN);
        }
        for (int a = 0; a < kAllpasses; ++a) {
            allpL_[a].init(static_cast<int>(kAllpassTuning[a] * scale) + 2);
            allpR_[a].init(static_cast<int>((kAllpassTuning[a] + kStereoSpread) * scale) + 2);
        }
        preL_.assign(static_cast<int>(sr_ * 0.2) + 2, 0.0f);
        preR_.assign(preL_.size(), 0.0f);
        preW_ = 0;
    }

    void process(float* buf, int32_t frames) override {
        const float decay = clamp01(p_[Decay]);
        const float damp  = clamp01(p_[HFDamp]);
        const float preMs = clamp01(p_[PreDelay]) * 200.0f;
        const float size  = 0.55f + clamp01(p_[Size]) * (kMaxSize - 0.55f);
        const float diff  = 0.4f + clamp01(p_[Diffusion]) * 0.35f;   // allpass gain
        const float width = clamp01(p_[Width]);
        const float modHz = expMap(clamp01(p_[ModRate]), 0.05, 5.0);
        const float modD  = clamp01(p_[ModDepth]) * 12.0f * (float)srScale_;   // samples
        const int   algo  = (int)std::lround(clamp01(p_[Algorithm]) * 3.0f);
        const bool  freeze = p_[Freeze].load(std::memory_order_relaxed) > 0.5f;
        const float wet   = clamp01(p_[DryWet]);
        const float dry   = 1.0f - wet;
        const float outG  = clamp01(p_[Output]) * 2.0f;

        const float feedback = freeze ? 1.0f : (0.72f + decay * 0.265f);
        const float damp1 = freeze ? 0.0f : damp * 0.4f, damp2 = 1.0f - damp1;
        const float inGain = freeze ? 0.0f : 0.015f;
        const float algoScale = kAlgoScale[algo];

        // Tone: one-pole HP (low cut) + LP (high cut) coefficients on the wet path.
        const double lc = expMap(clamp01(p_[LowCut]), 20.0, 1000.0);
        const double hc = expMap(clamp01(p_[HighCut]), 1000.0, 20000.0);
        const float hpCoef = (float)std::exp(-2.0 * kPi * lc / sr_);
        const float lpCoef = (float)(1.0 - std::exp(-2.0 * kPi * hc / sr_));

        for (int c = 0; c < kCombs; ++c) { combL_[c].set(feedback, damp1, damp2); combR_[c].set(feedback, damp1, damp2); }
        for (int a = 0; a < kAllpasses; ++a) { allpL_[a].g = diff; allpR_[a].g = diff; }
        const int preSamp = std::clamp((int)(preMs * 0.001 * sr_), 0, (int)preL_.size() - 1);
        const double modInc = modHz / sr_;

        // Comb delay length = kCombTuning[c] * srScale * (size * algoScale). Size and
        // Algorithm change per block; jumping the read pointer inside a high-feedback loop
        // injects an uncorrelated sample that recirculates as a metallic blast. Glide the
        // span toward its target per-sample so the pointer moves continuously (also gives a
        // smooth "size morph" instead of a click). Primed to target on the first ever block.
        const float spanTarget = size * algoScale;
        if (span_ < 0.0f) span_ = spanTarget;

        for (int32_t i = 0; i < frames; ++i) {
            const float l = san(buf[i * 2]), r = san(buf[i * 2 + 1]);
            span_ += (spanTarget - span_) * kSpanGlide;

            // Pre-delay (feed the reverb a delayed copy of the input). Write first so a
            // zero pre-delay reads back the current sample (passthrough), not a full buffer.
            preL_[preW_] = l; preR_[preW_] = r;
            int pr = preW_ - preSamp; if (pr < 0) pr += (int)preL_.size();
            const float pl = preL_[pr], prr = preR_[pr];
            if (++preW_ >= (int)preL_.size()) preW_ = 0;

            const float input = (pl + prr) * inGain;
            const float lfo = std::sin(kTwoPi * modPhase_); modPhase_ += modInc; if (modPhase_ >= 1.0) modPhase_ -= 1.0;
            const float mod = lfo * modD;

            float outL = 0.0f, outR = 0.0f;
            for (int c = 0; c < kCombs; ++c) {
                float dl = kCombTuning[c] * (float)srScale_ * span_;
                outL += combL_[c].process(input, dl + mod);
                outR += combR_[c].process(input, dl - mod);
            }
            for (int a = 0; a < kAllpasses; ++a) { outL = allpL_[a].process(outL); outR = allpR_[a].process(outR); }

            // Tone filters on the wet signal.
            hpL_ = san(hpCoef * (hpL_ + outL - hpPrevL_)); hpPrevL_ = san(outL); outL = hpL_;
            hpR_ = san(hpCoef * (hpR_ + outR - hpPrevR_)); hpPrevR_ = san(outR); outR = hpR_;
            lpL_ = san(lpL_ + lpCoef * (outL - lpL_)); outL = lpL_;
            lpR_ = san(lpR_ + lpCoef * (outR - lpR_)); outR = lpR_;

            // Stereo width (mid/side).
            const float mid = (outL + outR) * 0.5f, side = (outL - outR) * 0.5f * (0.2f + width * 1.8f);
            outL = mid + side; outR = mid - side;

            // Final guard: a runaway (but finite) loop build-up here would blast the
            // speakers. A hard clamp bounds it but the clip itself sounds metallic, so
            // soft-saturate the wet contribution instead: transparent at normal levels,
            // a smooth tanh knee only when the tail overshoots (dry path stays clean).
            float wl = softClip(san(outL * wet)), wr = softClip(san(outR * wet));
            buf[i * 2]     = (wl + l * dry) * outG;
            buf[i * 2 + 1] = (wr + r * dry) * outG;
        }
    }

    const char* displayName() const override { return "Nota Reverb"; }
    int32_t     builtinKind() const override { return 2; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Decay", "HF Damp", "Pre-Delay", "Size", "Diffusion", "Low Cut",
            "High Cut", "Width", "Mod Rate", "Mod Depth", "Algorithm", "Freeze", "Dry/Wet", "Output" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    Reverb() {
        set(Decay, 0.55f); set(HFDamp, 0.5f); set(PreDelay, 0.1f); set(Size, 0.6f); set(Diffusion, 0.6f);
        set(LowCut, 0.0f); set(HighCut, 0.85f); set(Width, 0.6f); set(ModRate, 0.3f); set(ModDepth, 0.25f);
        set(Algorithm, 0.0f); set(Freeze, 0.0f); set(DryWet, 0.3f); set(Output, 0.5f);
        setSampleRate(44100.0, 0);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr float kMaxSize = 1.6f;

    static constexpr float kSpanGlide = 0.002f;   // per-sample delay-length smoothing (~11 ms @ 44.1k)

    static float clamp01(const std::atomic<float>& a) { return std::clamp(a.load(std::memory_order_relaxed), 0.0f, 1.0f); }
    static float san(float x) { return std::isfinite(x) ? x : 0.0f; }
    // Transparent below |x| = 1.2, then a smooth tanh knee bounded to ±2.2 — slope-continuous
    // at the threshold so a runaway tail rounds off gracefully instead of clipping metallically.
    static float softClip(float x) {
        if (x >  1.2f) return  1.2f + std::tanh(x - 1.2f);
        if (x < -1.2f) return -1.2f + std::tanh(x + 1.2f);
        return x;
    }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }

    // Damped comb on a fractional delay line (variable length + modulation).
    struct Comb {
        std::vector<float> buf; int size = 1, w = 0;
        float store = 0.0f, fb = 0.7f, damp1 = 0.5f, damp2 = 0.5f;
        void init(int n) { size = std::max(4, n); buf.assign(size, 0.0f); w = 0; store = 0.0f; }
        void set(float f, float d1, float d2) { fb = f; damp1 = d1; damp2 = d2; }
        float process(float in, float delay) {
            // Reject non-finite input so a NaN upstream can't seed the loop.
            if (!std::isfinite(in)) in = 0.0f;
            float rp = w - std::clamp(delay, 1.0f, (float)size - 2.0f);
            if (rp < 0) rp += size;
            int i0 = (int)rp; float frac = rp - i0; int i1 = i0 + 1; if (i1 >= size) i1 -= size;
            float out = buf[i0] + (buf[i1] - buf[i0]) * frac;
            store = out * damp2 + store * damp1;
            // Never let a NaN/Inf latch into the recirculating loop (would ring forever, hot).
            if (!std::isfinite(store)) store = 0.0f;
            float written = in + store * fb;
            if (!std::isfinite(written)) written = 0.0f;   // belt-and-suspenders: NaN never enters the delay line
            buf[w] = written;
            if (++w >= size) w = 0;
            return std::isfinite(out) ? out : 0.0f;   // guard the read too
        }
    };
    struct Allpass {
        std::vector<float> buf; int size = 1, idx = 0; float g = 0.5f;
        void init(int n) { size = std::max(2, n); buf.assign(size, 0.0f); idx = 0; }
        float process(float in) {
            if (!std::isfinite(in)) in = 0.0f;
            const float bufout = std::isfinite(buf[idx]) ? buf[idx] : 0.0f;
            const float out = -in + bufout;
            float written = in + bufout * g;
            if (!std::isfinite(written)) written = 0.0f;   // never seed the line with NaN
            buf[idx] = written;
            if (++idx >= size) idx = 0;
            return std::isfinite(out) ? out : 0.0f;
        }
    };

    static constexpr int kCombs = 8, kAllpasses = 4, kStereoSpread = 23;
    static constexpr int kCombTuning[kCombs] = {1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617};
    static constexpr int kAllpassTuning[kAllpasses] = {556, 441, 341, 225};
    // Hall (big), Room (small), Plate (bright/dense), Chamber (mid).
    static constexpr float kAlgoScale[4] = {1.0f, 0.62f, 0.82f, 0.9f};

    double sr_ = 44100.0, srScale_ = 1.0, modPhase_ = 0.0;
    float  span_ = -1.0f;   // smoothed size*algoScale; <0 = prime to target on first block
    Comb combL_[kCombs], combR_[kCombs];
    Allpass allpL_[kAllpasses], allpR_[kAllpasses];
    std::vector<float> preL_, preR_; int preW_ = 0;
    float hpL_ = 0, hpR_ = 0, hpPrevL_ = 0, hpPrevR_ = 0, lpL_ = 0, lpR_ = 0;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
