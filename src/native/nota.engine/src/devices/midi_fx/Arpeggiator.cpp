// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Arp implementation.

#include "Arpeggiator.h"

namespace nota {

Arpeggiator::Arpeggiator() {
    p_[GRate].store(5.0f); // 1/16
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

const char* Arpeggiator::paramName(int32_t i) const {
    if (i < 0 || i >= kNumParams) return "";
    if (i < kNumGlobals) {
        static const char* g[] = {"Rate", "Sync", "FreeRate", "Gate", "Octaves", "OctMode", "Order", "Swing", "Hold",
            "Retrig", "Transpose", "Loop", "LoopMode"};
        return g[i];
    }
    if (i == PVelAmt) return "VelAmt";
    if (i == PView) return "View";
    static thread_local char buf[16];
    static const char* ln[] = {"Vel", "Len", "Chn", "Rat", "Trn", "On", "CC"};
    std::snprintf(buf, sizeof(buf), "%s %d", ln[(i - kNumGlobals) / kSteps], (i - kNumGlobals) % kSteps + 1);
    return buf;
}

float Arpeggiator::paramMin(int32_t i) const {
    if (i >= PVelAmt) return 0.0f;
    if (i < kNumGlobals) {
        switch (i) {
        case GTranspose: return -24.0f;
        case GFreeRate: return 0.1f;
        case GLoop: return 1.0f;
        default: return 0.0f;
        }
    }
    switch ((i - kNumGlobals) / kSteps) {
    case LTransp: return -24.0f;
    case LRatchet: return 1.0f;
    default: return 0.0f;
    }
}

float Arpeggiator::paramMax(int32_t i) const {
    if (i >= PVelAmt) return 1.0f;
    if (i < kNumGlobals) {
        switch (i) {
        case GRate: return 7.0f;
        case GFreeRate: return 50.0f;
        case GGate: return 2.0f;
        case GOctaves: return 8.0f;
        case GOctaveMode: return 3.0f;
        case GNoteOrder: return 8.0f;
        case GKeyRetrig: return 2.0f;
        case GTranspose: return 24.0f;
        case GLoop: return 16.0f;
        case GLoopMode: return 3.0f;
        default: return 1.0f;
        }
    }
    switch ((i - kNumGlobals) / kSteps) {
    case LLen: return 2.0f;
    case LRatchet: return 8.0f;
    case LTransp: return 24.0f;
    case LCC: return 127.0f;
    default: return 1.0f;
    }
}

std::shared_ptr<MidiDevice> Arpeggiator::clone() const {
    auto a = std::make_shared<Arpeggiator>();
    for (int i = 0; i < kNumParams; ++i)
        a->p_[i].store(p_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    a->setBypassed(bypassed());
    a->setCcDest(ccDev_.load(std::memory_order_relaxed), ccParam_.load(std::memory_order_relaxed));
    a->setCcDepth(ccDepth_.load(std::memory_order_relaxed));
    a->setSampleRate(sr_, 0);
    return a; // transient live state (held/pending/phase) starts fresh
}

void Arpeggiator::reset() {
    held_ = 0;
    physDown_ = 0;
    nOffs_ = 0;
    nOns_ = 0;
    arpBeat_ = 0.0;
    phaseRef_ = 0.0;
    curLp_.store(-1, std::memory_order_relaxed);
    curK_.store(-1, std::memory_order_relaxed);
    heldLive_.store(0, std::memory_order_relaxed);
}

void Arpeggiator::timeJump(double d) {
    arpBeat_ += d;
    phaseRef_ += d;
    for (int i = 0; i < nOns_; ++i) {
        onBuf_[i].onBeat += d;
        onBuf_[i].offBeat += d;
    }
    for (int i = 0; i < nOffs_; ++i) offBuf_[i].beat += d;
}

int32_t Arpeggiator::midiScope(float* out, int32_t maxN) const {
    if (!out || maxN < 4) return 0;
    out[0] = (float)curLp_.load(std::memory_order_relaxed);
    out[1] = (float)curK_.load(std::memory_order_relaxed);
    out[2] = (float)heldLive_.load(std::memory_order_relaxed);
    int n = std::min(chordN_.load(std::memory_order_acquire), std::min(kMaxHeld, (int)maxN - 4));
    out[3] = (float)n;
    for (int i = 0; i < n; ++i) out[4 + i] = (float)chord_[i].load(std::memory_order_relaxed);
    return 4 + n;
}

void Arpeggiator::process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames,
    double beatStart, double spb, bool playing) {
    nOut = 0;
    if (bypassed()) {
        for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i];
        return;
    }

    const double b0 = playing ? beatStart : arpBeat_;
    const double b1 = b0 + frames / spb;
    const int retrig = std::clamp((int)std::lround(p_[GKeyRetrig].load(std::memory_order_relaxed)), 0, 2);
    const bool hold = p_[GHold].load(std::memory_order_relaxed) > 0.5f;
    if (restartReq_.exchange(false, std::memory_order_relaxed)) {
        phaseRef_ = b0;
        nOns_ = 0;
    }

    // 1) Fold input into the held set (input notes are consumed by the arp).
    for (int i = 0; i < nIn; ++i) {
        const MidiEv& e = in[i];
        if (e.pitch < 0 || e.pitch > 127) continue;
        if (e.on) {
            const bool fresh = physDown_ == 0;
            if (fresh) held_ = 0;
            if (physDown_ < 127) physDown_++;
            addHeld(e.pitch, e.vel);
            if (fresh && retrig == RetrigNote) phaseRef_ = b0 + e.off / spb; // restart the pattern at the key-press
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
        const double ref = retrig == RetrigBeat ? 0.0 : phaseRef_; // Beat: bar-aligned grid
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

void Arpeggiator::addHeld(int32_t pitch, float vel) {
    for (int i = 0; i < held_; ++i)
        if (heldBuf_[i].pitch == pitch) {
            heldBuf_[i].vel = vel;
            return;
        }
    if (held_ < kMaxHeld) heldBuf_[held_++] = {pitch, vel, seqCtr_++};
}

void Arpeggiator::removeHeld(int32_t pitch) {
    for (int i = 0; i < held_; ++i)
        if (heldBuf_[i].pitch == pitch) {
            for (int j = i; j < held_ - 1; ++j) heldBuf_[j] = heldBuf_[j + 1];
            held_--;
            return;
        }
}

void Arpeggiator::pushOff(int32_t pitch, double beat, MidiEv* out, int& nOut, int maxOut) {
    cancelOff(pitch);
    if (nOffs_ < kMaxPending) offBuf_[nOffs_++] = {pitch, beat};
    else if (nOut < maxOut)
        out[nOut++] = {0, false, pitch, 0.0f}; // table full: off now
}

void Arpeggiator::cancelOff(int32_t pitch) {
    for (int i = 0; i < nOffs_; ++i)
        if (offBuf_[i].pitch == pitch) {
            for (int j = i; j < nOffs_ - 1; ++j) offBuf_[j] = offBuf_[j + 1];
            nOffs_--;
            return;
        }
}

void Arpeggiator::firePendingOns(MidiEv* out, int& nOut, int maxOut, double b0, double b1, double spb, int32_t frames) {
    for (int i = 0; i < nOns_;) {
        if (onBuf_[i].onBeat < b1) {
            if (onBuf_[i].onBeat >= b0 && nOut < maxOut) {
                out[nOut++] = {off(onBuf_[i].onBeat - b0, spb, frames), true, onBuf_[i].pitch, onBuf_[i].vel};
                pushOff(onBuf_[i].pitch, onBuf_[i].offBeat, out, nOut, maxOut);
            }
            for (int j = i; j < nOns_ - 1; ++j) onBuf_[j] = onBuf_[j + 1];
            nOns_--;
        } else
            ++i;
    }
}

void Arpeggiator::firePendingOffs(
    MidiEv* out, int& nOut, int maxOut, double b0, double b1, double spb, int32_t frames) {
    for (int i = 0; i < nOffs_;) {
        if (offBuf_[i].beat < b1) {
            if (offBuf_[i].beat >= b0 && nOut < maxOut)
                out[nOut++] = {off(offBuf_[i].beat - b0, spb, frames), false, offBuf_[i].pitch, 0.0f};
            for (int j = i; j < nOffs_ - 1; ++j) offBuf_[j] = offBuf_[j + 1];
            nOffs_--;
        } else
            ++i;
    }
}

void Arpeggiator::emitStep(long stepAbs, long kp, int lp, double stepBeat, double stepBeats, double b0, double b1,
    double spb, int32_t frames, MidiEv* out, int& nOut, int maxOut) {
    if (lane(LOn, lp).load(std::memory_order_relaxed) < 0.5f) return;
    if (rnd01(stepAbs, 11) >= std::clamp(lane(LChance, lp).load(std::memory_order_relaxed), 0.0f, 1.0f)) return;

    const float gate = std::clamp(p_[GGate].load(std::memory_order_relaxed), 0.0f, 2.0f);
    const float lenL = std::clamp(lane(LLen, lp).load(std::memory_order_relaxed), 0.0f, 2.0f);
    const int ratch = std::clamp((int)std::lround(lane(LRatchet, lp).load(std::memory_order_relaxed)), 1, 8);
    const float velL = std::clamp(lane(LVel, lp).load(std::memory_order_relaxed), 0.0f, 1.0f);
    const float amt = std::clamp(p_[PVelAmt].load(std::memory_order_relaxed), 0.0f, 1.0f);
    const float velK = 1.0f - amt + amt * velL; // Groove velocity, scaled by VelAmt
    const int trn = (int)std::lround(
        p_[GTranspose].load(std::memory_order_relaxed) + lane(LTransp, lp).load(std::memory_order_relaxed));
    const int order = (int)std::lround(p_[GNoteOrder].load(std::memory_order_relaxed));

    int seqLen = 0;
    buildSequence(seqBuf_, seqVel_, seqLen);
    if (seqLen <= 0) return;
    const int oc = 12 * octaveRandom(kp);

    const double subLen = stepBeats / ratch;
    long idx = (order == OrdRandom) ? (long)(rnd01(kp, 7) * seqLen) : kp % seqLen;
    idx = ((idx % seqLen) + seqLen) % seqLen;
    for (int r = 0; r < ratch; ++r) {
        const double onBeat = stepBeat + r * subLen;
        const double offBeat = onBeat + std::max(0.05, (double)lenL) * subLen * gate;
        if (order == OrdChord) {
            for (int h = 0; h < seqLen; ++h)
                place(
                    seqBuf_[h] + oc + trn, velK * seqVel_[h], onBeat, offBeat, b0, b1, spb, frames, out, nOut, maxOut);
        } else {
            place(
                seqBuf_[idx] + oc + trn, velK * seqVel_[idx], onBeat, offBeat, b0, b1, spb, frames, out, nOut, maxOut);
        }
    }
}

void Arpeggiator::place(int pitch, float vel, double onBeat, double offBeat, double b0, double b1, double spb,
    int32_t frames, MidiEv* out, int& nOut, int maxOut) {
    if (pitch < 0 || pitch > 127) return;
    vel = std::clamp(vel, 0.0f, 1.0f);
    if (onBeat >= b0 && onBeat < b1) {
        if (nOut < maxOut) out[nOut++] = {off(onBeat - b0, spb, frames), true, pitch, vel};
        pushOff(pitch, offBeat, out, nOut, maxOut);
    } else if (onBeat >= b1) {
        pushOn(pitch, vel, onBeat, offBeat);
    }
}

void Arpeggiator::buildSequence(int* seq, float* vel, int& len) {
    len = 0;
    if (held_ <= 0) return;
    int idx[kMaxHeld], m = held_;
    for (int i = 0; i < m; ++i) idx[i] = i;
    const int order = (int)std::lround(p_[GNoteOrder].load(std::memory_order_relaxed));
    if (order != OrdAsPlayed)
        std::sort(idx, idx + m, [&](int a, int b) { return heldBuf_[a].pitch < heldBuf_[b].pitch; });
    const int octs = std::clamp((int)std::lround(p_[GOctaves].load(std::memory_order_relaxed)), 1, 8);
    const int octMode = (int)std::lround(p_[GOctaveMode].load(std::memory_order_relaxed));
    int oo[16], on = 0;
    switch (octMode) {
    case OctDown:
        for (int o = octs - 1; o >= 0; --o) oo[on++] = o;
        break;
    case OctUpDown:
        for (int o = 0; o < octs; ++o) oo[on++] = o;
        for (int o = octs - 2; o >= 1; --o) oo[on++] = o;
        break;
    case OctRandom: oo[on++] = 0; break; // a random octave per step instead
    default:
        for (int o = 0; o < octs; ++o) oo[on++] = o;
        break;
    }
    int n = 0;
    for (int oi = 0; oi < on && n < kSeqMax; ++oi)
        for (int i = 0; i < m && n < kSeqMax; ++i) {
            rangeBuf_[n] = heldBuf_[idx[i]].pitch + 12 * oo[oi];
            rangeVel_[n] = heldBuf_[idx[i]].vel;
            ++n;
        }
    auto push = [&](int i) {
        if (len < kSeqMax) {
            seq[len] = rangeBuf_[i];
            vel[len] = rangeVel_[i];
            ++len;
        }
    };
    switch (order) {
    case OrdDown:
        for (int i = n - 1; i >= 0; --i) push(i);
        break;
    case OrdUpDown:
        for (int i = 0; i < n; ++i) push(i);
        for (int i = n - 2; i >= 1; --i) push(i);
        break;
    case OrdDownUp:
        for (int i = n - 1; i >= 0; --i) push(i);
        for (int i = 1; i <= n - 2; ++i) push(i);
        break;
    case OrdConverge:
    case OrdDiverge: {
        int c[kSeqMax], cn = 0, lo = 0, hi = n - 1;
        while (lo <= hi) {
            c[cn++] = lo;
            if (lo != hi) c[cn++] = hi;
            ++lo;
            --hi;
        }
        if (order == OrdConverge)
            for (int i = 0; i < cn; ++i) push(c[i]);
        else
            for (int i = cn - 1; i >= 0; --i) push(c[i]);
        break;
    }
    default:
        for (int i = 0; i < n; ++i) push(i);
        break; // Up / AsPlayed / Random / Chord
    }
}

int Arpeggiator::octaveRandom(long kp) {
    const int octs = std::clamp((int)std::lround(p_[GOctaves].load(std::memory_order_relaxed)), 1, 8);
    return ((int)std::lround(p_[GOctaveMode].load(std::memory_order_relaxed)) == OctRandom) ? (int)(rnd01(kp, 5) * octs)
                                                                                            : 0;
}

void Arpeggiator::publishChord() {
    heldLive_.store(held_, std::memory_order_relaxed);
    if (held_ <= 0) return;
    for (int i = 0; i < held_; ++i) chord_[i].store(heldBuf_[i].pitch, std::memory_order_relaxed);
    chordN_.store(held_, std::memory_order_release);
}

int Arpeggiator::loopPos(long stepAbs, int loop) {
    const int mode = (int)std::lround(p_[GLoopMode].load(std::memory_order_relaxed));
    long m = ((stepAbs % loop) + loop) % loop;
    switch (mode) {
    case LoopBack: return (int)(loop - 1 - m);
    case LoopPing: {
        long period = std::max(1L, 2L * loop - 2);
        long q = ((stepAbs % period) + period) % period;
        return (int)(q < loop ? q : period - q);
    }
    case LoopRandom: return (int)(rnd01(stepAbs, 3) * loop);
    default: return (int)m;
    }
}

double Arpeggiator::currentStepBeats(double spb) const {
    if (p_[GSync].load(std::memory_order_relaxed) > 0.5f) {
        static const double div[] = {4.0, 2.0, 1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125};
        return div[std::clamp((int)std::lround(p_[GRate].load(std::memory_order_relaxed)), 0, 7)];
    }
    return (sr_ / std::max(0.1f, p_[GFreeRate].load(std::memory_order_relaxed))) / std::max(1.0, spb);
}

} // namespace nota
