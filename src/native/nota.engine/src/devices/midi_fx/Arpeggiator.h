// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Arp — the built-in step arpeggiator (MidiDevice, midiKind 0). Classic "Step
// Arp"-style: global controls (rate/sync, octaves, note order, gate, swing, velocity
// amount, hold, retrigger Off/Note/Beat, transpose, loop length + mode) plus six 16-step
// Groove lanes (Velocity / Length / Chance / Ratchet / Transpose / On) and an inert
// Map-CC lane. It consumes the incoming held notes and emits a generated sequence.
//
// The step schedule is a PURE FUNCTION of the absolute step index (derived from the
// beat position), not an accumulator — robust to transport seeks/loops and reproducible
// for offline export regardless of how blocks are split.

#pragma once

#include <memory>

#include "devices/MidiDevice.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>

namespace nota {

class Arpeggiator final : public MidiDevice {
  public:
    static constexpr int kSteps = 16;
    enum {
        GRate = 0,
        GSync,
        GFreeRate,
        GGate,
        GOctaves,
        GOctaveMode,
        GNoteOrder,
        GSwing,
        GHold,
        GKeyRetrig,
        GTranspose,
        GLoop,
        GLoopMode,
        kNumGlobals
    };
    enum { LVel = 0, LLen, LChance, LRatchet, LTransp, LOn, LCC, kNumLanes };
    // Appended after the lanes (param order = persisted layout, append only).
    static constexpr int PVelAmt = kNumGlobals + kNumLanes * kSteps; // 125: Groove velocity depth
    static constexpr int PView = PVelAmt + 1;                        // 126: editor size (0 = L, 1 = S) — UI state
    static constexpr int kNumParams = PView + 1;                     // 127

    // Order values are persisted: new orders are appended, the editor lists them musically.
    enum { OrdUp = 0, OrdDown, OrdUpDown, OrdConverge, OrdAsPlayed, OrdChord, OrdRandom, OrdDownUp, OrdDiverge };
    enum { RetrigOff = 0, RetrigNote, RetrigBeat };
    enum { OctUp = 0, OctDown, OctUpDown, OctRandom };
    enum { LoopFwd = 0, LoopBack, LoopPing, LoopRandom };

    Arpeggiator();

    int32_t midiKind() const override { return 0; }
    const char* displayName() const override { return "Nota Arp"; }
    void setSampleRate(double sr, int32_t /*maxBlock*/) override { sr_ = sr > 0 ? sr : 44100.0; }

    // ---- params ----------------------------------------------------------
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
    // Loop wrap: the pattern continues from where it was — phase and pending events move
    // with the transport's beat clock.
    void timeJump(double d) override;

    // Editor commands: 1 = restart the pattern (step 1) at the next block.
    void midiCommand(int32_t cmd) override {
        if (cmd == 1) restartReq_.store(true, std::memory_order_relaxed);
    }

    // Editor telemetry: [0] loop position of the sounding step (-1 = idle), [1] its pattern
    // step counter, [2] notes held right now, [3] n = chord size, [4..4+n) the chord in held
    // order — the held notes, or the last chord played once released.
    int32_t midiScope(float* out, int32_t maxN) const override;

    // Map/CC routing (the CC lane modulates a target audio-device param).
    int32_t ccDestDevice() const override { return ccDev_.load(std::memory_order_relaxed); }
    int32_t ccDestParam() const override { return ccParam_.load(std::memory_order_relaxed); }
    float ccDepth() const override { return ccDepth_.load(std::memory_order_relaxed); }
    float ccValue() const override { return ccOut_.load(std::memory_order_relaxed); }
    void setCcDest(int32_t dev, int32_t param) override {
        ccDev_.store(dev, std::memory_order_relaxed);
        ccParam_.store(param, std::memory_order_relaxed);
    }
    void setCcDepth(float d) override { ccDepth_.store(std::clamp(d, 0.0f, 1.0f), std::memory_order_relaxed); }

