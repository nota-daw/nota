// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Reverb implementation (almanac update). The per-sample
// modulation LFO uses fast_math::sin; silent tails auto-sleep (frozen tails
// excluded — Freeze never sleeps).

#include "Reverb.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>

namespace nota {

Reverb::Reverb() {
    set(Decay, 0.55f);
    set(HFDamp, 0.5f);
    set(PreDelay, 0.1f);
    set(Size, 0.6f);
    set(Diffusion, 0.6f);
    set(LowCut, 0.0f);
    set(HighCut, 0.85f);
    set(Width, 0.6f);
    set(ModRate, 0.3f);
    set(ModDepth, 0.25f);
    set(Algorithm, 0.0f);
    set(Freeze, 0.0f);
    set(DryWet, 0.3f);
    set(Output, 0.5f);
    set(DryLevel, 0.70711f);
    set(EarlyRefl, 1.0f);
    set(ModOnTail, 1.0f);
    set(Vintage, 0.0f);
    set(BassMono, 0.0f);
    set(WetOnly, 0.0f);
    set(LatencyComp, 1.0f);
    setSampleRate(44100.0, 0);
}

void Reverb::setSampleRate(double sr, int32_t /*maxBlock*/) {
    sr_ = sr > 0 ? sr : 44100.0;
    const double scale = sr_ / 44100.0;
    srScale_ = scale;
    for (int c = 0; c < kCombs; ++c) {
        int maxN = static_cast<int>(kCombTuning[c] * scale * kMaxSize) + 4;
        combL_[c].init(maxN);
        combR_[c].init(maxN);
    }
    for (int a = 0; a < kAllpasses; ++a) {
        allpL_[a].init(static_cast<int>(kAllpassTuning[a] * scale) + 2);
        allpR_[a].init(static_cast<int>((kAllpassTuning[a] + kStereoSpread) * scale) + 2);
    }
    // Pre-delay (200 ms) + the furthest early reflection (~110 ms) + modulation headroom.
    preL_.assign(static_cast<int>(sr_ * 0.34) + 4, 0.0f);
    preR_.assign(preL_.size(), 0.0f);
    preW_ = 0;
    for (int k = 0; k < kInAp; ++k) inAp_[k].resize(static_cast<int>(sr_ * kInApSec[k]));
    hpL_ = hpR_ = hpPrevL_ = hpPrevR_ = lpL_ = lpR_ = vlp_ = monoS_ = 0.0f;
    quietSamples_ = 0;
    idle_ = false;
    feedPk_ = 0.0f;
    srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
}

void Reverb::wake() {
    idle_ = false;
    quietSamples_ = 0;
    feedPk_ = 0.0f;
}

void Reverb::process(float* buf, int32_t frames) {
    if (frames <= 0 || preL_.size() < 4) return;
    const auto t0 = std::chrono::steady_clock::now();
    if (kill_.exchange(false, std::memory_order_relaxed)) wipe();

    const bool freeze = on(Freeze);
    const bool early = on(EarlyRefl) && !freeze;
    const bool modTail = on(ModOnTail);
    const bool vintage = on(Vintage);
    const bool wetOnly = on(WetOnly);

    const double rt60 = expMap(clamp01(Decay), 0.2, 12.0);
    const float damp = clamp01(HFDamp);
    const float preMs = clamp01(PreDelay) * 200.0f;
    const float sizeV = clamp01(Size);
    const float size = 0.55f + sizeV * (kMaxSize - 0.55f);
    const float diffV = clamp01(Diffusion);
    const float diff = 0.4f + diffV * 0.35f; // output allpass gain
    const float width = clamp01(Width);
    const float modHz = static_cast<float>(expMap(clamp01(ModRate), 0.05, 5.0));
    const float modD = clamp01(ModDepth) * (vintage ? 18.0f : 12.0f) * static_cast<float>(srScale_); // samples
    const int algo = static_cast<int>(std::lround(clamp01(Algorithm) * 3.0f));
    const float mix = wetOnly ? 1.0f : clamp01(DryWet);
    const float dryG = wetOnly ? 0.0f : (1.0f - mix) * dryGain(clamp01(DryLevel));
    const float outG = clamp01(Output) * 2.0f;

    const float damp1 = freeze ? 0.0f : damp * 0.4f, damp2 = 1.0f - damp1;
    const float inGain = freeze ? 0.0f : 0.015f;
    const float algoScale = kAlgoScale[algo];
    // Input diffusion: denser for the plate, the algorithm with no room to speak of.
    const float inApG = std::min(0.75f, 0.25f + diffV * 0.45f + (algo == 2 ? 0.1f : 0.0f));

    // Tone: one-pole HP (low cut) + LP (high cut) coefficients on the wet path.
    const double lc = expMap(clamp01(LowCut), 20.0, 1000.0);
    const double hc = expMap(clamp01(HighCut), 1000.0, 20000.0);
    const float hpCoef = static_cast<float>(std::exp(-2.0 * kPi * lc / sr_));
    const float lpCoef = static_cast<float>(1.0 - std::exp(-2.0 * kPi * hc / sr_));
    const float vintC = onePole(7000.0);
    const float monoC = onePole(expMap(clamp01(BassMono), 30.0, 500.0));
    const bool monoOn = clamp01(BassMono) > 0.001f;

    // Comb delay length = kCombTuning[c] * srScale * (size * algoScale). Size and
    // Algorithm change per block; jumping the read pointer inside a high-feedback loop
    // injects an uncorrelated sample that recirculates as a metallic blast. Glide the
    // span toward its target per-sample so the pointer moves continuously (also gives a
    // smooth "size morph" instead of a click). Primed to target on the first ever block.
    const float spanTarget = size * algoScale;
    if (span_ < 0.0f) span_ = spanTarget;

    // Decay is an honest RT60: each comb loses 60 dB in rt60 seconds, whatever its length.
    for (int c = 0; c < kCombs; ++c) {
        const double loopSec = kCombTuning[c] * srScale_ * spanTarget / sr_;
        const float fb = freeze ? 1.0f : static_cast<float>(std::min(0.995, std::pow(10.0, -3.0 * loopSec / rt60)));
        combL_[c].set(fb, damp1, damp2);
        combR_[c].set(fb, damp1, damp2);
    }
    for (int a = 0; a < kAllpasses; ++a) {
        allpL_[a].g = diff;
        allpR_[a].g = diff;
    }

    // Pre-delay, less the input diffuser's group delay when compensated.
    int diffSmp = 0;
    for (const auto& a : inAp_) diffSmp += a.n;
    const int preLen = static_cast<int>(preL_.size());
    const double preSmp = preMs * 0.001 * sr_;
    const double effPre = std::max(0.0, preSmp - (on(LatencyComp) ? diffSmp : 0));

    // Early reflections: taps on the pre-delay line after the pre-delay, spaced by the
    // algorithm and the size; even taps lean left, odd ones right.
    const double erScale = kErScale[algo] * (0.5 + sizeV * 0.8);
    for (int k = 0; k < kEr; ++k) erSmp_[k] = std::min(kErMs[k] * erScale * 0.001 * sr_, 0.11 * sr_);

    const double modInc = modHz / sr_;

    // Idle skip: silent input on a settled tail leaves the buffer untouched. A
    // frozen tail never sleeps (infinite tail); the modulation phase keeps advancing
    // so the wake-up tail has no timing jump. Sleep engages only after the full
    // feed-through flight time (pre-delay + early taps + longest comb), tracked
    // below — output silence alone proves nothing while the lines are full.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    if (inSilent && !freeze && idle_) {
        modPhase_ += modInc * frames;
        if (modPhase_ >= 1.0) modPhase_ -= std::floor(modPhase_);
        return;
    }

