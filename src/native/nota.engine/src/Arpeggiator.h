// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Arp — the built-in step arpeggiator (MidiDevice, midiKind 0). Classic "Step
// Arp"-style: global controls (rate/sync, octaves, note order, gate, swing, velocity
// amount, hold, retrigger Off/Note/Beat, transpose, loop length + mode) plus six 16-step
// Groove lanes (Velocity / Length / Chance / Ratchet / Transpose / On) and an inert
// Map-CC lane. It consumes the incoming held notes and emits a generated sequence.
//
// The note order walks the whole octave-expanded range (Up·Down over two octaves climbs
// C3…B4 and back down), one note per step; a ratchet repeats that step's note. Step k of
// the pattern plays note k of the order, so the editor can draw exactly what will play.
// midiScope() publishes the live step and the held chord for the editor's Pattern view;
// midiCommand(1) restarts the pattern at the next block.
//
// The step schedule is a PURE FUNCTION of the absolute step index (derived from the
// beat position), not an accumulator — so it is robust to transport seeks/loops and
// reproducible for offline export regardless of how blocks are split. Generated
// note-ons whose time lands beyond this block (ratchets) and their note-offs are
// deferred in pending queues (kept in absolute beats, so tempo changes stay correct).
// Only the held chord, those queues, and the stopped-but-live beat clock are mutable.

#pragma once

#include <memory>

#include "MidiDevice.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>

namespace nota {

class Arpeggiator final : public MidiDevice {
public:
    static constexpr int kSteps = 16;
    enum { GRate = 0, GSync, GFreeRate, GGate, GOctaves, GOctaveMode, GNoteOrder,
           GSwing, GHold, GKeyRetrig, GTranspose, GLoop, GLoopMode, kNumGlobals };
    enum { LVel = 0, LLen, LChance, LRatchet, LTransp, LOn, LCC, kNumLanes };
    // Appended after the lanes (param order = persisted layout, append only).
    static constexpr int PVelAmt = kNumGlobals + kNumLanes * kSteps;   // 125: Groove velocity depth
    static constexpr int PView   = PVelAmt + 1;                          // 126: editor size (0 = L, 1 = S) — UI state
    static constexpr int kNumParams = PView + 1;                         // 127

    // Order values are persisted: new orders are appended, the editor lists them musically.
    enum { OrdUp = 0, OrdDown, OrdUpDown, OrdConverge, OrdAsPlayed, OrdChord, OrdRandom, OrdDownUp, OrdDiverge };
    enum { RetrigOff = 0, RetrigNote, RetrigBeat };
    enum { OctUp = 0, OctDown, OctUpDown, OctRandom };
    enum { LoopFwd = 0, LoopBack, LoopPing, LoopRandom };

    Arpeggiator() {
        p_[GRate].store(5.0f);       // 1/16
        p_[GSync].store(1.0f);
        p_[GFreeRate].store(8.0f);
        p_[GGate].store(0.9f);
        p_[GOctaves].store(1.0f);
        p_[GOctaveMode].store(0.0f);
        p_[GNoteOrder].store((float)OrdUp);
        p_[GSwing].store(0.0f);
        p_[GHold].store(0.0f);
        p_[GKeyRetrig].store(1.0f);
        p_[GTranspose].store(0.0f);
        p_[GLoop].store(16.0f);
        p_[GLoopMode].store(0.0f);
        for (int s = 0; s < kSteps; ++s) {
            lane(LVel, s).store(0.8f);
            lane(LLen, s).store(1.0f);
            lane(LChance, s).store(1.0f);
            lane(LRatchet, s).store(1.0f);
            lane(LTransp, s).store(0.0f);
            lane(LOn, s).store(1.0f);
            lane(LCC, s).store(0.0f);
        }
        p_[PVelAmt].store(1.0f);
        p_[PView].store(0.0f);
    }

    int32_t midiKind() const override { return 0; }
    const char* displayName() const override { return "Nota Arp"; }
    void setSampleRate(double sr, int32_t /*maxBlock*/) override { sr_ = sr > 0 ? sr : 44100.0; }

