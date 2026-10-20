// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Grain — the built-in granular synth (kind 10), a Granulator-style voice. It
// scans a loaded sample with a cloud of short windowed grains: a read Position moves
// through the file (Scan speed, or Freeze, or Key = position follows the keyboard),
// grains of a chosen Size spawn at a Density (overlap), each sprayed randomly around
// the position, pitched by the note (+ Coarse/Fine), spread across the stereo field,
// with per-grain position / pitch / pan randomisation. Dry/Wet blends the cloud with the
// sample itself, played straight from the read position at the note's pitch. The mix
// feeds a subtractive filter and an amp ADSR. Ships with a procedural default sample so
// it sounds before you drop your own.
//
//   sample ─▶ [Position/Scan] ─▶ grain cloud (Size · Density · Spray · Pitch · Spread) ─┬▶ filter ─▶ amp ─▶ out
//          └▶ dry read head (the sample at the note's pitch) ──────────── Dry/Wet ─────┘
//
// Position is live: a held note follows it (and its automation) — Scan adds a moving
// offset to it, Freeze sits on it, Key offsets it by the note. The audio thread publishes
// every live grain (read position, pan, window level) through scopeRead for the UI.
//
// Holds a shared SampleBuffer like the Sampler (so project save bundles it by id). All
// params ride the plugin-param interface (normalized 0..1) → automation / persist /
// clone. Allocation-free after construction (fixed grain pools).
// Grain scheduling and read positions stay std::-exact (kept verbatim); only the
// downstream Hann/Tukey amplitude windows use fast_math::cos. Silent clouds
// auto-sleep via isIdle(); release tails fully decay before sleep.

#pragma once

#include "devices/Instrument.h"
#include "media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class GrainSynth final : public Instrument {
  public:
    enum Param {
        Position = 0,
        Scan,
        Spray,
        GrainSize,
        Density,
        Coarse,
        Fine,
        Spread,
        PosRand,
        PitchRand,
        PanRand,
        ScanMode,
        FilterType,
        FilterFreq,
        FilterReso,
        Attack,
        Decay,
        Sustain,
        Release,
        Volume,
        Pan,
        GrainShape,
        DryWet, // appended 2026-09: older projects load fully wet, as they sounded
        kNumParams
    };

    GrainSynth();

    int32_t kind() const override { return 10; }
    const char* displayName() const override { return "Nota Grain"; }

    void setSampleRate(double sr) override;

    // Sample plumbing — mirrors Sampler so the engine's sample-load / bundling reuse it.
    std::shared_ptr<SampleBuffer> sample() const { return sample_; }
    int32_t rootNote() const { return rootNote_.load(std::memory_order_relaxed); }
    void setRoot(int32_t r) { rootNote_.store(std::clamp(r, 0, 127), std::memory_order_relaxed); }
    bool loopEnabled() const { return false; }

    // Live read positions of the active voices (0..1 through the sample), for the UI's
    // moving playheads. Lock-free; the audio thread publishes, the UI polls.
    int32_t playPositions(float* out, int32_t maxN) const;
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // Scope telemetry: [0] active voices · [1] live grains n · then n triples of (read
    // position 0..1 through the sample, pan 0..1, window level 0..1) — the grain cloud.
    int32_t scopeRead(float* out, int32_t maxN) const override;

    void setSample(std::shared_ptr<SampleBuffer> s, int32_t rootNote, bool /*loop*/) {
        if (s && !s->empty()) {
            sample_ = std::move(s);
            setRoot(rootNote);
        }
        wake();
    }

    // ---- parameters ----
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        wake();
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i)
            if (pluginParamId(i) == id) return i;
        return -1;
    }

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- notes ----
    void noteOn(int32_t pitch, float velocity) override;
    // Releases the oldest held voice of the pitch only: a repeat that started a hair before
    // the previous one of the same pitch ended keeps sounding.
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

    // Sleep contract: grains + release tails ring past note-off (hasTail true); isIdle
    // engages only with no active voice and a silent block, after a hangover.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quietSamples_ = 0;
    }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    static constexpr int kVoices = 8;
    static constexpr int kGrains = 12;
    // The dry head against the cloud: the cloud's windows and its 1/√overlap normalisation
    // leave it well under the raw sample, so the dry side is trimmed to meet it.
    static constexpr double kDryTrim = 0.5;

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Grain {
        bool active = false;
        double pos = 0, age = 0, len = 1, rate = 1;
        float gL = 0.7f, gR = 0.7f, pan = 0.5f;
    };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        float vel = 0.0f;
        uint64_t seq = 0;
        float env = 0.0f;
        Stage stage = Stage::Off;
        double readPos = 0.0, grainClock = 0.0;
        double base = 0.0, scanOff = 0.0;   // read position = base (live) + scan offset
        double dryPos = 0.0, dryRate = 1.0; // the dry head
        double s1L = 0, s2L = 0, s1R = 0, s2R = 0;
        Grain grains[kGrains];
    };
    Voice voices_[kVoices];

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    int scanModeNow() const { return std::clamp((int)std::lround(get(ScanMode) * 2.0f), 0, 2); }
    // Where a note reads (0..1): Position, or in Key mode Position moved by the note — C4
    // reads at Position, each octave moves it a third of the file.
    double basePos01(int32_t pitch, int mode) const {
        const double p = get(Position);
        return mode == 2 ? std::clamp(p + (pitch - 60) / 36.0, 0.0, 1.0) : p;
    }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const { return (float)(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float rnd() {
        rng_ ^= rng_ << 13;
        rng_ ^= rng_ >> 17;
        rng_ ^= rng_ << 5;
        return (rng_ & 0xFFFFFF) / 16777216.0f;
    }

    static double svf(double& s1, double& s2, double in, double g, double a1, double k, int type);
    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);
    // Grain amplitude window over x∈[0,1]: 0 Hann, 1 Gaussian, 2 Tukey (flat top), 3 Triangle.
    // Downstream amplitude only (scheduling/positions untouched): Hann/Tukey run on
    // fast_math::cos, Gaussian/triangle stay std::-exact.
    static float grainWindow(int shape, double x);
    Grain* freeGrain(Voice& v) {
        for (auto& g : v.grains)
            if (!g.active) return &g;
        return nullptr;
    }
    Voice* findFreeVoice();

    // A rich, slowly-evolving 1.6 s harmonic pad — pleasant to granulate out of the box.
    static std::shared_ptr<SampleBuffer> makeDefaultSample(double sr);

    double sampleRate_ = 44100.0;
    std::atomic<int32_t> rootNote_{60};
    std::shared_ptr<SampleBuffer> sample_;
    uint32_t rng_ = 0x1234567u;
    std::atomic<float> pn_[kNumParams];
    std::atomic<float> posPub_[kVoices] = {};
    std::atomic<float> scope_[2 + kVoices * kGrains * 3] = {};
    std::atomic<int32_t> activeVoices_{0};
    uint64_t seq_ = 0;

    // Auto-sleep hangover.
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
};

} // namespace nota
