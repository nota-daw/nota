// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Keys — the built-in physically modelled electric piano (kind 16). Nota's own DSP.
//
//   hammer ─→ resonator (tine + tone bar | reed | string) ─→ PICKUP curve ─→ Σ voices
//      ↑ hardness, vel→hardness, noise      ↑ decay, body, bright, key→bright
//   ─→ preamp (drive, bass, treble) ─→ tremolo (mono / stereo pan) ─→ phaser ─→ chorus
//   ─→ cabinet (Suitcase / Combo / DI) ─→ volume · pan
//
// Models. Tine and Suitcase: a cantilever tine (modes 1 : 6.27 : 17.55) coupled to a tone
// bar a hair sharper that rings longer (the beat and the long sustain). Reed: a struck reed
// with the same beam modes, shorter, into an electrostatic pickup (odd-heavy curve). Clav:
// a string struck by a tangent (24 near-harmonic partials) read by two pickups — Upper,
// Lower or Both — whose positions comb the spectrum.
//
// The hammer is a half-sine force pulse whose contact time comes from Hardness (+ velocity
// × Vel→H), fed into two-pole mode resonators, so a soft hammer excites the upper modes
// less on its own. The summed displacement (×velocity, ×MPE pressure) passes the PICKUP
// transfer curve — the very curve the editor draws: Symmetry offsets the tine against the
// pickup (even harmonics), Distance sets how hard it saturates (close = bark). Quiet notes
// stay on the linear middle of the curve, loud ones reach its knees — the e-piano growl.
//
// The damper. Key up drops a damper on the modes (Damper sets how fast, upper modes damp
// faster) with a felt thump (Rel N). The pedal — the Pedal param or a keyboard's CC64,
// whichever is down — holds released notes; re-striking a ringing key re-excites the same
// voice (the tine is still moving). Voices: 8 / 16 / 32 / 64, the quietest is stolen with
// a 5 ms fade. Age detunes / re-voices every key by its own fixed amount (deterministic,
// so a freeze equals the live render); Stretch is a piano-style octave stretch.
//
// MPE: bend retunes the modes, pressure pushes the tine into the pickup (more bark), slide
// shifts the pickup symmetry (timbre); a keyboard's wheel bends ±2 semitones.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable ids).
// Allocation-free after construction. Denormals handled by the
// unified dsp::ScopedNoDenormals; key-split/velocity-mapping thresholds and the
// resonator loops stay std::-exact — FastMath only shapes the downstream tremolo LFO.
// Silent voices auto-sleep via isIdle(); release tails fully decay before sleep.

#pragma once

#include "devices/Instrument.h"
#include "engine/NoteExpression.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

