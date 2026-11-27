// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DrumRack — pad-per-slot Drum Rack with choke groups, pad tune and decay VCA.

#pragma once

#include "nota/devices/Export.h"
#include "nota/devices/racks/RackInstrument.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>

namespace nota::devices {

class NOTA_DEVICES_API DrumRack final : public RackInstrument {
  public:
    ~DrumRack() override;

    int32_t kind() const override { return 4; }
    const char* displayName() const override { return "Nota Drum Rack"; }
    bool handlesSustain() const override { return true; }

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

    bool isIdle() const override {
        if (requiresActiveProcessing()) return false;
        return chainsIdle(true);
    }

    std::shared_ptr<dsp::Instrument> clone() const override;

  private:
    uint32_t renderSerial_ = 1;
    NOTA_NOINLINE void beginRender(RackState& st, int32_t frames);
    NOTA_NOINLINE void renderChunk(RackState& st, float* out, int32_t frames, int32_t tapAt);
    static int32_t tunedNote(const ChainControls& ctl, int32_t pitch) {
        return std::clamp(pitch + ctl.tune.load(std::memory_order_relaxed), 0, 127);
    }
};

} // namespace nota::devices
