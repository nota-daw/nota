// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInstrument — a parallel-chain Instrument Rack. A container Instrument whose
// parallel chains each hold a built-in instrument followed by an insert chain of
// built-in devices. A note reaches every chain whose key/velocity zone holds it and the
// chain outputs are summed. With mono on (specs/040) the rack plays one note at a time,
// last-note priority, and glides between notes by per-note pitch bend. All the chain/macro/snapshot/persistence
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
    bool hostsPlugin() const override { return chainsHostPlugin(); } // Drum Rack too

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

    // A fork for an undoable edit plays on with the same held keys, sounding note and glide.
    void shareMonoWith(const RackInstrument& o) { mono_ = o.mono_; }

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

  private:
    // Fan a note-on out to the chains whose zone holds it; with glideFrom >= 0 the chains
    // that take per-note bend start bent to the previous pitch and glide back (mono).
    void fanOn(int32_t pitch, float velocity, int32_t glideFrom);
    void fanOff(int32_t pitch);
    void monoRemove(int32_t pitch);
    void syncMono(); // mono switched since the last call: reconcile the sounding keys

    // Note state (audio thread only), shared by every fork of the rack so an edit that swaps
    // the rack object keeps it: held keys in press order (poly too, so switching mono on knows
    // them), their velocities, the sounding pitch in mono, and the glide ramp (bend in
    // semitones from start to 0 over total samples).
    struct MonoState {
        int8_t held[128] = {};
        int32_t heldN = 0;
        float heldVel[128] = {};
        int32_t sounding = -1;
        float glideBend = 0.0f;
        int64_t glideTotal = 0, glideLeft = 0;
        bool wasMono = false;
    };
    std::shared_ptr<MonoState> mono_ = std::make_shared<MonoState>();
};

} // namespace nota
