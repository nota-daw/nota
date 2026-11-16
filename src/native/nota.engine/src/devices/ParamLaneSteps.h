// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A param's automation lane, sampled at the steps of one block (specs/100). The engine applies
// automation once per block, at its start, so a param that switches something at a musical
// moment — a Rhythm bank, a Twin sequencer pattern — would hear a breakpoint on a bar line a
// step late, and differently at every block size. Instrument::pluginParamLane hands the lane
// over for the block instead; fill() samples it at the grid beat of every step that can trigger
// in the block, and the sequencer asks at() when the step fires.
//
// The window starts one step before the block: a swung step fires after its grid position, so
// its trigger can land in the block after the one that holds its grid beat. Each sample is taken
// a hair after the grid beat, so a vertical jump drawn exactly on the step (two points at one
// beat) already plays the new value there. Fixed storage, no allocation (audio thread); the
// lane is read only inside fill(), never kept.
//
// Lifetime: the samples belong to one block. A block may render in several pieces (between
// note events), so they are dropped once the render reaches the block's end (rendered()); a
// block whose render was skipped (a switched-off instrument) is caught at the next block's
// transport (transport()), which starts somewhere else.

#pragma once

#include "engine/Automation.h"

#include <cmath>
#include <cstdint>

namespace nota {

class ParamLaneSteps {
  public:
    static constexpr int kMax = 32;

    void fill(const AutomationLane& lane, double beatFrom, double beatTo, double stepBeats) {
        n_ = 0;
        from_ = beatFrom;
        to_ = beatTo;
        if (lane.points.empty() || !(stepBeats > 0.0) || !(beatTo >= beatFrom)) return;
        first_ = static_cast<int64_t>(std::ceil(beatFrom / stepBeats - 1e-9)) - 1;
        const int64_t last = static_cast<int64_t>(std::ceil(beatTo / stepBeats - 1e-9)) - 1;
        for (int64_t k = first_; k <= last && n_ < kMax; ++k)
            v_[n_++] = lane.valueAt(static_cast<double>(k) * stepBeats + kAfter);
    }
    // The lane's value at step k's grid beat, if this block sampled it.
    bool at(int64_t k, float& out) const {
        const int64_t i = k - first_;
        if (i < 0 || i >= n_) return false;
        out = v_[i];
        return true;
    }
    void clear() { n_ = 0; }
    // A new block's transport: samples taken for another block are stale.
    void transport(double blockBeat) {
        if (n_ && std::fabs(blockBeat - from_) > 1e-6) n_ = 0;
    }
    // A render piece ended at `beatNow`: drop the samples once the block is done (or stopped).
    void rendered(double beatNow, bool playing) {
        if (n_ && (!playing || beatNow >= to_ - 1e-6)) n_ = 0;
    }

  private:
    static constexpr double kAfter = 1e-7; // beats: just right of the grid beat
    double from_ = 0.0, to_ = 0.0;
    int64_t first_ = 0;
    int n_ = 0;
    float v_[kMax] = {};
};

} // namespace nota