    float wetPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f, feedPk = 0.0f;

    for (int32_t i = 0; i < frames; ++i) {
        const float l = san(buf[i * 2]), r = san(buf[i * 2 + 1]);
        span_ += (spanTarget - span_) * kSpanGlide;

        // Write first so a zero pre-delay reads back the current sample (passthrough).
        preL_[preW_] = l;
        preR_[preW_] = r;

        const float lfo = fast_math::sin(static_cast<float>(modPhase_));
        modPhase_ += modInc;
        if (modPhase_ >= 1.0) modPhase_ -= 1.0;
        // Mod on tail: the comb lines wobble. Off: the early part (pre-delay read) wobbles
        // instead and the tail stays still — never reading ahead of the write head.
        const float combMod = modTail ? lfo * modD : 0.0f;
        const double preMod = modTail ? 0.0 : (lfo * 0.5f + 0.5f) * modD;

        const float pl = readPre(preL_, effPre + preMod), prr = readPre(preR_, effPre + preMod);

        float input = (pl + prr) * inGain;
        if (vintage) {
            vlp_ = san(vlp_ + vintC * (input - vlp_));
            input = vlp_;
        }
        for (auto& a : inAp_) input = a.tick(input, inApG);
        feedPk = std::max(feedPk, std::fabs(input));

        float outL = 0.0f, outR = 0.0f;
        for (int c = 0; c < kCombs; ++c) {
            const float dl = kCombTuning[c] * static_cast<float>(srScale_) * span_;
            outL += combL_[c].process(input, dl + combMod);
            outR += combR_[c].process(input, dl - combMod);
        }
        for (int a = 0; a < kAllpasses; ++a) {
            outL = allpL_[a].process(outL);
            outR = allpR_[a].process(outR);
        }

        if (early) {
            float eL = 0.0f, eR = 0.0f;
            for (int k = 0; k < kEr; ++k) {
                const double d = effPre + preMod + erSmp_[k];
                const float s = (readPre(preL_, d) + readPre(preR_, d)) * 0.5f * kErGain[k] * kErLevel;
                if (k & 1) {
                    eR += s;
                    eL += s * 0.35f;
                } else {
                    eL += s;
                    eR += s * 0.35f;
                }
            }
            outL += eL;
            outR += eR;
        }
        if (++preW_ >= preLen) preW_ = 0;

        // Tone filters on the wet signal.
        hpL_ = san(hpCoef * (hpL_ + outL - hpPrevL_));
        hpPrevL_ = san(outL);
        outL = hpL_;
        hpR_ = san(hpCoef * (hpR_ + outR - hpPrevR_));
        hpPrevR_ = san(outR);
        outR = hpR_;
        lpL_ = san(lpL_ + lpCoef * (outL - lpL_));
        outL = lpL_;
        lpR_ = san(lpR_ + lpCoef * (outR - lpR_));
        outR = lpR_;

        // Stereo width (mid/side), then bass mono: high-pass the side.
        const float mid = (outL + outR) * 0.5f;
        float side = (outL - outR) * 0.5f * (0.2f + width * 1.8f);
        if (monoOn) {
            monoS_ = san(monoS_ + monoC * (side - monoS_));
            side -= monoS_;
        }
        outL = mid + side;
        outR = mid - side;

        // Final guard: a runaway (but finite) loop build-up here would blast the
        // speakers. A hard clamp bounds it but the clip itself sounds metallic, so
        // soft-saturate the wet contribution instead: transparent at normal levels,
        // a smooth tanh knee only when the tail overshoots (dry path stays clean).
        float wl = softClip(san(outL * mix)), wr = softClip(san(outR * mix));
        if (vintage) {
            wl = std::round(wl * 2048.0f) / 2048.0f;
            wr = std::round(wr * 2048.0f) / 2048.0f;
        }
        const float oL = (wl + l * dryG) * outG, oR = (wr + r * dryG) * outG;
        buf[i * 2] = oL;
        buf[i * 2 + 1] = oR;

        wetPk = std::max(wetPk, std::max(std::fabs(wl), std::fabs(wr)));
        outPkL = std::max(outPkL, std::fabs(oL));
        outPkR = std::max(outPkR, std::fabs(oR));
    }

