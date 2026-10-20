// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI note + clip model (M2). Times are musical (beats); the scheduler converts
// them to sample-accurate events per block (AR-8). Clips are immutable once
// published in a graph snapshot — edits build a new snapshot.

#pragma once

#include "Automation.h"
#include "MidiController.h"
#include "NoteExpression.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {

// A note's recorded MPE (per-note bend / pressure / slide): one breakpoint curve per
// dimension, times in beats from the note's start, values as Instrument::noteExpression
// takes them (bend in semitones). Immutable once built — notes share it by pointer, so
// moving, copying or undoing a note carries its expression for free. The engine keeps every
// curve in a store under a stable id (NotaNoteData.expr_id) so the UI can round-trip it.
struct ExprPoint {
    float beat;
    float value;
};

struct NoteExpr {
    std::vector<ExprPoint> dim[kExprDims]; // each sorted by beat; empty = not recorded

    bool empty() const {
        for (const auto& d : dim)
            if (!d.empty()) return false;
        return true;
    }
    // Linear between points, held before the first and after the last.
    float valueAt(int32_t d, double beat) const;
};

// Ramer–Douglas–Peucker: the fewest points that stay within `tol` of the curve (first and
// last always kept). Turns a block-rate recording into an editable shape.
std::vector<ExprPoint> thinExprCurve(const std::vector<ExprPoint>& p, float tol);

struct Note {
    int32_t pitch = 60;     // MIDI note number 0..127
    double startBeat = 0.0; // relative to the clip start
    double lengthBeats = 1.0;
    float velocity = 0.8f;                // 0..1
    int32_t exprId = 0;                   // the engine's expression store id (0 = none)
    std::shared_ptr<const NoteExpr> expr; // resolved from exprId; null = none
};

struct MidiClip {
    std::string name;         // user-facing clip name (empty = default)
    double startBeat = 0.0;   // timeline position of the clip
    double lengthBeats = 4.0; // clip length (for looping later; unused in M2)
    bool active = true;       // clip deactivate (key 0): false = stays but silent
    std::vector<Note> notes;
    // Clip envelopes (M9 follow-up): clip-local beats, value 0..1. Velocity scales
    // each note's velocity at note-on; volume scales the instrument output per
    // sample during the clip. Empty = no envelope (fast path).
    AutomationLane velocityEnvelope;
    AutomationLane volumeEnvelope;
    // Instrument-wide controllers (specs/020): at most one curve per ctl, sorted by ctl.
    // Shared, immutable curves — a clip copy shares them; an edit swaps in a new one.
    std::vector<std::shared_ptr<const ControllerCurve>> controllers;

    const ControllerCurve* controller(int32_t ctl) const {
        for (const auto& c : controllers)
            if (c && c->ctl == ctl) return c.get();
        return nullptr;
    }
    std::shared_ptr<const ControllerCurve> controllerPtr(int32_t ctl) const {
        for (const auto& c : controllers)
            if (c && c->ctl == ctl) return c;
        return nullptr;
    }
    // Replace ctl's curve with `pts` (normalised here); empty removes it.
    void setController(int32_t ctl, std::vector<AutomationPoint> pts);
    // This clip as a piece of `src` starting `from` beats into it, `len` long: its controllers
    // cut to the piece (the first piece keeps a pickup's points before 0).
    void sliceControllersFrom(const MidiClip& src, double from, double len);
    // Shift every controller point by `delta` beats (the clip's left edge moved, notes with it).
    void shiftControllers(double delta);
};

} // namespace nota
