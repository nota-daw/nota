// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in stereo delay ("Nota Delay"). The main lines run on the shared dsp::DelayLine (power-of-two bitmask wrap,
// Hermite-4p reads); the WOW LFO uses fast_math::sin, tape saturation uses
// tanhPrecise with a hoisted inverse drive gain; silent loops auto-sleep via
// isIdle() (frozen loops excluded — Freeze never sleeps). Params unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/DelayLine.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Delay : public Device {
  public:
    enum {
        SyncMode = 0,
        TimeL,
        TimeR,
        DivL,
        DivR,
        LinkLR,
        Feedback,
        Spread,
        PingPong,
        WowRate,
        WowDepth,
        Freeze,
        DryWet,
        Output,
        // ---- appended for the almanac update — keep the order, append only ----
        DryLevel,
        Diffuse,
        LowCut,
        HighCut,
        TapeMode,
        FadeChange,
        Width,
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
        S_Bpm,
        S_DiffuseSmp,
        S_Frozen,
        S_MsL,
        S_MsR,
        S_Latency,
        kScope
    };
    // deviceAction ids.
    enum { A_ClearLoop = 0 };

    static constexpr float kMaxTimeMs = 2000.0f;
    static constexpr int kNumDiv = 8;
    // Division lengths in beats (quarter = 1): 1/16, 1/8T, 1/8, 1/8·, 1/4T, 1/4, 1/4·, 1/2.
    static constexpr double kDivBeats[kNumDiv] = {0.25, 1.0 / 3.0, 0.5, 0.75, 2.0 / 3.0, 1.0, 1.5, 2.0};

    Delay();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: recirculating delay lines — the engine-visible tail.
    // A frozen loop never sleeps (infinite tail). Settles after the hangover.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Delay"; }
    int32_t builtinKind() const override { return 3; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // Nothing is added to the dry path — the wet is the effect itself.
    int32_t latencySamples() const override { return 0; }

    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    // A Schroeder allpass: lossless, so a frozen loop neither grows nor decays.
    struct AllPass {
        std::vector<float> z;
        int n = 0, w = 0;
        void resize(int len) {
            n = std::max(1, len);
            z.assign(n, 0.0f);
            w = 0;
        }
        void wipe() { std::fill(z.begin(), z.end(), 0.0f); }
        float tick(float x, float g) {
            const float d = z[w];
            const float v = x + g * d;
            z[w] = v;
            if (++w >= n) w = 0;
            return d - g * v;
        }
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool on(int i) const { return get(i) >= 0.5f; }
    float clamp01(int i) const { return std::clamp(get(i), 0.0f, 1.0f); }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    // Dry trim: 0 dB at 1/√2, up to +6 dB at the top, silent at 0.
    static float dryGain(float v) { return 2.0f * v * v; }
    static float decayPeak(float held, float now) { return now > held ? now : held * 0.82f; }
    float onePole(double hz) const {
        return static_cast<float>(1.0 - std::exp(-kTwoPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_));
    }

    int divIndex(int p) const {
        return std::clamp(
            static_cast<int>(std::lround(std::clamp(getParam(p), 0.0f, 1.0f) * (kNumDiv - 1))), 0, kNumDiv - 1);
    }

    double delaySamples(int timeParam, int divParam, bool sync) const;
    void wipe();

    double sr_ = 44100.0, spb_ = 0.0, wowPhase_ = 0.0;
    double curL_ = 0.0, curR_ = 0.0, oldL_ = 0.0, oldR_ = 0.0;
    int fadeLen_ = 1024, fadePos_ = 1024;
    dsp::DelayLine bufL_, bufR_;
    AllPass ap_[4];
    float hpL_ = 0, hpR_ = 0, lpL_ = 0, lpR_ = 0, tapeL_ = 0, tapeR_ = 0, monoL_ = 0, monoR_ = 0;
    double cpuS_ = 0.0;

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float loopPk_ = 0.0f; // peak loop-feed push (blocks sleep while the tail rings)

    std::atomic<bool> clear_{false};
    std::atomic<float> mWet_{0}, mOutL_{0}, mOutR_{0}, cpuA_{0}, srA_{44100.0f}, bpmA_{0}, diffA_{0}, msL_{0}, msR_{0};
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