    // ---- params ----------------------------------------------------------
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return "";
        if (i < kNumGlobals) {
            static const char* g[] = { "Rate", "Sync", "FreeRate", "Gate", "Octaves", "OctMode",
                                       "Order", "Swing", "Hold", "Retrig", "Transpose", "Loop", "LoopMode" };
            return g[i];
        }
        if (i == PVelAmt) return "VelAmt";
        if (i == PView) return "View";
        static thread_local char buf[16];
        static const char* ln[] = { "Vel", "Len", "Chn", "Rat", "Trn", "On", "CC" };
        std::snprintf(buf, sizeof(buf), "%s %d", ln[(i - kNumGlobals) / kSteps], (i - kNumGlobals) % kSteps + 1);
        return buf;
    }
    float paramMin(int32_t i) const override {
        if (i >= PVelAmt) return 0.0f;
        if (i < kNumGlobals) {
            switch (i) { case GTranspose: return -24.0f; case GFreeRate: return 0.1f; case GLoop: return 1.0f; default: return 0.0f; }
        }
        switch ((i - kNumGlobals) / kSteps) { case LTransp: return -24.0f; case LRatchet: return 1.0f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        if (i >= PVelAmt) return 1.0f;
        if (i < kNumGlobals) {
            switch (i) { case GRate: return 7.0f; case GFreeRate: return 50.0f; case GGate: return 2.0f;
                         case GOctaves: return 8.0f; case GOctaveMode: return 3.0f; case GNoteOrder: return 8.0f;
                         case GKeyRetrig: return 2.0f;
                         case GTranspose: return 24.0f; case GLoop: return 16.0f; case GLoopMode: return 3.0f; default: return 1.0f; }
        }
        switch ((i - kNumGlobals) / kSteps) { case LLen: return 2.0f; case LRatchet: return 8.0f;
                         case LTransp: return 24.0f; case LCC: return 127.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override {
        auto a = std::make_shared<Arpeggiator>();
        for (int i = 0; i < kNumParams; ++i) a->p_[i].store(p_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        a->setBypassed(bypassed());
        a->setCcDest(ccDev_.load(std::memory_order_relaxed), ccParam_.load(std::memory_order_relaxed));
        a->setCcDepth(ccDepth_.load(std::memory_order_relaxed));
        a->setSampleRate(sr_, 0);
        return a;   // transient live state (held/pending/phase) starts fresh
    }

    void reset() override {
        held_ = 0; physDown_ = 0; nOffs_ = 0; nOns_ = 0; arpBeat_ = 0.0; phaseRef_ = 0.0;
        curLp_.store(-1, std::memory_order_relaxed); curK_.store(-1, std::memory_order_relaxed);
        heldLive_.store(0, std::memory_order_relaxed);
    }

    // Editor commands: 1 = restart the pattern (step 1) at the next block.
    void midiCommand(int32_t cmd) override { if (cmd == 1) restartReq_.store(true, std::memory_order_relaxed); }

    // Editor telemetry: [0] loop position of the sounding step (-1 = idle), [1] its pattern
    // step counter, [2] notes held right now, [3] n = chord size, [4..4+n) the chord in held
    // order — the held notes, or the last chord played once released.
    int32_t midiScope(float* out, int32_t maxN) const override {
        if (!out || maxN < 4) return 0;
        out[0] = (float)curLp_.load(std::memory_order_relaxed);
        out[1] = (float)curK_.load(std::memory_order_relaxed);
        out[2] = (float)heldLive_.load(std::memory_order_relaxed);
        int n = std::min(chordN_.load(std::memory_order_acquire), std::min(kMaxHeld, (int)maxN - 4));
        out[3] = (float)n;
        for (int i = 0; i < n; ++i) out[4 + i] = (float)chord_[i].load(std::memory_order_relaxed);
        return 4 + n;
    }

    // Map/CC routing (the CC lane modulates a target audio-device param).
    int32_t ccDestDevice() const override { return ccDev_.load(std::memory_order_relaxed); }
    int32_t ccDestParam() const override { return ccParam_.load(std::memory_order_relaxed); }
    float   ccDepth() const override { return ccDepth_.load(std::memory_order_relaxed); }
    float   ccValue() const override { return ccOut_.load(std::memory_order_relaxed); }
    void    setCcDest(int32_t dev, int32_t param) override { ccDev_.store(dev, std::memory_order_relaxed); ccParam_.store(param, std::memory_order_relaxed); }
    void    setCcDepth(float d) override { ccDepth_.store(std::clamp(d, 0.0f, 1.0f), std::memory_order_relaxed); }

    // ---- the arpeggiator -------------------------------------------------
    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t frames, double beatStart, double spb, bool playing) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; return; }

        const double b0 = playing ? beatStart : arpBeat_;
        const double b1 = b0 + frames / spb;
        const int  retrig = std::clamp((int)std::lround(p_[GKeyRetrig].load(std::memory_order_relaxed)), 0, 2);
        const bool hold   = p_[GHold].load(std::memory_order_relaxed) > 0.5f;
        if (restartReq_.exchange(false, std::memory_order_relaxed)) { phaseRef_ = b0; nOns_ = 0; }

        // 1) Fold input into the held set (input notes are consumed by the arp).
        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            if (e.pitch < 0 || e.pitch > 127) continue;
            if (e.on) {
                const bool fresh = physDown_ == 0;
                if (fresh) held_ = 0;
                if (physDown_ < 127) physDown_++;
                addHeld(e.pitch, e.vel);
                if (fresh && retrig == RetrigNote) phaseRef_ = b0 + e.off / spb;   // restart the pattern at the key-press
            } else {
                if (physDown_ > 0) physDown_--;
                if (!hold) removeHeld(e.pitch);
            }
        }

        publishChord();

        // 2) Fire due pending note-ons (ratchet tails) and note-offs from earlier blocks.
        firePendingOns(out, nOut, maxOut, b0, b1, spb, frames);
        firePendingOffs(out, nOut, maxOut, b0, b1, spb, frames);

        // 3) Walk step boundaries in [b0, b1).
        const double stepBeats = currentStepBeats(spb);
        if (stepBeats > 1e-6 && held_ > 0) {
            const double swing = std::clamp(p_[GSwing].load(std::memory_order_relaxed), 0.0f, 1.0f);
            const double ref = retrig == RetrigBeat ? 0.0 : phaseRef_;   // Beat: bar-aligned grid
            long kLo = (long)std::floor((b0 - ref) / stepBeats) - 1;
            long kHi = (long)std::ceil((b1 - ref) / stepBeats) + 1;
            for (long k = kLo; k <= kHi; ++k) {
                if (k < 0) continue;
                double stepBeat = ref + k * stepBeats;
                if (k % 2 == 1) stepBeat += swing * stepBeats * 0.5;
                if (stepBeat < b0 || stepBeat >= b1) continue;
                // Beat retrigger: the pattern starts over on every bar (4 beats).
                long kp = k;
                if (retrig == RetrigBeat) {
                    const double bar = std::floor(k * stepBeats / 4.0 + 1e-9) * 4.0;
                    kp = k - (long)std::ceil(bar / stepBeats - 1e-9);
                    if (kp < 0) kp = 0;
                }
                const int loop = std::clamp((int)std::lround(p_[GLoop].load(std::memory_order_relaxed)), 1, kSteps);
                const int lp = loopPos(kp, loop);
                curLp_.store(lp, std::memory_order_relaxed);
                curK_.store((int32_t)std::min<long>(kp, 1 << 30), std::memory_order_relaxed);
                // Publish the step's CC-lane value (independent of note gating) for Map routing.
                ccOut_.store(lane(LCC, lp).load(std::memory_order_relaxed) / 127.0f, std::memory_order_relaxed);
                emitStep(k, kp, lp, stepBeat, stepBeats, b0, b1, spb, frames, out, nOut, maxOut);
            }
        } else if (held_ == 0 && nOns_ == 0 && nOffs_ == 0) {
            curLp_.store(-1, std::memory_order_relaxed);
        }
        arpBeat_ = b1;
    }

private:
    // --- held notes ---
    struct Held { int32_t pitch; float vel; uint32_t seq; };
    static constexpr int kMaxHeld = 32;
    Held heldBuf_[kMaxHeld]; int held_ = 0; int physDown_ = 0; uint32_t seqCtr_ = 0;
    void addHeld(int32_t pitch, float vel) {
        for (int i = 0; i < held_; ++i) if (heldBuf_[i].pitch == pitch) { heldBuf_[i].vel = vel; return; }
        if (held_ < kMaxHeld) heldBuf_[held_++] = { pitch, vel, seqCtr_++ };
    }
    void removeHeld(int32_t pitch) {
        for (int i = 0; i < held_; ++i) if (heldBuf_[i].pitch == pitch) {
            for (int j = i; j < held_ - 1; ++j) heldBuf_[j] = heldBuf_[j + 1]; held_--; return; }
    }