    // ---- the arpeggiator -------------------------------------------------
    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

  private:
    // --- held notes ---
    struct Held {
        int32_t pitch;
        float vel;
        uint32_t seq;
    };
    static constexpr int kMaxHeld = 32;
    Held heldBuf_[kMaxHeld];
    int held_ = 0;
    int physDown_ = 0;
    uint32_t seqCtr_ = 0;
    void addHeld(int32_t pitch, float vel);
    void removeHeld(int32_t pitch);

    // --- pending events (absolute beats) ---
    struct POn {
        int32_t pitch;
        float vel;
        double onBeat;
        double offBeat;
    };
    struct POff {
        int32_t pitch;
        double beat;
    };
    static constexpr int kMaxPending = 128;
    POn onBuf_[kMaxPending];
    int nOns_ = 0;
    POff offBuf_[kMaxPending];
    int nOffs_ = 0;

    void pushOn(int32_t pitch, float vel, double onBeat, double offBeat) {
        if (nOns_ < kMaxPending) onBuf_[nOns_++] = {pitch, vel, onBeat, offBeat};
    }
    void pushOff(int32_t pitch, double beat, MidiEv* out, int& nOut, int maxOut);
    void cancelOff(int32_t pitch);
    void firePendingOns(MidiEv* out, int& nOut, int maxOut, double b0, double b1, double spb, int32_t frames);
    void firePendingOffs(MidiEv* out, int& nOut, int maxOut, double b0, double b1, double spb, int32_t frames);

    // --- one step --- (stepAbs seeds chance; kp = pattern step picks the note; lp = lane slot)
    void emitStep(long stepAbs, long kp, int lp, double stepBeat, double stepBeats, double b0, double b1, double spb,
        int32_t frames, MidiEv* out, int& nOut, int maxOut);

    // Emit a note now if its onset is in this block, else defer it to the pending-on queue.
    void place(int pitch, float vel, double onBeat, double offBeat, double b0, double b1, double spb, int32_t frames,
        MidiEv* out, int& nOut, int maxOut);

    // --- note sequence: the held chord expanded over the octaves (blocks in OctMode order),
    //     then walked in the note order across that whole range. Chord = all of it at once.
    static constexpr int kSeqMax = 256;
    int seqBuf_[kSeqMax];
    float seqVel_[kSeqMax];
    int rangeBuf_[kSeqMax];
    float rangeVel_[kSeqMax];
    void buildSequence(int* seq, float* vel, int& len);
    int octaveRandom(long kp);

    // Snapshot the chord for the editor (audio thread → UI): the held notes, or — once all are
    // released — the last chord that played, so the Pattern view keeps showing it.
    void publishChord();

    int loopPos(long stepAbs, int loop);
    double currentStepBeats(double spb) const;

    static int32_t off(double beats, double spb, int32_t frames) {
        long v = (long)std::floor(beats * spb + 0.5);
        if (v < 0) v = 0;
        if (v >= frames) v = frames - 1;
        return (int32_t)v;
    }
    static uint32_t hash32(uint32_t x) {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }
    static double rnd01(long stepAbs, int salt) {
        return (hash32((uint32_t)(stepAbs * 2654435761u) + (uint32_t)salt * 40503u) & 0xffffff) / (double)0x1000000;
    }

    std::atomic<float>& lane(int l, int s) { return p_[kNumGlobals + l * kSteps + s]; }
    const std::atomic<float>& lane(int l, int s) const { return p_[kNumGlobals + l * kSteps + s]; }

    double sr_ = 44100.0, arpBeat_ = 0.0, phaseRef_ = 0.0;
    std::atomic<float> ccOut_{0.0f}, ccDepth_{1.0f};
    std::atomic<bool> restartReq_{false};
    std::atomic<int32_t> curLp_{-1}, curK_{-1}, heldLive_{0}, chordN_{0};
    std::atomic<int32_t> chord_[kMaxHeld]{};
    std::atomic<int32_t> ccDev_{-2}, ccParam_{-1};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
