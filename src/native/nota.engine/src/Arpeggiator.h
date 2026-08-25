// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Arp — the built-in step arpeggiator (MidiDevice, midiKind 0). Classic "Step
// Arp"-style: global controls (rate/sync, octaves, note order, gate, swing, hold,
// key-retrig, transpose, loop length + mode) plus six 16-step lanes (Velocity /
// Length / Chance / Ratchet / Transpose / On) and an inert Map-CC lane. It consumes
// the incoming held notes and emits a generated sequence to the instrument.
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
    static constexpr int kNumParams = kNumGlobals + kNumLanes * kSteps;   // 13 + 7*16 = 125

    enum { OrdUp = 0, OrdDown, OrdUpDown, OrdConverge, OrdAsPlayed, OrdChord, OrdRandom };
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
            lane(LLen, s).store(0.9f);
            lane(LChance, s).store(1.0f);
            lane(LRatchet, s).store(1.0f);
            lane(LTransp, s).store(0.0f);
            lane(LOn, s).store(1.0f);
            lane(LCC, s).store(0.0f);
        }
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
        static thread_local char buf[16];
        static const char* ln[] = { "Vel", "Len", "Chn", "Rat", "Trn", "On", "CC" };
        std::snprintf(buf, sizeof(buf), "%s %d", ln[(i - kNumGlobals) / kSteps], (i - kNumGlobals) % kSteps + 1);
        return buf;
    }
    float paramMin(int32_t i) const override {
        if (i < kNumGlobals) {
            switch (i) { case GTranspose: return -24.0f; case GFreeRate: return 0.1f; case GLoop: return 1.0f; default: return 0.0f; }
        }
        switch ((i - kNumGlobals) / kSteps) { case LTransp: return -24.0f; case LRatchet: return 1.0f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        if (i < kNumGlobals) {
            switch (i) { case GRate: return 7.0f; case GFreeRate: return 40.0f; case GGate: return 2.0f;
                         case GOctaves: return 8.0f; case GOctaveMode: return 3.0f; case GNoteOrder: return 6.0f;
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

    void reset() override { held_ = 0; physDown_ = 0; nOffs_ = 0; nOns_ = 0; arpBeat_ = 0.0; phaseRef_ = 0.0; }

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
        const bool retrig = p_[GKeyRetrig].load(std::memory_order_relaxed) > 0.5f;
        const bool hold   = p_[GHold].load(std::memory_order_relaxed) > 0.5f;

        // 1) Fold input into the held set (input notes are consumed by the arp).
        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            if (e.pitch < 0 || e.pitch > 127) continue;
            if (e.on) {
                const bool fresh = physDown_ == 0;
                if (fresh) held_ = 0;
                if (physDown_ < 127) physDown_++;
                addHeld(e.pitch, e.vel);
                if (fresh && retrig) phaseRef_ = b0 + e.off / spb;   // restart the pattern at the key-press
            } else {
                if (physDown_ > 0) physDown_--;
                if (!hold) removeHeld(e.pitch);
            }
        }

        // 2) Fire due pending note-ons (ratchet tails) and note-offs from earlier blocks.
        firePendingOns(out, nOut, maxOut, b0, b1, spb, frames);
        firePendingOffs(out, nOut, maxOut, b0, b1, spb, frames);

        // 3) Walk step boundaries in [b0, b1).
        const double stepBeats = currentStepBeats(spb);
        if (stepBeats > 1e-6 && held_ > 0) {
            const double swing = std::clamp(p_[GSwing].load(std::memory_order_relaxed), 0.0f, 1.0f);
            long kLo = (long)std::floor((b0 - phaseRef_) / stepBeats) - 1;
            long kHi = (long)std::ceil((b1 - phaseRef_) / stepBeats) + 1;
            for (long k = kLo; k <= kHi; ++k) {
                if (k < 0) continue;
                double stepBeat = phaseRef_ + k * stepBeats;
                if (k % 2 == 1) stepBeat += swing * stepBeats * 0.5;
                if (stepBeat < b0 || stepBeat >= b1) continue;
                // Publish the step's CC-lane value (independent of note gating) for Map routing.
                const int loop = std::clamp((int)std::lround(p_[GLoop].load(std::memory_order_relaxed)), 1, kSteps);
                ccOut_.store(lane(LCC, loopPos(k, loop)).load(std::memory_order_relaxed) / 127.0f, std::memory_order_relaxed);
                emitStep(k, stepBeat, stepBeats, b0, b1, spb, frames, out, nOut, maxOut);
            }
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

    // --- one step ---
    void emitStep(long stepAbs, double stepBeat, double stepBeats, double b0, double b1, double spb, int32_t frames,
                  MidiEv* out, int& nOut, int maxOut) {
        const int loop = std::clamp((int)std::lround(p_[GLoop].load(std::memory_order_relaxed)), 1, kSteps);
        const int lp = loopPos(stepAbs, loop);
        if (lane(LOn, lp).load(std::memory_order_relaxed) < 0.5f) return;
        if (rnd01(stepAbs, 11) >= std::clamp(lane(LChance, lp).load(std::memory_order_relaxed), 0.0f, 1.0f)) return;

        const float gate = std::clamp(p_[GGate].load(std::memory_order_relaxed), 0.0f, 2.0f);
        const float lenL = std::clamp(lane(LLen, lp).load(std::memory_order_relaxed), 0.0f, 2.0f);
        const int   ratch = std::clamp((int)std::lround(lane(LRatchet, lp).load(std::memory_order_relaxed)), 1, 8);
        const float velL = std::clamp(lane(LVel, lp).load(std::memory_order_relaxed), 0.0f, 1.0f);
        const int   trn = (int)std::lround(p_[GTranspose].load(std::memory_order_relaxed) + lane(LTransp, lp).load(std::memory_order_relaxed));
        const int   order = (int)std::lround(p_[GNoteOrder].load(std::memory_order_relaxed));

        int seqLen = 0; buildSequence(seqBuf_, seqVel_, seqLen);
        if (seqLen <= 0) return;

        const double subLen = stepBeats / ratch;
        for (int r = 0; r < ratch; ++r) {
            const double onBeat  = stepBeat + r * subLen;
            const double offBeat = onBeat + std::max(0.05, (double)lenL) * subLen * gate;
            if (order == OrdChord) {
                const int oc = octaveRandom(stepAbs);
                for (int h = 0; h < held_; ++h)
                    place(heldBuf_[h].pitch + 12 * oc + trn, velL * heldBuf_[h].vel, onBeat, offBeat, b0, b1, spb, frames, out, nOut, maxOut);
            } else {
                long idx = (order == OrdRandom) ? (long)(rnd01(stepAbs * 8 + r, 7) * seqLen) : ((stepAbs * ratch + r) % seqLen);
                idx = ((idx % seqLen) + seqLen) % seqLen;
                place(seqBuf_[idx] + trn, velL * seqVel_[idx], onBeat, offBeat, b0, b1, spb, frames, out, nOut, maxOut);
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

    // --- note sequence (ordered + octave-expanded pitches from the held chord) ---
    static constexpr int kSeqMax = 256;
    int seqBuf_[kSeqMax]; float seqVel_[kSeqMax];
    void buildSequence(int* seq, float* vel, int& len) {
        len = 0; if (held_ <= 0) return;
        int idx[kMaxHeld], m = held_;
        for (int i = 0; i < m; ++i) idx[i] = i;
        const int order = (int)std::lround(p_[GNoteOrder].load(std::memory_order_relaxed));
        if (order != OrdAsPlayed)
            std::sort(idx, idx + m, [&](int a, int b) { return heldBuf_[a].pitch < heldBuf_[b].pitch; });
        int pat[kMaxHeld * 2], pn = 0;
        switch (order) {
            case OrdDown: for (int i = m - 1; i >= 0; --i) pat[pn++] = idx[i]; break;
            case OrdUpDown: for (int i = 0; i < m; ++i) pat[pn++] = idx[i];
                            for (int i = m - 2; i >= 1; --i) pat[pn++] = idx[i]; break;
            case OrdConverge: { int lo = 0, hi = m - 1; bool low = true;
                                while (lo <= hi) { pat[pn++] = low ? idx[lo++] : idx[hi--]; low = !low; } break; }
            default: for (int i = 0; i < m; ++i) pat[pn++] = idx[i]; break;   // Up / AsPlayed / Random
        }
        if (pn == 0) pat[pn++] = idx[0];
        const int octs = std::clamp((int)std::lround(p_[GOctaves].load(std::memory_order_relaxed)), 1, 8);
        const int octMode = (int)std::lround(p_[GOctaveMode].load(std::memory_order_relaxed));
        int oo[16], on = 0;
        switch (octMode) {
            case OctDown: for (int o = octs - 1; o >= 0; --o) oo[on++] = o; break;
            case OctUpDown: for (int o = 0; o < octs; ++o) oo[on++] = o;
                            for (int o = octs - 2; o >= 1; --o) oo[on++] = o; break;
            default: for (int o = 0; o < octs; ++o) oo[on++] = o; break;   // Up / Random
        }
        if (on == 0) oo[on++] = 0;
        for (int oi = 0; oi < on && len < kSeqMax; ++oi)
            for (int pi = 0; pi < pn && len < kSeqMax; ++pi) {
                seq[len] = heldBuf_[pat[pi]].pitch + 12 * oo[oi];
                vel[len] = heldBuf_[pat[pi]].vel; ++len;
            }
    }
    int octaveRandom(long stepAbs) {
        const int octs = std::clamp((int)std::lround(p_[GOctaves].load(std::memory_order_relaxed)), 1, 8);
        return ((int)std::lround(p_[GOctaveMode].load(std::memory_order_relaxed)) == OctRandom) ? (int)(rnd01(stepAbs, 5) * octs) : 0;
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
    std::atomic<int32_t> ccDev_{-2}, ccParam_{-1};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
