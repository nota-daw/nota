// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — one voice (specs/055 US1): two oscillators, noise, the multimode and ladder
// filters, Brute feedback, drive, three envelopes and the per-voice LFO.
//
//   Osc 1 ─┐ (Osc 2 hard-syncs to Osc 1, frequency-modulates Osc 1 and the multimode cutoff)
//   Osc 2 ─┼─ each source's level, and its balance between the two filter inputs
//   Noise ─┘
//      ├─→ Multimode (LP→BP→HP→Notch) ─┬───────── route: series (into the ladder) ↔ parallel
//      └─→ Ladder (12 / 24 dB) ←───────┘ ─→ Σ ─→ Brute feedback ↺ ─→ drive ─→ VCA ─→ pan
//
// Oscillators: Pentad's band-limited core (4-point polyBLEP / polyBLAMP residuals through a
// two-sample ring), so a continuous Wave can blend triangle → saw → square and hard sync stays
// clean. Metal folds the triangle share of the wave (sin folder, first-order ADAA). The voice
// runs at ×2 (×1 at 88.2 kHz and above); Twin decimates the voice sum once.
//
// Everything a voice reads arrives in a PartCtl (the part's morphed, denormalized values,
// refreshed every kCtl samples on a fixed sample grid, so a render doesn't depend on the host's
// block size); the values that would zipper ramp linearly across the kCtl samples.

#pragma once

#include "Pentad.h" // pentad:: band-limited oscillator core, Rng, hash32, Adaa
#include "dsp/math/FastMath.h"
#include "engine/NoteExpression.h"
#include "dsp/primitives/LadderFilter.h"
#include "dsp/primitives/SteinerFilter.h"

#include <cmath>
#include <cstdint>

namespace nota {
namespace twin {

constexpr int kCtl = 32;  // control interval (samples)
constexpr int kMaxOs = 2; // voice oversampling

// lo · (hi/lo)^v through the fast exp2 (≈ 0.03 cent): it runs per voice per control interval.
inline double expMap(double v, double lo, double hi) {
    return lo * fast_math::exp2f(static_cast<float>(std::clamp(v, 0.0, 1.0) * std::log2(hi / lo)));
}

// ---- musical mappings (normalized → units); the card's readouts mirror these ----------
inline int coarseSemis(float v) { return std::clamp(static_cast<int>(std::lround(v * 48.0f)), 0, 48) - 24; }
inline float fineSemis(float v) { return (v - 0.5f); }                                 // ±50 cents
inline float cutoffOct(float v) { return 4.321928f + std::clamp(v, 0.0f, 1.0f) * 9.965784f; } // log2 Hz, 20 Hz..20 kHz
inline float envAmtOct(float v) { return (v - 0.5f) * 16.0f; }                         // ±8 octaves
inline float attackSec(float v) { return static_cast<float>(expMap(v, 0.0005, 10.0)); }
inline float decaySec(float v) { return static_cast<float>(expMap(v, 0.003, 15.0)); }
inline float delaySec(float v) { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.001, 5.0)); }
inline float glideSec(float v) { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.005, 5.0)); }
inline float lfoHz(float v) { return static_cast<float>(expMap(v, 0.02, 40.0)); }
inline float fadeSec(float v) { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.01, 10.0)); }
inline float dutyOf(float v) { return 0.05f + 0.9f * std::clamp(v, 0.0f, 1.0f); }
inline float partGain(float v) { return 2.0f * v * v; } // Out Volume: 0.7 ≈ unity
inline int stepOf(float v, int n) { return std::clamp(static_cast<int>(std::lround(v * (n - 1))), 0, n - 1); }

// LFO cycle lengths in beats (quarter notes) for the synced divisions: 8 bars … 1/32.
inline double syncBeats(float v) {
    static const double b[14] = {32., 16., 8., 4., 2., 1.5, 1., 0.75, 2. / 3., 0.5, 1. / 3., 0.25, 1. / 6., 0.125};
    return b[stepOf(v, 14)];
}