    // --- pending events (absolute beats) ---
    struct POn  { int32_t pitch; float vel; double onBeat; double offBeat; };
    struct POff { int32_t pitch; double beat; };
    static constexpr int kMaxPending = 128;
    POn  onBuf_[kMaxPending];  int nOns_ = 0;
    POff offBuf_[kMaxPending]; int nOffs_ = 0;

    void pushOn(int32_t pitch, float vel, double onBeat, double offBeat) {
        if (nOns_ < kMaxPending) onBuf_[nOns_++] = { pitch, vel, onBeat, offBeat };
    }
    void pushOff(int32_t pitch, double beat, MidiEv* out, int& nOut, int maxOut) {
        cancelOff(pitch);
        if (nOffs_ < kMaxPending) offBuf_[nOffs_++] = { pitch, beat };
        else if (nOut < maxOut) out[nOut++] = { 0, false, pitch, 0.0f };   // table full: off now
    }
    void cancelOff(int32_t pitch) {
        for (int i = 0; i < nOffs_; ++i) if (offBuf_[i].pitch == pitch) {
            for (int j = i; j < nOffs_ - 1; ++j) offBuf_[j] = offBuf_[j + 1]; nOffs_--; return; }
    }
    void firePendingOns(MidiEv* out, int& nOut, int maxOut, double b0, double b1, double spb, int32_t frames) {
        for (int i = 0; i < nOns_;) {
            if (onBuf_[i].onBeat < b1) {
                if (onBuf_[i].onBeat >= b0 && nOut < maxOut) {
                    out[nOut++] = { off(onBuf_[i].onBeat - b0, spb, frames), true, onBuf_[i].pitch, onBuf_[i].vel };
                    pushOff(onBuf_[i].pitch, onBuf_[i].offBeat, out, nOut, maxOut);
                }
                for (int j = i; j < nOns_ - 1; ++j) onBuf_[j] = onBuf_[j + 1]; nOns_--;
            } else ++i;
        }
    }
    void firePendingOffs(MidiEv* out, int& nOut, int maxOut, double b0, double b1, double spb, int32_t frames) {
        for (int i = 0; i < nOffs_;) {
            if (offBuf_[i].beat < b1) {
                if (offBuf_[i].beat >= b0 && nOut < maxOut) out[nOut++] = { off(offBuf_[i].beat - b0, spb, frames), false, offBuf_[i].pitch, 0.0f };
                for (int j = i; j < nOffs_ - 1; ++j) offBuf_[j] = offBuf_[j + 1]; nOffs_--;
            } else ++i;
        }
    }

