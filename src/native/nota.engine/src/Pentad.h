// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pentad — the built-in 5-voice polyphonic analog synth (kind 14), a musically faithful
// emulation of the Sequential Prophet-5 (Rev 3 architecture: CEM oscillators + CEM3320
// filter). Nota's own DSP, not a schematic clone. Per voice:
//
//   Osc A (saw + pulse, hard-sync to B) ┐
//   Osc B (saw + tri + pulse, lo-freq)  ┼─→ Mixer ─(soft sat)─→ 4-pole OTA low-pass ─→ VCA ─→ pan
//   Noise (white / pink, shared)        ┘                ▲ Filter Env (ADSR)            ▲ Amp Env (ADSR)
//
//   Poly-Mod : Filter Env + Osc B (audio rate) → Osc A freq / Osc A PW / Filter cutoff
//   Wheel-Mod: LFO ↔ noise × (initial amount + mod wheel) → A/B freq, A/B PW, cutoff
//
// Voice management: 5 / 10 / 16 voices, round-robin (authentic — reuses releasing voices,
// cutting their tails) or oldest-note steal with a ≤ 4 ms fade, Poly / Unison / Mono
// modes (unison + mono: low-note priority, legato, shared glide), poly unison stacks
// (×2 / ×5) with detune, per-voice exponential glide, the panel's Release switch.
//
// DSP quality. Each voice runs at an internal ×2 (or ×4) rate so hard sync, audio-rate
// Poly-Mod FM/PWM and the filter's non-linearities stay clean; the stereo voice sum is
// decimated once with HIIR half-band stages. Oscillator edges are band-limited with
// 4-point (cubic B-spline) polyBLEP / polyBLAMP residuals, which suppress the aliases that
// fold back into the audio band far better than the usual 2-point polyBLEP. The filter is
// a zero-delay-feedback (TPT) cascade of four OTA integrators (dy/dt = ωc·tanh(x − y), the
// CEM3320 topology) with a resonance loop; the tanh terms are linearised per sample from
// the previous sample's stage differences ("cheap non-linear ZDF"), so the loop is solved
// exactly each sample and self-oscillates as a clean, key-trackable sine near the top of
// Resonance. Bass thins with resonance (authentic); the Bass-Comp option restores it.
//
// Analog character ("Vintage Drift"): per-voice static spread of oscillator pitch, cutoff,
// envelope times and pulse width, plus a slow random walk, all derived from a seed that is
// a parameter — so it persists with the project/preset and a render is bit-identical for
// the same seed (Freeze: offline == realtime). Oscillators start at random phases
// (free-running emulation) and a -100 dBFS noise floor rides the VCA (switchable).
//
// Determinism: all state advances per sample with persistent counters (no per-block
// decisions), and every seeded state is re-seeded on a transport play edge (when silent)
// and on allNotesOff(), so a render from the same position is reproducible regardless of
// the host's block size.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable ids)
// → automation / MIDI learn / persist / clone for free. Header-only; allocation-free after
// construction; construction is cheap (no tables).

#pragma once

#include "Instrument.h"
#include "hiir/Downsampler2xFpu.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>

#if defined(__SSE2__) || defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#include <xmmintrin.h>
#define NOTA_PENTAD_FTZ_X86 1
#elif defined(__aarch64__) && !defined(_MSC_VER)
#define NOTA_PENTAD_FTZ_ARM 1
#endif

