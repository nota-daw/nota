// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DrumRack — a pad-per-slot Drum Rack. It's an Instrument Rack whose chains are
// pads: each chain has a trigger note (RackCore::ChainControls::triggerNote) and
// only fires when that note plays. Parallel chains, macros, persistence and the
// summing/render skeleton are inherited from RackInstrument/RackCore; the Drum
// Rack adds pad-specific note routing and per-pad shaping:
//   • choke groups — a hit on a pad cuts the other pads sharing its group (mono).
//   • tune         — the pad transposes the note it hands to its instrument.
//   • decay        — a per-pad amp VCA (retriggered on each hit) shortens the tail.
// Kit-level swing/humanize live in RackCore and are applied by the engine's event
// scheduler (Engine_Render), which already fires noteOn sample-accurately.

#pragma once

#include <memory>
#include <cstring>
#include <algorithm>

#include "RackInstrument.h"
#include "dsp/core/RtAttributes.h"

#include <cmath>

namespace nota {

class DrumRack final : public RackInstrument {
  public:
    int32_t kind() const override { return 4; } // built-in Drum Rack (project compat)
    const char* displayName() const override { return "Nota Drum Rack"; }
    bool handlesSustain() const override { return true; } // pads ignore the pedal (#99)

    // Route a note only to the pad(s) assigned to it, applying choke + tune and
    // arming the pad's decay envelope.
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    // Sum every audible pad, applying the per-pad decay VCA. Mirrors
    // RackInstrument::render with the pad envelope folded into the mix loop.
    void render(float* out, int32_t frames) override;

    bool isIdle() const override {
        if (sleepMode() == dsp::PluginSleepMode::Never) return false;
        return chainsIdle(true);
    }

    std::shared_ptr<Instrument> clone() const override;

  private:
    // Counts this rack's renders (audio thread): hits between the same two renders are at the
    // same sample, and a choke group does not cut a pad struck at that very moment (#644).
    uint32_t renderSerial_ = 1;
    NOTA_NOINLINE void beginRender(RackState& st, int32_t frames);
    void renderChunk(RackState& st, float* out, int32_t frames, int32_t tapAt);
    static int32_t tunedNote(const ChainControls& ctl, int32_t pitch) {
        return std::clamp(pitch + ctl.tune.load(std::memory_order_relaxed), 0, 127);
    }
};

} // namespace nota
