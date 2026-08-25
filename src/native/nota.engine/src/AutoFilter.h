// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Auto Filter (device kind 7) — a classic envelope/LFO-driven dynamic
// filter: a TPT state-variable filter (LP/HP/BP/Notch with a continuous Morph and a
// 12/24 dB slope) whose cutoff is swept by an envelope follower (attack / release /
// hold, keyed off this track OR a sidechain source) and an LFO (rate / amount / wave
// incl. sample-&-hold / morph). A pre-filter Drive stage adds colour; Dry/Wet blends.
// A core Device (JUCE-free), lock-free params. All params are normalized 0..1 and
// denormalized in process(); persistence/automation/clone flow generically.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class AutoFilter : public Device {
public:
    enum {
        Freq = 0,   // base cutoff (exp 30..18000 Hz)
        Res,        // resonance (Q 0.5..15)
        Type,       // filter type: 0 LP, 1 BP, 2 HP, 3 Notch
        Slope,      // 0 = 12 dB/oct, 1 = 24 dB/oct
        Morph,      // blend from Type toward the next type (continuous)
        EnvAmt,     // bipolar env→cutoff depth (0.5 = 0, ±6 oct)
        EnvAtt,     // envelope attack (exp 0.1..500 ms)
        EnvRel,     // envelope release (exp 1..2000 ms)
        EnvHold,    // peak-hold toggle (>=0.5)
        LfoAmt,     // LFO→cutoff depth (0..4 oct)
        LfoRate,    // LFO rate (exp 0.01..40 Hz)
        LfoWave,    // 0 Sine, 1 Tri, 2 Saw, 3 Square, 4 S&H (random)
        LfoMorph,   // LFO shape skew / pulse-width (0..1)
        Drive,      // pre-filter drive (0..1)
        DryWet,     // 0 = dry, 1 = wet
        EnvOn,      // envelope enable (>=0.5)
        LfoOn,      // LFO enable (>=0.5)
        Gain,       // output gain (0.5 = 0 dB, ±24 dB)
        Circuit,    // 0 = Clean, 1 = Analog (mild saturation)
        LfoSync,    // 0 = Free (Hz), 1 = tempo-synced (Rate selects a division)
        LfoPhase,   // stereo LFO phase offset for the right channel (0..180°)
        kNumParams
    };
    // Tempo-synced LFO divisions (beats per cycle), fastest → slowest by Rate.
    static constexpr double kLfoDiv[8] = { 8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625 };

    AutoFilter() {
        p_[Freq].store(0.55f);   // ~760 Hz
        p_[Res].store(0.1f);
        p_[Type].store(0.0f);    // LP
        p_[Slope].store(0.0f);   // 12 dB
        p_[Morph].store(0.0f);
        p_[EnvAmt].store(0.5f);  // 0 (bipolar)
        p_[EnvAtt].store(0.15f);
        p_[EnvRel].store(0.45f);
        p_[EnvHold].store(0.0f);
        p_[LfoAmt].store(0.0f);
        p_[LfoRate].store(0.4f);
        p_[LfoWave].store(0.0f);
        p_[LfoMorph].store(0.0f);
        p_[Drive].store(0.0f);
        p_[DryWet].store(1.0f);
        p_[EnvOn].store(1.0f);
        p_[LfoOn].store(1.0f);
        p_[Gain].store(0.5f);    // 0 dB
        p_[Circuit].store(0.0f); // Clean
        p_[LfoSync].store(0.0f); // Free
        p_[LfoPhase].store(0.0f);// 0° (mono)
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (auto& s : svf_) s.reset();
        env_ = 0.0f; lfoPhase_ = 0.0f; shHeld_ = 0.0f; shCycle_ = -1.0;
    }

    // Musical clock for the tempo-synced LFO (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override {
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : 0.0; playing_ = playing;
    }

    // Sidechain: the envelope follower keys off the source track when one is set.
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    // Real-time analyzer feed: the pre-filter mono signal, oldest→newest (like EQ-8),
    // for the UI's spectrum behind the response curve. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const int32_t n = std::min(maxSamples, kScope);
        const uint32_t w = scopeW_.load(std::memory_order_relaxed);
        const uint32_t start = w - static_cast<uint32_t>(n);
        for (int32_t k = 0; k < n; ++k) out[k] = scope_[(start + (uint32_t)k) & (kScope - 1)];
        return n;
    }

    // The live modulated cutoff, normalized 0..1 over the Freq range (30..18000 Hz),
    // published so the UI curve sweeps as the envelope (incl. sidechain) + LFO move it.
    // Reuses the generic per-device live-scalar channel (audio writes, UI reads lock-free)
    // — this device carries no gain reduction, so the slot is free.
    float gainReductionDb() const override { return modCutoffNorm_.load(std::memory_order_relaxed); }

    void process(float* buf, int32_t frames) override {
        const double baseHz  = expMap(get(Freq), 30.0, 18000.0);
        const double q       = 0.5 + (double)std::clamp(get(Res), 0.0f, 1.0f) * 14.5;
        const int    type    = std::clamp((int)std::lround(get(Type) * 3.0f), 0, 3);
        const bool   slope24 = get(Slope) >= 0.5f;
        const float  morph   = std::clamp(get(Morph), 0.0f, 1.0f);
        const double envOct  = get(EnvOn) >= 0.5f ? ((double)get(EnvAmt) - 0.5) * 2.0 * 6.0 : 0.0;   // ±6 oct
        const float  attMs   = (float)expMap(get(EnvAtt), 0.1, 500.0);
        const float  relMs   = (float)expMap(get(EnvRel), 1.0, 2000.0);
        const bool   hold    = get(EnvHold) >= 0.5f;
        const double lfoOct  = get(LfoOn) >= 0.5f ? (double)std::clamp(get(LfoAmt), 0.0f, 1.0f) * 4.0 : 0.0;
        const double lfoHz   = expMap(get(LfoRate), 0.01, 40.0);
        const int    lfoWave = std::clamp((int)std::lround(get(LfoWave) * 4.0f), 0, 4);
        const float  lfoMorph= std::clamp(get(LfoMorph), 0.0f, 1.0f);
        const float  drive   = std::clamp(get(Drive), 0.0f, 1.0f);
        const float  wet     = std::clamp(get(DryWet), 0.0f, 1.0f);
        const float  outGain = std::pow(10.0f, (get(Gain) - 0.5f) * 48.0f / 20.0f);   // ±24 dB
        const bool   analog  = get(Circuit) >= 0.5f;                                   // mild saturation

        // Per-sample envelope coefficients (one-pole).
        const float aCoef = attMs <= 0.0f ? 0.0f : (float)std::exp(-1.0 / (attMs * 0.001 * sr_));
        const float rCoef = relMs <= 0.0f ? 0.0f : (float)std::exp(-1.0 / (relMs * 0.001 * sr_));
        const float scGainLin = std::pow(10.0f, sidechainGainDb() / 20.0f);
        const float driveGain = 1.0f + drive * drive * 24.0f;     // up to ~+28 dB into the shaper
        const float driveComp = 1.0f / (1.0f + drive * 1.5f);     // loudness makeup
        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;

        const double phaseInc = lfoHz / sr_;
        const double k = 1.0 / q;
        const bool   sync    = get(LfoSync) >= 0.5f && spb_ > 0.0;
        const double divBeats= kLfoDiv[std::clamp((int)std::lround(get(LfoRate) * 7.0f), 0, 7)];
        const double stPhase = (double)std::clamp(get(LfoPhase), 0.0f, 1.0f) * 0.5;   // 0..0.5 cycle (0..180°)
        const double nyq     = std::min(20000.0, sr_ * 0.45);

        int32_t i = 0;
        while (i < frames) {
            const int32_t block = std::min(kCtrl, frames - i);

            // --- control-rate modulation: LFO phase (free or tempo-locked), per-channel cutoff ---
            double phase;
            if (sync) {
                const double beat = beatStart_ + (double)i / spb_;
                phase = beat / divBeats; phase -= std::floor(phase);
                const double cyc = std::floor(beat / divBeats);
                if (cyc != shCycle_) { shCycle_ = cyc; shHeld_ = whiteNoise(); }   // S&H reseed per cycle
            } else phase = lfoPhase_;

            const double lfoL = lfoValue(lfoWave, lfoMorph, phase);
            double pr = phase + stPhase; pr -= std::floor(pr);
            const double lfoR = stPhase > 1e-4 ? lfoValue(lfoWave, lfoMorph, pr) : lfoL;

            const double fcL = std::clamp(baseHz * std::exp2(envOct * (double)env_ + lfoOct * lfoL), 20.0, nyq);
            const double fcR = std::clamp(baseHz * std::exp2(envOct * (double)env_ + lfoOct * lfoR), 20.0, nyq);
            modCutoffNorm_.store((float)std::clamp(std::log(fcL / 30.0) / std::log(18000.0 / 30.0), 0.0, 1.0),
                                 std::memory_order_relaxed);   // publish for the UI curve sweep
            const double gL = std::tan(3.14159265358979323846 * fcL / sr_);
            const double a1L = 1.0 / (1.0 + gL * (gL + k)), a2L = gL * a1L, a3L = gL * a2L;
            const double gR = std::tan(3.14159265358979323846 * fcR / sr_);
            const double a1R = 1.0 / (1.0 + gR * (gR + k)), a2R = gR * a1R, a3R = gR * a2R;

            for (int32_t n = 0; n < block; ++n, ++i) {
                const float l = buf[i * 2], r = buf[i * 2 + 1];

                // Publish the pre-filter mono signal for the UI spectrum analyzer.
                const uint32_t sw = scopeW_.load(std::memory_order_relaxed);
                scope_[sw & (kScope - 1)] = 0.5f * (l + r);
                scopeW_.store(sw + 1, std::memory_order_relaxed);

                // Envelope detector: sidechain source if routed, else this track.
                const float det = sc ? scGainLin * std::max(std::fabs(sc[i * 2]), std::fabs(sc[i * 2 + 1]))
                                     : std::max(std::fabs(l), std::fabs(r));
                if (det > env_) env_ = det + aCoef * (env_ - det);
                else if (!hold) env_ = det + rCoef * (env_ - det);   // hold = freeze the peak
                if (env_ > 1.0f) env_ = 1.0f;

                buf[i * 2]     = filterOne(0, l, drive, driveGain, driveComp, type, morph, (float)k, a1L, a2L, a3L, slope24, wet, outGain, analog);
                buf[i * 2 + 1] = filterOne(1, r, drive, driveGain, driveComp, type, morph, (float)k, a1R, a2R, a3R, slope24, wet, outGain, analog);
            }
            if (!sync) {
                lfoPhase_ += phaseInc * block;
                if (lfoPhase_ >= 1.0) { lfoPhase_ -= std::floor(lfoPhase_); shHeld_ = whiteNoise(); }
            }
        }
        scBuf_ = nullptr;   // consume once
    }

    const char* displayName() const override { return "Nota Auto Filter"; }
    int32_t     builtinKind() const override { return 7; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Freq: return "Freq"; case Res: return "Res"; case Type: return "Type"; case Slope: return "Slope";
            case Morph: return "Morph"; case EnvAmt: return "Env Amt"; case EnvAtt: return "Env Attack";
            case EnvRel: return "Env Release"; case EnvHold: return "Env Hold"; case LfoAmt: return "LFO Amt";
            case LfoRate: return "LFO Rate"; case LfoWave: return "LFO Wave"; case LfoMorph: return "LFO Morph";
            case Drive: return "Drive"; case DryWet: return "Dry/Wet";
            case EnvOn: return "Env On"; case LfoOn: return "LFO On"; case Gain: return "Gain"; case Circuit: return "Circuit";
            case LfoSync: return "LFO Sync"; case LfoPhase: return "LFO Stereo";
            default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

private:
    static constexpr int32_t kCtrl = 16;   // control-rate block for coefficient updates

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }

    // TPT state-variable filter: one stage yields LP/BP/HP/Notch simultaneously.
    struct Svf {
        double ic1 = 0, ic2 = 0;
        void reset() { ic1 = ic2 = 0; }
        // Returns the four outputs for input x with precomputed coeffs.
        inline void tick(double x, double k, double a1, double a2, double a3,
                          double& lp, double& bp, double& hp, double& notch) {
            const double v3 = x - ic2;
            const double v1 = a1 * ic1 + a2 * v3;
            const double v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2.0 * v1 - ic1;
            ic2 = 2.0 * v2 - ic2;
            bp = v1; lp = v2; hp = x - k * v1 - v2; notch = lp + hp;
        }
    };

    inline float filterOne(int ch, float xin, float drive, float driveGain, float driveComp,
                           int type, float morph, float k, double a1, double a2, double a3,
                           bool slope24, float wet, float outGain, bool analog) {
        double x = xin;
        if (drive > 0.0001f) x = std::tanh(x * driveGain) * driveComp;

        double lp, bp, hp, notch;
        svf_[ch * 2].tick(x, k, a1, a2, a3, lp, bp, hp, notch);
        if (slope24) {
            const double outs1[4] = { lp, bp, hp, notch };
            svf_[ch * 2 + 1].tick(outs1[type], k, a1, a2, a3, lp, bp, hp, notch);
        }
        const double outs[4] = { lp, bp, hp, notch };
        double y = outs[type];
        if (morph > 0.0f) y = y * (1.0 - morph) + outs[(type + 1) & 3] * morph;   // blend toward next type
        if (analog) y = std::tanh(y * 1.3) / 0.86172;                             // Analog: gentle saturation
        return (float)((xin * (1.0f - wet) + y * wet) * outGain);
    }

    // LFO shape at a given phase; morph skews the waveform (pulse width / slope).
    double lfoValue(int wave, float morph, double ph) const {
        switch (wave) {
            case 0: return std::sin(2.0 * 3.14159265358979323846 * ph);                        // sine
            case 1: return 4.0 * std::fabs(ph - 0.5) - 1.0;                                     // triangle
            case 2: return 2.0 * ph - 1.0;                                                      // saw
            case 3: return ph < (0.5 + (morph - 0.0) * 0.49) ? 1.0 : -1.0;                      // square (PWM via morph)
            default: return shHeld_;                                                            // sample & hold
        }
    }

    float whiteNoise() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((int32_t)(rng_ >> 8) / 8388608.0 - 1.0); }

    static constexpr int kScope = 4096;   // power of two

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Svf svf_[4];               // [ch*2 + stage]
    float env_ = 0.0f;
    double lfoPhase_ = 0.0;
    float shHeld_ = 0.0f;
    double shCycle_ = -1.0;                 // last synced S&H cycle index
    double beatStart_ = 0.0, spb_ = 0.0;    // transport (for LFO sync)
    bool playing_ = false;
    uint32_t rng_ = 0x1234567u;
    mutable float scope_[kScope] = {};                 // pre-filter mono ring (UI spectrum)
    std::atomic<uint32_t> scopeW_{0};
    std::atomic<float> modCutoffNorm_{0.5f};           // live modulated cutoff, normalized (UI)

    // Sidechain (engine hands the source buffer just before process()).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
};

} // namespace nota