    // Tank-feed peak (decays slowly so a ringing tail blocks accrual); sleep
    // additionally waits out the feed-through flight time so late reflections
    // arriving after a quiet gap are never cut.
    feedPk_ = feedPk > feedPk_ ? feedPk : feedPk_ * 0.90f;
    const double flight = (double)preL_.size() + kCombTuning[kCombs - 1] * srScale_ * spanTarget + 2000.0;
    if (inSilent && !freeze && std::max(outPkL, outPkR) <= 1.0e-5f && feedPk_ <= 1.0e-5f)
        quietSamples_ += (uint64_t)frames;
    else {
        quietSamples_ = 0;
        idle_ = false;
    }
    if (quietSamples_ > (uint64_t)flight + kIdleSamples) idle_ = true;

    // ---- telemetry -----------------------------------------------------------
    mWet_.store(decayPeak(mWet_.load(std::memory_order_relaxed), wetPk), std::memory_order_relaxed);
    mOutL_.store(decayPeak(mOutL_.load(std::memory_order_relaxed), outPkL), std::memory_order_relaxed);
    mOutR_.store(decayPeak(mOutR_.load(std::memory_order_relaxed), outPkR), std::memory_order_relaxed);
    rt60A_.store(freeze ? -1.0f : static_cast<float>(rt60), std::memory_order_relaxed);
    preA_.store(static_cast<float>(effPre * 1000.0 / sr_), std::memory_order_relaxed);
    diffA_.store(static_cast<float>(diffSmp), std::memory_order_relaxed);
    erOnA_.store(early ? 1.0f : 0.0f, std::memory_order_relaxed);
    srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
    cpuA_.store(static_cast<float>(cpuS_), std::memory_order_relaxed);
}

const char* Reverb::paramName(int32_t i) const {
    static const char* nm[] = {"Decay", "HF Damp", "Pre-Delay", "Size", "Diffusion", "Low Cut", "High Cut", "Width",
        "Mod Rate", "Mod Depth", "Algorithm", "Freeze", "Dry/Wet", "Output", "Dry Level", "Early Refl", "Mod on Tail",
        "Vintage", "Bass Mono", "Wet Only", "Latency Comp"};
    return (i >= 0 && i < kNumParams) ? nm[i] : "";
}

