// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Monolith — the built-in monophonic subtractive synth (kind 13), a musically
// faithful emulation of the Minimoog Model D architecture (three oscillators → mixer
// → Moog transistor-ladder low-pass → two contour generators). It is Nota's own DSP,
// not a schematic clone. Signal flow (single mono voice, optional unison stack):
//
//   Osc1 ┐
//   Osc2 ┼─→ Mixer ─(+ noise / ext / feedback overload)─→ Ladder LP (24 dB/oct) ─→ VCA ─→ out
//   Osc3 ┘     ▲                                              ▲ Filter Contour        ▲ Loudness Contour
//   noise──────┘        Osc3 / noise → Osc Mod (via Mod Wheel)┘  + keytrack + Filter Mod
//
// Model-D authenticity kept here: six waveforms per oscillator (triangle, shark-tooth,
// saw, square, wide/narrow pulse; Osc3 swaps shark-tooth for reverse saw), foot ranges
// (LO / 32'..2'), ±7-semitone detune on Osc2/3, Osc3 keyboard-control defeat (free LFO),
// oscillator modulation and filter modulation driven by the mod wheel, a self-oscillating
// ladder with keyboard tracking (1/3 · 2/3), a single Decay (release) switch shared by both
// contours, glide, note-priority mono logic (low / high / last), single/multi trigger
// (Legato), unison with detune, mixer feedback/overload, and gentle analog drift.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable ids)
// → automation / persist / clone for free. Header-only; allocation-free after construction.
// A per-sample loop keeps modulation smooth; the ladder oversamples ×2 internally and the
// filter coefficients update at control rate.

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

