// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Volt — the built-in virtual-analog synth (kind 6), a dual-path subtractive
// voice modelled on classic two-oscillator analogs. Signal
// flow per voice:
//
//   Osc1 ┐                         ┌─ Filter 1 ─→ Amp1 (pan/level) ─┐
//   Osc2 ┼─ route (F1↔F2) per src ─┤                                ├─→ out
//   Noise┘                         └─ Filter 2 ─→ Amp2 (pan/level) ─┘
//                                     (Fil1 can also feed Fil2 via "To F2")
//
// Two TPT state-variable filters (LP/HP/BP/Notch, switchable 12/24 dB slope), each
// with cutoff modulated by a shared filter envelope, keytracking and one of two
// global LFOs. Shared amp ADSR with velocity depth. Per-oscillator octave/semi/
// detune/start-phase, unison spread, portamento (glide), a dedicated vibrato, and
// mono/poly voice mode.
//
// On top of the fixed routings sits a generic MODULATION MATRIX (mockup 2a/2e):
// 7 sources (Amp Env, Filter Env, LFO 1, LFO 2, Velocity, Key, Mod Wheel) × 6
// destinations (Pitch, Osc2 Pitch, Cutoff, Reso, Level, Pan), each a bipolar amount.
// Eight MACROS add manual offsets into the same destinations. Two full-shape LFOs
// (sin/tri/sqr/S&H) with depth, per-note fade-in and optional tempo sync round it out.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable
// ids) → automation / persist / clone for free. Header-only; allocation-free after
// construction. A per-sample loop keeps modulation smooth; filter coefficients update
// at control rate (every 16 samples). Name & DSP are Nota's own.

#pragma once

#include "Instrument.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>

namespace nota {

class VoltSynth final : public Instrument {
public:
    // Modulation matrix / macro dimensions.
    static constexpr int kSources = 7;   // Amp Env, Flt Env, LFO1, LFO2, Vel, Key, Mod Whl
    static constexpr int kDests   = 6;   // Pitch, Osc2 Pitch, Cutoff, Reso, Level, Pan
    static constexpr int kMacros  = 8;

    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    // Scalar block is 0..63; the matrix (64..105) and macros (106..129) follow.
    enum Param {
        Osc1Wave = 0, Osc1Octave, Osc1Semi, Osc1Detune, Osc1Level, Osc1Route,
        Osc2Wave, Osc2Octave, Osc2Semi, Osc2Detune, Osc2Level, Osc2Route,
        NoiseLevel, NoiseColor, NoiseRoute,
        Fil1Type, Fil1Freq, Fil1Reso, Fil1EnvAmt, Fil1LfoAmt, Fil1KeyAmt, Fil1ToF2,
        Fil2Type, Fil2Freq, Fil2Reso, Fil2EnvAmt, Fil2LfoAmt, Fil2KeyAmt,
        FAttack, FDecay, FSustain, FRelease,
        Amp1Pan, Amp1Level, Amp2Pan, Amp2Level,
        Attack, Decay, Sustain, Release,
        Lfo1Rate, Lfo2Rate,
        Volume, VibRate, VibAmt, Unison, Glide, VelAmp, VelFilter,   // ends at 48
        // ---- appended (v4, mockup 2a/2e rework) ----
        Osc1Phase, Osc2Phase,                                        // 49,50
        Lfo1Depth, Lfo1Shape, Lfo1Sync, Lfo1Fade,                    // 51..54
        Lfo2Depth, Lfo2Shape, Lfo2Sync, Lfo2Fade,                    // 55..58
        Fil1Slope, Fil2Slope,                                        // 59,60
        ModWheel, MonoMode, OutPan,                                  // 61,62,63
        MatrixBase,                                                  // 64 (42 entries)
        MacroBase = MatrixBase + kSources * kDests,                  // 106 (24 entries)
        kNumParams = MacroBase + kMacros * 3                         // 130
    };

    static int matrixIdx(int s, int d) { return MatrixBase + s * kDests + d; }
    static int macroIdx(int m, int field) { return MacroBase + m * 3 + field; }  // field: 0 val, 1 dest, 2 amt

