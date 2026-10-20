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
// → automation / persist / clone for free. Allocation-free after
// construction. A per-sample loop keeps modulation smooth; the vector/world + filter
// coefficients update once per block (the vector moves slowly). Name & DSP are Nota's own.
//
// DSP notes: the sub-oscillator sine DEFINES pitch and stays std::-exact; fast_math::sin
// is used only for the slow Motion drift and the analog-drift LFO (gain/detune wobble,
// never the pitch itself), and fast_math::tanhPrecise for the downstream drive
// saturation and output soft-clip. Sleep: release + Space tails ring past note-off
// (hasTail), auto-sleep after a silent hangover (engine skips render while idle;
// noteOn/noteOff/setParam wake).

#pragma once

#include "devices/Instrument.h"

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
        VecX = 0,
        VecY,
        Listen,
        Target,
        Age,
        Motion,
        MotRate,
        Filter,
        Env,
        Space,
        Glide,
        Tune,
        Gain,
        kNumParams
    };
    // Live telemetry (scopeRead) — the editor draws the React scope + the moving pad dot.
    // S_Onsets counts detected transients since construction (the editor turns it into
    // transients per bar); S_Source is 1 while a source is assigned.
    enum Scope { S_Env = 0, S_Transient, S_Tilt, S_React, S_VecX, S_VecY, S_Active, S_Onsets, S_Source, kScopeN };

    FluxSynth();

    int32_t kind() const override { return 11; }
    const char* displayName() const override { return "Nota Flux"; }

    void setSampleRate(double sr) override;

    // React: this instrument listens to another track's signal.
    bool acceptsSidechain() const override { return true; }
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }

    void setTransport(double beatStart, double spb, bool playing) override;

    // Sleep contract: release + Space tails ring past note-off (the engine-visible tail).
    // Settles after the hangover; noteOn/noteOff/setParam wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override;
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    int32_t scopeRead(float* out, int32_t maxN) const override;

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    // Releases the oldest held note of this pitch only: a repeated note that starts a hair
    // before the previous one ends keeps sounding when the old one lets go.
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    // Motion sync cycle length in beats: 1/1, 1/2, 1/4, 1/8, 1/8T, 1/16.
    static constexpr double kMotBeats[6] = {4.0, 2.0, 1.0, 0.5, 1.0 / 3.0, 0.25};

    // A timbre "world" at a pad corner. Bilinearly blended by the vector (X,Y).
    // The editor mirrors these (FluxInstrumentCard) to read the cutoff back in Hz.
    struct World {
        double wave, subLvl, cutoff, reso, drive, bright, uni;
    };
    //                                   wave   sub   cutoff reso  drive bright unison
    static constexpr World WARM{0.15, 0.45, 0.42, 0.12, 0.30, 0.20, 0.55};  // top-left
    static constexpr World GLASS{0.70, 0.08, 0.88, 0.30, 0.12, 0.95, 0.35}; // top-right
    static constexpr World MOOG{0.05, 0.70, 0.38, 0.62, 0.55, 0.10, 0.00};  // bottom-left
    static constexpr World GRAIN{0.85, 0.20, 0.55, 0.22, 0.80, 0.55, 0.25}; // bottom-right
    static World blend(double x, double y);

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        double freqTarget = 0.0, freqCur = 0.0;
        double phM = 0.0, phMu = 0.0, phS = 0.0;
        float vel = 0.0f, aEnv = 0.0f, rndDet = 0.0f, pan = 0.0f;
        uint32_t seq = 0; // note-on order, for noteOff
        Stage aStage = Stage::Attack;
        double s1[2] = {0, 0};
    };

    // ---- React analysis: fast/slow envelope, transient, spectral tilt -----
    void analyzeSidechain(int32_t frames);
    // The followers run once per block with time constants, so they behave the same at any
    // block size (tuned at 512 frames / 48 kHz: attack ~12 ms, release ~130 ms, slow ~350 ms).
    float coef(int32_t frames, double tauSec) const;
    void updateFollowers(float rms, float tilt, int32_t frames);

    // ---- helpers ----------------------------------------------------------
    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double lerp(double a, double b, double t) { return a + (b - a) * t; }

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);
    void setSvf(double& a1, double& a2, double& a3, double fc, double k) const;
    static double svf(double* st, double a1, double a2, double a3, double k, double in);
    // One sample of the saw → square morph at phase `ph` (band-limited with polyBLEP).
    static double morph(double ph, double inc, double m01);
    static double polyBlep(double t, double dt);
    float noise();
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(double seconds) const { return (float)(1.0 / (std::max(1e-4, seconds) * sampleRate_)); }
    Voice* findFreeVoice();

    // ---- mini stereo reverb (Schroeder: 2 combs + 1 allpass per channel) --
    struct MiniVerb {
        std::vector<float> c0L, c1L, c0R, c1R, apL, apR;
        int i0L = 0, i1L = 0, i0R = 0, i1R = 0, iaL = 0, iaR = 0;
        void init(double sr);
        static float comb(std::vector<float>& b, int& idx, float in, float fb);
        static float allpass(std::vector<float>& b, int& idx, float in, float g);
        void process(float in, float& l, float& r);
    };

    static constexpr int kVoices = 16;
    Voice voices_[kVoices];
    MiniVerb verb_;
    double sampleRate_ = 44100.0;
    double spb_ = 22050.0, beatStart_ = 0.0;
    bool playing_ = false;
    double motionPhase_ = 0.0, driftPhase_ = 0.0;
    double lastFreq_ = 0.0;
    uint32_t noteSeq_ = 0;
    double a1_ = 0.0, a2_ = 0.0, a3_ = 0.0; // shared filter coeffs (block rate)
    uint32_t rng_ = 0x9E3779B9u;

    // React state.
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    std::atomic<int32_t> scTrackId_{-1};
    float scLp_ = 0.0f, scFast_ = 0.0f, scSlow_ = 0.0f, scTransient_ = 0.0f, scTilt_ = 0.5f;
    float reactAmt_ = 0.0f;
    bool hasSource_ = false, onsetArmed_ = true;
    float onsetPeak_ = 0.0f;
    uint32_t onsets_ = 0;

    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> scope_[kScopeN];
    std::atomic<float> pn_[kNumParams];

    // Auto-sleep hangover (flag only — the engine skips render while idle).
    static constexpr uint64_t kIdleSamples = 16384;
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
