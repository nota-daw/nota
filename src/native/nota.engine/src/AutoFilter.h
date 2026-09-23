// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Auto Filter (device kind 7) — a classic envelope/LFO-driven dynamic
// filter: a TPT state-variable filter (LP/BP/HP/Notch with a continuous Morph and a
// 12/24 dB slope) whose cutoff — and/or resonance (Mod Target) — is swept by an
// envelope follower (attack / release / timed or infinite hold, keyed off this track OR
// a sidechain source) and an LFO (rate free or tempo-synced / amount / wave incl.
// sample-&-hold / morph / start phase / stereo phase / retrigger). Mod Smooth slews the
// modulation. A pre-filter Drive stage adds colour; Analog saturates; Dry/Wet blends.
//
// A core Device (JUCE-free), lock-free params. All params are normalized 0..1 and
// denormalized in process(); persistence/automation/clone flow generically. Param order
// is the persisted layout — APPEND ONLY. process() never allocates.
//
// Telemetry for the card and MCP (scopeRead): kTele live values, then the envelope and
// cutoff histories (1 ms per point, oldest → newest), then the pre-filter mono ring for
// the spectrum. deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>

namespace nota {

class AutoFilter : public Device {
public:
    enum {
        Freq = 0,   // base cutoff (exp 30..18000 Hz)
        Res,        // resonance (Q 0.5..15)
        Type,       // filter type: 0 LP, 1 BP, 2 HP, 3 Notch
        Slope,      // 0 = 12 dB/oct, 1 = 24 dB/oct
        Morph,      // blend from Type toward the next type (continuous)
        EnvAmt,     // bipolar env depth (0.5 = 0; ±6 oct on Freq, ±80 % on Res)
        EnvAtt,     // envelope attack (exp 0.1..500 ms)
        EnvRel,     // envelope release (exp 1..2000 ms)
        EnvHold,    // peak hold on/off (>=0.5); how long = EnvHoldTime
        LfoAmt,     // LFO depth (0..4 oct on Freq, ±50 % on Res)
        LfoRate,    // LFO rate (exp 0.01..40 Hz; in Sync picks a division)
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
        // ---- appended for the almanac redesign — keep the order, append only ----
        ModTarget,  // what ENV + LFO move: 0 Freq, 0.5 Reso, 1 Both
        EnvHoldTime,// hold length when Hold is on: 400·v² ms; the top (≥ 0.995) = ∞ (freeze)
        ModSmooth,  // slew on the modulation: 120·v² ms (0 = none)
        LfoOffset,  // LFO start phase (0..360°)
        LfoRetrig,  // restart the LFO on each input onset and at play (>=0.5)
        kNumParams
    };
    // Scope telemetry layout (read by the card and MCP).
    enum {
        S_Cut = 0,   // live modulated cutoff, normalized over 30..18000 Hz (left channel)
        S_CutR,      // right channel (differs with LFO stereo phase)
        S_Res,       // live modulated resonance, normalized
        S_Env,       // envelope follower level, linear 0..1
        S_Lfo,       // LFO value (−1..1, left)
        S_LfoPhase,  // LFO phase 0..1 (left, incl. start offset)
        S_InPk,      // input peak (decaying), linear
        S_OutPk,     // output peak (decaying), linear
        S_Cpu,       // share of real time spent in process()
        S_SampleRate,
        S_Bpm,       // 0 until the transport has run
        S_Onsets,    // input onsets counted since load (retrig events)
        S_EnvHeld,   // 1 while the envelope is held at its peak
        S_HistLen,   // points per history (kHist)
        S_HistMs,    // ms per history point
        S_Reserved,
        kTele
    };
    enum { A_RetrigLfo = 0, A_ResetEnv = 1 };   // deviceAction ids
    static constexpr int kHist = 2048;           // 1 ms per point → ~2 s
    static constexpr int kScope = 2048;          // pre-filter ring (power of two)
    static constexpr int kScopeTotal = kTele + 2 * kHist + kScope;
    static constexpr float kOnsetOn = 0.063f;    // −24 dBFS — an onset (Retrig, event count)
    static constexpr float kOnsetOff = 0.0316f;  // −30 dBFS — re-arms

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
        p_[ModTarget].store(0.0f);   // Freq
        p_[EnvHoldTime].store(1.0f); // ∞ — what Hold did before it had a length
        p_[ModSmooth].store(0.0f);
        p_[LfoOffset].store(0.0f);
        p_[LfoRetrig].store(0.0f);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (auto& s : svf_) s.reset();
        env_ = 0.0f; lfoPhase_ = 0.0; shHeld_ = 0.0f; shCycle_ = -1.0;
        smOct_ = smRes_ = 0.0; holdLeft_ = 0; armed_ = true;
        histAcc_ = 0.0; histEnvMax_ = 0.0f;
        srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    }

