// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Velocity — a MIDI effect (midiKind 4), almanac rework (mockups 1a / 1b). Reshapes
// note-on velocity through a transfer curve: Curve (x^(1/drive) — drive > 1 lifts soft notes,
// < 1 pushes them down), Compand (the same around the middle — drive > 1 expands towards the
// edges, < 1 squeezes towards the middle) or Fixed (one value). The result is remapped into
// the Out range and can be humanised by Random (±0..64 velocity, Both / Up / Down), gated by
// its own switch so the amount is kept while it is off. The output never leaves the range and
// never reaches 0 (a velocity-0 note-on would be a note-off). The last note (in→out) and the
// last 12 (in, out) pairs are published for the editor and MCP.

#pragma once

#include "devices/MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

class MidiVelocity final : public MidiDevice {
  public:
    enum Param {
        PDrive = 0, // was "Scale" — curve/compand shape (1 = linear); old projects map cleanly
        PFixed,     // Fixed-mode output value
        POutLo,     // was "Fixed amt" — output range minimum (0 default = no floor)
        PRandom,    // random amount
        PMode,      // 0 Curve / 1 Compand / 2 Fixed
        POutHi,     // output range maximum
        PRandomDir, // 0 Both / 1 Up / 2 Down
        PRandomOn,  // Random switch (the amount survives while it is off)
        PView,      // card size: 0 = L, 1 = S (editor state)
        kNumParams
    };
    static constexpr float kDriveMin = 0.25f, kDriveMax = 4.0f; // 4^(2f-1), f = 0..1
    static constexpr float kRandomSpan = 64.0f;                 // Random 1.0 = ±64 velocity
    static constexpr int kHist = 12;

    MidiVelocity();

    int32_t midiKind() const override { return 4; }
    const char* displayName() const override { return "Nota Velocity"; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override { return i == PDrive ? kDriveMin : 0.0f; }
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    // Live telemetry: last note in/out velocity (1..127, -1 = none yet). The scope is the last
    // 12 (in, out) pairs oldest → newest, 0..1 (unused slots 0, 0), then [24] = the number of
    // notes shaped since the device was created (the editor spots a new note by it).
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }
    int32_t midiScope(float* out, int32_t maxN) const override;

    std::shared_ptr<MidiDevice> clone() const override;

    // Transport stop / loop wrap: the note history (the card's dots) stays — it is what was played.
    void reset() override {}

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

    // Transfer function, shared with the card's curve drawing.
    static float transfer(float n, int mode, float drive, float fixed);

  private:
    // histHead_ = the next write slot, which is also the oldest entry — so scopeRead from
    // head forward wraps chronologically oldest→newest.
    void pushHist(float in, float outv);
    float histIn_[kHist] = {}, histOut_[kHist] = {};
    std::atomic<uint32_t> histHead_{0};
    std::atomic<int32_t> lastIn_{-1}, lastOut_{-1};
    std::atomic<uint32_t> count_{0};
    uint32_t rng_ = 0x2545f491u;
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
