// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// CV modulation (Phase 3, Modular editor). A Modulator is a modulation *source*
// living on a track (Phase 3 ships the LFO); a CvLink wires one to a built-in
// device parameter on the same track with a signed depth and a combine mode.
//
// Modulators are deliberately *stateless*: an LFO's output is a pure function of
// the transport (the beat when tempo-synced, elapsed seconds when free-running),
// so there is no runtime phase to advance and the struct copies trivially across
// graph snapshots. Config fields are atomics so the UI can tweak rate/depth/etc.
// without republishing the graph (like device params).

#pragma once

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

// LFO, envelope follower (AR, follows audio level), MIDI→CV (velocity/gate/note),
// and ADSR (gated by MIDI notes). Env / MIDI / ADSR output the smoothed `envValue`;
// the LFO is stateless.
// Macro (kind 4): a manual knob; output = its `depth` value. Math (kind 5): two CV
// inputs (other modulators) combined by an op.
// Scope (kind 6): a monitor — samples a CV input into a rolling history buffer for
// the UI to draw; passes the input through as its output.
enum class ModulatorKind : int32_t { LFO = 0, EnvFollower = 1, MidiToCv = 2, Adsr = 3, Macro = 4, Math = 5, Scope = 6 };

inline constexpr int kScopeLen = 128;   // CV oscilloscope history length
enum class CvMode : int32_t { Add = 0, Multiply = 1, Override = 2 };
enum class MathOp : int32_t { Add = 0, Subtract = 1, Multiply = 2, Invert = 3, Scale = 4, Min = 5, Max = 6 };

// Field selector for the generic modulator get/set (keeps the C ABI small).
enum class ModField : int32_t {
    Waveform = 0, TempoSync = 1, RateHz = 2, RateSyncBeats = 3, Depth = 4, Phase = 5,
    Attack = 6, Release = 7,   // env follower + ADSR (ms)
    Decay = 8, Sustain = 9,    // ADSR
    InputA = 10, InputB = 11, MathOffset = 12,   // Math (op = Waveform field; gain = Depth field)
};

struct Modulator {
    int32_t id = 0;
    ModulatorKind kind = ModulatorKind::LFO;
    std::atomic<int32_t> waveform{0};        // 0 sine · 1 tri · 2 saw · 3 square · 4 random (S&H)
    std::atomic<int32_t> tempoSync{1};       // 1 = beats/cycle, 0 = free Hz
    std::atomic<float>   rateHz{1.0f};        // free-run rate
    std::atomic<float>   rateSyncBeats{0.5f}; // beats per cycle when synced (0.5 = 1/8)
    std::atomic<float>   depth{1.0f};         // 0..1 output amplitude
    std::atomic<float>   phase01{0.0f};       // 0..1 phase offset
    std::atomic<float>   attackMs{10.0f};     // env follower / ADSR rise time
    std::atomic<float>   releaseMs{120.0f};   // env follower / ADSR fall time
    std::atomic<float>   decayMs{200.0f};     // ADSR decay time
    std::atomic<float>   sustain{0.7f};       // ADSR sustain level 0..1
    std::atomic<float>   envValue{0.0f};      // runtime: current envelope 0..1
    std::atomic<int32_t> adsrStage{0};        // runtime: 0 idle, 1 A, 2 D, 3 S, 4 R
    std::atomic<int32_t> prevGate{0};         // runtime: last gate (edge detection)
    std::atomic<int32_t> inputA{-1};          // Math: source modulator ids
    std::atomic<int32_t> inputB{-1};
    std::atomic<float>   mathOffset{0.0f};    // Math: Scale offset
    std::atomic<float>   outValue{0.0f};      // runtime: this block's output (read by links + Math)
    float                scopeBuf[kScopeLen] = {};   // Scope: rolling history (audio-thread write)
    std::atomic<int32_t> scopeWrite{0};