namespace nota {

namespace pentad {

// ---- fast math (accurate enough for pitch / cutoff; branch-light) ---------------------
inline float exp2f(float x) {
    x = std::clamp(x, -60.0f, 60.0f);
    const float fi = std::floor(x);
    const float f = x - fi;
    // Taylor series of 2^f on [0,1): ~1.5e-5 relative error (≈0.03 cent).
    const float p = 1.0f + f * (0.69314718f + f * (0.24022651f + f * (0.05550411f + f * (0.00961813f
                  + f * (0.00133336f + f * 0.00015404f)))));
    const int32_t e = static_cast<int32_t>(fi) + 127;
    const uint32_t bits = static_cast<uint32_t>(e) << 23;
    float s; std::memcpy(&s, &bits, sizeof s);
    return p * s;
}
// tanh(v)/v (rational tanh, exact ±1 limit beyond |v| = 3) — the per-sample
// linearisation gain of an OTA stage.
inline float tanhRatio(float v) {
    const float a = std::fabs(v);
    if (a > 3.0f) return 1.0f / a;
    const float v2 = v * v;
    return (27.0f + v2) / (27.0f + 9.0f * v2);
}
// Mixer overdrive: a cubic soft clipper (unity slope at 0, flat at ±1.5) and its
// antiderivative, run through first-order ADAA so the drive doesn't alias.
inline double softClip(double x) {
    if (x >= 1.5) return 1.0;
    if (x <= -1.5) return -1.0;
    return x - (4.0 / 27.0) * x * x * x;
}
inline double softClipAD(double x) {
    const double a = std::fabs(x);
    if (a >= 1.5) return a - 0.5625;   // continues F(1.5) = 0.9375 with slope 1
    const double x2 = x * x;
    return 0.5 * x2 - x2 * x2 / 27.0;
}
struct Adaa {
    double x1 = 0.0, f1 = 0.0;
    float process(double x) {
        const double f = softClipAD(x), dx = x - x1;
        const double y = std::fabs(dx) > 1e-5 ? (f - f1) / dx : softClip(0.5 * (x + x1));
        x1 = x; f1 = f;
        return static_cast<float>(y);
    }
    void reset() { x1 = f1 = 0.0; }
};
// Padé tan for the bilinear prewarp, x ∈ [0, ~1.3].
inline float tanPrewarp(float x) {
    const float x2 = x * x;
    return x * (15.0f - x2) / (15.0f - 6.0f * x2);
}
inline uint32_t hash32(uint32_t x) {
    x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
    return x;
}
struct Rng {
    uint32_t s = 0x9E3779B9U;
    void seed(uint32_t v) { s = v ? v : 0x9E3779B9U; }
    uint32_t next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
    float uni() { return static_cast<float>(next() >> 8) * (1.0f / 16777216.0f); }   // [0,1)
    float bi() { return uni() * 2.0f - 1.0f; }                                         // [-1,1)
};

// ---- 4-point band-limiting residuals (integrated cubic B-spline) ----------------------
// d = distance (in samples, 0..1) from the discontinuity to the current sample. Taps are
// for samples m-2, m-1, m, m+1 (m = the sample being computed), so an oscillator's output
// is delayed by two samples through a 4-slot ring.
inline void blepTaps(float d, float r[4]) {
    d = std::clamp(d, 0.0f, 0.99999f);
    const float d2 = d * d, d3 = d2 * d, d4 = d2 * d2;
    const float e = d - 1.0f, e2 = e * e, e3 = e2 * e, e4 = e2 * e2;
    const float o = 1.0f - d, o2 = o * o;
    r[0] = d4 * (1.0f / 24.0f);
    r[1] = 1.0f / 24.0f + (4.0f * e - 2.0f * e3 - 0.75f * e4 + 2.75f) * (1.0f / 6.0f);
    r[2] = -(1.0f / 24.0f + (-4.0f * d + 2.0f * d3 - 0.75f * d4 + 2.75f) * (1.0f / 6.0f));
    r[3] = -(o2 * o2) * (1.0f / 24.0f);
}
inline float blampD(float t) {   // ∫ of the integrated B-spline, t ∈ [-2, 0]
    if (t <= -1.0f) { const float u = 2.0f + t, u2 = u * u; return u2 * u2 * u * (1.0f / 120.0f); }
    const float t2 = t * t, t4 = t2 * t2, t5 = t4 * t;
    return 1.0f / 120.0f + (t + 1.0f) * (1.0f / 24.0f)
         + (2.0f * t2 - 0.5f * t4 - 0.15f * t5 + 2.75f * t + 1.1f) * (1.0f / 6.0f);
}
inline void blampTaps(float d, float r[4]) {
    d = std::clamp(d, 0.0f, 0.99999f);
    r[0] = blampD(d - 2.0f); r[1] = blampD(d - 1.0f); r[2] = blampD(-d); r[3] = blampD(-d - 1.0f);
}

struct Ring {
    float a[4] = { 0, 0, 0, 0 };
    uint32_t m = 0;
    void naive(float v) { a[m & 3u] += v; }
    void blep(float d, float h) {
        if (h == 0.0f) return;
        float r[4]; blepTaps(d, r);
        a[(m - 2u) & 3u] += h * r[0]; a[(m - 1u) & 3u] += h * r[1];
        a[m & 3u] += h * r[2];        a[(m + 1u) & 3u] += h * r[3];
    }
    void blamp(float d, float h) {
        if (h == 0.0f) return;
        float r[4]; blampTaps(d, r);
        a[(m - 2u) & 3u] += h * r[0]; a[(m - 1u) & 3u] += h * r[1];
        a[m & 3u] += h * r[2];        a[(m + 1u) & 3u] += h * r[3];
    }
    float pop() { const uint32_t o = (m - 2u) & 3u; const float v = a[o]; a[o] = 0.0f; ++m; return v; }
    void clear() { a[0] = a[1] = a[2] = a[3] = 0.0f; }
};

// Saw + pulse core. `hi` = comparator state (phase < duty).
struct Osc {
    double ph = 0.0;
    bool hi = true;
    Ring ring;
};

// Pulse amplitude near the extremes of the width: the comparator stops flipping below 2 %
// and above 98 % (silence), with a short fade so PWM sweeps into the dead zone don't click.
inline float pulseZone(float duty) {
    auto sm = [](float x) { x = std::clamp(x, 0.0f, 1.0f); return x * x * (3.0f - 2.0f * x); };
    return sm((duty - 0.02f) / 0.03f) * sm((0.98f - duty) / 0.03f);
}

// Advance a saw/pulse core by `adv` phase over a span ending `tEnd` samples before the
// current sample; emits the edge residuals. Returns the distance of a phase wrap, or -1.
inline float advance(Osc& o, double adv, float tEnd, double dt, float duty, float gSaw, float gPul) {
    const double p0 = o.ph;
    double p1 = p0 + adv;
    const float invDt = static_cast<float>(1.0 / dt);
    float wrapD = -1.0f;
    if (p1 >= 1.0) {
        p1 -= 1.0;
        if (p1 >= 1.0) p1 -= std::floor(p1);
        wrapD = tEnd + static_cast<float>(p1) * invDt;
        if (o.hi && p0 < duty) { o.ring.blep(tEnd + static_cast<float>(1.0 - duty + p1) * invDt, -2.0f * gPul); o.hi = false; }
        o.ring.blep(wrapD, -2.0f * gSaw);
        if (!o.hi) { o.ring.blep(wrapD, 2.0f * gPul); o.hi = true; }
        if (p1 >= duty) { o.ring.blep(tEnd + static_cast<float>(p1 - duty) * invDt, -2.0f * gPul); o.hi = false; }
    } else {
        const bool nh = p1 < duty;
        if (nh != o.hi) {   // phase crossed the width (or the width moved across the phase)
            float d = tEnd + static_cast<float>(std::fabs(p1 - duty)) * invDt;
            const float lim = tEnd + static_cast<float>(adv) * invDt;
            if (d > lim) d = lim;
            o.ring.blep(d, nh ? 2.0f * gPul : -2.0f * gPul);
            o.hi = nh;
        }
    }
    o.ph = p1;
    return wrapD;
}

} // namespace pentad

class Pentad final : public Instrument {
public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        // --- controllers / voice / global (0..23) ---
        Tune = 0, Bend, ModWheel, Aftertouch, BendRange, Glide, GlideMode, VoiceMode,
        Polyphony, Alloc, Unison, UniDetune, ReleaseOn, Drift, Seed, Volume,
        Spread, VelAmp, VelFilt, AtCutoff, AtLfo, NoiseFloor, LowComp, Oversample,
        // --- oscillator A (24..30) ---
        OaOct, OaSemi, OaFine, OaSaw, OaPulse, OaPw, OaSync,
        // --- oscillator B (31..39) ---
        ObOct, ObSemi, ObFine, ObSaw, ObTri, ObPulse, ObPw, ObLoFreq, ObKbd,
        // --- mixer (40..46) ---
        MixA, MixAOn, MixB, MixBOn, MixNoise, MixNoiseOn, NoiseColor,
        // --- filter (47..50) ---
        Cutoff, Reso, FEnvAmt, KeyTrk,
        // --- filter envelope (51..54) ---
        FAttack, FDecay, FSustain, FRelease,
        // --- amp envelope (55..58) ---
        AAttack, ADecay, ASustain, ARelease,
        // --- LFO (59..64) ---
        LfoRate, LfoSaw, LfoTri, LfoSquare, LfoAmt, LfoSync,
        // --- wheel-mod (65..70) ---
        WmMix, WmFreqA, WmFreqB, WmPwA, WmPwB, WmFilter,
        // --- poly-mod (71..75) ---
        PmEnv, PmOscB, PmFreqA, PmPwA, PmFilter,
        kNumParams
    };

    static constexpr int kMaxVoices = 16;

    // Scope telemetry layout (scopeRead): see publishTelemetry().
    enum Scope { ScPoly = 0, ScActive, ScPeakL, ScPeakR, ScMixPeak, ScCpu, ScOs, ScRate, ScVoices, kScopeN = ScVoices + kMaxVoices };

    Pentad() {
        // A plain two-saw brass-ish init: Osc A + B saws (B a few cents sharp), filter half
        // open with a moderate envelope and half keyboard tracking, organ-like amp envelope.
        set(Tune, 0.5f); set(Bend, 0.5f); set(ModWheel, 0.0f); set(Aftertouch, 0.0f);
        set(BendRange, 1.0f / 11.0f); set(Glide, 0.0f); set(GlideMode, 0.0f); set(VoiceMode, 0.0f);
        set(Polyphony, 0.0f); set(Alloc, 0.0f); set(Unison, 0.0f); set(UniDetune, 0.3f);
        set(ReleaseOn, 1.0f); set(Drift, 0.25f); set(Seed, 0.5f); set(Volume, 0.7f);
        set(Spread, 0.3f); set(VelAmp, 0.0f); set(VelFilt, 0.0f); set(AtCutoff, 0.0f);
        set(AtLfo, 0.0f); set(NoiseFloor, 1.0f); set(LowComp, 0.0f); set(Oversample, 0.0f);
        set(OaOct, 2.0f / 3.0f); set(OaSemi, 0.5f); set(OaFine, 0.5f); set(OaSaw, 1.0f);
        set(OaPulse, 0.0f); set(OaPw, 0.5f); set(OaSync, 0.0f);
        set(ObOct, 2.0f / 3.0f); set(ObSemi, 0.5f); set(ObFine, 0.54f); set(ObSaw, 1.0f);
        set(ObTri, 0.0f); set(ObPulse, 0.0f); set(ObPw, 0.5f); set(ObLoFreq, 0.0f); set(ObKbd, 1.0f);
        set(MixA, 0.8f); set(MixAOn, 1.0f); set(MixB, 0.6f); set(MixBOn, 1.0f);
        set(MixNoise, 0.0f); set(MixNoiseOn, 1.0f); set(NoiseColor, 0.0f);
        set(Cutoff, 0.55f); set(Reso, 0.1f); set(FEnvAmt, 0.35f); set(KeyTrk, 0.5f);
        set(FAttack, 0.0f); set(FDecay, 0.45f); set(FSustain, 0.3f); set(FRelease, 0.4f);
        set(AAttack, 0.0f); set(ADecay, 0.45f); set(ASustain, 0.85f); set(ARelease, 0.4f);
        set(LfoRate, 0.55f); set(LfoSaw, 0.0f); set(LfoTri, 1.0f); set(LfoSquare, 0.0f);
        set(LfoAmt, 0.0f); set(LfoSync, 0.0f);
        set(WmMix, 0.0f); set(WmFreqA, 1.0f); set(WmFreqB, 1.0f); set(WmPwA, 0.0f); set(WmPwB, 0.0f); set(WmFilter, 0.0f);
        set(PmEnv, 0.0f); set(PmOscB, 0.0f); set(PmFreqA, 0.0f); set(PmPwA, 0.0f); set(PmFilter, 0.0f);
        initDecimators();
        resetAll();
    }

