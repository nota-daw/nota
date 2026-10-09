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
// Header-only; allocation-free after construction.

#pragma once

#include "Instrument.h"
#include "NoteExpression.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

#if defined(__SSE2__) || defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#include <xmmintrin.h>
#define NOTA_KEYS_FTZ_X86 1
#elif defined(__aarch64__) && !defined(_MSC_VER)
#define NOTA_KEYS_FTZ_ARM 1
#endif

namespace nota {

namespace keys {

constexpr float kPi = 3.14159265358979f;

// The PICKUP transfer curve, shared with the editor's graph (KeysModel.cs mirrors it):
// x = tine displacement (−1..1 at full scale), sym −1..1, dist 0..1, model 0..3.
inline float pickupRaw(float x, float sym, float dist, int model) {
    const float a = 0.3f + (1.0f - dist) * 3.2f;
    if (model == 2) {   // Reed — electrostatic: odd-heavy, a little symmetry
        const float k = (1.0f - dist) * 0.8f;
        return x / (1.0f + a * 0.6f * std::fabs(x)) * (1.0f + k * x * x) / (1.0f + k)
             + sym * 0.35f * (2.0f - dist) * x * x;
    }
    const float aa = model == 3 ? a * 0.5f : a;   // Clav — a gentler magnetic pickup
    return x / (1.0f + aa * std::fabs(x)) + sym * 0.55f * (2.0f - dist) * x * x;
}
// The curve's peak magnitude on −1..1 (the editor scales its graph by the same number).
inline float pickupNorm(float sym, float dist, int model) {
    float mx = 0.0f;
    for (int i = 0; i <= 60; ++i) mx = std::max(mx, std::fabs(pickupRaw(-1.0f + i / 30.0f, sym, dist, model)));
    return mx > 1e-6f ? mx : 1.0f;
}

inline uint32_t hash32(uint32_t x) {
    x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
    return x;
}
// −1..1, fixed per (key, slot) — the Age offsets.
inline float keyRand(int pitch, int slot) {
    return static_cast<float>(hash32(static_cast<uint32_t>(pitch * 131 + slot * 7919 + 17)) & 0xFFFFFF) / 8388607.5f - 1.0f;
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
    void bypass() { b0 = 1; b1 = b2 = a1 = a2 = 0; }
    void norm(double B0, double B1, double B2, double A0, double A1, double A2) {
        b0 = static_cast<float>(B0 / A0); b1 = static_cast<float>(B1 / A0); b2 = static_cast<float>(B2 / A0);
        a1 = static_cast<float>(A1 / A0); a2 = static_cast<float>(A2 / A0);
    }
    void lowpass(double fc, double q, double sr) {
        const double w = 2.0 * 3.141592653589793 * std::min(fc, sr * 0.45) / sr, c = std::cos(w), al = std::sin(w) / (2.0 * q);
        norm((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
    }
    void highpass(double fc, double q, double sr) {
        const double w = 2.0 * 3.141592653589793 * std::min(fc, sr * 0.45) / sr, c = std::cos(w), al = std::sin(w) / (2.0 * q);
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
        if (high) norm(A * ((A + 1) + (A - 1) * c + sq), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sq),
                       (A + 1) - (A - 1) * c + sq, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sq);
        else      norm(A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq),
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
        Hard, VelHard, Noise,                    // hammer
        Decay, Body, Bright, KeyBright,          // resonator
        Sym, Dist, PuPos,                        // pickup
        Damper, RelNoise, Pedal,                 // damper
        Tune, Age, Stretch, Voices,              // play
        PreOn, Drive, Bass, Treble,              // preamp
        TremOn, TremMode, TremSync, TremRate, TremDepth,
        PhaserOn, PhaserRate, PhaserDepth,
        ChorusOn, ChorusMix,
        Cab, Volume, Pan,
        View,                                    // editor state, not sound: card size (0 = L, 1 = S)
        kNumParams
    };

    // scopeRead layout.
    enum Scope { ScActive = 0, ScLimit, ScHeld, ScSustained, ScPedal, ScLfo, ScPeakL, ScPeakR, ScLastNote, kScopeN };

    Keys() {
        // "Mk I Stage": a Tine through a DI, the preamp warm, no modulation effects.
        for (int i = 0; i < kNumParams; ++i) pn_[i].store(0.0f, std::memory_order_relaxed);
        set(Model, 0.0f);
        set(Hard, 0.5f); set(VelHard, 0.6f); set(Noise, 0.25f);
        set(Decay, 0.5f); set(Body, 0.5f); set(Bright, 0.5f); set(KeyBright, 0.65f);
        set(Sym, 0.6f); set(Dist, 0.4f); set(PuPos, 0.5f);
        set(Damper, 0.7f); set(RelNoise, 0.3f); set(Pedal, 0.0f);
        set(Tune, 0.5f); set(Age, 0.15f); set(Stretch, 0.3f); set(Voices, 2.0f / 3.0f);
        set(PreOn, 1.0f); set(Drive, 0.35f); set(Bass, 0.62f); set(Treble, 0.46f);
        set(TremOn, 0.0f); set(TremMode, 1.0f); set(TremSync, 0.0f); set(TremRate, 0.65f); set(TremDepth, 0.5f);
        set(PhaserOn, 0.0f); set(PhaserRate, 0.3f); set(PhaserDepth, 0.6f);
        set(ChorusOn, 0.0f); set(ChorusMix, 0.35f);
        set(Cab, 1.0f); set(Volume, 0.8f); set(Pan, 0.5f);
        set(View, 0.0f);
        std::memset(chBuf_, 0, sizeof(chBuf_));
        for (auto& t : tele_) t.store(0.0f, std::memory_order_relaxed);
    }

    int32_t kind() const override { return 16; }
    const char* displayName() const override { return "Nota Keys"; }

    void setSampleRate(double sr) override { pendingRate_.store(sr > 0 ? sr : 44100.0, std::memory_order_relaxed); }

    int32_t activeVoiceCount() const override { return static_cast<int32_t>(tele_[ScActive].load(std::memory_order_relaxed)); }
    int32_t scopeRead(float* out, int32_t maxN) const override {
        const int n = std::min<int>(maxN, kScopeN);
        for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
        return n;
    }

    // MPE: bend → pitch, pressure → drive into the pickup (+ a little level), slide → pickup symmetry.
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override {
        if (pitch < 0) {
            if (dim == ExprSustain) cc64_ = value >= 0.5f;
            else gexpr_.set(dim, value);
            return;
        }
        if (dim < 0 || dim >= kExprDims) return;
        float add[kExprDims]; gexpr_.offsets(kWheelRange, add);
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.held) v.ex.set(dim, value, add);
    }

    void setTransportInfo(const TransportInfo& ti) override {
        looping_ = ti.isLooping; loopStart_ = ti.ppqLoopStart;
    }

    void setTransport(double beatStart, double samplesPerBeat, bool playing) override {
        // A jump while rolling (a seek, a fresh play from elsewhere — not a loop wrap) restarts
        // like a play edge, and the notes from before it stop: the engine re-chases held clip notes.
        if (playing && playing_ && samplesPerBeat > 0.0) {
            const double expect = beat_ + sinceBlock_ / std::max(1.0, spb_);
            const bool wrap = looping_ && std::fabs(beatStart - loopStart_) < 1e-6;
            if (std::fabs(beatStart - expect) * samplesPerBeat > 2.0 && !wrap) {
                fxRestart_ = true;
                for (auto& v : voices_) v.active = false;
            }
        }
        if (samplesPerBeat > 0.0) spb_ = samplesPerBeat;
        // A play edge restarts the effects (tails, LFOs, smoothing) from a fixed state, so a
        // freeze or a bounce equals what a fresh play of the same bars sounds like.
        // Released tails from before the edge stop with it; keys still held keep sounding.
        if (playing && !playing_) {
            fxRestart_ = true;
            for (auto& v : voices_) if (v.active && !v.held) v.active = false;
        }
        beat_ = beatStart; playing_ = playing; sinceBlock_ = 0;
    }

    // ---- parameters -----------------------------------------------------------
    // Display names group by the part before the last space (the automation menu nests on it).
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[kNumParams] = {
            "model", "hard", "velhard", "noise", "decay", "body", "bright", "keybright",
            "sym", "dist", "pupos", "damper", "relnoise", "pedal",
            "tune", "age", "stretch", "voices",
            "preon", "drive", "bass", "treble",
            "tremon", "tremmode", "tremsync", "tremrate", "tremdepth",
            "phaseron", "phaserrate", "phaserdepth", "choruson", "chorusmix",
            "cab", "volume", "pan", "view" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[kNumParams] = {
            "Model", "Hammer Hardness", "Hammer Vel-Hardness", "Hammer Noise",
            "Resonator Decay", "Resonator Body", "Resonator Bright", "Resonator Key-Bright",
            "Pickup Symmetry", "Pickup Distance", "Pickup Position",
            "Damper Amount", "Damper Release-Noise", "Damper Pedal",
            "Play Tune", "Play Age", "Play Stretch", "Play Voices",
            "Preamp On", "Preamp Drive", "Preamp Bass", "Preamp Treble",
            "Tremolo On", "Tremolo Mode", "Tremolo Sync", "Tremolo Rate", "Tremolo Depth",
            "Phaser On", "Phaser Rate", "Phaser Depth", "Chorus On", "Chorus Mix",
            "Cabinet Type", "Output Volume", "Output Pan", "View" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            const float v = pn_[i].load(std::memory_order_relaxed);
            std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
        }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size <= 0) return;
        const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
        for (int i = 0; i < n; ++i) {
            float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float));
            if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto k = std::make_shared<Keys>();
        for (int i = 0; i < kNumParams; ++i) k->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        k->setSampleRate(pendingRate_.load(std::memory_order_relaxed));
        return k;
    }