    void pushScope(float v) {
        const int32_t w = scopeWrite.load(std::memory_order_relaxed);
        scopeBuf[w & (kScopeLen - 1)] = v;
        scopeWrite.store(w + 1, std::memory_order_relaxed);
    }
    // Copy the history oldest→newest into `out`; returns the count written.
    int32_t readScope(float* out, int32_t cap) const {
        const int32_t w = scopeWrite.load(std::memory_order_relaxed);
        const int32_t n = cap < kScopeLen ? cap : kScopeLen;
        for (int32_t i = 0; i < n; ++i) out[i] = scopeBuf[(w + i) & (kScopeLen - 1)];
        return n;
    }

    Modulator() = default;
    Modulator(const Modulator& o) { *this = o; }
    Modulator& operator=(const Modulator& o) {
        id = o.id; kind = o.kind;
        waveform.store(o.waveform.load(std::memory_order_relaxed), std::memory_order_relaxed);
        tempoSync.store(o.tempoSync.load(std::memory_order_relaxed), std::memory_order_relaxed);
        rateHz.store(o.rateHz.load(std::memory_order_relaxed), std::memory_order_relaxed);
        rateSyncBeats.store(o.rateSyncBeats.load(std::memory_order_relaxed), std::memory_order_relaxed);
        depth.store(o.depth.load(std::memory_order_relaxed), std::memory_order_relaxed);
        phase01.store(o.phase01.load(std::memory_order_relaxed), std::memory_order_relaxed);
        attackMs.store(o.attackMs.load(std::memory_order_relaxed), std::memory_order_relaxed);
        releaseMs.store(o.releaseMs.load(std::memory_order_relaxed), std::memory_order_relaxed);
        decayMs.store(o.decayMs.load(std::memory_order_relaxed), std::memory_order_relaxed);
        sustain.store(o.sustain.load(std::memory_order_relaxed), std::memory_order_relaxed);
        envValue.store(o.envValue.load(std::memory_order_relaxed), std::memory_order_relaxed);
        adsrStage.store(o.adsrStage.load(std::memory_order_relaxed), std::memory_order_relaxed);
        prevGate.store(o.prevGate.load(std::memory_order_relaxed), std::memory_order_relaxed);
        inputA.store(o.inputA.load(std::memory_order_relaxed), std::memory_order_relaxed);
        inputB.store(o.inputB.load(std::memory_order_relaxed), std::memory_order_relaxed);
        mathOffset.store(o.mathOffset.load(std::memory_order_relaxed), std::memory_order_relaxed);
        outValue.store(o.outValue.load(std::memory_order_relaxed), std::memory_order_relaxed);
        for (int i = 0; i < kScopeLen; ++i) scopeBuf[i] = o.scopeBuf[i];
        scopeWrite.store(o.scopeWrite.load(std::memory_order_relaxed), std::memory_order_relaxed);
        return *this;
    }

