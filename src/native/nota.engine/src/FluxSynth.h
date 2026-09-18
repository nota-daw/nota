// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Flux — the built-in vector-morph analog synth (instrument kind 11). Its signature
// move is that it LISTENS to a track: a sidechain input becomes a modulation source
// ("React"), not a ducker. The surface is deliberately small (~8 controls), the complexity
// lives inside. Signal flow per voice:
//
//   Vector (X,Y) blends four "timbre worlds" at the pad corners into one voice engine
//     WARM (saw, warm)  · GLASS (bright square) · MOOG (fat, resonant) · GRAIN (gritty/noise)
//   Osc (saw↔square↔noise morph, plus a detuned twin — the adaptive unison) + Sub → drive
//   (tanh) → TPT SVF low-pass → Amp (ADSR), then a shared Space stage (mini stereo reverb +
//   width). Age adds analog wear (per-voice detune drift + hiss). Motion is a tempo-synced
//   internal drift of the vector. Resonance, unison and drive are never set directly: each
//   world carries its own, and the vector blends them ("adaptive").
//
//   React: the sidechain's envelope / transients / spectral tilt drive one Target —
//     Filter, Pitch, Space, or Vector (drags the pad dot in rhythm). With no source
//     assigned React is off: the followers fall to rest and nothing is modulated.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable ids)
// → automation / persist / clone for free. Header-only, allocation-free after construction.
// A per-sample loop keeps modulation smooth; the vector/world + filter coefficients update
// once per block (the vector moves slowly). Name & DSP are Nota's own.

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

