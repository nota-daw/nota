// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Consort — the built-in paraphonic semi-modular synth (kind 15), in the spirit of
// a four-oscillator paraphonic Moog (Matriarch-style architecture). Nota's own DSP.
//
//   VCO 1 ─┐ (2 syncs to 1)                        ┌─ ladder A ─┐ (HP/LP series, LP/LP or
//   VCO 2 ─┤                                        │            │  HP/LP parallel stereo,
//   VCO 3 ─┼─→ Mixer (+ noise, EXT, drive) ─────────┤            ├─→ VCA ─→ Σ ─→ BBD delay ─→ out
//   VCO 4 ─┘ (4 syncs to 3)                         └─ ladder B ─┘   (B = A + spacing)
//          Env 1 → cutoff · Env 2 → VCA · LFO (6 shapes) → pitch / cutoff / PWM
//
// Voicing. MONO (all four oscillators on one note), DUO (1+2 and 3+4 each take a note) and
// PARA (every oscillator its own note) share ONE filter / VCA / envelope pair — true
// paraphony, with Multi-trig (retrigger the shared envelopes on every new note) and Unison
// (idle oscillators double held notes, detuned). "True poly" swaps that for 16 complete
// voices (four oscillators, two ladders, two envelopes each). Glide per oscillator: linear
// constant-rate, linear constant-time or exponential, optionally gated (legato only).
//
// Patch bay. Twelve cables, each a (source jack, destination jack, bipolar depth) triple of
// plugin-params — so a patch saves, clones, lives in presets and the depth automates.
// Sources are audio or CV (oscillators, filter out, noise, LFO, envelopes, keyboard, the
// sequencer's pitch / gate / clock, two attenuators and a summing node); destinations are
// pitch / PWM / cutoff / resonance / VCA CV / delay / LFO rate, the attenuator and sum
// inputs, and four NORMALLED inputs — Gate 1 / Gate 2 (the envelopes' keyboard gates), Filter
// in (the mixer), VCA in (the filter) and EXT in (the VCA output, the classic Moog feedback
// loop) — where inserting a cable breaks the normal. Every cable reads its source from the
// previous sample, so feedback loops are legal. Depth is squared (fine near zero, exact
// 1 V/oct at ±100 for keyboard / sequencer pitch into pitch or cutoff).
//
// Sequencer / arpeggiator: 16 steps (note / ratchet / tie / rest, ±24 st per step), 1/4..1/32
// with swing, forward / backward / random order, latch; transposed from the keyboard (SEQ) or
// walking the held chord over 1..3 octaves (ARP, the step types still shape the rhythm).
// Locked to the host grid while the transport rolls, free-running at the host tempo when not.
//
// Analog delay: a stereo bucket-brigade model — clock-dependent bandwidth (longer = darker),
// a 2:1 compander with its breathing, soft overload and hiss, a slewed clock so moving the
// time transposes the tail; Ping-pong or stereo, or a clean digital line.
//
// DSP quality: voices run at ×1/×2/×4 with HIIR decimation, oscillators use 4-point polyBLEP /
// polyBLAMP (Pentad's core), the ladders are zero-delay-feedback with per-sample linearised
// saturation (self-oscillating, key-trackable, HP taps by mode mixing). Deterministic state
// (seeded, re-seeded on play edges / jumps) so a freeze equals the live render.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable ids).
// Allocation-free after construction (the delay line is calloc'd, so an
// untouched instance — e.g. the engine's default-value probe — costs no page faults).
//
// The denormal guard and fast math ride the unified wrappers
// (both bit-identical); adds the isIdle()/hasTail()/wake() sleep contract
// (shared envelopes + the BBD/digital delay, whose flight time blocks sleep so
// an in-flight echo is never cut; a running sequencer blocks sleep too).
// Pitch, tuning, filter and envelope paths are otherwise verbatim.

#pragma once

#include "devices/Instrument.h"
#include "Pentad.h"                     // pentad:: band-limited oscillator core + fast math, hiir decimators
#include "dsp/core/ScopedNoDenormals.h" // unified denormal guard (replaces the local FtzGuard)

#include <algorithm>
#include <atomic>
#include <chrono>
#include <climits>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