    float get(ModField f) const {
        switch (f) {
            case ModField::Waveform:      return (float)waveform.load(std::memory_order_relaxed);
            case ModField::TempoSync:     return (float)tempoSync.load(std::memory_order_relaxed);
            case ModField::RateHz:        return rateHz.load(std::memory_order_relaxed);
            case ModField::RateSyncBeats: return rateSyncBeats.load(std::memory_order_relaxed);
            case ModField::Depth:         return depth.load(std::memory_order_relaxed);
            case ModField::Phase:         return phase01.load(std::memory_order_relaxed);
            case ModField::Attack:        return attackMs.load(std::memory_order_relaxed);
            case ModField::Release:       return releaseMs.load(std::memory_order_relaxed);
            case ModField::Decay:         return decayMs.load(std::memory_order_relaxed);
            case ModField::Sustain:       return sustain.load(std::memory_order_relaxed);
            case ModField::InputA:        return (float)inputA.load(std::memory_order_relaxed);
            case ModField::InputB:        return (float)inputB.load(std::memory_order_relaxed);
            case ModField::MathOffset:    return mathOffset.load(std::memory_order_relaxed);
        }
        return 0.0f;
    }
    void set(ModField f, float v) {
        switch (f) {
            case ModField::Waveform:      waveform.store((int32_t)std::lround(v), std::memory_order_relaxed); break;
            case ModField::TempoSync:     tempoSync.store(v > 0.5f ? 1 : 0, std::memory_order_relaxed); break;
            case ModField::RateHz:        rateHz.store(v, std::memory_order_relaxed); break;
            case ModField::RateSyncBeats: rateSyncBeats.store(v, std::memory_order_relaxed); break;
            case ModField::Depth:         depth.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); break;
            case ModField::Phase:         phase01.store(v, std::memory_order_relaxed); break;
            case ModField::Attack:        attackMs.store(std::max(0.1f, v), std::memory_order_relaxed); break;
            case ModField::Release:       releaseMs.store(std::max(1.0f, v), std::memory_order_relaxed); break;
            case ModField::Decay:         decayMs.store(std::max(1.0f, v), std::memory_order_relaxed); break;
            case ModField::Sustain:       sustain.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); break;
            case ModField::InputA:        inputA.store((int32_t)std::lround(v), std::memory_order_relaxed); break;
            case ModField::InputB:        inputB.store((int32_t)std::lround(v), std::memory_order_relaxed); break;
            case ModField::MathOffset:    mathOffset.store(v, std::memory_order_relaxed); break;
        }
    }

    // Math: combine two input values by the op (op = waveform field, gain = depth field).
    float mathEval(float a, float b) const {
        const float gain = depth.load(std::memory_order_relaxed), off = mathOffset.load(std::memory_order_relaxed);
        switch (waveform.load(std::memory_order_relaxed)) {
            case 1: return a - b;                 // Subtract
            case 2: return a * b;                 // Multiply
            case 3: return -a;                    // Invert
            case 4: return a * gain + off;        // Scale (offset + gain)
            case 5: return std::min(a, b);        // Min
            case 6: return std::max(a, b);        // Max
            default: return a + b;                // Add
        }
    }

    // Envelope follower: smooth the source level toward `envValue` with attack/release
    // (called once per audio block from the engine; `dt` = block seconds).
    void updateEnv(float level, double dt) {
        const float target = std::clamp(level, 0.0f, 1.0f);
        const float cur = envValue.load(std::memory_order_relaxed);
        const float tc = (target > cur ? attackMs.load(std::memory_order_relaxed)
                                       : releaseMs.load(std::memory_order_relaxed)) * 0.001f;
        const float a = tc <= 1e-6f ? 1.0f : (float)(1.0 - std::exp(-dt / tc));
        envValue.store(cur + (target - cur) * a, std::memory_order_relaxed);
    }

    // ADSR: a gated A/D/S/R envelope. `gate` = a note is held; on its rising edge the
    // envelope attacks, on its falling edge it releases. Exponential stage approaches.
    void updateAdsr(bool gate, double dt) {
        int32_t stage = adsrStage.load(std::memory_order_relaxed);
        const bool pg = prevGate.load(std::memory_order_relaxed) != 0;
        if (gate && !pg) stage = 1;        // note-on  → attack
        else if (!gate && pg) stage = 4;   // note-off → release
        prevGate.store(gate ? 1 : 0, std::memory_order_relaxed);

        auto coeff = [dt](float ms) { const double tc = ms * 0.001; return tc <= 1e-6 ? 1.0f : (float)(1.0 - std::exp(-dt / tc)); };
        float v = envValue.load(std::memory_order_relaxed);
        const float s = sustain.load(std::memory_order_relaxed);
        switch (stage) {
            case 1: v += (1.0f - v) * coeff(attackMs.load(std::memory_order_relaxed));  if (v >= 0.99f) stage = 2; break;   // attack → 1
            case 2: v += (s - v)    * coeff(decayMs.load(std::memory_order_relaxed));    if (std::fabs(v - s) < 1e-3f) stage = 3; break; // decay → sustain
            case 3: v = s; break;                                                                                                        // sustain
            case 4: v += (0.0f - v) * coeff(releaseMs.load(std::memory_order_relaxed));  if (v < 1e-3f) { v = 0.0f; stage = 0; } break;  // release → 0
            default: break;                                                                                                              // idle
        }
        envValue.store(v, std::memory_order_relaxed);
        adsrStage.store(stage, std::memory_order_relaxed);
    }

    // LFO: bipolar output in [-depth, +depth]. Env / MIDI→CV / ADSR: unipolar
    // envValue*depth (they update envValue outside eval).
    float eval(double beat, double timeSec) const {
        if (kind == ModulatorKind::Math || kind == ModulatorKind::Scope)
            return outValue.load(std::memory_order_relaxed);   // precomputed each block (Scope = pass-through)
        if (kind == ModulatorKind::Macro)
            return depth.load(std::memory_order_relaxed);   // manual knob value 0..1
        if (kind == ModulatorKind::EnvFollower || kind == ModulatorKind::MidiToCv || kind == ModulatorKind::Adsr)
            return envValue.load(std::memory_order_relaxed) * depth.load(std::memory_order_relaxed);
        double cyc;
        if (tempoSync.load(std::memory_order_relaxed)) {
            const float rb = rateSyncBeats.load(std::memory_order_relaxed);
            cyc = rb > 1e-6f ? beat / (double)rb : 0.0;
        } else {
            cyc = timeSec * (double)rateHz.load(std::memory_order_relaxed);
        }
        double p = cyc + (double)phase01.load(std::memory_order_relaxed);
        p -= std::floor(p);                       // [0,1)
        float w;
        switch (waveform.load(std::memory_order_relaxed)) {
            case 1: w = 1.0f - 4.0f * std::fabs((float)p - 0.5f); break;   // triangle
            case 2: w = 2.0f * (float)p - 1.0f; break;                     // saw
            case 3: w = p < 0.5 ? 1.0f : -1.0f; break;                     // square
            case 4: {                                                      // random (sample & hold)
                uint32_t n = (uint32_t)(int64_t)std::floor(cyc);
                n ^= n >> 16; n *= 0x7feb352dU; n ^= n >> 15; n *= 0x846ca68bU; n ^= n >> 16;
                w = (float)((double)n / 4294967295.0) * 2.0f - 1.0f; break;
            }
            default: w = std::sin((float)p * 6.28318530718f); break;       // sine
        }
        return w * depth.load(std::memory_order_relaxed);
    }
};

