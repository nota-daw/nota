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
// Header-only; allocation-free after construction (the delay line is calloc'd, so an
// untouched instance — e.g. the engine's default-value probe — costs no page faults).

#pragma once

#include "Instrument.h"
#include "Pentad.h"   // pentad:: band-limited oscillator core + fast math, hiir decimators

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
    SrcNone = 0, SrcLfo, SrcEnv1, SrcEnv2, SrcOsc1, SrcOsc2, SrcOsc3, SrcOsc4, SrcNoise, SrcFilt,
    SrcSeqPitch, SrcSeqGate, SrcClock, SrcKbdPitch, SrcKbdGate, SrcVelocity, SrcAftertouch, SrcModWheel,
    SrcAtten1, SrcAtten2, SrcSum,
    kSrcN
};
enum Dst : int {
    DstNone = 0, DstLfoRate, DstGate1, DstGate2, DstPitchAll, DstPitch1, DstPitch2, DstPitch3, DstPitch4, DstPwm,
    DstCut1, DstCut2, DstReso, DstFiltIn, DstVcaCv, DstVcaIn, DstDlyTime, DstDlyFb, DstDlyMix, DstExtIn,
    DstAtten1, DstAtten2, DstSum,
    kDstN
};
constexpr int kJackScale = 63;   // room for future jacks without re-encoding saved patches

inline bool dstIsGlobal(int d) { return d == DstLfoRate || d == DstDlyTime || d == DstDlyFb || d == DstDlyMix; }
// Destination units per unit of source at depth ±100 %.
inline float dstScale(int d) {
    switch (d) {
        case DstLfoRate: return 4.0f;                                   // octaves of rate
        case DstPitchAll: case DstPitch1: case DstPitch2: case DstPitch3: case DstPitch4: return 60.0f;   // semitones
        case DstPwm: return 0.45f;                                      // duty
        case DstCut1: case DstCut2: return 5.0f;                        // octaves (1 V/oct from kbd pitch)
        case DstDlyTime: return 2.0f;                                   // octaves of time
        default: return 1.0f;
    }
}

