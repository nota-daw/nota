// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Bass — the built-in bass synthesizer (kind 7), a mono-first subtractive voice
// tuned for low end, in the spirit of classic bass synths. Signal flow per voice:
//
//   Osc (morph sine→tri→saw→pulse, PWM) ┐
//   Sub (sine/tri/square, −1/−2 oct)    ┼─→ pre-drive → Filter (LP/HP/BP/Notch, 12/24)
//                                        ┘        │  ▲ filter env · key · LFO
//                                                 ▼
//                                          Amp (ADSR × vel) → output drive → out
//
// A single morphing oscillator (continuous shape blend with pulse-width for the square
// end), a dedicated sub oscillator, a TPT state-variable filter (optional 24 dB cascade)
// with its own envelope, key-tracking and LFO, plus pre-filter and output saturation for
// grit. Mono mode (note stack, last-note priority) with glide makes it a proper bass;
// every new note retriggers the envelopes unless Legato is on, in which case a note
// played over a held one slides into it without a new attack. Poly mode is available
// too. Two performance wheels ride on top — pitch bend with a selectable range (1..12
// semitones) and a mod wheel that opens the LFO onto the cutoff (a hand-played wobble)
// — plus an output pan. All parameters ride the Instrument plugin-param interface
// (normalized 0..1, stable ids) → automation / persist / clone for free.
//
// Note bookkeeping counts instances, not pitches: a note-off releases the oldest held
// instance of that pitch only. Two overlapping notes of the same pitch (a repeated note
// whose on lands a sample before the previous off) therefore no longer silence the new
// one.
//
// Header-only like the other built-in synths. Allocation-free after construction; a
// per-sample loop keeps filter/LFO modulation smooth. Filter coefficients update at
// control rate (every 16 samples). Name & DSP are Nota's own.

#pragma once