class FluxSynth final : public Instrument {
public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        VecX = 0, VecY, Listen, Target, Age, Motion, MotRate, Filter, Env, Space, Glide, Tune, Gain,
        kNumParams
    };
    // Live telemetry (scopeRead) — the editor draws the React scope + the moving pad dot.
    // S_Onsets counts detected transients since construction (the editor turns it into
    // transients per bar); S_Source is 1 while a source is assigned.
    enum Scope { S_Env = 0, S_Transient, S_Tilt, S_React, S_VecX, S_VecY, S_Active, S_Onsets, S_Source, kScopeN };

    FluxSynth() {
        set(VecX, 0.34f); set(VecY, 0.28f);
        set(Listen, 0.72f); set(Target, 1.0f);   // Target: Vector (index 3)
        set(Age, 0.30f); set(Motion, 0.45f); set(MotRate, 0.40f);   // MotRate: 1/4
        set(Filter, 0.62f); set(Env, 0.55f); set(Space, 0.40f);
        set(Glide, 0.0f); set(Tune, 0.5f); set(Gain, 0.72f);
    }

    int32_t kind() const override { return 11; }
    const char* displayName() const override { return "Nota Flux"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        verb_.init(sampleRate_);
    }

    // React: this instrument listens to another track's signal.
    bool    acceptsSidechain() const override { return true; }
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }

    void setTransport(double beatStart, double spb, bool playing) override {
        if (spb > 0.0) spb_ = spb;
        beatStart_ = beatStart; playing_ = playing;
    }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "vecx", "vecy", "listen", "target", "age", "motion", "motrate",
            "filter", "env", "space", "glide", "tune", "gain" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = {
            "Vector X", "Vector Y", "Listen", "Target", "Age", "Motion", "Motion Rate",
            "Filter", "Env", "Space", "Glide", "Tune", "Gain" };
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
        auto s = std::make_shared<FluxSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    int32_t scopeRead(float* out, int32_t maxN) const override {
        const int n = std::min(maxN, (int)kScopeN);
        for (int i = 0; i < n; ++i) out[i] = scope_[i].load(std::memory_order_relaxed);
        return n;
    }

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        const bool glideOn = get(Glide) > 0.001f;
        Voice* v = findFreeVoice();
        v->pitch = pitch;
        v->freqTarget = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        v->freqCur = (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freqTarget;
        v->vel = velocity;
        v->phM = 0.0; v->phMu = noise(); v->phS = 0.0;   // the unison twin starts off-phase
        v->aEnv = 0.0f; v->aStage = Stage::Attack;
        v->s1[0] = v->s1[1] = 0.0;
        v->rndDet = noise() * 2.0f - 1.0f;
        v->pan = (noise() * 2.0f - 1.0f);
        v->active = true;
        v->seq = ++noteSeq_;
        lastFreq_ = v->freqTarget;
    }
    // Releases the oldest held note of this pitch only: a repeated note that starts a hair
    // before the previous one ends keeps sounding when the old one lets go.
    void noteOff(int32_t pitch) override {
        Voice* oldest = nullptr;
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.aStage != Stage::Release && (!oldest || v.seq < oldest->seq)) oldest = &v;
        if (oldest) oldest->aStage = Stage::Release;
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        // ---- 1) React: analyse the sidechain (or self) once per block ------
        analyzeSidechain(frames);
        const int target = std::clamp((int)std::lround(get(Target) * 3.0f), 0, 3);
        const float react = reactAmt_;
        const float filterReact = target == 0 ? react : 0.0f;
        const float pitchReact  = target == 1 ? react : 0.0f;
        const float spaceReact  = target == 2 ? react : 0.0f;
        const bool  vecReact    = target == 3;

        // ---- 2) Motion: tempo-synced internal drift of the vector ----------
        const double motion = get(Motion);
        const int    rIdx = std::clamp((int)std::lround(get(MotRate) * 5.0f), 0, 5);
        const double cycleBeats = kMotBeats[rIdx];
        motionPhase_ += (double)frames / (std::max(1.0, cycleBeats * spb_));
        motionPhase_ -= std::floor(motionPhase_);
        const double mp = motionPhase_ * kTwoPi;
        const double dx = motion * 0.14 * std::sin(mp);
        const double dy = motion * 0.14 * std::sin(mp * 0.5 + 1.2);

        // ---- 3) Effective vector (base + motion + React) → four-world blend -
        double vx = get(VecX) + dx + (vecReact ? react * 0.32 : 0.0);
        double vy = get(VecY) + dy + (vecReact ? react * ((scTilt_ - 0.5) * 0.6 + 0.10) : 0.0);
        vx = std::clamp(vx, 0.0, 1.0); vy = std::clamp(vy, 0.0, 1.0);
        World w = blend(vx, vy);

        // ---- 4) Per-block DSP coefficients from the blended world -----------
        const double cutN = std::clamp(w.cutoff * 0.55 + (get(Filter) - 0.5) * 0.9
                                       + w.bright * 0.18 + filterReact * 0.45, 0.02, 0.99);
        const double fc = std::clamp(expMap(cutN, 50.0, 18000.0), 40.0, sampleRate_ * 0.47);
        const double k = std::clamp(2.0 - 1.9 * w.reso, 0.10, 2.0);
        setSvf(a1_, a2_, a3_, fc, k);
        const double drive = 1.0 + w.drive * 7.0;
        const double driveMk = 1.0 / std::tanh(drive);
        const double waveMix = w.wave;
        const double m01 = std::clamp(waveMix * 2.0, 0.0, 1.0);          // saw → square
        const double noiseMix = std::clamp((waveMix - 0.5) * 2.0, 0.0, 1.0);
        const float  subLvl = (float)w.subLvl;
        // Adaptive unison: a twin of the morph oscillator, detuned by the world's spread
        // (and a little more with Age), mixed in at equal power.
        const double uniMix = std::clamp(w.uni, 0.0, 1.0);
        const double uniDet = std::exp2(uniMix * (7.0 + get(Age) * 8.0) / 1200.0);
        const double uniNorm = 1.0 / std::sqrt(1.0 + uniMix * uniMix);

        // amp ADSR from Env macro (pad ⇠⇢ pluck).
        const double e = get(Env);
        const float aA = rateOf(lerp(0.60, 0.002, e));
        const float aD = rateOf(lerp(0.30, 0.16, e));
        const float aS = (float)lerp(0.85, 0.0, e);
        const float aR = rateOf(lerp(1.20, 0.22, e));

        const double age = get(Age);
        const double tuneMult = std::exp2((get(Tune) - 0.5) * 100.0 / 1200.0);   // ±50 cents
        const double pitchReactMult = std::exp2(pitchReact * 3.0 / 12.0);        // up to +3 semis
        const double glide = get(Glide);
        const double glideCoef = glide > 0.001 ? (1.0 - std::exp(-1.0 / (expMap(glide, 0.005, 0.6) * sampleRate_))) : 1.0;
        const double invSr = 1.0 / sampleRate_;
        const float  gain = (float)(get(Gain) * 0.5);   // headroom for polyphony (voices sum below)
        const float  space = (float)(std::clamp(get(Space) + spaceReact * 0.5f, 0.0f, 1.0f));
        const float  wetAmt = space * 0.6f;
        const double driftInc = 0.4 * invSr;                                     // ~0.4 Hz analog drift

        int active = 0;
        for (int32_t i = 0; i < frames; ++i) {
            driftPhase_ += driftInc; if (driftPhase_ >= 1.0) driftPhase_ -= 1.0;
            const double driftLfo = std::sin(kTwoPi * driftPhase_);

            float mono = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;
                advanceEnv(v.aEnv, v.aStage, aA, aD, aS, aR);
                if (v.aStage == Stage::Off) { v.active = false; continue; }

                if (glideCoef < 1.0) v.freqCur += (v.freqTarget - v.freqCur) * glideCoef; else v.freqCur = v.freqTarget;
                const double cents = age * (v.rndDet * 6.0 + driftLfo * 4.0);
                const double f = v.freqCur * tuneMult * pitchReactMult * std::exp2(cents / 1200.0);
                const double inc = f * invSr;

                // Morph oscillator: saw → square → noise, with its detuned twin.
                double tone = morph(v.phM, inc, m01);
                if (uniMix > 1e-3) {
                    const double inc2 = inc * uniDet;
                    tone = (tone + morph(v.phMu, inc2, m01) * uniMix) * uniNorm;
                    v.phMu += inc2; if (v.phMu >= 1.0) v.phMu -= 1.0;
                }
                tone = tone * (1.0 - noiseMix) + (noise() * 2.0f - 1.0f) * noiseMix;
                v.phM += inc; if (v.phM >= 1.0) v.phM -= 1.0;

                // Sub (sine, one octave down).
                const double sub = std::sin(kTwoPi * v.phS) * subLvl;
                v.phS += inc * 0.5; if (v.phS >= 1.0) v.phS -= 1.0;

                double in = tone + sub;
                in = std::tanh(in * drive) * driveMk;                    // saturation
                double y = svf(v.s1, a1_, a2_, a3_, k, in);              // low-pass
                mono += (float)y * v.aEnv * (0.4f + 0.6f * v.vel);
            }

            // Space: mini stereo reverb + width, mixed by Space.
            float wl = 0.0f, wr = 0.0f;
            verb_.process(mono, wl, wr);
            float l = mono * (1.0f - wetAmt * 0.5f) + wl * wetAmt;
            float r = mono * (1.0f - wetAmt * 0.5f) + wr * wetAmt;
            // Analog hiss (Age).
            const float hiss = (float)(age * 0.0025);
            l += (noise() * 2.0f - 1.0f) * hiss;
            r += (noise() * 2.0f - 1.0f) * hiss;
            // Gentle output soft-clip: dense chords + drive can't push past ±1 into overload.
            l = std::tanh(l * gain);
            r = std::tanh(r * gain);
            out[i * 2]     += l;
            out[i * 2 + 1] += r;
        }

        for (auto& v : voices_) if (v.active) ++active;
        activeVoices_.store(active, std::memory_order_relaxed);

        // Publish telemetry for the editor.
        scope_[S_Env].store(scFast_, std::memory_order_relaxed);
        scope_[S_Transient].store(scTransient_, std::memory_order_relaxed);
        scope_[S_Tilt].store(scTilt_, std::memory_order_relaxed);
        scope_[S_React].store(react, std::memory_order_relaxed);
        scope_[S_VecX].store((float)vx, std::memory_order_relaxed);
        scope_[S_VecY].store((float)vy, std::memory_order_relaxed);
        scope_[S_Active].store((float)active, std::memory_order_relaxed);
        scope_[S_Onsets].store((float)onsets_, std::memory_order_relaxed);
        scope_[S_Source].store(hasSource_ ? 1.0f : 0.0f, std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    // Motion sync cycle length in beats: 1/1, 1/2, 1/4, 1/8, 1/8T, 1/16.
    static constexpr double kMotBeats[6] = { 4.0, 2.0, 1.0, 0.5, 1.0 / 3.0, 0.25 };

    // A timbre "world" at a pad corner. Bilinearly blended by the vector (X,Y).
    // The editor mirrors these (FluxInstrumentCard) to read the cutoff back in Hz.
    struct World { double wave, subLvl, cutoff, reso, drive, bright, uni; };
    //                                   wave   sub   cutoff reso  drive bright unison
    static constexpr World WARM  {0.15, 0.45, 0.42, 0.12, 0.30, 0.20, 0.55};   // top-left
    static constexpr World GLASS {0.70, 0.08, 0.88, 0.30, 0.12, 0.95, 0.35};   // top-right
    static constexpr World MOOG  {0.05, 0.70, 0.38, 0.62, 0.55, 0.10, 0.00};   // bottom-left
    static constexpr World GRAIN {0.85, 0.20, 0.55, 0.22, 0.80, 0.55, 0.25};   // bottom-right
    static World blend(double x, double y) {
        const double wt = (1 - x) * (1 - y), gt = x * (1 - y), mt = (1 - x) * y, rt = x * y;
        auto mix = [&](double a, double b, double c, double d) { return a * wt + b * gt + c * mt + d * rt; };
        return { mix(WARM.wave, GLASS.wave, MOOG.wave, GRAIN.wave),
                 mix(WARM.subLvl, GLASS.subLvl, MOOG.subLvl, GRAIN.subLvl),
                 mix(WARM.cutoff, GLASS.cutoff, MOOG.cutoff, GRAIN.cutoff),
                 mix(WARM.reso, GLASS.reso, MOOG.reso, GRAIN.reso),
                 mix(WARM.drive, GLASS.drive, MOOG.drive, GRAIN.drive),
                 mix(WARM.bright, GLASS.bright, MOOG.bright, GRAIN.bright),
                 mix(WARM.uni, GLASS.uni, MOOG.uni, GRAIN.uni) };
    }

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool    active = false;
        int32_t pitch = 0;
        double  freqTarget = 0.0, freqCur = 0.0;
        double  phM = 0.0, phMu = 0.0, phS = 0.0;
        float   vel = 0.0f, aEnv = 0.0f, rndDet = 0.0f, pan = 0.0f;
        uint32_t seq = 0;   // note-on order, for noteOff
        Stage   aStage = Stage::Attack;
        double  s1[2] = {0, 0};
    };

    // ---- React analysis: fast/slow envelope, transient, spectral tilt -----
    void analyzeSidechain(int32_t frames) {
        double sumSq = 0.0, sumHi = 0.0;
        hasSource_ = scTrackId_.load(std::memory_order_relaxed) >= 0;
        if (hasSource_ && scBuf_ && scFrames_ > 0) {
            const int n = std::min(frames, scFrames_);
            for (int i = 0; i < n; ++i) {
                const float m = 0.5f * (scBuf_[i * 2] + scBuf_[i * 2 + 1]);
                const float lp = scLp_ + 0.06f * (m - scLp_); scLp_ = lp;
                const float hp = m - lp;
                sumSq += (double)m * m; sumHi += (double)hp * hp;
            }
            const float rms = n > 0 ? (float)std::sqrt(sumSq / n) : 0.0f;
            const float hi = n > 0 ? (float)std::sqrt(sumHi / n) : 0.0f;
            const float tilt = std::clamp(hi / (rms + 1e-5f) * 0.8f, 0.0f, 1.0f);
            updateFollowers(rms, tilt, frames);
        } else {
            // No source (or a silent one): the followers fall to rest, tilt to neutral.
            updateFollowers(0.0f, 0.5f, frames);
        }
        // consume the buffer (only valid for this block).
        scBuf_ = nullptr; scFrames_ = 0;
    }
    // The followers run once per block with time constants, so they behave the same at any
    // block size (tuned at 512 frames / 48 kHz: attack ~12 ms, release ~130 ms, slow ~350 ms).
    float coef(int32_t frames, double tauSec) const {
        return (float)(1.0 - std::exp(-(double)std::max<int32_t>(1, frames) / (tauSec * sampleRate_)));
    }
    void updateFollowers(float rms, float tilt, int32_t frames) {
        const float prevFast = scFast_;
        scFast_ += (rms - scFast_) * coef(frames, rms > scFast_ ? 0.0117 : 0.128);
        scSlow_ += (rms - scSlow_) * coef(frames, 0.35);
        // Transient = how far the fast follower jumps above the slow one, relative to it, so
        // a quiet source registers its hits as clearly as a loud one (gated at about −50 dB).
        scTransient_ = scFast_ > 0.003f ? std::clamp((scFast_ / (scSlow_ + 1e-3f) - 1.0f) * 0.5f, 0.0f, 1.0f) : 0.0f;
        scTilt_ += (tilt - scTilt_) * coef(frames, 0.066);
        // An onset: the fast follower jumps (by a fifth or more) while standing clear of the
        // slow one. One per hit — it re-arms only once the level has fallen a third below the
        // hit's peak, so the ripple of a low note inside one hit doesn't count twice.
        if (!onsetArmed_) {
            onsetPeak_ = std::max(onsetPeak_, scFast_);
            if (scFast_ < onsetPeak_ * 0.67f) onsetArmed_ = true;
        } else if (scTransient_ > 0.35f && scFast_ > prevFast * 1.2f) {
            ++onsets_; onsetArmed_ = false; onsetPeak_ = scFast_;
        }
        const float env01 = std::clamp(scFast_ * 3.5f, 0.0f, 1.0f);
        reactAmt_ = hasSource_ ? get(Listen) * env01 : 0.0f;
    }

    // ---- helpers ----------------------------------------------------------
    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double lerp(double a, double b, double t) { return a + (b - a) * t; }

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
    static double svf(double* st, double a1, double a2, double a3, double k, double in) {
        const double v3 = in - st[1];
        const double v1 = a1 * st[0] + a2 * v3;
        const double v2 = st[1] + a2 * st[0] + a3 * v3;
        st[0] = 2.0 * v1 - st[0];
        st[1] = 2.0 * v2 - st[1];
        (void)k;
        return v2;   // low-pass
    }
    // One sample of the saw → square morph at phase `ph` (band-limited with polyBLEP).
    static double morph(double ph, double inc, double m01) {
        const double saw = (2.0 * ph - 1.0) - polyBlep(ph, inc);
        double sq = ph < 0.5 ? 1.0 : -1.0;
        sq += polyBlep(ph, inc);
        double p2 = ph + 0.5; if (p2 >= 1.0) p2 -= 1.0;
        sq -= polyBlep(p2, inc);
        return saw + (sq - saw) * m01;
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
    float rateOf(double seconds) const { return (float)(1.0 / (std::max(1e-4, seconds) * sampleRate_)); }
    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.aEnv < q->aEnv) q = &v;
        return q;
    }

    // ---- mini stereo reverb (Schroeder: 2 combs + 1 allpass per channel) --
    struct MiniVerb {
        std::vector<float> c0L, c1L, c0R, c1R, apL, apR;
        int i0L = 0, i1L = 0, i0R = 0, i1R = 0, iaL = 0, iaR = 0;
        void init(double sr) {
            auto mk = [&](std::vector<float>& b, double sec) { b.assign(std::max(1, (int)(sec * sr)), 0.0f); };
            mk(c0L, 0.0297); mk(c1L, 0.0371); mk(c0R, 0.0411); mk(c1R, 0.0437);
            mk(apL, 0.0050); mk(apR, 0.0057);
            i0L = i1L = i0R = i1R = iaL = iaR = 0;
        }
        static float comb(std::vector<float>& b, int& idx, float in, float fb) {
            float y = b[idx]; b[idx] = in + y * fb; if (++idx >= (int)b.size()) idx = 0; return y;
        }
        static float allpass(std::vector<float>& b, int& idx, float in, float g) {
            float bufout = b[idx]; float y = -in + bufout; b[idx] = in + bufout * g;
            if (++idx >= (int)b.size()) idx = 0; return y;
        }
        void process(float in, float& l, float& r) {
            const float fb = 0.77f;
            float L = comb(c0L, i0L, in, fb) + comb(c1L, i1L, in, fb);
            float R = comb(c0R, i0R, in, fb) + comb(c1R, i1R, in, fb);
            l = allpass(apL, iaL, L * 0.5f, 0.7f);
            r = allpass(apR, iaR, R * 0.5f, 0.7f);
        }
    };

    static constexpr int kVoices = 16;
    Voice   voices_[kVoices];
    MiniVerb verb_;
    double  sampleRate_ = 44100.0;
    double  spb_ = 22050.0, beatStart_ = 0.0;
    bool    playing_ = false;
    double  motionPhase_ = 0.0, driftPhase_ = 0.0;
    double  lastFreq_ = 0.0;
    uint32_t noteSeq_ = 0;
    double  a1_ = 0.0, a2_ = 0.0, a3_ = 0.0;   // shared filter coeffs (block rate)
    uint32_t rng_ = 0x9E3779B9u;

    // React state.
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    std::atomic<int32_t> scTrackId_{-1};
    float scLp_ = 0.0f, scFast_ = 0.0f, scSlow_ = 0.0f, scTransient_ = 0.0f, scTilt_ = 0.5f;
    float reactAmt_ = 0.0f;
    bool  hasSource_ = false, onsetArmed_ = true;
    float onsetPeak_ = 0.0f;
    uint32_t onsets_ = 0;

    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> scope_[kScopeN];
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
