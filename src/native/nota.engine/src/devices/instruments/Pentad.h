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
// → automation / MIDI learn / persist / clone for free. Allocation-free after
// construction; construction is cheap (no tables).
//
// pentad::exp2f/tanhRatio/tanPrewarp unify onto dsp::FastMath (bit-identical — same
// algorithms); adds the isIdle()/hasTail()/wake() sleep contract (voices are the only tail, with a
// quiet hangover covering the HIIR decimator drain). Pitch, tuning, filter and
// envelope paths are otherwise verbatim. Param order unchanged.

#pragma once

#include "devices/Instrument.h"
#include "engine/NoteExpression.h"
#include "dsp/core/ScopedNoDenormals.h"
#include "dsp/math/FastMath.h"
#include <hiir/Downsampler2xFpu.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>

namespace nota {

namespace pentad {

// ---- fast math (unified onto dsp::FastMath; bit-identical) ---------------------------
// Taylor series of 2^f on [0,1): ~1.5e-5 relative error (≈0.03 cent).
inline float exp2f(float x) {
    return fast_math::exp2f(x);
}
// tanh(v)/v (rational tanh, exact ±1 limit beyond |v| = 3) — the per-sample
// linearisation gain of an OTA stage.
inline float tanhRatio(float v) {
    return fast_math::tanhRatio(v);
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
    if (a >= 1.5) return a - 0.5625; // continues F(1.5) = 0.9375 with slope 1
    const double x2 = x * x;
    return 0.5 * x2 - x2 * x2 / 27.0;
}
struct Adaa {
    double x1 = 0.0, f1 = 0.0;
    float process(double x) {
        const double f = softClipAD(x), dx = x - x1;
        const double y = std::fabs(dx) > 1e-5 ? (f - f1) / dx : softClip(0.5 * (x + x1));
        x1 = x;
        f1 = f;
        return static_cast<float>(y);
    }
    void reset() { x1 = f1 = 0.0; }
};
// Padé tan for the bilinear prewarp, x ∈ [0, ~1.3].
inline float tanPrewarp(float x) {
    return fast_math::tanPrewarp(x);
}
inline uint32_t hash32(uint32_t x) {
    x ^= x >> 16;
    x *= 0x7feb352dU;
    x ^= x >> 15;
    x *= 0x846ca68bU;
    x ^= x >> 16;
    return x;
}
struct Rng {
    uint32_t s = 0x9E3779B9U;
    void seed(uint32_t v) { s = v ? v : 0x9E3779B9U; }
    uint32_t next() {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }
    float uni() { return static_cast<float>(next() >> 8) * (1.0f / 16777216.0f); } // [0,1)
    float bi() { return uni() * 2.0f - 1.0f; }                                     // [-1,1)
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
inline float blampD(float t) { // ∫ of the integrated B-spline, t ∈ [-2, 0]
    if (t <= -1.0f) {
        const float u = 2.0f + t, u2 = u * u;
        return u2 * u2 * u * (1.0f / 120.0f);
    }
    const float t2 = t * t, t4 = t2 * t2, t5 = t4 * t;
    return 1.0f / 120.0f + (t + 1.0f) * (1.0f / 24.0f) +
           (2.0f * t2 - 0.5f * t4 - 0.15f * t5 + 2.75f * t + 1.1f) * (1.0f / 6.0f);
}
inline void blampTaps(float d, float r[4]) {
    d = std::clamp(d, 0.0f, 0.99999f);
    r[0] = blampD(d - 2.0f);
    r[1] = blampD(d - 1.0f);
    r[2] = blampD(-d);
    r[3] = blampD(-d - 1.0f);
}

struct Ring {
    float a[4] = {0, 0, 0, 0};
    uint32_t m = 0;
    void naive(float v) { a[m & 3u] += v; }
    void blep(float d, float h) {
        if (h == 0.0f) return;
        float r[4];
        blepTaps(d, r);
        a[(m - 2u) & 3u] += h * r[0];
        a[(m - 1u) & 3u] += h * r[1];
        a[m & 3u] += h * r[2];
        a[(m + 1u) & 3u] += h * r[3];
    }
    void blamp(float d, float h) {
        if (h == 0.0f) return;
        float r[4];
        blampTaps(d, r);
        a[(m - 2u) & 3u] += h * r[0];
        a[(m - 1u) & 3u] += h * r[1];
        a[m & 3u] += h * r[2];
        a[(m + 1u) & 3u] += h * r[3];
    }
    float pop() {
        const uint32_t o = (m - 2u) & 3u;
        const float v = a[o];
        a[o] = 0.0f;
        ++m;
        return v;
    }
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
    auto sm = [](float x) {
        x = std::clamp(x, 0.0f, 1.0f);
        return x * x * (3.0f - 2.0f * x);
    };
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
        if (o.hi && p0 < duty) {
            o.ring.blep(tEnd + static_cast<float>(1.0 - duty + p1) * invDt, -2.0f * gPul);
            o.hi = false;
        }
        o.ring.blep(wrapD, -2.0f * gSaw);
        if (!o.hi) {
            o.ring.blep(wrapD, 2.0f * gPul);
            o.hi = true;
        }
        if (p1 >= duty) {
            o.ring.blep(tEnd + static_cast<float>(p1 - duty) * invDt, -2.0f * gPul);
            o.hi = false;
        }
    } else {
        const bool nh = p1 < duty;
        if (nh != o.hi) { // phase crossed the width (or the width moved across the phase)
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
        Tune = 0,
        Bend,
        ModWheel,
        Aftertouch,
        BendRange,
        Glide,
        GlideMode,
        VoiceMode,
        Polyphony,
        Alloc,
        Unison,
        UniDetune,
        ReleaseOn,
        Drift,
        Seed,
        Volume,
        Spread,
        VelAmp,
        VelFilt,
        AtCutoff,
        AtLfo,
        NoiseFloor,
        LowComp,
        Oversample,
        // --- oscillator A (24..30) ---
        OaOct,
        OaSemi,
        OaFine,
        OaSaw,
        OaPulse,
        OaPw,
        OaSync,
        // --- oscillator B (31..39) ---
        ObOct,
        ObSemi,
        ObFine,
        ObSaw,
        ObTri,
        ObPulse,
        ObPw,
        ObLoFreq,
        ObKbd,
        // --- mixer (40..46) ---
        MixA,
        MixAOn,
        MixB,
        MixBOn,
        MixNoise,
        MixNoiseOn,
        NoiseColor,
        // --- filter (47..50) ---
        Cutoff,
        Reso,
        FEnvAmt,
        KeyTrk,
        // --- filter envelope (51..54) ---
        FAttack,
        FDecay,
        FSustain,
        FRelease,
        // --- amp envelope (55..58) ---
        AAttack,
        ADecay,
        ASustain,
        ARelease,
        // --- LFO (59..64) ---
        LfoRate,
        LfoSaw,
        LfoTri,
        LfoSquare,
        LfoAmt,
        LfoSync,
        // --- wheel-mod (65..70) ---
        WmMix,
        WmFreqA,
        WmFreqB,
        WmPwA,
        WmPwB,
        WmFilter,
        // --- poly-mod (71..75) ---
        PmEnv,
        PmOscB,
        PmFreqA,
        PmPwA,
        PmFilter,
        kNumParams
    };

    static constexpr int kMaxVoices = 16;

    // Scope telemetry layout (scopeRead): see publishTelemetry().
    enum Scope {
        ScPoly = 0,
        ScActive,
        ScPeakL,
        ScPeakR,
        ScMixPeak,
        ScCpu,
        ScOs,
        ScRate,
        ScVoices,
        kScopeN = ScVoices + kMaxVoices
    };

    Pentad();

    int32_t kind() const override { return 14; }
    const char* displayName() const override { return "Nota Pentad"; }

    // Deferred to the audio thread (render) so a device-rate change can't race a render.
    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override { pendingRate_.store(sr > 0 ? sr : 44100.0, std::memory_order_relaxed); }

    int32_t activeVoiceCount() const override {
        return static_cast<int32_t>(tele_[ScActive].load(std::memory_order_relaxed));
    }
    int32_t scopeRead(float* out, int32_t maxN) const override;
    int32_t heldNotes(int32_t* out, int32_t maxN) const override;

    // ---- parameters -------------------------------------------------------
    // Display names group by the part before the last space (the automation menu nests on it).
    static const char* paramId(int i);
    static const char* paramName(int i);

    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override { return paramId(i); }
    std::string pluginParamName(int32_t i) const override { return paramName(i); }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v))
            pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        wake();
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- project state (kNumParams normalized floats, little-endian; includes the seed) ----
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- musical mappings (shared with the editor's readouts) ---------------
    static int polyphonyOf(float v) {
        static const int n[3] = {5, 10, 16};
        return n[std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2)];
    }
    static int unisonOf(float v) {
        static const int n[3] = {1, 2, 5};
        return n[std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2)];
    }
    static int bendRangeOf(float v) { return 1 + std::clamp(static_cast<int>(std::lround(v * 11.0f)), 0, 11); }
    static int octaveOf(float v) {
        return std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3) - 2;
    } // 32'..4' → -2..+1
    static int semiOf(float v) { return std::clamp(static_cast<int>(std::lround((v - 0.5f) * 24.0f)), -12, 12); }
    static float fineCentsOf(float v) { return (v - 0.5f) * 100.0f; }
    static float cutoffHzOf(float v) { return 20.0f * std::pow(1000.0f, std::clamp(v, 0.0f, 1.0f)); }
    static float attackSecOf(float v) { return static_cast<float>(expMap(v, 0.0005, 10.0)); }
    static float decaySecOf(float v) { return static_cast<float>(expMap(v, 0.002, 15.0)); }
    static float glideSecOf(float v) { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.005, 10.0)); }
    static float lfoHzOf(float v) { return static_cast<float>(expMap(v, 0.05, 20.0)); }
    static constexpr int kSyncDivs = 14;
    static float syncBeatsOf(float v);
    static float volumeGainOf(float v) { return 2.0f * v * v; }

    // ---- transport (play edge → reseed for determinism; LFO tempo sync) -----
    // A play edge re-seeds when nothing sounds (a live-held key survives pressing Play); a
    // jump of the rolling timeline (seek, loop wrap, a freeze bounce restarting at 0 without
    // a stopped block in between) always does — the old voices belong to another position.
    // Positions compare in samples, so tempo changes never read as a jump.
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;

    // ---- note events ----------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override {
        resetAll();
        wake();
    }

    // Sleep contract: voices are the only tail (amp release, stolen-voice
    // fades; no delay line). Idle only when no voice is sounding and the
    // rendered block is silent — the HIIR decimator drain is covered by the
    // quiet hangover below.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // MPE: bend → pitch; pressure → cutoff (by the patch's Aftertouch Cutoff, two octaves when
    // that is off — it is poly aftertouch here) + level; slide → cutoff (±2 octaves). A keyboard's
    // wheel rides Bend Range and its channel pressure adds to the Aftertouch control.
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override;

    // ---- render ---------------------------------------------------------------
    void render(float* out, int32_t frames) override;

  private:
    static constexpr int kChunk = 64;
    static constexpr int kMaxOs = 4;
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr float kStageDrive = 0.35f; // OTA stage saturation (colour)
    static constexpr float kLoopDrive = 2.0f;   // resonance-loop saturation (self-osc level)

    struct Voice {
        bool active = false, gate = false;
        int note = 60, stackIdx = 0, stackN = 1;
        float vel = 0.8f;
        uint64_t order = 0;
        // stolen-voice handoff (oldest-steal fade)
        int pendNote = -1, pendStack = 0, pendStackN = 1, fade = 0;
        float pendVel = 0.0f;
        bool pendGlide = false;
        float pendExpr[kExprDims] = {0.0f, 0.0f, 0.5f};
        VoiceExpr ex; // MPE bend / pressure / slide (the instrument-wide part rides the sm_ controls)
        // pitch (semitones, glided)
        float pitch = 60.0f, pitchTgt = 60.0f;
        bool hasPitch = false;
        // envelopes: stage 0 idle, 1 attack, 2 decay/sustain, 3 release
        float fenv = 0.0f, aenv = 0.0f;
        int fst = 0, ast = 0;
        pentad::Osc A, B;
        float s[4] = {0, 0, 0, 0}, df[4] = {0, 0, 0, 0}; // filter integrator states / stage diffs
        float y4 = 0.0f;                                 // last filter output (loop saturation)
        pentad::Adaa sat;                                // mixer overdrive
        // vintage: static spread (from the seed) + slow random walk
        float oA = 0, oB = 0, oCut = 0, oEnvF = 0, oEnvA = 0, oPw = 0;
        float wA = 0, wB = 0, wC = 0, tA = 0, tB = 0, tC = 0;
        int wCount = 0;
        pentad::Rng rng;
        // previous base-rate controls, interpolated across the oversampled sub-steps
        double pDtA = 0, pDtB = 0;
        float pG = 0, pCut = 0, pPwA = 0.5f, pPwB = 0.5f, pAmp = 0;
        bool fresh = true;
        float panL = 1.0f, panR = 1.0f;
        float level = 0.0f; // telemetry
    };

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    int voiceMode() const { return std::clamp(static_cast<int>(std::lround(get(VoiceMode) * 2.0f)), 0, 2); }
    uint32_t seedValue() const {
        return static_cast<uint32_t>(std::lround(std::clamp(get(Seed), 0.0f, 1.0f) * 16777215.0f));
    }

    // ---- held keys --------------------------------------------------------------
    void pushHeld(int p) {
        for (int i = 0; i < heldN_; ++i)
            if (held_[i] == p) return;
        if (heldN_ < 128) held_[heldN_++] = p;
    }
    void removeHeld(int p) {
        int w = 0;
        for (int r = 0; r < heldN_; ++r)
            if (held_[r] != p) held_[w++] = held_[r];
        heldN_ = w;
    }
    int lowestHeld() const {
        int b = heldN_ > 0 ? held_[0] : 60;
        for (int i = 1; i < heldN_; ++i) b = std::min(b, held_[i]);
        return b;
    }

    // ---- voice allocation -----------------------------------------------------
    int allocate(int N, bool& fade);

    void startVoice(int v, int note, float vel, int s, int S, bool glide);
    // A sleeping voice wakes: free-running oscillators land at arbitrary phases; the
    // filter/BLEP state it slept with is stale and cleared.
    void wake(Voice& V);
    static void release(Voice& V) {
        V.gate = false;
        if (V.fst) V.fst = 3;
        if (V.ast) V.ast = 3;
    }

    // ---- reset / seeding --------------------------------------------------------
    void computeStatics(int v);
    void resetAll();
    void initDecimators();

    // Voice mode / polyphony / seed changes, applied before any note event or render
    // sees them (audio thread): a mode switch releases everything, fewer voices fade
    // the surplus out, a new seed re-spreads the per-voice vintage offsets.
    void syncStructure();

    // ---- one chunk (≤ kChunk frames) ---------------------------------------------
    void renderChunk(float* out, int n);

    struct VoiceCtl {
        int os;
        float invOs;
        double invFsOs, fsOs, sr;
        float drift, oaOff, obOff, sawA, pulA, sawB, triB, pulB;
        bool sync, kbdB, loB, pmFreqA, pmPwA, pmFilt, noiseFloor;
        float velAmp, velFilt, comp;
        float tFA, tFD, tFR, tAA, tAD, tAR, fS, aS;
        float glideC, walkC, spread, invFade, cutMax;
    };

    static float coefOf(float tau, double sr) {
        return 1.0f - static_cast<float>(std::exp(-1.0 / (std::max(tau, 1e-5f) * sr)));
    }

    void renderVoice(Voice& V, int n, const VoiceCtl& c);

    void publishTelemetry(int32_t /*frames*/);

    // ---- state ------------------------------------------------------------------
    enum Sm {
        SmPitch = 0,
        SmMod,
        SmAt,
        SmCut,
        SmK,
        SmMixA,
        SmMixB,
        SmMixN,
        SmFenv,
        SmPmEnv,
        SmPmOscB,
        SmPwA,
        SmPwB,
        SmVol,
        SmKey,
        SmLfoAmt,
        SmWmMix,
        SmDet,
        kSm
    };

    double sampleRate_ = 44100.0;
    std::atomic<double> pendingRate_{44100.0};
    Voice voices_[kMaxVoices];
    int held_[128] = {0};
    int heldN_ = 0;
    int rr_ = 0;
    uint64_t orderCtr_ = 0;
    int lastMode_ = 0, lastPoly_ = 5, fadeLen_ = 176, osCur_ = 2, activeCount_ = 0;
    uint32_t seedCache_ = 0;
    double lfoPh_ = 0.0;
    double tBeat_ = 0.0, tSpb_ = 0.0, tPos_ = 0.0, tFrames_ = 0.0;
    bool tPlaying_ = false, tFresh_ = true;
    pentad::Rng noiseRng_, wmRng_;
    float pk0_ = 0, pk1_ = 0, pk2_ = 0;
    float sm_[kSm] = {};
    bool smoothInit_ = false;
    static constexpr float kNoExprAdd[kExprDims] = {0.0f, 0.0f, 0.0f};
    GlobalExpr gexpr_; // pitch wheel / channel pressure / CC74 (pitch −1)
    float exprCoef_ = exprSmoothCoef(44100.0), exprAtCut_ = 2.0f;
    float gPitchA_[kChunk], gPitchB_[kChunk], gPwA_[kChunk], gPwB_[kChunk], gCut_[kChunk], gK_[kChunk];
    float gMixA_[kChunk], gMixB_[kChunk], gMixN_[kChunk], gFenv_[kChunk], gPmE_[kChunk], gPmB_[kChunk];
    float gVol_[kChunk], gKey_[kChunk], gDet_[kChunk];
    float gNoise_[kChunk * kMaxOs];
    float osL_[kChunk * kMaxOs], osR_[kChunk * kMaxOs], tmpL_[kChunk * 2], tmpR_[kChunk * 2], dsL_[kChunk],
        dsR_[kChunk];
    hiir::Downsampler2xFpu<12> dnSteep_[2];
    hiir::Downsampler2xFpu<5> dnMid_[2];
    float peakL_ = 0, peakR_ = 0, mixPeak_ = 0, mixPeakChunk_ = 0;
    // Auto-sleep hangover (see render()): idle only after the tail has settled.
    static constexpr uint64_t kIdleHangover = 16384; // frames of silence before sleep
    uint64_t quietSamples_ = 0;
    float chunkOutPeak_ = 0.0f; // our peak contribution across this render's chunks
    bool idle_ = false;
    double cpu_ = 0.0;
    mutable std::atomic<float> tele_[kScopeN] = {};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
