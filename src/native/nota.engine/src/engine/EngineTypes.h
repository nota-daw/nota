// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine free types: lock-free queue payloads/aliases, undo
// model and control events shared between the Engine core, ABI layer and
// audio/MIDI threads.

#pragma once

#include "CommandQueue.h"
#include "Graph.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

// trackId -1 = ordinary live input (armed / audition tracks); >= 0 = addressed to that
// one track whatever its arm state (a Nota Remote phone playing its own track).
// expr ≥ 0 makes it a per-note expression (an ExprDim, NoteExpression.h) carrying its value
// in `velocity`; pitch −1 then addresses the whole instrument.
struct MidiEvent {
    bool on;
    int32_t pitch;
    float velocity;
    int32_t trackId = -1;
    int32_t expr = -1;
};
using MidiQueue = SpscRingBuffer<MidiEvent, 1024>;

// Recorded note (audio -> message), lock-free up-queue. startBeat is absolute.
// pass: the loop pass of the take the note started in (0 without a loop; a note held across
// the loop seam is split, its continuation starting the next pass) — loop record mode (#29).
struct RecordedNote {
    int32_t pitch;
    double startBeat;
    double lengthBeats;
    float velocity;
    int32_t pass = 0;
};
using RecordedQueue = SpscRingBuffer<RecordedNote, 1024>;
// A recorded note's expression sample (audio -> message): the note is identified by its pitch
// and start (as its RecordedNote will carry them), `beat` is the offset from that start.
struct RecordedExpr {
    int32_t pitch;
    double startBeat;
    int32_t dim;
    float beat;
    float value;
};
using RecordedExprQueue = SpscRingBuffer<RecordedExpr, 1u << 14>;
// A recorded controller sample (audio -> message): at an absolute beat of the take (a Session
// take: loop-local), in loop pass `pass`. ctl -1 is a pass-end marker at `beat` (the loop end):
// controllers still being replaced over a range stop there (specs/020, FR-021).
struct RecordedCtl {
    int32_t ctl;
    double beat;
    float value;
    int32_t pass;
};
using RecordedCtlQueue = SpscRingBuffer<RecordedCtl, 1u << 14>;
// A clip note's expression at a sample offset of the block (the render pass replays these).
struct ExprEv {
    int32_t off;
    int32_t pitch;
    int32_t dim;
    float value;
};

// Captured input frame (input thread -> message), lock-free up-queue (M4-3).
struct InputFrame {
    float l;
    float r;
};
using InputQueue = SpscRingBuffer<InputFrame, 1u << 17>;   // ~3 s at 44.1 kHz — headroom for UI-thread drain stalls
using MonitorQueue = SpscRingBuffer<InputFrame, 1u << 14>; // input thread -> audio thread (live monitoring)

// Incoming MIDI control event surfaced to the UI for MIDI-learn (MIDI thread ->
// message thread, lock-free up-queue). kind: 0 = control-change, 1 = note-on.
// For CC, number is the controller and value its 0..127 data; for notes, number
// is the pitch and value the velocity. The engine still routes notes to instruments.
struct ControlEvent {
    int32_t kind;
    int32_t channel;
    int32_t number;
    int32_t value;
};

// Value undo (specs/009, specs/016): volume / pan / master volume live in atomics outside the
// graph, and device parameters live in Device objects that cloned tracks share — restoring
// an older graph restores neither. Their undo steps carry values instead of a snapshot. They
// share one history with the structural snapshots (UndoEntry), which keeps the order of all
// edits. Parameter kinds address (track, device index, parameter index): DeviceParam an audio
// effect, MidiDeviceParam a MIDI effect, PluginParam a normalized hosted-plugin / built-in
// instrument parameter (deviceIndex -1 = the instrument).
enum class MixKind : int32_t {
    TrackVolume = 0,
    TrackPan = 1,
    MasterVolume = 2,
    DeviceParam = 3,
    MidiDeviceParam = 4,
    PluginParam = 5
};
struct MixTarget {
    MixKind kind;
    int32_t trackId; // trackId ignored for MasterVolume
    int32_t deviceIndex = -1;
    int32_t paramIndex = -1; // parameter kinds only
};
struct MixChange {
    MixKind kind;
    int32_t trackId;
    float before;
    float after;
    int32_t deviceIndex = -1;
    int32_t paramIndex = -1; // parameter kinds only
};
struct UndoEntry {
    std::shared_ptr<Graph> graph; // the state to restore; null for a mix-only step
    std::vector<MixChange> mix;   // empty for a structural step; one change per target
};
using ControlEventQueue = SpscRingBuffer<ControlEvent, 1024>;

} // namespace nota
