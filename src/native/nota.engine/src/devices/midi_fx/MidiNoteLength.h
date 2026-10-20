// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Length — a MIDI effect (midiKind 3), almanac rework (mockups 1a / 1b). Forces every
// note to a chosen length: Sync (a tempo division), ms (absolute) or Gate % (a proportion of
// how long the note was actually held). The forced note starts from the note-on (normal) or
// from the note-off (it fires when the key is released, the length added after the played
// note). Length is shaped per note by velocity and key (bipolar) and by Random (± spread
// per note). Legato stretches each note to the start of the next one; Clip length limit
// stops a forced note from crossing the bar line it started in (4/4).
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

class MidiNoteLength final : public MidiDevice {
  public:
    enum Param {
        PRate = 0,
        PGate,
        PMode,
        PMs,
        PPercent,
        PTrigger,
        PVelToLen,
        PKeyToLen,
        PRandom,
        PLegato,
        PClipLimit,
        PDivision,
        PView,
        kNumParams
    };
    // Sync divisions (beats): 1/32, 1/16, 1/8, 1/4, 1/2, 1/1, 1/8 dotted, 1/4 triplet.
    static constexpr int kNumDiv = 8;
    static constexpr double kDiv[kNumDiv] = {0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 0.75, 2.0 / 3.0};
    static constexpr double kBar = 4.0; // beats per bar for Legato / Clip (4/4)

    MidiNoteLength();

    int32_t midiKind() const override { return 3; }
    const char* displayName() const override { return "Nota Length"; }

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
    void reset() override {
        nPending_ = 0;
        nHeld_ = 0;
        nSound_ = 0;
        intBeat_ = 0.0;
        sounding_.store(0, std::memory_order_relaxed);
    }
    void timeJump(double d) override;

    // Editor / MCP telemetry: [0] last input pitch, [1] its velocity 0..1, [2] its held length
    // (beats, −1 = still held), [3] last finished output pitch, [4] its actual length (beats),
    // [5] notes sounding now, [6] notes finished since the device was created.
    int32_t midiScope(float* out, int32_t maxN) const override;
    int32_t midiLastIn() const override { return lastInPitch_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOutPitch_.load(std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override;

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

  private:
    // Length in beats for one note, with the velocity / key / random modifiers (mirrors the card's model).
    double len(float vel, int pitch, double heldBeats, int mode, double spb);
    static double barEnd(double beat) { return (std::floor(beat / kBar + 1e-9) + 1.0) * kBar; }

    // A forced note starts: legato cuts whatever still sounds, a same-pitch note is retriggered.
    void start(int32_t pitch, float vel, double beat, double b0, double spb, int32_t frames, bool legato, MidiEv* out,
        int& nOut, int maxOut);
    // Queue the off: lenBeats < 0 = legato (held until the next note starts, at most to the bar line).
    void schedule(int32_t pitch, double onBeat, double lenBeats, bool clip, bool legato);
    void emitOff(int k, double beat, double b0, double spb, int32_t frames, MidiEv* out, int& nOut, int maxOut);
    bool sounding(int32_t pitch) const {
        for (int s = 0; s < nSound_; ++s)
            if (sound_[s].pitch == pitch) return true;
        return false;
    }
    int findPending(int32_t pitch) const {
        for (int i = 0; i < nPending_; ++i)
            if (pending_[i].pitch == pitch) return i;
        return -1;
    }

    struct Held {
        int32_t pitch;
        double onBeat;
        float vel;
    };
    Held* findHeld(int32_t pitch) {
        for (int i = 0; i < nHeld_; ++i)
            if (held_[i].pitch == pitch) return &held_[i];
        return nullptr;
    }
    void addHeld(int32_t pitch, double onBeat, float vel) {
        if (auto* h = findHeld(pitch)) {
            h->onBeat = onBeat;
            h->vel = vel;
            return;
        }
        if (nHeld_ < kMaxHeld) held_[nHeld_++] = {pitch, onBeat, vel};
    }
    void removeHeld(int32_t pitch) {
        for (int i = 0; i < nHeld_; ++i)
            if (held_[i].pitch == pitch) {
                held_[i] = held_[--nHeld_];
                return;
            }
    }
    static int32_t at(double beat, double b0, double spb, int32_t frames) {
        long v = (long)std::floor((beat - b0) * spb + 0.5);
        if (v < 0) v = 0;
        if (v >= frames) v = frames - 1;
        return (int32_t)v;
    }
    float frand() {
        rng_ ^= rng_ << 13;
        rng_ ^= rng_ >> 17;
        rng_ ^= rng_ << 5;
        return (rng_ & 0xFFFFFF) / (float)0x1000000;
    }

    struct Off {
        int32_t pitch;
        double on, off;
    };
    struct Sound {
        int32_t pitch;
        double on;
    };
    static constexpr int kMaxPending = 128, kMaxHeld = 64;
    Off pending_[kMaxPending];
    int nPending_ = 0;
    Sound sound_[kMaxPending];
    int nSound_ = 0; // forced notes started and not yet released
    Held held_[kMaxHeld];
    int nHeld_ = 0;
    double intBeat_ = 0.0, sr_ = 44100.0;
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<float> p_[kNumParams];
    std::atomic<int32_t> lastInPitch_{-1}, lastOutPitch_{-1}, sounding_{0}, finished_{0};
    std::atomic<float> lastInVel_{0.0f}, lastInHeld_{-1.0f}, lastOutLen_{0.0f};
};

} // namespace nota