// Triangle → saw → square gains for Wave 0..1, the pulse trimmed to the saw's loudness.
struct WaveGains {
    float tri, saw, pul;
};
inline WaveGains waveGains(float w) {
    w = std::clamp(w, 0.0f, 1.0f);
    return {std::max(0.0f, 1.0f - 2.0f * w), 1.0f - std::fabs(2.0f * w - 1.0f), 0.6f * std::max(0.0f, 2.0f * w - 1.0f)};
}

// The part's sounding values for one control interval.
struct PartCtl {
    // oscillators
    float semi[2] = {0, 0}, wave[2] = {0.5f, 0.5f}, duty[2] = {0.5f, 0.5f}, metal[2] = {0, 0};
    float lvl[2] = {1, 0}, bal[2] = {1, 1};
    bool sync = false, o2lo = false;
    float fm21 = 0, fmFilt = 0; // octaves per unit of Osc 2
    float noiseLvl = 0, noiseCol = 0.5f, noiseBal = 1;
    // filters
    float mmCut = 14.0f, mmRes = 0, mmMode = 0, mmKey = 0, mmEnv = 0;
    float ldCut = 11.0f, ldRes = 0, ldKey = 0.5f, ldEnv = 0;
    bool ld12 = false;
    float route = 1, brute = 0, drive = 0;
    // envelopes: seconds, sustain, velocity, curve
    float fA = 0, fD = 0.1f, fS = 0.5f, fR = 0.1f, fVel = 0, fCurve = 0.5f;
    float aA = 0, aD = 0.1f, aS = 1, aR = 0.1f, aVel = 0.5f, aCurve = 0.5f;
    float mDly = 0, mA = 0, mD = 0.1f, mS = 0, mR = 0.1f, mVel = 0;
    bool mLoop = false;
    // LFO 1 (vibrato in US1; the matrix reads all three in US4)
    float lfoHz[3] = {1, 1, 1}, lfoFade[3] = {0, 0, 0}, lfoAmt[3] = {1, 1, 1};
    int lfoShape[3] = {0, 0, 0};
    bool lfoGlobal[3] = {false, false, true}, lfoRetrig[3] = {false, false, false};
    float vibSemis = 0;
    // voice
    float drift = 0, uniDetune = 0, spread = 0;
    int spreadMode = 0;
    int voiceMode = 0; // Poly / Mono / Legato / Unison
    float glideSec = 0;
    bool glideLegato = false;
    int octave = 0;
    float gain = 1, pan = 0;
    // Expression (US3): the instrument-wide offsets every voice adds (wheel bend in semitones,
    // channel pressure, CC74 slide), their smoothing, and the pad's X / Z.
    float exAdd[kExprDims] = {0.0f, 0.0f, 0.0f};
    float exCoef = 1.0f;
    float vibExtra = 0.0f; // the matrix's Vibrato destination: semitones of LFO 1, per voice
    // Effects (US5): normalized values for the internal devices, and which ones are on.
    bool fxOn = true, modOn = false, dlyOn = false, revOn = false;
    int modType = 0; // Chorus / Flanger / Phaser / Ensemble
    float modRate = 0.3f, modDepth = 0.5f, modMix = 0.3f;
    float dlyTime = 0.4f, dlyDiv = 0.7f, dlyFb = 0.35f, dlyMix = 0.25f;
    bool dlySync = true, dlyPing = true;
    float revSize = 0.5f, revDamp = 0.5f, revMix = 0.2f;
};

// The sequencer's motion lanes for a control interval: side params set outright (on side A or
// B, before the morph blends them) and, optionally, the part's Morph and the expression pad.
struct MotionOverride {
    int n = 0;
    int side[4] = {}; // 0 = A, 1 = B
    int param[4] = {}; // SideParam
    float v[4] = {};
    float morph = -1.0f; // −1 = the knob's own
    float pad[3] = {-1.0f, -1.0f, -1.0f}; // the expression pad's X / Y / Z, −1 = the pad's own
};

