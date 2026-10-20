// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Random — a MIDI effect (midiKind 5), almanac rework (mockups 1a / 1b). With probability
// Chance a note is varied across up to five dimensions: Note (±semitones), Velocity (±64),
// Timing (a 0..100 ms delay — a real-time effect can only play late), Skip (drop it) and
// Octave (±2). The random values are shaped by a Distribution — Gauss (a bell), Even (flat)
// or Walk (a bounded random walk that drifts from note to note) — drawn Per note or once Per
// bar, and optionally snapped into C major (Stay in scale).
//
// Param layout is APPEND-ONLY: 0 Chance / 1 Note Range stay put so old projects load unchanged.

#pragma once

#include "devices/MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>
#include <cstring>

namespace nota {

class MidiRandom final : public MidiDevice {
  public:
    enum Param {
        PChance = 0,
        PNoteRange,
        PVelAmt,
        PTimeAmt,
        PSkip,
        POctAmt,
        PDist,
        PRate,
        PStayInScale,
        PLocked,
        PSeed,
        PView,    // editor card size (0 = L, 1 = S) — not a sound param
        PLockBar, // the bar whose roll Lock holds
        kNumParams
    };
    static constexpr double kBarBeats = 4.0;
    static constexpr double kMaxDelayMs = 100.0;

    MidiRandom();

    int32_t midiKind() const override { return 5; }
    const char* displayName() const override { return "Nota Random"; }

    void setSampleRate(double sr, int32_t) override { sr_ = sr > 0 ? sr : 44100.0; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }
    void reset() override;

    // Editor / MCP telemetry: [0] the bar being rolled, [1] notes in, [2] notes changed,
    // [3] notes skipped (counts since the device was created), [4] last input pitch, [5] its
    // output pitch (−1 = skipped), [6] input velocity 0..1, [7] output velocity, [8] its delay
    // in ms, [9] events waiting in the delay queue.
    int32_t midiScope(float* out, int32_t maxN) const override;
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override;

    // ---- the roll (shared with the editor's model, which ports these exactly) -------------------
    static uint32_t hash(uint32_t a, uint32_t b) {
        uint32_t h = a * 2654435761u + b * 40503u + 0x9e3779b9u;
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        return h;
    }
    static float u01(uint32_t& s) {
        s = s * 1664525u + 1013904223u;
        return (float)(s >> 8) / 16777216.0f;
    }
    // One draw in −1..1 from the distribution. Walk steps the caller's walk state (reflecting
    // at ±1); Gauss is a Box–Muller normal scaled so ±1 ≈ 2.6 σ.
    static float draw(int dist, uint32_t& s, float& walk);
    static int snapToCMajor(int p) {
        static const bool in[12] = {true, false, true, false, true, true, false, true, false, true, false, true};
        return in[((p % 12) + 12) % 12] ? p : p - 1;
    }
    static uint32_t rollKey(uint32_t seed, uint32_t barKey) { return hash(seed * 7919u + 17u, barKey * 104729u + 3u); }
    static uint32_t noteKey(uint32_t roll, int tick, int pitch) {
        return hash(roll, (uint32_t)tick * 128u + (uint32_t)pitch);
    }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

  private:
    struct Pending {
        int64_t due;
        MidiEv ev;
    };
    static constexpr int kQueue = 512;

    static void push(MidiEv* out, int& nOut, int maxOut, const MidiEv& e) {
        if (nOut < maxOut) out[nOut++] = e;
    }
    void enqueue(int64_t due, const MidiEv& e) {
        if (nQueue_ < kQueue) queue_[nQueue_++] = {due, e};
    }

    double sr_ = 44100.0, intBeat_ = 0.0;
    int64_t clock_ = 0;
    uint64_t lastRollKey_ = UINT64_MAX;
    float walk_[4] = {0, 0, 0, 0};
    float barD_[4] = {0, 0, 0, 0};
    int8_t offset_[128] = {};
    uint8_t skipped_[128] = {};
    int32_t delay_[128] = {};
    Pending queue_[kQueue];
    int nQueue_ = 0;
    std::atomic<uint32_t> pass_{0};
    std::atomic<int32_t> bar_{0}, notesIn_{0}, changed_{0}, skippedN_{0}, lastIn_{-1}, lastOut_{-1}, queued_{0};
    std::atomic<float> lastInVel_{0.0f}, lastOutVel_{0.0f}, lastDelayMs_{0.0f};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
