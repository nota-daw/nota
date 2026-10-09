// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI note + clip model (M2). Times are musical (beats); the scheduler converts
// them to sample-accurate events per block (AR-8). Clips are immutable once
// published in a graph snapshot — edits build a new snapshot.

#pragma once

#include "Automation.h"
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
struct ExprPoint { float beat; float value; };

struct NoteExpr {
    std::vector<ExprPoint> dim[kExprDims];   // each sorted by beat; empty = not recorded

    bool empty() const { for (const auto& d : dim) if (!d.empty()) return false; return true; }
    // Linear between points, held before the first and after the last.
    float valueAt(int32_t d, double beat) const {
        const auto& p = dim[d];
        if (p.empty()) return kExprNeutral[d];
        if (beat <= p.front().beat) return p.front().value;
        if (beat >= p.back().beat) return p.back().value;
        auto it = std::upper_bound(p.begin(), p.end(), beat, [](double b, const ExprPoint& e) { return b < e.beat; });
        const ExprPoint& b = *it; const ExprPoint& a = *(it - 1);
        const double t = b.beat > a.beat ? (beat - a.beat) / (b.beat - a.beat) : 1.0;
        return static_cast<float>(a.value + (b.value - a.value) * t);
    }
};

// Ramer–Douglas–Peucker: the fewest points that stay within `tol` of the curve (first and
// last always kept). Turns a block-rate recording into an editable shape.
inline std::vector<ExprPoint> thinExprCurve(const std::vector<ExprPoint>& p, float tol) {
    if (p.size() <= 2) return p;
    std::vector<char> keep(p.size(), 0);
    keep.front() = keep.back() = 1;
    std::vector<std::pair<size_t, size_t>> stack{ { 0, p.size() - 1 } };
    while (!stack.empty()) {
        auto [a, b] = stack.back(); stack.pop_back();
        if (b <= a + 1) continue;
        float worst = -1.0f; size_t at = a;
        for (size_t i = a + 1; i < b; ++i) {
            const float span = p[b].beat - p[a].beat;
            const float t = span > 0.0f ? (p[i].beat - p[a].beat) / span : 0.0f;
            const float d = std::fabs(p[i].value - (p[a].value + (p[b].value - p[a].value) * t));
            if (d > worst) { worst = d; at = i; }
        }
        if (worst > tol) { keep[at] = 1; stack.push_back({ a, at }); stack.push_back({ at, b }); }
    }
    std::vector<ExprPoint> out;
    for (size_t i = 0; i < p.size(); ++i) if (keep[i]) out.push_back(p[i]);
    return out;
}

struct Note {
    int32_t pitch = 60;       // MIDI note number 0..127
    double  startBeat = 0.0;  // relative to the clip start
    double  lengthBeats = 1.0;
    float   velocity = 0.8f;  // 0..1
    int32_t exprId = 0;                       // the engine's expression store id (0 = none)
    std::shared_ptr<const NoteExpr> expr;     // resolved from exprId; null = none
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