    // ---- notes ----------------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        if (pitch < 0 || pitch > 127) return;
        applyRate();
        const float vel = std::clamp(velocity, 0.0f, 1.0f);
        lastNote_ = pitch;
        // Re-striking a ringing key hits the same tine again.
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.kill <= 0) { strike(v, vel, false); return; }

        int active = 0;
        for (auto& v : voices_) if (v.active && v.kill <= 0) ++active;
        if (active >= voiceLimit()) {
            Voice* q = nullptr;   // steal the quietest, released ones first
            for (auto& v : voices_) {
                if (!v.active || v.kill > 0) continue;
                if (!q || (v.held < q->held) || (v.held == q->held && v.peak < q->peak)) q = &v;
            }
            if (q) q->kill = killLen();
        }
        Voice* slot = nullptr;
        for (auto& v : voices_) if (!v.active) { slot = &v; break; }
        if (!slot) {   // all 64 busy: take the quietest outright
            slot = &voices_[0];
            for (auto& v : voices_) if (v.peak < slot->peak) slot = &v;
        }
        slot->pitch = pitch;
        strike(*slot, vel, true);
    }

    void noteOff(int32_t pitch) override {
        for (auto& v : voices_) {
            if (!v.active || v.pitch != pitch || !v.held) continue;
            v.held = false;
            if (pedalDown_) v.sustained = true;
            else damp(v);
        }
    }

    void allNotesOff() override {
        for (auto& v : voices_) v.active = false;
        gexpr_.reset(); cc64_ = false; pedalDown_ = false;
        dcL_ = 0.0f; dcPrev_ = 0.0f;
    }

    // ---- render ---------------------------------------------------------------
    void render(float* out, int32_t frames) override {
        FtzGuard ftz;
        applyRate();
        const double sr = sr_;

        // Block snapshot.
        const int model = modelIndex();
        const float sym = (get(Sym) - 0.5f) * 2.0f, dist = get(Dist);
        const float pnorm = keys::pickupNorm(sym, dist, model);
        const float noiseLin = dbMap(get(Noise)) * 0.5f, relLin = dbMap(get(RelNoise)) * 0.5f;

        // The pedal: param or CC64. Lifting it drops the dampers on every held-over note.
        const bool pedal = get(Pedal) >= 0.5f || cc64_;
        if (pedalDown_ && !pedal)
            for (auto& v : voices_) if (v.active && v.sustained) { v.sustained = false; damp(v); }
        pedalDown_ = pedal;

        float exAdd[kExprDims]; gexpr_.offsets(kWheelRange, exAdd);
        const float exprCoef = exprSmoothCoef(sr);

        float mono[kChunk];
        int done = 0;
        while (done < frames) {
            const int n = std::min<int>(kChunk, frames - done);
            std::fill(mono, mono + n, 0.0f);
            for (auto& v : voices_) if (v.active) renderVoice(v, mono, n, model, sym, dist, pnorm, noiseLin, relLin, exAdd, exprCoef);
            renderFx(mono, out + done * 2, n);
            done += n;
        }
        publishTelemetry();
    }