#include "Instrument.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class BassSynth final : public Instrument {
public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        OscShape = 0, OscPW, OscOctave, OscSemi, OscLevel,
        SubWave, SubOctave, SubLevel,
        FilType, FilSlope, FilFreq, FilReso, FilDrive, FilEnvAmt, FilKeyAmt, FilLfoAmt,
        FAttack, FDecay, FSustain, FRelease,
        Attack, Decay, Sustain, Release,
        LfoRate, LfoWave, LfoPitch,
        Glide, Unison, Drive, Volume, VelAmp, VelFilter, Mono,
        // ---- appended (v2, almanac rework): wheels, output pan, legato ----
        Bend, BendRange, ModWheel, OutPan, Legato,
        kNumParams
    };

    BassSynth() {
        // Classic init: a saw-ish main osc + a sine sub an octave down through a
        // resonant low-pass with snappy filter-env motion, mono with a touch of glide.
        set(OscShape, 0.62f); set(OscPW, 0.5f); set(OscOctave, 0.5f); set(OscSemi, 0.5f); set(OscLevel, 0.85f);
        set(SubWave, 0.0f);   set(SubOctave, 0.0f); set(SubLevel, 0.7f);
        set(FilType, 0.0f); set(FilSlope, 1.0f); set(FilFreq, 0.4f); set(FilReso, 0.22f); set(FilDrive, 0.25f);
        set(FilEnvAmt, 0.72f); set(FilKeyAmt, 0.35f); set(FilLfoAmt, 0.5f);
        set(FAttack, 0.0f); set(FDecay, 0.35f); set(FSustain, 0.15f); set(FRelease, 0.25f);
        set(Attack, 0.0f); set(Decay, 0.4f); set(Sustain, 0.85f); set(Release, 0.22f);
        set(LfoRate, 0.35f); set(LfoWave, 0.0f); set(LfoPitch, 0.5f);
        set(Glide, 0.08f); set(Unison, 0.0f); set(Drive, 0.15f); set(Volume, 0.85f);
        set(VelAmp, 0.4f); set(VelFilter, 0.35f); set(Mono, 1.0f);
        set(Bend, 0.5f);                  // centred
        set(BendRange, 1.0f / 11.0f);     // ±2 semitones
        set(ModWheel, 0.0f);
        set(OutPan, 0.5f);                // centre
        set(Legato, 0.0f);                // every note retriggers
    }

    int32_t kind() const override { return 7; }
    const char* displayName() const override { return "Nota Bass"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "oscshape", "oscpw", "oscoctave", "oscsemi", "osclevel",
            "subwave", "suboctave", "sublevel",
            "filtype", "filslope", "filfreq", "filreso", "fildrive", "filenv", "filkey", "fillfo",
            "fattack", "fdecay", "fsustain", "frelease",
            "attack", "decay", "sustain", "release",
            "lforate", "lfowave", "lfopitch",
            "glide", "unison", "drive", "volume", "velamp", "velfilter", "mono",
            "bend", "bendrange", "modwheel", "outpan", "legato" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = {
            "Osc Shape", "Pulse Width", "Osc Octave", "Osc Semi", "Osc Level",
            "Sub Wave", "Sub Octave", "Sub Level",
            "Filter Type", "Filter Slope", "Filter Freq", "Filter Reso", "Filter Drive", "Filter Env", "Filter Key", "Filter LFO",
            "Filter Attack", "Filter Decay", "Filter Sustain", "Filter Release",
            "Attack", "Decay", "Sustain", "Release",
            "LFO Rate", "LFO Wave", "LFO →Pitch",
            "Glide", "Unison", "Drive", "Volume", "Vel→Amp", "Vel→Filter", "Mono",
            "Pitch Bend", "Bend Range", "Mod Wheel", "Out Pan", "Legato" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
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
        auto s = std::make_shared<BassSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        const bool glideOn = get(Glide) > 0.001f;
        const double target = mtof(pitch);
        const uint32_t seq = ++noteSeq_;
        if (get(Mono) >= 0.5f) {
            const bool hadHeld = monoCount_ > 0;   // another key already down → an overlap
            if (monoCount_ == kMonoStack) {         // full: forget the oldest key
                for (int r = 1; r < kMonoStack; ++r) monoStack_[r - 1] = monoStack_[r];
                --monoCount_;
            }
            monoStack_[monoCount_++] = pitch;
            Voice& v = voices_[0];
            const bool sounding = v.active && v.aStage != Stage::Release;
            // Legato (no new attack, slide the pitch) only when the patch asks for it AND
            // another note is still held. Otherwise every note is a fresh attack — before,
            // an overlap with glide on skipped it, so on a plucky patch the next note of a
            // tight line was silent.
            const bool legato = get(Legato) >= 0.5f && hadHeld && sounding;
            if (!v.active) v.freqCur = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : target;
            else if (!glideOn) v.freqCur = target;   // else glide on from where it is now
            v.freqTarget = target;
            v.pitch = pitch; v.ktOct = (pitch - 60) / 12.0;
            v.seq = seq;
            if (!legato) { v.vel = velocity; startVoice(v); }
            v.active = true;
            lastFreq_ = target;
            return;
        }
        Voice* v = findFreeVoice();
        v->pitch = pitch;
        v->freqTarget = target;
        // Poly: each voice starts at its own pitch. Gliding a fresh voice from the last
        // note's frequency (a mono/legato notion) makes chord/sequence voices sweep through
        // one another and beat — so no portamento in poly.
        v->freqCur = v->freqTarget;
        v->ktOct = (pitch - 60) / 12.0;
        v->vel = velocity;
        v->seq = seq;
        v->active = false;   // a stolen voice starts clean, not from its old envelope
        startVoice(*v);
        v->active = true;
        lastFreq_ = v->freqTarget;
    }
    void noteOff(int32_t pitch) override {
        // Drop one instance of the key from the mono stack (the oldest), whatever the mode:
        // a key pressed in mono and released after switching to poly must not stay "held".
        int found = -1;
        for (int r = 0; r < monoCount_; ++r) if (monoStack_[r] == pitch) { found = r; break; }
        if (found >= 0) {
            for (int r = found + 1; r < monoCount_; ++r) monoStack_[r - 1] = monoStack_[r];
            --monoCount_;
        }
        if (get(Mono) >= 0.5f && found >= 0) {
            Voice& v = voices_[0];
            if (monoCount_ == 0) { release(v); return; }
            // Back to the newest key still held (or keep sounding the same one, when the
            // key just lifted was an older repeat of it). No new attack — it's one phrase.
            const int top = monoStack_[monoCount_ - 1];
            if (top != v.pitch) {
                v.pitch = top; v.ktOct = (top - 60) / 12.0;
                v.freqTarget = mtof(top);
                lastFreq_ = v.freqTarget;
            }
            return;
        }
        // Poly (or a note that was struck in poly before switching to mono): release the
        // oldest held voice of this pitch — only one, so a newer repeat keeps sounding.
        Voice* oldest = nullptr;
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.aStage != Stage::Release && (!oldest || v.seq < oldest->seq))
                oldest = &v;
        if (oldest) release(*oldest);
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; monoCount_ = 0; }

    void render(float* out, int32_t frames) override {
        // ---- per-block param snapshot ----
        const double shape = std::clamp((double)get(OscShape), 0.0, 1.0);
        const double pw = 0.05 + 0.9 * get(OscPW);
        const double oscMult = pitchMult(OscOctave, OscSemi);
        const float  oscLvl = get(OscLevel);
        const int    subWave = std::clamp((int)std::lround(get(SubWave) * 2.0f), 0, 2);
        const int    subOctShift = -1 - (int)std::lround(get(SubOctave));   // −1 or −2 octaves
        const double subMult = std::exp2(subOctShift);
        const float  subLvl = get(SubLevel);

        const int    ftype = std::clamp((int)std::lround(get(FilType) * 3.0f), 0, 3);
        const bool   slope24 = get(FilSlope) >= 0.5f;
        const double fbase = expMap(get(FilFreq), 30.0, 18000.0);
        const double k = std::clamp(2.0 - 1.9 * get(FilReso), 0.05, 2.0);
        const double preDrive = 1.0 + get(FilDrive) * 11.0;                 // 1..12
        const double fEnvAmt = bip(FilEnvAmt), fKey = get(FilKeyAmt), fLfoAmt = bip(FilLfoAmt);

        const float fA = rateOf(FAttack, 0.001, 2.0), fD = rateOf(FDecay, 0.002, 4.0), fR = rateOf(FRelease, 0.002, 5.0);
        const float fS = get(FSustain);
        const float aA = rateOf(Attack, 0.001, 2.0), aD = rateOf(Decay, 0.002, 4.0), aR = rateOf(Release, 0.002, 5.0);
        const float aS = get(Sustain);

        const double lfoInc = expMap(get(LfoRate), 0.05, 30.0) / sampleRate_;
        const int    lfoWave = std::clamp((int)std::lround(get(LfoWave) * 4.0f), 0, 4);
        const double lfoPitchSemis = bip(LfoPitch) * 12.0;                  // ±1 octave

        const double uni = get(Unison);
        const double uniDet = std::exp2(uni * 22.0 / 1200.0);              // side-osc detune (up to ~22 cents)
        const float  uniLvl = (float)(uni * 0.9);
        const double glide = get(Glide);
        const double glideCoef = glide > 0.001 ? (1.0 - std::exp(-1.0 / (expMap(glide, 0.005, 0.6) * sampleRate_))) : 1.0;
        const float  velAmp = get(VelAmp), velFilt = get(VelFilter);
        // Pitch-bend wheel, in the range the patch declares (1..12 semitones); the mod
        // wheel opens the LFO onto the cutoff, up to ±2 octaves on top of the patch's own.
        const double bendMul = std::exp2((get(Bend) - 0.5) * 2.0 * std::round(1.0 + get(BendRange) * 11.0) / 12.0);
        const double wheelLfo = get(ModWheel) * 2.0;
        // Balance-law pan: centre is unity on both sides, so a centred patch sounds as it
        // always has; a side fades the other channel out.
        const float  pan = (get(OutPan) - 0.5f) * 2.0f;
        const float  panL = std::min(1.0f, 1.0f - pan), panR = std::min(1.0f, 1.0f + pan);
        const double outDrive = 1.0 + get(Drive) * 7.0;                     // 1..8
        const double outMakeup = 1.0 / std::tanh(outDrive);
        const float  volume = get(Volume);
        const double invSr = 1.0 / sampleRate_;

        for (int32_t i = 0; i < frames; ++i) {
            // Shared LFO.
            const double lfo = lfoValue(lfoWave, lfoPhase_, lfoSH_);
            lfoPhase_ += lfoInc; if (lfoPhase_ >= 1.0) { lfoPhase_ -= 1.0; lfoSH_ = noise() * 2.0f - 1.0f; }
            const double pitchMod = (lfoPitchSemis != 0.0 ? std::exp2(lfoPitchSemis * lfo / 12.0) : 1.0) * bendMul;

            float mono = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;
                advanceEnv(v.aEnv, v.aStage, aA, aD, aS, aR);
                if (v.aStage == Stage::Off) { v.active = false; continue; }
                advanceEnv(v.fEnv, v.fStage, fA, fD, fS, fR);

                if (glideCoef < 1.0) v.freqCur += (v.freqTarget - v.freqCur) * glideCoef; else v.freqCur = v.freqTarget;
                const double base = v.freqCur * pitchMod * invSr;

                // Main morph oscillator (+ detuned unison partner) and sub.
                const double iM = base * oscMult, iMu = iM * uniDet;
                double s = morphOsc(shape, v.phM, iM, pw) + morphOsc(shape, v.phMu, iMu, pw) * uniLvl;
                s *= oscLvl / (1.0 + uniLvl);
                v.phM += iM; if (v.phM >= 1.0) v.phM -= 1.0;
                v.phMu += iMu; if (v.phMu >= 1.0) v.phMu -= 1.0;

                const double iS = base * subMult;
                double sub = subOsc(subWave, v.phS) * subLvl;
                v.phS += iS; if (v.phS >= 1.0) v.phS -= 1.0;

                double in = (s + sub);
                if (preDrive > 1.001) in = std::tanh(in * preDrive) / std::tanh(preDrive);

                // Control-rate filter cutoff (cutoff = base × 2^mod).
                if (v.modCount-- <= 0) {
                    v.modCount = 15;
                    const double velF = 1.0f - velFilt + velFilt * v.vel;
                    const double octs = fEnvAmt * 4.0 * v.fEnv * velF + fKey * v.ktOct + (fLfoAmt * 2.0 + wheelLfo) * lfo;
                    const double fc = std::clamp(fbase * std::exp2(octs), 20.0, sampleRate_ * 0.49);
                    setSvf(v.a1, v.a2, v.a3, fc, k);
                }
                double y = svf(v.s1, v.a1, v.a2, v.a3, k, in, ftype);
                if (slope24) y = svf(v.s2, v.a1, v.a2, v.a3, k, y, ftype);

                const float ampG = v.aEnv * (1.0f - velAmp + velAmp * v.vel);
                mono += (float)y * ampG;
            }

            if (outDrive > 1.001) mono = (float)(std::tanh(mono * outDrive) * outMakeup);
            const float o = mono * volume * 0.5f;
            out[i * 2]     += o * panL;
            out[i * 2 + 1] += o * panR;
        }
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool     active = false;
        int32_t  pitch = 0;
        double   freqTarget = 0.0, freqCur = 0.0, ktOct = 0.0;
        double   phM = 0.0, phMu = 0.0, phS = 0.0;      // main / unison / sub phase
        float    vel = 0.0f, aEnv = 0.0f, fEnv = 0.0f;
        Stage    aStage = Stage::Attack, fStage = Stage::Attack;
        double   s1[2] = {0, 0}, s2[2] = {0, 0};        // SVF integrator state (two cascaded stages)
        double   a1 = 0, a2 = 0, a3 = 0;
        int32_t  modCount = 0;
        uint32_t seq = 0;                               // note-on order: a note-off releases the oldest
    };

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    double bip(Param p) const { return (get(p) - 0.5f) * 2.0; }   // 0..1 → -1..+1

    static double mtof(int32_t pitch) { return 440.0 * std::pow(2.0, (pitch - 69) / 12.0); }

    // New attack. A silent voice starts clean — phases at zero, envelopes and filter from
    // rest. A voice that is still sounding (a mono retrigger) keeps its phases, filter
    // state and envelope levels and attacks from where it is, so the retrigger never clicks.
    void startVoice(Voice& v) {
        if (!v.active) {
            v.phM = 0.0; v.phMu = 0.25; v.phS = 0.0;
            v.aEnv = 0.0f; v.fEnv = 0.0f;
            v.s1[0] = v.s1[1] = v.s2[0] = v.s2[1] = 0.0;
            v.a1 = v.a2 = v.a3 = 0.0;
        }
        v.aStage = Stage::Attack;
        v.fStage = Stage::Attack;
        v.modCount = 0;
    }
    static void release(Voice& v) {
        if (!v.active) return;
        v.aStage = Stage::Release; v.fStage = Stage::Release;
    }

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

    // Continuous morph: sine → triangle → saw → pulse (with PWM at the square end).
    static double morphOsc(double shape, double ph, double dt, double pw) {
        const double sine = std::sin(kTwoPi * ph);
        const double tri = 4.0 * std::fabs(ph - 0.5) - 1.0;
        const double saw = (2.0 * ph - 1.0) - polyBlep(ph, dt);
        double sq = ph < pw ? 1.0 : -1.0;
        sq += polyBlep(ph, dt);
        double p2 = ph + (1.0 - pw); if (p2 >= 1.0) p2 -= 1.0;
        sq -= polyBlep(p2, dt);
        const double s = shape * 3.0;
        if (s < 1.0)      return sine + (tri - sine) * s;
        else if (s < 2.0) return tri + (saw - tri) * (s - 1.0);
        else              return saw + (sq - saw) * (s - 2.0);
    }
    // Sub oscillator: 0 sine, 1 square, 2 triangle (naïve — an octave-low signal).
    static double subOsc(int wave, double ph) {
        switch (wave) {
            case 1:  return ph < 0.5 ? 1.0 : -1.0;
            case 2:  return 4.0 * std::fabs(ph - 0.5) - 1.0;
            default: return std::sin(kTwoPi * ph);
        }
    }
    static double polyBlep(double t, double dt) {
        if (dt <= 0.0) return 0.0;
        if (t < dt)       { double x = t / dt;         return x + x - x * x - 1.0; }
        if (t > 1.0 - dt) { double x = (t - 1.0) / dt; return x * x + x + x + 1.0; }
        return 0.0;
    }
    // LFO waveforms: 0 sine, 1 triangle, 2 saw, 3 square, 4 sample & hold (sh held per cycle).
    static double lfoValue(int wave, double ph, float sh) {
        switch (wave) {
            case 1:  return 4.0 * std::fabs(ph - 0.5) - 1.0;
            case 2:  return 2.0 * ph - 1.0;
            case 3:  return ph < 0.5 ? 1.0 : -1.0;
            case 4:  return sh;
            default: return std::sin(kTwoPi * ph);
        }
    }
    float noise() {
        rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5;
        return (rng_ & 0xFFFFFF) / static_cast<float>(0x1000000);
    }

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }
    // Octave (±2) + semi (±12) → frequency multiplier.
    double pitchMult(Param oct, Param semi) const {
        int o = (int)std::lround((get(oct) - 0.5f) * 4.0f);
        int s = (int)std::lround((get(semi) - 0.5f) * 24.0f);
        return std::exp2(o + s / 12.0);
    }

    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.aEnv < q->aEnv) q = &v;
        return q;
    }

    static constexpr int kVoices = 16;
    static constexpr int kMonoStack = 32;
    Voice   voices_[kVoices];
    int     monoStack_[kMonoStack];
    int     monoCount_ = 0;
    double  sampleRate_ = 44100.0;
    double  lfoPhase_ = 0.0;
    float   lfoSH_ = 0.0f;
    double  lastFreq_ = 0.0;    // portamento reference
    uint32_t noteSeq_ = 0;
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