namespace keys {

constexpr float kPi = 3.14159265358979f;

// The PICKUP transfer curve, shared with the editor's graph (KeysModel.cs mirrors it):
// x = tine displacement (−1..1 at full scale), sym −1..1, dist 0..1, model 0..3.
inline float pickupRaw(float x, float sym, float dist, int model) {
    const float a = 0.3f + (1.0f - dist) * 3.2f;
    if (model == 2) { // Reed — electrostatic: odd-heavy, a little symmetry
        const float k = (1.0f - dist) * 0.8f;
        return x / (1.0f + a * 0.6f * std::fabs(x)) * (1.0f + k * x * x) / (1.0f + k) +
               sym * 0.35f * (2.0f - dist) * x * x;
    }
    const float aa = model == 3 ? a * 0.5f : a; // Clav — a gentler magnetic pickup
    return x / (1.0f + aa * std::fabs(x)) + sym * 0.55f * (2.0f - dist) * x * x;
}
// The curve's peak magnitude on −1..1 (the editor scales its graph by the same number).
inline float pickupNorm(float sym, float dist, int model) {
    float mx = 0.0f;
    for (int i = 0; i <= 60; ++i) mx = std::max(mx, std::fabs(pickupRaw(-1.0f + i / 30.0f, sym, dist, model)));
    return mx > 1e-6f ? mx : 1.0f;
}

inline uint32_t hash32(uint32_t x) {
    x ^= x >> 16;
    x *= 0x7feb352dU;
    x ^= x >> 15;
    x *= 0x846ca68bU;
    x ^= x >> 16;
    return x;
}
// −1..1, fixed per (key, slot) — the Age offsets.
inline float keyRand(int pitch, int slot) {
    return static_cast<float>(hash32(static_cast<uint32_t>(pitch * 131 + slot * 7919 + 17)) & 0xFFFFFF) / 8388607.5f -
           1.0f;
}

// RBJ biquad (transposed direct form II).
struct Biquad {
    float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
    float tick(float x) {
        const float y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
    void clear() { z1 = z2 = 0.0f; }
    void bypass() {
        b0 = 1;
        b1 = b2 = a1 = a2 = 0;
    }
    void norm(double B0, double B1, double B2, double A0, double A1, double A2) {
        b0 = static_cast<float>(B0 / A0);
        b1 = static_cast<float>(B1 / A0);
        b2 = static_cast<float>(B2 / A0);
        a1 = static_cast<float>(A1 / A0);
        a2 = static_cast<float>(A2 / A0);
    }
    void lowpass(double fc, double q, double sr) {
        const double w = 2.0 * 3.141592653589793 * std::min(fc, sr * 0.45) / sr, c = std::cos(w),
                     al = std::sin(w) / (2.0 * q);
        norm((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
    }
    void highpass(double fc, double q, double sr) {
        const double w = 2.0 * 3.141592653589793 * std::min(fc, sr * 0.45) / sr, c = std::cos(w),
                     al = std::sin(w) / (2.0 * q);
        norm((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
    }
    void peak(double fc, double q, double db, double sr) {
        const double A = std::pow(10.0, db / 40.0), w = 2.0 * 3.141592653589793 * std::min(fc, sr * 0.45) / sr;
        const double c = std::cos(w), al = std::sin(w) / (2.0 * q);
        norm(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A);
    }
    void shelf(double fc, double db, double sr, bool high) {
        const double A = std::pow(10.0, db / 40.0), w = 2.0 * 3.141592653589793 * std::min(fc, sr * 0.45) / sr;
        const double c = std::cos(w), s = std::sin(w), al = s / 2.0 * std::sqrt(2.0), sq = 2.0 * std::sqrt(A) * al;
        if (high)
            norm(A * ((A + 1) + (A - 1) * c + sq), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sq),
                (A + 1) - (A - 1) * c + sq, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sq);
        else
            norm(A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq),
                (A + 1) + (A - 1) * c + sq, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sq);
    }
};

} // namespace keys

class Keys final : public Instrument {
  public:
    static constexpr int kMaxVoices = 64, kMaxModes = 24;

    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        Model = 0,
        Hard,
        VelHard,
        Noise, // hammer
        Decay,
        Body,
        Bright,
        KeyBright, // resonator
        Sym,
        Dist,
        PuPos, // pickup
        Damper,
        RelNoise,
        Pedal, // damper
        Tune,
        Age,
        Stretch,
        Voices, // play
        PreOn,
        Drive,
        Bass,
        Treble, // preamp
        TremOn,
        TremMode,
        TremSync,
        TremRate,
        TremDepth,
        PhaserOn,
        PhaserRate,
        PhaserDepth,
        ChorusOn,
        ChorusMix,
        Cab,
        Volume,
        Pan,
        View, // editor state, not sound: card size (0 = L, 1 = S)
        kNumParams
    };

    // scopeRead layout.
    enum Scope { ScActive = 0, ScLimit, ScHeld, ScSustained, ScPedal, ScLfo, ScPeakL, ScPeakR, ScLastNote, kScopeN };

    Keys();

    int32_t kind() const override { return 16; }
    const char* displayName() const override { return "Nota Keys"; }

    void setSampleRate(double sr) override {
        pendingRate_.store(sr > 0 ? sr : 44100.0, std::memory_order_relaxed);
        wake();
    }

    int32_t activeVoiceCount() const override {
        return static_cast<int32_t>(tele_[ScActive].load(std::memory_order_relaxed));
    }
    int32_t scopeRead(float* out, int32_t maxN) const override;

    // MPE: bend → pitch, pressure → drive into the pickup (+ a little level), slide → pickup symmetry.
    bool supportsMpe() const override { return true; }
    bool handlesSustain() const override { return true; } // its own damper model (CC64)
    void noteExpression(int32_t pitch, int32_t dim, float value) override;

    void setTransportInfo(const TransportInfo& ti) override;
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;

    // ---- parameters -----------------------------------------------------------
    // Display names group by the part before the last space (the automation menu nests on it).
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v))
            pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
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

    // ---- notes ----------------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    // ---- render ---------------------------------------------------------------
    void render(float* out, int32_t frames) override;