// One live matrix route of a part for a control interval (TwinMod): depth morphed, −1..1.
struct LiveRoute {
    int src;
    int dst;
    float amt;
};

constexpr float kRoutePitchSt = 24.0f;  // a full-depth route to a pitch destination: ±24 st
constexpr float kRouteVibratoSt = 1.0f; // … to Vibrato: ±1 st of LFO 1

// ---- envelope ------------------------------------------------------------------------
// RC segments like an analog generator: the attack charges towards a target above 1 (the
// curve sets how far: near-linear at 0, strongly exponential at 1), decay and release fall
// exponentially. The mod envelope adds a delay and can loop attack → decay while held.
struct Env {
    enum : uint8_t { Idle, Delay, Attack, Decay, Release };
    uint8_t st = Idle;
    float lvl = 0.0f;
    int32_t dly = 0;
    float aC = 1, dC = 1, rC = 1, aTgt = 1.3f, sus = 1;
    bool loop = false;

    void gateOn(int32_t delaySamples) {
        dly = delaySamples;
        st = dly > 0 ? Delay : Attack; // retrigger from the current level, no click
    }
    void gateOff() {
        if (st != Idle) st = Release;
    }
    // Per-control-interval coefficients from times in seconds.
    void setTimes(float a, float d, float s, float r, float curve, double sr, bool loopIt) {
        const float over = 0.02f + 3.0f * (1.0f - curve) * (1.0f - curve); // attack target overshoot
        aTgt = 1.0f + over;
        const double tauA = std::max(1e-5, static_cast<double>(a)) / std::log((1.0 + over) / over);
        aC = static_cast<float>(1.0 - std::exp(-1.0 / (tauA * sr)));
        dC = static_cast<float>(1.0 - std::exp(-6.9 / (std::max(1e-4, static_cast<double>(d)) * sr)));
        rC = static_cast<float>(1.0 - std::exp(-6.9 / (std::max(1e-4, static_cast<double>(r)) * sr)));
        sus = std::clamp(s, 0.0f, 1.0f);
        loop = loopIt;
    }
    float tick() {
        switch (st) {
        case Delay:
            if (--dly <= 0) st = Attack;
            break;
        case Attack:
            lvl += (aTgt - lvl) * aC;
            if (lvl >= 1.0f) {
                lvl = 1.0f;
                st = Decay;
            }
            break;
        case Decay:
            lvl += (sus - lvl) * dC;
            if (loop && std::fabs(lvl - sus) < 1e-3f) st = Attack;
            break;
        case Release:
            lvl -= lvl * rC;
            if (lvl < 3e-5f) { // ≈ −90 dB
                lvl = 0.0f;
                st = Idle;
            }
            break;
        default: break;
        }
        return lvl;
    }
};

// ---- LFO -----------------------------------------------------------------------------
// One sine-ish cycle per unit phase; the random shapes draw a new value per cycle.
inline float fastSin(float ph) { // ph 0..1 → sin(2π ph), parabolic with one refinement (≈ 0.1 % error)
    float x = ph < 0.25f ? 4.0f * ph : (ph < 0.75f ? 2.0f - 4.0f * ph : 4.0f * ph - 4.0f); // triangle, 0 at 0, peak at 0.25
    x = x * (2.0f - std::fabs(x));                                                           // parabola through the triangle
    return x * (0.775f + 0.225f * std::fabs(x));                 // refine towards a sine
}

struct Lfo {
    double ph = 0.0;
    float val = 0.0f; // the last value step() returned
    float rPrev = 0, rNext = 0;
    pentad::Rng rng;