    // --- one step --- (stepAbs seeds chance; kp = pattern step picks the note; lp = lane slot)
    void emitStep(long stepAbs, long kp, int lp, double stepBeat, double stepBeats, double b0, double b1, double spb, int32_t frames,
                  MidiEv* out, int& nOut, int maxOut) {
        if (lane(LOn, lp).load(std::memory_order_relaxed) < 0.5f) return;
        if (rnd01(stepAbs, 11) >= std::clamp(lane(LChance, lp).load(std::memory_order_relaxed), 0.0f, 1.0f)) return;

        const float gate = std::clamp(p_[GGate].load(std::memory_order_relaxed), 0.0f, 2.0f);
        const float lenL = std::clamp(lane(LLen, lp).load(std::memory_order_relaxed), 0.0f, 2.0f);
        const int   ratch = std::clamp((int)std::lround(lane(LRatchet, lp).load(std::memory_order_relaxed)), 1, 8);
        const float velL = std::clamp(lane(LVel, lp).load(std::memory_order_relaxed), 0.0f, 1.0f);
        const float amt  = std::clamp(p_[PVelAmt].load(std::memory_order_relaxed), 0.0f, 1.0f);
        const float velK = 1.0f - amt + amt * velL;   // Groove velocity, scaled by VelAmt
        const int   trn = (int)std::lround(p_[GTranspose].load(std::memory_order_relaxed) + lane(LTransp, lp).load(std::memory_order_relaxed));
        const int   order = (int)std::lround(p_[GNoteOrder].load(std::memory_order_relaxed));

        int seqLen = 0; buildSequence(seqBuf_, seqVel_, seqLen);
        if (seqLen <= 0) return;
        const int oc = 12 * octaveRandom(kp);

        const double subLen = stepBeats / ratch;
        long idx = (order == OrdRandom) ? (long)(rnd01(kp, 7) * seqLen) : kp % seqLen;
        idx = ((idx % seqLen) + seqLen) % seqLen;
        for (int r = 0; r < ratch; ++r) {
            const double onBeat  = stepBeat + r * subLen;
            const double offBeat = onBeat + std::max(0.05, (double)lenL) * subLen * gate;
            if (order == OrdChord) {
                for (int h = 0; h < seqLen; ++h)
                    place(seqBuf_[h] + oc + trn, velK * seqVel_[h], onBeat, offBeat, b0, b1, spb, frames, out, nOut, maxOut);
            } else {
                place(seqBuf_[idx] + oc + trn, velK * seqVel_[idx], onBeat, offBeat, b0, b1, spb, frames, out, nOut, maxOut);
            }
        }
    }