    VoltSynth() {
        // Classic init: two detuned saws → Fil1 low-pass with filter-env motion, a
        // little unison, snappy amp env. Osc2 sits on Fil1 too; Fil2 idle by default.
        set(Osc1Wave, 0.0f);  set(Osc1Octave, 0.5f); set(Osc1Semi, 0.5f); set(Osc1Detune, 0.5f);  set(Osc1Level, 0.85f); set(Osc1Route, 0.0f);
        set(Osc2Wave, 0.0f);  set(Osc2Octave, 0.5f); set(Osc2Semi, 0.5f); set(Osc2Detune, 0.55f); set(Osc2Level, 0.70f); set(Osc2Route, 0.0f);
        set(NoiseLevel, 0.0f); set(NoiseColor, 0.6f); set(NoiseRoute, 0.0f);
        set(Fil1Type, 0.0f); set(Fil1Freq, 0.55f); set(Fil1Reso, 0.18f); set(Fil1EnvAmt, 0.66f); set(Fil1LfoAmt, 0.5f); set(Fil1KeyAmt, 0.3f); set(Fil1ToF2, 0.0f);
        set(Fil2Type, 0.0f); set(Fil2Freq, 0.7f);  set(Fil2Reso, 0.15f); set(Fil2EnvAmt, 0.5f);  set(Fil2LfoAmt, 0.5f); set(Fil2KeyAmt, 0.3f);
        set(FAttack, 0.02f); set(FDecay, 0.4f); set(FSustain, 0.25f); set(FRelease, 0.3f);
        set(Amp1Pan, 0.5f); set(Amp1Level, 0.85f); set(Amp2Pan, 0.5f); set(Amp2Level, 0.0f);
        set(Attack, 0.02f); set(Decay, 0.35f); set(Sustain, 0.8f); set(Release, 0.3f);
        set(Lfo1Rate, 0.42f); set(Lfo2Rate, 0.3f);
        set(Volume, 0.8f); set(VibRate, 0.4f); set(VibAmt, 0.0f); set(Unison, 0.25f); set(Glide, 0.0f); set(VelAmp, 0.4f); set(VelFilter, 0.3f);
        // Appended defaults: neutral so old projects (49-float state) sound unchanged.
        set(Osc1Phase, 0.0f); set(Osc2Phase, 0.0f);                 // 0 = free running
        set(Lfo1Depth, 0.5f); set(Lfo1Shape, 0.0f); set(Lfo1Sync, 0.0f); set(Lfo1Fade, 0.0f);
        set(Lfo2Depth, 0.5f); set(Lfo2Shape, 0.0f); set(Lfo2Sync, 0.0f); set(Lfo2Fade, 0.0f);
        set(Fil1Slope, 0.0f); set(Fil2Slope, 0.0f);                 // 0 = 12 dB
        set(ModWheel, 0.0f); set(MonoMode, 0.0f); set(OutPan, 0.5f);
        for (int s = 0; s < kSources; ++s) for (int d = 0; d < kDests; ++d) set((Param)matrixIdx(s, d), 0.5f);  // bipolar zero
        for (int m = 0; m < kMacros; ++m) { set((Param)macroIdx(m, 0), 0.0f); set((Param)macroIdx(m, 1), 0.0f); set((Param)macroIdx(m, 2), 0.5f); }
    }

