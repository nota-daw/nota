// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in reverb ("Nota Reverb" — almanac update) on the Chamber frame — an extended
// Freeverb (Schroeder-Moorer): 8 damped comb filters + 4 allpasses per channel on a
// variable-size, modulated fractional delay line, fed through a pre-delay line and a short
// input diffuser. SPACE (pre-delay · size · diffusion · early reflections) and TONE (low
// cut · high cut · width), a decay calibrated as a true RT60, HF damping, a chorus-like
// modulation on the tail (or on the early part only), four algorithms (Hall / Room /
// Plate / Chamber), Vintage (a band-limited, 12-bit early-digital colour), Freeze
// (infinite tail, input muted), Kill tail, and an output stage (dry level · dry/wet ·
// width · bass mono · wet only · output trim).
//
// The input diffuser delays the tail by a fixed group delay; Latency Comp shortens the
// pre-delay by that much so the tail starts where the pre-delay says (as far as the
// pre-delay allows — it cannot go below zero).
//
// All params are normalized 0..1 (denormalized in process()); persist/clone/automation
// are generic through the base Device. Param order is the persisted layout — APPEND ONLY.
// Buffers are allocated at max size on the message thread in setSampleRate, so process()
// never allocates. Freeverb is public-domain (Jezar at Dreampoint); the extensions &
// tuning are Nota's own.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Reverb : public Device {
public:
    static constexpr int kEr = 8;   // early-reflection taps

    enum {
        Decay = 0, HFDamp, PreDelay, Size, Diffusion, LowCut, HighCut, Width,
        ModRate, ModDepth, Algorithm, Freeze, DryWet, Output,
        // ---- appended for the almanac update — keep the order, append only ----
        DryLevel, EarlyRefl, ModOnTail, Vintage, BassMono, WetOnly, LatencyComp, kNumParams
    };
    // Scope telemetry layout (read by the card).
    enum {
        S_WetPk = 0, S_OutL, S_OutR, S_Cpu, S_SampleRate, S_Latency, S_Rt60, S_PreMs,
        S_DiffuseSmp, S_Frozen, S_EarlyOn, kScope
    };
    // deviceAction ids.
    enum { A_KillTail = 0 };

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
        // Pre-delay (200 ms) + the furthest early reflection (~110 ms) + modulation headroom.
        preL_.assign(static_cast<int>(sr_ * 0.34) + 4, 0.0f);
        preR_.assign(preL_.size(), 0.0f);
        preW_ = 0;
        for (int k = 0; k < kInAp; ++k) inAp_[k].resize(static_cast<int>(sr_ * kInApSec[k]));
        hpL_ = hpR_ = hpPrevL_ = hpPrevR_ = lpL_ = lpR_ = vlp_ = monoS_ = 0.0f;
        srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        if (frames <= 0 || preL_.size() < 4) return;
        const auto t0 = std::chrono::steady_clock::now();
        if (kill_.exchange(false, std::memory_order_relaxed)) wipe();

        const bool freeze  = on(Freeze);
        const bool early   = on(EarlyRefl) && !freeze;
        const bool modTail = on(ModOnTail);
        const bool vintage = on(Vintage);
        const bool wetOnly = on(WetOnly);

        const double rt60 = expMap(clamp01(Decay), 0.2, 12.0);
        const float damp  = clamp01(HFDamp);
        const float preMs = clamp01(PreDelay) * 200.0f;
        const float sizeV = clamp01(Size);
        const float size  = 0.55f + sizeV * (kMaxSize - 0.55f);
        const float diffV = clamp01(Diffusion);
        const float diff  = 0.4f + diffV * 0.35f;   // output allpass gain
        const float width = clamp01(Width);
        const float modHz = static_cast<float>(expMap(clamp01(ModRate), 0.05, 5.0));
        const float modD  = clamp01(ModDepth) * (vintage ? 18.0f : 12.0f) * static_cast<float>(srScale_);   // samples
        const int   algo  = static_cast<int>(std::lround(clamp01(Algorithm) * 3.0f));
        const float mix   = wetOnly ? 1.0f : clamp01(DryWet);
        const float dryG  = wetOnly ? 0.0f : (1.0f - mix) * dryGain(clamp01(DryLevel));
        const float outG  = clamp01(Output) * 2.0f;

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
        const float vintC  = onePole(7000.0);
        const float monoC  = onePole(expMap(clamp01(BassMono), 30.0, 500.0));
        const bool  monoOn = clamp01(BassMono) > 0.001f;

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
            combL_[c].set(fb, damp1, damp2); combR_[c].set(fb, damp1, damp2);
        }
        for (int a = 0; a < kAllpasses; ++a) { allpL_[a].g = diff; allpR_[a].g = diff; }

        // Pre-delay, less the input diffuser's group delay when compensated.
        int diffSmp = 0; for (const auto& a : inAp_) diffSmp += a.n;
        const int preLen = static_cast<int>(preL_.size());
        const double preSmp = preMs * 0.001 * sr_;
        const double effPre = std::max(0.0, preSmp - (on(LatencyComp) ? diffSmp : 0));

        // Early reflections: taps on the pre-delay line after the pre-delay, spaced by the
        // algorithm and the size; even taps lean left, odd ones right.
        const double erScale = kErScale[algo] * (0.5 + sizeV * 0.8);
        for (int k = 0; k < kEr; ++k)
            erSmp_[k] = std::min(kErMs[k] * erScale * 0.001 * sr_, 0.11 * sr_);

        const double modInc = modHz / sr_;
        float wetPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;

        for (int32_t i = 0; i < frames; ++i) {
            const float l = san(buf[i * 2]), r = san(buf[i * 2 + 1]);
            span_ += (spanTarget - span_) * kSpanGlide;

            // Write first so a zero pre-delay reads back the current sample (passthrough).
            preL_[preW_] = l; preR_[preW_] = r;

            const float lfo = std::sin(static_cast<float>(kTwoPi * modPhase_));
            modPhase_ += modInc; if (modPhase_ >= 1.0) modPhase_ -= 1.0;
            // Mod on tail: the comb lines wobble. Off: the early part (pre-delay read) wobbles
            // instead and the tail stays still — never reading ahead of the write head.
            const float combMod = modTail ? lfo * modD : 0.0f;
            const double preMod = modTail ? 0.0 : (lfo * 0.5f + 0.5f) * modD;

            const float pl = readPre(preL_, effPre + preMod), prr = readPre(preR_, effPre + preMod);

            float input = (pl + prr) * inGain;
            if (vintage) { vlp_ = san(vlp_ + vintC * (input - vlp_)); input = vlp_; }
            for (auto& a : inAp_) input = a.tick(input, inApG);

            float outL = 0.0f, outR = 0.0f;
            for (int c = 0; c < kCombs; ++c) {
                const float dl = kCombTuning[c] * static_cast<float>(srScale_) * span_;
                outL += combL_[c].process(input, dl + combMod);
                outR += combR_[c].process(input, dl - combMod);
            }
            for (int a = 0; a < kAllpasses; ++a) { outL = allpL_[a].process(outL); outR = allpR_[a].process(outR); }

            if (early) {
                float eL = 0.0f, eR = 0.0f;
                for (int k = 0; k < kEr; ++k) {
                    const double d = effPre + preMod + erSmp_[k];
                    const float s = (readPre(preL_, d) + readPre(preR_, d)) * 0.5f * kErGain[k] * kErLevel;
                    if (k & 1) { eR += s; eL += s * 0.35f; } else { eL += s; eR += s * 0.35f; }
                }
                outL += eL; outR += eR;
            }
            if (++preW_ >= preLen) preW_ = 0;

            // Tone filters on the wet signal.
            hpL_ = san(hpCoef * (hpL_ + outL - hpPrevL_)); hpPrevL_ = san(outL); outL = hpL_;
            hpR_ = san(hpCoef * (hpR_ + outR - hpPrevR_)); hpPrevR_ = san(outR); outR = hpR_;
            lpL_ = san(lpL_ + lpCoef * (outL - lpL_)); outL = lpL_;
            lpR_ = san(lpR_ + lpCoef * (outR - lpR_)); outR = lpR_;

            // Stereo width (mid/side), then bass mono: high-pass the side.
            const float mid = (outL + outR) * 0.5f;
            float side = (outL - outR) * 0.5f * (0.2f + width * 1.8f);
            if (monoOn) { monoS_ = san(monoS_ + monoC * (side - monoS_)); side -= monoS_; }
            outL = mid + side; outR = mid - side;

            // Final guard: a runaway (but finite) loop build-up here would blast the
            // speakers. A hard clamp bounds it but the clip itself sounds metallic, so
            // soft-saturate the wet contribution instead: transparent at normal levels,
            // a smooth tanh knee only when the tail overshoots (dry path stays clean).
            float wl = softClip(san(outL * mix)), wr = softClip(san(outR * mix));
            if (vintage) { wl = std::round(wl * 2048.0f) / 2048.0f; wr = std::round(wr * 2048.0f) / 2048.0f; }
            const float oL = (wl + l * dryG) * outG, oR = (wr + r * dryG) * outG;
            buf[i * 2] = oL; buf[i * 2 + 1] = oR;

            wetPk = std::max(wetPk, std::max(std::fabs(wl), std::fabs(wr)));
            outPkL = std::max(outPkL, std::fabs(oL)); outPkR = std::max(outPkR, std::fabs(oR));
        }

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

    // ---- identity / params -------------------------------------------------------
    const char* displayName() const override { return "Nota Reverb"; }
    int32_t     builtinKind() const override { return 2; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Decay", "HF Damp", "Pre-Delay", "Size", "Diffusion", "Low Cut",
            "High Cut", "Width", "Mod Rate", "Mod Depth", "Algorithm", "Freeze", "Dry/Wet", "Output",
            "Dry Level", "Early Refl", "Mod on Tail", "Vintage", "Bass Mono", "Wet Only", "Latency Comp" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Nothing is added to the dry path — the wet is the effect itself.
    int32_t latencySamples() const override { return 0; }

    void deviceAction(int32_t id, int32_t, float) override {
        if (id == A_KillTail) kill_.store(true, std::memory_order_relaxed);
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
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

    // id 0 — a one-line summary of what the reverb is doing now (the card's status strip,
    // and get_device_text over MCP).
    std::string deviceText(int32_t id) const override {
        if (id != 0) return {};
        static const char* algo[4] = { "Hall", "Room", "Plate", "Chamber" };
        const int a = static_cast<int>(std::lround(std::clamp(getParam(Algorithm), 0.0f, 1.0f) * 3.0f));
        char b[256];
        if (getParam(Freeze) >= 0.5f) {
            std::snprintf(b, sizeof b, "Freeze: %s tail held, input muted - damp %.0f %% - mix %.0f %%",
                          algo[a], getParam(HFDamp) * 100.0f, getParam(DryWet) * 100.0f);
        } else {
            std::snprintf(b, sizeof b, "%s - RT60 %.2f s - pre %.0f ms - diffuse %.0f %% - mix %.0f %%%s%s",
                          algo[a], expMap(getParam(Decay), 0.2, 12.0), getParam(PreDelay) * 200.0f,
                          getParam(Diffusion) * 100.0f, getParam(WetOnly) >= 0.5f ? 100.0f : getParam(DryWet) * 100.0f,
                          getParam(EarlyRefl) >= 0.5f ? " - early reflections" : "",
                          getParam(Vintage) >= 0.5f ? " - vintage" : "");
        }
        return std::string(b);
    }

    Reverb() {
        set(Decay, 0.55f); set(HFDamp, 0.5f); set(PreDelay, 0.1f); set(Size, 0.6f); set(Diffusion, 0.6f);
        set(LowCut, 0.0f); set(HighCut, 0.85f); set(Width, 0.6f); set(ModRate, 0.3f); set(ModDepth, 0.25f);
        set(Algorithm, 0.0f); set(Freeze, 0.0f); set(DryWet, 0.3f); set(Output, 0.5f);
        set(DryLevel, 0.70711f); set(EarlyRefl, 1.0f); set(ModOnTail, 1.0f); set(Vintage, 0.0f);
        set(BassMono, 0.0f); set(WetOnly, 0.0f); set(LatencyComp, 1.0f);
        setSampleRate(44100.0, 0);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr float kMaxSize = 1.6f;

    static constexpr float kSpanGlide = 0.002f;   // per-sample delay-length smoothing (~11 ms @ 44.1k)

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool on(int i) const { return get(i) >= 0.5f; }
    float clamp01(int i) const { return std::clamp(get(i), 0.0f, 1.0f); }
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
    // Dry trim: 0 dB at 1/√2, up to +6 dB at the top, silent at 0.
    static float dryGain(float v) { return 2.0f * v * v; }
    static float decayPeak(float held, float now) { return now > held ? now : held * 0.82f; }
    float onePole(double hz) const { return static_cast<float>(1.0 - std::exp(-kTwoPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_)); }

    // Fractional read `delay` samples behind the pre-delay write head (0 = the sample just written).
    float readPre(const std::vector<float>& b, double delay) const {
        const int n = static_cast<int>(b.size());
        double rp = preW_ - std::clamp(delay, 0.0, static_cast<double>(n) - 2.0);
        if (rp < 0) rp += n;
        const int i0 = static_cast<int>(rp); const float frac = static_cast<float>(rp - i0);
        int i1 = i0 + 1; if (i1 >= n) i1 -= n;
        return b[i0] + (b[i1] - b[i0]) * frac;
    }

    void wipe() {
        for (int c = 0; c < kCombs; ++c) { combL_[c].wipe(); combR_[c].wipe(); }
        for (int a = 0; a < kAllpasses; ++a) { allpL_[a].wipe(); allpR_[a].wipe(); }
        for (auto& a : inAp_) a.wipe();
        std::fill(preL_.begin(), preL_.end(), 0.0f);
        std::fill(preR_.begin(), preR_.end(), 0.0f);
        hpL_ = hpR_ = hpPrevL_ = hpPrevR_ = lpL_ = lpR_ = vlp_ = monoS_ = 0.0f;
    }

    // Damped comb on a fractional delay line (variable length + modulation).
    struct Comb {
        std::vector<float> buf; int size = 1, w = 0;
        float store = 0.0f, fb = 0.7f, damp1 = 0.5f, damp2 = 0.5f;
        void init(int n) { size = std::max(4, n); buf.assign(size, 0.0f); w = 0; store = 0.0f; }
        void wipe() { std::fill(buf.begin(), buf.end(), 0.0f); store = 0.0f; }
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
        void wipe() { std::fill(buf.begin(), buf.end(), 0.0f); }
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
    // A Schroeder allpass for the input diffuser (lossless; its length is its group delay).
    struct DiffAp {
        std::vector<float> z; int n = 1, w = 0;
        void resize(int len) { n = std::max(1, len); z.assign(n, 0.0f); w = 0; }
        void wipe() { std::fill(z.begin(), z.end(), 0.0f); }
        float tick(float x, float g) {
            const float d = z[w];
            float v = x + g * d;
            if (!std::isfinite(v)) v = 0.0f;
            z[w] = v; if (++w >= n) w = 0;
            return d - g * v;
        }
    };

    static constexpr int kCombs = 8, kAllpasses = 4, kStereoSpread = 23, kInAp = 4;
    static constexpr int kCombTuning[kCombs] = {1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617};
    static constexpr int kAllpassTuning[kAllpasses] = {556, 441, 341, 225};
    static constexpr double kInApSec[kInAp] = {0.00083, 0.00121, 0.00211, 0.00297};   // ≈ 7.1 ms in all
    // Hall (big), Room (small), Plate (bright/dense), Chamber (mid).
    static constexpr float kAlgoScale[4] = {1.0f, 0.62f, 0.82f, 0.9f};
    // Early-reflection pattern (ms at scale 1) and how the algorithm stretches it: a hall's
    // walls are far apart, a plate's "reflections" are a tight cluster.
    static constexpr double kErMs[kEr] = {7.3, 11.9, 16.1, 23.4, 29.7, 37.9, 47.3, 61.1};
    static constexpr float  kErGain[kEr] = {1.0f, 0.78f, 0.86f, 0.62f, 0.66f, 0.5f, 0.42f, 0.34f};
    static constexpr double kErScale[4] = {1.0, 0.45, 0.28, 0.7};
    static constexpr float  kErLevel = 0.32f;

    double sr_ = 44100.0, srScale_ = 1.0, modPhase_ = 0.0;
    float  span_ = -1.0f;   // smoothed size*algoScale; <0 = prime to target on first block
    Comb combL_[kCombs], combR_[kCombs];
    Allpass allpL_[kAllpasses], allpR_[kAllpasses];
    DiffAp inAp_[kInAp];
    std::vector<float> preL_, preR_; int preW_ = 0;
    double erSmp_[kEr] = {};
    float hpL_ = 0, hpR_ = 0, hpPrevL_ = 0, hpPrevR_ = 0, lpL_ = 0, lpR_ = 0, vlp_ = 0, monoS_ = 0;
    double cpuS_ = 0.0;
    std::atomic<bool> kill_{false};
    std::atomic<float> mWet_{0}, mOutL_{0}, mOutR_{0}, cpuA_{0}, srA_{44100.0f}, rt60A_{0}, preA_{0}, diffA_{0}, erOnA_{0};
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
