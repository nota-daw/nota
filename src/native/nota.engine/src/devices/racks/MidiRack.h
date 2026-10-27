// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MidiRack — the MIDI Effect Rack (specs/040, MIDI kind 6). A MIDI effect whose chains each
// run a series of built-in MIDI effects on the notes their key / velocity zone (and, in
// Select mode, their select range around the selector) lets in; the chains' outputs merge
// into the track's note stream before the instrument. A chain transposes its output and
// scales its velocity ("Volume": 0 = silent). Notes no chain takes pass through when
// "pass through" is on; two chains sending one pitch play it once when "merge" is on.
//
// Structure, macros, mappings, persistence and undo come from RackCore. The note
// bookkeeping (which chains a key went to, which pitches each chain holds) lives in a
// Runtime shared by every fork of the rack, so an undoable edit that swaps the rack object
// keeps playing notes; a change of chains or devices cuts the held notes instead of leaving
// any hanging. Audio thread: fixed buffers only, no allocation (AR-6).

#pragma once

#include "RackCore.h"

#include <array>
#include <cstdint>
#include <memory>

namespace nota {

class MidiRack final : public MidiDevice, public RackCore {
  public:
    static constexpr int32_t kKind = 6;
    static constexpr int32_t kMaxChains = 16;
    static constexpr int32_t kMaxEvents = 1024;     // per chain, as the track's MIDI chain
    static constexpr int32_t kPoolEvents = 4096;    // every chain's output before the merge

    MidiRack();

    int32_t midiKind() const override { return kKind; }
    const RackCore* asRack() const override { return this; }
    const char* displayName() const override { return "Nota MIDI Effect Rack"; }
    void setSampleRate(double sr, int32_t maxBlock) override { setRates(sr, maxBlock); }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;
    void reset() override;
    void timeJump(double deltaBeats) override;

    // The eight macros are the rack's parameters (automation, MIDI Learn, presets).
    int32_t paramCount() const override { return kNumMacros; }
    const char* paramName(int32_t index) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t index) const override { return macroGet(index); }
    void setParam(int32_t index, float value) override { macroSet(index, value); }

    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override { deserialize(data, size); }
    std::shared_ptr<MidiDevice> clone() const override;

    // A fork for an undoable edit plays on with the same held-note tables.
    void shareRuntimeWith(const MidiRack& o) { rt_ = o.rt_; }

    // Live note activity of a chain, 0..1 (decays), for the card's chain rows.
    float chainActivity(int32_t c) const { return chainMeter(c); }

  private:
    // Held-note bookkeeping, audio thread only.
    struct Runtime {
        uint16_t route[128] = {};              // input pitch → the chains its note-on went to
        bool thru[128] = {};                   // input pitch passed through (no chain took it)
        int8_t outPitch[kMaxChains][128];      // chain c, device-output pitch → transposed pitch sent (−1 none)
        uint8_t held[128] = {};                // merged output: how many sources hold each pitch
        uint8_t rawHeld[128] = {};             // notes passed on untouched while bypassed
        bool bypassed = false;                 // the last block ran bypassed
        uint64_t structure = 0;                // chains + devices the tables were filled for
        Runtime() { clearOut(); }
        void clearOut() {
            for (auto& row : outPitch)
                for (auto& p : row) p = -1;
        }
    };
    struct Tagged {
        MidiEv ev;
        int16_t chain; // −1 = pass-through
        int32_t seq;   // arrival order: equal times keep it (std::sort, no allocation)
    };

    uint64_t structureOf(const RackState& st) const;
    void cutAll(const RackState& st, MidiEv* out, int& nOut, int maxOut);
    void emitMerged(const MidiEv& ev, MidiEv* out, int& nOut, int maxOut);

    std::shared_ptr<Runtime> rt_ = std::make_shared<Runtime>();
    MidiEv bufA_[kMaxEvents];
    MidiEv bufB_[kMaxEvents];
    Tagged pool_[kPoolEvents];
    uint16_t evMask_[kMaxEvents] = {}; // per input event: the chains it goes to
    uint16_t order_[kMaxEvents] = {};  // the input events in time order
    std::array<char[24], kNumMacros> paramNames_{};
};

} // namespace nota