private:
    static constexpr int kChunk = 64;
    static constexpr int kCtl = 32;               // control-rate interval (bend / damper retune)
    static constexpr float kWheelRange = 2.0f;
    static constexpr int kChLen = 8192;           // chorus delay line (≥ 25 ms at 192 kHz)

    struct Voice {
        bool active = false, held = false, sustained = false, damped = false;
        int32_t pitch = 60, model = 0;
        float vel = 0.0f, peak = 0.0f, level = 1.0f, symOff = 0.0f;
        int nModes = 0;
        float a1[kMaxModes] = {}, a2[kMaxModes] = {}, b[kMaxModes] = {}, y1[kMaxModes] = {}, y2[kMaxModes] = {};
        float w[kMaxModes] = {}, R[kMaxModes] = {}, Rd[kMaxModes] = {}, amp[kMaxModes] = {};
        // hammer pulse
        int pulseN = 0, pulsePos = 0; float pulseK = 0.0f;
        float drive = 0.75f;
        // hammer noise / release thump
        float hnEnv = 0.0f, hnDec = 0.0f, hnG = 0.0f, hnF = 0.1f, hnIc1 = 0.0f, hnIc2 = 0.0f;
        float rnEnv = 0.0f, rnDec = 0.0f, rnLp = 0.0f;
        uint32_t rng = 1;
        int kill = 0;                             // >0: stolen, fading out over killLen()
        int ctl = 0;
        float exBend = 0.0f;
        VoiceExpr ex;
    };

    // ---- helpers ----------------------------------------------------------------
    struct FtzGuard {
#if defined(NOTA_KEYS_FTZ_X86)
        unsigned int old; FtzGuard() : old(_mm_getcsr()) { _mm_setcsr(old | 0x8040u); } ~FtzGuard() { _mm_setcsr(old); }
#elif defined(NOTA_KEYS_FTZ_ARM)
        uint64_t old = 0;
        FtzGuard() { asm volatile("mrs %0, fpcr" : "=r"(old)); const uint64_t n = old | (1ull << 24); asm volatile("msr fpcr, %0" : : "r"(n)); }
        ~FtzGuard() { asm volatile("msr fpcr, %0" : : "r"(old)); }
#endif
    };

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    int modelIndex() const { return std::clamp(static_cast<int>(std::lround(get(Model) * 3.0f)), 0, 3); }
    int voiceLimit() const { static const int n[4] = { 8, 16, 32, 64 }; return n[std::clamp(static_cast<int>(std::lround(get(Voices) * 3.0f)), 0, 3)]; }
    int killLen() const { return std::max(8, static_cast<int>(0.005 * sr_)); }
    // Noise levels: 0 = off, else −60 … −12 dB (√-shaped so the low end is finely graded).
    static float dbMap(float v) { return v <= 1e-3f ? 0.0f : std::pow(10.0f, (-60.0f + 48.0f * std::sqrt(v)) / 20.0f); }
    float rnd(Voice& v) { v.rng = v.rng * 1664525u + 1013904223u; return static_cast<float>(v.rng >> 8) / 8388608.0f - 1.0f; }

    void applyRate() {
        const double r = pendingRate_.load(std::memory_order_relaxed);
        if (r != sr_) { sr_ = r; for (auto& v : voices_) v.active = false; fxDirty_ = true; }
    }

    // ---- the strike: build the modes for this key + model, start the hammer pulse ----
    void strike(Voice& v, float vel, bool fresh) {
        const int p = v.pitch;
        const int model = modelIndex();
        const float age = get(Age);
        const double stretchC = get(Stretch) * 30.0 * (p >= 60 ? 1.0 : -1.0) * std::pow(std::fabs(p - 60.0) / 48.0, 2.0);
        const double cents = (get(Tune) - 0.5) * 100.0 + stretchC + age * 12.0 * keys::keyRand(p, 0);
        const double f0 = 440.0 * std::pow(2.0, (p - 69) / 12.0 + cents / 1200.0);
        const double nyq = sr_ * 0.45;

        // Hammer: hardness (+ velocity) → contact time.
        const float hard = std::clamp(get(Hard) + get(VelHard) * (vel - 0.55f) * 1.2f, 0.0f, 1.0f);
        const double contact = expMap(1.0 - hard, 0.00035, 0.0065) * (model == 3 ? 0.5 : 1.0);
        const int pulseN = std::max(2, static_cast<int>(contact * sr_));

        // Resonator character.
        const double bodyF = get(Body) * 2.0;
        const double kb = (get(KeyBright) - 0.5) * 3.0;   // ±1.5 octaves of brightness per 2 octaves of key
        const double brightF = expMap(get(Bright), 0.15, 4.0) * std::pow(2.0, kb * (p - 60) / 24.0);
        const double decMul = expMap(get(Decay), 0.25, 4.0) * (1.0 + age * 0.35 * keys::keyRand(p, 1));
        double T = 6.5 * std::pow(2.0, -(p - 36) / 18.0);   // fundamental ring time (s) at Decay 100 %
        T = std::clamp(T, 0.3, 14.0) * decMul;

        struct M { double ratio, amp, tau; };
        M ms[kMaxModes]; int nm = 0;
        auto add = [&](double r, double a, double tauMul) { if (nm < kMaxModes) ms[nm++] = { r, a, T * tauMul }; };
        if (model <= 1) {
            const bool suit = model == 1;
            add(1.0, 1.0, 1.0);
            add(1.0 + (suit ? 0.45 : 0.35) / f0, (suit ? 0.55 : 0.4) * bodyF, 1.7);   // tone bar
            add(suit ? 5.9 : 6.27, (suit ? 0.16 : 0.22) * brightF, 0.16);              // tine bell
            add(17.55, 0.06 * brightF, 0.05);
            add(2.0, 0.04 * bodyF, 0.4);
        } else if (model == 2) {
            T *= 0.6;
            add(1.0, 0.6 + 0.4 * bodyF, 1.0);
            add(6.27, 0.3 * brightF, 0.2);
            add(17.55, 0.1 * brightF, 0.07);
            add(3.0, 0.03 * brightF, 0.25);
        } else {
            T *= 0.45;
            const int pos = std::clamp(static_cast<int>(std::lround(get(PuPos) * 2.0f)), 0, 2);
            const double B = 0.00012;
            for (int k = 1; k <= kMaxModes; ++k) {
                const double r = k * std::sqrt(1.0 + B * k * k);
                if (f0 * r >= nyq) break;
                const double wu = std::sin(k * keys::kPi * 0.2), wl = std::sin(k * keys::kPi * 0.34);
                const double pw = pos == 0 ? wu : pos == 2 ? wl : 0.6 * (wu + wl);
                double a = std::sin(k * keys::kPi * 0.12) / k * pw * 3.0 * std::pow(brightF, 0.5 * std::log2((double)k));
                if (k <= 2) a *= 0.5 + 0.5 * bodyF;
                add(r, a, 1.0 / (1.0 + 0.012 * k * k * (1.6 - get(Bright))));
            }
        }

        // Damper time for a released note (s); upper modes go faster.
        const double damp = expMap(1.0 - get(Damper), 0.025, 3.0) * (model == 3 ? 0.5 : 1.0);
        v.nModes = 0;
        for (int i = 0; i < nm; ++i) {
            const double f = f0 * ms[i].ratio;
            if (f >= nyq || f <= 0.0 || std::fabs(ms[i].amp) < 1e-5) continue;
            const int m = v.nModes++;
            const double w = 2.0 * 3.141592653589793 * f / sr_;
            const double R = std::exp(-1.0 / (ms[i].tau * sr_));
            const double tauD = std::min(ms[i].tau, damp / std::sqrt(ms[i].ratio));
            v.w[m] = static_cast<float>(w);
            v.R[m] = static_cast<float>(R);
            v.Rd[m] = static_cast<float>(std::exp(-1.0 / (tauD * sr_)));
            v.amp[m] = static_cast<float>(ms[i].amp);
            v.a1[m] = static_cast<float>(2.0 * R * std::cos(w));
            v.a2[m] = static_cast<float>(R * R);
            v.b[m] = static_cast<float>(ms[i].amp * std::sin(w));
            if (fresh) { v.y1[m] = 0.0f; v.y2[m] = 0.0f; }
        }
        if (!fresh)   // a re-strike rebuilt the table — clear the modes it no longer has
            for (int m = v.nModes; m < kMaxModes; ++m) { v.y1[m] = v.y2[m] = 0.0f; }

        v.model = model;
        v.vel = vel;
        v.pulseN = pulseN; v.pulsePos = 0;
        v.pulseK = static_cast<float>(3.141592653589793 / (2.0 * pulseN));   // Σ pulse = 1
        v.drive = 0.75f * std::pow(std::max(vel, 0.02f), 0.9f);
        v.level = static_cast<float>(1.0 + age * 0.25 * keys::keyRand(p, 2));
        v.symOff = age * 0.15f * keys::keyRand(p, 3);
        v.rng = keys::hash32(static_cast<uint32_t>(p * 977 + static_cast<int>(vel * 127.0f) * 31 + 5));
        // Hammer noise: a short band-passed burst near the strike.
        v.hnEnv = 1.0f;
        v.hnDec = static_cast<float>(std::exp(-1.0 / ((0.004 + 0.006 * (1.0 - hard)) * sr_)));
        v.hnG = vel * (1.0f + age);
        v.hnF = static_cast<float>(std::tan(3.141592653589793 * std::min(1500.0 * std::pow(2.0, (p - 60) / 24.0) * (0.5 + hard), sr_ * 0.4) / sr_));
        if (fresh) { v.hnIc1 = v.hnIc2 = 0.0f; v.rnEnv = 0.0f; v.rnLp = 0.0f; v.peak = vel; }
        v.held = true; v.sustained = false; v.damped = false; v.kill = 0;
        v.active = true;
        float add2[kExprDims]; gexpr_.offsets(kWheelRange, add2);
        v.ex.reset(add2);
        v.exBend = 1e9f; v.ctl = 0;   // the coefficients follow the (reset) bend on the first tick
    }

    void damp(Voice& v) {
        if (v.damped) return;
        v.damped = true;
        v.ctl = 0; v.exBend = 1e9f;   // re-derive coefficients with the damper R on the next tick
        v.rnEnv = std::max(v.rnEnv, std::min(1.0f, v.vel * 1.2f));
        v.rnDec = static_cast<float>(std::exp(-1.0 / (0.03 * sr_)));
    }

    void retune(Voice& v, float bend) {
        const float mul = std::exp2(bend / 12.0f);
        const float wMax = keys::kPi * 0.98f;
        for (int m = 0; m < v.nModes; ++m) {
            const float w = v.w[m] * mul;
            const float R = v.damped ? v.Rd[m] : v.R[m];
            if (w >= wMax) { v.a1[m] = 0.0f; v.a2[m] = 0.0f; v.b[m] = 0.0f; continue; }
            v.a1[m] = 2.0f * R * std::cos(w);
            v.a2[m] = R * R;
            v.b[m] = v.amp[m] * std::sin(w);
        }
    }

    void renderVoice(Voice& v, float* mono, int n, int /*model*/, float sym, float dist, float pnorm,
                     float noiseLin, float relLin, const float exAdd[kExprDims], float exprCoef) {
        const float killStep = 1.0f / static_cast<float>(killLen());
        const float invNorm = 1.0f / pnorm;
        const float relLp = 1.0f - std::exp(-2.0f * keys::kPi * 700.0f / static_cast<float>(sr_));
        for (int i = 0; i < n; ++i) {
            v.ex.tick(exAdd, exprCoef);
            if (--v.ctl <= 0) {
                v.ctl = kCtl;
                const float b = v.ex.bend();
                if (std::fabs(b - v.exBend) > 0.002f) { v.exBend = b; retune(v, b); }
            }
            // Hammer force pulse.
            float exc = 0.0f;
            if (v.pulsePos < v.pulseN) { exc = std::sin((v.pulsePos + 0.5f) * 2.0f * v.pulseK) * v.pulseK; ++v.pulsePos; }
            // Modes → displacement.
            float x = 0.0f;
            for (int m = 0; m < v.nModes; ++m) {
                const float y = v.a1[m] * v.y1[m] - v.a2[m] * v.y2[m] + v.b[m] * exc;
                v.y2[m] = v.y1[m]; v.y1[m] = y;
                x += y;
            }
            // Pickup.
            const float xd = std::clamp(x * v.drive * (1.0f + 0.6f * v.ex.pressure()), -1.6f, 1.6f);
            const float s = std::clamp(sym + v.symOff + 0.4f * v.ex.slide(), -1.5f, 1.5f);
            float o = keys::pickupRaw(xd, s, dist, v.model) * invNorm * v.level * (1.0f + 0.3f * v.ex.pressure());
            // Hammer + damper noise.
            if (v.hnEnv > 1e-4f && noiseLin > 0.0f) {
                const float nz = rnd(v);
                const float g = v.hnF, k = 1.2f;
                const float a1 = 1.0f / (1.0f + g * (g + k)), a2 = g * a1;
                const float v1 = a1 * v.hnIc1 + a2 * (nz - v.hnIc2);
                const float v2 = v.hnIc2 + g * v1;
                v.hnIc1 = 2.0f * v1 - v.hnIc1; v.hnIc2 = 2.0f * v2 - v.hnIc2;
                o += v1 * v.hnEnv * v.hnG * noiseLin * 4.0f;
                v.hnEnv *= v.hnDec;
            }
            if (v.rnEnv > 1e-4f) {
                v.rnLp += relLp * (rnd(v) - v.rnLp);
                o += v.rnLp * v.rnEnv * relLin * 6.0f;
                v.rnEnv *= v.rnDec;
            }
            if (v.kill > 0) {
                o *= static_cast<float>(v.kill) * killStep;
                if (--v.kill <= 0) { v.active = false; return; }
            }
            mono[i] += o * 0.22f;
            v.peak = std::max(v.peak * 0.9995f, std::fabs(o));
        }
        if (v.peak < 3e-5f && v.pulsePos >= v.pulseN && v.rnEnv < 1e-4f) v.active = false;   // rang out
    }

    // ---- the effects chain (mono voices in, stereo out, added into out) ----------
    void updateFx() {
        const double sr = sr_;
        const int cab = std::clamp(static_cast<int>(std::lround(get(Cab) * 3.0f)), 0, 3);
        const float bass = (get(Bass) - 0.5f) * 24.0f, treble = (get(Treble) - 0.5f) * 24.0f;
        if (fxDirty_ || cab != cabCache_ || bass != bassCache_ || treble != trebleCache_) {
            loShelf_.shelf(200.0, bass, sr, false);
            hiShelf_.shelf(2500.0, treble, sr, true);
            for (int c = 0; c < 2; ++c) {
                auto* q = cabF_[c];
                switch (cab) {
                    case 1: q[0].highpass(45, 0.7, sr); q[1].peak(110, 1.0, 3.0, sr); q[2].peak(900, 0.8, -2.0, sr); q[3].lowpass(6000, 0.7, sr); break;
                    case 2: q[0].highpass(120, 0.7, sr); q[1].peak(1600, 0.9, 4.0, sr); q[2].peak(300, 1.0, 1.5, sr); q[3].lowpass(4500, 0.8, sr); break;
                    case 3: q[0].highpass(25, 0.7, sr); q[1].bypass(); q[2].bypass(); q[3].lowpass(16000, 0.7, sr); break;
                    default: for (int k = 0; k < 4; ++k) q[k].bypass(); break;
                }
            }
            if (fxDirty_ || cab != cabCache_) for (int c = 0; c < 2; ++c) for (int k = 0; k < 4; ++k) cabF_[c][k].clear();
            cabCache_ = cab; bassCache_ = bass; trebleCache_ = treble; fxDirty_ = false;
        }
    }

    double tremHz() const {
        if (get(TremSync) >= 0.5f) {
            static const double cpb[8] = { 0.25, 0.5, 1.0, 2.0, 3.0, 4.0, 6.0, 8.0 };   // 1/1 … 1/32
            const int d = std::clamp(static_cast<int>(std::lround(get(TremRate) * 7.0f)), 0, 7);
            return cpb[d] * sr_ / std::max(1.0, spb_);
        }
        return expMap(get(TremRate), 0.5, 15.0);
    }
    double tremCyclesPerBeat() const {
        static const double cpb[8] = { 0.25, 0.5, 1.0, 2.0, 3.0, 4.0, 6.0, 8.0 };
        return cpb[std::clamp(static_cast<int>(std::lround(get(TremRate) * 7.0f)), 0, 7)];
    }

    void renderFx(const float* mono, float* out, int n) {
        if (fxRestart_) {
            fxRestart_ = false;
            resetFx();
            lfo_ = 0.0; phLfo_ = 0.0; chL1_ = 0.0; chL2_ = 0.37; chW_ = 0;
            sDepth_ = sPh_ = sCh_ = sVol_ = 0.0f;
        }
        updateFx();
        const double sr = sr_;
        const bool preOn = get(PreOn) >= 0.5f;
        const float driveG = static_cast<float>(expMap(get(Drive), 1.0, 14.0));
        const float driveComp = 1.0f / std::pow(driveG, 0.6f);
        const bool tremOn = get(TremOn) >= 0.5f, stereo = get(TremMode) >= 0.5f;
        const float tremDepth = tremOn ? get(TremDepth) : 0.0f;
        const double tremInc = tremHz() / sr;
        // Tempo-synced tremolo locks to the grid while the transport rolls (a freeze matches).
        // (render() runs in segments between note events; sinceBlock_ counts the block's frames so far.)
        if (tremOn && get(TremSync) >= 0.5f && playing_) {
            const double ph = (beat_ + sinceBlock_ / std::max(1.0, spb_)) * tremCyclesPerBeat();
            lfo_ = ph - std::floor(ph);
        }
        sinceBlock_ += n;
        const bool phOn = get(PhaserOn) >= 0.5f;
        const double phInc = expMap(get(PhaserRate), 0.05, 5.0) / sr;
        const float phDepth = get(PhaserDepth);
        const bool chOn = get(ChorusOn) >= 0.5f;
        const float chMixT = chOn ? get(ChorusMix) : 0.0f;
        const float vol = volumeGain(get(Volume));
        const float pan = (get(Pan) - 0.5f) * 2.0f;
        const float panL = std::cos((pan + 1.0f) * 0.25f * keys::kPi) * 1.41421356f;
        const float panR = std::sin((pan + 1.0f) * 0.25f * keys::kPi) * 1.41421356f;
        const float sm = 1.0f - std::exp(-1.0f / (0.01f * static_cast<float>(sr)));
        const float dcR = 1.0f - static_cast<float>(2.0 * 3.141592653589793 * 12.0 / sr);
        const float chBase = static_cast<float>(0.007 * sr), chDepth = static_cast<float>(0.0025 * sr);
        const double chInc1 = 0.52 / sr, chInc2 = 0.63 / sr;

        for (int i = 0; i < n; ++i) {
            // DC blocker (the pickup's even term carries DC with the note's envelope).
            float x = mono[i];
            const float dc = x - dcPrev_ + dcR * dcL_;
            dcPrev_ = x; dcL_ = dc; x = dc;

            // Preamp.
            if (preOn) {
                const float d = x * driveG;
                x = (d / (1.0f + std::fabs(d)) + 0.08f * d / (1.0f + d * d)) * driveComp * 1.6f;
                x = hiShelf_.tick(loShelf_.tick(x));
            }

            // Tremolo (mono amplitude, or the stereo pan of a Suitcase).
            sDepth_ += (tremDepth - sDepth_) * sm;
            float l = x, r = x;
            if (sDepth_ > 1e-4f) {
                const float sh = std::tanh(2.2f * std::sin(static_cast<float>(lfo_) * 2.0f * keys::kPi)) / std::tanh(2.2f);
                if (stereo) { l *= 1.0f - sDepth_ * (1.0f + sh) * 0.5f; r *= 1.0f - sDepth_ * (1.0f - sh) * 0.5f; }
                else { const float g = 1.0f - sDepth_ * (1.0f + sh) * 0.5f; l *= g; r *= g; }
            }
            lfo_ += tremInc; if (lfo_ >= 1.0) lfo_ -= 1.0;

            // Phaser: four first-order all-passes per side, the sweep a quarter-turn apart.
            sPh_ += ((phOn ? 1.0f : 0.0f) - sPh_) * sm;
            if (sPh_ > 1e-4f) {
                phLfo_ += phInc; if (phLfo_ >= 1.0) phLfo_ -= 1.0;
                for (int c = 0; c < 2; ++c) {
                    const float ph = static_cast<float>(phLfo_) + c * 0.25f;
                    const float tri = 1.0f - 2.0f * std::fabs(ph - std::floor(ph) - 0.5f) * 2.0f;   // −1..1
                    const float fc = 700.0f * std::exp2(tri * 1.7f * phDepth);
                    const float t = std::tan(keys::kPi * std::min(fc, static_cast<float>(sr) * 0.4f) / static_cast<float>(sr));
                    const float a = (t - 1.0f) / (t + 1.0f);
                    float in = (c ? r : l) + phFb_[c] * 0.35f;
                    for (int s = 0; s < 4; ++s) {
                        const float y = a * in + phZ_[c][s];
                        phZ_[c][s] = in - a * y;
                        in = y;
                    }
                    phFb_[c] = in;
                    float& ch = c ? r : l;
                    ch += (0.5f * (ch + in) - ch) * sPh_;
                }
            }

            // Chorus: a modulated delay per side.
            sCh_ += (chMixT - sCh_) * sm;
            chBuf_[0][chW_] = l; chBuf_[1][chW_] = r;
            if (sCh_ > 1e-4f) {
                chL1_ += chInc1; if (chL1_ >= 1.0) chL1_ -= 1.0;
                chL2_ += chInc2; if (chL2_ >= 1.0) chL2_ -= 1.0;
                const float dL = chBase + chDepth * std::sin(static_cast<float>(chL1_) * 2.0f * keys::kPi);
                const float dR = chBase + chDepth * std::sin(static_cast<float>(chL2_) * 2.0f * keys::kPi + 1.7f);
                const float wl = readCh(0, dL), wr = readCh(1, dR);
                l = l * (1.0f - 0.5f * sCh_) + wl * 0.7f * sCh_;
                r = r * (1.0f - 0.5f * sCh_) + wr * 0.7f * sCh_;
            }
            chW_ = (chW_ + 1) & (kChLen - 1);

            // Cabinet.
            for (int k = 0; k < 4; ++k) { l = cabF_[0][k].tick(l); r = cabF_[1][k].tick(r); }

            // Output.
            sVol_ += (vol - sVol_) * sm;
            l *= sVol_ * panL; r *= sVol_ * panR;
            if (!std::isfinite(l) || !std::isfinite(r)) { resetFx(); l = r = 0.0f; }
            out[i * 2] += l; out[i * 2 + 1] += r;
            pkL_ = std::max(pkL_ * 0.9997f, std::fabs(l));
            pkR_ = std::max(pkR_ * 0.9997f, std::fabs(r));
        }
    }

    float readCh(int c, float delay) const {
        float rp = static_cast<float>(chW_) - delay;
        while (rp < 0.0f) rp += kChLen;
        const int i0 = static_cast<int>(rp);
        const float fr = rp - i0;
        return chBuf_[c][i0 & (kChLen - 1)] * (1.0f - fr) + chBuf_[c][(i0 + 1) & (kChLen - 1)] * fr;
    }

    void resetFx() {
        loShelf_.clear(); hiShelf_.clear();
        for (auto& q : cabF_) for (auto& f : q) f.clear();
        for (auto& z : phZ_) for (auto& s : z) s = 0.0f;
        phFb_[0] = phFb_[1] = 0.0f; dcL_ = dcPrev_ = 0.0f;
        std::memset(chBuf_, 0, sizeof(chBuf_));
    }

    // Volume: 0.8 = 0 dB, 1 = +6 dB, a log taper below (0.4 ≈ −18 dB), 0 = silent.
    static float volumeGain(float v) {
        if (v <= 1e-3f) return 0.0f;
        const float db = v >= 0.8f ? (v - 0.8f) * 30.0f : 60.0f * std::log10(v / 0.8f);
        return std::pow(10.0f, db / 20.0f);
    }

    void publishTelemetry() {
        int active = 0, held = 0, sus = 0;
        for (const auto& v : voices_) {
            if (!v.active || v.kill > 0) continue;
            ++active;
            if (v.held) ++held; else if (v.sustained) ++sus;
        }
        tele_[ScActive].store(static_cast<float>(active), std::memory_order_relaxed);
        tele_[ScLimit].store(static_cast<float>(voiceLimit()), std::memory_order_relaxed);
        tele_[ScHeld].store(static_cast<float>(held), std::memory_order_relaxed);
        tele_[ScSustained].store(static_cast<float>(sus), std::memory_order_relaxed);
        tele_[ScPedal].store(pedalDown_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        tele_[ScLfo].store(static_cast<float>(lfo_), std::memory_order_relaxed);
        tele_[ScPeakL].store(pkL_, std::memory_order_relaxed);
        tele_[ScPeakR].store(pkR_, std::memory_order_relaxed);
        tele_[ScLastNote].store(static_cast<float>(lastNote_), std::memory_order_relaxed);
    }

    std::atomic<float> pn_[kNumParams];
    std::atomic<float> tele_[kScopeN];
    std::atomic<double> pendingRate_{44100.0};
    double sr_ = 0.0;   // 0 → the first applyRate() adopts the pending rate
    Voice voices_[kMaxVoices];
    GlobalExpr gexpr_;
    bool cc64_ = false, pedalDown_ = false;
    int32_t lastNote_ = -1;
    double spb_ = 22050.0, beat_ = 0.0; bool playing_ = false;
    int64_t sinceBlock_ = 0;
    bool looping_ = false; double loopStart_ = 0.0;

    // FX state
    bool fxDirty_ = true, fxRestart_ = false;
    int cabCache_ = -1; float bassCache_ = 1e9f, trebleCache_ = 1e9f;
    keys::Biquad loShelf_, hiShelf_, cabF_[2][4];
    double lfo_ = 0.0, phLfo_ = 0.0, chL1_ = 0.0, chL2_ = 0.37;
    float phZ_[2][4] = {}, phFb_[2] = {};
    float chBuf_[2][kChLen];
    int chW_ = 0;
    float sDepth_ = 0.0f, sPh_ = 0.0f, sCh_ = 0.0f, sVol_ = 0.0f;
    float dcL_ = 0.0f, dcPrev_ = 0.0f;
    float pkL_ = 0.0f, pkR_ = 0.0f;
};

} // namespace nota