    // Sleep contract: voices + damper/noise tails ring past note-off (hasTail true);
    // isIdle engages only with no active voice and a silent FX block, after a hangover
    // that lets the chorus/phaser/cabinet tails ring out.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quietSamples_ = 0;
    }

  private:
    static constexpr int kChunk = 64;
    static constexpr int kCtl = 32; // control-rate interval (bend / damper retune)
    static constexpr float kWheelRange = 2.0f;
    static constexpr int kChLen = 8192;             // chorus delay line (≥ 25 ms at 192 kHz)
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    struct Voice {
        bool active = false, held = false, sustained = false, damped = false;
        int32_t pitch = 60, model = 0;
        float vel = 0.0f, peak = 0.0f, level = 1.0f, symOff = 0.0f;
        int nModes = 0;
        float a1[kMaxModes] = {}, a2[kMaxModes] = {}, b[kMaxModes] = {}, y1[kMaxModes] = {}, y2[kMaxModes] = {};
        float w[kMaxModes] = {}, R[kMaxModes] = {}, Rd[kMaxModes] = {}, amp[kMaxModes] = {};
        // hammer pulse
        int pulseN = 0, pulsePos = 0;
        float pulseK = 0.0f;
        float drive = 0.75f;
        // hammer noise / release thump
        float hnEnv = 0.0f, hnDec = 0.0f, hnG = 0.0f, hnF = 0.1f, hnIc1 = 0.0f, hnIc2 = 0.0f;
        float rnEnv = 0.0f, rnDec = 0.0f, rnLp = 0.0f;
        uint32_t rng = 1;
        int kill = 0; // >0: stolen, fading out over killLen()
        int ctl = 0;
        float exBend = 0.0f;
        VoiceExpr ex;
    };

    // ---- helpers ----------------------------------------------------------------
    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    int modelIndex() const { return std::clamp(static_cast<int>(std::lround(get(Model) * 3.0f)), 0, 3); }
    int voiceLimit() const {
        static const int n[4] = {8, 16, 32, 64};
        return n[std::clamp(static_cast<int>(std::lround(get(Voices) * 3.0f)), 0, 3)];
    }
    int killLen() const { return std::max(8, static_cast<int>(0.005 * sr_)); }
    // Noise levels: 0 = off, else −60 … −12 dB (√-shaped so the low end is finely graded).
    static float dbMap(float v) { return v <= 1e-3f ? 0.0f : std::pow(10.0f, (-60.0f + 48.0f * std::sqrt(v)) / 20.0f); }
    float rnd(Voice& v) {
        v.rng = v.rng * 1664525u + 1013904223u;
        return static_cast<float>(v.rng >> 8) / 8388608.0f - 1.0f;
    }

    void applyRate();

    // ---- the strike: build the modes for this key + model, start the hammer pulse ----
    void strike(Voice& v, float vel, bool fresh);

    void damp(Voice& v);

    void retune(Voice& v, float bend);

    void renderVoice(Voice& v, float* mono, int n, int model, float sym, float dist, float pnorm, float noiseLin,
        float relLin, const float exAdd[kExprDims], float exprCoef);

    // ---- the effects chain (mono voices in, stereo out, added into out) ----------
    // Returns this block's own output peak (for the sleep gate).
    void updateFx();

    double tremHz() const;
    double tremCyclesPerBeat() const;

    float renderFx(const float* mono, float* out, int n);

    float readCh(int c, float delay) const;

    void resetFx();

    // Volume: 0.8 = 0 dB, 1 = +6 dB, a log taper below (0.4 ≈ −18 dB), 0 = silent.
    static float volumeGain(float v);

    void publishTelemetry();

    std::atomic<float> pn_[kNumParams];
    std::atomic<float> tele_[kScopeN];
    std::atomic<double> pendingRate_{44100.0};
    double sr_ = 0.0; // 0 → the first applyRate() adopts the pending rate
    Voice voices_[kMaxVoices];
    GlobalExpr gexpr_;
    bool cc64_ = false, pedalDown_ = false;
    int32_t lastNote_ = -1;
    double spb_ = 22050.0, beat_ = 0.0;
    bool playing_ = false;
    int64_t sinceBlock_ = 0;
    bool looping_ = false;
    double loopStart_ = 0.0;

    // FX state
    bool fxDirty_ = true, fxRestart_ = false;
    int cabCache_ = -1;
    float bassCache_ = 1e9f, trebleCache_ = 1e9f;
    keys::Biquad loShelf_, hiShelf_, cabF_[2][4];
    double lfo_ = 0.0, phLfo_ = 0.0, chL1_ = 0.0, chL2_ = 0.37;
    float phZ_[2][4] = {}, phFb_[2] = {};
    float chBuf_[2][kChLen];
    int chW_ = 0;
    float sDepth_ = 0.0f, sPh_ = 0.0f, sCh_ = 0.0f, sVol_ = 0.0f;
    float dcL_ = 0.0f, dcPrev_ = 0.0f;
    float pkL_ = 0.0f, pkR_ = 0.0f;

    // Auto-sleep hangover.
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
};

} // namespace nota