    int32_t kind() const override { return 6; }
    const char* displayName() const override { return "Nota Volt"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }
    void setTransport(double /*beatStart*/, double samplesPerBeat, bool /*playing*/) override {
        if (samplesPerBeat > 0.0) samplesPerBeat_ = samplesPerBeat;
    }
    // Active (sounding) voice count, for the editor's voice meter. Message thread read
    // of an audio-thread-updated snapshot; approximate is fine.
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i < MatrixBase) {
            static const char* ids[] = {
                "osc1wave", "osc1octave", "osc1semi", "osc1detune", "osc1level", "osc1route",
                "osc2wave", "osc2octave", "osc2semi", "osc2detune", "osc2level", "osc2route",
                "noise", "noisecolor", "noiseroute",
                "fil1type", "fil1freq", "fil1reso", "fil1env", "fil1lfo", "fil1key", "fil1tof2",
                "fil2type", "fil2freq", "fil2reso", "fil2env", "fil2lfo", "fil2key",
                "fattack", "fdecay", "fsustain", "frelease",
                "amp1pan", "amp1level", "amp2pan", "amp2level",
                "attack", "decay", "sustain", "release",
                "lfo1rate", "lfo2rate",
                "volume", "vibrate", "vibamt", "unison", "glide", "velamp", "velfilter",
                "osc1phase", "osc2phase",
                "lfo1depth", "lfo1shape", "lfo1sync", "lfo1fade",
                "lfo2depth", "lfo2shape", "lfo2sync", "lfo2fade",
                "fil1slope", "fil2slope",
                "modwheel", "mono", "outpan" };
            return std::string(ids[i]);
        }
        if (i < MacroBase) { int j = i - MatrixBase; return "mtx" + std::to_string(j / kDests) + "_" + std::to_string(j % kDests); }
        int j = i - MacroBase, m = j / 3, f = j % 3;
        return "mac" + std::to_string(m) + (f == 0 ? "val" : f == 1 ? "dest" : "amt");
    }
    std::string pluginParamName(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i < MatrixBase) {
            static const char* nm[] = {
                "Osc1 Wave", "Osc1 Octave", "Osc1 Semi", "Osc1 Detune", "Osc1 Level", "Osc1 →Filter",
                "Osc2 Wave", "Osc2 Octave", "Osc2 Semi", "Osc2 Detune", "Osc2 Level", "Osc2 →Filter",
                "Noise", "Noise Color", "Noise →Filter",
                "Filter 1 Type", "Filter 1 Freq", "Filter 1 Reso", "Filter 1 Env", "Filter 1 LFO", "Filter 1 Key", "To Filter 2",
                "Filter 2 Type", "Filter 2 Freq", "Filter 2 Reso", "Filter 2 Env", "Filter 2 LFO", "Filter 2 Key",
                "Filter Attack", "Filter Decay", "Filter Sustain", "Filter Release",
                "Amp1 Pan", "Amp1 Level", "Amp2 Pan", "Amp2 Level",
                "Attack", "Decay", "Sustain", "Release",
                "LFO1 Rate", "LFO2 Rate",
                "Volume", "Vibrato Rate", "Vibrato", "Unison", "Glide", "Vel→Amp", "Vel→Filter",
                "Osc1 Phase", "Osc2 Phase",
                "LFO1 Depth", "LFO1 Shape", "LFO1 Sync", "LFO1 Fade",
                "LFO2 Depth", "LFO2 Shape", "LFO2 Sync", "LFO2 Fade",
                "Filter 1 Slope", "Filter 2 Slope",
                "Mod Wheel", "Mono Mode", "Out Pan" };
            return std::string(nm[i]);
        }
        static const char* srcNm[] = { "Amp Env", "Flt Env", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl" };
        static const char* dstNm[] = { "Pitch", "Osc2 Pitch", "Cutoff", "Reso", "Level", "Pan" };
        if (i < MacroBase) { int j = i - MatrixBase; return std::string(srcNm[j / kDests]) + " → " + dstNm[j % kDests]; }
        int j = i - MacroBase, m = j / 3, f = j % 3;
        return "Macro " + std::to_string(m + 1) + (f == 0 ? "" : f == 1 ? " Dest" : " Amount");
    }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            float v = pn_[i].load(std::memory_order_relaxed);
            std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
        }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data) return;
        const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
        for (int i = 0; i < n; ++i) {
            float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float));
            if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<VoltSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        const bool mono = get(MonoMode) > 0.5f;
        const bool glideOn = pn_[Glide].load(std::memory_order_relaxed) > 0.001f;

        if (mono) {
            Voice* v = &voices_[0];
            const bool legato = v->active && v->aStage != Stage::Off;
            v->pitch = pitch;
            v->freqTarget = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
            if (!legato) { v->freqCur = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freqTarget; startVoice(*v, pitch, velocity); }
            else if (!glideOn) v->freqCur = v->freqTarget;   // legato, no glide: jump pitch, keep envelopes
            v->active = true;
            lastFreq_ = v->freqTarget;
            heldPitch_ = pitch;
            return;
        }

        Voice* v = findFreeVoice();
        v->pitch = pitch;
        v->freqTarget = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        v->freqCur = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freqTarget;
        startVoice(*v, pitch, velocity);
        v->active = true;
        lastFreq_ = v->freqTarget;
    }
    void noteOff(int32_t pitch) override {
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.aStage != Stage::Release) {
                v.aStage = Stage::Release; v.fStage = Stage::Release;
            }
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        // ---- per-block param snapshot ----
        const int   w1 = waveOf(Osc1Wave), w2 = waveOf(Osc2Wave);
        const double mult1 = pitchMult(Osc1Octave, Osc1Semi, Osc1Detune);
        const double mult2 = pitchMult(Osc2Octave, Osc2Semi, Osc2Detune);
        const float lvl1 = get(Osc1Level), lvl2 = get(Osc2Level);
        const double r1 = get(Osc1Route), r2 = get(Osc2Route), rn = get(NoiseRoute);
        const float noiseLvl = get(NoiseLevel);
        const double nColor = std::clamp((double)get(NoiseColor), 0.0, 1.0);
        const double nCoef = 0.02 + 0.9 * nColor;   // one-pole toward white as color rises

        const int    t1 = filtTypeOf(Fil1Type), t2 = filtTypeOf(Fil2Type);
        const bool   slope1 = get(Fil1Slope) > 0.5f, slope2 = get(Fil2Slope) > 0.5f;   // true = 24 dB (cascade)
        const double f1base = expMap(get(Fil1Freq), 20.0, 18000.0);
        const double f2base = expMap(get(Fil2Freq), 20.0, 18000.0);
        const double reso1 = get(Fil1Reso), reso2 = get(Fil2Reso);
        const double f1Env = bip(Fil1EnvAmt), f1Lfo = bip(Fil1LfoAmt), f1Key = get(Fil1KeyAmt), toF2 = get(Fil1ToF2);
        const double f2Env = bip(Fil2EnvAmt), f2Lfo = bip(Fil2LfoAmt), f2Key = get(Fil2KeyAmt);

        const float fA = rateOf(FAttack, 0.001, 2.0), fD = rateOf(FDecay, 0.002, 4.0), fR = rateOf(FRelease, 0.002, 5.0);
        const float fS = get(FSustain);
        const float aA = rateOf(Attack, 0.001, 2.0), aD = rateOf(Decay, 0.002, 4.0), aR = rateOf(Release, 0.002, 5.0);
        const float aS = get(Sustain);

        const float p1 = bipF(Amp1Pan), p2 = bipF(Amp2Pan);
        const float p1L = std::cos((p1 + 1.0f) * 0.25f * (float)kPi), p1R = std::sin((p1 + 1.0f) * 0.25f * (float)kPi);
        const float p2L = std::cos((p2 + 1.0f) * 0.25f * (float)kPi), p2R = std::sin((p2 + 1.0f) * 0.25f * (float)kPi);
        const float l1 = get(Amp1Level), l2 = get(Amp2Level);

        // LFOs: shape + depth + optional tempo sync.
        const int    lfo1shape = shapeOf(Lfo1Shape), lfo2shape = shapeOf(Lfo2Shape);
        const double lfo1Inc = lfoInc(Lfo1Rate, Lfo1Sync);
        const double lfo2Inc = lfoInc(Lfo2Rate, Lfo2Sync);
        const float  lfo1Depth = get(Lfo1Depth), lfo2Depth = get(Lfo2Depth);
        const float  fade1Inc = fadeInc(Lfo1Fade), fade2Inc = fadeInc(Lfo2Fade);

        const double vibInc = expMap(get(VibRate), 0.1, 12.0) / sampleRate_;
        const double vibCents = get(VibAmt) * 50.0;
        const double uni = get(Unison);
        const double uniDet = std::exp2(uni * 25.0 / 1200.0);   // side-osc detune (up to ~25 cents)
        const float  uniLvl = (float)(uni * 0.9);
        const double glide = get(Glide);
        const double glideCoef = glide > 0.001 ? (1.0 - std::exp(-1.0 / (expMap(glide, 0.005, 0.6) * sampleRate_))) : 1.0;
        const float velAmp = get(VelAmp), velFilt = get(VelFilter);
        const float volume = get(Volume);
        const float outPan = bipF(OutPan);
        const float opL = std::cos((outPan + 1.0f) * 0.25f * (float)kPi), opR = std::sin((outPan + 1.0f) * 0.25f * (float)kPi);
        const float modWheel = get(ModWheel);

        // ---- modulation matrix: compact list of active (src,dst,amount) ----
        int amS[kSources * kDests], amD[kSources * kDests], amCount = 0;
        double amA[kSources * kDests];
        double macroDst[kDests] = { 0, 0, 0, 0, 0, 0 };
        for (int s = 0; s < kSources; ++s)
            for (int d = 0; d < kDests; ++d) {
                double a = (get((Param)matrixIdx(s, d)) - 0.5) * 2.0;
                if (std::fabs(a) > 1e-4) { amS[amCount] = s; amD[amCount] = d; amA[amCount] = a; ++amCount; }
            }
        for (int m = 0; m < kMacros; ++m) {
            double val = get((Param)macroIdx(m, 0));
            double amt = (get((Param)macroIdx(m, 2)) - 0.5) * 2.0;
            if (std::fabs(val * amt) > 1e-4) macroDst[destOf(get((Param)macroIdx(m, 1)))] += val * amt;
        }
        const bool pitchMod = amActive(amD, amCount, 0) || amActive(amD, amCount, 1) || macroDst[0] != 0.0 || macroDst[1] != 0.0;
        const bool panMod   = amActive(amD, amCount, 5) || macroDst[5] != 0.0;
        const bool lvlMod   = amActive(amD, amCount, 4) || macroDst[4] != 0.0;

        const double invSr = 1.0 / sampleRate_;

        for (int32_t i = 0; i < frames; ++i) {
            if (lfoPhase1_ >= 1.0) { lfoPhase1_ -= 1.0; lfoSh1_ = noise() * 2.0f - 1.0f; }
            if (lfoPhase2_ >= 1.0) { lfoPhase2_ -= 1.0; lfoSh2_ = noise() * 2.0f - 1.0f; }
            const double lfo1 = lfoValue(lfo1shape, lfoPhase1_, lfoSh1_);
            const double lfo2 = lfoValue(lfo2shape, lfoPhase2_, lfoSh2_);
            lfoPhase1_ += lfo1Inc; lfoPhase2_ += lfo2Inc;
            const double vibv = std::sin(kTwoPi * vibPhase_); vibPhase_ += vibInc; if (vibPhase_ >= 1.0) vibPhase_ -= 1.0;
            const double vibMult = vibCents > 0.0 ? std::exp2(vibCents * vibv / 1200.0) : 1.0;

            // Shared noise source (one-pole "color" toward white).
            const float white = noise() * 2.0f - 1.0f;
            nlp_ += (float)nCoef * (white - nlp_);
            const float nz = (float)(nlp_ * (1.0 - nColor) + white * nColor) * noiseLvl;

            float outL = 0.0f, outR = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;
                advanceEnv(v.aEnv, v.aStage, aA, aD, aS, aR);
                if (v.aStage == Stage::Off) { v.active = false; continue; }
                advanceEnv(v.fEnv, v.fStage, fA, fD, fS, fR);
                if (v.fade1 < 1.0f) v.fade1 = std::min(1.0f, v.fade1 + fade1Inc);
                if (v.fade2 < 1.0f) v.fade2 = std::min(1.0f, v.fade2 + fade2Inc);

                // Modulation sources → destinations for this voice/sample.
                double dst[kDests] = { macroDst[0], macroDst[1], macroDst[2], macroDst[3], macroDst[4], macroDst[5] };
                if (amCount) {
                    const double src[kSources] = {
                        v.aEnv, v.fEnv,
                        lfo1 * lfo1Depth * v.fade1, lfo2 * lfo2Depth * v.fade2,
                        v.vel, std::clamp(v.ktOct / 4.0, -1.0, 1.0), modWheel };
                    for (int k = 0; k < amCount; ++k) dst[amD[k]] += src[amS[k]] * amA[k];
                }

                if (glideCoef < 1.0) v.freqCur += (v.freqTarget - v.freqCur) * glideCoef; else v.freqCur = v.freqTarget;
                double base = v.freqCur * vibMult * invSr;
                double m1 = mult1, m2 = mult2;
                if (pitchMod) {
                    const double pm = std::exp2(dst[0] * 2.0);        // ±24 semitones at full
                    m1 *= pm; m2 *= pm * std::exp2(dst[1] * 2.0);
                }

                const double i1 = base * m1, i1u = i1 * uniDet;
                const double i2 = base * m2, i2u = i2 * uniDet;
                double s1 = (oscBlep(w1, v.ph1, i1) + oscBlep(w1, v.ph1u, i1u) * uniLvl);
                double s2 = (oscBlep(w2, v.ph2, i2) + oscBlep(w2, v.ph2u, i2u) * uniLvl);
                s1 *= lvl1 / (1.0 + uniLvl); s2 *= lvl2 / (1.0 + uniLvl);
                v.ph1 += i1; if (v.ph1 >= 1.0) v.ph1 -= 1.0;
                v.ph1u += i1u; if (v.ph1u >= 1.0) v.ph1u -= 1.0;
                v.ph2 += i2; if (v.ph2 >= 1.0) v.ph2 -= 1.0;
                v.ph2u += i2u; if (v.ph2u >= 1.0) v.ph2u -= 1.0;

                double in1 = s1 * (1.0 - r1) + s2 * (1.0 - r2) + nz * (1.0 - rn);
                double in2 = s1 * r1 + s2 * r2 + nz * rn;

                // Control-rate filter coefficients (cutoff = base × 2^mod, incl. matrix).
                if (v.modCount-- <= 0) {
                    v.modCount = 15;
                    const double velF = 1.0f - velFilt + velFilt * v.vel;
                    const double cutMod = dst[2] * 4.0;               // matrix Cutoff (±4 oct)
                    double o1 = f1Env * 4.0 * v.fEnv * velF + f1Key * v.ktOct + f1Lfo * 2.0 * lfo1 + cutMod;
                    double o2 = f2Env * 4.0 * v.fEnv * velF + f2Key * v.ktOct + f2Lfo * 2.0 * lfo2 + cutMod;
                    const double k1 = resoToK(reso1 + dst[3]);
                    const double k2 = resoToK(reso2 + dst[3]);
                    v.k1 = k1; v.k2 = k2;
                    setSvf(v.f1a1, v.f1a2, v.f1a3, std::clamp(f1base * std::exp2(o1), 20.0, sampleRate_ * 0.49), k1);
                    setSvf(v.f2a1, v.f2a2, v.f2a3, std::clamp(f2base * std::exp2(o2), 20.0, sampleRate_ * 0.49), k2);
                }
                double o1 = svf(v.s1, v.f1a1, v.f1a2, v.f1a3, v.k1, in1, t1);
                if (slope1) o1 = svf(v.s1b, v.f1a1, v.f1a2, v.f1a3, v.k1, o1, t1);
                in2 += o1 * toF2;
                double o2 = svf(v.s2, v.f2a1, v.f2a2, v.f2a3, v.k2, in2, t2);
                if (slope2) o2 = svf(v.s2b, v.f2a1, v.f2a2, v.f2a3, v.k2, o2, t2);

                float ampG = v.aEnv * (1.0f - velAmp + velAmp * v.vel);
                if (lvlMod) ampG *= (float)std::clamp(1.0 + dst[4], 0.0, 4.0);
                const float a1 = (float)o1 * l1 * ampG;
                const float a2 = (float)o2 * l2 * ampG;
                float vL = a1 * p1L + a2 * p2L;
                float vR = a1 * p1R + a2 * p2R;
                if (panMod) {
                    const float pm = (float)std::clamp(dst[5], -1.0, 1.0);   // linear balance atop static pan
                    vL *= pm <= 0.0f ? 1.0f : 1.0f - pm;
                    vR *= pm >= 0.0f ? 1.0f : 1.0f + pm;
                }
                outL += vL; outR += vR;
            }
            const float mL = outL * volume * 0.5f, mR = outR * volume * 0.5f;
            out[i * 2]     += mL * opL * kSqrt2;
            out[i * 2 + 1] += mR * opR * kSqrt2;
        }
        activeVoices_.store(countActive(), std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr float  kSqrt2 = 1.41421356f;   // restore unity for centred out-pan

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool     active = false;
        int32_t  pitch = 0;
        double   freqTarget = 0.0, freqCur = 0.0, ktOct = 0.0;
        double   ph1 = 0.0, ph1u = 0.0, ph2 = 0.0, ph2u = 0.0;
        float    vel = 0.0f, aEnv = 0.0f, fEnv = 0.0f, fade1 = 1.0f, fade2 = 1.0f;
        Stage    aStage = Stage::Attack, fStage = Stage::Attack;
        double   s1[2] = {0, 0}, s2[2] = {0, 0};          // SVF integrator state (ic1, ic2)
        double   s1b[2] = {0, 0}, s2b[2] = {0, 0};        // 2nd stage for 24 dB slope
        double   f1a1 = 0, f1a2 = 0, f1a3 = 0, f2a1 = 0, f2a2 = 0, f2a3 = 0;
        double   k1 = 1.0, k2 = 1.0;
        int32_t  modCount = 0;
    };

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    double bip(Param p) const { return (get(p) - 0.5f) * 2.0; }   // 0..1 → -1..+1
    float  bipF(Param p) const { return (get(p) - 0.5f) * 2.0f; }
    int    waveOf(Param p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    int    filtTypeOf(Param p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    int    shapeOf(Param p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    static int destOf(float v) { return std::clamp((int)std::lround(v * (kDests - 1)), 0, kDests - 1); }
    static bool amActive(const int* d, int n, int dest) { for (int k = 0; k < n; ++k) if (d[k] == dest) return true; return false; }
    static double resoToK(double reso) { return std::clamp(2.0 - 1.9 * std::clamp(reso, 0.0, 1.0), 0.05, 2.0); }

    void startVoice(Voice& v, int32_t pitch, float velocity) {
        v.ktOct = (pitch - 60) / 12.0;
        v.ph1 = phaseStart(Osc1Phase); v.ph1u = wrap(v.ph1 + 0.25);
        v.ph2 = phaseStart(Osc2Phase); v.ph2u = wrap(v.ph2 + 0.25);
        v.vel = velocity;
        v.aEnv = 0.0f; v.aStage = Stage::Attack;
        v.fEnv = 0.0f; v.fStage = Stage::Attack;
        v.fade1 = get(Lfo1Fade) > 0.001f ? 0.0f : 1.0f;
        v.fade2 = get(Lfo2Fade) > 0.001f ? 0.0f : 1.0f;
        v.f1a1 = v.f1a2 = 0.0; v.f2a1 = v.f2a2 = 0.0;
        for (double& z : v.s1) z = 0.0;  for (double& z : v.s2) z = 0.0;
        for (double& z : v.s1b) z = 0.0; for (double& z : v.s2b) z = 0.0;
        v.modCount = 0;
    }
    // 0 = free running (leave previous phase); >0 = retrigger to that start phase.
    double phaseStart(Param p) const { float ph = get(p); return ph > 0.001f ? ph : 0.0; }
    static double wrap(double x) { return x >= 1.0 ? x - 1.0 : x; }

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
        switch (st) {
            case Stage::Attack:  env += atk; if (env >= 1.0f) { env = 1.0f; st = Stage::Decay; } break;
            case Stage::Decay:   env -= dec; if (env <= sus)  { env = sus;  st = Stage::Sustain; } break;
            case Stage::Sustain: break;
            case Stage::Release: env -= rel; if (env <= 0.0f) { env = 0.0f; st = Stage::Off; } break;
            case Stage::Off:     break;
        }
    }

    void setSvf(double& a1, double& a2, double& a3, double fc, double k) const {
        const double g = std::tan(kPi * fc / sampleRate_);
        a1 = 1.0 / (1.0 + g * (g + k));
        a2 = g * a1;
        a3 = g * a2;
    }
    // TPT SVF (Cytomic). type: 0 LP, 1 HP, 2 BP, 3 Notch. state[0]=ic1, state[1]=ic2.
    static double svf(double* st, double a1, double a2, double a3, double k, double in, int type) {
        const double v3 = in - st[1];
        const double v1 = a1 * st[0] + a2 * v3;
        const double v2 = st[1] + a2 * st[0] + a3 * v3;
        st[0] = 2.0 * v1 - st[0];
        st[1] = 2.0 * v2 - st[1];
        switch (type) {
            case 1:  return in - k * v1 - v2;   // high-pass
            case 2:  return v1;                 // band-pass
            case 3:  return in - k * v1;        // notch
            default: return v2;                 // low-pass
        }
    }

    // LFO shape: 0 sin, 1 tri, 2 sqr, 3 sample & hold. Returns -1..+1.
    static double lfoValue(int shape, double ph, float shHold) {
        switch (shape) {
            case 1:  return ph < 0.5 ? (4.0 * ph - 1.0) : (3.0 - 4.0 * ph);   // triangle
            case 2:  return ph < 0.5 ? 1.0 : -1.0;                            // square
            case 3:  return shHold;                                           // sample & hold
            default: return std::sin(kTwoPi * ph);                            // sine
        }
    }
    // Free-run increment, or tempo-synced division when Sync param > 0.
    double lfoInc(Param rate, Param sync) const {
        int div = std::clamp((int)std::lround(get(sync) * 7.0f), 0, 7);       // 0 = free
        if (div > 0 && samplesPerBeat_ > 0.0) {
            static const double beats[8] = { 0, 4, 2, 1, 0.5, 0.25, 0.125, 0.0625 };
            return 1.0 / (beats[div] * samplesPerBeat_);
        }
        return expMap(get(rate), 0.05, 20.0) / sampleRate_;
    }
    float fadeInc(Param p) const {
        double t = get(p); if (t <= 0.001) return 1.0f;
        return (float)(1.0 / (expMap(t, 0.01, 5.0) * sampleRate_));
    }

    static double oscBlep(int wave, double ph, double dt) {
        switch (wave) {
            case 1: {  // square
                double s = ph < 0.5 ? 1.0 : -1.0;
                s += polyBlep(ph, dt);
                double p2 = ph + 0.5; if (p2 >= 1.0) p2 -= 1.0;
                return s - polyBlep(p2, dt);
            }
            case 2:  return 4.0 * std::fabs(ph - 0.5) - 1.0;      // triangle
            case 3:  return std::sin(kTwoPi * ph);                // sine
            default: return (2.0 * ph - 1.0) - polyBlep(ph, dt);  // saw
        }
    }
    static double polyBlep(double t, double dt) {
        if (dt <= 0.0) return 0.0;
        if (t < dt)       { double x = t / dt;         return x + x - x * x - 1.0; }
        if (t > 1.0 - dt) { double x = (t - 1.0) / dt; return x * x + x + x + 1.0; }
        return 0.0;
    }
    float noise() {
        rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5;
        return (rng_ & 0xFFFFFF) / static_cast<float>(0x1000000);
    }

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }
    // Octave (±3) + semi (±12) + detune (±50 cents) → frequency multiplier.
    double pitchMult(Param oct, Param semi, Param det) const {
        int o = (int)std::lround((get(oct) - 0.5f) * 6.0f);
        int s = (int)std::lround((get(semi) - 0.5f) * 24.0f);
        double c = (get(det) - 0.5f) * 100.0;
        return std::exp2(o + s / 12.0 + c / 1200.0);
    }

    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.aEnv < q->aEnv) q = &v;
        return q;
    }
    int countActive() const { int n = 0; for (const auto& v : voices_) if (v.active) ++n; return n; }

    static constexpr int kVoices = 16;
    Voice  voices_[kVoices];
    double sampleRate_ = 44100.0;
    double samplesPerBeat_ = 0.0;   // from setTransport, for LFO sync
    double lfoPhase1_ = 0.0, lfoPhase2_ = 0.0, vibPhase_ = 0.0;
    float  lfoSh1_ = 0.0f, lfoSh2_ = 0.0f;   // sample & hold values
    double lastFreq_ = 0.0;    // portamento reference
    int32_t heldPitch_ = -1;   // mono last-note
    float  nlp_ = 0.0f;        // noise color one-pole state
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
