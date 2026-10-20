// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
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

inline constexpr int kScopeLen = 128; // CV oscilloscope history length
enum class CvMode : int32_t { Add = 0, Multiply = 1, Override = 2 };
enum class MathOp : int32_t { Add = 0, Subtract = 1, Multiply = 2, Invert = 3, Scale = 4, Min = 5, Max = 6 };

// Field selector for the generic modulator get/set (keeps the C ABI small).
enum class ModField : int32_t {
    Waveform = 0,
    TempoSync = 1,
    RateHz = 2,
    RateSyncBeats = 3,
    Depth = 4,
    Phase = 5,
    Attack = 6,
    Release = 7, // env follower + ADSR (ms)
    Decay = 8,
    Sustain = 9, // ADSR
    InputA = 10,
    InputB = 11,
    MathOffset = 12, // Math (op = Waveform field; gain = Depth field)
};

struct Modulator {
    int32_t id = 0;
    ModulatorKind kind = ModulatorKind::LFO;
    std::atomic<int32_t> waveform{0};       // 0 sine · 1 tri · 2 saw · 3 square · 4 random (S&H)
    std::atomic<int32_t> tempoSync{1};      // 1 = beats/cycle, 0 = free Hz
    std::atomic<float> rateHz{1.0f};        // free-run rate
    std::atomic<float> rateSyncBeats{0.5f}; // beats per cycle when synced (0.5 = 1/8)
    std::atomic<float> depth{1.0f};         // 0..1 output amplitude (Math: gain 0..2)
    std::atomic<float> phase01{0.0f};       // 0..1 phase offset
    std::atomic<float> attackMs{10.0f};     // env follower / ADSR rise time
    std::atomic<float> releaseMs{120.0f};   // env follower / ADSR fall time
    std::atomic<float> decayMs{200.0f};     // ADSR decay time
    std::atomic<float> sustain{0.7f};       // ADSR sustain level 0..1
    std::atomic<float> envValue{0.0f};      // runtime: current envelope 0..1
    std::atomic<int32_t> adsrStage{0};      // runtime: 0 idle, 1 A, 2 D, 3 S, 4 R
    std::atomic<int32_t> prevGate{0};       // runtime: last gate (edge detection)
    std::atomic<int32_t> inputA{-1};        // Math: source modulator ids
    std::atomic<int32_t> inputB{-1};
    std::atomic<float> mathOffset{0.0f}; // Math: Scale offset
    std::atomic<float> outValue{0.0f};   // runtime: this block's output (read by links + Math)
    float scopeBuf[kScopeLen] = {};      // Scope: rolling history (audio-thread write)
    std::atomic<int32_t> scopeWrite{0};

    void pushScope(float v) {
        const int32_t w = scopeWrite.load(std::memory_order_relaxed);
        scopeBuf[w & (kScopeLen - 1)] = v;
        scopeWrite.store(w + 1, std::memory_order_relaxed);
    }
    // Copy the history oldest→newest into `out`; returns the count written.
    int32_t readScope(float* out, int32_t cap) const;

    Modulator() = default;
    Modulator(const Modulator& o) { *this = o; }
    Modulator& operator=(const Modulator& o);

    float get(ModField f) const;
    void set(ModField f, float v);

    // Math: combine two input values by the op (op = waveform field, gain = depth field).
    float mathEval(float a, float b) const;

    // Envelope follower: smooth the source level toward `envValue` with attack/release
    // (called once per audio block from the engine; `dt` = block seconds).
    void updateEnv(float level, double dt);

    // ADSR: a gated A/D/S/R envelope. `gate` = a note is held; on its rising edge the
    // envelope attacks, on its falling edge it releases. Exponential stage approaches.
    void updateAdsr(bool gate, double dt);

    // LFO: bipolar output in [-depth, +depth]. Env / MIDI→CV / ADSR: unipolar
    // envValue*depth (they update envValue outside eval).
    float eval(double beat, double timeSec) const;
};

// CV source kind for a link: a modulator, or another parameter's value (param → param
// cross-automation). Param sources live on the link's owner track (same-track for now).
enum class CvSourceKind : int32_t { Modulator = 0, Param = 1 };
// What the link modulates: a built-in insert-effect param, the instrument (plugin
// param, targetDevice = -1), or a MIDI-FX param (targetDevice = chain index).
enum class CvTargetKind : int32_t { Device = 0, Instrument = 1, MidiFx = 2 };

struct CvLink {
    int32_t sourceKind = 0;    // CvSourceKind
    int32_t sourceModId = 0;   // Modulator source
    int32_t sourceDevice = -1; // Param source: device + param on the owner track
    int32_t sourceParam = -1;
    int32_t targetKind = 0;    // CvTargetKind
    int32_t targetTrack = -1;  // target track id (-1 = the owner track)
    int32_t targetDevice = -1; // insert-effect / MIDI-FX index on the target track
    int32_t targetParam = -1;
    std::atomic<float> depth{1.0f}; // -1..1
    std::atomic<int32_t> mode{0};   // CvMode
    // The target param's *base* value (the centre the modulation swings around). The
    // device atomic holds the live modulated value; the UI edits this base instead.
    std::atomic<float> base{0.0f};

    CvLink() = default;
    CvLink(const CvLink& o) { *this = o; }
    CvLink& operator=(const CvLink& o);
};

} // namespace nota