inline float triVal(double ph) { return ph < 0.5 ? static_cast<float>(4.0 * ph - 1.0) : static_cast<float>(3.0 - 4.0 * ph); }

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
        o.ring.blep(syncD, -2.0f * static_cast<float>(pr) * gSaw);        // saw snaps back to -1
        if (!o.hi && du > 0.0f) o.ring.blep(syncD, 2.0f * gPul);          // pulse goes high
        if (tri) {
            o.ring.blep(syncD, -1.0f - triVal(pr));                        // triangle restarts at -1
            if (pr >= 0.5) o.ring.blamp(syncD, static_cast<float>(8.0 * dt));   // falling → rising slope
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
    float s[4] = { 0, 0, 0, 0 }, df[4] = { 0, 0, 0, 0 }, y4 = 0.0f;
    void clear() { for (int j = 0; j < 4; ++j) { s[j] = 0.0f; df[j] = 0.0f; } y4 = 0.0f; }
    float tick(float x, float g, float k, float comp, bool hp) {
        constexpr float kStageDrive = 0.3f, kLoopDrive = 2.2f;
        float G[4], S[4];
        for (int j = 0; j < 4; ++j) {
            const float a = g * pentad::tanhRatio(kStageDrive * df[j]);
            const float inv = 1.0f / (1.0f + a);
            G[j] = a * inv; S[j] = s[j] * inv;
        }
        const float Gt = G[0] * G[1] * G[2] * G[3];
        const float St = G[3] * (G[2] * (G[1] * S[0] + S[1]) + S[2]) + S[3];
        const float xin = x * (1.0f + comp * k);
        const float kf = k * pentad::tanhRatio(kLoopDrive * y4);
        float y3 = (Gt * xin + St) / (1.0f + kf * Gt);
        const float u = xin - kf * y3;
        const float y0 = G[0] * u + S[0], y1 = G[1] * y0 + S[1], y2 = G[2] * y1 + S[2];
        y3 = G[3] * y2 + S[3];
        df[0] = u - y0; df[1] = y0 - y1; df[2] = y1 - y2; df[3] = y2 - y3;
        s[0] = 2.0f * y0 - s[0]; s[1] = 2.0f * y1 - s[1]; s[2] = 2.0f * y2 - s[2]; s[3] = 2.0f * y3 - s[3];
        y4 = y3;
        if (!std::isfinite(s[0] + s[1] + s[2] + s[3])) { clear(); return 0.0f; }
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
        ic1 = 2.0f * v1 - ic1; ic2 = 2.0f * v2 - ic2;
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
        Tune = 0, Bend, ModWheel, Aftertouch, BendRange, Volume, Oversample, VoiceMode,
        TruePoly, MultiTrig, Unison, Drift, Glide, GlideType, GlideGated, VelVca, AtCut,
        // --- oscillators (17..30) ---
        O1Oct, O1Wave, O2Oct, O2Freq, O2Wave, O2Sync, O3Oct, O3Freq, O3Wave, O4Oct, O4Freq, O4Wave, O4Sync, Pw,
        // --- mixer (31..38) ---
        Mix1, Mix2, Mix3, Mix4, MixNoise, MixExt, NoiseColor, MixDrive,
        // --- dual ladder (39..45) ---
        FiltMode, Cutoff, Reso, Spacing, FEnvAmt, KbdTrk, BassComp,
        // --- envelopes (46..53) ---
        FAttack, FDecay, FSustain, FRelease, AAttack, ADecay, ASustain, ARelease,
        // --- LFO (54..60) ---
        LfoWave, LfoRate, LfoSync, LfoPitch, LfoCut, LfoPwm, LfoDest,
        // --- BBD delay (61..66) ---
        DlyTime, DlySpacing, DlyFb, DlyMix, DlyPing, DlyDigital,
        // --- sequencer / arpeggiator (67..74) ---
        SeqMode, SeqRate, SeqSwing, SeqOrder, SeqLatch, SeqLength, SeqRatchet, ArpOct,
        // --- 16 step types (75..90), 16 step pitches (91..106) ---
        StepType0,
        StepPitch0 = StepType0 + kSteps,
        // --- patch utilities (107..108) ---
        Atten1 = StepPitch0 + kSteps, Atten2,
        // --- 12 cables × (source, destination, depth) (109..144) ---
        Cable0,
        kNumParams = Cable0 + kCables * 3
    };

    // Scope telemetry layout (scopeRead): see publishTelemetry().
    enum Scope {
        ScMode = 0, ScActive, ScPeakL, ScPeakR, ScCpu, ScOs, ScRate, ScStep, ScBpm, ScLfo, ScEnv1, ScEnv2,
        ScOscNote, ScOscLvl = ScOscNote + kOsc, ScCutA = ScOscLvl + kOsc, ScCutB, ScDlyL, ScDlyR, ScSeqNote,
        kScopeN
    };

    Consort() {
        // "Init": four saws at 8' (a hair apart), PARA, stereo LP/LP a half-octave apart,
        // a moderate filter envelope, organ-ish amp envelope; delay dry, sequencer off.
        for (int i = 0; i < kNumParams; ++i) pn_[i].store(0.0f, std::memory_order_relaxed);
        set(Tune, 0.5f); set(Bend, 0.5f); set(ModWheel, 0.0f); set(Aftertouch, 0.0f);
        set(BendRange, 1.0f / 11.0f); set(Volume, 0.6f); set(Oversample, 0.5f); set(VoiceMode, 1.0f);
        set(TruePoly, 0.0f); set(MultiTrig, 1.0f); set(Unison, 1.0f); set(Drift, 0.25f);
        set(Glide, 0.0f); set(GlideType, 1.0f); set(GlideGated, 0.0f); set(VelVca, 0.0f); set(AtCut, 0.0f);
        set(O1Oct, 0.5f); set(O1Wave, 1.0f / 3.0f);
        set(O2Oct, 0.5f); set(O2Freq, 0.5f + 0.05f / 14.0f); set(O2Wave, 1.0f / 3.0f); set(O2Sync, 0.0f);
        set(O3Oct, 0.5f); set(O3Freq, 0.5f - 0.04f / 14.0f); set(O3Wave, 1.0f / 3.0f);
        set(O4Oct, 0.5f); set(O4Freq, 0.5f + 0.08f / 14.0f); set(O4Wave, 1.0f / 3.0f); set(O4Sync, 0.0f);
        set(Pw, 0.4f);
        set(Mix1, 0.75f); set(Mix2, 0.75f); set(Mix3, 0.75f); set(Mix4, 0.75f);
        set(MixNoise, 0.0f); set(MixExt, 0.0f); set(NoiseColor, 0.0f); set(MixDrive, 0.0f);
        set(FiltMode, 0.5f); set(Cutoff, 0.55f); set(Reso, 0.15f); set(Spacing, 0.5f + 0.5f / 6.0f);
        set(FEnvAmt, 0.66f); set(KbdTrk, 0.5f); set(BassComp, 0.0f);
        set(FAttack, 0.0f); set(FDecay, 0.45f); set(FSustain, 0.4f); set(FRelease, 0.4f);
        set(AAttack, 0.0f); set(ADecay, 0.45f); set(ASustain, 0.85f); set(ARelease, 0.35f);
        set(LfoWave, 0.0f); set(LfoRate, 0.5f); set(LfoSync, 0.0f); set(LfoPitch, 0.5f);
        set(LfoCut, 0.0f); set(LfoPwm, 0.0f); set(LfoDest, 0.0f);
        set(DlyTime, 0.62f); set(DlySpacing, 0.5f + 0.13f); set(DlyFb, 0.35f); set(DlyMix, 0.0f);
        set(DlyPing, 1.0f); set(DlyDigital, 0.0f);
        set(SeqMode, 0.0f); set(SeqRate, 0.6f); set(SeqSwing, 0.0f); set(SeqOrder, 0.0f);
        set(SeqLatch, 0.0f); set(SeqLength, 1.0f); set(SeqRatchet, 0.5f); set(ArpOct, 0.0f);
        static const int pat[kSteps] = { 0, 0, 12, 0, 7, 0, 12, 3, 0, 0, 12, 0, 7, 5, 10, 12 };
        for (int s = 0; s < kSteps; ++s) { set(StepType0 + s, 0.0f); set(StepPitch0 + s, 0.5f + pat[s] / 48.0f); }
        set(Atten1, 0.5f); set(Atten2, 0.5f);
        for (int c = 0; c < kCables; ++c) { set(Cable0 + c * 3, 0.0f); set(Cable0 + c * 3 + 1, 0.0f); set(Cable0 + c * 3 + 2, 0.75f); }

        initDecimators();
        dlyN_ = kDlyLen;
        dlyBuf_.reset(static_cast<float*>(std::calloc(static_cast<size_t>(kDlyLen) * 4, sizeof(float))));   // L, R, gain L, gain R
        resetAll();
    }

    int32_t kind() const override { return 15; }
    const char* displayName() const override { return "Nota Consort"; }

    // Deferred to the audio thread (render) so a device-rate change can't race a render.
    void setSampleRate(double sr) override { pendingRate_.store(sr > 0 ? sr : 44100.0, std::memory_order_relaxed); }

    int32_t activeVoiceCount() const override { return static_cast<int32_t>(tele_[ScActive].load(std::memory_order_relaxed)); }
    int32_t scopeRead(float* out, int32_t maxN) const override {
        const int n = std::min<int>(maxN, kScopeN);
        for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
        return n;
    }
    int32_t heldNotes(int32_t* out, int32_t maxN) const override {
        const int n = std::min<int>(keyN_, maxN);
        for (int i = 0; i < n; ++i) out[i] = keys_[i];
        return n;
    }

    // ---- parameters -----------------------------------------------------------
    // Display names group by the part before the last space (the automation menu nests on it).
    static const std::vector<std::string>& idTable() {
        static const std::vector<std::string> t = [] {
            std::vector<std::string> v = {
                "tune", "bend", "modwheel", "aftertouch", "bendrange", "volume", "oversample", "voicemode",
                "truepoly", "multitrig", "unison", "drift", "glide", "glidetype", "glidegated", "velvca", "atcut",
                "o1oct", "o1wave", "o2oct", "o2freq", "o2wave", "o2sync", "o3oct", "o3freq", "o3wave", "o4oct", "o4freq", "o4wave", "o4sync", "pw",
                "mix1", "mix2", "mix3", "mix4", "mixnoise", "mixext", "noisecolor", "mixdrive",
                "filtmode", "cutoff", "reso", "spacing", "fenvamt", "kbdtrk", "basscomp",
                "fattack", "fdecay", "fsustain", "frelease", "aattack", "adecay", "asustain", "arelease",
                "lfowave", "lforate", "lfosync", "lfopitch", "lfocut", "lfopwm", "lfodest",
                "dlytime", "dlyspacing", "dlyfb", "dlymix", "dlyping", "dlydigital",
                "seqmode", "seqrate", "seqswing", "seqorder", "seqlatch", "seqlen", "seqratchet", "arpoct" };
            for (int s = 0; s < kSteps; ++s) v.push_back("st" + std::to_string(s + 1));
            for (int s = 0; s < kSteps; ++s) v.push_back("sp" + std::to_string(s + 1));
            v.push_back("atten1"); v.push_back("atten2");
            for (int c = 0; c < kCables; ++c) {
                const std::string n = std::to_string(c + 1);
                v.push_back("c" + n + "src"); v.push_back("c" + n + "dst"); v.push_back("c" + n + "amt");
            }
            return v;
        }();
        return t;
    }
    static const std::vector<std::string>& nameTable() {
        static const std::vector<std::string> t = [] {
            std::vector<std::string> v = {
                "Master Tune", "Control Pitch-Bend", "Control Mod-Wheel", "Control Aftertouch", "Control Bend-Range",
                "Output Volume", "Output Oversampling", "Voice Mode",
                "Voice True-Poly", "Voice Multi-Trig", "Voice Unison-Detune", "Voice Drift", "Voice Glide-Time",
                "Voice Glide-Type", "Voice Glide-Gated", "Output Velocity-VCA", "Output Aftertouch-Cutoff",
                "Osc 1 Octave", "Osc 1 Wave", "Osc 2 Octave", "Osc 2 Frequency", "Osc 2 Wave", "Osc 2 Sync",
                "Osc 3 Octave", "Osc 3 Frequency", "Osc 3 Wave", "Osc 4 Octave", "Osc 4 Frequency", "Osc 4 Wave", "Osc 4 Sync",
                "Osc Pulse-Width",
                "Mixer Osc-1", "Mixer Osc-2", "Mixer Osc-3", "Mixer Osc-4", "Mixer Noise", "Mixer Ext", "Mixer Noise-Color", "Mixer Drive",
                "Filter Mode", "Filter Cutoff", "Filter Resonance", "Filter Spacing", "Filter Env-Amount", "Filter Key-Track", "Filter Bass-Comp",
                "Filter-Env Attack", "Filter-Env Decay", "Filter-Env Sustain", "Filter-Env Release",
                "Amp-Env Attack", "Amp-Env Decay", "Amp-Env Sustain", "Amp-Env Release",
                "LFO Wave", "LFO Rate", "LFO Tempo-Sync", "LFO Pitch", "LFO Cutoff", "LFO PWM", "LFO Destination",
                "Delay Time", "Delay Spacing", "Delay Feedback", "Delay Mix", "Delay Ping-Pong", "Delay Digital",
                "Seq Mode", "Seq Rate", "Seq Swing", "Seq Order", "Seq Latch", "Seq Length", "Seq Ratchet", "Seq Arp-Octaves" };
            auto two = [](int i) { return (i < 10 ? "0" : "") + std::to_string(i); };
            for (int s = 0; s < kSteps; ++s) v.push_back("Seq-Type Step-" + two(s + 1));
            for (int s = 0; s < kSteps; ++s) v.push_back("Seq-Pitch Step-" + two(s + 1));
            v.push_back("Patch Atten-1"); v.push_back("Patch Atten-2");
            for (int c = 0; c < kCables; ++c) {
                const std::string n = two(c + 1);
                v.push_back("Patch-Source Cable-" + n); v.push_back("Patch-Dest Cable-" + n); v.push_back("Patch-Depth Cable-" + n);
            }
            return v;
        }();
        return t;
    }
    static std::string paramId(int i) { return (i >= 0 && i < kNumParams) ? idTable()[static_cast<size_t>(i)] : std::string(); }

    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override { return paramId(i); }
    std::string pluginParamName(int32_t i) const override { return (i >= 0 && i < kNumParams) ? nameTable()[static_cast<size_t>(i)] : std::string(); }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        const auto& t = idTable();
        for (int32_t i = 0; i < kNumParams; ++i) if (t[static_cast<size_t>(i)] == id) return i;
        return -1;
    }

    // ---- project state (kNumParams normalized floats, little-endian) ------------
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
        auto s = std::make_shared<Consort>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(pendingRate_.load(std::memory_order_relaxed));
        return s;
    }

    // ---- musical mappings (mirrored by the editor's readouts) ------------------
    static int   octaveOf(float v)     { return std::clamp(static_cast<int>(std::lround(v * 4.0f)), 0, 4) - 2; }   // 32'..2'
    static float freqSemisOf(float v)  { return (v - 0.5f) * 14.0f; }                                          // ±7 st
    static int   waveOf(float v)       { return std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3); }
    static float dutyOf(float v)       { return 0.5f - 0.45f * std::clamp(v, 0.0f, 1.0f); }
    static float cutoffHzOf(float v)   { return 20.0f * std::pow(1000.0f, std::clamp(v, 0.0f, 1.0f)); }
    static float spacingOctOf(float v) { return (v - 0.5f) * 6.0f; }
    static float envAmtOctOf(float v)  { return (v - 0.5f) * 2.0f * 7.0f; }
    static float attackSecOf(float v)  { return static_cast<float>(expMap(v, 0.001, 10.0)); }
    static float decaySecOf(float v)   { return static_cast<float>(expMap(v, 0.003, 15.0)); }
    static float glideSecOf(float v)   { return v < 0.002f ? 0.0f : static_cast<float>(expMap(v, 0.005, 5.0)); }
    static float lfoHzOf(float v)      { return static_cast<float>(expMap(v, 0.05, 30.0)); }
    static float lfoCentsOf(float v)   { const float b = (v - 0.5f) * 2.0f; return b * std::fabs(b) * 1200.0f; }
    static float dlySecOf(float v)     { return static_cast<float>(expMap(v, 0.02, 1.5)); }
    static float dlySpacingOf(float v) { return (v - 0.5f) * 2.0f * 0.5f; }
    static int   bendRangeOf(float v)  { return 1 + std::clamp(static_cast<int>(std::lround(v * 11.0f)), 0, 11); }
    static float volumeGainOf(float v) { return 2.0f * v * v; }
    static int   seqLengthOf(float v)  { return 1 + std::clamp(static_cast<int>(std::lround(v * (kSteps - 1))), 0, kSteps - 1); }
    static int   ratchetOf(float v)    { return 2 + std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2); }
    static int   arpOctOf(float v)     { return 1 + std::clamp(static_cast<int>(std::lround(v * 2.0f)), 0, 2); }
    static float swingOf(float v)      { return 0.5f + 0.25f * std::clamp(v, 0.0f, 1.0f); }
    static int   stepTypeOf(float v)   { return std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3); }   // note/ratchet/tie/rest
    static int   stepPitchOf(float v)  { return std::clamp(static_cast<int>(std::lround((v - 0.5f) * 48.0f)), -24, 24); }
    static int   jackOf(float v)       { return std::clamp(static_cast<int>(std::lround(v * consort::kJackScale)), 0, consort::kJackScale); }
    static float depthOf(float v)      { return (v - 0.5f) * 2.0f; }
    static constexpr int kSeqRates = 6;
    static double seqRateBeats(float v) {
        static const double b[kSeqRates] = { 1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125 };   // 1/4 1/8 1/8T 1/16 1/16T 1/32
        return b[std::clamp(static_cast<int>(std::lround(v * (kSeqRates - 1))), 0, kSeqRates - 1)];
    }
    static constexpr int kSyncDivs = 14;
    static double syncBeatsOf(float v) {   // LFO cycle length in beats (quarter notes)
        static const double b[kSyncDivs] = { 32., 16., 8., 4., 2., 1.5, 1., 0.75, 2. / 3., 0.5, 1. / 3., 0.25, 1. / 6., 0.125 };
        return b[std::clamp(static_cast<int>(std::lround(v * (kSyncDivs - 1))), 0, kSyncDivs - 1)];
    }

    // ---- transport (play edge → reseed for determinism; LFO / sequencer sync) ----
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override {
        const double pos = beatStart * samplesPerBeat;
        const bool jumped = playing && tPlaying_ && std::fabs(pos - (tPos_ + tFrames_)) > 2.0;
        if (jumped) resetAll();
        else if (playing && !tPlaying_) {
            bool any = false;
            for (int v = 0; v < kMaxVoices; ++v) any |= voices_[v].active;
            if (!any && !seqRun_) resetAll();
        }
        tBeat_ = beatStart; tSpb_ = samplesPerBeat; tFresh_ = true;
        tPos_ = pos; tFrames_ = 0.0;
        if (playing != tPlaying_) nextEvBeat_ = -1e300;   // re-grid the sequencer on the new clock
        tPlaying_ = playing;
    }

    // ---- note events ----------------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        syncStructure();
        const float vel = std::clamp(velocity, 0.0f, 1.0f);
        pushKey(pitch);
        if (seqMode() != 0) { seqKeyOn(pitch, vel); return; }
        playOn(pitch, vel);
    }
    void noteOff(int32_t pitch) override {
        syncStructure();
        removeKey(pitch);
        if (seqMode() != 0) { seqKeyOff(pitch); return; }
        playOff(pitch);
    }
    // Stop / loop wrap: silence the voices and the sequencer; the delay tail keeps ringing.
    void allNotesOff() override { resetVoices(); }

    // ---- render ---------------------------------------------------------------------
    void render(float* out, int32_t frames) override {
        FtzGuard ftz;
        const auto t0 = std::chrono::steady_clock::now();
        const double pr = pendingRate_.load(std::memory_order_relaxed);
        if (pr != sampleRate_) {   // voices keep playing; only rate-derived state restarts
            sampleRate_ = pr;
            fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
            for (int c = 0; c < 2; ++c) { dnSteep_[c].clear_buffers(); dnMid_[c].clear_buffers(); }
            smoothInit_ = false;
            setupDelayRate();
        }
        for (int32_t done = 0; done < frames;) {
            const int n = std::min<int>(kChunk, frames - done);
            renderChunk(out + done * 2, n);   // advances tFrames_
            done += n;
        }
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        const double avail = frames / sampleRate_;
        if (avail > 0.0) cpu_ += 0.05 * (el / avail - cpu_);
        publishTelemetry();
    }

