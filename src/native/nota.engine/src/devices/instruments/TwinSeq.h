// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — the sequencer's data (specs/055 US8): eight patterns of up to 64 steps, each step
// up to six notes with a velocity and a gate (or a tie into the next step), and four motion lanes
// per pattern that set a parameter step by step. The patterns are structural state, not params:
// the message thread edits an authoring copy and publishes an immutable snapshot the audio thread
// reads (AR-5); undo swaps the serialized bytes (Instrument::editState).
//
// The arpeggiator's note for a step is a pure function of the step's absolute index (the Nota
// Arp idea), so seeks, loops and block sizes never change what plays.

#pragma once

#include <cstdint>
#include <vector>

namespace nota {
namespace twin {

constexpr int kPatterns = 8, kSteps = 64, kStepNotes = 6, kLanes = 4;
constexpr uint8_t kGateTie = 101;       // a step's gate past 100 % holds into the next step
constexpr uint16_t kMotionUnset = 0xFFFF; // a motion lane step that leaves the param alone

struct Step {
    uint8_t n = 0; // notes in the step (0 = rest)
    uint8_t note[kStepNotes] = {};
    uint8_t vel = 100;  // 1..127
    uint8_t gate = 50;  // 1..100 % of the step, or kGateTie
};

struct Lane {
    int16_t target = -1; // a plugin-param index of the Twin, −1 = none
    uint8_t smooth = 0;  // 0 = a step per step, 1 = glide to the next step's value
    uint16_t val[kSteps];
    Lane() {
        for (auto& v : val) v = kMotionUnset;
    }
};

struct Pattern {
    uint8_t length = 16; // 1..64
    Step step[kSteps];
    Lane lane[kLanes];
};

struct PatternSet {
    Pattern pat[kPatterns];
};

// The pattern block of the state blob: [u32 "TWP1"] then per pattern its length, every step
// (n, six notes, velocity, gate) and every lane (target i16, smooth, 64 × u16) — little-endian.
std::vector<uint8_t> serializePatterns(const PatternSet& ps);
bool deserializePatterns(const uint8_t* data, int32_t size, PatternSet& out);

// The arpeggiator (FR-064). Modes: Up, Down, Up/Down, Random, As played, Chord.
enum ArpMode : int { ArpUp = 0, ArpDown, ArpUpDown, ArpRandom, ArpAsPlayed, ArpChord, kArpModes };
// The notes step k plays from `held` (in the order played), over `octaves` octaves: one note,
// or every note for Chord. Writes up to maxN into out and returns the count.
int arpNotes(const int* held, int n, int mode, int octaves, int64_t k, uint32_t seed, int* out, int maxN);

} // namespace twin
} // namespace nota