    // Emit a note now if its onset is in this block, else defer it to the pending-on queue.
    void place(int pitch, float vel, double onBeat, double offBeat, double b0, double b1, double spb, int32_t frames,
               MidiEv* out, int& nOut, int maxOut) {
        if (pitch < 0 || pitch > 127) return;
        vel = std::clamp(vel, 0.0f, 1.0f);
        if (onBeat >= b0 && onBeat < b1) {
            if (nOut < maxOut) out[nOut++] = { off(onBeat - b0, spb, frames), true, pitch, vel };
            pushOff(pitch, offBeat, out, nOut, maxOut);
        } else if (onBeat >= b1) {
            pushOn(pitch, vel, onBeat, offBeat);
        }
    }

    // --- note sequence: the held chord expanded over the octaves (blocks in OctMode order),
    //     then walked in the note order across that whole range. Chord = all of it at once.
    static constexpr int kSeqMax = 256;
    int seqBuf_[kSeqMax]; float seqVel_[kSeqMax];
    int rangeBuf_[kSeqMax]; float rangeVel_[kSeqMax];
    void buildSequence(int* seq, float* vel, int& len) {
        len = 0; if (held_ <= 0) return;
        int idx[kMaxHeld], m = held_;
        for (int i = 0; i < m; ++i) idx[i] = i;
        const int order = (int)std::lround(p_[GNoteOrder].load(std::memory_order_relaxed));
        if (order != OrdAsPlayed)
            std::sort(idx, idx + m, [&](int a, int b) { return heldBuf_[a].pitch < heldBuf_[b].pitch; });
        const int octs = std::clamp((int)std::lround(p_[GOctaves].load(std::memory_order_relaxed)), 1, 8);
        const int octMode = (int)std::lround(p_[GOctaveMode].load(std::memory_order_relaxed));
        int oo[16], on = 0;
        switch (octMode) {
            case OctDown: for (int o = octs - 1; o >= 0; --o) oo[on++] = o; break;
            case OctUpDown: for (int o = 0; o < octs; ++o) oo[on++] = o;
                            for (int o = octs - 2; o >= 1; --o) oo[on++] = o; break;
            case OctRandom: oo[on++] = 0; break;   // a random octave per step instead
            default: for (int o = 0; o < octs; ++o) oo[on++] = o; break;
        }
        int n = 0;
        for (int oi = 0; oi < on && n < kSeqMax; ++oi)
            for (int i = 0; i < m && n < kSeqMax; ++i) {
                rangeBuf_[n] = heldBuf_[idx[i]].pitch + 12 * oo[oi];
                rangeVel_[n] = heldBuf_[idx[i]].vel; ++n;
            }
        auto push = [&](int i) { if (len < kSeqMax) { seq[len] = rangeBuf_[i]; vel[len] = rangeVel_[i]; ++len; } };
        switch (order) {
            case OrdDown:   for (int i = n - 1; i >= 0; --i) push(i); break;
            case OrdUpDown: for (int i = 0; i < n; ++i) push(i); for (int i = n - 2; i >= 1; --i) push(i); break;
            case OrdDownUp: for (int i = n - 1; i >= 0; --i) push(i); for (int i = 1; i <= n - 2; ++i) push(i); break;
            case OrdConverge: case OrdDiverge: {
                int c[kSeqMax], cn = 0, lo = 0, hi = n - 1;
                while (lo <= hi) { c[cn++] = lo; if (lo != hi) c[cn++] = hi; ++lo; --hi; }
                if (order == OrdConverge) for (int i = 0; i < cn; ++i) push(c[i]);
                else for (int i = cn - 1; i >= 0; --i) push(c[i]);
                break;
            }
            default: for (int i = 0; i < n; ++i) push(i); break;   // Up / AsPlayed / Random / Chord
        }
    }
    int octaveRandom(long kp) {
        const int octs = std::clamp((int)std::lround(p_[GOctaves].load(std::memory_order_relaxed)), 1, 8);
        return ((int)std::lround(p_[GOctaveMode].load(std::memory_order_relaxed)) == OctRandom) ? (int)(rnd01(kp, 5) * octs) : 0;
    }