    // Advance by inc (cycles per sample) and return the shape's value −1..1.
    float step(double inc, int shape) {
        ph += inc;
        if (ph >= 1.0) {
            ph -= std::floor(ph);
            rPrev = rNext;
            rNext = rng.bi();
        }
        const float p = static_cast<float>(ph);
        switch (shape) {
        case 0: val = fastSin(p); break;
        case 1: val = p < 0.5f ? 4.0f * p - 1.0f : 3.0f - 4.0f * p; break;
        case 2: val = 1.0f - 2.0f * p; break;
        case 3: val = p < 0.5f ? 1.0f : -1.0f; break;
        case 4: val = rNext; break;
        default: {
            const float s = p * p * (3.0f - 2.0f * p);
            val = rPrev + (rNext - rPrev) * s;
        }
        }
        return val;
    }
};

// A linear ramp across one control interval.
struct Ramp {
    float cur = 0, inc = 0;
    void to(float target, bool snap) {
        if (snap) {
            cur = target;
            inc = 0;
        } else
            inc = (target - cur) * (1.0f / kCtl);
    }
    float next() { return cur += inc; }
};

// ---- the voice ---------------------------------------------------------------------------
struct Voice {
    // allocation
    bool active = false, gate = false, hasPitch = false, fresh = true;
    bool seq = false, pendSeq = false; // played by the sequencer (its note-offs, not the keyboard's)
    int note = 60, part = 0, stackIdx = 0, stackN = 1;
    float vel = 0.8f;
    float relVel = 0.0f; // the release velocity of its note-off (0 until released, or none sent)
    uint64_t order = 0;
    int fade = 0; // samples left of a steal fade-out
    int pendNote = -1, pendStack = 0, pendStackN = 1;
    float pendVel = 0;
    bool pendGlide = false;
    float pendExpr[kExprDims] = {0.0f, 0.0f, 0.5f};
    VoiceExpr ex; // this note's bend / pressure / slide
    // pitch
    float pitch = 60, pitchTgt = 60, glideC = 1;
    float det = 0; // unison stack detune (semitones)
    float panL = 0.7071f, panR = 0.7071f;
    // oscillators
    pentad::Osc osc[2];
    float o2Out = 0;          // Osc 2's last sample (FM into Osc 1)
    double fx1[2] = {0, 0};   // Metal folder ADAA state per oscillator
    float fy1[2] = {0, 0};
    uint32_t noiseRng = 1;
    float noiseLp = 0;
    // filters
    dsp::SteinerFilter mm;
    dsp::LadderFilter ld;
    float fb = 0; // Brute: the filter section's last output
    pentad::Adaa sat;
    // modulation
    Env fenv, aenv, menv;
    Lfo lfo[3];
    float lfoFadeLvl[3] = {1, 1, 1}, lfoFadeInc[3] = {0, 0, 0};
    // analog drift: static spread + a slow walk
    float oP[2] = {0, 0}, oCut = 0, oEnv = 0;
    float walk = 0, walkTgt = 0;
    int walkLeft = 0;
    pentad::Rng rng;
    // ramps across the control interval
    Ramp rPitch[2], rMmCut, rLdCut, rLvl[2], rNoise, rDuty[2], rWave[2], rMetal[2], rGain;
    // The matrix (US4): when a route moves anything for this voice — a per-note source on the
    // Morph included — the voice sounds its own values, re-evaluated each interval into `own`.
    float morphOff = 0.0f; // this voice's morph offset (telemetry)
    float rnd = 0.0f;      // the Random source: drawn at the note-on
    bool hasOwn = false;
    PartCtl own;
};

// Render base-rate samples [from, n) of one voice into the os-rate stereo buffers (added).
// Returns where it stopped: n, or earlier when the voice went silent or a steal fade ended
// (then, if the voice has a pending note, the caller starts it and renders on from there).
// gLfo1: LFO 1's shared values for the interval when it runs in global mode.
int renderVoice(Voice& V, const PartCtl& c, int from, int n, int os, double sr, float modWheel, int fadeLen,
    const float* gLfo1, float* osL, float* osR);

// Per-control-interval voice setup: envelope coefficients and the ramps towards this
// interval's targets.
void voiceControl(Voice& V, const PartCtl& c, double sr);

} // namespace twin
} // namespace nota