// CV source kind for a link: a modulator, or another parameter's value (param → param
// cross-automation). Param sources live on the link's owner track (same-track for now).
enum class CvSourceKind : int32_t { Modulator = 0, Param = 1 };
// What the link modulates: a built-in insert-effect param, the instrument (plugin
// param, targetDevice = -1), or a MIDI-FX param (targetDevice = chain index).
enum class CvTargetKind : int32_t { Device = 0, Instrument = 1, MidiFx = 2 };

struct CvLink {
    int32_t sourceKind = 0;                  // CvSourceKind
    int32_t sourceModId = 0;                 // Modulator source
    int32_t sourceDevice = -1;               // Param source: device + param on the owner track
    int32_t sourceParam = -1;
    int32_t targetKind = 0;                  // CvTargetKind
    int32_t targetTrack = -1;                // target track id (-1 = the owner track)
    int32_t targetDevice = -1;               // insert-effect / MIDI-FX index on the target track
    int32_t targetParam = -1;
    std::atomic<float>   depth{1.0f};        // -1..1
    std::atomic<int32_t> mode{0};            // CvMode
    // The target param's *base* value (the centre the modulation swings around). The
    // device atomic holds the live modulated value; the UI edits this base instead.
    std::atomic<float>   base{0.0f};

    CvLink() = default;
    CvLink(const CvLink& o) { *this = o; }
    CvLink& operator=(const CvLink& o) {
        sourceKind = o.sourceKind; sourceModId = o.sourceModId;
        sourceDevice = o.sourceDevice; sourceParam = o.sourceParam;
        targetKind = o.targetKind;
        targetTrack = o.targetTrack; targetDevice = o.targetDevice; targetParam = o.targetParam;
        depth.store(o.depth.load(std::memory_order_relaxed), std::memory_order_relaxed);
        mode.store(o.mode.load(std::memory_order_relaxed), std::memory_order_relaxed);
        base.store(o.base.load(std::memory_order_relaxed), std::memory_order_relaxed);
        return *this;
    }
};

} // namespace nota
