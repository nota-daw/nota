// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in stereo delay (mockup 2h) — independent L/R times (free ms or tempo-synced
// divisions 1/16…1/2 incl. triplet/dotted), Link L/R, feedback, ping-pong cross-feed,
// stereo spread, tape-style WOW modulation (rate · depth), Freeze (infinite loop) and
// dry/wet + output trim. Tempo comes from Device::setTransport.
//
// All params are normalized 0..1 (denormalized in process()); persist/clone/automation
// are generic through the base Device. The ring buffers are allocated on the message
// thread in setSampleRate, so process() never allocates. A core Device (JUCE-free).

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Delay : public Device {
public:
    enum {
        SyncMode = 0, TimeL, TimeR, DivL, DivR, LinkLR, Feedback, Spread,
        PingPong, WowRate, WowDepth, Freeze, DryWet, Output, kNumParams
    };
    static constexpr float kMaxTimeMs = 2000.0f;
    static constexpr int kNumDiv = 8;
    // Division lengths in beats (quarter = 1): 1/16, 1/8T, 1/8, 1/8·, 1/4T, 1/4, 1/4·, 1/2.
    static constexpr double kDivBeats[kNumDiv] = {0.25, 1.0 / 3.0, 0.5, 0.75, 2.0 / 3.0, 1.0, 1.5, 2.0};

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        size_ = static_cast<int>(sr_ * (kMaxTimeMs / 1000.0)) + static_cast<int>(sr_ * 0.06) + 4;
        bufL_.assign(size_, 0.0f); bufR_.assign(size_, 0.0f);
        write_ = 0;
    }
    void setTransport(double, double samplesPerBeat, bool) override {
        if (samplesPerBeat > 0.0) spb_ = samplesPerBeat;
    }

    void process(float* buf, int32_t frames) override {
        if (size_ <= 4) return;
        const bool sync = p_[SyncMode].load(std::memory_order_relaxed) > 0.5f && spb_ > 0.0;
        const bool link = p_[LinkLR].load(std::memory_order_relaxed) > 0.5f;
        const bool ping = p_[PingPong].load(std::memory_order_relaxed) > 0.5f;
        const bool freeze = p_[Freeze].load(std::memory_order_relaxed) > 0.5f;
        const float fb = freeze ? 1.0f : std::clamp(p_[Feedback].load(std::memory_order_relaxed), 0.0f, 0.95f);
        const float inGain = freeze ? 0.0f : 1.0f;
        const float mix = clamp01(p_[DryWet]); const float wet = mix, dry = 1.0f - mix;
        const float outG = clamp01(p_[Output]) * 2.0f;
        const float spreadMs = clamp01(p_[Spread]) * 50.0f;

        float dL = delaySamples(TimeL, DivL, sync);
        float dR = link ? dL : delaySamples(TimeR, DivR, sync);
        dR += spreadMs * 0.001f * (float)sr_;
        dL = std::clamp(dL, 1.0f, (float)size_ - 3.0f);
        dR = std::clamp(dR, 1.0f, (float)size_ - 3.0f);

        const double wowInc = expMap(clamp01(p_[WowRate]), 0.05, 8.0) / sr_;
        const float wowDepth = clamp01(p_[WowDepth]) * 6.0f * (float)(sr_ / 44100.0);   // samples

        for (int32_t i = 0; i < frames; ++i) {
            const float lfo = std::sin(kTwoPi * wowPhase_); wowPhase_ += wowInc; if (wowPhase_ >= 1.0) wowPhase_ -= 1.0;
            const float mod = lfo * wowDepth;
            const float dl = read(bufL_, dL + mod), dr = read(bufR_, dR - mod);
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            const float fbL = ping ? dr : dl, fbR = ping ? dl : dr;
            bufL_[write_] = l * inGain + fbL * fb;
            bufR_[write_] = r * inGain + fbR * fb;
            buf[i * 2]     = (dry * l + wet * dl) * outG;
            buf[i * 2 + 1] = (dry * r + wet * dr) * outG;
            if (++write_ >= size_) write_ = 0;
        }
    }

    const char* displayName() const override { return "Nota Delay"; }
    int32_t     builtinKind() const override { return 3; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Sync", "Time L", "Time R", "Div L", "Div R", "Link L/R", "Feedback",
            "Spread", "Ping-Pong", "Wow Rate", "Wow Depth", "Freeze", "Dry/Wet", "Output" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    Delay() {
        set(SyncMode, 1.0f); set(TimeL, 0.35f); set(TimeR, 0.35f); set(DivL, 2.0f / 7.0f); set(DivR, 2.0f / 7.0f);
        set(LinkLR, 0.0f); set(Feedback, 0.38f); set(Spread, 0.0f); set(PingPong, 0.0f);
        set(WowRate, 0.3f); set(WowDepth, 0.15f); set(Freeze, 0.0f); set(DryWet, 0.3f); set(Output, 0.5f);
    }

private:
    static constexpr double kTwoPi = 6.283185307179586;
    static float clamp01(const std::atomic<float>& a) { return std::clamp(a.load(std::memory_order_relaxed), 0.0f, 1.0f); }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }

    float delaySamples(int timeParam, int divParam, bool sync) const {
        if (sync) {
            int d = std::clamp((int)std::lround(clamp01(p_[divParam]) * (kNumDiv - 1)), 0, kNumDiv - 1);
            return (float)(kDivBeats[d] * spb_);
        }
        float ms = std::clamp(clamp01(p_[timeParam]) * kMaxTimeMs, 1.0f, kMaxTimeMs);
        return ms * 0.001f * (float)sr_;
    }
    float read(const std::vector<float>& b, float delay) const {
        float rp = write_ - delay; while (rp < 0) rp += size_;
        int i0 = (int)rp; float frac = rp - i0; int i1 = i0 + 1; if (i1 >= size_) i1 -= size_;
        return b[i0] + (b[i1] - b[i0]) * frac;
    }

    double sr_ = 44100.0, spb_ = 0.0, wowPhase_ = 0.0;
    int size_ = 0, write_ = 0;
    std::vector<float> bufL_, bufR_;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
