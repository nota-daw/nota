// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI note + clip model (M2). Times are musical (beats); the scheduler converts
// them to sample-accurate events per block (AR-8). Clips are immutable once
// published in a graph snapshot — edits build a new snapshot.

#pragma once

#include "Automation.h"
#include <cstdint>
#include <string>
#include <vector>

namespace nota {

struct Note {
    int32_t pitch = 60;       // MIDI note number 0..127
    double  startBeat = 0.0;  // relative to the clip start
    double  lengthBeats = 1.0;
    float   velocity = 0.8f;  // 0..1
};

struct MidiClip {
    std::string name;         // user-facing clip name (empty = default)
    double startBeat = 0.0;   // timeline position of the clip
    double lengthBeats = 4.0; // clip length (for looping later; unused in M2)
    bool   active = true;     // clip deactivate (key 0): false = stays but silent
    std::vector<Note> notes;
    // Clip envelopes (M9 follow-up): clip-local beats, value 0..1. Velocity scales
    // each note's velocity at note-on; volume scales the instrument output per
    // sample during the clip. Empty = no envelope (fast path).
    AutomationLane velocityEnvelope;
    AutomationLane volumeEnvelope;
};

} // namespace nota
