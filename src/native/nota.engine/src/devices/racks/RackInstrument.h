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
    RackInstrument();
    int32_t kind() const override { return 3; } // built-in Instrument Rack (project compat)
    const RackCore* asRack() const override { return this; }
    const char* displayName() const override { return "Nota Instrument Rack"; }
    bool hostsPlugin() const override { return chainsHostPlugin(); } // Drum Rack too

    void setSampleRate(double sr) override { setRates(sr, kMaxBlock); }

    // Forward the DAW transport to every chain child (nested plugins sync too).
    void setTransportInfo(const TransportInfo& ti) override { forwardTransport(ti); }
    void setTransport(double beatStart, double spb, bool playing) override { forwardTempo(beatStart, spb, playing); }

    // Chain MIDI effects (issue #329): a chain with any runs them on its share of the block's
    // notes up front; render then plays their output into that chain's instrument, sample-accurate.
    bool takesBlockMidi() const override;
    void blockMidi(const MidiEv* evs, int n, int32_t frames, double beatStart, double spb, bool playing) override;
    void midiTimeJump(double deltaBeats) override;
    // MIDI effects run in the first 16 chains; the engine refuses one further down, and a chain
    // with effects moved past them plays its notes as a plain chain.
    static constexpr int32_t kMaxMidiChains = 16;

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

    // ---- parts (specs/050): each chain is a sidechain sub-source, by its chain uid ----------
    int32_t partCount() const override;
    uint64_t partId(int32_t index) const override { return chainBaseUid(index); }
    int32_t partIndex(uint64_t id) const override;
    std::string partName(int32_t index) const override; // the chain name, else its instrument's
    void setPartTaps(const PartTap* taps, int32_t n) override {
        taps_ = taps;
        nTaps_ = n;
    }

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

    // Where this block's keyed chains go (specs/050), or nullptr: audio thread, set by the
    // engine around render. A short scan — at most one entry per route slot.
    const PartTap* tapFor(uint64_t chainUid) const {
        for (int32_t i = 0; i < nTaps_; ++i)
            if (taps_[i].part == chainUid) return &taps_[i];
        return nullptr;
    }
    static void addTap(float* dst, const float* src, int32_t frames) {
        for (int32_t i = 0; i < frames * 2; ++i) dst[i] += src[i];
    }
    const PartTap* taps_ = nullptr;
    int32_t nTaps_ = 0;

  private:
    // Fan a note-on out to the chains whose zone holds it; with glideFrom >= 0 the chains
    // that take per-note bend start bent to the previous pitch and glide back (mono).
    void fanOn(int32_t pitch, float velocity, int32_t glideFrom);
    void fanOff(int32_t pitch);
    void monoRemove(int32_t pitch);
    void syncMono(); // mono switched since the last call: reconcile the sounding keys
    void endGlide(); // a glide stops half-way: the sounding note returns to its own pitch
    // The chains or their MIDI effects changed since the last call: carry the held routes over
    // to the chains' new places and end the notes whose MIDI effects went away.
    void syncChains();
    void deliver(int32_t c, Instrument& inst, const MidiEv& ev); // a MIDI chain's event into its instrument

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
        // MIDI chains (#329): the chains each held key went to, and the pitches each chain's
        // MIDI effects started in its instrument and haven't ended. They are kept for the chains
        // as they were at `structure`: chain c is the one playing `chainId[c]` (its instrument)
        // with MIDI effects `chainSig[c]` (0 = a plain chain).
        uint16_t midiRoute[128] = {};
        uint8_t outHeld[kMaxMidiChains][128] = {};
        const void* chainId[kMaxMidiChains] = {};
        uint64_t chainSig[kMaxMidiChains] = {};
        uint64_t structure = 0;
    };
    std::shared_ptr<MonoState> mono_ = std::make_shared<MonoState>();

    // Chain MIDI effects (audio thread): each MIDI chain's processed events for this block (its
    // own kChainEvents slots, sorted — a chain's effects emit at most that many per block, so
    // nothing, a release least of all, is dropped), the render position within the block, and
    // the chains each input event goes to.
    static constexpr int32_t kChainEvents = 1024;
    MidiEv* chainEvs_ = nullptr;                      // kMaxMidiChains × kChainEvents, allocated once in the ctor
    MidiEv* scratchA_ = nullptr;                      // 1024
    MidiEv* scratchB_ = nullptr;                      // 1024
    std::unique_ptr<MidiEv[]> evStore_;
    int32_t chainCount_[kMaxMidiChains] = {}, chainCursor_[kMaxMidiChains] = {};
    int32_t blockPos_ = 0;
    uint16_t evMask_[1024] = {}; // per input event: the MIDI chains it goes to (in block order)
    bool midiChainAt(int32_t c) const;
};

} // namespace nota