float Reverb::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Reverb::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

bool Reverb::combWrapSelfTest() {
    int hits = 0;
    for (int size : {1000, 1500, 2000, 2500, 3000, 4000}) {
        for (int w : {2, 100, size / 2, size - 3}) {
            Comb c;
            c.init(size);
            std::fill(c.buf.begin(), c.buf.end(), 0.0f);
            c.buf[0] = 1.0f;
            c.w = w;
            c.set(0.0f, 0.0f, 1.0f);
            // Smallest float delay above w: w - delay is a tiny negative, rp + size rounds to size.
            const float delay = std::nextafter(static_cast<float>(w), 1e9f);
            float rp = w - delay;
            if (rp < 0) rp += size;
            if (static_cast<int>(rp) != size) continue; // this size/w doesn't hit the rounding case
            ++hits;
            if (c.process(0.0f, delay) != 1.0f) return false;
        }
    }
    return hits > 0; // the rounding case must actually have been exercised
}

void Reverb::deviceAction(int32_t id, int32_t, float) {
    if (id == A_KillTail) kill_.store(true, std::memory_order_relaxed);
    wake();
}

int32_t Reverb::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples < kScope) return 0;
    out[S_WetPk] = mWet_.load(std::memory_order_relaxed);
    out[S_OutL] = mOutL_.load(std::memory_order_relaxed);
    out[S_OutR] = mOutR_.load(std::memory_order_relaxed);
    out[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    out[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    out[S_Latency] = static_cast<float>(latencySamples());
    out[S_Rt60] = rt60A_.load(std::memory_order_relaxed);
    out[S_PreMs] = preA_.load(std::memory_order_relaxed);
    out[S_DiffuseSmp] = diffA_.load(std::memory_order_relaxed);
    out[S_Frozen] = getParam(Freeze) >= 0.5f ? 1.0f : 0.0f;
    out[S_EarlyOn] = erOnA_.load(std::memory_order_relaxed);
    return kScope;
}

std::string Reverb::deviceText(int32_t id) const {
    if (id != 0) return {};
    static const char* algo[4] = {"Hall", "Room", "Plate", "Chamber"};
    const int a = static_cast<int>(std::lround(std::clamp(getParam(Algorithm), 0.0f, 1.0f) * 3.0f));
    char b[256];
    if (getParam(Freeze) >= 0.5f) {
        std::snprintf(b, sizeof b, "Freeze: %s tail held, input muted - damp %.0f %% - mix %.0f %%", algo[a],
            getParam(HFDamp) * 100.0f, getParam(DryWet) * 100.0f);
    } else {
        std::snprintf(b, sizeof b, "%s - RT60 %.2f s - pre %.0f ms - diffuse %.0f %% - mix %.0f %%%s%s", algo[a],
            expMap(getParam(Decay), 0.2, 12.0), getParam(PreDelay) * 200.0f, getParam(Diffusion) * 100.0f,
            getParam(WetOnly) >= 0.5f ? 100.0f : getParam(DryWet) * 100.0f,
            getParam(EarlyRefl) >= 0.5f ? " - early reflections" : "", getParam(Vintage) >= 0.5f ? " - vintage" : "");
    }
    return std::string(b);
}

inline float Reverb::readPre(const std::vector<float>& b, double delay) const {
    const int n = static_cast<int>(b.size());
    double rp = preW_ - std::clamp(delay, 0.0, static_cast<double>(n) - 2.0);
    if (rp < 0) rp += n;
    int i0 = static_cast<int>(rp);
    const float frac = static_cast<float>(rp - i0);
    if (i0 >= n) i0 -= n; // a hair-negative rp + n can round up to exactly n
    int i1 = i0 + 1;
    if (i1 >= n) i1 -= n;
    return b[i0] + (b[i1] - b[i0]) * frac;
}

void Reverb::wipe() {
    for (int c = 0; c < kCombs; ++c) {
        combL_[c].wipe();
        combR_[c].wipe();
    }
    for (int a = 0; a < kAllpasses; ++a) {
        allpL_[a].wipe();
        allpR_[a].wipe();
    }
    for (auto& a : inAp_) a.wipe();
    std::fill(preL_.begin(), preL_.end(), 0.0f);
    std::fill(preR_.begin(), preR_.end(), 0.0f);
    hpL_ = hpR_ = hpPrevL_ = hpPrevR_ = lpL_ = lpR_ = vlp_ = monoS_ = 0.0f;
    feedPk_ = 0.0f;
}

} // namespace nota
