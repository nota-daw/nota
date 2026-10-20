// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Scale — a MIDI effect (midiKind 2), almanac mockups 1a / 1b. Snaps incoming pitches
// into a scale — a preset (Scale 0..9) or the editable 12-note mask (Scale 10 = Custom,
// stored relative to Root) — folding an out-of-scale note to the Nearest / Down / Up
// in-scale note, then adding Transpose. Extras: Range, Follow Key, Learn (see the
// implementation). What each key produced is remembered, so its note-off releases exactly
// that note even if the scale changed while it was held.
//
// Param layout is APPEND-ONLY (see the .cpp).

#pragma once

#include "devices/MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

class MidiScale final : public MidiDevice {
  public:
    enum Param {
        PRoot = 0,
        PScale,
        PTranspose,
        PMask0,
        PMask1,
        PMask2,
        PMask3,
        PMask4,
        PMask5,
        PMask6,
        PMask7,
        PMask8,
        PMask9,
        PMask10,
        PMask11,
        PFold,
        PFollowKey,
        PRangeLo,
        PRangeHi,
        PLearn,
        PView,
        kNumParams
    };
    static constexpr int kNumScales = 10; // 0..9 presets; 10 = Custom (mask params)
    static constexpr int kCustom = 10;
    static constexpr int kScopeN = 32;

    MidiScale();

    int32_t midiKind() const override { return 2; }
    const char* displayName() const override { return "Nota Scale"; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override { return i == PTranspose ? -24.0f : 0.0f; }
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    // Live IN→OUT readout for the card (last note-on); -1 = none yet.
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }

    // Editor scope: [0] last in, [1] last out (-1 none), [2] the root in effect (Follow Key
    // moves it), [3] the scale mask in effect (12 bits relative to the root), [4] pitch
    // classes of the keys held now (12 bits, absolute), [5] pitch classes they sound as,
    // [6] learning (0/1), [7] note-ons seen, [8..20) note-ons per input pitch class,
    // [20..32) per output pitch class — counters, so a note shorter than a UI frame still flashes.
    int32_t midiScope(float* out, int32_t maxN) const override;

    std::shared_ptr<MidiDevice> clone() const override;

    void reset() override;

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

  private:
    static uint16_t presetMask(int sc);
    int root() const { return ((int)std::lround(p_[PRoot].load(std::memory_order_relaxed)) % 12 + 12) % 12; }
    uint16_t mask() const;

    // The pitch a note-on plays as: snapped (inside the range, with a non-empty scale), then transposed.
    int map(int pitch) const;
    static int quantize(int pitch, int root, uint16_t bits, int fold);

    template <class Emit> void release(const MidiEv& e, Emit& emit) {
        const int p = outFor_[e.pitch];
        if (p < 0) {
            emit(e, e.pitch);
            return;
        } // never mapped (e.g. held before the device) — pass it on
        outFor_[e.pitch] = -1;
        if (outRef_[p] > 0 && --outRef_[p] == 0) emit(e, p);
    }

    void learnNote(int pitch);
    void followNote(int pitch);
    void publish();
    void resetLive();

    // audio-thread state
    int8_t outFor_[128];  // input key → the pitch it sounds as (-1 = not held)
    uint8_t outRef_[128]; // output pitch → how many held keys share it
    float hist_[12] = {};
    bool learnWas_ = false, learnFresh_ = false;

    // telemetry
    std::atomic<int32_t> lastIn_{-1}, lastOut_{-1};
    std::atomic<int32_t> rootLive_{0}, maskLive_{1453}, heldIn_{0}, heldOut_{0};
    std::atomic<uint32_t> onCount_{0};
    std::atomic<uint32_t> inHits_[12] = {}, outHits_[12] = {};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