    // Snapshot the chord for the editor (audio thread → UI): the held notes, or — once all are
    // released — the last chord that played, so the Pattern view keeps showing it.
    void publishChord() {
        heldLive_.store(held_, std::memory_order_relaxed);
        if (held_ <= 0) return;
        for (int i = 0; i < held_; ++i) chord_[i].store(heldBuf_[i].pitch, std::memory_order_relaxed);
        chordN_.store(held_, std::memory_order_release);
    }

    int loopPos(long stepAbs, int loop) {
        const int mode = (int)std::lround(p_[GLoopMode].load(std::memory_order_relaxed));
        long m = ((stepAbs % loop) + loop) % loop;
        switch (mode) {
            case LoopBack: return (int)(loop - 1 - m);
            case LoopPing: { long period = std::max(1L, 2L * loop - 2); long q = ((stepAbs % period) + period) % period;
                             return (int)(q < loop ? q : period - q); }
            case LoopRandom: return (int)(rnd01(stepAbs, 3) * loop);
            default: return (int)m;
        }
    }
    double currentStepBeats(double spb) const {
        if (p_[GSync].load(std::memory_order_relaxed) > 0.5f) {
            static const double div[] = { 4.0, 2.0, 1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125 };
            return div[std::clamp((int)std::lround(p_[GRate].load(std::memory_order_relaxed)), 0, 7)];
        }
        return (sr_ / std::max(0.1f, p_[GFreeRate].load(std::memory_order_relaxed))) / std::max(1.0, spb);
    }

    static int32_t off(double beats, double spb, int32_t frames) {
        long v = (long)std::floor(beats * spb + 0.5); if (v < 0) v = 0; if (v >= frames) v = frames - 1; return (int32_t)v;
    }
    static uint32_t hash32(uint32_t x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
    static double rnd01(long stepAbs, int salt) { return (hash32((uint32_t)(stepAbs * 2654435761u) + (uint32_t)salt * 40503u) & 0xffffff) / (double)0x1000000; }

    std::atomic<float>& lane(int l, int s) { return p_[kNumGlobals + l * kSteps + s]; }
    const std::atomic<float>& lane(int l, int s) const { return p_[kNumGlobals + l * kSteps + s]; }

    double sr_ = 44100.0, arpBeat_ = 0.0, phaseRef_ = 0.0;
    std::atomic<float> ccOut_{0.0f}, ccDepth_{1.0f};
    std::atomic<bool> restartReq_{false};
    std::atomic<int32_t> curLp_{-1}, curK_{-1}, heldLive_{0}, chordN_{0};
    std::atomic<int32_t> chord_[kMaxHeld] {};
    std::atomic<int32_t> ccDev_{-2}, ccParam_{-1};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