    int32_t kind() const override { return 14; }
    const char* displayName() const override { return "Nota Pentad"; }

    // Deferred to the audio thread (render) so a device-rate change can't race a render.
    void setSampleRate(double sr) override { pendingRate_.store(sr > 0 ? sr : 44100.0, std::memory_order_relaxed); }

    int32_t activeVoiceCount() const override { return static_cast<int32_t>(tele_[ScActive].load(std::memory_order_relaxed)); }
    int32_t scopeRead(float* out, int32_t maxN) const override {
        const int n = std::min<int>(maxN, kScopeN);
        for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
        return n;
    }
    int32_t heldNotes(int32_t* out, int32_t maxN) const override {
        const int n = std::min<int>(heldN_, maxN);
        for (int i = 0; i < n; ++i) out[i] = held_[i];
        return n;
    }

    // ---- parameters -------------------------------------------------------
    // Display names group by the part before the last space (the automation menu nests on it).
    static const char* paramId(int i) {
        static const char* ids[kNumParams] = {
            "tune", "bend", "modwheel", "aftertouch", "bendrange", "glide", "glidemode", "voicemode",
            "polyphony", "alloc", "unison", "unidetune", "releaseon", "drift", "seed", "volume",
            "spread", "velamp", "velfilt", "atcutoff", "atlfo", "noisefloor", "lowcomp", "oversample",
            "oaoct", "oasemi", "oafine", "oasaw", "oapulse", "oapw", "oasync",
            "oboct", "obsemi", "obfine", "obsaw", "obtri", "obpulse", "obpw", "oblofreq", "obkbd",
            "mixa", "mixaon", "mixb", "mixbon", "mixnoise", "mixnoiseon", "noisecolor",
            "cutoff", "reso", "fenvamt", "keytrk",
            "fattack", "fdecay", "fsustain", "frelease",
            "aattack", "adecay", "asustain", "arelease",
            "lforate", "lfosaw", "lfotri", "lfosquare", "lfoamt", "lfosync",
            "wmmix", "wmfreqa", "wmfreqb", "wmpwa", "wmpwb", "wmfilter",
            "pmenv", "pmoscb", "pmfreqa", "pmpwa", "pmfilter" };
        return (i >= 0 && i < kNumParams) ? ids[i] : "";
    }
    static const char* paramName(int i) {
        static const char* nm[kNumParams] = {
            "Master Tune", "Pitch Bend", "Mod Wheel", "Aftertouch Amount", "Bend Range", "Glide Time", "Glide Mode", "Voice Mode",
            "Voice Count", "Voice Allocation", "Unison Stack", "Unison Detune", "Release Switch", "Vintage Drift", "Vintage Seed", "Output Volume",
            "Output Spread", "Velocity Amp", "Velocity Filter", "Aftertouch Cutoff", "Aftertouch LFO", "Analog Noise-Floor", "Filter Bass-Comp", "Quality Oversampling",
            "Osc A Octave", "Osc A Semitone", "Osc A Fine", "Osc A Saw", "Osc A Pulse", "Osc A Pulse-Width", "Osc A Sync",
            "Osc B Octave", "Osc B Semitone", "Osc B Fine", "Osc B Saw", "Osc B Triangle", "Osc B Pulse", "Osc B Pulse-Width", "Osc B Lo-Freq", "Osc B Keyboard",
            "Mixer Osc-A Level", "Mixer Osc-A On", "Mixer Osc-B Level", "Mixer Osc-B On", "Mixer Noise Level", "Mixer Noise On", "Mixer Noise Color",
            "Filter Cutoff", "Filter Resonance", "Filter Env-Amount", "Filter Key-Track",
            "Filter-Env Attack", "Filter-Env Decay", "Filter-Env Sustain", "Filter-Env Release",
            "Amp-Env Attack", "Amp-Env Decay", "Amp-Env Sustain", "Amp-Env Release",
            "LFO Rate", "LFO Saw", "LFO Triangle", "LFO Square", "LFO Initial-Amount", "LFO Tempo-Sync",
            "Wheel-Mod Source-Mix", "Wheel-Mod Freq-A", "Wheel-Mod Freq-B", "Wheel-Mod PW-A", "Wheel-Mod PW-B", "Wheel-Mod Filter",
            "Poly-Mod Filter-Env", "Poly-Mod Osc-B", "Poly-Mod Freq-A", "Poly-Mod PW-A", "Poly-Mod Filter" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }

    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override { return paramId(i); }
    std::string pluginParamName(int32_t i) const override { return paramName(i); }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (id == paramId(i)) return i;
        return -1;
    }

    // ---- project state (kNumParams normalized floats, little-endian; includes the seed) ----
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            const float v = pn_[i].load(std::memory_order_relaxed);
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
        auto s = std::make_shared<Pentad>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(pendingRate_.load(std::memory_order_relaxed));
        return s;
    }

    // ---- musical mappings (shared with the editor's readouts) ---------------
    static int   polyphonyOf(float v) { static const int n[3] = { 5, 10, 16 }; return n[std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2)]; }
    static int   unisonOf(float v)    { static const int n[3] = { 1, 2, 5 };   return n[std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2)]; }
    static int   bendRangeOf(float v) { return 1 + std::clamp(static_cast<int>(std::lround(v * 11.0f)), 0, 11); }
    static int   octaveOf(float v)    { return std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3) - 2; }   // 32'..4' → -2..+1
    static int   semiOf(float v)      { return std::clamp(static_cast<int>(std::lround((v - 0.5f) * 24.0f)), -12, 12); }
    static float fineCentsOf(float v) { return (v - 0.5f) * 100.0f; }
    static float cutoffHzOf(float v)  { return 20.0f * std::pow(1000.0f, std::clamp(v, 0.0f, 1.0f)); }
    static float attackSecOf(float v) { return static_cast<float>(expMap(v, 0.0005, 10.0)); }
    static float decaySecOf(float v)  { return static_cast<float>(expMap(v, 0.002, 15.0)); }
    static float glideSecOf(float v)  { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.005, 10.0)); }
    static float lfoHzOf(float v)     { return static_cast<float>(expMap(v, 0.05, 20.0)); }
    static constexpr int kSyncDivs = 14;
    static float syncBeatsOf(float v) {   // cycle length in beats (quarter notes)
        static const float b[kSyncDivs] = { 32.f, 16.f, 8.f, 4.f, 2.f, 1.5f, 1.f, 0.75f, 2.f / 3.f, 0.5f, 1.f / 3.f, 0.25f, 1.f / 6.f, 0.125f };
        return b[std::clamp(static_cast<int>(std::lround(v * (kSyncDivs - 1))), 0, kSyncDivs - 1)];
    }
    static float volumeGainOf(float v) { return 2.0f * v * v; }

    // ---- transport (play edge → reseed for determinism; LFO tempo sync) -----
    // A play edge re-seeds when nothing sounds (a live-held key survives pressing Play); a
    // jump of the rolling timeline (seek, loop wrap, a freeze bounce restarting at 0 without
    // a stopped block in between) always does — the old voices belong to another position.
    // Positions compare in samples, so tempo changes never read as a jump.
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override {
        const double pos = beatStart * samplesPerBeat;
        const bool jumped = playing && tPlaying_ && std::fabs(pos - (tPos_ + tFrames_)) > 2.0;
        if (jumped) resetAll();
        else if (playing && !tPlaying_) {
            bool any = false;
            for (int v = 0; v < kMaxVoices; ++v) any |= voices_[v].active;
            if (!any) resetAll();
        }
        tBeat_ = beatStart; tSpb_ = samplesPerBeat; tFresh_ = true;
        tPos_ = pos; tFrames_ = 0.0;
        tPlaying_ = playing;
    }

    // ---- note events ----------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        syncStructure();
        const int mode = voiceMode();
        const bool othersHeld = heldN_ > 0;
        pushHeld(pitch);
        const float vel = std::clamp(velocity, 0.0f, 1.0f);
        const int N = polyphonyOf(get(Polyphony));
        const float glideT = glideSecOf(get(Glide));
        if (mode == 0) {
            const int gm = std::clamp(static_cast<int>(std::lround(get(GlideMode) * 2.0f)), 0, 2);
            const bool glide = glideT > 0.0f && (gm == 1 || (gm == 2 && othersHeld));
            const int S = std::min(unisonOf(get(Unison)), N);
            for (int s = 0; s < S; ++s) {
                int v = -1;
                for (int k = 0; k < N; ++k) {   // the same key again → retrigger its voice
                    const Voice& V = voices_[k];
                    if (V.active && V.note == pitch && V.stackIdx == s && V.stackN == S && V.pendNote < 0) { v = k; break; }
                }
                bool fade = false;
                if (v < 0) v = allocate(N, fade);
                if (fade) {
                    Voice& V = voices_[v];
                    V.pendNote = pitch; V.pendVel = vel; V.pendStack = s; V.pendStackN = S; V.pendGlide = glide;
                    V.fade = fadeLen_;
                } else {
                    startVoice(v, pitch, vel, s, S, glide);
                }
            }
        } else {
            const int S = (mode == 1) ? N : 1;
            const int low = lowestHeld();
            if (!othersHeld) {
                for (int s = 0; s < S; ++s) startVoice(s, low, vel, s, S, glideT > 0.0f);
            } else {
                for (int s = 0; s < S; ++s) { Voice& V = voices_[s]; V.note = low; V.pitchTgt = static_cast<float>(low); if (glideT <= 0.0f) V.pitch = V.pitchTgt; }
            }
        }
    }
    void noteOff(int32_t pitch) override {
        syncStructure();
        removeHeld(pitch);
        if (voiceMode() == 0) {
            for (int v = 0; v < kMaxVoices; ++v) {
                Voice& V = voices_[v];
                if (V.gate && V.note == pitch) release(V);
                if (V.pendNote == pitch) V.pendNote = -1;   // stolen-voice note ended before it started
            }
            return;
        }
        const int S = voiceMode() == 1 ? polyphonyOf(get(Polyphony)) : 1;
        if (heldN_ == 0) { for (int s = 0; s < S; ++s) if (voices_[s].gate) release(voices_[s]); return; }
        const int low = lowestHeld();
        const bool glide = glideSecOf(get(Glide)) > 0.0f;
        for (int s = 0; s < S; ++s) {
            Voice& V = voices_[s];
            if (!V.active) continue;
            V.note = low; V.pitchTgt = static_cast<float>(low);
            if (!glide) V.pitch = V.pitchTgt;
        }
    }
    void allNotesOff() override { resetAll(); }

    // ---- render ---------------------------------------------------------------
    void render(float* out, int32_t frames) override {
        FtzGuard ftz;
        const auto t0 = std::chrono::steady_clock::now();
        const double pr = pendingRate_.load(std::memory_order_relaxed);
        if (pr != sampleRate_) {   // voices keep playing; only rate-derived state restarts
            sampleRate_ = pr;
            fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
            for (int c = 0; c < 2; ++c) { dnSteep_[c].clear_buffers(); dnMid_[c].clear_buffers(); }
            smoothInit_ = false;
        }
        for (int32_t done = 0; done < frames;) {
            const int n = std::min<int>(kChunk, frames - done);
            renderChunk(out + done * 2, n);
            done += n;
        }
        tFrames_ += frames;
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        const double avail = frames / sampleRate_;
        if (avail > 0.0) cpu_ += 0.05 * (el / avail - cpu_);
        publishTelemetry(frames);
    }