private:
    static constexpr int kChunk = 64;
    static constexpr int kMaxOs = 4;
    static constexpr int kDlyLen = 1 << 17;           // per channel, at a line rate ≤ 50 kHz
    static constexpr double kPi = 3.14159265358979323846;

    struct Voice {
        bool active = false, gate = false, retrig = false;
        int note = 60; float vel = 0.8f;
        uint64_t order = 0;
        // stolen-voice handoff (poly)
        int pendNote = -1, fade = 0; float pendVel = 0.0f; bool pendGlide = false;
        // per-oscillator pitch (semitones), glide, slot gain (paraphonic fades)
        float pitch[kOsc] = { 60, 60, 60, 60 }, tgt[kOsc] = { 60, 60, 60, 60 }, rate[kOsc] = { 0, 0, 0, 0 };
        bool hasPitch[kOsc] = { false, false, false, false };
        float g[kOsc] = { 0, 0, 0, 0 }, gT[kOsc] = { 0, 0, 0, 0 };
        float det[kOsc] = { 0, 0, 0, 0 };
        // envelopes: stage 0 idle, 1 attack, 2 decay/sustain, 3 release
        float fenv = 0.0f, aenv = 0.0f; int fst = 0, ast = 0;
        bool g1s = false, g2s = false;     // patched gate-in state (edge detection)
        pentad::Osc osc[kOsc];
        consort::Ladder fa, fb;
        pentad::Adaa sat;
        // patch: previous-sample sources and utility nodes, gate inputs
        float oPrev[kOsc] = { 0, 0, 0, 0 }, fPrev = 0.0f, vPrev = 0.0f;
        float uIn[3] = { 0, 0, 0 };        // atten 1, atten 2, sum (inputs, previous sample)
        float gIn1 = 0.0f, gIn2 = 0.0f;
        // vintage: static per-osc spread + slow random walk
        float oOff[kOsc] = { 0, 0, 0, 0 }, oCut = 0.0f;
        float w[kOsc + 1] = { 0, 0, 0, 0, 0 }, wt[kOsc + 1] = { 0, 0, 0, 0, 0 }; int wCount = 0;
        pentad::Rng rng;
        // previous base-rate controls, interpolated across the oversampled sub-steps
        float pP[kOsc] = { 0, 0, 0, 0 }, pCa = 0, pCb = 0, pAmp = 0, pDuty = 0.5f;
        bool fresh = true;
        float cutA = 0.0f, cutB = 0.0f;    // telemetry (log2 Hz)
        float level = 0.0f;
    };

    struct FtzGuard {
#if defined(NOTA_CONSORT_FTZ_X86)
        unsigned int old; FtzGuard() : old(_mm_getcsr()) { _mm_setcsr(old | 0x8040u); } ~FtzGuard() { _mm_setcsr(old); }
#elif defined(NOTA_CONSORT_FTZ_ARM)
        uint64_t old = 0;
        FtzGuard() { asm volatile("mrs %0, fpcr" : "=r"(old)); const uint64_t n = old | (1ull << 24); asm volatile("msr fpcr, %0" : : "r"(n)); }
        ~FtzGuard() { asm volatile("msr fpcr, %0" : : "r"(old)); }
#endif
    };
    struct FreeDel { void operator()(float* p) const { std::free(p); } };

    void  set(int p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(int p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    static float coefOf(float tau, double sr) { return 1.0f - static_cast<float>(std::exp(-1.0 / (std::max(tau, 1e-5f) * sr))); }
    // 0..2 = Mono / Duo / Para, 3 = true poly.
    int structMode() const { return get(TruePoly) > 0.5f ? 3 : std::clamp(static_cast<int>(std::lround(get(VoiceMode) * 2.0f)), 0, 2); }
    int seqMode() const { return std::clamp(static_cast<int>(std::lround(get(SeqMode) * 2.0f)), 0, 2); }
    static int slotsOf(int mode) { return mode == 0 ? 1 : mode == 1 ? 2 : 4; }
    static int slotOfOsc(int k, int S) { return S == 1 ? 0 : S == 2 ? k / 2 : k; }

    // ---- key lists ------------------------------------------------------------------
    static void listPush(int* a, int& n, int p) { for (int i = 0; i < n; ++i) if (a[i] == p) { for (int j = i; j < n - 1; ++j) a[j] = a[j + 1]; --n; break; } if (n < 128) a[n++] = p; }
    static bool listRemove(int* a, int& n, int p) { int w = 0; bool f = false; for (int r = 0; r < n; ++r) { if (a[r] == p) f = true; else a[w++] = a[r]; } n = w; return f; }
    void pushKey(int p) { listPush(keys_, keyN_, p); }
    void removeKey(int p) { listRemove(keys_, keyN_, p); }

    // ===================================================================================
    // Voice layer: playOn / playOff (from the keyboard, or from the sequencer)
    // ===================================================================================
    void playOn(int p, float vel) {
        if (structMode() == 3) polyOn(p, vel); else paraOn(p, vel);
    }
    void playOff(int p) {
        if (structMode() == 3) polyOff(p); else paraOff(p);
    }

    // Per-oscillator glide target. Linear types store a rate (semitones / sample).
    void setOscTarget(Voice& V, int k, int note, bool glide) {
        const float tg = static_cast<float>(note);
        const float T = glideSecOf(get(Glide));
        if (!glide || !V.hasPitch[k] || T <= 0.0f) { V.pitch[k] = V.tgt[k] = tg; V.rate[k] = 0.0f; V.hasPitch[k] = true; return; }
        V.tgt[k] = tg;
        const int type = std::clamp(static_cast<int>(std::lround(get(GlideType) * 2.0f)), 0, 2);
        const double spT = T * sampleRate_;
        if (type == 0) V.rate[k] = static_cast<float>(12.0 / spT);                              // LCR: T per octave
        else if (type == 1) V.rate[k] = static_cast<float>(std::fabs(tg - V.pitch[k]) / spT);    // LCT: always T
        else V.rate[k] = -1.0f;                                                                  // EXP
    }
    bool glideAllowed(bool legato) const { return glideSecOf(get(Glide)) > 0.0f && (get(GlideGated) < 0.5f || legato); }

    static void trigger(Voice& V) { V.retrig = true; }
    static void release(Voice& V) { V.gate = false; V.retrig = false; if (V.fst) V.fst = 3; if (V.ast) V.ast = 3; }

    // A sleeping voice wakes: free-running oscillators land at arbitrary phases; the filter /
    // BLEP state it slept with is stale and cleared.
    void wake(Voice& V) {
        V.active = true; V.fresh = true;
        const float duty = dutyOf(get(Pw));
        for (int k = 0; k < kOsc; ++k) {
            V.osc[k].ph += V.rng.uni(); V.osc[k].ph -= std::floor(V.osc[k].ph);
            V.osc[k].hi = V.osc[k].ph < duty; V.osc[k].ring.clear();
            V.oPrev[k] = 0.0f;
        }
        V.fa.clear(); V.fb.clear(); V.sat.reset();
        V.fPrev = V.vPrev = 0.0f; V.uIn[0] = V.uIn[1] = V.uIn[2] = 0.0f; V.gIn1 = V.gIn2 = 0.0f;
        V.g1s = V.g2s = false;
    }

    // ---- paraphonic layer (Mono / Duo / Para: voice 0, oscillators grouped in slots) ----
    void paraOn(int p, float vel) {
        const bool others = pHeldN_ > 0;
        listPush(pHeld_, pHeldN_, p);
        Voice& V = voices_[0];
        V.vel = vel; V.note = p; V.order = ++orderCtr_;
        focus_ = 0;
        assignSlots(others);
        V.gate = true;
        if (!others || get(MultiTrig) > 0.5f) trigger(V);
        if (!V.active) wake(V);
    }
    void paraOff(int p) {
        if (!listRemove(pHeld_, pHeldN_, p)) return;
        Voice& V = voices_[0];
        if (pHeldN_ == 0) { release(V); return; }   // slots keep their pitch for the release tail
        V.note = pHeld_[pHeldN_ - 1];
        assignSlots(true);
    }
    // The most recent S held notes own the S slots (a note keeps its slot while held; new
    // notes rotate into free slots). With Unison on, empty slots double the held notes.
    void assignSlots(bool legato) {
        Voice& V = voices_[0];
        const int S = slotsOf(structMode());
        const int nw = std::min(S, pHeldN_);
        int want[4] = { -1, -1, -1, -1 };
        for (int j = 0; j < nw; ++j) want[j] = pHeld_[pHeldN_ - nw + j];
        int na[4] = { -1, -1, -1, -1 }; bool used[4] = { false, false, false, false };
        for (int s = 0; s < S; ++s) {
            if (slotDbl_[s] || slotNote_[s] < 0) continue;
            for (int j = 0; j < nw; ++j) if (!used[j] && want[j] == slotNote_[s]) { na[s] = want[j]; used[j] = true; break; }
        }
        for (int j = 0; j < nw; ++j) {
            if (used[j]) continue;
            for (int t = 0; t < S; ++t) { const int s = (rr_ + t) % S; if (na[s] < 0) { na[s] = want[j]; used[j] = true; rr_ = (s + 1) % S; break; } }
        }
        bool dbl[4] = { false, false, false, false };
        if (get(Unison) > 0.5f && nw > 0) {
            int k = 0;
            for (int s = 0; s < S; ++s) if (na[s] < 0) { na[s] = want[nw - 1 - (k % nw)]; dbl[s] = true; ++k; }
        }
        for (int s = 0; s < 4; ++s) { slotNote_[s] = s < S ? na[s] : -1; slotDbl_[s] = s < S && dbl[s]; }
        const bool gl = glideAllowed(legato);
        for (int k = 0; k < kOsc; ++k) {
            const int note = na[slotOfOsc(k, S)];
            if (note >= 0) { setOscTarget(V, k, note, gl); V.gT[k] = 1.0f; }
            else V.gT[k] = 0.0f;
        }
    }

    // ---- true poly layer (16 full voices; oldest-note steal with a short fade) -----
    void polyOn(int p, float vel) {
        const bool others = pHeldN_ > 0;
        listPush(pHeld_, pHeldN_, p);
        const bool glide = glideAllowed(others);
        int v = -1;
        for (int k = 0; k < kMaxVoices; ++k) { const Voice& V = voices_[k]; if (V.active && V.gate && V.note == p && V.pendNote < 0) { v = k; break; } }
        bool fade = false;
        if (v < 0) v = allocate(fade);
        Voice& V = voices_[v];
        if (fade) { V.pendNote = p; V.pendVel = vel; V.pendGlide = glide; V.fade = fadeLen_; V.gate = false; return; }
        startPoly(v, p, vel, glide);
    }
    void startPoly(int v, int p, float vel, bool glide) {
        Voice& V = voices_[v];
        V.note = p; V.vel = vel; V.gate = true; V.order = ++orderCtr_;
        for (int k = 0; k < kOsc; ++k) { setOscTarget(V, k, p, glide); V.gT[k] = 1.0f; V.g[k] = 1.0f; }
        trigger(V);
        focus_ = v;
        if (!V.active) wake(V);
    }
    void polyOff(int p) {
        listRemove(pHeld_, pHeldN_, p);
        for (int v = 0; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            if (V.gate && V.note == p) release(V);
            if (V.pendNote == p) V.pendNote = -1;
        }
    }
    int allocate(bool& fade) {
        fade = false;
        int best = -1; uint64_t bo = ~0ull;
        for (int v = 0; v < kMaxVoices; ++v) if (!voices_[v].active && voices_[v].pendNote < 0 && voices_[v].order < bo) { bo = voices_[v].order; best = v; }
        if (best >= 0) return best;
        for (int v = 0; v < kMaxVoices; ++v) if (!voices_[v].gate && voices_[v].pendNote < 0 && voices_[v].order < bo) { bo = voices_[v].order; best = v; }
        if (best >= 0) return best;
        for (int v = 0; v < kMaxVoices; ++v) if (voices_[v].pendNote < 0 && voices_[v].order < bo) { bo = voices_[v].order; best = v; }
        if (best < 0) best = 0;
        fade = true;
        return best;
    }

    // ===================================================================================
    // Sequencer / arpeggiator (drives playOn / playOff, sample-accurate)
    // ===================================================================================
    const int* poolArr() const { return get(SeqLatch) > 0.5f ? lat_ : phys_; }
    int poolN() const { return get(SeqLatch) > 0.5f ? latN_ : physN_; }

    void seqKeyOn(int p, float vel) {
        const bool latch = get(SeqLatch) > 0.5f;
        if (latch && physN_ == 0) latN_ = 0;      // a fresh chord replaces the latched one
        listPush(phys_, physN_, p);
        if (latch) listPush(lat_, latN_, p);
        seqVel_ = vel; seqBase_ = p;
        if (!seqRun_ && poolN() > 0) seqStart();
    }
    void seqKeyOff(int p) {
        listRemove(phys_, physN_, p);
        if (get(SeqLatch) > 0.5f) return;
        if (physN_ == 0) { seqStop(); return; }
        seqBase_ = phys_[physN_ - 1];
    }
    void seqStart() {
        seqRun_ = true; curN_ = LLONG_MIN; arpPos_ = -1; seqIntBeat_ = 0.0; nextEvBeat_ = -1e300;
        curType_ = 3; curSub_ = 0;
    }
    void seqStop() {
        if (seqNote_ >= 0) { playOff(seqNote_); seqNote_ = -1; }
        seqRun_ = false; curN_ = LLONG_MIN; curIdx_ = -1; seqGateV_ = 0.0f;
    }
    double spb() const { return tSpb_ > 1.0 ? tSpb_ : sampleRate_ * 0.5; }
    double beatNow() const { return tPlaying_ && tSpb_ > 1.0 ? tBeat_ + tFrames_ / tSpb_ : seqIntBeat_; }

    int stepIndexOf(long long n) const {
        const int len = seqLengthOf(get(SeqLength));
        const int ord = std::clamp(static_cast<int>(std::lround(get(SeqOrder) * 2.0f)), 0, 2);
        const long long m = ((n % len) + len) % len;
        if (seqMode() == 2 || ord == 0) return static_cast<int>(m);          // ARP: order walks the chord
        if (ord == 1) return len - 1 - static_cast<int>(m);
        return static_cast<int>(pentad::hash32(static_cast<uint32_t>(n) * 2654435761U ^ 0x51ED27U) % static_cast<uint32_t>(len));
    }
    int stepType(int idx) const { return stepTypeOf(get(StepType0 + idx)); }
    int stepPitch(int idx) const { return stepPitchOf(get(StepPitch0 + idx)); }

    int arpNext() {
        int srt[128]; const int n = poolN();
        if (n <= 0) return seqBase_;
        std::memcpy(srt, poolArr(), sizeof(int) * static_cast<size_t>(n));
        std::sort(srt, srt + n);
        const int oct = arpOctOf(get(ArpOct));
        const int cnt = n * oct;
        const int ord = std::clamp(static_cast<int>(std::lround(get(SeqOrder) * 2.0f)), 0, 2);
        int j;
        if (ord == 2) j = static_cast<int>(pentad::hash32(static_cast<uint32_t>(++arpCtr_) * 0x9E3779B9U ^ 0xA11CE5U) % static_cast<uint32_t>(cnt));
        else { arpPos_ = (arpPos_ + 1) % cnt; j = ord == 1 ? cnt - 1 - arpPos_ : arpPos_; }
        seqPitchV_ = static_cast<float>(12 * (j / n) + srt[j % n] - srt[0]) / 60.0f;
        return srt[j % n] + 12 * (j / n);
    }
    int seqPitchFor(int idx) {
        if (seqMode() == 2) return std::clamp(arpNext(), 0, 127);
        const int sp = stepPitch(idx);
        seqPitchV_ = static_cast<float>(sp) / 60.0f;
        return std::clamp(seqBase_ + sp, 0, 127);
    }
    void seqNoteOn(int p) { if (seqNote_ >= 0) playOff(seqNote_); seqNote_ = p; playOn(p, seqVel_); seqGateV_ = 1.0f; }
    void seqNoteOff() { if (seqNote_ >= 0) { playOff(seqNote_); seqNote_ = -1; } seqGateV_ = 0.0f; }

    // Fire every event due at the current position.
    void seqService() {
        if (!seqRun_) return;
        const double b = beatNow();
        for (int guard = 0; guard < 8 && b + 1e-9 >= nextEvBeat_; ++guard) seqProcess(b);
    }
    void seqProcess(double b) {
        constexpr double eps = 1e-9;
        const double rb = seqRateBeats(get(SeqRate));
        const double sw = swingOf(get(SeqSwing));
        const double pair = std::floor((b + eps) / (2.0 * rb));
        const double p = b - pair * 2.0 * rb;
        const bool odd = p + eps >= 2.0 * rb * sw;
        const long long n = static_cast<long long>(pair) * 2 + (odd ? 1 : 0);
        const double sb = pair * 2.0 * rb + (odd ? 2.0 * rb * sw : 0.0);
        const double sl = odd ? 2.0 * rb * (1.0 - sw) : 2.0 * rb * sw;
        const int R = ratchetOf(get(SeqRatchet));
        if (n != curN_) {
            curN_ = n;
            const int idx = stepIndexOf(n);
            const int nextT = stepType(stepIndexOf(n + 1));
            curIdx_ = idx; clk_ = std::max(1, static_cast<int>(0.005 * sampleRate_));
            const int t = stepType(idx);
            const double gateEnd = nextT == 2 ? 1e300 : sb + 0.6 * sl;
            if (t == 3) { seqNoteOff(); curType_ = 3; }
            else if (t == 2) {
                if (seqNote_ < 0) curType_ = 3;
                else { curType_ = 2; offBeat_ = gateEnd; }
            } else if (t == 1) {
                seqNoteOn(seqPitchFor(idx)); curType_ = 1; curSub_ = 0; curPitch_ = seqNote_;
                offBeat_ = sb + 0.5 * sl / R;
            } else {
                seqNoteOn(seqPitchFor(idx)); curType_ = 0; offBeat_ = gateEnd;
            }
        } else if (curType_ == 1) {
            const int k = static_cast<int>(std::floor((b - sb + eps) / (sl / R)));
            if (k != curSub_ && k < R) {
                curSub_ = k; clk_ = std::max(1, static_cast<int>(0.005 * sampleRate_));
                seqNoteOn(curPitch_);
                offBeat_ = sb + (k + 0.5) * sl / R;
            }
        }
        if (seqNote_ >= 0 && b + eps >= offBeat_) seqNoteOff();
        double nx = sb + sl;
        if (curType_ == 1) { const double sub = sb + (curSub_ + 1) * sl / R; if (sub < nx - eps) nx = sub; }
        if (seqNote_ >= 0 && offBeat_ < nx) nx = offBeat_;
        nextEvBeat_ = std::max(nx, b + 1e-7);
    }
    int seqSamplesToNext() const {
        if (!seqRun_) return INT_MAX;
        const double d = (nextEvBeat_ - beatNow()) * spb();
        if (d <= 0.0) return 0;
        return d > 1e9 ? INT_MAX : static_cast<int>(std::ceil(d - 1e-6));
    }

    // ===================================================================================
    // Structure / reset
    // ===================================================================================
    // Mode changes are applied before any note event or render sees them (audio thread):
    // a voice-structure or sequencer switch releases everything; turning latch off drops
    // the latched chord.
    void syncStructure() {
        const int st = structMode();
        if (st != lastStruct_) {
            for (auto& V : voices_) if (V.gate || V.pendNote >= 0) { release(V); V.pendNote = -1; }
            pHeldN_ = 0; rr_ = 0;
            for (int s = 0; s < 4; ++s) { slotNote_[s] = -1; slotDbl_[s] = false; }
            if (seqNote_ >= 0) seqNote_ = -1;
            lastStruct_ = st;
        }
        const int sm = seqMode();
        if (sm != lastSeq_) {
            seqStop();
            physN_ = latN_ = 0;
            for (auto& V : voices_) if (V.gate) release(V);
            pHeldN_ = 0;
            lastSeq_ = sm;
        }
        if (get(SeqLatch) < 0.5f && latN_ > 0) { latN_ = 0; if (seqRun_ && physN_ == 0) seqStop(); }
    }
    void resetVoices() {
        for (int v = 0; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            const pentad::Rng rng = V.rng;
            const float oOff[kOsc] = { V.oOff[0], V.oOff[1], V.oOff[2], V.oOff[3] }; const float oCut = V.oCut;
            V = Voice{};
            V.rng = rng; V.oCut = oCut;
            for (int k = 0; k < kOsc; ++k) { V.oOff[k] = oOff[k]; V.osc[k].ph = V.rng.uni(); }
            V.wCount = 1 + static_cast<int>(V.rng.next() % 4096u);
        }
        pHeldN_ = 0; keyN_ = 0; rr_ = 0; physN_ = latN_ = 0;
        for (int s = 0; s < 4; ++s) { slotNote_[s] = -1; slotDbl_[s] = false; }
        seqRun_ = false; seqNote_ = -1; curN_ = LLONG_MIN; curIdx_ = -1; seqGateV_ = 0.0f; seqPitchV_ = 0.0f; clk_ = 0;
        lastStruct_ = structMode(); lastSeq_ = seqMode();
        fadeLen_ = std::max(1, static_cast<int>(0.004 * sampleRate_));
    }
    void resetAll() {
        for (int v = 0; v < kMaxVoices; ++v) {
            Voice& V = voices_[v];
            V.rng.seed(pentad::hash32(0xC0A5E7u ^ pentad::hash32(static_cast<uint32_t>(v) * 0x9E3779B9U + 1U)));
            for (int k = 0; k < kOsc; ++k) V.oOff[k] = V.rng.bi();
            V.oCut = V.rng.bi();
        }
        resetVoices();
        orderCtr_ = 0; focus_ = 0; arpCtr_ = 0;
        lfoPh_ = 0.0; lfoRnd0_ = lfoRnd1_ = 0.0f; tFresh_ = true;
        noiseRng_.seed(0xA5A5A5A5U); lfoRng_.seed(0x5A5A5A5AU); dlyRng_.seed(0x3C3C3C3CU);
        pk0_ = pk1_ = pk2_ = 0.0f;
        for (int c = 0; c < 2; ++c) { dnSteep_[c].clear_buffers(); dnMid_[c].clear_buffers(); }
        smoothInit_ = false;
        peakL_ = peakR_ = 0.0f;
        clearDelay();
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

    // ===================================================================================
    // Render
    // ===================================================================================
    // A chunk splits at sequencer events so they land on their exact sample.
    void renderChunk(float* out, int n) {
        syncStructure();
        for (int pos = 0; pos < n;) {
            seqService();
            int m = n - pos;
            const int toEv = seqSamplesToNext();
            if (toEv < m) m = std::max(1, toEv);
            renderSpan(out + pos * 2, m);
            if (!(tPlaying_ && tSpb_ > 1.0)) seqIntBeat_ += m / spb();
            tFrames_ += m;
            pos += m;
        }
    }

    struct Cable { int src, dst; float amt; };
    struct VoiceCtl {
        int os; float invOs; double invFsOs, fsOs, sr;
        float oscOff[kOsc]; int wave[kOsc]; bool sync2, sync4;
        bool lfo24, unison; float uniSpread, drift;
        int fmode; float comp, kbd, velVca, atCut; bool drive;
        float fA, fD, fR, aA, aD, aR, fS, aS;
        float glideC, walkC, invFade, fadeC, cutMax;
        float voiceGain;
        Cable cab[kCables]; int nCab;
        bool patched[consort::kDstN];
        float atten1, atten2;
    };

    void renderSpan(float* out, int n) {
        using namespace consort;
        float P[kNumParams];
        for (int i = 0; i < kNumParams; ++i) P[i] = pn_[i].load(std::memory_order_relaxed);
        const double sr = sampleRate_;

        int os = P[Oversample] < 0.25f ? 1 : P[Oversample] < 0.75f ? 2 : 4;
        if (sr >= 176000.0) os = 1; else if (sr >= 88000.0) os = std::max(1, os / 2);
        if (os != osCur_) { for (int c = 0; c < 2; ++c) { dnSteep_[c].clear_buffers(); dnMid_[c].clear_buffers(); } osCur_ = os; }
        const double fsOs = sr * os;

        // ---- patch cables (per-voice and global destinations) ----
        VoiceCtl c{};
        Cable cabG[kCables]; int nG = 0;
        for (int k = 0; k < kCables; ++k) {
            const int s = jackOf(P[Cable0 + k * 3]), d = jackOf(P[Cable0 + k * 3 + 1]);
            if (s <= 0 || s >= kSrcN || d <= 0 || d >= kDstN) continue;
            const float dp = depthOf(P[Cable0 + k * 3 + 2]);
            const Cable cb{ s, d, dp * std::fabs(dp) * dstScale(d) };
            c.patched[d] = true;
            if (dstIsGlobal(d)) cabG[nG++] = cb; else c.cab[c.nCab++] = cb;
        }
        c.atten1 = depthOf(P[Atten1]); c.atten2 = depthOf(P[Atten2]);

        // ---- global per-sample controls (smoothed ~20 ms) ----
        const float sc = 1.0f - static_cast<float>(std::exp(-1.0 / (0.02 * sr)));
        const float bendR = static_cast<float>(bendRangeOf(P[BendRange]));
        const float tgt[kSm] = {
            (P[Tune] - 0.5f) * 4.0f + (P[Bend] - 0.5f) * 2.0f * bendR,            // SmPitch (semis)
            std::log2(20.0f) + std::clamp(P[Cutoff], 0.0f, 1.0f) * 9.96578428f,   // SmCut (log2 Hz)
            4.3f * P[Reso], spacingOctOf(P[Spacing]), envAmtOctOf(P[FEnvAmt]),
            P[Mix1], P[Mix2], P[Mix3], P[Mix4], P[MixNoise], P[MixExt],
            P[Pw], volumeGainOf(P[Volume]) * 2.0f, P[ModWheel], P[Aftertouch],
            lfoCentsOf(P[LfoPitch]) * 0.01f, P[LfoCut] * P[LfoCut] * 4.0f, P[LfoPwm] * 0.45f,
            P[DlyMix], P[DlyFb] };
        if (!smoothInit_) { for (int k = 0; k < kSm; ++k) sm_[k] = tgt[k]; smoothInit_ = true; }

        // LFO
        const int lfoW = std::clamp(static_cast<int>(std::lround(P[LfoWave] * 5.0f)), 0, 5);
        const bool lfoSync = P[LfoSync] > 0.5f && tSpb_ > 1.0;
        double lfoInc = lfoHzOf(P[LfoRate]) / sr;
        if (lfoSync) {
            const double beats = syncBeatsOf(P[LfoRate]);
            lfoInc = 1.0 / (beats * tSpb_);
            if (tFresh_ && tPlaying_) { const double cyc = (tBeat_ + tFrames_ / tSpb_) / beats; lfoPh_ = cyc - std::floor(cyc); }
        }
        tFresh_ = false;
        const Voice& Fv = voices_[focus_];
        const bool atCut = P[AtCut] > 0.5f;

        for (int i = 0; i < n; ++i) {
            for (int k = 0; k < kSm; ++k) sm_[k] += (tgt[k] - sm_[k]) * sc;
            // global patch destinations (sources: global signals now, the focus voice's last sample)
            float gd[4] = { 0, 0, 0, 0 };   // lfo rate, dly time, dly fb, dly mix
            if (nG > 0) {
                float sv[kSrcN];
                fillGlobalSources(sv, lfoVal_, sm_[SmMw], sm_[SmAt], clk_ > 0 ? 1.0f : 0.0f);
                fillVoiceSources(sv, Fv, c, noiseLast_);
                for (int k = 0; k < nG; ++k) {
                    const float v = cabG[k].amt * sv[cabG[k].src];
                    switch (cabG[k].dst) { case DstLfoRate: gd[0] += v; break; case DstDlyTime: gd[1] += v; break; case DstDlyFb: gd[2] += v; break; default: gd[3] += v; break; }
                }
            }
            // LFO
            const float ph = static_cast<float>(lfoPh_);
            float lfo;
            switch (lfoW) {
                case 0: lfo = std::sin(ph * 6.2831853f); break;
                case 1: lfo = 2.0f * ph - 1.0f; break;
                case 2: lfo = 1.0f - 2.0f * ph; break;
                case 3: lfo = ph < 0.5f ? 1.0f : -1.0f; break;
                case 4: lfo = lfoRnd1_; break;
                default: lfo = lfoRnd0_ + (lfoRnd1_ - lfoRnd0_) * (0.5f - 0.5f * std::cos(ph * 3.14159265f)); break;
            }
            lfoVal_ = lfo;
            lfoPh_ += lfoInc * (gd[0] != 0.0f ? pentad::exp2f(std::clamp(gd[0], -8.0f, 8.0f)) : 1.0f);
            if (lfoPh_ >= 1.0) { lfoPh_ -= std::floor(lfoPh_); lfoRnd0_ = lfoRnd1_; lfoRnd1_ = lfoRng_.bi(); }
            gLfo_[i] = lfo;
            gClk_[i] = clk_ > 0 ? 1.0f : 0.0f; if (clk_ > 0) --clk_;
            gPitch_[i] = sm_[SmPitch];
            gLfoP_[i] = lfo * (sm_[SmLfoP] + sm_[SmMw] * 0.5f);
            gVib_[i] = lfo * sm_[SmMw] * 0.5f;
            gCut_[i] = sm_[SmCut] + lfo * sm_[SmLfoC] + (atCut ? 2.0f * sm_[SmAt] : 0.0f);
            gK_[i] = sm_[SmK]; gSpace_[i] = sm_[SmSpace]; gFenv_[i] = sm_[SmFenv];
            gMix_[0][i] = sm_[SmM1]; gMix_[1][i] = sm_[SmM2]; gMix_[2][i] = sm_[SmM3]; gMix_[3][i] = sm_[SmM4];
            gNzL_[i] = sm_[SmNz]; gExt_[i] = sm_[SmExt];
            gDuty_[i] = dutyOf(sm_[SmPw]) - lfo * sm_[SmLfoPwm] * 0.5f;
            gVol_[i] = sm_[SmVol]; gMw_[i] = sm_[SmMw]; gAt_[i] = sm_[SmAt];
            gDlyT_[i] = gd[1]; gDlyFb_[i] = std::clamp(sm_[SmDlyFb] + gd[2], 0.0f, 1.05f); gDlyMix_[i] = std::clamp(sm_[SmDlyMix] + gd[3], 0.0f, 1.0f);
        }
        // Shared noise at the oversampled rate (white, or Kellet "economy" pink).
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
        noiseLast_ = gNoise_[n * os - 1];

        // ---- voices ----
        const int m = n * os;
        std::fill(osL_, osL_ + m, 0.0f);
        std::fill(osR_, osR_ + m, 0.0f);
        c.os = os; c.invOs = 1.0f / static_cast<float>(os); c.invFsOs = 1.0 / fsOs; c.fsOs = fsOs; c.sr = sr;
        const int oct[kOsc] = { octaveOf(P[O1Oct]), octaveOf(P[O2Oct]), octaveOf(P[O3Oct]), octaveOf(P[O4Oct]) };
        c.oscOff[0] = 12.0f * oct[0];
        c.oscOff[1] = 12.0f * oct[1] + freqSemisOf(P[O2Freq]);
        c.oscOff[2] = 12.0f * oct[2] + freqSemisOf(P[O3Freq]);
        c.oscOff[3] = 12.0f * oct[3] + freqSemisOf(P[O4Freq]);
        c.wave[0] = waveOf(P[O1Wave]); c.wave[1] = waveOf(P[O2Wave]); c.wave[2] = waveOf(P[O3Wave]); c.wave[3] = waveOf(P[O4Wave]);
        c.sync2 = P[O2Sync] > 0.5f; c.sync4 = P[O4Sync] > 0.5f;
        c.lfo24 = P[LfoDest] > 0.5f; c.unison = P[Unison] > 0.5f; c.drift = P[Drift];
        c.uniSpread = (6.0f + 20.0f * c.drift) * 0.01f;
        c.fmode = std::clamp(static_cast<int>(std::lround(P[FiltMode] * 2.0f)), 0, 2);
        c.comp = P[BassComp] > 0.5f ? 1.0f : 0.0f; c.kbd = P[KbdTrk];
        c.velVca = P[VelVca] > 0.5f ? 1.0f : 0.0f; c.drive = P[MixDrive] > 0.5f;
        c.fA = attackSecOf(P[FAttack]) / 1.4663f; c.fD = decaySecOf(P[FDecay]) * 0.25f; c.fR = decaySecOf(P[FRelease]) * 0.25f;
        c.aA = attackSecOf(P[AAttack]) / 1.4663f; c.aD = decaySecOf(P[ADecay]) * 0.25f; c.aR = decaySecOf(P[ARelease]) * 0.25f;
        c.fS = P[FSustain]; c.aS = P[ASustain];
        const float gT = glideSecOf(P[Glide]);
        c.glideC = gT > 0.0f ? 1.0f - static_cast<float>(std::exp(-1.0 / (gT / 3.0 * sr))) : 1.0f;
        c.walkC = 1.0f - static_cast<float>(std::exp(-1.0 / (0.8 * sr)));
        c.invFade = 1.0f / static_cast<float>(fadeLen_);
        c.fadeC = 1.0f - static_cast<float>(std::exp(-1.0 / (0.004 * sr)));
        c.cutMax = static_cast<float>(std::min(fsOs * 0.40, 30000.0));

        const bool poly = structMode() == 3;
        const int S = slotsOf(structMode());
        c.voiceGain = poly ? 0.5f : 1.0f;   // a poly voice stacks four oscillators on one note
        int active = 0;
        for (int v = 0; v < (poly ? kMaxVoices : 1); ++v) {
            Voice& V = voices_[v];
            if (!V.active) { V.level = 0.0f; continue; }
            // Unison detune: oscillators sharing a note fan out around it.
            for (int k = 0; k < kOsc; ++k) V.det[k] = 0.0f;
            if (c.unison) {
                for (int k = 0; k < kOsc; ++k) {
                    const int key = poly ? 0 : slotNote_[slotOfOsc(k, S)];
                    int cnt = 0, idx = 0;
                    for (int j = 0; j < kOsc; ++j) {
                        const int kj = poly ? 0 : slotNote_[slotOfOsc(j, S)];
                        if (kj == key && (poly || V.gT[j] > 0.0f)) { if (j < k) ++idx; ++cnt; }
                    }
                    if (cnt > 1) V.det[k] = (2.0f * idx / static_cast<float>(cnt - 1) - 1.0f) * c.uniSpread;
                }
            }
            renderVoice(V, n, c);
            if (V.active) ++active;
        }
        if (poly) activeCount_ = active;
        else {
            int own = 0;
            if (voices_[0].active) for (int s = 0; s < S; ++s) if (slotNote_[s] >= 0 && !slotDbl_[s]) ++own;
            activeCount_ = voices_[0].active ? std::max(own, 1) : 0;
        }

        // ---- decimate the oversampled stereo sum ----
        if (os == 1) {
            for (int i = 0; i < n; ++i) { dsL_[i] = osL_[i]; dsR_[i] = osR_[i]; }
        } else if (os == 2) {
            dnSteep_[0].process_block(dsL_, osL_, n); dnSteep_[1].process_block(dsR_, osR_, n);
        } else {
            dnMid_[0].process_block(tmpL_, osL_, n * 2); dnMid_[1].process_block(tmpR_, osR_, n * 2);
            dnSteep_[0].process_block(dsL_, tmpL_, n); dnSteep_[1].process_block(dsR_, tmpR_, n);
        }

        // ---- analog delay + output ----
        processDelay(n, P);
        float pl = 0.0f, prr = 0.0f;
        for (int i = 0; i < n; ++i) {
            float l = dsL_[i] * gVol_[i], r = dsR_[i] * gVol_[i];
            if (!std::isfinite(l) || !std::isfinite(r)) { l = r = 0.0f; }
            out[i * 2] += l; out[i * 2 + 1] += r;
            pl = std::max(pl, std::fabs(l)); prr = std::max(prr, std::fabs(r));
        }
        const float dec = static_cast<float>(std::exp(-n / (0.3 * sr)));
        peakL_ = std::max(pl, peakL_ * dec); peakR_ = std::max(prr, peakR_ * dec);
    }

    // Global patch sources (the sequencer's, LFO, controllers).
    void fillGlobalSources(float* sv, float lfo, float mw, float at, float clk) const {
        using namespace consort;
        sv[SrcNone] = 0.0f; sv[SrcLfo] = lfo;
        sv[SrcSeqPitch] = seqRun_ ? seqPitchV_ : 0.0f; sv[SrcSeqGate] = seqGateV_; sv[SrcClock] = clk;
        sv[SrcAftertouch] = at; sv[SrcModWheel] = mw;
    }
    // Per-voice patch sources (previous sample).
    static void fillVoiceSources(float* sv, const Voice& V, const VoiceCtl& c, float nz) {
        using namespace consort;
        sv[SrcEnv1] = V.fenv; sv[SrcEnv2] = V.aenv;
        sv[SrcOsc1] = V.oPrev[0]; sv[SrcOsc2] = V.oPrev[1]; sv[SrcOsc3] = V.oPrev[2]; sv[SrcOsc4] = V.oPrev[3];
        sv[SrcNoise] = nz; sv[SrcFilt] = V.fPrev;
        sv[SrcKbdPitch] = static_cast<float>(V.note - 60) / 60.0f; sv[SrcKbdGate] = V.gate ? 1.0f : 0.0f; sv[SrcVelocity] = V.vel;
        sv[SrcAtten1] = c.atten1 * V.uIn[0]; sv[SrcAtten2] = c.atten2 * V.uIn[1]; sv[SrcSum] = V.uIn[2];
    }

    void renderVoice(Voice& V, int n, const VoiceCtl& c) {
        using namespace consort;
        const double sr = c.sr;
        const float fA = coefOf(c.fA, sr), fD = coefOf(c.fD, sr), fR = coefOf(c.fR, sr);
        const float aA = coefOf(c.aA, sr), aD = coefOf(c.aD, sr), aR = coefOf(c.aR, sr);
        const float velA = (1.0f - c.velVca * 0.85f * (1.0f - V.vel)) * c.voiceGain;
        const int os = c.os;
        const float kPiF = static_cast<float>(kPi);
        const bool g1p = c.patched[DstGate1], g2p = c.patched[DstGate2];
        const bool filtInP = c.patched[DstFiltIn], vcaInP = c.patched[DstVcaIn], extInP = c.patched[DstExtIn];
        const bool anyCab = c.nCab > 0;
        float* L = osL_; float* R = osR_;
        const float keyOct = (V.note - 60) * (1.0f / 12.0f);

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
                        startPoly(static_cast<int>(&V - voices_), pn, V.pendVel, V.pendGlide);
                    } else { V.active = false; V.gate = false; V.fst = V.ast = 0; V.fenv = V.aenv = 0.0f; V.level = 0.0f; return; }
                }
            }
            // -- gates: the keyboard, or a patched Gate in (rising edges retrigger while held) --
            if (V.retrig) {
                V.retrig = false;
                if (V.gate) { if (!g1p) V.fst = 1; if (!g2p) V.ast = 1; }
            }
            if (g1p) { const bool g = V.gate && V.gIn1 > 0.25f; if (g && !V.g1s) V.fst = 1; else if (!g && V.g1s && V.fst && V.fst != 3) V.fst = 3; V.g1s = g; }
            if (g2p) { const bool g = V.gate && V.gIn2 > 0.25f; if (g && !V.g2s) V.ast = 1; else if (!g && V.g2s && V.ast && V.ast != 3) V.ast = 3; V.g2s = g; }
            // -- envelopes (RC curves; the attack aims past 1 like the analog charge) --
            switch (V.fst) {
                case 1: V.fenv += (1.3f - V.fenv) * fA; if (V.fenv >= 1.0f) { V.fenv = 1.0f; V.fst = 2; } break;
                case 2: V.fenv += (c.fS - V.fenv) * fD; break;
                case 3: V.fenv -= V.fenv * fR; if (V.fenv < 1e-6f) { V.fenv = 0.0f; V.fst = 0; } break;
                default: break;
            }
            switch (V.ast) {
                case 1: V.aenv += (1.3f - V.aenv) * aA; if (V.aenv >= 1.0f) { V.aenv = 1.0f; V.ast = 2; } break;
                case 2: V.aenv += (c.aS - V.aenv) * aD; break;
                case 3: V.aenv -= V.aenv * aR; if (V.aenv < 3e-5f) { V.aenv = 0.0f; V.ast = 0; } break;
                default: break;
            }
            if (V.ast == 0 && !V.gate && V.fade == 0 && !V.retrig) {   // asleep: stop computing this voice
                V.active = false; V.fst = 0; V.level = 0.0f;
                for (int k = 0; k < kOsc; ++k) V.oPrev[k] = 0.0f;
                V.fPrev = V.vPrev = 0.0f;
                return;
            }
            // -- glide (per oscillator) + paraphonic slot fades --
            for (int k = 0; k < kOsc; ++k) {
                const float d = V.tgt[k] - V.pitch[k];
                if (d != 0.0f) {
                    if (V.rate[k] < 0.0f) { V.pitch[k] += d * c.glideC; if (std::fabs(d) < 1e-4f) V.pitch[k] = V.tgt[k]; }
                    else if (V.rate[k] > 0.0f) { const float st = std::min(std::fabs(d), V.rate[k]); V.pitch[k] += d > 0.0f ? st : -st; }
                    else V.pitch[k] = V.tgt[k];
                }
                V.g[k] += (V.gT[k] - V.g[k]) * c.fadeC;
            }
            // -- slow random walk (drift) --
            if (--V.wCount <= 0) {
                for (int k = 0; k <= kOsc; ++k) V.wt[k] = V.rng.bi();
                V.wCount = static_cast<int>(sr * (0.4 + 1.6 * V.rng.uni())) + 1;
            }
            for (int k = 0; k <= kOsc; ++k) V.w[k] += (V.wt[k] - V.w[k]) * c.walkC;

            // -- base-rate control targets --
            float pb[kOsc];
            for (int k = 0; k < kOsc; ++k) {
                const bool lfoOn = !c.lfo24 || k == 1 || k == 3;
                pb[k] = V.pitch[k] + c.oscOff[k] + V.det[k] + gPitch_[i] + (lfoOn ? gLfoP_[i] : gVib_[i])
                      + c.drift * (0.15f * V.oOff[k] + 0.1f * V.w[k]);
            }
            const float fe = V.fenv;
            const float cut = gCut_[i] + gFenv_[i] * fe + c.kbd * keyOct + c.drift * (0.25f * V.oCut + 0.1f * V.w[kOsc]);
            const float ca = cut, cb = cut + gSpace_[i];
            const float amp = V.aenv * velA * fadeG;
            const float duty = gDuty_[i];
            if (V.fresh) { for (int k = 0; k < kOsc; ++k) V.pP[k] = pb[k]; V.pCa = ca; V.pCb = cb; V.pAmp = amp; V.pDuty = duty; V.fresh = false; }
            const float k0 = gK_[i];
            const float mix[kOsc] = { gMix_[0][i] * V.g[0], gMix_[1][i] * V.g[1], gMix_[2][i] * V.g[2], gMix_[3][i] * V.g[3] };
            const float nzL = gNzL_[i], extL = gExt_[i];
            const float* nz = gNoise_ + i * os;
            float* lo = L + i * os; float* ro = R + i * os;
            float sv[kSrcN];
            if (anyCab) fillGlobalSources(sv, gLfo_[i], gMw_[i], gAt_[i], gClk_[i]);

            for (int s = 0; s < os; ++s) {
                const float t = static_cast<float>(s + 1) * c.invOs;
                // -- patch (sources from the previous sample) --
                float d[kDstN] = {};
                if (anyCab) {
                    fillVoiceSources(sv, V, c, nz[s]);
                    for (int q = 0; q < c.nCab; ++q) d[c.cab[q].dst] += c.cab[q].amt * sv[c.cab[q].src];
                    V.uIn[0] = d[DstAtten1]; V.uIn[1] = d[DstAtten2]; V.uIn[2] = d[DstSum];
                    V.gIn1 = d[DstGate1]; V.gIn2 = d[DstGate2];
                }
                // -- oscillators: 1 → 2 (sync), 3 → 4 (sync) --
                const float du = std::clamp(V.pDuty + (duty - V.pDuty) * t + d[DstPwm], 0.02f, 0.98f);
                float o[kOsc], wrap[kOsc];
                for (int k = 0; k < kOsc; ++k) {
                    const float note = V.pP[k] + (pb[k] - V.pP[k]) * t + d[DstPitchAll] + d[DstPitch1 + k];
                    const double dt = std::min(440.0 * pentad::exp2f((note - 69.0f) * (1.0f / 12.0f)) * c.invFsOs, 0.45);
                    const float sy = (k == 1 && c.sync2) ? wrap[0] : (k == 3 && c.sync4) ? wrap[2] : -1.0f;
                    o[k] = oscStep(V.osc[k], dt, c.wave[k], du, sy, wrap[k]);
                }
                // -- mixer (+ noise, EXT: normalled to the VCA output → feedback) --
                const float ext = extInP ? d[DstExtIn] : V.vPrev;
                const float sum = mix[0] * o[0] + mix[1] * o[1] + mix[2] * o[2] + mix[3] * o[3] + nzL * nz[s] + extL * ext;
                float x;
                if (filtInP) x = d[DstFiltIn];
                else x = c.drive ? V.sat.process(sum * 0.45 * 2.4) * (1.0f / 1.3f) : V.sat.process(sum * 0.45 * 0.7) * (1.0f / 0.7f);
                // -- dual ladder --
                const float kk = std::clamp(k0 + d[DstReso] * 4.3f, 0.0f, 4.6f);
                const float cA = V.pCa + (ca - V.pCa) * t + d[DstCut1];
                const float cB = V.pCb + (cb - V.pCb) * t + d[DstCut2];
                const float gA = pentad::tanPrewarp(kPiF * std::clamp(pentad::exp2f(cA), 8.0f, c.cutMax) * static_cast<float>(c.invFsOs));
                const float gB = pentad::tanPrewarp(kPiF * std::clamp(pentad::exp2f(cB), 8.0f, c.cutMax) * static_cast<float>(c.invFsOs));
                float yl, yr;
                if (c.fmode == 0) { yl = yr = V.fb.tick(V.fa.tick(x, gA, kk, c.comp, true), gB, kk, c.comp, false); }
                else { yl = V.fa.tick(x, gA, kk, c.comp, c.fmode == 2); yr = V.fb.tick(x, gB, kk, c.comp, false); }
                // -- VCA (Env 2 × CV; its input normalled to the filter) --
                const float a = (V.pAmp + (amp - V.pAmp) * t) * std::clamp(1.0f + d[DstVcaCv], 0.0f, 3.0f);
                float vl = vcaInP ? d[DstVcaIn] : yl, vr = vcaInP ? d[DstVcaIn] : yr;
                vl = std::clamp(vl, -6.0f, 6.0f) * a; vr = std::clamp(vr, -6.0f, 6.0f) * a;
                vl -= 0.008f * vl * vl * vl; vr -= 0.008f * vr * vr * vr;
                lo[s] += vl; ro[s] += vr;
                for (int k = 0; k < kOsc; ++k) V.oPrev[k] = o[k];
                V.fPrev = 0.5f * (yl + yr);
                V.vPrev = std::clamp(0.5f * (vl + vr) * 2.0f, -2.0f, 2.0f);
            }
            for (int k = 0; k < kOsc; ++k) V.pP[k] = pb[k];
            V.pCa = ca; V.pCb = cb; V.pAmp = amp; V.pDuty = duty;
            V.cutA = ca; V.cutB = cb;
        }
        V.level = V.aenv * velA * (V.fade > 0 ? V.fade * c.invFade : 1.0f);
    }

    // ===================================================================================
    // Bucket-brigade delay (stereo; line rate ≤ 50 kHz, decimated at higher host rates)
    // ===================================================================================
    void setupDelayRate() {
        dlyD_ = sampleRate_ > 100000.0 ? 4 : sampleRate_ > 50000.0 ? 2 : 1;
        dlyRate_ = sampleRate_ / dlyD_;
        dlyPhase_ = 0; dlyAccL_ = dlyAccR_ = 0.0f;
    }
    void clearDelay() {
        if (dlyDirty_ && dlyBuf_) std::memset(dlyBuf_.get(), 0, sizeof(float) * static_cast<size_t>(dlyN_) * 4);
        dlyDirty_ = false;
        dlyW_ = 0;
        for (int c = 0; c < 2; ++c) {
            dlyY_[c] = 0.0f; dlyPrev_[c] = dlyCur_[c] = 0.0f; envIn_[c] = 1e-6f;
            pre_[c].clear(); post_[c].clear();
            dlyTimeCur_[c] = -1.0f;
        }
        setupDelayRate();
    }
    float readGain(int ch, float delay) const {   // the compander gain stored beside the signal
        const float* b = dlyBuf_.get() + (2 + ch) * dlyN_;
        const float rp = static_cast<float>(dlyW_) - delay;
        const float fl = std::floor(rp);
        const int i1 = static_cast<int>(fl), mask = dlyN_ - 1;
        const float y1 = b[i1 & mask], y2 = b[(i1 + 1) & mask];
        return y1 + (y2 - y1) * (rp - fl);
    }
    float readLine(int ch, float delay) const {
        const float* b = dlyBuf_.get() + ch * dlyN_;
        const float rp = static_cast<float>(dlyW_) - delay;
        const float fl = std::floor(rp);
        const float f = rp - fl;
        const int i1 = static_cast<int>(fl);
        const int mask = dlyN_ - 1;
        const float y0 = b[(i1 - 1) & mask], y1 = b[i1 & mask], y2 = b[(i1 + 1) & mask], y3 = b[(i1 + 2) & mask];
        const float c1 = 0.5f * (y2 - y0), c2 = y0 - 2.5f * y1 + 2.0f * y2 - 0.5f * y3, c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
        return ((c3 * f + c2) * f + c1) * f + y1;
    }
    void processDelay(int n, const float* P) {
        if (!dlyBuf_) return;
        const bool digital = P[DlyDigital] > 0.5f, ping = P[DlyPing] > 0.5f;
        const float baseT = dlySecOf(P[DlyTime]), spc = dlySpacingOf(P[DlySpacing]);
        const float maxT = static_cast<float>((dlyN_ - 8) / dlyRate_);
        const float maxTime = std::min(2.0f, maxT);
        const float slew = 1.0f - static_cast<float>(std::exp(-1.0 / (0.12 * dlyRate_)));
        // BBD bandwidth from the virtual clock (8192 stages): longer delays are darker.
        float gPre[2], kq = 1.2f;
        for (int ch = 0; ch < 2; ++ch) {
            const float tsec = dlyTimeCur_[ch] > 0.0f ? dlyTimeCur_[ch] / static_cast<float>(dlyRate_) : baseT;
            const float fclk = 8192.0f / (2.0f * std::max(tsec, 0.005f));
            const float bw = std::clamp(0.42f * fclk, 600.0f, digital ? 16000.0f : 11000.0f);
            gPre[ch] = std::tan(static_cast<float>(kPi) * std::min(bw, static_cast<float>(dlyRate_) * 0.45f) / static_cast<float>(dlyRate_));
        }
        const float envA = 1.0f - static_cast<float>(std::exp(-1.0 / (0.002 * dlyRate_)));
        const float envR = 1.0f - static_cast<float>(std::exp(-1.0 / (0.030 * dlyRate_)));
        const int D = dlyD_;
        for (int i = 0; i < n; ++i) {
            dlyAccL_ += dsL_[i]; dlyAccR_ += dsR_[i];
            if (++dlyPhase_ >= D) {
                dlyPhase_ = 0;
                const float xl = dlyAccL_ / D, xr = dlyAccR_ / D;
                dlyAccL_ = dlyAccR_ = 0.0f;
                const float tl = std::clamp(baseT * pentad::exp2f(std::clamp(gDlyT_[i], -4.0f, 4.0f)), 0.005f, maxTime);
                const float tr = std::clamp(tl + spc, 0.005f, maxTime);
                const float tgt[2] = { tl * static_cast<float>(dlyRate_), tr * static_cast<float>(dlyRate_) };
                const float fb = gDlyFb_[i];
                float y[2];
                for (int ch = 0; ch < 2; ++ch) {
                    if (dlyTimeCur_[ch] < 0.0f) dlyTimeCur_[ch] = tgt[ch];
                    dlyTimeCur_[ch] += (tgt[ch] - dlyTimeCur_[ch]) * slew;
                    const float dl = std::clamp(dlyTimeCur_[ch], 2.0f, static_cast<float>(dlyN_ - 4));
                    const float yc = readLine(ch, dl);
                    if (digital) y[ch] = yc;
                    else y[ch] = post_[ch].tick(yc * readGain(ch, dl), gPre[ch], kq);   // expander + reconstruction filter
                    dlyY_[ch] = y[ch];
                }
                float inL, inR;
                if (ping) { inL = 0.5f * (xl + xr) + fb * y[1]; inR = fb * y[0]; }
                else { inL = xl + fb * y[0]; inR = xr + fb * y[1]; }
                const float in[2] = { inL, inR };
                float* b = dlyBuf_.get();
                for (int ch = 0; ch < 2; ++ch) {
                    float wv = in[ch];
                    wv = wv / (1.0f + std::fabs(wv) * 0.35f);                 // overload
                    float gi = 1.0f;
                    if (!digital) {   // anti-alias filter + 2:1 compressor + hiss (the expander undoes the gain, not the hiss)
                        wv = pre_[ch].tick(wv, gPre[ch], kq);
                        const float a = std::fabs(wv);
                        envIn_[ch] += (a - envIn_[ch]) * (a > envIn_[ch] ? envA : envR);
                        const float sq = std::sqrt(std::max(envIn_[ch], 1e-6f));
                        wv = wv * (0.25f / sq) + 2.5e-4f * dlyRng_.bi();
                        gi = sq * 4.0f;
                    }
                    if (!std::isfinite(wv)) wv = 0.0f;
                    b[ch * dlyN_ + dlyW_] = wv;
                    b[(2 + ch) * dlyN_ + dlyW_] = gi;
                    if (wv != 0.0f) dlyDirty_ = true;
                }
                dlyW_ = (dlyW_ + 1) & (dlyN_ - 1);
                dlyPrev_[0] = dlyCur_[0]; dlyPrev_[1] = dlyCur_[1];
                dlyCur_[0] = y[0]; dlyCur_[1] = y[1];
            }
            const float fr = D > 1 ? static_cast<float>(dlyPhase_ + 1) / D : 1.0f;
            const float wl = dlyPrev_[0] + (dlyCur_[0] - dlyPrev_[0]) * fr;
            const float wr = dlyPrev_[1] + (dlyCur_[1] - dlyPrev_[1]) * fr;
            const float mx = gDlyMix_[i];
            dsL_[i] += mx * wl; dsR_[i] += mx * wr;
        }
    }

    void publishTelemetry() {
        const int st = structMode();
        tele_[ScMode].store(static_cast<float>(st), std::memory_order_relaxed);
        tele_[ScActive].store(static_cast<float>(activeCount_), std::memory_order_relaxed);
        tele_[ScPeakL].store(peakL_, std::memory_order_relaxed);
        tele_[ScPeakR].store(peakR_, std::memory_order_relaxed);
        tele_[ScCpu].store(static_cast<float>(cpu_), std::memory_order_relaxed);
        tele_[ScOs].store(static_cast<float>(osCur_), std::memory_order_relaxed);
        tele_[ScRate].store(static_cast<float>(sampleRate_), std::memory_order_relaxed);
        tele_[ScStep].store(seqRun_ ? static_cast<float>(curIdx_) : -1.0f, std::memory_order_relaxed);
        tele_[ScBpm].store(tSpb_ > 1.0 ? static_cast<float>(60.0 * sampleRate_ / tSpb_) : 120.0f, std::memory_order_relaxed);
        tele_[ScLfo].store(lfoVal_, std::memory_order_relaxed);
        const Voice& F = voices_[focus_];
        tele_[ScEnv1].store(F.active ? F.fenv : 0.0f, std::memory_order_relaxed);
        tele_[ScEnv2].store(F.active ? F.aenv : 0.0f, std::memory_order_relaxed);
        if (st == 3) {   // the four most recent sounding voices
            int idx[kOsc] = { -1, -1, -1, -1 };
            for (int v = 0; v < kMaxVoices; ++v) {
                if (!voices_[v].active) continue;
                for (int j = 0; j < kOsc; ++j) {
                    if (idx[j] < 0 || voices_[v].order > voices_[idx[j]].order) {
                        for (int q = kOsc - 1; q > j; --q) idx[q] = idx[q - 1];
                        idx[j] = v; break;
                    }
                }
            }
            for (int j = 0; j < kOsc; ++j) {
                tele_[ScOscNote + j].store(idx[j] >= 0 ? static_cast<float>(voices_[idx[j]].note) : -1.0f, std::memory_order_relaxed);
                tele_[ScOscLvl + j].store(idx[j] >= 0 ? voices_[idx[j]].level : 0.0f, std::memory_order_relaxed);
            }
        } else {
            const Voice& V = voices_[0];
            const int S = slotsOf(st);
            for (int k = 0; k < kOsc; ++k) {
                const int note = slotNote_[slotOfOsc(k, S)];
                const bool on = V.active && note >= 0 && V.gT[k] > 0.0f;
                tele_[ScOscNote + k].store(on ? static_cast<float>(note) : -1.0f, std::memory_order_relaxed);
                tele_[ScOscLvl + k].store(on ? V.aenv * V.g[k] : 0.0f, std::memory_order_relaxed);
            }
        }
        tele_[ScCutA].store(pentad::exp2f(F.cutA), std::memory_order_relaxed);
        tele_[ScCutB].store(pentad::exp2f(F.cutB), std::memory_order_relaxed);
        const float r = static_cast<float>(dlyRate_);
        tele_[ScDlyL].store(dlyTimeCur_[0] > 0.0f ? dlyTimeCur_[0] / r * 1000.0f : 0.0f, std::memory_order_relaxed);
        tele_[ScDlyR].store(dlyTimeCur_[1] > 0.0f ? dlyTimeCur_[1] / r * 1000.0f : 0.0f, std::memory_order_relaxed);
        tele_[ScSeqNote].store(static_cast<float>(seqNote_), std::memory_order_relaxed);
    }

    // ---- state ------------------------------------------------------------------------
    enum Sm { SmPitch = 0, SmCut, SmK, SmSpace, SmFenv, SmM1, SmM2, SmM3, SmM4, SmNz, SmExt,
              SmPw, SmVol, SmMw, SmAt, SmLfoP, SmLfoC, SmLfoPwm, SmDlyMix, SmDlyFb, kSm };

    double sampleRate_ = 44100.0;
    std::atomic<double> pendingRate_{ 44100.0 };
    Voice voices_[kMaxVoices];
    int focus_ = 0;
    // keys (physical), the voice layer's held notes, paraphonic slots
    int keys_[128] = { 0 }; int keyN_ = 0;
    int pHeld_[128] = { 0 }; int pHeldN_ = 0;
    int slotNote_[4] = { -1, -1, -1, -1 }; bool slotDbl_[4] = { false, false, false, false };
    int rr_ = 0; uint64_t orderCtr_ = 0;
    int lastStruct_ = 2, lastSeq_ = 0, fadeLen_ = 176, osCur_ = 2, activeCount_ = 0;
    // sequencer
    int phys_[128] = { 0 }; int physN_ = 0; int lat_[128] = { 0 }; int latN_ = 0;
    bool seqRun_ = false; int seqNote_ = -1, seqBase_ = 60, curIdx_ = -1, curType_ = 3, curSub_ = 0, curPitch_ = 60, arpPos_ = -1, clk_ = 0;
    uint32_t arpCtr_ = 0;
    long long curN_ = LLONG_MIN;
    float seqVel_ = 0.8f, seqPitchV_ = 0.0f, seqGateV_ = 0.0f;
    double seqIntBeat_ = 0.0, nextEvBeat_ = -1e300, offBeat_ = 1e300;
    // transport
    double tBeat_ = 0.0, tSpb_ = 0.0, tPos_ = 0.0, tFrames_ = 0.0; bool tPlaying_ = false, tFresh_ = true;
    // LFO / noise
    double lfoPh_ = 0.0; float lfoRnd0_ = 0.0f, lfoRnd1_ = 0.0f, lfoVal_ = 0.0f, noiseLast_ = 0.0f;
    pentad::Rng noiseRng_, lfoRng_, dlyRng_;
    float pk0_ = 0, pk1_ = 0, pk2_ = 0;
    float sm_[kSm] = {}; bool smoothInit_ = false;
    float gPitch_[kChunk], gLfoP_[kChunk], gVib_[kChunk], gCut_[kChunk], gK_[kChunk], gSpace_[kChunk], gFenv_[kChunk];
    float gMix_[kOsc][kChunk], gNzL_[kChunk], gExt_[kChunk], gDuty_[kChunk], gVol_[kChunk], gMw_[kChunk], gAt_[kChunk];
    float gLfo_[kChunk], gClk_[kChunk], gDlyT_[kChunk], gDlyFb_[kChunk], gDlyMix_[kChunk];
    float gNoise_[kChunk * kMaxOs];
    float osL_[kChunk * kMaxOs], osR_[kChunk * kMaxOs], tmpL_[kChunk * 2], tmpR_[kChunk * 2], dsL_[kChunk], dsR_[kChunk];
    hiir::Downsampler2xFpu<12> dnSteep_[2];
    hiir::Downsampler2xFpu<5> dnMid_[2];
    // delay
    std::unique_ptr<float, FreeDel> dlyBuf_;
    int dlyN_ = kDlyLen, dlyW_ = 0, dlyD_ = 1, dlyPhase_ = 0;
    double dlyRate_ = 44100.0;
    bool dlyDirty_ = false;
    float dlyAccL_ = 0, dlyAccR_ = 0, dlyY_[2] = { 0, 0 }, dlyPrev_[2] = { 0, 0 }, dlyCur_[2] = { 0, 0 };
    float dlyTimeCur_[2] = { -1, -1 }, envIn_[2] = { 1e-6f, 1e-6f };
    consort::SvfLp pre_[2], post_[2];
    float peakL_ = 0, peakR_ = 0;
    double cpu_ = 0.0;
    mutable std::atomic<float> tele_[kScopeN] = {};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
