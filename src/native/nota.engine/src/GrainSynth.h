// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Grain — the built-in granular synth (kind 10), a Granulator-style voice. It
// scans a loaded sample with a cloud of short windowed grains: a read Position moves
// through the file (Scan speed, or Freeze, or Key = position follows the keyboard),
// grains of a chosen Size spawn at a Density (overlap), each sprayed randomly around
// the position, pitched by the note (+ Coarse/Fine), spread across the stereo field,
// with per-grain position / pitch / pan randomisation. The grain cloud feeds a
// subtractive filter and an amp ADSR. Ships with a procedural default sample so it
// sounds before you drop your own.
//
//   sample ─▶ [Position/Scan] ─▶ grain cloud (Size · Density · Spray · Pitch · Spread) ─▶ filter ─▶ amp ─▶ out
//
// Holds a shared SampleBuffer like the Sampler (so project save bundles it by id). All
// params ride the plugin-param interface (normalized 0..1) → automation / persist /
// clone. Header-only, allocation-free after construction (fixed grain pools).

#pragma once

#include "Instrument.h"
#include "SampleBuffer.h"

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
        Position = 0, Scan, Spray, GrainSize, Density, Coarse, Fine, Spread,
        PosRand, PitchRand, PanRand, ScanMode, FilterType, FilterFreq, FilterReso,
        Attack, Decay, Sustain, Release, Volume, Pan, GrainShape,
        kNumParams
    };

    GrainSynth() {
        set(Position, 0.3f); set(Scan, 0.5f); set(Spray, 0.15f); set(GrainSize, 0.42f); set(Density, 0.6f);
        set(Coarse, 0.5f); set(Fine, 0.5f); set(Spread, 0.4f);
        set(PosRand, 0.12f); set(PitchRand, 0.0f); set(PanRand, 0.3f);
        set(ScanMode, 0.5f);   // Freeze
        set(FilterType, 0.0f); set(FilterFreq, 0.9f); set(FilterReso, 0.1f);
        set(Attack, 0.06f); set(Decay, 0.3f); set(Sustain, 0.85f); set(Release, 0.4f);
        set(Volume, 0.8f); set(Pan, 0.5f); set(GrainShape, 0.0f);
        for (auto& p : posPub_) p.store(-1.0f, std::memory_order_relaxed);
    }

    int32_t kind() const override { return 10; }
    const char* displayName() const override { return "Nota Grain"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        if (!sample_ || sample_->empty()) sample_ = makeDefaultSample(sampleRate_);
    }

    // Sample plumbing — mirrors Sampler so the engine's sample-load / bundling reuse it.
    std::shared_ptr<SampleBuffer> sample() const { return sample_; }
    int32_t rootNote() const { return rootNote_; }
    bool loopEnabled() const { return false; }

    // Live read positions of the active voices (0..1 through the sample), for the UI's
    // moving playheads. Lock-free; the audio thread publishes, the UI polls.
    int32_t playPositions(float* out, int32_t maxN) const {
        int c = 0;
        for (int i = 0; i < kVoices && c < maxN; ++i) { float p = posPub_[i].load(std::memory_order_relaxed); if (p >= 0.0f) out[c++] = p; }
        return c;
    }
    void setSample(std::shared_ptr<SampleBuffer> s, int32_t rootNote, bool /*loop*/) {
        if (s && !s->empty()) { sample_ = std::move(s); rootNote_ = rootNote; }
    }

    // ---- parameters ----
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "position", "scan", "spray", "grainsize", "density", "coarse", "fine", "spread",
            "posrand", "pitchrand", "panrand", "scanmode", "filtype", "filfreq", "filreso",
            "attack", "decay", "sustain", "release", "volume", "pan", "grainshape" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = {
            "Position", "Scan", "Spray", "Grain Size", "Density", "Coarse", "Fine", "Spread",
            "Pos Rand", "Pitch Rand", "Pan Rand", "Scan Mode", "Filter Type", "Filter Freq", "Filter Reso",
            "Attack", "Decay", "Sustain", "Release", "Volume", "Pan", "Grain Shape" };
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

    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) { float v = pn_[i].load(std::memory_order_relaxed); std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float)); }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data) return;
        const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
        for (int i = 0; i < n; ++i) { float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float)); if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<GrainSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->sample_ = sample_; s->rootNote_ = rootNote_; s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- notes ----
    void noteOn(int32_t pitch, float velocity) override {
        Voice* v = findFreeVoice();
        v->active = true; v->pitch = pitch; v->vel = std::clamp(velocity, 0.0f, 1.0f);
        v->env = 0.0f; v->stage = Stage::Attack; v->grainClock = 0.0;
        v->s1L = v->s2L = v->s1R = v->s2R = 0.0;
        for (auto& g : v->grains) g.active = false;
        const int64_t frames = sample_ ? sample_->frames : 0;
        const int mode = scanModeNow();
        if (mode == 2) {   // Key: position follows the keyboard, pitch stays at root
            double pos01 = std::clamp((double)get(Position) + (pitch - 60) / 36.0, 0.0, 1.0);
            v->readPos = pos01 * frames;
        } else {
            v->readPos = (double)get(Position) * frames;
        }
    }
    void noteOff(int32_t pitch) override {
        for (auto& v : voices_) if (v.active && v.pitch == pitch && v.stage != Stage::Release) v.stage = Stage::Release;
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        auto sb = sample_;
        if (!sb || sb->empty()) return;
        const int64_t slen = sb->frames;
        const double srcRatio = (sb->sourceSampleRate > 0 ? sb->sourceSampleRate : sampleRate_) / sampleRate_;

        const int    mode      = scanModeNow();
        const double scanRate  = (mode == 0) ? ((double)get(Scan) - 0.5) * 2.0 * 4.0 * srcRatio : 0.0;
        const double sprayFr   = (double)get(Spray) * 0.25 * slen;                    // up to 25% of the file
        const double grainLen  = std::max(64.0, expMap(get(GrainSize), 0.004, 0.4) * sampleRate_);
        const double overlap   = 1.0 + (double)get(Density) * 7.0;
        const double interval  = std::max(16.0, grainLen / overlap);
        const double coarse    = ((double)get(Coarse) - 0.5) * 48.0;
        const double fine       = ((double)get(Fine) - 0.5) * 2.0;
        const double spread    = get(Spread);
        const double posRandFr = (double)get(PosRand) * 0.25 * slen;
        const double pitchRand = (double)get(PitchRand) * 12.0;
        const double panRand   = get(PanRand);
        const int    ftype     = std::clamp((int)std::lround(get(FilterType) * 2.0f), 0, 2);
        const double fbase     = expMap(get(FilterFreq), 60.0, 18000.0);
        const double fk        = std::clamp(2.0 - 1.9 * get(FilterReso), 0.05, 2.0);
        const int    gshape    = std::clamp((int)std::lround(get(GrainShape) * 3.0f), 0, 3);
        const float  aA = rateOf(Attack, 0.001, 3.0), aD = rateOf(Decay, 0.002, 4.0), aR = rateOf(Release, 0.003, 6.0);
        const float  aS = get(Sustain);
        const double volume = get(Volume);
        const double panv = (get(Pan) - 0.5) * 2.0;
        const double gpL = std::cos((panv + 1.0) * 0.25 * kPi), gpR = std::sin((panv + 1.0) * 0.25 * kPi);
        const double fg = std::tan(kPi * std::min(fbase, sampleRate_ * 0.49) / sampleRate_);
        const double fa1 = 1.0 / (1.0 + fg * (fg + fk));

        for (int32_t i = 0; i < frames; ++i) {
            float mixL = 0.0f, mixR = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;
                advanceEnv(v.env, v.stage, aA, aD, aS, aR);
                if (v.stage == Stage::Off) { v.active = false; continue; }

                // Scan the read position (Scan mode only; wraps within the file).
                if (mode == 0) { v.readPos += scanRate; if (v.readPos >= slen) v.readPos -= slen; else if (v.readPos < 0) v.readPos += slen; }

                // Spawn grains at the density interval.
                v.grainClock += 1.0;
                if (v.grainClock >= interval) {
                    v.grainClock -= interval;
                    Grain* g = freeGrain(v);
                    if (g) {
                        double pos = v.readPos + (sprayFr + posRandFr) * (rnd() * 2.0 - 1.0);
                        pos = std::clamp(pos, 0.0, (double)std::max<int64_t>(0, slen - 2));
                        const double semis = (mode == 2 ? 0.0 : (v.pitch - rootNote_)) + coarse + fine + pitchRand * (rnd() * 2.0 - 1.0);
                        g->pos = pos; g->age = 0.0; g->len = grainLen;
                        g->rate = std::pow(2.0, semis / 12.0) * srcRatio;
                        double pan = 0.5 + (spread * 0.5 + panRand * 0.5) * (rnd() * 2.0 - 1.0);
                        pan = std::clamp(pan, 0.0, 1.0);
                        g->gL = (float)std::cos(pan * 0.5 * kPi); g->gR = (float)std::sin(pan * 0.5 * kPi);
                        g->active = true;
                    }
                }

                // Render active grains.
                float gl = 0.0f, gr = 0.0f;
                for (auto& g : v.grains) {
                    if (!g.active) continue;
                    const double idx = g.pos + g.age * g.rate;
                    if (idx < 0.0 || idx >= slen - 1) { g.active = false; continue; }
                    const int64_t i0 = (int64_t)idx; const double fr = idx - i0;
                    float l0, r0, l1, r1; sb->readStereo(i0, l0, r0); sb->readStereo(i0 + 1, l1, r1);
                    const float w = grainWindow(gshape, g.age / g.len);
                    gl += (l0 + (l1 - l0) * (float)fr) * w * g.gL;
                    gr += (r0 + (r1 - r0) * (float)fr) * w * g.gR;
                    g.age += 1.0;
                    if (g.age >= g.len) g.active = false;
                }
                const float norm = (float)(1.0 / std::sqrt(overlap));
                double yL = gl * norm, yR = gr * norm;
                // Subtractive TPT filter per channel.
                yL = svf(v.s1L, v.s2L, yL, fg, fa1, fk, ftype);
                yR = svf(v.s1R, v.s2R, yR, fg, fa1, fk, ftype);
                const float e = v.env * v.vel;
                mixL += (float)yL * e; mixR += (float)yR * e;
            }
            out[i * 2]     += (float)((mixL * gpL) * volume * 2.4);
            out[i * 2 + 1] += (float)((mixR * gpR) * volume * 2.4);
        }
        // Publish each voice's read position (0..1) for the UI playheads.
        const double inv = slen > 0 ? 1.0 / slen : 0.0;
        for (int vi = 0; vi < kVoices; ++vi)
            posPub_[vi].store(voices_[vi].active ? (float)(voices_[vi].readPos * inv) : -1.0f, std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr int kVoices = 8;
    static constexpr int kGrains = 12;

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Grain { bool active = false; double pos = 0, age = 0, len = 1, rate = 1; float gL = 0.7f, gR = 0.7f; };
    struct Voice {
        bool active = false; int32_t pitch = 0; float vel = 0.0f;
        float env = 0.0f; Stage stage = Stage::Off;
        double readPos = 0.0, grainClock = 0.0;
        double s1L = 0, s2L = 0, s1R = 0, s2R = 0;
        Grain grains[kGrains];
    };
    Voice voices_[kVoices];

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    int   scanModeNow() const { return std::clamp((int)std::lround(get(ScanMode) * 2.0f), 0, 2); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const { return (float)(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float rnd() { rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5; return (rng_ & 0xFFFFFF) / 16777216.0f; }

    static double svf(double& s1, double& s2, double in, double g, double a1, double k, int type) {
        const double v3 = in - s2;
        const double v1 = a1 * s1 + a1 * g * v3;
        const double v2 = s2 + g * v1;
        s1 = 2.0 * v1 - s1; s2 = 2.0 * v2 - s2;
        return type == 1 ? (in - k * v1 - v2) : type == 2 ? v1 : v2;   // HP / BP / LP
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
    // Grain amplitude window over x∈[0,1]: 0 Hann, 1 Gaussian, 2 Tukey (flat top), 3 Triangle.
    static float grainWindow(int shape, double x) {
        switch (shape) {
            case 1: { const double d = (x - 0.5) * 4.0; return (float)std::exp(-d * d); }
            case 2: { const double e = 0.25; if (x < e) return (float)(0.5 - 0.5 * std::cos(kPi * x / e)); if (x > 1 - e) return (float)(0.5 - 0.5 * std::cos(kPi * (1 - x) / e)); return 1.0f; }
            case 3:  return (float)(1.0 - std::fabs(2.0 * x - 1.0));
            default: return (float)(0.5 - 0.5 * std::cos(kTwoPi * x));
        }
    }
    Grain* freeGrain(Voice& v) { for (auto& g : v.grains) if (!g.active) return &g; return nullptr; }
    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0]; for (auto& v : voices_) if (v.env < q->env) q = &v; return q;
    }

    // A rich, slowly-evolving 1.6 s harmonic pad — pleasant to granulate out of the box.
    static std::shared_ptr<SampleBuffer> makeDefaultSample(double sr) {
        auto s = std::make_shared<SampleBuffer>();
        const int64_t n = (int64_t)(1.6 * sr);
        s->channels = 1; s->frames = n; s->sourceSampleRate = sr; s->samples.resize(n);
        const double f0 = 110.0;
        for (int64_t i = 0; i < n; ++i) {
            const double t = (double)i / sr;
            double v = 0.0;
            for (int k = 1; k <= 6; ++k) {
                const double amp = 1.0 / k;
                const double det = 1.0 + 0.002 * std::sin(2.0 * kPi * 0.3 * t * k);   // slow drift
                v += amp * std::sin(2.0 * kPi * f0 * k * det * t);
            }
            const double env = 0.5 - 0.5 * std::cos(2.0 * kPi * std::min(1.0, i / (0.05 * sr))) ;  // fade-in
            s->samples[i] = (float)(v * 0.16 * (i < 0.05 * sr ? env : 1.0));
        }
        return s;
    }

    double sampleRate_ = 44100.0;
    int32_t rootNote_ = 60;
    std::shared_ptr<SampleBuffer> sample_;
    uint32_t rng_ = 0x1234567u;
    std::atomic<float> pn_[kNumParams];
    std::atomic<float> posPub_[kVoices] = {};
};

} // namespace nota
