// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Articulation playback (specs/085): which articulation MIDI one block sends. A pure function of
// the track's clips, its table and the block. Session latching releases also retain the actual
// keys and their remaining rendered frames, so tempo changes cannot skip a pending release:
//
//   effective note time = nominal + track offset + articulation offset      (FR-018)
//   trigger time        = effective − lead                                   (FR-019)
//
// A latching articulation is sent where a note asks for one that isn't already in force — decided
// across all of the track's clips, by trigger time — so a phrase sends its keyswitch once and an
// overlapping clip's switch in between is honoured; a keyswitch note is released kKeyswitchHoldMs
// later. Momentary ones are sent with each note and their keyswitch held until the last note
// holding that key ends. On a chase (start, seek, loop wrap, a slot's launch) the latching
// articulation in force at the block start is sent at offset 0 and released 20 ms later, and
// a held momentary note's switch goes with it. Audio thread: reads published, immutable data only
// and writes into the caller's fixed array — no allocation, no locks (AR-6).

#pragma once

#include "ArticulationTable.h"

#include <cstdint>
#include <type_traits>
#include <vector>

namespace nota {

struct MidiClip;

enum class ArtEvKind : uint8_t {
    KeyOn = 0,   // keyswitch note on: data1 pitch, data2 velocity
    KeyOff = 1,  // keyswitch note off: data1 pitch
    Control = 2, // CC data1 = data2 (bank select is CC 0 / CC 32)
    Program = 3, // program change data1
};

// One articulation event of a block. Sorted by off; at a shared offset, key releases first (a
// switch let go before the next one is pressed), then the actions in their map order (seq).
// Trivially constructible on purpose: the render keeps a kMaxBlockArt array of these on the stack
// for every instrument track, every block, and default member initialisers would zero it each time.
struct ArtEv {
    int32_t off; // sample offset in the block
    int32_t seq; // order among events at the same offset
    ArtEvKind kind;
    uint8_t channel; // 0 = the note's own channel, 1..16
    uint8_t data1;
    uint8_t data2;
};
static_assert(std::is_trivially_default_constructible_v<ArtEv>);

inline constexpr double kKeyswitchHoldMs = 20.0; // how long a latching keyswitch note is held
inline constexpr int kMaxBlockArt = 256;          // a block's articulation events (stack array)

// Audio-thread-owned, fixed-capacity release storage. Only emitted latching key-ons enter it.
// Remaining frames count down with rendering, independent of a slot's beat position or tempo.
// The pending array is deliberately uninitialized: entries beyond count are never read.
struct ArticulationReleaseState {
    struct Pending {
        int32_t remaining;
        uint8_t channel, pitch;
    };
    Pending pending[kMaxBlockArt];
    int count = 0;
};

struct ArticulationBlock {
    int32_t frames = 0;
    double blockStart = 0.0; // samples, the transport position of the block's first frame
    double spb = 0.0;        // samples per beat
    double sampleRate = 0.0;
    double trackOffsetMs = 0.0;
    bool chase = false;      // start / seek / loop wrap: re-send what is in force
    bool seamOffs = false;   // a loop seam just passed: let go of keys held across it
    double seamEnd = 0.0;    // where the seam was (samples), when seamOffs
    double chaseStart = -1.0; // last discontinuity on this clock, for a chased key's later release
    ArticulationReleaseState* releases = nullptr; // Session: rendered-frame clock for all latching releases
    const class TempoMap* tempoMap = nullptr; // arrangement absolute timing, no effect on Session's local clock
};

// Fills `out` (capacity `cap`) with the block's events, sorted, and returns how many. Events that
// don't fit are dropped and counted in *dropped (never allocated). Inactive clips send nothing.
int scheduleArticulations(const std::vector<MidiClip>& clips, const ArticulationTable& table, const ArticulationBlock& b,
    ArtEv* out, int cap, int* dropped = nullptr);

// A Session slot (specs/085, T042): the slot's clip looped every `loopBeats` on the slot's own clock.
// In `b`, blockStart is the slot's local position in samples (localBeats × spb); a pass k starts at
// k × loopBeats. A one-shot slot plays pass 0 only. Set chase on the first block after a launch.
// Every pass whose events can reach the block is read, however far the offsets move them.
int scheduleSlotArticulations(const MidiClip& slotClip, double loopBeats, bool loop, const ArticulationTable& table,
    const ArticulationBlock& b, ArtEv* out, int cap, int* dropped = nullptr);

// Continue pending releases even after the track's map was cleared; no table or beat clock needed.
int scheduleArticulationReleases(ArticulationReleaseState& releases, int32_t frames, ArtEv* out, int cap);

// The offset (ms) an articulation adds to a note with this id: its entry's (or the default's), else 0.
double articulationOffsetMs(const ArticulationTable* table, int32_t noteArticulation);

} // namespace nota
