// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chord — a MIDI effect (midiKind 1), almanac mockups 1a / 1b. Each incoming note is
// played (Keep Root) plus up to six added voices ("shifts"): each slot has an on/off switch,
// a semitone offset (−12..+12) and a velocity offset (MIDI units, −64..+63). Shaping:
// Strum / Spread / Fold (see the implementation). What each key produced is remembered,
// so its note-off releases exactly those notes even if the shifts changed while held.
//
// Param layout is APPEND-ONLY so old projects load unchanged (see the .cpp).

#pragma once

#include "devices/MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

class MidiChord final : public MidiDevice {
  public:
    static constexpr int kVoices = 6;
    static constexpr int kMaxOut = kVoices + 1;
    enum Param {
        Voice1 = 0,
        Voice2,
        Voice3,
        Voice4,
        Voice5,
        Voice6, // semitone offsets
        Strum,
        KeepRoot,
        Spread,
        Fold,
        Vel1,
        Vel2,
        Vel3,
        Vel4,
        Vel5,
        Vel6, // velocity offsets (MIDI units)
        On1,
        On2,
        On3,
        On4,
        On5,
        On6, // slot switches
        FoldKey,
        FoldMode, // fold scale: key 0..11 (C..B), 0 major / 1 minor
        View,     // editor size (0 = L, 1 = S) — UI state
        kNumParams
    };

    MidiChord();

    int32_t midiKind() const override { return 1; }
    const char* displayName() const override { return "Nota Chord"; }

    void setSampleRate(double sr, int32_t) override { sr_ = sr > 0 ? sr : 44100.0; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    std::shared_ptr<MidiDevice> clone() const override;

    void reset() override;

    // Editor telemetry: [0] root of the latest chord (-1 = none yet), [1] keys held now,
    // [2] how many of its notes have sounded (the strum's progress), [3] n = chord size,
    // [4..4+n) its pitches low → high, [4+n..4+2n) their velocities 1..127.
    int32_t midiScope(float* out, int32_t maxN) const override;

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

  private:
    struct Key {
        int n = 0;
        int out[kMaxOut] = {};
    };

    template <class Emit> void releaseKey(const MidiEv& e, int64_t at, Emit& emit) {
        Key& kk = keys_[e.pitch];
        if (kk.n == 0) {
            emit(at, false, e.pitch, e.vel);
            return;
        } // not ours (e.g. after reset)
        for (int q = 0; q < kk.n; ++q) {
            int p = kk.out[q];
            if (outCount_[p] > 0 && --outCount_[p] > 0) continue; // another key still holds it
            if (cancelPending(p)) continue;                       // never sounded
            emit(at, false, p, e.vel);
        }
        kk.n = 0;
        if (held_ > 0) --held_;
        heldLive_.store(held_, std::memory_order_relaxed);
    }

    // Drop a pitch outside the scale a semitone — for major and natural minor that always
    // lands on a scale degree.
    static int foldInto(int p, int key, bool minor);
    static void sortByPitch(int* pit, float* vel, bool* pl, int n);
    bool cancelPending(int pitch);
    void resetLive();

    static constexpr int kMaxPend = 256;
    struct Pend {
        int64_t abs;
        int32_t pitch;
        float vel;
        uint32_t chord;
        float dur;
    };
    Pend pend_[kMaxPend];
    int nPend_ = 0;
    Key keys_[128];
    int outCount_[128] = {};
    int held_ = 0;
    int64_t clock_ = 0;
    uint32_t chordSeq_ = 0;
    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams];

    // Editor telemetry (audio thread writes, message thread reads).
    std::atomic<int> lastRoot_{-1}, heldLive_{0}, sounded_{0}, lastN_{0};
    std::atomic<int> lastPit_[kMaxOut] = {}, lastVel_[kMaxOut] = {};
};

} // namespace nota
