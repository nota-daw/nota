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
// The per-sample modulation LFO uses fast_math::sin, silent tails
// auto-sleep via isIdle() (frozen tails excluded — Freeze never sleeps). Params unchanged.
//
// All params are normalized 0..1 (denormalized in process()); persist/clone/automation
// are generic through the base Device. Param order is the persisted layout — APPEND ONLY.
// Buffers are allocated at max size on the message thread in setSampleRate, so process()
// never allocates. Freeverb is public-domain (Jezar at Dreampoint); the extensions &
// tuning are Nota's own.

#pragma once

#include "devices/Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <string>
#include <vector>

namespace nota {

class Reverb : public Device {
  public:
    static constexpr int kEr = 8; // early-reflection taps

    enum {
        Decay = 0,
        HFDamp,
        PreDelay,
        Size,
        Diffusion,
        LowCut,
        HighCut,
        Width,
        ModRate,
        ModDepth,
        Algorithm,
        Freeze,
        DryWet,
        Output,
        // ---- appended for the almanac update — keep the order, append only ----
        DryLevel,
        EarlyRefl,
        ModOnTail,
        Vintage,
        BassMono,
        WetOnly,
        LatencyComp,
        kNumParams
    };
    // Scope telemetry layout (read by the card).
    enum {
        S_WetPk = 0,
        S_OutL,
        S_OutR,
        S_Cpu,
        S_SampleRate,
        S_Latency,
        S_Rt60,
        S_PreMs,
        S_DiffuseSmp,
        S_Frozen,
        S_EarlyOn,
        kScope
    };
    // deviceAction ids.
    enum { A_KillTail = 0 };

