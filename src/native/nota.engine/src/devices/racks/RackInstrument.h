// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInstrument — a parallel-chain Instrument Rack. A container Instrument whose
// parallel chains each hold a built-in instrument followed by an insert chain of
// built-in devices. Every note is fanned to every chain (no key/velocity zones in
// v1) and the chain outputs are summed. All the chain/macro/snapshot/persistence
// machinery lives in RackCore; this class only adds the Instrument interface.
//
// Because a RackInstrument is just an Instrument, it slots onto an instrument
// track with no change to the engine's render path.

#pragma once

#include <memory>
#include <algorithm>

#include "RackCore.h"

#include <cstring>

namespace nota {

class RackInstrument : public Instrument, public RackCore {
  public:
    int32_t kind() const override { return 3; } // built-in Instrument Rack (project compat)
    const char* displayName() const override { return "Nota Instrument Rack"; }

    void setSampleRate(double sr) override { setRates(sr, kMaxBlock); }

    // Forward the DAW transport to every chain child (nested plugins sync too).
    void setTransportInfo(const TransportInfo& ti) override { forwardTransport(ti); }
    void setTransport(double beatStart, double spb, bool playing) override { forwardTempo(beatStart, spb, playing); }

    // ---- note fan-out: a note reaches a chain only if it's inside its key/vel zone --
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;
    // Expression goes to every chain: one whose zone kept the note out isn't holding the
    // pitch, so it ignores it.
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    // Controllers too: every chain hears the pedal and the wheels, like live input.
    void controller(int32_t ctl, float value) override;
    bool supportsMpe() const override;

    // ---- render: sum every audible chain into `out` -------------------------
    void render(float* out, int32_t frames) override;

    int32_t latencySamples() const override { return maxChainLatency(); }

    // ---- macros over the plugin-param surface (automation-free) -------------
    int32_t pluginParamCount() const override { return kNumMacros; }
    std::string pluginParamId(int32_t i) const override { return macroParamId(i); }
    std::string pluginParamName(int32_t i) const override { return macroParamName(i); }
    float pluginParamGet(int32_t i) const override { return macroGet(i); }
    void pluginParamSet(int32_t i, float v) override { macroSet(i, v); }
    int32_t pluginParamIndexOfId(const std::string& id) const override { return macroParamIndexOfId(id); }

    // ---- persistence + clone (shared blob) ----------------------------------
    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override { deserialize(data, size); }
    std::shared_ptr<Instrument> clone() const override;

  protected:
    float scratch_[kMaxBlock * 2]; // per-chain render buffer (DrumRack reuses it)
};

} // namespace nota