    // Musical clock for the tempo-synced LFO (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override {
        if (playing && !playing_) playStarted_ = true;   // Retrig restarts the LFO at play
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : 0.0; playing_ = playing;
    }

    // Sidechain: the envelope follower keys off the source track when one is set.
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    // Telemetry block, then the histories, then the pre-filter ring (see the header note).
    // Writes as much of that layout as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        t[S_Cut] = cutA_.load(std::memory_order_relaxed);
        t[S_CutR] = cutRA_.load(std::memory_order_relaxed);
        t[S_Res] = resA_.load(std::memory_order_relaxed);
        t[S_Env] = envA_.load(std::memory_order_relaxed);
        t[S_Lfo] = lfoA_.load(std::memory_order_relaxed);
        t[S_LfoPhase] = lfoPhA_.load(std::memory_order_relaxed);
        t[S_InPk] = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPk] = outPkA_.load(std::memory_order_relaxed);
        t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
        t[S_Onsets] = static_cast<float>(onsets_.load(std::memory_order_relaxed));
        t[S_EnvHeld] = heldA_.load(std::memory_order_relaxed);
        t[S_HistLen] = static_cast<float>(kHist);
        t[S_HistMs] = 1.0f;
        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        const uint32_t hw = histW_.load(std::memory_order_relaxed);
        for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = histEnv_[(hw + (uint32_t)k) & (kHist - 1)];
        for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = histCut_[(hw + (uint32_t)k) & (kHist - 1)];
        const uint32_t w = scopeW_.load(std::memory_order_relaxed);
        for (int k = 0; k < kScope && n < maxSamples; ++k) out[n++] = scope_[(w + (uint32_t)k) & (kScope - 1)];
        return n;
    }

    // The live modulated cutoff, normalized 0..1 over the Freq range (30..18000 Hz), on the
    // generic per-device live-scalar channel — this device carries no gain reduction.
    float gainReductionDb() const override { return cutA_.load(std::memory_order_relaxed); }

    void deviceAction(int32_t id, int32_t, float) override {
        if (id == A_RetrigLfo) retrigReq_.store(true, std::memory_order_relaxed);
        else if (id == A_ResetEnv) envResetReq_.store(true, std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        const double baseHz  = expMap(get(Freq), 30.0, 18000.0);
        const float  resBase = std::clamp(get(Res), 0.0f, 1.0f);
        const int    type    = std::clamp((int)std::lround(get(Type) * 3.0f), 0, 3);
        const bool   slope24 = get(Slope) >= 0.5f;
        const float  morph   = std::clamp(get(Morph), 0.0f, 1.0f);
        const bool   envOn   = get(EnvOn) >= 0.5f;
        const double envBip  = envOn ? ((double)get(EnvAmt) - 0.5) * 2.0 : 0.0;           // −1..1
        const float  attMs   = (float)expMap(get(EnvAtt), 0.1, 500.0);
        const float  relMs   = (float)expMap(get(EnvRel), 1.0, 2000.0);
        const bool   hold    = get(EnvHold) >= 0.5f;
        const float  holdV   = std::clamp(get(EnvHoldTime), 0.0f, 1.0f);
        const bool   holdInf = holdV >= 0.995f;
        const int64_t holdN  = (int64_t)(400.0 * holdV * holdV * 0.001 * sr_);
        const bool   lfoOn   = get(LfoOn) >= 0.5f;
        const double lfoDep  = lfoOn ? (double)std::clamp(get(LfoAmt), 0.0f, 1.0f) : 0.0;   // 0..1
        const double lfoHz   = expMap(get(LfoRate), 0.01, 40.0);
        const int    lfoWave = std::clamp((int)std::lround(get(LfoWave) * 4.0f), 0, 4);
        const float  lfoMorph= std::clamp(get(LfoMorph), 0.0f, 1.0f);
        const float  drive   = std::clamp(get(Drive), 0.0f, 1.0f);
        const float  wet     = std::clamp(get(DryWet), 0.0f, 1.0f);
        const float  outGain = std::pow(10.0f, (get(Gain) - 0.5f) * 48.0f / 20.0f);   // ±24 dB
        const bool   analog  = get(Circuit) >= 0.5f;                                   // mild saturation
        const int    target  = std::clamp((int)std::lround(get(ModTarget) * 2.0f), 0, 2);
        const bool   toFreq  = target != 1, toRes = target != 0;
        const double smMs    = 120.0 * (double)get(ModSmooth) * (double)get(ModSmooth);
        const double smCoef  = smMs < 0.05 ? 0.0 : std::exp(-(double)kCtrl / (smMs * 0.001 * sr_));
        const double offset  = (double)std::clamp(get(LfoOffset), 0.0f, 1.0f);
        const bool   retrig  = get(LfoRetrig) >= 0.5f;

        // Per-sample envelope coefficients (one-pole).
        const float aCoef = attMs <= 0.0f ? 0.0f : (float)std::exp(-1.0 / (attMs * 0.001 * sr_));
        const float rCoef = relMs <= 0.0f ? 0.0f : (float)std::exp(-1.0 / (relMs * 0.001 * sr_));
        const float scGainLin = std::pow(10.0f, sidechainGainDb() / 20.0f);
        const float driveGain = 1.0f + drive * drive * 24.0f;     // up to ~+28 dB into the shaper
        const float driveComp = 1.0f / (1.0f + drive * 1.5f);     // loudness makeup
        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;

        const double phaseInc = lfoHz / sr_;
        const bool   sync    = get(LfoSync) >= 0.5f && spb_ > 0.0;
        const double divBeats= kLfoDiv[std::clamp((int)std::lround(get(LfoRate) * 7.0f), 0, 7)];
        const double stPhase = (double)std::clamp(get(LfoPhase), 0.0f, 1.0f) * 0.5;   // 0..0.5 cycle (0..180°)
        const double nyq     = std::min(20000.0, sr_ * 0.45);
        const double histStep = sr_ * 0.001;                                         // 1 ms per history point

        if (envResetReq_.exchange(false, std::memory_order_relaxed)) { env_ = 0.0f; holdLeft_ = 0; }
        bool doRetrig = retrigReq_.exchange(false, std::memory_order_relaxed);
        if (playStarted_) { playStarted_ = false; if (retrig) doRetrig = true; }

        float inPk = 0.0f, outPk = 0.0f;
        double lastLfoL = 0.0, lastPhase = 0.0, fcLast = baseHz, fcRLast = baseHz, resLast = resBase;
        bool held = false;
        int32_t i = 0;
        while (i < frames) {
            const int32_t block = std::min(kCtrl, frames - i);
            const double beat = sync ? beatStart_ + (double)i / spb_ : 0.0;
            if (doRetrig) { doRetrig = false; lfoPhase_ = 0.0; anchorBeat_ = sync ? beat : 0.0; shCycle_ = -1.0; }

            // --- control-rate modulation: LFO phase (free or tempo-locked), per-channel cutoff ---
            double phase;
            if (sync) {
                const double rel = (beat - (retrig ? anchorBeat_ : 0.0)) / divBeats;
                phase = rel - std::floor(rel);
                const double cyc = std::floor(rel);
                if (cyc != shCycle_) { shCycle_ = cyc; shHeld_ = whiteNoise(); }   // S&H reseed per cycle
            } else phase = lfoPhase_;
            phase += offset; phase -= std::floor(phase);

            const double lfoL = lfoValue(lfoWave, lfoMorph, phase);
            double pr = phase + stPhase; pr -= std::floor(pr);
            const double lfoR = stPhase > 1e-4 ? lfoValue(lfoWave, lfoMorph, pr) : lfoL;

            // Modulation in "depth units": env ±1 · amount, LFO ±1 · amount — then smoothed.
            const double envMod = envBip * (double)env_;
            const double rawL = envMod * 6.0 + lfoDep * 4.0 * lfoL;   // octaves (Freq)
            const double rawR = envMod * 6.0 + lfoDep * 4.0 * lfoR;
            const double rawRes = envMod * 0.8 + lfoDep * 0.5 * lfoL; // resonance offset
            smOct_ = rawL + smCoef * (smOct_ - rawL);
            smOctR_ = rawR + smCoef * (smOctR_ - rawR);
            smRes_ = rawRes + smCoef * (smRes_ - rawRes);

            const double fcL = std::clamp(baseHz * std::exp2(toFreq ? smOct_ : 0.0), 20.0, nyq);
            const double fcR = std::clamp(baseHz * std::exp2(toFreq ? smOctR_ : 0.0), 20.0, nyq);
            const double resN = std::clamp((double)resBase + (toRes ? smRes_ : 0.0), 0.0, 1.0);
            const double k = 1.0 / (0.5 + resN * 14.5);
            const double gL = std::tan(3.14159265358979323846 * fcL / sr_);
            const double a1L = 1.0 / (1.0 + gL * (gL + k)), a2L = gL * a1L, a3L = gL * a2L;
            const double gR = std::tan(3.14159265358979323846 * fcR / sr_);
            const double a1R = 1.0 / (1.0 + gR * (gR + k)), a2R = gR * a1R, a3R = gR * a2R;
            lastLfoL = lfoL; lastPhase = phase; fcLast = fcL; fcRLast = fcR; resLast = resN;

            for (int32_t n = 0; n < block; ++n, ++i) {
                const float l = buf[i * 2], r = buf[i * 2 + 1];
                inPk = std::max(inPk, std::max(std::fabs(l), std::fabs(r)));

                // Publish the pre-filter mono signal for the UI spectrum analyzer.
                const uint32_t sw = scopeW_.load(std::memory_order_relaxed);
                scope_[sw & (kScope - 1)] = 0.5f * (l + r);
                scopeW_.store(sw + 1, std::memory_order_relaxed);

                // Envelope detector: sidechain source if routed, else this track.
                const float det = sc ? scGainLin * std::max(std::fabs(sc[i * 2]), std::fabs(sc[i * 2 + 1]))
                                     : std::max(std::fabs(l), std::fabs(r));
                if (det > env_) {
                    env_ = det + aCoef * (env_ - det);
                    holdLeft_ = holdN;
                } else if (hold && (holdInf || holdLeft_ > 0)) {
                    if (holdLeft_ > 0) --holdLeft_;                  // hold = keep the peak a while
                    held = true;
                } else env_ = det + rCoef * (env_ - det);
                if (env_ > 1.0f) env_ = 1.0f;

                buf[i * 2]     = filterOne(0, l, drive, driveGain, driveComp, type, morph, (float)k, a1L, a2L, a3L, slope24, wet, outGain, analog);
                buf[i * 2 + 1] = filterOne(1, r, drive, driveGain, driveComp, type, morph, (float)k, a1R, a2R, a3R, slope24, wet, outGain, analog);
                outPk = std::max(outPk, std::max(std::fabs(buf[i * 2]), std::fabs(buf[i * 2 + 1])));
            }

            // Onsets: the follower rising through −24 dBFS (re-arms under −30). Retrig restarts the LFO.
            if (armed_ && env_ >= kOnsetOn) {
                armed_ = false;
                onsets_.fetch_add(1, std::memory_order_relaxed);
                if (retrig) doRetrig = true;
            } else if (!armed_ && env_ < kOnsetOff) armed_ = true;

            // History: 1 ms per point — the follower's peak over the point, and the cutoff.
            histEnvMax_ = std::max(histEnvMax_, env_);
            histAcc_ += block;
            while (histAcc_ >= histStep) {
                histAcc_ -= histStep;
                const uint32_t hw = histW_.load(std::memory_order_relaxed);
                histEnv_[hw & (kHist - 1)] = histEnvMax_;
                histCut_[hw & (kHist - 1)] = (float)std::clamp(std::log(fcL / 30.0) / std::log(18000.0 / 30.0), 0.0, 1.0);
                histW_.store(hw + 1, std::memory_order_relaxed);
                histEnvMax_ = env_;
            }

            if (!sync) {
                lfoPhase_ += phaseInc * block;
                if (lfoPhase_ >= 1.0) { lfoPhase_ -= std::floor(lfoPhase_); shHeld_ = whiteNoise(); }
            }
        }
        scBuf_ = nullptr;   // consume once

        // ---- telemetry -----------------------------------------------------------
        auto norm = [](double hz) { return (float)std::clamp(std::log(hz / 30.0) / std::log(18000.0 / 30.0), 0.0, 1.0); };
        cutA_.store(norm(fcLast), std::memory_order_relaxed);
        cutRA_.store(norm(fcRLast), std::memory_order_relaxed);
        resA_.store((float)resLast, std::memory_order_relaxed);
        envA_.store(env_, std::memory_order_relaxed);
        lfoA_.store(lfoOn ? (float)lastLfoL : 0.0f, std::memory_order_relaxed);
        lfoPhA_.store((float)lastPhase, std::memory_order_relaxed);
        heldA_.store(held ? 1.0f : 0.0f, std::memory_order_relaxed);
        const float fall = std::exp(-(float)frames / (float)(0.3 * sr_));   // ~300 ms peak fall
        inPkA_.store(std::max(inPk, inPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
        outPkA_.store(std::max(outPk, outPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
        bpmA_.store(spb_ > 0.0 ? (float)(60.0 * sr_ / spb_) : 0.0f, std::memory_order_relaxed);
        srA_.store((float)sr_, std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    const char* displayName() const override { return "Nota Auto Filter"; }
    int32_t     builtinKind() const override { return 7; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = {
            "Freq", "Res", "Type", "Slope", "Morph", "Env Amt", "Env Attack", "Env Release", "Env Hold",
            "LFO Amt", "LFO Rate", "LFO Wave", "LFO Morph", "Drive", "Dry/Wet", "Env On", "LFO On", "Gain",
            "Circuit", "LFO Sync", "LFO Stereo",
            "Mod Target", "Env Hold Time", "Mod Smooth", "LFO Offset", "LFO Retrig" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    // 0 — a one-line status (what the filter is set to do); 1 — the live reading;
    // 2 — a guide to the normalized parameter values (for an assistant over MCP).
    std::string deviceText(int32_t id) const override {
        char b[1024];
        static const char* types[4] = { "LP", "BP", "HP", "Notch" };
        static const char* waves[5] = { "Sine", "Tri", "Saw", "Square", "S&H" };
        static const char* divs[8] = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
        static const char* targets[3] = { "Freq", "Reso", "Freq+Reso" };
        if (id == 0) {
            const int type = std::clamp((int)std::lround(getParam(Type) * 3.0f), 0, 3);
            const int target = std::clamp((int)std::lround(getParam(ModTarget) * 2.0f), 0, 2);
            std::string s;
            std::snprintf(b, sizeof b, "%s %d dB/oct%s - %s - Q %.2f", types[type], getParam(Slope) >= 0.5f ? 24 : 12,
                          getParam(Circuit) >= 0.5f ? " analog" : "", hzText(expMap(getParam(Freq), 30.0, 18000.0)).c_str(),
                          0.5 + getParam(Res) * 14.5);
            s = b;
            if (getParam(EnvOn) >= 0.5f && std::fabs(getParam(EnvAmt) - 0.5f) > 0.004f) {
                std::snprintf(b, sizeof b, " - ENV -> %s %+.0f %% (%.1f / %.1f ms%s)", targets[target], (getParam(EnvAmt) - 0.5f) * 200.0f,
                              expMap(getParam(EnvAtt), 0.1, 500.0), expMap(getParam(EnvRel), 1.0, 2000.0), holdText().c_str());
                s += b;
            }
            if (getParam(LfoOn) >= 0.5f && getParam(LfoAmt) > 0.004f) {
                const bool sync = getParam(LfoSync) >= 0.5f;
                char rate[32];
                if (sync) std::snprintf(rate, sizeof rate, "%s", divs[std::clamp((int)std::lround(getParam(LfoRate) * 7.0f), 0, 7)]);
                else std::snprintf(rate, sizeof rate, "%.2f Hz", expMap(getParam(LfoRate), 0.01, 40.0));
                std::snprintf(b, sizeof b, " - LFO %s %s -> %s %.0f %%%s", waves[std::clamp((int)std::lround(getParam(LfoWave) * 4.0f), 0, 4)],
                              rate, targets[target], getParam(LfoAmt) * 100.0f, getParam(LfoRetrig) >= 0.5f ? " retrig" : "");
                s += b;
            }
            if (getParam(Drive) > 0.004f) { std::snprintf(b, sizeof b, " - drive %.0f %%", getParam(Drive) * 100.0f); s += b; }
            std::snprintf(b, sizeof b, " - mix %.0f %%", getParam(DryWet) * 100.0f);
            s += b;
            if (sidechainSourceTrackId() >= 0) s += " - sidechain key";
            return s;
        }
        if (id == 1) {
            const double hz = 30.0 * std::pow(18000.0 / 30.0, (double)cutA_.load(std::memory_order_relaxed));
            const double midi = 69.0 + 12.0 * std::log2(hz / 440.0);
            const int note = (int)std::lround(midi);
            static const char* nn[12] = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            const float env = envA_.load(std::memory_order_relaxed);
            std::snprintf(b, sizeof b, "cutoff %s (%s%d %+.0f ct) - reso %.0f %% - env %.1f dB%s - lfo %+.2f phase %.0f deg - onsets %u",
                          hzText(hz).c_str(), nn[((note % 12) + 12) % 12], note / 12 - 1, (midi - note) * 100.0,
                          resA_.load(std::memory_order_relaxed) * 100.0f, env > 1e-6f ? 20.0f * std::log10(env) : -120.0f,
                          heldA_.load(std::memory_order_relaxed) > 0.5f ? " (held)" : "",
                          lfoA_.load(std::memory_order_relaxed), lfoPhA_.load(std::memory_order_relaxed) * 360.0f,
                          onsets_.load(std::memory_order_relaxed));
            return b;
        }
        if (id == 2) {
            return "All params are 0..1. Freq: 30*600^v Hz. Res: Q 0.5+14.5v. Type: 0 LP, 0.333 BP, 0.667 HP, 1 Notch. "
                   "Slope: 0 = 12, 1 = 24 dB/oct. Morph: blend toward the next type. Env Amt: bipolar, 0.5 = 0 (+/-6 oct on Freq, "
                   "+/-80 % on Res). Env Attack: 0.1*5000^v ms. Env Release: 2000^v ms. Env Hold: on/off; Env Hold Time: 400v^2 ms, "
                   ">= 0.995 = infinite. LFO Amt: 0..4 oct on Freq (+/-50 % on Res). LFO Rate: free 0.01*4000^v Hz; with LFO Sync on "
                   "round(v*7) picks 2/1,1/1,1/2,1/4,1/8,1/16,1/32,1/64. LFO Wave: 0 Sine, 0.25 Tri, 0.5 Saw, 0.75 Square, 1 S&H. "
                   "LFO Morph: skew / pulse width. LFO Stereo: right-channel phase 0..180 deg. LFO Offset: start phase 0..360 deg. "
                   "LFO Retrig: restart on each input onset (-24 dBFS) and at play. Mod Target: 0 Freq, 0.5 Reso, 1 both. "
                   "Mod Smooth: 120v^2 ms slew. Drive: pre-filter saturation. Circuit: 0 Clean, 1 Analog. Gain: 0.5 = 0 dB, +/-24 dB. "
                   "Env On / LFO On / toggles: >= 0.5 = on.";
        }
        return {};
    }

private:
    static constexpr int32_t kCtrl = 16;   // control-rate block for coefficient updates

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static std::string hzText(double hz) {
        char b[32];
        if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.2f kHz", hz / 1000.0); else std::snprintf(b, sizeof b, "%.0f Hz", hz);
        return b;
    }
    std::string holdText() const {
        if (getParam(EnvHold) < 0.5f) return {};
        const float v = getParam(EnvHoldTime);
        if (v >= 0.995f) return ", hold inf";
        char b[32]; std::snprintf(b, sizeof b, ", hold %.0f ms", 400.0f * v * v); return b;
    }

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

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Svf svf_[4];               // [ch*2 + stage]
    float env_ = 0.0f;
    int64_t holdLeft_ = 0;                  // samples of hold left at the current peak
    bool armed_ = true;                     // onset detector armed
    double lfoPhase_ = 0.0;
    double anchorBeat_ = 0.0;               // synced LFO restarts here after a retrig
    double smOct_ = 0.0, smOctR_ = 0.0, smRes_ = 0.0;   // smoothed modulation
    float shHeld_ = 0.0f;
    double shCycle_ = -1.0;                 // last synced S&H cycle index
    double beatStart_ = 0.0, spb_ = 0.0;    // transport (for LFO sync)
    bool playing_ = false, playStarted_ = false;
    uint32_t rng_ = 0x1234567u;
    std::atomic<bool> retrigReq_{false}, envResetReq_{false};

    // Telemetry.
    mutable float scope_[kScope] = {};                 // pre-filter mono ring (UI spectrum)
    std::atomic<uint32_t> scopeW_{0};
    float histEnv_[kHist] = {}, histCut_[kHist] = {};  // 1 ms history rings
    std::atomic<uint32_t> histW_{0};
    double histAcc_ = 0.0;
    float histEnvMax_ = 0.0f;
    std::atomic<uint32_t> onsets_{0};
    double cpuS_ = 0.0;
    std::atomic<float> cutA_{0.5f}, cutRA_{0.5f}, resA_{0.1f}, envA_{0}, lfoA_{0}, lfoPhA_{0}, inPkA_{0}, outPkA_{0},
                       cpuA_{0}, srA_{44100.0f}, bpmA_{0}, heldA_{0};

    // Sidechain (engine hands the source buffer just before process()).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
};

} // namespace nota