class Monolith final : public Instrument {
public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        // --- controllers / global (0..20) ---
        Tune = 0, Glide, GlideOn, ModMix, OscModOn, FilterModOn, Osc3Kbd, Drift,
        Legato, DecayOn, A440, BendRange, Priority, Unison, UniDetune, Volume,
        BassComp, VelVca, VelVcf, Bend, ModWheel,
        // --- oscillators (21..28) ---
        O1Range, O1Wave,
        O2Range, O2Tune, O2Wave,
        O3Range, O3Tune, O3Wave,
        // --- mixer (29..40) ---
        Mix1Lvl, Mix1On, Mix2Lvl, Mix2On, Mix3Lvl, Mix3On,
        NoiseLvl, NoiseOn, NoiseType, ExtLvl, ExtOn, Feedback,
        // --- filter (41..45) ---
        Cutoff, Emph, Contour, Kbd1, Kbd2,
        // --- filter contour (46..48) ---
        FAttack, FDecay, FSustain,
        // --- loudness contour (49..51) ---
        AAttack, ADecay, ASustain,
        kNumParams
    };

    Monolith() {
        // Classic Model-D init: three saws, Osc1 8', Osc2 8' barely detuned, Osc3 8' free
        // is muted; ladder half open with a snappy filter contour and a fast amp contour.
        set(Tune, 0.5f); set(Glide, 0.0f); set(GlideOn, 0.0f); set(ModMix, 0.0f);
        set(OscModOn, 0.0f); set(FilterModOn, 0.0f); set(Osc3Kbd, 1.0f); set(Drift, 0.15f);
        set(Legato, 0.0f); set(DecayOn, 0.0f); set(A440, 0.0f); set(BendRange, 0.0f);
        set(Priority, 1.0f); set(Unison, 0.0f); set(UniDetune, 0.3f); set(Volume, 0.8f);
        set(BassComp, 0.0f); set(VelVca, 0.0f); set(VelVcf, 0.0f); set(Bend, 0.5f); set(ModWheel, 0.0f);
        set(O1Range, 0.6f); set(O1Wave, 0.4f);
        set(O2Range, 0.6f); set(O2Tune, 0.5f); set(O2Wave, 0.4f);
        set(O3Range, 0.6f); set(O3Tune, 0.5f); set(O3Wave, 0.4f);
        set(Mix1Lvl, 0.85f); set(Mix1On, 1.0f); set(Mix2Lvl, 0.6f); set(Mix2On, 0.0f);
        set(Mix3Lvl, 0.5f); set(Mix3On, 0.0f);
        set(NoiseLvl, 0.3f); set(NoiseOn, 0.0f); set(NoiseType, 0.0f); set(ExtLvl, 0.0f); set(ExtOn, 0.0f); set(Feedback, 0.0f);
        set(Cutoff, 0.5f); set(Emph, 0.15f); set(Contour, 0.5f); set(Kbd1, 0.0f); set(Kbd2, 0.0f);
        set(FAttack, 0.04f); set(FDecay, 0.4f); set(FSustain, 0.3f);
        set(AAttack, 0.02f); set(ADecay, 0.5f); set(ASustain, 0.85f);
    }

    int32_t kind() const override { return 13; }
    const char* displayName() const override { return "Nota Monolith"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }
    int32_t activeVoiceCount() const override { return active_.load(std::memory_order_relaxed); }
    int32_t heldNotes(int32_t* out, int32_t maxN) const override {
        int n = std::min<int>(heldN_, maxN);
        for (int i = 0; i < n; ++i) out[i] = held_[i];
        return n;
    }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "tune", "glide", "glideon", "modmix", "oscmodon", "filtmodon", "osc3kbd", "drift",
            "legato", "decayon", "a440", "bendrange", "priority", "unison", "unidetune", "volume",
            "basscomp", "velvca", "velvcf", "bend", "modwheel",
            "o1range", "o1wave",
            "o2range", "o2tune", "o2wave",
            "o3range", "o3tune", "o3wave",
            "mix1lvl", "mix1on", "mix2lvl", "mix2on", "mix3lvl", "mix3on",
            "noiselvl", "noiseon", "noisetype", "extlvl", "exton", "feedback",
            "cutoff", "emph", "contour", "kbd1", "kbd2",
            "fattack", "fdecay", "fsustain",
            "aattack", "adecay", "asustain" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string();
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = {
            "Master Tune", "Glide Time", "Glide On", "Mod Mix", "Osc-Mod On", "Filter-Mod On", "Osc3 Keyboard", "Drift",
            "Legato Mode", "Decay On", "A-440 Tone", "Bend Range", "Note Priority", "Unison Voices", "Unison Detune", "Output Volume",
            "Bass Compensation", "Velocity Amp", "Velocity Filter", "Pitch Bend", "Mod Wheel",
            "Osc1 Range", "Osc1 Wave",
            "Osc2 Range", "Osc2 Tune", "Osc2 Wave",
            "Osc3 Range", "Osc3 Tune", "Osc3 Wave",
            "Mixer Osc1 Level", "Mixer Osc1 On", "Mixer Osc2 Level", "Mixer Osc2 On", "Mixer Osc3 Level", "Mixer Osc3 On",
            "Mixer Noise Level", "Mixer Noise On", "Mixer Noise Type", "Mixer External Level", "Mixer External On", "Mixer Feedback",
            "Filter Cutoff", "Filter Emphasis", "Filter Contour", "Filter Keyboard 1/3", "Filter Keyboard 2/3",
            "Filter-Env Attack", "Filter-Env Decay", "Filter-Env Sustain",
            "Amp-Env Attack", "Amp-Env Decay", "Amp-Env Sustain" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string();
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
        auto s = std::make_shared<Monolith>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- note events (monophonic with a held-note priority stack) ---------
    void noteOn(int32_t pitch, float velocity) override {
        pushHeld(pitch);
        const bool glideOn = get(GlideOn) > 0.5f;
        const bool legato  = get(Legato) > 0.5f;
        const bool wasSilent = (aStage_ == Stage::Off);
        const int  sel = selectPitch();
        if (sel < 0) return;
        vel_ = velocity;
        const bool changed = (sel != sounding_) || wasSilent;
        sounding_ = sel;
        freqTarget_ = noteHz(sel);
        if (wasSilent) { freqCur_ = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : freqTarget_; retrigger(); }
        else if (changed) {
            if (!glideOn) freqCur_ = freqTarget_;
            if (!legato) retrigger();
        }
        lastFreq_ = freqTarget_;
    }
    void noteOff(int32_t pitch) override {
        removeHeld(pitch);
        const bool glideOn = get(GlideOn) > 0.5f;
        const bool legato  = get(Legato) > 0.5f;
        if (heldN_ == 0) { aStage_ = Stage::Release; fStage_ = Stage::Release; return; }
        const int sel = selectPitch();
        if (sel != sounding_) {
            sounding_ = sel;
            freqTarget_ = noteHz(sel);
            if (!glideOn) freqCur_ = freqTarget_;
            if (!legato) retrigger();
            lastFreq_ = freqTarget_;
        }
    }
    void allNotesOff() override { heldN_ = 0; sounding_ = -1; aStage_ = Stage::Off; fStage_ = Stage::Off; aEnv_ = fEnv_ = 0.0f; }

    void render(float* out, int32_t frames) override {
        // ---- per-block snapshot ----
        const double invSr = 1.0 / sampleRate_;
        const double masterMult = std::exp2((get(Tune) - 0.5) * 2.0 * 2.5 / 12.0);   // ±2.5 st
        const double maxBend = 2.0 + get(BendRange) * 10.0;                          // ±2..±12 st
        const double bendMult = std::exp2((get(Bend) - 0.5) * 2.0 * maxBend / 12.0);
        const int  w1 = wave6(O1Wave), w2 = wave6(O2Wave), w3 = wave6(O3Wave);
        const double rm1 = rangeMult(O1Range), rm2 = rangeMult(O2Range), rm3 = rangeMult(O3Range);
        const double det2 = std::exp2((get(O2Tune) - 0.5) * 2.0 * 7.0 / 12.0);
        const double det3 = std::exp2((get(O3Tune) - 0.5) * 2.0 * 7.0 / 12.0);
        const bool  osc3Kbd = get(Osc3Kbd) > 0.5f;
        const double freeHz3 = 13.75 * rangeMult(O3Range) * det3;   // free-run Osc3 (LFO/drone)

        const bool  m1on = get(Mix1On) > 0.5f, m2on = get(Mix2On) > 0.5f, m3on = get(Mix3On) > 0.5f;
        const bool  nzOn = get(NoiseOn) > 0.5f, extOn = get(ExtOn) > 0.5f;
        const float l1 = m1on ? get(Mix1Lvl) : 0.0f, l2 = m2on ? get(Mix2Lvl) : 0.0f, l3 = m3on ? get(Mix3Lvl) : 0.0f;
        const float nzLvl = nzOn ? get(NoiseLvl) : 0.0f;
        const bool  pink = get(NoiseType) > 0.5f;
        const float feedAmt = get(Feedback) + (extOn ? get(ExtLvl) * 0.5f : 0.0f);

        const bool  oscMod = get(OscModOn) > 0.5f, filtMod = get(FilterModOn) > 0.5f;
        const double modMix = get(ModMix);
        const float modWheel = get(ModWheel);

        const double cutBase = expMap(get(Cutoff), 16.0, 20000.0);
        const double reso = get(Emph);
        const double contour = get(Contour);
        const double keyAmt = (get(Kbd1) > 0.5f ? 1.0 / 3.0 : 0.0) + (get(Kbd2) > 0.5f ? 2.0 / 3.0 : 0.0);
        const float velVcf = get(VelVcf), velVca = get(VelVca);
        const bool  bassComp = get(BassComp) > 0.5f;

        const bool decayOn = get(DecayOn) > 0.5f;
        const float fA = rateOf(FAttack, 0.001, 10.0), fD = rateOf(FDecay, 0.004, 20.0);
        const float fS = get(FSustain);
        const float aA = rateOf(AAttack, 0.001, 10.0), aD = rateOf(ADecay, 0.004, 20.0);
        const float aS = get(ASustain);
        const float fRel = decayOn ? fD : rateOf2(0.004);   // Decay switch off → near-instant release
        const float aRel = decayOn ? aD : rateOf2(0.004);

        const double glide = get(Glide);
        const double glideCoef = (get(GlideOn) > 0.5f && glide > 0.001)
            ? (1.0 - std::exp(-1.0 / (expMap(glide, 0.002, 10.0) * sampleRate_))) : 1.0;

        const float volume = get(Volume);

        // Unison: N detuned copies of the whole oscillator bank.
        const int uN = 1 + (int)std::lround(get(Unison) * (kUni - 1));
        const double uCents = get(UniDetune) * 50.0;
        double uMult[kUni]; float uPanL[kUni], uPanR[kUni];
        for (int u = 0; u < uN; ++u) {
            double s = uN > 1 ? (2.0 * u / (uN - 1) - 1.0) : 0.0;       // -1..+1
            uMult[u] = std::exp2(uCents * s / 1200.0);
            float p = 0.5f + 0.5f * (float)s * (float)get(UniDetune);   // spread pan with detune
            uPanL[u] = std::cos(p * 0.5f * (float)kPi);
            uPanR[u] = std::sin(p * 0.5f * (float)kPi);
        }
        const float uGain = 1.0f / std::sqrt((float)uN);

        // Drift: very slow random detune per oscillator, depth from the Drift param.
        const double driftDepth = get(Drift) * 0.012;   // up to ~±20 cents
        const double driftInc = 0.7 * invSr;

        const double modWheelD = modWheel;
        int active = 0;

        for (int32_t i = 0; i < frames; ++i) {
            advanceEnv(aEnv_, aStage_, aA, aD, aS, aRel);
            if (aStage_ == Stage::Off && heldN_ == 0) { /* silent */ }
            advanceEnv(fEnv_, fStage_, fA, fD, fS, fRel);

            // Slow drift update.
            driftPhase_ += driftInc; if (driftPhase_ >= 1.0) { driftPhase_ -= 1.0; for (int o = 0; o < 3; ++o) driftTarget_[o] = (noise() * 2.0f - 1.0f); }
            for (int o = 0; o < 3; ++o) drift_[o] += 0.0006 * (driftTarget_[o] - drift_[o]);

            // Glide the pitch.
            if (glideCoef < 1.0) freqCur_ += (freqTarget_ - freqCur_) * glideCoef; else freqCur_ = freqTarget_;

            // Noise source (white or pink via a simple one-pole tilt).
            const float white = noise() * 2.0f - 1.0f;
            pinkState_ += 0.03f * (white - pinkState_);
            const float nz = (pink ? (pinkState_ * 2.2f) : white);

            // Osc3 sample first (it may feed the mixer and/or oscillator/filter modulation).
            const double f3base = (osc3Kbd ? freqCur_ * rm3 * det3 * masterMult * bendMult : freeHz3)
                                  * std::exp2(drift_[2] * driftDepth);
            double s3 = 0.0;
            {
                const double inc = f3base * invSr;
                s3 = osc3(w3, ph3_[0], inc);
                ph3_[0] += inc; if (ph3_[0] >= 1.0) ph3_[0] -= 1.0;
            }
            const double modSrc = s3 * (1.0 - modMix) + nz * modMix;          // Osc3 ↔ noise
            const double oscModMult = oscMod ? std::exp2(modSrc * modWheelD * 0.5) : 1.0;  // ±0.5 oct
            const double filtModOct = filtMod ? modSrc * modWheelD * 2.0 : 0.0;             // ±2 oct

            // Oscillator bank (with unison), Osc1 & Osc2 keyboard-tracked.
            const double f1 = freqCur_ * rm1 * masterMult * bendMult * oscModMult * std::exp2(drift_[0] * driftDepth);
            const double f2 = freqCur_ * rm2 * det2 * masterMult * bendMult * oscModMult * std::exp2(drift_[1] * driftDepth);

            float mixL = 0.0f, mixR = 0.0f;
            for (int u = 0; u < uN; ++u) {
                double o1 = 0, o2 = 0;
                if (l1 > 0.0f) { double inc = f1 * uMult[u] * invSr; o1 = osc12(w1, ph1_[u], inc); ph1_[u] += inc; if (ph1_[u] >= 1.0) ph1_[u] -= 1.0; }
                if (l2 > 0.0f) { double inc = f2 * uMult[u] * invSr; o2 = osc12(w2, ph2_[u], inc); ph2_[u] += inc; if (ph2_[u] >= 1.0) ph2_[u] -= 1.0; }
                float s = (float)(o1 * l1 + o2 * l2) + (float)(s3 * l3 + nz * nzLvl) * (u == 0 ? 1.0f : 0.0f);
                mixL += s * uPanL[u]; mixR += s * uPanR[u];
            }
            float mix = (mixL * 0.70711f + mixR * 0.70711f) * uGain;   // collapse to mono into one ladder

            // Feedback / overload: previous output back into the filter input, soft-clipped.
            mix += (float)fbState_ * feedAmt * 0.9f;
            mix = std::tanh(mix * (1.0f + feedAmt * 2.0f));

            // Control-rate ladder cutoff.
            if (--modCount_ <= 0) {
                modCount_ = 15;
                const double velF = 1.0f - velVcf + velVcf * vel_;
                const double keyOct = keyAmt * (sounding_ >= 0 ? (sounding_ - 60) / 12.0 : 0.0);
                double oct = (contour * 6.0) * (fEnv_ * velF) + keyOct + filtModOct;
                cutHz_ = std::clamp(cutBase * std::exp2(oct), 16.0, sampleRate_ * 0.48);
                ladderSet(cutHz_, reso);
            }
            double lp = ladder(mix, bassComp);

            float amp = aEnv_ * (1.0f - velVca + velVca * vel_);
            float o = (float)lp * amp;
            fbState_ = o;
            const float outv = o * volume;
            out[i * 2]     += outv;
            out[i * 2 + 1] += outv;
        }
        if (aStage_ != Stage::Off) active = 1;
        active_.store(active, std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr int kUni = 7;

    enum class Stage { Attack, Decay, Sustain, Release, Off };

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const { return (float)(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float rateOf2(double sec) const { return (float)(1.0 / (sec * sampleRate_)); }
    double noteHz(int pitch) const { return 440.0 * std::pow(2.0, (pitch - 69) / 12.0); }
    int wave6(Param p) const { return std::clamp((int)std::lround(get(p) * 5.0f), 0, 5); }
    // Range knob: LO / 32' / 16' / 8' / 4' / 2'  →  octave offset vs 8' reference.
    double rangeMult(Param p) const {
        static const double m[6] = { 0.0625, 0.25, 0.5, 1.0, 2.0, 4.0 };   // LO,32,16,8,4,2
        return m[std::clamp((int)std::lround(get(p) * 5.0f), 0, 5)];
    }

    void retrigger() { aStage_ = Stage::Attack; fStage_ = Stage::Attack; }

    void pushHeld(int pitch) {
        for (int i = 0; i < heldN_; ++i) if (held_[i] == pitch) return;
        if (heldN_ < 128) held_[heldN_++] = pitch;
    }
    void removeHeld(int pitch) {
        int w = 0;
        for (int r = 0; r < heldN_; ++r) if (held_[r] != pitch) held_[w++] = held_[r];
        heldN_ = w;
    }
    int selectPitch() const {
        if (heldN_ == 0) return -1;
        int mode = std::clamp((int)std::lround(get(Priority) * 2.0f), 0, 2);   // 0 low, 1 high, 2 last
        if (mode == 2) return held_[heldN_ - 1];
        int best = held_[0];
        for (int i = 1; i < heldN_; ++i) { if (mode == 0 && held_[i] < best) best = held_[i]; if (mode == 1 && held_[i] > best) best = held_[i]; }
        return best;
    }

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
        switch (st) {
            case Stage::Attack:  env += atk; if (env >= 1.0f) { env = 1.0f; st = Stage::Decay; } break;
            case Stage::Decay:   env -= dec; if (env <= sus)  { env = sus;  st = Stage::Sustain; } break;
            case Stage::Sustain: env = sus; break;
            case Stage::Release: env -= rel; if (env <= 0.0f) { env = 0.0f; st = Stage::Off; } break;
            case Stage::Off:     break;
        }
    }

    // ---- oscillators ------------------------------------------------------
    // Osc1/Osc2 waveforms: 0 tri, 1 shark-tooth, 2 saw, 3 square, 4 wide pulse, 5 narrow pulse.
    static double osc12(int w, double ph, double dt) {
        switch (w) {
            case 0:  return tri(ph);
            case 1:  return 0.55 * tri(ph) + 0.55 * sawB(ph, dt);          // shark-tooth
            case 2:  return sawB(ph, dt);
            case 3:  return pulseB(ph, dt, 0.5);
            case 4:  return pulseB(ph, dt, 0.30);
            default: return pulseB(ph, dt, 0.12);
        }
    }
    // Osc3 waveforms: 0 tri, 1 reverse saw, 2 saw, 3 square, 4 wide pulse, 5 narrow pulse.
    static double osc3(int w, double ph, double dt) {
        switch (w) {
            case 0:  return tri(ph);
            case 1:  return -sawB(ph, dt);                                 // reverse saw
            case 2:  return sawB(ph, dt);
            case 3:  return pulseB(ph, dt, 0.5);
            case 4:  return pulseB(ph, dt, 0.30);
            default: return pulseB(ph, dt, 0.12);
        }
    }
    static double tri(double ph) { return 4.0 * std::fabs(ph - 0.5) - 1.0; }
    static double sawB(double ph, double dt) { return (2.0 * ph - 1.0) - polyBlep(ph, dt); }
    static double pulseB(double ph, double dt, double duty) {
        double s = ph < duty ? 1.0 : -1.0;
        s += polyBlep(ph, dt);
        double p2 = ph - duty; if (p2 < 0.0) p2 += 1.0;
        s -= polyBlep(p2, dt);
        return s;
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

    // ---- Moog transistor-ladder low-pass (tanh state-difference model, ×2 oversampled) ----
    // Four cascaded non-linear one-poles with a resonance feedback tap; self-oscillates near
    // the top of Emphasis. Poles are tuned a touch above the cutoff knob so the passband stays
    // open (a straight ladder reads dark) while the resonant peak still tracks the knob.
    static double ftanh(double x) { return std::tanh(std::clamp(x, -4.0, 4.0)); }
    void ladderSet(double fc, double reso) {
        const double srOs = sampleRate_ * 2.0;
        const double fpole = std::min(fc * 1.35, srOs * 0.45);
        ladderF_ = std::clamp(1.0 - std::exp(-kTwoPi * fpole / srOs), 0.0, 0.995);
        ladderReso_ = std::clamp(reso, 0.0, 1.0);
        ladderFb_ = ladderReso_ * 4.2;                  // → self-oscillation at max
    }
    double ladder(double x, bool bassComp) {
        const double f = ladderF_, fb = ladderFb_;
        for (int os = 0; os < 2; ++os) {
            const double in = x - fb * ftanh(l4_);
            l1_ += f * (ftanh(in)  - ftanh(l1_));
            l2_ += f * (ftanh(l1_) - ftanh(l2_));
            l3_ += f * (ftanh(l2_) - ftanh(l3_));
            l4_ += f * (ftanh(l3_) - ftanh(l4_));
        }
        double y = l4_ * (1.0 + ladderReso_ * 0.7);      // makeup for resonant passband loss
        if (bassComp) y += x * (ladderReso_ * 0.20);     // restore low end lost to resonance
        return y;
    }

    // ---- state ------------------------------------------------------------
    static constexpr int kMaxUni = kUni;
    double sampleRate_ = 44100.0;

    int   held_[128] = { 0 }; int heldN_ = 0;
    int   sounding_ = -1;
    float vel_ = 0.0f;
    double freqTarget_ = 0.0, freqCur_ = 0.0, lastFreq_ = 0.0;

    float aEnv_ = 0.0f, fEnv_ = 0.0f;
    Stage aStage_ = Stage::Off, fStage_ = Stage::Off;

    double ph1_[kUni] = { 0 }, ph2_[kUni] = { 0 }, ph3_[1] = { 0 };
    float  pinkState_ = 0.0f;
    double drift_[3] = { 0, 0, 0 }, driftTarget_[3] = { 0, 0, 0 }, driftPhase_ = 0.0;

    int    modCount_ = 0; double cutHz_ = 1000.0;
    double ladderF_ = 0.1, ladderFb_ = 0.0, ladderReso_ = 0.0;
    double l1_ = 0, l2_ = 0, l3_ = 0, l4_ = 0;
    double fbState_ = 0.0;

    uint32_t rng_ = 0x1234567u;
    std::atomic<int32_t> active_{ 0 };
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