#if defined(__SSE2__) || defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#include <xmmintrin.h>
#define NOTA_CONSORT_FTZ_X86 1
#elif defined(__aarch64__) && !defined(_MSC_VER)
#define NOTA_CONSORT_FTZ_ARM 1
#endif

namespace nota {

namespace consort {

// Patch-bay jacks. The indices are persisted (a cable's source/dest param stores
// index / kJackScale) — APPEND ONLY. The editor mirrors these tables.
enum Src : int {
    SrcNone = 0,
    SrcLfo,
    SrcEnv1,
    SrcEnv2,
    SrcOsc1,
    SrcOsc2,
    SrcOsc3,
    SrcOsc4,
    SrcNoise,
    SrcFilt,
    SrcSeqPitch,
    SrcSeqGate,
    SrcClock,
    SrcKbdPitch,
    SrcKbdGate,
    SrcVelocity,
    SrcAftertouch,
    SrcModWheel,
    SrcAtten1,
    SrcAtten2,
    SrcSum,
    kSrcN
};
enum Dst : int {
    DstNone = 0,
    DstLfoRate,
    DstGate1,
    DstGate2,
    DstPitchAll,
    DstPitch1,
    DstPitch2,
    DstPitch3,
    DstPitch4,
    DstPwm,
    DstCut1,
    DstCut2,
    DstReso,
    DstFiltIn,
    DstVcaCv,
    DstVcaIn,
    DstDlyTime,
    DstDlyFb,
    DstDlyMix,
    DstExtIn,
    DstAtten1,
    DstAtten2,
    DstSum,
    kDstN
};
constexpr int kJackScale = 63; // room for future jacks without re-encoding saved patches

inline bool dstIsGlobal(int d) {
    return d == DstLfoRate || d == DstDlyTime || d == DstDlyFb || d == DstDlyMix;
}
// Destination units per unit of source at depth ±100 %.
inline float dstScale(int d) {
    switch (d) {
    case DstLfoRate: return 4.0f; // octaves of rate
    case DstPitchAll:
    case DstPitch1:
    case DstPitch2:
    case DstPitch3:
    case DstPitch4: return 60.0f; // semitones
    case DstPwm: return 0.45f;    // duty
    case DstCut1:
    case DstCut2: return 5.0f;    // octaves (1 V/oct from kbd pitch)
    case DstDlyTime: return 2.0f; // octaves of time
    default: return 1.0f;
    }
}

inline float triVal(double ph) {
    return ph < 0.5 ? static_cast<float>(4.0 * ph - 1.0) : static_cast<float>(3.0 - 4.0 * ph);
}

// Triangle slope corners (at phase 0.5 and at the wrap) as BLAMP residuals. p0 = phase before
// the span, the oscillator's phase is after it; the span ends tEnd samples before the current one.
inline void triCorners(pentad::Osc& o, double p0, float wrapD, float tEnd, double dt) {
    const float sl = static_cast<float>(8.0 * dt);
    const float inv = static_cast<float>(1.0 / dt);
    const float p1 = static_cast<float>(o.ph);
    if (wrapD >= 0.0f) {
        if (p0 < 0.5) o.ring.blamp(tEnd + (0.5f + p1) * inv, -sl);
        o.ring.blamp(wrapD, sl);
        if (p1 >= 0.5f) o.ring.blamp(tEnd + (p1 - 0.5f) * inv, -sl);
    } else if (p0 < 0.5 && p1 >= 0.5f) {
        o.ring.blamp(tEnd + (p1 - 0.5f) * inv, -sl);
    }
}

// One band-limited oscillator sample. wave: 0 triangle, 1 saw, 2 square, 3 pulse (duty).
// syncD ≥ 0: the master wrapped syncD samples ago → hard reset there. Returns the output
// (two samples late, the 4-point residual ring) and this oscillator's wrap distance.
inline float oscStep(pentad::Osc& o, double dt, int wave, float duty, float syncD, float& wrapOut) {
    const float gSaw = wave == 1 ? 1.0f : 0.0f;
    const float du = wave == 2 ? 0.5f : duty;
    const float gPul = wave >= 2 ? pentad::pulseZone(du) : 0.0f;
    const bool tri = wave == 0;
    wrapOut = -1.0f;
    if (syncD >= 0.0f) {
        const double p0 = o.ph;
        const float w1 = pentad::advance(o, dt * (1.0 - syncD), syncD, dt, du, gSaw, gPul);
        if (tri) triCorners(o, p0, w1, syncD, dt);
        const double pr = o.ph;
        o.ring.blep(syncD, -2.0f * static_cast<float>(pr) * gSaw); // saw snaps back to -1
        if (!o.hi && du > 0.0f) o.ring.blep(syncD, 2.0f * gPul);   // pulse goes high
        if (tri) {
            o.ring.blep(syncD, -1.0f - triVal(pr));                           // triangle restarts at -1
            if (pr >= 0.5) o.ring.blamp(syncD, static_cast<float>(8.0 * dt)); // falling → rising slope
        }
        o.hi = du > 0.0f;
        o.ph = 0.0;
        const float w2 = pentad::advance(o, dt * syncD, 0.0f, dt, du, gSaw, gPul);
        if (tri) triCorners(o, 0.0, w2, 0.0f, dt);
    } else {
        const double p0 = o.ph;
        wrapOut = pentad::advance(o, dt, 0.0f, dt, du, gSaw, gPul);
        if (tri) triCorners(o, p0, wrapOut, 0.0f, dt);
    }
    const float p = static_cast<float>(o.ph);
    float nv = gSaw * (2.0f * p - 1.0f) + gPul * ((o.hi ? 1.0f : -1.0f) - (2.0f * du - 1.0f));
    if (tri) nv += triVal(o.ph);
    o.ring.naive(nv);
    return o.ring.pop();
}

// Transistor-ladder core: four ZDF one-poles with a saturating resonance loop (Pentad's
// linearised-tanh solver). Low-pass is the 4th stage; 4-pole high-pass by mode mixing
// (u − 4y1 + 6y2 − 4y3 + y4), with the same resonant loop.
struct Ladder {
    float s[4] = {0, 0, 0, 0}, df[4] = {0, 0, 0, 0}, y4 = 0.0f;
    void clear() {
        for (int j = 0; j < 4; ++j) {
            s[j] = 0.0f;
            df[j] = 0.0f;
        }
        y4 = 0.0f;
    }
    float tick(float x, float g, float k, float comp, bool hp) {
        constexpr float kStageDrive = 0.3f, kLoopDrive = 2.2f;
        float G[4], S[4];
        for (int j = 0; j < 4; ++j) {
            const float a = g * pentad::tanhRatio(kStageDrive * df[j]);
            const float inv = 1.0f / (1.0f + a);
            G[j] = a * inv;
            S[j] = s[j] * inv;
        }
        const float Gt = G[0] * G[1] * G[2] * G[3];
        const float St = G[3] * (G[2] * (G[1] * S[0] + S[1]) + S[2]) + S[3];
        const float xin = x * (1.0f + comp * k);
        const float kf = k * pentad::tanhRatio(kLoopDrive * y4);
        float y3 = (Gt * xin + St) / (1.0f + kf * Gt);
        const float u = xin - kf * y3;
        const float y0 = G[0] * u + S[0], y1 = G[1] * y0 + S[1], y2 = G[2] * y1 + S[2];
        y3 = G[3] * y2 + S[3];
        df[0] = u - y0;
        df[1] = y0 - y1;
        df[2] = y1 - y2;
        df[3] = y2 - y3;
        s[0] = 2.0f * y0 - s[0];
        s[1] = 2.0f * y1 - s[1];
        s[2] = 2.0f * y2 - s[2];
        s[3] = 2.0f * y3 - s[3];
        y4 = y3;
        if (!std::isfinite(s[0] + s[1] + s[2] + s[3])) {
            clear();
            return 0.0f;
        }
        return hp ? (u - 4.0f * y0 + 6.0f * y1 - 4.0f * y2 + y3) : y3;
    }
};

// 2-pole TPT state-variable low-pass (the BBD's anti-alias / reconstruction filters).
struct SvfLp {
    float ic1 = 0.0f, ic2 = 0.0f;
    float tick(float x, float g, float k) {
        const float a1 = 1.0f / (1.0f + g * (g + k)), a2 = g * a1, a3 = g * a2;
        const float v3 = x - ic2;
        const float v1 = a1 * ic1 + a2 * v3;
        const float v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = 2.0f * v1 - ic1;
        ic2 = 2.0f * v2 - ic2;
        return v2;
    }
    void clear() { ic1 = ic2 = 0.0f; }
};

} // namespace consort

class Consort final : public Instrument {
  public:
    static constexpr int kSteps = 16, kCables = 12, kMaxVoices = 16, kOsc = 4;

    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        // --- controllers / voice / output (0..16) ---
        Tune = 0,
        Bend,
        ModWheel,
        Aftertouch,
        BendRange,
        Volume,
        Oversample,
        VoiceMode,
        TruePoly,
        MultiTrig,
        Unison,
        Drift,
        Glide,
        GlideType,
        GlideGated,
        VelVca,
        AtCut,
        // --- oscillators (17..30) ---
        O1Oct,
        O1Wave,
        O2Oct,
        O2Freq,
        O2Wave,
        O2Sync,
        O3Oct,
        O3Freq,
        O3Wave,
        O4Oct,
        O4Freq,
        O4Wave,
        O4Sync,
        Pw,
        // --- mixer (31..38) ---
        Mix1,
        Mix2,
        Mix3,
        Mix4,
        MixNoise,
        MixExt,
        NoiseColor,
        MixDrive,
        // --- dual ladder (39..45) ---
        FiltMode,
        Cutoff,
        Reso,
        Spacing,
        FEnvAmt,
        KbdTrk,
        BassComp,
        // --- envelopes (46..53) ---
        FAttack,
        FDecay,
        FSustain,
        FRelease,
        AAttack,
        ADecay,
        ASustain,
        ARelease,
        // --- LFO (54..60) ---
        LfoWave,
        LfoRate,
        LfoSync,
        LfoPitch,
        LfoCut,
        LfoPwm,
        LfoDest,
        // --- BBD delay (61..66) ---
        DlyTime,
        DlySpacing,
        DlyFb,
        DlyMix,
        DlyPing,
        DlyDigital,
        // --- sequencer / arpeggiator (67..74) ---
        SeqMode,
        SeqRate,
        SeqSwing,
        SeqOrder,
        SeqLatch,
        SeqLength,
        SeqRatchet,
        ArpOct,
        // --- 16 step types (75..90), 16 step pitches (91..106) ---
        StepType0,
        StepPitch0 = StepType0 + kSteps,
        // --- patch utilities (107..108) ---
        Atten1 = StepPitch0 + kSteps,
        Atten2,
        // --- 12 cables × (source, destination, depth) (109..144) ---
        Cable0,
        kNumParams = Cable0 + kCables * 3
    };

