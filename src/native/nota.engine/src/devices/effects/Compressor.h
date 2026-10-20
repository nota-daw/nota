// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in compressor ("Nota Compressor"). The detector key filters run on the shared dsp::SvfFilter core;
// per-sample dB mapping uses FastMath (linToDb/dbToLin); the key-ring index
// is block-decimated (was an atomic store per sample); silent stretches
// auto-sleep via isIdle() (look-ahead line advances through zeros).
// Params and defaults unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/SvfFilter.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Compressor : public Device {
  public:
    enum {
        Threshold = 0,
        Ratio,
        Attack,
        Release,
        Makeup,
        Knee,
        Mix,
        Lookahead,
        Detection,
        AutoRelease,
        AutoGain,
        Range,
        Character,
        ScHP,
        ScLP,
        ScListen,
        // ---- appended for the almanac update — keep the order, append only ----
        ScGain,
        Hold,
        ScQ,
        External,
        StereoLink,
        kNumParams
    };

    // Scope telemetry layout (read by the card and the MCP read_dynamics tool). After the
    // kScope values come kEnvN input-level points, kEnvN gain-reduction points (dB, one per
    // kEnvMs, oldest→newest) and kKeyN samples of the unfiltered key (oldest→newest).
    enum {
        S_InPk = 0,
        S_OutPk,
        S_InRms,
        S_OutRms,
        S_Gr,
        S_KeyPk,
        S_AtkMs,
        S_RelMs,
        S_SampleRate,
        S_Bpm,
        S_Latency,
        S_Cpu,
        S_ExtKey,
        S_EnvMs,
        kScope
    };
    static constexpr int kEnvN = 2048; // 2 s of envelope at 1 ms per point
    static constexpr int kKeyN = 2048; // key signal for the card's spectrum
    static constexpr float kEnvMs = 1.0f;

    static constexpr int kNumChars = 5; // Clean / Glue / Punch / Opto / FET

    Compressor();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: look-ahead delay plus follower ballistics — the
    // engine-visible tail. Settles after the hangover of quiet blocks.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Look-ahead delays the audio by this much; the engine's PDC lines the other tracks up.
    int32_t latencySamples() const override { return lookaheadSamples(); }

    float gainReductionDb() const override { return gr_.load(std::memory_order_relaxed); }
    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    std::string deviceText(int32_t id) const override;

    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }

    const char* displayName() const override { return "Nota Compressor"; }
    int32_t builtinKind() const override { return 1; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

  private:
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    struct Chan {
        dsp::SvfFilter hp, lp;
        float env = 0.0f, rmsE = 0.0f, grPrev = 0.0f;
        int hold = 0;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool on(int i) const { return get(i) >= 0.5f; }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    int charIndex() const { return std::clamp(static_cast<int>(std::lround(getParam(Character))), 0, kNumChars - 1); }
    int detectionMode() const { return std::clamp(static_cast<int>(std::lround(getParam(Detection))), 0, 2); }
    int lookaheadSamples() const;
    static float dbToLin(float db);
    static float decayPeak(float held, float now) { return now > held ? now : held * 0.86f; }

    double sr_ = 44100.0, spb_ = 0.0;
    Chan ch_[2];
    std::vector<float> laBuf_;
    int laSize_ = 0, laW_ = 0;
    double inMs_ = 0.0, outMs_ = 0.0, cpuS_ = 0.0;
    int envDecim_ = 44, envCnt_ = 0;
    float envIn_ = 0.0f, envGr_ = 0.0f;
    float envRing_[kEnvN] = {}, grRing_[kEnvN] = {}, keyRing_[kKeyN] = {};
    std::atomic<int> envW_{0}, keyW_{0};
    std::atomic<float> gr_{0.0f};
    std::atomic<float> inPkA_{0}, outPkA_{0}, keyPkA_{0}, inRmsA_{0}, outRmsA_{0}, extA_{0}, atkA_{0}, relA_{0},
        srA_{44100.0f}, bpmA_{0}, cpuA_{0};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    std::atomic<float> p_[kNumParams] = {};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
