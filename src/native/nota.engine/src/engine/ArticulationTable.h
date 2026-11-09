// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Articulation maps in the engine (specs/085). A track's map arrives compiled from the UI as a
// flat table — articulations sorted by id, each pointing at its ordered MIDI actions — and a
// clip derives from its notes the points where the latching articulation changes. Both are
// built on the message thread, immutable once published with the graph (AR-5), and only read on
// the audio thread: lookups are binary searches over arrays, nothing allocates there (AR-6).
// Disabled articulations are left out by the UI's compiler, so here they are simply missing.

#pragma once

#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

struct MidiClip;

enum class ArticulationActionKind : int32_t {
    Keyswitch = 0,     // note data1 at velocity data2
    ControlChange = 1, // controller data1 = data2
    ProgramChange = 2, // program data1
    BankSelectMsb = 3, // CC 0 = data2
    BankSelectLsb = 4, // CC 32 = data2
};

enum class ArticulationTrigger : int32_t {
    Latching = 0,  // sent when the articulation changes; stays in force
    Momentary = 1, // sent with each note and released with it
};

struct ArticulationAction {
    ArticulationActionKind kind = ArticulationActionKind::Keyswitch;
    int32_t channel = 0; // 0 = the note's own channel, 1..16 explicit
    int32_t data1 = 0;
    int32_t data2 = 0;
};

struct ArticulationEntry {
    int32_t id = 0; // > 0, unique, ascending in the table
    ArticulationTrigger trigger = ArticulationTrigger::Latching;
    double offsetMs = 0.0; // added to the track's MIDI offset for notes with this articulation
    double leadMs = 0.0;   // the actions go out this long before the (offset) note
    int32_t actionStart = 0;
    int32_t actionCount = 0;
};

// Limits shared with the C ABI's validation.
inline constexpr double kArticulationMaxOffsetMs = 2000.0;
inline constexpr double kArticulationMaxLeadMs = 2000.0;
inline constexpr int32_t kArticulationMaxEntries = 4096;
inline constexpr int32_t kArticulationMaxActions = 65536;

class ArticulationTable {
  public:
    // A validated table, or null (and nothing built) when the input breaks a rule: ids must be
    // > 0 and strictly ascending; each entry's action range lies inside `actions`; kinds,
    // channels (0..16) and data (0..127) in range; offsets within ±kArticulationMaxOffsetMs and
    // leads within 0..kArticulationMaxLeadMs, both finite; defaultId is 0 or an entry's id.
    static std::shared_ptr<const ArticulationTable> build(int32_t defaultId, const ArticulationEntry* entries,
        int32_t entryCount, const ArticulationAction* actions, int32_t actionCount);

    // The articulation a note with `id` plays: its own entry, else the default's (id 0, an id
    // the map no longer has, or a disabled one), else null — no articulation, nothing sent.
    const ArticulationEntry* resolve(int32_t id) const;
    const ArticulationEntry* find(int32_t id) const;

    const ArticulationAction* actionsOf(const ArticulationEntry& e) const { return actions_.data() + e.actionStart; }
    int32_t defaultId() const { return defaultId_; }
    const std::vector<ArticulationEntry>& entries() const { return entries_; }

  private:
    int32_t defaultId_ = 0;
    std::vector<ArticulationEntry> entries_;
    std::vector<ArticulationAction> actions_;
};

// The latching articulation each note of a clip asks for, in clip-local beats, sorted by beat (at a
// shared beat, by ascending pitch; a repeat at the same beat is folded). The scheduler sends a
// point's switch only when it changes what is in force across the whole track — so a phrase sends
// its keyswitch once, and an overlapping clip's switch in between is respected. Momentary
// articulations are not here — they are sent per note — and do not change the latched state.
struct ArticulationChange {
    double beat = 0.0;     // the start of the note that brings the change
    int32_t entryId = 0;   // the resolved articulation (an entry id in the table)
    int32_t notePitch = 0; // that note, for ordering changes that share a beat
};

struct ArticulationTimeline {
    std::vector<ArticulationChange> changes; // may be empty: built, nothing latching

    // What it was built from, so a publish can tell a current timeline from a stale one: the
    // table itself (held, so a new table at a reused address can never pass for it) and a
    // fingerprint of the clip's notes.
    std::shared_ptr<const ArticulationTable> builtFor;
    uint64_t notesKey = 0;

    // The change in force at clip-local `beat` (the last one at or before it), or null.
    const ArticulationChange* inForceAt(double beat) const;
};

// The clip's timeline against `table` (never null; empty when nothing latches).
std::shared_ptr<const ArticulationTimeline> buildArticulationTimeline(
    const MidiClip& clip, const std::shared_ptr<const ArticulationTable>& table);

// A fingerprint of what a timeline depends on in the notes: each one's start, pitch and articulation, in order.
uint64_t articulationNotesKey(const MidiClip& clip);

// True when the clip's timeline matches `table` and its notes as they are (no table: no timeline).
bool articulationTimelineCurrent(const MidiClip& clip, const std::shared_ptr<const ArticulationTable>& table);

} // namespace nota
