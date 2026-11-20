// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Recording a Session performance into the Arrangement (#971, specs/109). While an Arrangement
// take runs, the audio thread reports every Session launch and stop as it really applies; the
// message thread folds those reports into played spans here, and at the end of the take each span
// becomes ordinary Arrangement clips. Pure bookkeeping and clip building — no engine state, so the
// rules can be checked without rendering (sessionPassSelfTest).

#pragma once

#include "MidiClip.h"

#include <cstdint>
#include <limits>
#include <utility>
#include <vector>

namespace nota {

// One report from the audio thread. Fixed size, copied through an SPSC ring (AR-4, AR-6).
struct LaunchEvent {
    enum Kind : int32_t { Launch = 0, Seam = 1, Stop = 2 };
    int32_t kind = Launch;
    int32_t trackId = 0;
    int32_t slot = -1;    // Launch: the slot now playing, -1 = stopped (back to its Arrangement)
    double beat = 0.0;    // where it took effect (Seam: the loop end, Stop: where the transport stopped)
    double slotPos = 0.0; // Launch: the slot's loop position at `beat`; Seam: the loop start
    float gain = 1.0f;    // Launch: the launch-velocity gain
    uint32_t serial = 0;  // the pass it belongs to; events of an earlier attempt are dropped
};

// One slot playing on one track, from `start` to `end` (timeline beats).
struct PlayedSpan {
    int32_t trackId = 0;
    int32_t slot = -1;
    double start = 0.0;
    double end = std::numeric_limits<double>::quiet_NaN(); // NaN while it plays
    double slotPos = 0.0; // the slot's loop position at `start`
    float gain = 1.0f;
    bool open() const { return end != end; }
};

class SessionPass {
  public:
    bool active = false;
    bool performanceOnly = false; // started with nothing armed (record start status 3)
    uint32_t serial = 0;
    double tempoBefore = 0.0;
    std::vector<std::pair<double, double>> tempoSteps; // (beat, bpm): scene tempos applied in the pass
    int32_t dropped = 0;                                // reports lost to a full ring
    bool stopped = false;                               // the transport stopped under the pass
    // Spans in the order they started: applied in that order, so a later loop pass replaces an
    // earlier one over the same range.
    std::vector<PlayedSpan> spans;

    void reset() { *this = SessionPass{}; }
    void onEvent(const LaunchEvent& e);
    void closeAll(double beat);
    bool empty() const { return spans.empty() && tempoSteps.empty(); }

  private:
    void close(PlayedSpan& s, double beat);
    void prune();
};

// The slot's MIDI as it played over a span: its content repeated every `L` beats from the slot
// position at the span start, cut to the span (a one-shot slot plays its content once). Notes start
// inside each repetition exactly as the Session player starts them, keep their length across a
// repetition seam (the player does) and are cut at the span end (the stop flushed them); launch
// gain scales their velocity; a note still sounding at the span start keeps its remaining part
// (its expression re-based, stored by the writer: exprId 0). Controllers repeat with the content;
// the clip envelopes do not (Session playback ignores them).
MidiClip unrollSlotMidi(const MidiClip& slot, double L, bool loop, const PlayedSpan& span);

// The start beats of each repetition of a slot of length `L` that overlaps the span (an audio slot
// becomes one clip per repetition).
std::vector<double> slotRepetitions(double L, bool loop, const PlayedSpan& span);

// Pure checks of the span rules and the MIDI unroll; 0 = pass, else the failed check.
int32_t sessionPassSelfTest();

} // namespace nota