    // Scope telemetry layout (scopeRead): see publishTelemetry().
    enum Scope {
        ScMode = 0,
        ScActive,
        ScPeakL,
        ScPeakR,
        ScCpu,
        ScOs,
        ScRate,
        ScStep,
        ScBpm,
        ScLfo,
        ScEnv1,
        ScEnv2,
        ScOscNote,
        ScOscLvl = ScOscNote + kOsc,
        ScCutA = ScOscLvl + kOsc,
        ScCutB,
        ScDlyL,
        ScDlyR,
        ScSeqNote,
        kScopeN
    };

    Consort();

    int32_t kind() const override { return 15; }
    const char* displayName() const override { return "Nota Consort"; }

    // Deferred to the audio thread (render) so a device-rate change can't race a render.
    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override { pendingRate_.store(sr > 0 ? sr : 44100.0, std::memory_order_relaxed); }

    int32_t activeVoiceCount() const override {
        return static_cast<int32_t>(tele_[ScActive].load(std::memory_order_relaxed));
    }
    int32_t scopeRead(float* out, int32_t maxN) const override;
    int32_t heldNotes(int32_t* out, int32_t maxN) const override;

    // ---- parameters -----------------------------------------------------------
    // Display names group by the part before the last space (the automation menu nests on it).
    static const std::vector<std::string>& idTable();
    static const std::vector<std::string>& nameTable();
    static std::string paramId(int i) {
        return (i >= 0 && i < kNumParams) ? idTable()[static_cast<size_t>(i)] : std::string();
    }

    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override { return paramId(i); }
    std::string pluginParamName(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? nameTable()[static_cast<size_t>(i)] : std::string();
    }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v))
            pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        wake();
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- project state (kNumParams normalized floats, little-endian) ------------
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- musical mappings (mirrored by the editor's readouts) ------------------
    static int octaveOf(float v) { return std::clamp(static_cast<int>(std::lround(v * 4.0f)), 0, 4) - 2; } // 32'..2'
    static float freqSemisOf(float v) { return (v - 0.5f) * 14.0f; }                                       // ±7 st
    static int waveOf(float v) { return std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3); }
    static float dutyOf(float v) { return 0.5f - 0.45f * std::clamp(v, 0.0f, 1.0f); }
    static float cutoffHzOf(float v) { return 20.0f * std::pow(1000.0f, std::clamp(v, 0.0f, 1.0f)); }
    static float spacingOctOf(float v) { return (v - 0.5f) * 6.0f; }
    static float envAmtOctOf(float v) { return (v - 0.5f) * 2.0f * 7.0f; }
    static float attackSecOf(float v) { return static_cast<float>(expMap(v, 0.001, 10.0)); }
    static float decaySecOf(float v) { return static_cast<float>(expMap(v, 0.003, 15.0)); }
    static float glideSecOf(float v) { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.005, 5.0)); }
    static float lfoHzOf(float v) { return static_cast<float>(expMap(v, 0.05, 30.0)); }
    static float lfoCentsOf(float v) {
        const float b = (v - 0.5f) * 2.0f;
        return b * std::fabs(b) * 1200.0f;
    }
    static float dlySecOf(float v) { return static_cast<float>(expMap(v, 0.02, 1.5)); }
    static float dlySpacingOf(float v) { return (v - 0.5f) * 2.0f * 0.5f; }
    static int bendRangeOf(float v) { return 1 + std::clamp(static_cast<int>(std::lround(v * 11.0f)), 0, 11); }
    static float volumeGainOf(float v) { return 2.0f * v * v; }
    static int seqLengthOf(float v) {
        return 1 + std::clamp(static_cast<int>(std::lround(v * (kSteps - 1))), 0, kSteps - 1);
    }
    static int ratchetOf(float v) { return 2 + std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2); }
    static int arpOctOf(float v) { return 1 + std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2); }
    static float swingOf(float v) { return 0.5f + 0.25f * std::clamp(v, 0.0f, 1.0f); }
    static int stepTypeOf(float v) {
        return std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3);
    } // note/ratchet/tie/rest
    static int stepPitchOf(float v) { return std::clamp(static_cast<int>(std::lround((v - 0.5f) * 48.0f)), -24, 24); }
    static int jackOf(float v) {
        return std::clamp(static_cast<int>(std::lround(v * consort::kJackScale)), 0, consort::kJackScale);
    }
    static float depthOf(float v) { return (v - 0.5f) * 2.0f; }
    static constexpr int kSeqRates = 6;
    static double seqRateBeats(float v);
    static constexpr int kSyncDivs = 14;
    static double syncBeatsOf(float v);

    // ---- transport (play edge → reseed for determinism; LFO / sequencer sync) ----
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;

    // ---- note events ----------------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    // Stop / loop wrap: silence the voices and the sequencer; the delay tail keeps ringing.
    void allNotesOff() override {
        resetVoices();
        wake();
    }

    // Sleep contract: shared envelopes make this paraphonic core a single tail,
    // plus the BBD/digital delay line. Idle only when no voice is sounding and
    // the rendered block (voices + delay tap) is silent, after the delay's
    // flight time has also passed — so an in-flight echo is never cut.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // ---- render ---------------------------------------------------------------------
    void render(float* out, int32_t frames) override;

  private:
    static constexpr int kChunk = 64;
    static constexpr int kMaxOs = 4;
    static constexpr int kDlyLen = 1 << 17; // per channel, at a line rate ≤ 50 kHz
    static constexpr double kPi = 3.14159265358979323846;

    struct Voice {
        bool active = false, gate = false, retrig = false;
        int note = 60;
        float vel = 0.8f;
        uint64_t order = 0;
        // stolen-voice handoff (poly)
        int pendNote = -1, fade = 0;
        float pendVel = 0.0f;
        bool pendGlide = false;
        // per-oscillator pitch (semitones), glide, slot gain (paraphonic fades)
        float pitch[kOsc] = {60, 60, 60, 60}, tgt[kOsc] = {60, 60, 60, 60}, rate[kOsc] = {0, 0, 0, 0};
        bool hasPitch[kOsc] = {false, false, false, false};
        float g[kOsc] = {0, 0, 0, 0}, gT[kOsc] = {0, 0, 0, 0};
        float det[kOsc] = {0, 0, 0, 0};
        // envelopes: stage 0 idle, 1 attack, 2 decay/sustain, 3 release
        float fenv = 0.0f, aenv = 0.0f;
        int fst = 0, ast = 0;
        bool g1s = false, g2s = false; // patched gate-in state (edge detection)
        pentad::Osc osc[kOsc];
        consort::Ladder fa, fb;
        pentad::Adaa sat;
        // patch: previous-sample sources and utility nodes, gate inputs
        float oPrev[kOsc] = {0, 0, 0, 0}, fPrev = 0.0f, vPrev = 0.0f;
        float uIn[3] = {0, 0, 0}; // atten 1, atten 2, sum (inputs, previous sample)
        float gIn1 = 0.0f, gIn2 = 0.0f;
        // vintage: static per-osc spread + slow random walk
        float oOff[kOsc] = {0, 0, 0, 0}, oCut = 0.0f;
        float w[kOsc + 1] = {0, 0, 0, 0, 0}, wt[kOsc + 1] = {0, 0, 0, 0, 0};
        int wCount = 0;
        pentad::Rng rng;
        // previous base-rate controls, interpolated across the oversampled sub-steps
        float pP[kOsc] = {0, 0, 0, 0}, pCa = 0, pCb = 0, pAmp = 0, pDuty = 0.5f;
        bool fresh = true;
        float cutA = 0.0f, cutB = 0.0f; // telemetry (log2 Hz)
        float level = 0.0f;
    };

    struct FreeDel {
        void operator()(float* p) const { std::free(p); }
    };

    void set(int p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(int p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    static float coefOf(float tau, double sr) {
        return 1.0f - static_cast<float>(std::exp(-1.0 / (std::max(tau, 1e-5f) * sr)));
    }
    // 0..2 = Mono / Duo / Para, 3 = true poly.
    int structMode() const {
        return get(TruePoly) > 0.5f ? 3 : std::clamp(static_cast<int>(std::lround(get(VoiceMode) * 2.0f)), 0, 2);
    }
    int seqMode() const { return std::clamp(static_cast<int>(std::lround(get(SeqMode) * 2.0f)), 0, 2); }
    static int slotsOf(int mode) { return mode == 0 ? 1 : mode == 1 ? 2 : 4; }
    static int slotOfOsc(int k, int S) { return S == 1 ? 0 : S == 2 ? k / 2 : k; }

    // ---- key lists ------------------------------------------------------------------
    static void listPush(int* a, int& n, int p) {
        for (int i = 0; i < n; ++i)
            if (a[i] == p) {
                for (int j = i; j < n - 1; ++j) a[j] = a[j + 1];
                --n;
                break;
            }
        if (n < 128) a[n++] = p;
    }
    static bool listRemove(int* a, int& n, int p) {
        int w = 0;
        bool f = false;
        for (int r = 0; r < n; ++r) {
            if (a[r] == p) f = true;
            else
                a[w++] = a[r];
        }
        n = w;
        return f;
    }
    void pushKey(int p) { listPush(keys_, keyN_, p); }
    void removeKey(int p) { listRemove(keys_, keyN_, p); }

    // ===================================================================================
    // Voice layer: playOn / playOff (from the keyboard, or from the sequencer)
    // ===================================================================================
    void playOn(int p, float vel) {
        if (structMode() == 3) polyOn(p, vel);
        else
            paraOn(p, vel);
    }
    void playOff(int p) {
        if (structMode() == 3) polyOff(p);
        else
            paraOff(p);
    }

    // Per-oscillator glide target. Linear types store a rate (semitones / sample).
    void setOscTarget(Voice& V, int k, int note, bool glide);
    bool glideAllowed(bool legato) const { return glideSecOf(get(Glide)) > 0.0f && (get(GlideGated) < 0.5f || legato); }

    static void trigger(Voice& V) { V.retrig = true; }
    static void release(Voice& V) {
        V.gate = false;
        V.retrig = false;
        if (V.fst) V.fst = 3;
        if (V.ast) V.ast = 3;
    }

    // A sleeping voice wakes: free-running oscillators land at arbitrary phases; the filter /
    // BLEP state it slept with is stale and cleared.
    void wake(Voice& V);

    // ---- paraphonic layer (Mono / Duo / Para: voice 0, oscillators grouped in slots) ----
    void paraOn(int p, float vel);
    void paraOff(int p);
    // The most recent S held notes own the S slots (a note keeps its slot while held; new
    // notes rotate into free slots). With Unison on, empty slots double the held notes.
    void assignSlots(bool legato);

    // ---- true poly layer (16 full voices; oldest-note steal with a short fade) -----
    void polyOn(int p, float vel);
    void startPoly(int v, int p, float vel, bool glide);
    void polyOff(int p);
    int allocate(bool& fade);

    // ===================================================================================
    // Sequencer / arpeggiator (drives playOn / playOff, sample-accurate)
    // ===================================================================================
    const int* poolArr() const { return get(SeqLatch) > 0.5f ? lat_ : phys_; }
    int poolN() const { return get(SeqLatch) > 0.5f ? latN_ : physN_; }

    void seqKeyOn(int p, float vel);
    void seqKeyOff(int p);
    void seqStart();
    void seqStop();
    double spb() const { return tSpb_ > 1.0 ? tSpb_ : sampleRate_ * 0.5; }
    double beatNow() const { return tPlaying_ && tSpb_ > 1.0 ? tBeat_ + tFrames_ / tSpb_ : seqIntBeat_; }

    int stepIndexOf(long long n) const;
    int stepType(int idx) const { return stepTypeOf(get(StepType0 + idx)); }
    int stepPitch(int idx) const { return stepPitchOf(get(StepPitch0 + idx)); }

    int arpNext();
    int seqPitchFor(int idx);
    void seqNoteOn(int p) {
        if (seqNote_ >= 0) playOff(seqNote_);
        seqNote_ = p;
        playOn(p, seqVel_);
        seqGateV_ = 1.0f;
    }
    void seqNoteOff() {
        if (seqNote_ >= 0) {
            playOff(seqNote_);
            seqNote_ = -1;
        }
        seqGateV_ = 0.0f;
    }

    // Fire every event due at the current position.
    void seqService();
    void seqProcess(double b);
    int seqSamplesToNext() const;

    // ===================================================================================
    // Structure / reset
    // ===================================================================================
    // Mode changes are applied before any note event or render sees them (audio thread):
    // a voice-structure or sequencer switch releases everything; turning latch off drops
    // the latched chord.
    void syncStructure();
    void resetVoices();
    void resetAll();
    void initDecimators();

    // ===================================================================================
    // Render
    // ===================================================================================
    // A chunk splits at sequencer events so they land on their exact sample.
    void renderChunk(float* out, int n);

    struct Cable {
        int src, dst;
        float amt;
    };
    struct VoiceCtl {
        int os;
        float invOs;
        double invFsOs, fsOs, sr;
        float oscOff[kOsc];
        int wave[kOsc];
        bool sync2, sync4;
        bool lfo24, unison;
        float uniSpread, drift;
        int fmode;
        float comp, kbd, velVca, atCut;
        bool drive;
        float fA, fD, fR, aA, aD, aR, fS, aS;
        float glideC, walkC, invFade, fadeC, cutMax;
        float voiceGain;
        Cable cab[kCables];
        int nCab;
        bool patched[consort::kDstN];
        float atten1, atten2;
    };

    void renderSpan(float* out, int n);

    // Global patch sources (the sequencer's, LFO, controllers).
    void fillGlobalSources(float* sv, float lfo, float mw, float at, float clk) const;
    // Per-voice patch sources (previous sample).
    static void fillVoiceSources(float* sv, const Voice& V, const VoiceCtl& c, float nz);

    void renderVoice(Voice& V, int n, const VoiceCtl& c);

    // ===================================================================================
    // Bucket-brigade delay (stereo; line rate ≤ 50 kHz, decimated at higher host rates)
    // ===================================================================================
    void setupDelayRate();
    void clearDelay();
    float readGain(int ch, float delay) const;
    float readLine(int ch, float delay) const;
    void processDelay(int n, const float* P);

    void publishTelemetry();

    // ---- state ------------------------------------------------------------------------
    enum Sm {
        SmPitch = 0,
        SmCut,
        SmK,
        SmSpace,
        SmFenv,
        SmM1,
        SmM2,
        SmM3,
        SmM4,
        SmNz,
        SmExt,
        SmPw,
        SmVol,
        SmMw,
        SmAt,
        SmLfoP,
        SmLfoC,
        SmLfoPwm,
        SmDlyMix,
        SmDlyFb,
        kSm
    };

    double sampleRate_ = 44100.0;
    std::atomic<double> pendingRate_{44100.0};
    Voice voices_[kMaxVoices];
    int focus_ = 0;
    // keys (physical), the voice layer's held notes, paraphonic slots
    int keys_[128] = {0};
    int keyN_ = 0;
    int pHeld_[128] = {0};
    int pHeldN_ = 0;
    int slotNote_[4] = {-1, -1, -1, -1};
    bool slotDbl_[4] = {false, false, false, false};
    int rr_ = 0;
    uint64_t orderCtr_ = 0;
    int lastStruct_ = 2, lastSeq_ = 0, fadeLen_ = 176, osCur_ = 2, activeCount_ = 0;
    // sequencer
    int phys_[128] = {0};
    int physN_ = 0;
    int lat_[128] = {0};
    int latN_ = 0;
    bool seqRun_ = false;
    int seqNote_ = -1, seqBase_ = 60, curIdx_ = -1, curType_ = 3, curSub_ = 0, curPitch_ = 60, arpPos_ = -1, clk_ = 0;
    uint32_t arpCtr_ = 0;
    long long curN_ = LLONG_MIN;
    float seqVel_ = 0.8f, seqPitchV_ = 0.0f, seqGateV_ = 0.0f;
    double seqIntBeat_ = 0.0, nextEvBeat_ = -1e300, offBeat_ = 1e300;
    // transport
    double tBeat_ = 0.0, tSpb_ = 0.0, tPos_ = 0.0, tFrames_ = 0.0;
    bool tPlaying_ = false, tFresh_ = true;
    // LFO / noise
    double lfoPh_ = 0.0;
    float lfoRnd0_ = 0.0f, lfoRnd1_ = 0.0f, lfoVal_ = 0.0f, noiseLast_ = 0.0f;
    pentad::Rng noiseRng_, lfoRng_, dlyRng_;
    float pk0_ = 0, pk1_ = 0, pk2_ = 0;
    float sm_[kSm] = {};
    bool smoothInit_ = false;
    float gPitch_[kChunk], gLfoP_[kChunk], gVib_[kChunk], gCut_[kChunk], gK_[kChunk], gSpace_[kChunk], gFenv_[kChunk];
    float gMix_[kOsc][kChunk], gNzL_[kChunk], gExt_[kChunk], gDuty_[kChunk], gVol_[kChunk], gMw_[kChunk], gAt_[kChunk];
    float gLfo_[kChunk], gClk_[kChunk], gDlyT_[kChunk], gDlyFb_[kChunk], gDlyMix_[kChunk];
    float gNoise_[kChunk * kMaxOs];
    float osL_[kChunk * kMaxOs], osR_[kChunk * kMaxOs], tmpL_[kChunk * 2], tmpR_[kChunk * 2], dsL_[kChunk],
        dsR_[kChunk];
    hiir::Downsampler2xFpu<12> dnSteep_[2];
    hiir::Downsampler2xFpu<5> dnMid_[2];
    // delay
    std::unique_ptr<float, FreeDel> dlyBuf_;
    int dlyN_ = kDlyLen, dlyW_ = 0, dlyD_ = 1, dlyPhase_ = 0;
    double dlyRate_ = 44100.0;
    bool dlyDirty_ = false;
    float dlyAccL_ = 0, dlyAccR_ = 0, dlyY_[2] = {0, 0}, dlyPrev_[2] = {0, 0}, dlyCur_[2] = {0, 0};
    float dlyTimeCur_[2] = {-1, -1}, envIn_[2] = {1e-6f, 1e-6f};
    consort::SvfLp pre_[2], post_[2];
    float peakL_ = 0, peakR_ = 0;
    // Auto-sleep hangover (see render()): idle only after voices, delay tap
    // and the delay line's flight time have all settled.
    static constexpr uint64_t kIdleSamples = 16384; // hangover past the flight time
    uint64_t quietSamples_ = 0;
    float chunkOutPeak_ = 0.0f; // our peak contribution across this render's chunks
    bool idle_ = false;
    double cpu_ = 0.0;
    mutable std::atomic<float> tele_[kScopeN] = {};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
