// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Chamber IR synthesis . The procedural impulse-response side
// of Nota Chamber: sixteen built-in responses (halls, rooms, plates, a spring,
// spaces, FX) synthesised once per process at 48 kHz and shared by every
// instance, plus the trim/stretch/reverse shaping applied when a kernel is
// built. Split out of the Chamber monolith; math verbatim (offline synthesis,
// runs on worker/message threads — no FastMath substitution).

#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {
namespace chamber {

constexpr double kPi = 3.14159265358979323846;

struct Rng {
    uint32_t s;
    explicit Rng(uint32_t seed) : s(seed ? seed : 0x9E3779B9u) {}
    uint32_t next() {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }
    float uni() { return static_cast<float>(next() >> 8) * (1.0f / 16777216.0f); } // [0, 1)
    float bi() { return uni() * 2.0f - 1.0f; }                                     // [-1, 1)
};

double expMap(double v, double lo, double hi);
float onePole(double hz, double sr);
float flush(float x);
// Transparent below |1|, a tanh knee above (the wet safety net).
float softClip(float x);
// Loop guard: identity in the normal range, bounded when shimmer / freeze-in overdrives it.
float loopLimit(float x);

// ============================================================================
// Impulse responses
// ============================================================================

struct SourceIr {
    std::vector<float> data; // interleaved, `channels` per frame
    int channels = 0;
    int64_t frames = 0;
    double sampleRate = 48000.0;
    std::string name, category;
    float seconds() const { return sampleRate > 0.0 ? static_cast<float>(frames / sampleRate) : 0.0f; }
};

enum IrType { IrRoom = 0, IrPlate, IrSpring, IrGated, IrMetal, IrOutdoor };

struct IrSpec {
    const char* name;
    const char* category;
    float length;               // generated length (s)
    float rtLow, rtMid, rtHigh; // RT60 (s) around 125 Hz / 1 kHz / 8 kHz
    float erSpan;
    int erCount;   // early reflections: window (ms), count
    float buildup; // echo-density build-up (ms)
    float bright;  // overall high cut (Hz)
    float cross;   // true-stereo cross-feed gain (LR / RL)
    float flutter; // flutter-echo period (ms), 0 = none
    int type;
    uint32_t seed;
};

constexpr int kNumBuiltinIrs = 16;

const IrSpec& irSpec(int i);

// Rooms / halls / plates / gated / outdoor: sparse-to-dense noise (echo density growing with
// t²) split into four bands that each decay at their own RT60, plus discrete early taps.
void synthRoom(std::vector<float>& out, const IrSpec& s, double sr, Rng& rng);
// A spring tank: one recirculating delay per channel with a long first-order allpass chain
// in the loop (the dispersive "boing" chirp), band-limited like the real thing.
void synthSpring(std::vector<float>& out, const IrSpec& s, double sr, int c, Rng& rng);
// A metal tank: inharmonic decaying modes (recursive phasors) — resonant and clangy.
void synthMetal(std::vector<float>& out, const IrSpec& s, double sr, Rng& rng);

// The 4-channel (LL, LR, RL, RR) true-stereo response of a built-in IR at 48 kHz.
std::shared_ptr<SourceIr> synthesizeIr(int idx);
// Built-in IRs are synthesised once per process and shared by every Chamber instance.
std::shared_ptr<const SourceIr> builtinIr(int idx);

struct Shape {
    float start = 0.0f, end = 1.0f, attackMs = 0.0f, size = 1.0f;
    bool reverse = false;
};

// Trim [start, end] of the source, time-stretch by Size while resampling to the engine rate
// (cubic Hermite), fade the truncated end, optionally reverse, then fade in over Attack.
// Loudness is normalised on the whole source so trims read as trims, not as level jumps.
std::vector<std::vector<float>> shapeIr(
    const SourceIr& src, const std::vector<int>& chans, const Shape& sh, double sr, double maxSec);

} // namespace chamber
} // namespace nota
