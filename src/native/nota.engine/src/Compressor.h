// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in compressor (mockup 2n) — feed-forward, stereo-linked, with a smoothed
// level follower (attack/release, optional program-dependent auto-release), a
// soft-knee gain computer, peak/RMS detection, look-ahead, a gain-reduction range
// limit, auto make-up gain, dry/wet mix, five voicing "character" models and a
// filtered side-chain detector (HP/LP) with a Listen monitor. A core Device
// (JUCE-free); params are atomic / lock-free. Existing params keep their names so
// old projects load; new ones are appended.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Compressor : public Device {
public:
    enum { Threshold = 0, Ratio, Attack, Release, Makeup,
           Knee, Mix, Lookahead, Detection, AutoRelease, AutoGain, Range, Character,
           ScHP, ScLP, ScListen, kNumParams };

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        laSize_ = static_cast<int>(sr_ * 0.011) + 2;   // up to ~10 ms look-ahead
        laBuf_.assign(static_cast<size_t>(laSize_) * 2, 0.0f); laW_ = 0;
    }

    void process(float* buf, int32_t frames) override {
        const double sr = sr_;
        const int   ch  = std::clamp((int)std::lround(p_[Character].load(std::memory_order_relaxed)), 0, 4);
        static const float chAtk[5] = {1.0f, 1.4f, 0.5f, 1.7f, 0.3f};   // Clean/Glue/Punch/Opto/FET
        static const float chRel[5] = {1.0f, 1.7f, 0.8f, 2.2f, 0.6f};
        static const float chKnee[5] = {0.0f, 6.0f, 1.0f, 8.0f, 2.0f};

        const float threshDb = p_[Threshold].load(std::memory_order_relaxed);
        const float ratio    = std::max(1.0f, p_[Ratio].load(std::memory_order_relaxed));
        const float atkMs    = std::max(0.05f, p_[Attack].load(std::memory_order_relaxed)) * chAtk[ch];
        const float relMs    = std::max(1.0f, p_[Release].load(std::memory_order_relaxed)) * chRel[ch];
        const float makeupDb = p_[Makeup].load(std::memory_order_relaxed);
        const float kneeDb   = std::max(0.0f, p_[Knee].load(std::memory_order_relaxed) + chKnee[ch]);
        const float mix      = std::clamp(p_[Mix].load(std::memory_order_relaxed) * 0.01f, 0.0f, 1.0f);
        const float rangeDb  = p_[Range].load(std::memory_order_relaxed);      // max reduction, dB
        const bool  rms      = p_[Detection].load(std::memory_order_relaxed) > 0.5f;
        const bool  autoRel  = p_[AutoRelease].load(std::memory_order_relaxed) > 0.5f;
        const bool  autoGain = p_[AutoGain].load(std::memory_order_relaxed) > 0.5f;
        const bool  listen   = p_[ScListen].load(std::memory_order_relaxed) > 0.5f;

        const double atkC  = std::exp(-1.0 / (atkMs * 0.001 * sr));
        const double relC  = std::exp(-1.0 / (relMs * 0.001 * sr));
        const double relCs = std::exp(-1.0 / (relMs * 4.0 * 0.001 * sr));      // slow stage (auto-release)
        const double rmsC  = std::exp(-1.0 / (0.010 * sr));                    // 10 ms RMS window
        const float slope  = 1.0f - 1.0f / ratio;

        // Side-chain detector filters (HP + LP one-pole) on the detector path.
        const double hp = std::clamp((double)p_[ScHP].load(std::memory_order_relaxed), 20.0, 2000.0);
        const double lp = std::clamp((double)p_[ScLP].load(std::memory_order_relaxed), 200.0, 20000.0);
        const float hpCoef = (float)std::exp(-2.0 * kPi * hp / sr);
        const float lpCoef = (float)(1.0 - std::exp(-2.0 * kPi * lp / sr));

        // Auto make-up estimate: roughly half the reduction at 0 dBFS peaks.
        const float autoMk = autoGain ? std::max(0.0f, -threshDb * slope * 0.5f) : 0.0f;

        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const float scGainLin = std::pow(10.0f, sidechainGainDb() / 20.0f);
        const int la = std::clamp((int)(p_[Lookahead].load(std::memory_order_relaxed) * 0.001 * sr), 0, laSize_ - 1);

        float maxGr = 0.0f;
        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            float d = sc ? (sc[i * 2] + sc[i * 2 + 1]) * 0.5f * scGainLin : (l + r) * 0.5f;
            // Detector HP then LP.
            hpS_ = hpCoef * (hpS_ + d - hpPrev_); hpPrev_ = d; d = hpS_;
            lpS_ += lpCoef * (d - lpS_); d = lpS_;

            float det;
            if (rms) { rmsE_ = (float)(rmsC * rmsE_ + (1.0 - rmsC) * d * d); det = std::sqrt(rmsE_); }
            else det = std::fabs(d);

            if (det > env_) env_ = (float)(atkC * env_ + (1.0 - atkC) * det);
            else { double rc = (autoRel && grPrev_ > 3.0f) ? relCs : relC; env_ = (float)(rc * env_ + (1.0 - rc) * det); }

            const float envDb = 20.0f * std::log10(env_ + 1e-9f);
            const float over = envDb - threshDb;
            float grDb;
            if (kneeDb > 0.01f && 2.0f * over > -kneeDb && 2.0f * over < kneeDb)
                grDb = slope * (over + kneeDb * 0.5f) * (over + kneeDb * 0.5f) / (2.0f * kneeDb);   // soft knee
            else grDb = over > 0.0f ? slope * over : 0.0f;
            grDb = std::min(grDb, rangeDb);
            grPrev_ = grDb;
            if (grDb > maxGr) maxGr = grDb;

            const float wetGain = std::pow(10.0f, (makeupDb + autoMk - grDb) / 20.0f);
            const float gain = (1.0f - mix) + mix * wetGain;

            // Look-ahead: apply the current gain to the delayed audio.
            laBuf_[laW_ * 2] = l; laBuf_[laW_ * 2 + 1] = r;
            int rp = laW_ - la; if (rp < 0) rp += laSize_;
            const float dl = laBuf_[rp * 2], dr = laBuf_[rp * 2 + 1];
            if (++laW_ >= laSize_) laW_ = 0;

            if (listen) { buf[i * 2] = d; buf[i * 2 + 1] = d; }
            else { buf[i * 2] = dl * gain; buf[i * 2 + 1] = dr * gain; }
        }
        gr_.store(maxGr, std::memory_order_relaxed);
        scBuf_ = nullptr;
    }

    float gainReductionDb() const override { return gr_.load(std::memory_order_relaxed); }

    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    const char* displayName() const override { return "Nota Compressor"; }
    int32_t     builtinKind() const override { return 1; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Thresh", "Ratio", "Attack", "Release", "Makeup", "Knee", "Mix",
            "Lookahead", "Detection", "AutoRelease", "AutoGain", "Range", "Character", "SC HP", "SC LP", "SC Listen" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) { case Threshold: return -60.0f; case Ratio: return 1.0f; case Attack: return 0.1f; case Release: return 5.0f;
            case ScHP: return 20.0f; case ScLP: return 200.0f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        switch (i) { case Threshold: return 0.0f; case Ratio: return 20.0f; case Attack: return 100.0f; case Release: return 1000.0f;
            case Makeup: return 24.0f; case Knee: return 24.0f; case Mix: return 100.0f; case Lookahead: return 10.0f;
            case Range: return 48.0f; case Character: return 4.0f; case ScHP: return 2000.0f; case ScLP: return 20000.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    Compressor() {
        p_[Threshold].store(-18.0f); p_[Ratio].store(3.0f); p_[Attack].store(10.0f); p_[Release].store(120.0f); p_[Makeup].store(0.0f);
        p_[Knee].store(6.0f); p_[Mix].store(100.0f); p_[Lookahead].store(0.0f); p_[Detection].store(0.0f);
        p_[AutoRelease].store(0.0f); p_[AutoGain].store(0.0f); p_[Range].store(48.0f); p_[Character].store(0.0f);
        p_[ScHP].store(20.0f); p_[ScLP].store(20000.0f); p_[ScListen].store(0.0f);
        setSampleRate(44100.0, 0);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    double sr_ = 44100.0;
    float env_ = 0.0f, rmsE_ = 0.0f, grPrev_ = 0.0f;
    float hpS_ = 0.0f, hpPrev_ = 0.0f, lpS_ = 0.0f;
    std::vector<float> laBuf_; int laSize_ = 0, laW_ = 0;
    std::atomic<float> gr_{0.0f};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