private:
    static constexpr int kChunk = 64;
    static constexpr int kMaxOs = 4;
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr float kStageDrive = 0.35f;   // OTA stage saturation (colour)
    static constexpr float kLoopDrive = 2.0f;     // resonance-loop saturation (self-osc level)

    struct Voice {
        bool active = false, gate = false;
        int note = 60, stackIdx = 0, stackN = 1;
        float vel = 0.8f;
        uint64_t order = 0;
        // stolen-voice handoff (oldest-steal fade)
        int pendNote = -1, pendStack = 0, pendStackN = 1, fade = 0;
        float pendVel = 0.0f; bool pendGlide = false;
        // pitch (semitones, glided)
        float pitch = 60.0f, pitchTgt = 60.0f; bool hasPitch = false;
        // envelopes: stage 0 idle, 1 attack, 2 decay/sustain, 3 release
        float fenv = 0.0f, aenv = 0.0f; int fst = 0, ast = 0;
        pentad::Osc A, B;
        float s[4] = { 0, 0, 0, 0 }, df[4] = { 0, 0, 0, 0 };   // filter integrator states / stage diffs
        float y4 = 0.0f;                                           // last filter output (loop saturation)
        pentad::Adaa sat;                                          // mixer overdrive
        // vintage: static spread (from the seed) + slow random walk
        float oA = 0, oB = 0, oCut = 0, oEnvF = 0, oEnvA = 0, oPw = 0;
        float wA = 0, wB = 0, wC = 0, tA = 0, tB = 0, tC = 0; int wCount = 0;
        pentad::Rng rng;
        // previous base-rate controls, interpolated across the oversampled sub-steps
        double pDtA = 0, pDtB = 0; float pG = 0, pCut = 0, pPwA = 0.5f, pPwB = 0.5f, pAmp = 0;
        bool fresh = true;
        float panL = 1.0f, panR = 1.0f;
        float level = 0.0f;   // telemetry
    };

    // Flush-to-zero / denormals-are-zero for the render (restored after).
    struct FtzGuard {
#if defined(NOTA_PENTAD_FTZ_X86)
        unsigned int old; FtzGuard() : old(_mm_getcsr()) { _mm_setcsr(old | 0x8040u); } ~FtzGuard() { _mm_setcsr(old); }
#elif defined(NOTA_PENTAD_FTZ_ARM)
        uint64_t old = 0;
        FtzGuard() { asm volatile("mrs %0, fpcr" : "=r"(old)); const uint64_t n = old | (1ull << 24); asm volatile("msr fpcr, %0" : : "r"(n)); }
        ~FtzGuard() { asm volatile("msr fpcr, %0" : : "r"(old)); }
#endif
    };

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    int voiceMode() const { return std::clamp(static_cast<int>(std::lround(get(VoiceMode) * 2.0f)), 0, 2); }
    uint32_t seedValue() const { return static_cast<uint32_t>(std::lround(std::clamp(get(Seed), 0.0f, 1.0f) * 16777215.0f)); }

    // ---- held keys --------------------------------------------------------------
    void pushHeld(int p) { for (int i = 0; i < heldN_; ++i) if (held_[i] == p) return; if (heldN_ < 128) held_[heldN_++] = p; }
    void removeHeld(int p) { int w = 0; for (int r = 0; r < heldN_; ++r) if (held_[r] != p) held_[w++] = held_[r]; heldN_ = w; }
    int lowestHeld() const { int b = heldN_ > 0 ? held_[0] : 60; for (int i = 1; i < heldN_; ++i) b = std::min(b, held_[i]); return b; }

    // ---- voice allocation -----------------------------------------------------
    int allocate(int N, bool& fade) {
        fade = false;
        if (get(Alloc) < 0.5f) {   // round-robin: next voice in rotation that isn't held
            for (int k = 0; k < N; ++k) {
                const int v = (rr_ + k) % N;
                if (!voices_[v].gate && voices_[v].pendNote < 0) { rr_ = (v + 1) % N; return v; }
            }
            const int v = rr_ % N; rr_ = (v + 1) % N;   // all held: reassign in rotation
            return v;
        }
        int best = -1; uint64_t bo = ~0ull;   // oldest steal: idle (least recently used) …
        for (int v = 0; v < N; ++v) if (!voices_[v].active && voices_[v].pendNote < 0 && voices_[v].order < bo) { bo = voices_[v].order; best = v; }
        if (best >= 0) return best;
        for (int v = 0; v < N; ++v) if (!voices_[v].gate && voices_[v].pendNote < 0 && voices_[v].order < bo) { bo = voices_[v].order; best = v; }
        if (best >= 0) return best;           // … then the oldest releasing voice, then fade-steal
        for (int v = 0; v < N; ++v) if (voices_[v].pendNote < 0 && voices_[v].order < bo) { bo = voices_[v].order; best = v; }
        if (best < 0) best = 0;
        fade = true;
        return best;
    }

    void startVoice(int v, int note, float vel, int s, int S, bool glide) {
        Voice& V = voices_[v];
        V.note = note; V.vel = vel; V.gate = true; V.stackIdx = s; V.stackN = S; V.order = ++orderCtr_;
        V.pitchTgt = static_cast<float>(note);
        if (!V.hasPitch || !glide) V.pitch = V.pitchTgt;
        V.hasPitch = true;
        V.fst = 1; V.ast = 1;   // retrigger from the current level (no restart at zero)
        if (!V.active) wake(V);
    }
    // A sleeping voice wakes: free-running oscillators land at arbitrary phases; the
    // filter/BLEP state it slept with is stale and cleared.
    void wake(Voice& V) {
        V.active = true; V.fresh = true;
        V.A.ph += V.rng.uni(); V.A.ph -= std::floor(V.A.ph);
        V.B.ph += V.rng.uni(); V.B.ph -= std::floor(V.B.ph);
        V.A.hi = V.A.ph < get(OaPw); V.B.hi = V.B.ph < get(ObPw);
        V.A.ring.clear(); V.B.ring.clear(); V.sat.reset();
        for (int j = 0; j < 4; ++j) { V.s[j] = 0.0f; V.df[j] = 0.0f; }
        V.y4 = 0.0f;
    }
    static void release(Voice& V) { V.gate = false; if (V.fst) V.fst = 3; if (V.ast) V.ast = 3; }

    // ---- reset / seeding --------------------------------------------------------
    void computeStatics(int v) {
        pentad::Rng r; r.seed(pentad::hash32(seedCache_ * 2654435761U + pentad::hash32(static_cast<uint32_t>(v) + 17U)));
        Voice& V = voices_[v];
        V.oA = r.bi(); V.oB = r.bi(); V.oCut = r.bi(); V.oEnvF = r.bi(); V.oEnvA = r.bi(); V.oPw = r.bi();
    }
    void resetAll() {
        seedCache_ = seedValue();
        for (int v = 0; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            V = Voice{};
            V.rng.seed(pentad::hash32(seedCache_ ^ pentad::hash32(static_cast<uint32_t>(v) * 0x9E3779B9U + 1U)));
            V.A.ph = V.rng.uni(); V.B.ph = V.rng.uni();
            V.wCount = 1 + static_cast<int>(V.rng.next() % 4096u);
            computeStatics(v);
        }
        heldN_ = 0; rr_ = 0; orderCtr_ = 0;
        lfoPh_ = 0.0; tFresh_ = true;
        noiseRng_.seed(pentad::hash32(seedCache_ ^ 0xA5A5A5A5U));
        wmRng_.seed(pentad::hash32(seedCache_ ^ 0x5A5A5A5AU));
        pk0_ = pk1_ = pk2_ = 0.0f;
        for (int c = 0; c < 2; ++c) { dnSteep_[c].clear_buffers(); dnMid_[c].clear_buffers(); }
        smoothInit_ = false;
        lastMode_ = voiceMode(); lastPoly_ = polyphonyOf(get(Polyphony));
        fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
        peakL_ = peakR_ = mixPeak_ = 0.0f;
    }
    void initDecimators() {
        static const double steep[12] = {
            0.017347915108876406, 0.067150480426919179, 0.14330738338179819, 0.23745131944299824,
            0.34085550201503761, 0.44601111310335906, 0.54753112652956148, 0.6423859124721446,
            0.72968928615804163, 0.81029959388029904, 0.88644514917318362, 0.96150605146543733 };
        static const double mid[5] = {
            0.029113887601773612, 0.11638402872809682, 0.26337786480329456, 0.47885453461538624,
            0.78984065611473109 };
        for (int c = 0; c < 2; ++c) { dnSteep_[c].set_coefs(steep); dnMid_[c].set_coefs(mid); }
    }

    // Voice mode / polyphony / seed changes, applied before any note event or render
    // sees them (audio thread): a mode switch releases everything, fewer voices fade
    // the surplus out, a new seed re-spreads the per-voice vintage offsets.
    void syncStructure() {
        const int mode = voiceMode();
        if (mode != lastMode_) { for (auto& V : voices_) if (V.gate) release(V); heldN_ = 0; lastMode_ = mode; }
        const int N = polyphonyOf(get(Polyphony));
        if (N != lastPoly_) {
            for (int v = N; v < kMaxVoices; ++v) { Voice& V = voices_[v]; if (V.active && V.fade == 0) { V.pendNote = -1; V.fade = fadeLen_; V.gate = false; } }
            rr_ = 0; lastPoly_ = N;
        }
        const uint32_t sd = seedValue();
        if (sd != seedCache_) { seedCache_ = sd; for (int v = 0; v < kMaxVoices; ++v) computeStatics(v); }
    }

    // ---- one chunk (≤ kChunk frames) ---------------------------------------------
    void renderChunk(float* out, int n) {
        float P[kNumParams];
        for (int i = 0; i < kNumParams; ++i) P[i] = pn_[i].load(std::memory_order_relaxed);
        const double sr = sampleRate_;

        syncStructure();

        // Oversampling factor: ×2 / ×4 at ≤ 48 kHz, halved at 88.2/96, ×1 at 176.4/192.
        int os = P[Oversample] > 0.5f ? 4 : 2;
        if (sr >= 176000.0) os = 1; else if (sr >= 88000.0) os = std::max(1, os / 2);
        if (os != osCur_) { for (int c = 0; c < 2; ++c) { dnSteep_[c].clear_buffers(); dnMid_[c].clear_buffers(); } osCur_ = os; }
        const double fsOs = sr * os;

        // ---- global per-sample controls (smoothed ~20 ms) ----
        const float sc = 1.0f - static_cast<float>(std::exp(-1.0 / (0.02 * sr)));
        const float bendRange = static_cast<float>(bendRangeOf(P[BendRange]));
        const float tgt[kSm] = {
            (P[Tune] - 0.5f) * 2.0f + (P[Bend] - 0.5f) * 2.0f * bendRange,   // SmPitch (semis)
            P[ModWheel], P[Aftertouch],
            std::log2(20.0f) + std::clamp(P[Cutoff], 0.0f, 1.0f) * 9.96578428f, // SmCut (log2 Hz)
            4.6f * P[Reso],
            P[MixAOn] > 0.5f ? P[MixA] : 0.0f, P[MixBOn] > 0.5f ? P[MixB] : 0.0f,
            P[MixNoiseOn] > 0.5f ? P[MixNoise] : 0.0f,
            P[FEnvAmt] * 9.0f, P[PmEnv], P[PmOscB], P[OaPw], P[ObPw],
            volumeGainOf(P[Volume]) * 0.35f, P[KeyTrk], P[LfoAmt], P[WmMix], P[UniDetune] * 0.5f };
        if (!smoothInit_) { for (int k = 0; k < kSm; ++k) sm_[k] = tgt[k]; smoothInit_ = true; }

        // LFO
        const int lfoN = (P[LfoSaw] > 0.5f) + (P[LfoTri] > 0.5f) + (P[LfoSquare] > 0.5f);
        const bool lfoSync = P[LfoSync] > 0.5f && tSpb_ > 0.0;
        double lfoInc = lfoHzOf(P[LfoRate]) / sr;
        if (lfoSync) {
            const double beats = syncBeatsOf(P[LfoRate]);
            lfoInc = 1.0 / (beats * tSpb_);
            if (tFresh_ && tPlaying_) { const double c = tBeat_ / beats; lfoPh_ = c - std::floor(c); }
        }
        tFresh_ = false;
        const bool wmA = P[WmFreqA] > 0.5f, wmB = P[WmFreqB] > 0.5f, wmPA = P[WmPwA] > 0.5f, wmPB = P[WmPwB] > 0.5f, wmF = P[WmFilter] > 0.5f;
        const float atCut = P[AtCutoff] * 4.0f, atLfo = P[AtLfo];

        for (int i = 0; i < n; ++i) {
            for (int k = 0; k < kSm; ++k) sm_[k] += (tgt[k] - sm_[k]) * sc;
            float lfo = 0.0f;
            if (lfoN > 0) {
                const float ph = static_cast<float>(lfoPh_);
                if (P[LfoSaw] > 0.5f) lfo += 2.0f * ph - 1.0f;
                if (P[LfoTri] > 0.5f) lfo += ph < 0.5f ? 4.0f * ph - 1.0f : 3.0f - 4.0f * ph;
                if (P[LfoSquare] > 0.5f) lfo += ph < 0.5f ? 1.0f : -1.0f;
                lfo /= static_cast<float>(lfoN);
            }
            lfoPh_ += lfoInc; if (lfoPh_ >= 1.0) lfoPh_ -= std::floor(lfoPh_);
            const float wmNoise = wmRng_.bi();
            const float src = lfo + (wmNoise - lfo) * sm_[SmWmMix];
            const float amt = std::clamp(sm_[SmLfoAmt] + sm_[SmMod] + atLfo * sm_[SmAt], 0.0f, 1.0f);
            const float semis = src * amt * amt * 12.0f;
            gPitchA_[i] = sm_[SmPitch] + (wmA ? semis : 0.0f);
            gPitchB_[i] = sm_[SmPitch] + (wmB ? semis : 0.0f);
            gPwA_[i] = sm_[SmPwA] + (wmPA ? src * amt * 0.45f : 0.0f);
            gPwB_[i] = sm_[SmPwB] + (wmPB ? src * amt * 0.45f : 0.0f);
            gCut_[i] = sm_[SmCut] + (wmF ? src * amt * 4.0f : 0.0f) + atCut * sm_[SmAt];
            gK_[i] = sm_[SmK]; gMixA_[i] = sm_[SmMixA]; gMixB_[i] = sm_[SmMixB]; gMixN_[i] = sm_[SmMixN];
            gFenv_[i] = sm_[SmFenv]; gPmE_[i] = sm_[SmPmEnv]; gPmB_[i] = sm_[SmPmOscB];
            gVol_[i] = sm_[SmVol]; gKey_[i] = sm_[SmKey]; gDet_[i] = sm_[SmDet];
        }
        // Shared noise source at the oversampled rate (white, or Kellet "economy" pink).
        const bool pink = P[NoiseColor] > 0.5f;
        for (int k = 0, m = n * os; k < m; ++k) {
            const float w = noiseRng_.bi();
            if (pink) {
                pk0_ = 0.99765f * pk0_ + w * 0.0990460f;
                pk1_ = 0.96300f * pk1_ + w * 0.2965164f;
                pk2_ = 0.57000f * pk2_ + w * 1.0526913f;
                gNoise_[k] = (pk0_ + pk1_ + pk2_ + w * 0.1848f) * 0.3f;
            } else gNoise_[k] = w;
        }

        // ---- voices ----
        const int m = n * os;
        std::fill(osL_, osL_ + m, 0.0f);
        std::fill(osR_, osR_ + m, 0.0f);
        VoiceCtl c{};
        c.os = os; c.invOs = 1.0f / static_cast<float>(os); c.invFsOs = 1.0 / fsOs; c.fsOs = fsOs; c.sr = sr;
        c.drift = P[Drift];
        c.oaOff = 12.0f * octaveOf(P[OaOct]) + semiOf(P[OaSemi]) + fineCentsOf(P[OaFine]) * 0.01f;
        c.obOff = 12.0f * octaveOf(P[ObOct]) + semiOf(P[ObSemi]) + fineCentsOf(P[ObFine]) * 0.01f;
        c.sawA = P[OaSaw] > 0.5f ? 1.0f : 0.0f; c.pulA = P[OaPulse] > 0.5f ? 1.0f : 0.0f; c.sync = P[OaSync] > 0.5f;
        c.sawB = P[ObSaw] > 0.5f ? 1.0f : 0.0f; c.triB = P[ObTri] > 0.5f ? 1.0f : 0.0f; c.pulB = P[ObPulse] > 0.5f ? 1.0f : 0.0f;
        c.kbdB = P[ObKbd] > 0.5f; c.loB = P[ObLoFreq] > 0.5f;
        c.pmFreqA = P[PmFreqA] > 0.5f; c.pmPwA = P[PmPwA] > 0.5f; c.pmFilt = P[PmFilter] > 0.5f;
        c.velAmp = P[VelAmp]; c.velFilt = P[VelFilt];
        c.comp = P[LowComp] > 0.5f ? 1.0f : 0.0f;
        c.noiseFloor = P[NoiseFloor] > 0.5f;
        const bool relOn = P[ReleaseOn] > 0.5f;
        c.tFA = attackSecOf(P[FAttack]) / 1.4663f; c.tFD = decaySecOf(P[FDecay]) * 0.25f; c.tFR = relOn ? decaySecOf(P[FRelease]) * 0.25f : 0.001f;
        c.tAA = attackSecOf(P[AAttack]) / 1.4663f; c.tAD = decaySecOf(P[ADecay]) * 0.25f; c.tAR = relOn ? decaySecOf(P[ARelease]) * 0.25f : 0.001f;
        c.fS = P[FSustain]; c.aS = P[ASustain];
        const float gT = glideSecOf(P[Glide]);
        c.glideC = gT > 0.0f ? 1.0f - static_cast<float>(std::exp(-1.0 / (gT / 3.0 * sr))) : 1.0f;
        c.walkC = 1.0f - static_cast<float>(std::exp(-1.0 / (0.8 * sr)));
        c.spread = P[Spread];
        c.invFade = 1.0f / static_cast<float>(fadeLen_);
        c.cutMax = static_cast<float>(std::min(fsOs * 0.40, 30000.0));
        mixPeakChunk_ = 0.0f;

        int active = 0;
        for (int v = 0; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            if (!V.active) { V.level = 0.0f; continue; }
            // Pan: unison stacks fan out across the field, poly voices on a golden-ratio spread.
            float pos = V.stackN > 1 ? (2.0f * V.stackIdx / static_cast<float>(V.stackN - 1) - 1.0f)
                                     : (2.0f * static_cast<float>(std::fmod(0.5 + v * 0.61803398875, 1.0)) - 1.0f);
            const float ang = (std::clamp(c.spread * pos, -1.0f, 1.0f) + 1.0f) * 0.25f * static_cast<float>(kPi);
            V.panL = std::cos(ang) * 1.41421356f; V.panR = std::sin(ang) * 1.41421356f;
            renderVoice(V, n, c);
            if (V.active) ++active;
        }

        // ---- decimate the oversampled stereo sum and add into the output ----
        if (os == 1) {
            for (int i = 0; i < n; ++i) { dsL_[i] = osL_[i]; dsR_[i] = osR_[i]; }
        } else if (os == 2) {
            dnSteep_[0].process_block(dsL_, osL_, n); dnSteep_[1].process_block(dsR_, osR_, n);
        } else {
            dnMid_[0].process_block(tmpL_, osL_, n * 2); dnMid_[1].process_block(tmpR_, osR_, n * 2);
            dnSteep_[0].process_block(dsL_, tmpL_, n); dnSteep_[1].process_block(dsR_, tmpR_, n);
        }
        float pl = 0.0f, prr = 0.0f;
        for (int i = 0; i < n; ++i) {
            float l = dsL_[i], r = dsR_[i];
            if (!std::isfinite(l) || !std::isfinite(r)) { l = r = 0.0f; }
            out[i * 2] += l; out[i * 2 + 1] += r;
            pl = std::max(pl, std::fabs(l)); prr = std::max(prr, std::fabs(r));
        }
        const float dec = static_cast<float>(std::exp(-n / (0.3 * sr)));
        peakL_ = std::max(pl, peakL_ * dec); peakR_ = std::max(prr, peakR_ * dec);
        mixPeak_ = std::max(mixPeakChunk_, mixPeak_ * dec);
        activeCount_ = active;
    }

    struct VoiceCtl {
        int os; float invOs; double invFsOs, fsOs, sr;
        float drift, oaOff, obOff, sawA, pulA, sawB, triB, pulB;
        bool sync, kbdB, loB, pmFreqA, pmPwA, pmFilt, noiseFloor;
        float velAmp, velFilt, comp;
        float tFA, tFD, tFR, tAA, tAD, tAR, fS, aS;
        float glideC, walkC, spread, invFade, cutMax;
    };

    static float coefOf(float tau, double sr) { return 1.0f - static_cast<float>(std::exp(-1.0 / (std::max(tau, 1e-5f) * sr))); }

    void renderVoice(Voice& V, int n, const VoiceCtl& c) {
        using namespace pentad;
        const double sr = c.sr;
        // Per-voice envelope coefficients with the vintage time spread.
        const float ef = 1.0f + c.drift * 0.2f * V.oEnvF, ea = 1.0f + c.drift * 0.2f * V.oEnvA;
        const float fA = coefOf(c.tFA * ef, sr), fD = coefOf(c.tFD * ef, sr), fR = coefOf(c.tFR * ef, sr);
        const float aA = coefOf(c.tAA * ea, sr), aD = coefOf(c.tAD * ea, sr), aR = coefOf(c.tAR * ea, sr);
        // Unison stacks stay fat but don't scale linearly with the voice count.
        const float stackGain = V.stackN > 1 ? std::pow(static_cast<float>(V.stackN), -0.4f) : 1.0f;
        const float velA = (1.0f - c.velAmp * (1.0f - V.vel)) * stackGain;
        const float velF = 1.0f - c.velFilt * (1.0f - V.vel);
        const int os = c.os;
        const float kPiF = static_cast<float>(kPi);
        const bool audioCut = c.pmFilt && c.sawB + c.triB + c.pulB > 0.0f;
        float* L = osL_; float* R = osR_;
        float mixPk = mixPeakChunk_;

        for (int i = 0; i < n; ++i) {
            // -- stolen-voice fade → hand the voice to its pending note --
            float fadeG = 1.0f;
            if (V.fade > 0) {
                --V.fade;
                fadeG = static_cast<float>(V.fade) * c.invFade;
                if (V.fade == 0) {
                    if (V.pendNote >= 0) {
                        V.fenv = V.aenv = 0.0f;
                        const int pn = V.pendNote; V.pendNote = -1;
                        startVoice(static_cast<int>(&V - voices_), pn, V.pendVel, V.pendStack, V.pendStackN, V.pendGlide);
                    } else { V.active = false; V.gate = false; V.fst = V.ast = 0; V.fenv = V.aenv = 0.0f; V.level = 0.0f; return; }
                }
            }
            // -- glide (exponential, in semitones) --
            V.pitch += (V.pitchTgt - V.pitch) * c.glideC;
            // -- envelopes (RC curves; attack aims past 1 like the analog charge) --
            switch (V.fst) {
                case 1: V.fenv += (1.3f - V.fenv) * fA; if (V.fenv >= 1.0f) { V.fenv = 1.0f; V.fst = 2; } break;
                case 2: V.fenv += (c.fS - V.fenv) * fD; break;
                case 3: V.fenv -= V.fenv * fR; if (V.fenv < 1e-6f) { V.fenv = 0.0f; V.fst = 0; } break;
                default: break;
            }
            switch (V.ast) {
                case 1: V.aenv += (1.3f - V.aenv) * aA; if (V.aenv >= 1.0f) { V.aenv = 1.0f; V.ast = 2; } break;
                case 2: V.aenv += (c.aS - V.aenv) * aD; break;
                case 3: V.aenv -= V.aenv * aR; if (V.aenv < 3e-5f) { V.aenv = 0.0f; V.ast = 0; } break;   // ≈ -90 dB
                default: break;
            }
            if (V.ast == 0 && V.fade == 0) {   // asleep: stop computing this voice
                V.active = false; V.gate = false; V.fst = 0; V.level = 0.0f;
                mixPeakChunk_ = mixPk;
                return;
            }
            // -- slow random walk (vintage drift) --
            if (--V.wCount <= 0) {
                V.tA = V.rng.bi(); V.tB = V.rng.bi(); V.tC = V.rng.bi();
                V.wCount = static_cast<int>(sr * (0.4 + 1.6 * V.rng.uni())) + 1;
            }
            V.wA += (V.tA - V.wA) * c.walkC; V.wB += (V.tB - V.wB) * c.walkC; V.wC += (V.tC - V.wC) * c.walkC;

            // -- base-rate control targets --
            const float key = V.pitch;
            const float det = V.stackN > 1 ? gDet_[i] * (2.0f * V.stackIdx / static_cast<float>(V.stackN - 1) - 1.0f) : 0.0f;
            const float fe = V.fenv;
            const float pmE = gPmE_[i] * gPmE_[i];
            float noteA = key + det + gPitchA_[i] + c.oaOff + c.drift * (0.15f * V.oA + 0.08f * V.wA);
            if (c.pmFreqA) noteA += pmE * 60.0f * fe;
            float noteB = (c.kbdB ? key + det : 60.0f) + gPitchB_[i] + c.obOff + c.drift * (0.15f * V.oB + 0.08f * V.wB);
            if (c.loB) noteB -= 96.0f;
            const double dtA = std::min(440.0 * exp2f((noteA - 69.0f) * (1.0f / 12.0f)) * c.invFsOs, 0.45);
            const double dtB = std::min(440.0 * exp2f((noteB - 69.0f) * (1.0f / 12.0f)) * c.invFsOs, 0.45);
            float cut = gCut_[i] + gFenv_[i] * fe * velF + gKey_[i] * (key - 60.0f) * (1.0f / 12.0f)
                      + c.drift * (0.3f * V.oCut + 0.1f * V.wC);
            if (c.pmFilt) cut += gPmE_[i] * 6.0f * fe;
            const float fc = std::clamp(exp2f(cut), 8.0f, c.cutMax);
            const float g = tanPrewarp(kPiF * fc * static_cast<float>(c.invFsOs));
            const float pwA = gPwA_[i] + (c.pmPwA ? gPmE_[i] * 0.5f * fe : 0.0f) + c.drift * 0.02f * V.oPw;
            const float pwB = gPwB_[i];
            const float amp = (V.aenv * velA + 3e-5f) * fadeG * gVol_[i];
            const float pmB = gPmB_[i] * gPmB_[i];
            const float fmOct = c.pmFreqA ? pmB * 4.0f : 0.0f;
            const float pwmB = c.pmPwA ? gPmB_[i] * 0.5f : 0.0f;
            const float cutB = c.pmFilt ? pmB * 4.0f : 0.0f;
            if (V.fresh) { V.pDtA = dtA; V.pDtB = dtB; V.pG = g; V.pCut = cut; V.pPwA = pwA; V.pPwB = pwB; V.pAmp = amp; V.fresh = false; }
            const float k = gK_[i];
            const float mA = gMixA_[i], mB = gMixB_[i], mN = gMixN_[i];
            const float xin0 = 1.0f + c.comp * k;
            const float* nz = gNoise_ + i * os;
            float* lo = L + i * os; float* ro = R + i * os;

            for (int s = 0; s < os; ++s) {
                const float t = static_cast<float>(s + 1) * c.invOs;
                // --- Osc B (master) ---
                const double dB = V.pDtB + (dtB - V.pDtB) * t;
                const float duB = std::clamp(V.pPwB + (pwB - V.pPwB) * t, 0.0f, 1.0f);
                const float gzB = c.pulB * pulseZone(duB);
                const double pb0 = V.B.ph;
                const float wrapB = advance(V.B, dB, 0.0f, dB, duB, c.sawB, gzB);
                const float pb = static_cast<float>(V.B.ph);
                if (c.triB != 0.0f) {   // triangle slope corners (BLAMP)
                    const float sl = 8.0f * static_cast<float>(dB) * c.triB;
                    const float inv = static_cast<float>(1.0 / dB);
                    if (wrapB >= 0.0f) {
                        if (pb0 < 0.5) V.B.ring.blamp((0.5f + pb) * inv, -sl);
                        V.B.ring.blamp(wrapB, sl);
                        if (pb >= 0.5f) V.B.ring.blamp((pb - 0.5f) * inv, -sl);
                    } else if (pb0 < 0.5 && pb >= 0.5f) V.B.ring.blamp((pb - 0.5f) * inv, -sl);
                }
                float nb = c.sawB * (2.0f * pb - 1.0f);
                if (c.triB != 0.0f) nb += pb < 0.5f ? 4.0f * pb - 1.0f : 3.0f - 4.0f * pb;
                nb += gzB * ((V.B.hi ? 1.0f : -1.0f) - (2.0f * duB - 1.0f));
                V.B.ring.naive(nb);
                const float bOut = V.B.ring.pop();

                // --- Osc A (slave; Poly-Mod FM / PWM from B at audio rate) ---
                double dA = V.pDtA + (dtA - V.pDtA) * t;
                if (fmOct != 0.0f) dA = std::min(dA * exp2f(fmOct * bOut), 0.45);
                const float duA = std::clamp(V.pPwA + (pwA - V.pPwA) * t + pwmB * bOut, 0.0f, 1.0f);
                const float gzA = c.pulA * pulseZone(duA);
                if (c.sync && wrapB >= 0.0f) {
                    advance(V.A, dA * (1.0 - wrapB), wrapB, dA, duA, c.sawA, gzA);
                    const float pr = static_cast<float>(V.A.ph);
                    V.A.ring.blep(wrapB, -2.0f * pr * c.sawA);                   // saw snaps back to -1
                    if (!V.A.hi && duA > 0.0f) V.A.ring.blep(wrapB, 2.0f * gzA);  // pulse goes high
                    V.A.hi = duA > 0.0f;
                    V.A.ph = 0.0;
                    advance(V.A, dA * wrapB, 0.0f, dA, duA, c.sawA, gzA);
                } else {
                    advance(V.A, dA, 0.0f, dA, duA, c.sawA, gzA);
                }
                const float pa = static_cast<float>(V.A.ph);
                V.A.ring.naive(c.sawA * (2.0f * pa - 1.0f) + gzA * ((V.A.hi ? 1.0f : -1.0f) - (2.0f * duA - 1.0f)));
                const float aOut = V.A.ring.pop();

                // --- mixer (soft saturation into the filter) ---
                const float mix = mA * aOut + mB * bOut + mN * nz[s];
                mixPk = std::max(mixPk, std::fabs(mix));
                const float x = V.sat.process(mix * 0.6) * (1.0f / 0.6f);

                // --- 4-pole OTA low-pass (ZDF, per-sample linearised tanh) ---
                float gg;
                if (audioCut) {
                    const float cc = V.pCut + (cut - V.pCut) * t + cutB * bOut;
                    gg = tanPrewarp(kPiF * std::clamp(exp2f(cc), 8.0f, c.cutMax) * static_cast<float>(c.invFsOs));
                } else gg = V.pG + (g - V.pG) * t;
                float G[4], S[4];
                for (int j = 0; j < 4; ++j) {
                    const float a = gg * tanhRatio(kStageDrive * V.df[j]);
                    const float inv = 1.0f / (1.0f + a);
                    G[j] = a * inv; S[j] = V.s[j] * inv;
                }
                const float Gt = G[0] * G[1] * G[2] * G[3];
                const float St = G[3] * (G[2] * (G[1] * S[0] + S[1]) + S[2]) + S[3];
                const float xin = x * xin0;
                // The resonance loop saturates (it bounds the self-oscillation amplitude
                // without dragging its pitch off the cutoff the way stage clipping would).
                const float kf = k * tanhRatio(kLoopDrive * V.y4);
                float y3 = (Gt * xin + St) / (1.0f + kf * Gt);
                const float u = xin - kf * y3;
                const float y0 = G[0] * u + S[0], y1 = G[1] * y0 + S[1], y2 = G[2] * y1 + S[2];
                y3 = G[3] * y2 + S[3];
                V.df[0] = u - y0; V.df[1] = y0 - y1; V.df[2] = y1 - y2; V.df[3] = y2 - y3;
                V.s[0] = 2.0f * y0 - V.s[0]; V.s[1] = 2.0f * y1 - V.s[1];
                V.s[2] = 2.0f * y2 - V.s[2]; V.s[3] = 2.0f * y3 - V.s[3];
                V.y4 = y3;

                // --- VCA (gentle curvature + leakage) and noise floor ---
                float o = std::clamp(y3, -6.0f, 6.0f) * (V.pAmp + (amp - V.pAmp) * t);
                o -= 0.008f * o * o * o;
                if (c.noiseFloor) o += 1e-5f * V.rng.bi();
                lo[s] += o * V.panL; ro[s] += o * V.panR;
            }
            V.pDtA = dtA; V.pDtB = dtB; V.pG = g; V.pCut = cut; V.pPwA = pwA; V.pPwB = pwB; V.pAmp = amp;

            // Extreme settings must never poison the voice: reset a non-finite filter.
            if (!std::isfinite(V.s[0] + V.s[1] + V.s[2] + V.s[3])) {
                for (int j = 0; j < 4; ++j) { V.s[j] = 0.0f; V.df[j] = 0.0f; }
                V.y4 = 0.0f;
            }
        }
        V.level = V.aenv * velA * (V.fade > 0 ? V.fade * c.invFade : 1.0f);
        mixPeakChunk_ = mixPk;
    }

    void publishTelemetry(int32_t /*frames*/) {
        tele_[ScPoly].store(static_cast<float>(lastPoly_), std::memory_order_relaxed);
        tele_[ScActive].store(static_cast<float>(activeCount_), std::memory_order_relaxed);
        tele_[ScPeakL].store(peakL_, std::memory_order_relaxed);
        tele_[ScPeakR].store(peakR_, std::memory_order_relaxed);
        tele_[ScMixPeak].store(mixPeak_, std::memory_order_relaxed);
        tele_[ScCpu].store(static_cast<float>(cpu_), std::memory_order_relaxed);
        tele_[ScOs].store(static_cast<float>(osCur_), std::memory_order_relaxed);
        tele_[ScRate].store(static_cast<float>(sampleRate_), std::memory_order_relaxed);
        for (int v = 0; v < kMaxVoices; ++v)
            tele_[ScVoices + v].store(voices_[v].active ? voices_[v].level : 0.0f, std::memory_order_relaxed);
    }

    // ---- state ------------------------------------------------------------------
    enum Sm { SmPitch = 0, SmMod, SmAt, SmCut, SmK, SmMixA, SmMixB, SmMixN, SmFenv, SmPmEnv, SmPmOscB,
              SmPwA, SmPwB, SmVol, SmKey, SmLfoAmt, SmWmMix, SmDet, kSm };

    double sampleRate_ = 44100.0;
    std::atomic<double> pendingRate_{ 44100.0 };
    Voice voices_[kMaxVoices];
    int held_[128] = { 0 }; int heldN_ = 0;
    int rr_ = 0; uint64_t orderCtr_ = 0;
    int lastMode_ = 0, lastPoly_ = 5, fadeLen_ = 176, osCur_ = 2, activeCount_ = 0;
    uint32_t seedCache_ = 0;
    double lfoPh_ = 0.0;
    double tBeat_ = 0.0, tSpb_ = 0.0, tPos_ = 0.0, tFrames_ = 0.0; bool tPlaying_ = false, tFresh_ = true;
    pentad::Rng noiseRng_, wmRng_;
    float pk0_ = 0, pk1_ = 0, pk2_ = 0;
    float sm_[kSm] = {}; bool smoothInit_ = false;
    float gPitchA_[kChunk], gPitchB_[kChunk], gPwA_[kChunk], gPwB_[kChunk], gCut_[kChunk], gK_[kChunk];
    float gMixA_[kChunk], gMixB_[kChunk], gMixN_[kChunk], gFenv_[kChunk], gPmE_[kChunk], gPmB_[kChunk];
    float gVol_[kChunk], gKey_[kChunk], gDet_[kChunk];
    float gNoise_[kChunk * kMaxOs];
    float osL_[kChunk * kMaxOs], osR_[kChunk * kMaxOs], tmpL_[kChunk * 2], tmpR_[kChunk * 2], dsL_[kChunk], dsR_[kChunk];
    hiir::Downsampler2xFpu<12> dnSteep_[2];
    hiir::Downsampler2xFpu<5> dnMid_[2];
    float peakL_ = 0, peakR_ = 0, mixPeak_ = 0, mixPeakChunk_ = 0;
    double cpu_ = 0.0;
    mutable std::atomic<float> tele_[kScopeN] = {};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
