// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Prism — three-band dynamics. The crossover runs on the shared dsp::SvfFilter TPT core (same shared-stage LR4
// topology and coefficients); per-sample dB mapping uses FastMath
// (linToDb/exp2f/tanhPrecise); silent stretches auto-sleep via isIdle().
// Params and defaults unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/SvfFilter.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

namespace prism {

constexpr double kPi = 3.14159265358979323846;
constexpr float kSqrt2 = 1.41421356f;

// Butterworth-Q TPT coefficients (Cytomic form, k = √2).
struct XCoe {
    float a1 = 1, a2 = 0, a3 = 0;
};
XCoe xcoef(double hz, double sr);

// One channel's band splitter. LR4 low/high share their first stage (its lp
// feeds the low LR4, its hp the high LR4); in 3-band mode the low band passes
// the second crossover's allpass so Low + Mid + High sums to a flat allpass.
// Sections run on the shared dsp::SvfFilter core (identical TPT math).
struct Splitter {
    dsp::SvfFilter a1, b1, c1, ap2, a2, b2, c2;
    void reset();
    void setXovers(const XCoe& k1, const XCoe& k2);
    void run(float x, int nb, float out[3]);
};

// Soft-knee static curve: 0 below −W/2, quadratic across the knee, linear (= u) above.
inline float knee(float u, float w) {
    if (w > 0.01f) {
        if (2.0f * u < -w) return 0.0f;
        if (2.0f * u <= w) {
            const float t = u + 0.5f * w;
            return t * t / (2.0f * w);
        }
        return u;
    }
    return u > 0.0f ? u : 0.0f;
}

} // namespace prism

class Prism final : public Device {
  public:
    static constexpr int kBands = 3;
    // Global params.
    enum {
        Amount = 0,
        Output,
        Bands,
        XoverLow,
        XoverHigh,
        Detect,
        Lookahead,
        AutoMakeup,
        SoftClip,
        ScListen,
        Solo,
        PeakHold,
        Mix,
        kGlobal
    };
    // Per-band fields (band b's field f lives at kGlobal + b * kPerBand + f).
    enum {
        AboveThresh = 0,
        AboveRatio,
        Attack,
        Release,
        Gain,
        BelowOn,
        BelowThresh,
        BelowRatio,
        BelowAttack,
        BelowRelease,
        Floor,
        Knee,
        AutoRelease,
        kPerBand
    };
    static constexpr int kNumParams = kGlobal + kBands * kPerBand; // 52
    static constexpr int P(int band, int field) { return kGlobal + band * kPerBand + field; }

    // Packed scope telemetry (lock-free, UI-polled).
    enum {
        S_Gr0 = 0,
        S_Gr1,
        S_Gr2,
        S_Boost0,
        S_Boost1,
        S_Boost2,
        S_Level0,
        S_Level1,
        S_Level2,
        S_OutL,
        S_OutR,
        S_InPeak,
        S_Cpu,
        S_SampleRate,
        S_Latency,
        S_ScActive,
        S_ClipDb,
        S_Bands,
        S_XoverLow,
        S_XoverHigh,
        kScope
    };

    static constexpr int kSpecLen = 2048; // spectrum rings (power of two)
    static constexpr int kTraceLen = 256; // detector traces

    // Musical mappings (shared with the card / presets via the comments in FactoryPresetCatalog).
    static double threshAboveDb(float v) { return -60.0 + 60.0 * v; }
    static double threshBelowDb(float v) { return -80.0 + 80.0 * v; }
    static double ratioBelow(float v);
    static double timeMs(float v, bool release);
    static double xoverHz(float v);

    Prism();
    void setSampleRate(double sr, int32_t maxBlock) override;
    int32_t latencySamples() const override;

    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }

    void process(float* buf, int32_t frames) override;

    // Sleep contract: look-ahead delay lines plus detector envelopes — the
    // engine-visible tail. Settles to true after the hangover of quiet blocks.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Prism"; }
    int32_t builtinKind() const override { return 21; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    float gainReductionDb() const override;
    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override;

  private:
    static constexpr int kCtrl = 16;                // crossover coefficient refresh (samples)
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    struct BandState {
        float ms = 0, envA = 0, envS = 0, envB = 0;
        float thrA = -18, slopeA = 0, thrB = -40, slopeB = 0, gain = 0;
        float level = -120, dyn = 0, trPk = 0;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    prism::Splitter split_[2], scSplit_;
    BandState st_[kBands];
    std::vector<float> delay_[kBands];
    int laSize_ = 2, laW_ = 0;
    float xl_ = 5.2f, xh_ = 7.8f, outS_ = 1.0f, mixS_ = 1.0f;
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    std::atomic<int32_t> scTrackId_{-1};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // telemetry
    float inRing_[kSpecLen] = {}, outRing_[kSpecLen] = {};
    uint32_t specW_ = 0;
    std::atomic<uint32_t> specWA_{0};
    float trIn_[kBands][kTraceLen] = {}, trDet_[kBands][kTraceLen] = {};
    uint32_t traceW_ = 0;
    std::atomic<uint32_t> traceWA_{0};
    int traceDiv_ = 256, traceCount_ = 0;
    std::atomic<float> gr_[kBands] = {}, boost_[kBands] = {}, level_[kBands] = {};
    std::atomic<float> mOutL_{0}, mOutR_{0}, mIn_{0}, cpuA_{0}, srA_{44100}, clipA_{0}, xlA_{180}, xhA_{2400};
    std::atomic<int32_t> bandsA_{3};
    std::atomic<bool> scActA_{false};
    double cpuS_ = 0.0;
};

} // namespace nota
