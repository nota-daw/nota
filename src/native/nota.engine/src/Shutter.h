// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Shutter (device kind 19, mockup "Nota Shutter") — a classic noise gate.
// The "shutter" opens when the detector crosses the Threshold and closes when it falls back
// past the Return (hysteresis, stops chatter); Attack / Hold / Release shape each opening,
// Floor sets how far the closed signal drops (−∞ = mute, higher = ducking), Lookahead lets
// the gate open just before a transient, and Flip turns the gate into a ducker. The detector
// keys off this track or an external sidechain, through its own band-pass (HP/LP) with a
// Listen monitor. A core Device (JUCE-free); params normalized 0..1, denormalized in
// process(); persistence / automation / clone flow generically. Telemetry via scopeRead.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Shutter : public Device {
public:
    enum {
        Threshold = 0,  // open level (0..1 → −70..0 dB)
        Return,         // hysteresis below threshold to close (0..1 → 0..24 dB)
        Attack,         // open time (exp 0.01..100 ms)
        Hold,           // minimum open time after the detector drops (exp 0.1..500 ms)
        Release,        // close time (exp 1..2000 ms)
        Floor,          // closed level (0..1 → −70..0 dB; 0 = full mute / −∞)
        Lookahead,      // 0/1/5 ms (discrete)
        Flip,           // 0 = Gate, 1 = Duck (invert)
        DetHP,          // detector high-pass (exp 20..2000 Hz)
        DetLP,          // detector low-pass (exp 200..20000 Hz)
        Listen,         // >=0.5 monitor the detector (band-passed key) instead of the gated audio
        kNumParams
    };
    // Packed scope telemetry: instantaneous meters. The UI builds the scrolling SIGNAL graph.
    enum { S_InDb = 0, S_GateGain, S_GrDb, S_DetDb, S_Open, kScope };

    Shutter() {
        p_[Threshold].store(0.457f);  // −38 dB
        p_[Return].store(0.125f);     // 3 dB
        p_[Attack].store(0.175f);     // ~0.05 ms
        p_[Hold].store(0.588f);       // ~15 ms
        p_[Release].store(0.63f);     // ~120 ms
        p_[Floor].store(0.0f);        // −∞ (mute)
        p_[Lookahead].store(0.5f);    // 1 ms
        p_[Flip].store(0.0f);         // Gate
        p_[DetHP].store(0.301f);      // ~80 Hz
        p_[DetLP].store(0.548f);      // ~2.5 kHz
        p_[Listen].store(0.0f);
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        laMax_ = (int)std::lround(sr_ * 0.006) + 2;   // up to 5 ms look-ahead
        laBuf_.assign((size_t)laMax_ * 2, 0.0f); laW_ = 0;
        detEnv_ = 0.0f; g_ = 1.0f; holdCtr_ = 0; open_ = false;
        dHp_ = dLp_ = 0.0f; inEnv_ = 0.0f;
    }

    // Sidechain: external key source (Compressor / Ceiling plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    // GR (dB, >=0) for the shell titlebar meter, like the Compressor.
    float gainReductionDb() const override { return grDb_.load(std::memory_order_relaxed); }

    int32_t latencySamples() const override { return laSamples(); }

    void process(float* buf, int32_t frames) override {
        const float thrLin = dbToLin(-70.0f + get(Threshold) * 70.0f);
        const float retLin = dbToLin(-70.0f + get(Threshold) * 70.0f - get(Return) * 24.0f);
        const float atkMs = (float)expMap(get(Attack), 0.01, 100.0);
        const float holdMs = (float)expMap(get(Hold), 0.1, 500.0);
        const float relMs = (float)expMap(get(Release), 1.0, 2000.0);
        const float floorLin = get(Floor) <= 0.001f ? 0.0f : dbToLin(-70.0f + get(Floor) * 70.0f);
        const bool flip = get(Flip) >= 0.5f;
        const bool listen = get(Listen) >= 0.5f;
        const double detHpHz = expMap(get(DetHP), 20.0, 2000.0);
        const double detLpHz = expMap(get(DetLP), 200.0, 20000.0);

        const float gAtk = coef(atkMs), gRel = coef(relMs);
        const float dAtk = coef(0.05f), dRel = coef(3.0f);   // detector follower (fast)
        const float inRel = coef(80.0f);                     // input meter envelope
        const int holdSamps = std::max(0, (int)std::lround(holdMs * 0.001 * sr_));
        const float hpC = onePole(detHpHz), lpC = onePole(detLpHz);
        const int la = laSamples();

        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);

        float grMax = 0.0f;

        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];

            // Detector source: external key if routed, else this track.
            float dl = sc ? sc[i * 2] * scGain : l;
            float dr = sc ? sc[i * 2 + 1] * scGain : r;
            float dmono = 0.5f * (dl + dr);

            // Detector band-pass (one-pole HP then LP), then a fast peak follower.
            dLp_ += (dmono - dLp_) * hpC;         // low content
            float hp = dmono - dLp_;              // remove below DetHP
            dHp_ += (hp - dHp_) * lpC;            // remove above DetLP
            const float det = std::fabs(dHp_);
            if (det > detEnv_) detEnv_ = det + dAtk * (detEnv_ - det);
            else detEnv_ = det + dRel * (detEnv_ - det);

            // Input meter envelope (pre-gate, for the olive graph).
            const float ain = std::max(std::fabs(l), std::fabs(r));
            inEnv_ = ain > inEnv_ ? ain : ain + inRel * (inEnv_ - ain);

            // Hysteresis + hold state machine.
            if (detEnv_ > thrLin) { open_ = true; holdCtr_ = holdSamps; }
            else if (detEnv_ < retLin) { if (holdCtr_ > 0) --holdCtr_; else open_ = false; }

            bool wantOpen = flip ? !open_ : open_;
            const float target = wantOpen ? 1.0f : floorLin;
            g_ += (target - g_) * (target > g_ ? gAtk : gRel);

            // Look-ahead: apply the (anticipating) gain to the delayed audio.
            laBuf_[laW_ * 2] = l; laBuf_[laW_ * 2 + 1] = r;
            int rp = laW_ - la; if (rp < 0) rp += laMax_;
            float ol = laBuf_[rp * 2], or_ = laBuf_[rp * 2 + 1];
            if (++laW_ >= laMax_) laW_ = 0;

            if (listen) { buf[i * 2] = dHp_; buf[i * 2 + 1] = dHp_; }
            else { buf[i * 2] = ol * g_; buf[i * 2 + 1] = or_ * g_; }

            const float gr = g_ < 0.999f ? -20.0f * std::log10(std::max(1e-5f, g_)) : 0.0f;
            if (gr > grMax) grMax = gr;
        }

        // Publish meters.
        inDb_.store(db(inEnv_), std::memory_order_relaxed);
        gateGain_.store(g_, std::memory_order_relaxed);
        grDb_.store(grMax, std::memory_order_relaxed);
        detDb_.store(db(detEnv_), std::memory_order_relaxed);
        openF_.store(open_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        scBuf_ = nullptr;
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        out[S_InDb] = inDb_.load(std::memory_order_relaxed);
        out[S_GateGain] = gateGain_.load(std::memory_order_relaxed);
        out[S_GrDb] = grDb_.load(std::memory_order_relaxed);
        out[S_DetDb] = detDb_.load(std::memory_order_relaxed);
        out[S_Open] = openF_.load(std::memory_order_relaxed);
        return kScope;
    }

    const char* displayName() const override { return "Nota Shutter"; }
    int32_t     builtinKind() const override { return 19; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Threshold", "Return", "Attack", "Hold", "Release", "Floor",
                                    "Lookahead", "Flip", "Det HP", "Det LP", "Listen" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

private:
    static constexpr double kPi = 3.14159265358979323846;
    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float dbToLin(float dbv) { return std::pow(10.0f, dbv / 20.0f); }
    static float db(float lin) { return 20.0f * std::log10(std::max(1e-6f, lin)); }
    float coef(float ms) const { return (float)(1.0 - std::exp(-1.0 / (std::max(0.01f, ms) * 0.001 * sr_))); }
    float onePole(double hz) const { return (float)(1.0 - std::exp(-2.0 * kPi * std::clamp(hz, 5.0, sr_ * 0.45) / sr_)); }
    int laSamples() const {
        static const double ms[3] = { 0.0, 1.0, 5.0 };
        int idx = std::clamp((int)std::lround(get(Lookahead) * 2.0f), 0, 2);
        return std::clamp((int)std::lround(ms[idx] * 0.001 * sr_), 0, laMax_ - 1);
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};

    std::vector<float> laBuf_; int laMax_ = 0, laW_ = 0;
    float detEnv_ = 0.0f, inEnv_ = 0.0f, g_ = 1.0f;
    float dHp_ = 0.0f, dLp_ = 0.0f;
    int holdCtr_ = 0; bool open_ = false;

    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;

    std::atomic<float> inDb_{-120.0f}, gateGain_{1.0f}, grDb_{0.0f}, detDb_{-120.0f}, openF_{0.0f};
};

} // namespace nota