    Reverb();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: recirculating comb/allpass tail — the engine-visible tail.
    // A frozen tail never sleeps (infinite tail). Settles after the hangover.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // ---- identity / params -------------------------------------------------------
    const char* displayName() const override { return "Nota Reverb"; }
    int32_t builtinKind() const override { return 2; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // Regression self-test for the comb read wrap (nota_engine_reverb_wrap_selftest): put the
    // read position a hair past the write head — the case that used to read buf[size] — and
    // check the comb reads its own first sample. Device-free, no allocation beyond the comb.
    static bool combWrapSelfTest();

    // Nothing is added to the dry path — the wet is the effect itself.
    int32_t latencySamples() const override { return 0; }

    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    // id 0 — a one-line summary of what the reverb is doing now (the card's status strip,
    // and get_device_text over MCP).
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr float kMaxSize = 1.6f;

    static constexpr float kSpanGlide = 0.002f;     // per-sample delay-length smoothing (~11 ms @ 44.1k)
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool on(int i) const { return get(i) >= 0.5f; }
    float clamp01(int i) const { return std::clamp(get(i), 0.0f, 1.0f); }
    static float san(float x) { return std::isfinite(x) ? x : 0.0f; }
    // Transparent below |x| = 1.2, then a smooth tanh knee bounded to ±2.2 — slope-continuous
    // at the threshold so a runaway tail rounds off gracefully instead of clipping metallically.
    static float softClip(float x) {
        if (x > 1.2f) return 1.2f + std::tanh(x - 1.2f);
        if (x < -1.2f) return -1.2f + std::tanh(x + 1.2f);
        return x;
    }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    // Dry trim: 0 dB at 1/√2, up to +6 dB at the top, silent at 0.
    static float dryGain(float v) { return 2.0f * v * v; }
    static float decayPeak(float held, float now) { return now > held ? now : held * 0.82f; }
    float onePole(double hz) const {
        return static_cast<float>(1.0 - std::exp(-kTwoPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_));
    }

    // Fractional read `delay` samples behind the pre-delay write head (0 = the sample just written).
    float readPre(const std::vector<float>& b, double delay) const;

    void wipe();

    // Damped comb on a fractional delay line (variable length + modulation).
    struct Comb {
        std::vector<float> buf;
        int size = 1, w = 0;
        float store = 0.0f, fb = 0.7f, damp1 = 0.5f, damp2 = 0.5f;
        void init(int n) {
            size = std::max(4, n);
            buf.assign(size, 0.0f);
            w = 0;
            store = 0.0f;
        }
        void wipe() {
            std::fill(buf.begin(), buf.end(), 0.0f);
            store = 0.0f;
        }
        void set(float f, float d1, float d2) {
            fb = f;
            damp1 = d1;
            damp2 = d2;
        }
        float process(float in, float delay) {
            // Reject non-finite input so a NaN upstream can't seed the loop.
            if (!std::isfinite(in)) in = 0.0f;
            float rp = w - std::clamp(delay, 1.0f, (float)size - 2.0f);
            if (rp < 0) rp += size;
            int i0 = (int)rp;
            float frac = rp - i0;
            // A read position a hair past the write head (w - delay ≈ -1e-5) wraps to
            // size - 1e-5, which rounds to exactly `size` in float: buf[size] is one past the
            // end. That stray heap word (often a huge finite float) recirculated as the loud
            // looped "metallic burst". Keep the index in range.
            if (i0 >= size) i0 -= size;
            int i1 = i0 + 1;
            if (i1 >= size) i1 -= size;
            float out = buf[i0] + (buf[i1] - buf[i0]) * frac;
            store = out * damp2 + store * damp1;
            // Never let a NaN/Inf latch into the recirculating loop (would ring forever, hot).
            if (!std::isfinite(store)) store = 0.0f;
            float written = in + store * fb;
            if (!std::isfinite(written)) written = 0.0f; // belt-and-suspenders: NaN never enters the delay line
            buf[w] = written;
            if (++w >= size) w = 0;
            return std::isfinite(out) ? out : 0.0f; // guard the read too
        }
    };
    struct Allpass {
        std::vector<float> buf;
        int size = 1, idx = 0;
        float g = 0.5f;
        void init(int n) {
            size = std::max(2, n);
            buf.assign(size, 0.0f);
            idx = 0;
        }
        void wipe() { std::fill(buf.begin(), buf.end(), 0.0f); }
        float process(float in) {
            if (!std::isfinite(in)) in = 0.0f;
            const float bufout = std::isfinite(buf[idx]) ? buf[idx] : 0.0f;
            const float out = -in + bufout;
            float written = in + bufout * g;
            if (!std::isfinite(written)) written = 0.0f; // never seed the line with NaN
            buf[idx] = written;
            if (++idx >= size) idx = 0;
            return std::isfinite(out) ? out : 0.0f;
        }
    };
    // A Schroeder allpass for the input diffuser (lossless; its length is its group delay).
    struct DiffAp {
        std::vector<float> z;
        int n = 1, w = 0;
        void resize(int len) {
            n = std::max(1, len);
            z.assign(n, 0.0f);
            w = 0;
        }
        void wipe() { std::fill(z.begin(), z.end(), 0.0f); }
        float tick(float x, float g) {
            const float d = z[w];
            float v = x + g * d;
            if (!std::isfinite(v)) v = 0.0f;
            z[w] = v;
            if (++w >= n) w = 0;
            return d - g * v;
        }
    };

    static constexpr int kCombs = 8, kAllpasses = 4, kStereoSpread = 23, kInAp = 4;
    static constexpr int kCombTuning[kCombs] = {1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617};
    static constexpr int kAllpassTuning[kAllpasses] = {556, 441, 341, 225};
    static constexpr double kInApSec[kInAp] = {0.00083, 0.00121, 0.00211, 0.00297}; // ≈ 7.1 ms in all
    // Hall (big), Room (small), Plate (bright/dense), Chamber (mid).
    static constexpr float kAlgoScale[4] = {1.0f, 0.62f, 0.82f, 0.9f};
    // Early-reflection pattern (ms at scale 1) and how the algorithm stretches it: a hall's
    // walls are far apart, a plate's "reflections" are a tight cluster.
    static constexpr double kErMs[kEr] = {7.3, 11.9, 16.1, 23.4, 29.7, 37.9, 47.3, 61.1};
    static constexpr float kErGain[kEr] = {1.0f, 0.78f, 0.86f, 0.62f, 0.66f, 0.5f, 0.42f, 0.34f};
    static constexpr double kErScale[4] = {1.0, 0.45, 0.28, 0.7};
    static constexpr float kErLevel = 0.32f;

    double sr_ = 44100.0, srScale_ = 1.0, modPhase_ = 0.0;
    float span_ = -1.0f; // smoothed size*algoScale; <0 = prime to target on first block
    Comb combL_[kCombs], combR_[kCombs];
    Allpass allpL_[kAllpasses], allpR_[kAllpasses];
    DiffAp inAp_[kInAp];
    std::vector<float> preL_, preR_;
    int preW_ = 0;
    double erSmp_[kEr] = {};
    float hpL_ = 0, hpR_ = 0, hpPrevL_ = 0, hpPrevR_ = 0, lpL_ = 0, lpR_ = 0, vlp_ = 0, monoS_ = 0;
    double cpuS_ = 0.0;

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak tank feed (blocks sleep while the tail rings)

    std::atomic<bool> kill_{false};
    std::atomic<float> mWet_{0}, mOutL_{0}, mOutR_{0}, cpuA_{0}, srA_{44100.0f}, rt60A_{0}, preA_{0}, diffA_{0},
        erOnA_{0};
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
