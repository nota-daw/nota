// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — voice allocation, part split/layer routing, held stacks, and keyboard/sequencer note handling.

#include "instruments/twin/Twin.h"
#include "effects/chorus/Chorus.h"
#include "effects/delay/Delay.h"
#include "effects/flanger/Flanger.h"
#include "effects/phaser/Phaser.h"
#include "effects/reverb/Reverb.h"
#include "instruments/pentad/Pentad.h"
#include "instruments/twin/TwinSeq.h"
#include "instruments/twin/TwinVoice.h"
#include "nota/dsp/Instrument.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace nota {

void Twin::computeStatics(int v) {
    pentad::Rng r;
    r.seed(pentad::hash32(0x7717u * 2654435761U + pentad::hash32(static_cast<uint32_t>(v) + 17U)));
    twin::Voice& V = voices_[v];
    V.oP[0] = r.bi();
    V.oP[1] = r.bi();
    V.oCut = r.bi();
    V.oEnv = r.bi();
}

void Twin::resetAll() {
    for (int v = 0; v < kMaxVoices; ++v) {
        twin::Voice& V = voices_[v];
        V = twin::Voice{};
        V.rng.seed(pentad::hash32(0x7717u ^ pentad::hash32(static_cast<uint32_t>(v) * 0x9E3779B9U + 1U)));
        V.noiseRng = pentad::hash32(static_cast<uint32_t>(v) + 0x51ED27u) | 1u;
        for (auto& l : V.lfo) l.rng.seed(V.rng.next());
        V.osc[0].ph = V.rng.uni();
        V.osc[1].ph = V.rng.uni();
        V.walkLeft = 1 + static_cast<int>(V.rng.next() % 4096u);
        computeStatics(v);
    }
    for (int p = 0; p < twin::kParts; ++p)
        for (int l = 0; l < 3; ++l) {
            glfo_[p][l] = twin::Lfo{};
            glfo_[p][l].rng.seed(pentad::hash32(0xA5A5u + static_cast<uint32_t>(l + 3 * p)));
        }
    heldN_[0] = heldN_[1] = 0;
    heldBits_[0] = heldBits_[1] = 0;
    for (auto& part : gLfoFade_)
        for (auto& f : part) f = 1.0f;
    fxHeld_[0] = fxHeld_[1] = false;
    orderCtr_ = 0;
    ctlLeft_ = 0;
    gexpr_.reset();
    seqNoteN_ = 0;
    transpose_ = transKeys_ = 0;
    arpN_ = arpKeys_ = 0;
    arpTook_[0] = arpTook_[1] = transTook_[0] = transTook_[1] = 0;
    pendPat_ = -1;
    freeRun_ = false; // all notes off ends a key-run sequence too
    freeT_ = 0.0;
    upperTail_ = false;
    upperQuiet_ = 0;
    patStartK_ = 0;
    playStep_ = -1;
    seqStepV_ = 0.0f;
    seqResync_ = true;
    for (auto& m : motion_) m = Motion{};
    for (auto& m : motionOvr_) m = twin::MotionOverride{};
    for (auto& part : dn_)
        for (auto& d : part) d.clear_buffers();
    // The effects start over too (a silent start or jump re-seeds everything, so renders repeat).
    for (auto& part : fx_) {
        part[FxChorus]->deviceAction(Chorus::A_Restart, 0, 0.0f);
        part[FxChorus]->deviceAction(Chorus::A_Clear, 0, 0.0f);
        part[FxFlanger]->deviceAction(Flanger::A_Restart, 0, 0.0f);
        part[FxFlanger]->deviceAction(Flanger::A_Clear, 0, 0.0f);
        part[FxPhaser]->deviceAction(Phaser::A_Restart, 0, 0.0f);
        part[FxPhaser]->deviceAction(Phaser::A_Clear, 0, 0.0f);
        part[FxDelay]->deviceAction(Delay::A_Reset, 0, 0.0f);
        part[FxReverb]->deviceAction(Reverb::A_Reset, 0, 0.0f);
    }
}

// Each part keeps its own stack of held keys (in the order played) for its mono fallback.
void Twin::pushHeld(int part, int note) {
    removeHeld(part, note);
    int* h = held_[part];
    int& n = heldN_[part];
    if (n == kMaxHeld) {
        std::memmove(h, h + 1, sizeof(int) * (kMaxHeld - 1));
        --n;
    }
    h[n++] = note;
}

void Twin::removeHeld(int part, int note) {
    int* h = held_[part];
    int& n = heldN_[part];
    for (int i = 0; i < n; ++i)
        if (h[i] == note) {
            std::memmove(h + i, h + i + 1, sizeof(int) * static_cast<size_t>(n - i - 1));
            --n;
            break;
        }
}

// ---- parts (US9) ---------------------------------------------------------------------------
// Single: the Lower part has every voice. Layer: both parts play every key. Split: keys below
// the split key go to Lower, the rest to Upper. In Layer and Split the voices are shared out —
// Lower takes `share` (2, 4 … 10), Upper the rest of the cap — and each part steals only from
// its own share.

int Twin::layoutMode() const {
    return twin::stepOf(get(twin::GMode), 3);
}

Twin::Slice Twin::slice(int part) const {
    const int cap = voiceCap();
    if (layoutMode() == 0) return part == 0 ? Slice{0, cap} : Slice{0, 0};
    const int lo = std::clamp(2 + 2 * twin::stepOf(get(twin::GShare), 5), 2, cap - 2);
    return part == 0 ? Slice{0, lo} : Slice{lo, cap - lo};
}

int Twin::keyParts(int pitch, int* out) const {
    switch (layoutMode()) {
    case 1: out[0] = 0, out[1] = 1; return 2;
    case 2: out[0] = pitch < static_cast<int>(std::lround(get(twin::GSplitKey) * 127.0f)) ? 0 : 1; return 1;
    default: out[0] = 0; return 1;
    }
}

int Twin::seqParts(int* out) const {
    if (layoutMode() == 0) return out[0] = 0, 1;
    switch (twin::stepOf(get(twin::GSeqTarget), 3)) {
    case 1: out[0] = 1; return 1;
    case 2: out[0] = 0, out[1] = 1; return 2;
    default: out[0] = 0; return 1;
    }
}

// A free voice (least recently used), else the oldest releasing one, else the oldest of all
// with a steal fade (`fade` = true).
int Twin::allocate(int first, int count, bool& fade) {
    fade = false;
    int best = -1;
    uint64_t bo = ~0ull;
    for (int v = first; v < first + count; ++v)
        if (!voices_[v].active && voices_[v].pendNote < 0 && voices_[v].order < bo) bo = voices_[v].order, best = v;
    if (best >= 0) return best;
    for (int v = first; v < first + count; ++v)
        if (!voices_[v].gate && voices_[v].pendNote < 0 && voices_[v].order < bo) bo = voices_[v].order, best = v;
    if (best >= 0) return best;
    for (int v = first; v < first + count; ++v)
        if (voices_[v].pendNote < 0 && voices_[v].order < bo) bo = voices_[v].order, best = v;
    fade = true;
    return best >= 0 ? best : first;
}

void Twin::wakeVoice(twin::Voice& V) {
    V.active = true;
    V.fresh = true;
    for (auto& o : V.osc) {
        o.ph += V.rng.uni();
        o.ph -= std::floor(o.ph);
        o.hi = true;
        o.ring.clear();
    }
    V.o2Out = 0.0f;
    V.fx1[0] = V.fx1[1] = 0.0;
    V.fy1[0] = V.fy1[1] = 0.0f;
    V.noiseLp = 0.0f;
    V.mm.reset();
    V.ld.reset();
    V.ld.setInternalOversampling(1); // the voice already runs at ×2
    V.sat.reset();
    V.fb = 0.0f;
}

void Twin::startVoice(int v, int note, float vel, int s, int S, bool glide) {
    twin::Voice& V = voices_[v];
    const twin::PartCtl& c = ctl_[V.part];
    V.note = note;
    V.vel = vel;
    V.relVel = 0.0f;
    V.gate = true;
    V.seq = false;
    V.stackIdx = s;
    V.stackN = S;
    V.order = ++orderCtr_;
    const float u = S > 1 ? 2.0f * static_cast<float>(s) / static_cast<float>(S - 1) - 1.0f : 0.0f;
    V.stackU = u;
    V.pitchTgt = static_cast<float>(note);
    if (!V.hasPitch || !glide) V.pitch = V.pitchTgt;
    V.hasPitch = true;
    // Stereo position: the unison stack fans out; otherwise by voice or by key.
    float pos;
    if (S > 1) pos = u;
    else if (c.spreadMode == 1)
        pos = std::clamp((static_cast<float>(note) - 60.0f) / 30.0f, -1.0f, 1.0f);
    else
        pos = static_cast<float>((v * 7) % kMaxVoices) / static_cast<float>(kMaxVoices - 1) * 2.0f - 1.0f;
    V.panPos = pos; // voiceControl() turns it into the pan gains, and keeps them current
    // A global LFO restarts (Retrig) and fades in (Fade) from the first key of a phrase: no other
    // key of the part is down.
    bool first = true;
    for (const auto& W : voices_)
        if (&W != &V && W.gate && W.part == V.part) first = false;
    for (int l = 0; l < 3; ++l) {
        if (first && c.lfoGlobal[l]) {
            if (c.lfoRetrig[l]) glfo_[V.part][l].ph = 0.0;
            if (c.lfoFade[l] > 0.0f) gLfoFade_[V.part][l] = 0.0f;
        }
        if (c.lfoRetrig[l] && !c.lfoGlobal[l]) V.lfo[l].ph = 0.0;
        V.lfoFadeLvl[l] = c.lfoFade[l] > 0.0f ? 0.0f : 1.0f;
        V.lfoFadeInc[l] = c.lfoFade[l] > 0.0f ? static_cast<float>(1.0 / (c.lfoFade[l] * sr_)) : 0.0f;
    }
    if (!V.active) wakeVoice(V);
    V.fresh = true; // snap the ramps to this note's values
    V.ex.reset(c.exAdd);
    V.rnd = V.rng.bi();
    twin::voiceControl(V, c, sr_);
    V.fenv.gateOn(0);
    V.aenv.gateOn(0);
    V.menv.gateOn(static_cast<int32_t>(c.mDly * sr_));
}

void Twin::startPending(twin::Voice& V) {
    const int note = V.pendNote;
    V.pendNote = -1;
    V.fenv.lvl = V.aenv.lvl = V.menv.lvl = 0.0f;
    startVoice(static_cast<int>(&V - voices_), note, V.pendVel, V.pendStack, V.pendStackN, V.pendGlide);
    V.seq = V.pendSeq;
    for (int d = 0; d < kExprDims; ++d) V.ex.set(d, V.pendExpr[d], ctl_[V.part].exAdd);
}

void Twin::release(twin::Voice& V) {
    V.gate = false;
    V.fenv.gateOff();
    V.aenv.gateOff();
    V.menv.gateOff();
}

// ---- notes in: the keyboard, or the sequencer ----------------------------------------------

void Twin::noteOn(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    wake();
    applyRate();
    if (!ctlInit_) updateControl(clockAt(0));
    const float vel = std::clamp(velocity, 0.0f, 1.0f);
    tele_[ScLastKey].store(static_cast<float>(pitch), std::memory_order_relaxed);
    if (recArmed_.load(std::memory_order_relaxed)) { // step record: the card turns these into steps
        const uint32_t h = ringHead_.load(std::memory_order_relaxed);
        if (h - ringTail_.load(std::memory_order_acquire) < kRing) {
            ring_[h % kRing].store(static_cast<uint32_t>(pitch) << 8 | static_cast<uint32_t>(std::lround(vel * 127.0f)),
                std::memory_order_relaxed);
            ringHead_.store(h + 1, std::memory_order_release);
        }
    }
    // Transport stopped: the first key held starts the sequencer (Consort-style); while it rolls,
    // the sequencer plays on the song's grid and the keys arpeggiate or transpose it.
    if (get(twin::GSeqOn) >= 0.5f && spb_ > 1.0 && !playing_ &&
        (!freeRun_ || (arpKeys_ == 0 && transKeys_ == 0 && !seqLatched())))
        freeStart();
    if (seqActive()) {
        if (twin::stepOf(get(twin::GSeqKind), 2) == 1) {                         // the arpeggiator takes the keys
            if (get(twin::GArpHold) >= 0.5f && arpKeys_ == 0) arpN_ = 0;         // a new chord replaces a held one
            if (arpN_ == 0) arpStartK_ = !playing_ && !seqRunning_ ? 0 : nextK_; // a free start counts from 0
            if (arpN_ < 32) arpList_[arpN_++] = pitch;
            if (!(arpTook_[pitch >> 6] & (uint64_t{1} << (pitch & 63)))) ++arpKeys_;
            arpTook_[pitch >> 6] |= uint64_t{1} << (pitch & 63);
            arpVel_ = vel;
            return;
        }
        if (get(twin::GSeqTranspose) >= 0.5f || !playing_) { // the key transposes the pattern from its first note
            int root = 60;
            if (const twin::PatternSet* ps = pats_.live()) {
                const twin::Pattern& P = ps->pat[curPat_];
                for (int i = 0; i < P.length; ++i)
                    if (P.step[i].n > 0) {
                        root = P.step[i].note[0];
                        break;
                    }
            }
            transpose_ = get(twin::GSeqTranspose) >= 0.5f ? pitch - root : 0;
            if (!(transTook_[pitch >> 6] & (uint64_t{1} << (pitch & 63)))) ++transKeys_;
            transTook_[pitch >> 6] |= uint64_t{1} << (pitch & 63);
            return;
        }
    }
    keyOn(pitch, vel);
}

void Twin::noteOff(int32_t pitch) {
    if (pitch < 0 || pitch > 127) return;
    wake();
    if (recArmed_.load(std::memory_order_relaxed)) {
        const uint32_t h = ringHead_.load(std::memory_order_relaxed);
        if (h - ringTail_.load(std::memory_order_acquire) < kRing) {
            ring_[h % kRing].store(1u << 16 | static_cast<uint32_t>(pitch) << 8, std::memory_order_relaxed);
            ringHead_.store(h + 1, std::memory_order_release);
        }
    }
    // Only the keys the sequencer took are its releases; any other key plays the voices as usual.
    const uint64_t bit = uint64_t{1} << (pitch & 63);
    if (arpTook_[pitch >> 6] & bit) {
        arpTook_[pitch >> 6] &= ~bit;
        if (arpKeys_ > 0) {
            --arpKeys_;
            if (get(twin::GArpHold) < 0.5f) { // without Hold the arpeggio follows the keys
                int w = 0;
                for (int i = 0; i < arpN_; ++i)
                    if (arpList_[i] != pitch) arpList_[w++] = arpList_[i];
                arpN_ = w;
            }
        }
        freeStop();
        return;
    }
    if (transTook_[pitch >> 6] & bit) {
        transTook_[pitch >> 6] &= ~bit;
        if (transKeys_ > 0 && --transKeys_ == 0 && get(twin::GSeqLatch) < 0.5f) transpose_ = 0;
        freeStop();
        return;
    }
    keyOff(pitch);
}

// The last key up stops a free-running sequence, unless Latch / Hold keeps it.
void Twin::freeStop() {
    if (freeRun_ && !playing_ && arpKeys_ == 0 && transKeys_ == 0 && !seqLatched()) freeRun_ = false;
}

// The release velocity reaches the key's sounding voices before they release (FR-030).
void Twin::noteOffVelocity(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    for (auto& V : voices_)
        if (V.active && V.gate && !V.seq && V.note == pitch) V.relVel = std::clamp(velocity, 0.0f, 1.0f);
    noteOff(pitch);
}

void Twin::keyOn(int pitch, float vel) {
    heldBits_[pitch >> 6] |= uint64_t{1} << (pitch & 63);
    int parts[2];
    const int n = keyParts(pitch, parts);
    for (int i = 0; i < n; ++i) keyOnPart(parts[i], pitch, vel);
}

void Twin::keyOff(int pitch) {
    heldBits_[pitch >> 6] &= ~(uint64_t{1} << (pitch & 63));
    for (int p = 0; p < twin::kParts; ++p) keyOffPart(p, pitch);
}

void Twin::keyOnPart(int part, int pitch, float vel) {
    const twin::PartCtl& c = ctl_[part];
    const Slice sl = slice(part);
    if (sl.count <= 0) return;
    const bool othersHeld = heldN_[part] > 0;
    pushHeld(part, pitch);
    const bool glideOn = c.glideSec > 0.0f && (!c.glideLegato || othersHeld);
    if (c.voiceMode == 0) { // Poly
        int v = -1;
        for (int k = sl.first; k < sl.first + sl.count; ++k) // the same key again → retrigger its voice
            if (voices_[k].gate && voices_[k].note == pitch && voices_[k].pendNote < 0 && !voices_[k].seq) {
                v = k;
                break;
            }
        bool fade = false;
        if (v < 0) v = allocate(sl.first, sl.count, fade);
        twin::Voice& V = voices_[v];
        V.part = part;
        if (fade) {
            V.pendNote = pitch;
            V.pendVel = vel;
            V.pendStack = 0;
            V.pendStackN = 1;
            V.pendGlide = glideOn;
            V.pendSeq = false;
            for (int d = 0; d < kExprDims; ++d) V.pendExpr[d] = kExprNeutral[d];
            V.fade = fadeLen_;
            V.gate = false;
        } else
            startVoice(v, pitch, vel, 0, 1, glideOn);
        return;
    }
    // Mono / Legato / Unison: the last key wins; Legato and Unison glide without retriggering
    // while another key is held, Mono always retriggers.
    const int S = c.voiceMode == 3 ? sl.count : 1;
    if (!othersHeld || c.voiceMode == 1) {
        for (int s = 0; s < S; ++s) {
            voices_[sl.first + s].part = part;
            startVoice(sl.first + s, pitch, vel, s, S, glideOn);
        }
        return;
    }
    for (int s = 0; s < S; ++s) {
        twin::Voice& V = voices_[sl.first + s];
        V.note = pitch;
        V.pitchTgt = static_cast<float>(pitch);
        if (!glideOn) V.pitch = V.pitchTgt;
    }
}

void Twin::keyOffPart(int part, int pitch) {
    bool wasHeld = false;
    for (int i = 0; i < heldN_[part]; ++i) wasHeld |= held_[part][i] == pitch;
    removeHeld(part, pitch);
    const twin::PartCtl& c = ctl_[part];
    const Slice sl = slice(part);
    if (c.voiceMode == 0 || sl.count <= 0) {
        for (auto& V : voices_) {
            if (V.part != part) continue;
            if (V.gate && V.note == pitch && !V.seq) release(V);
            if (V.pendNote == pitch && !V.pendSeq) V.pendNote = -1; // ended before its steal fade finished
        }
        return;
    }
    if (!wasHeld) return;
    const int S = c.voiceMode == 3 ? sl.count : 1;
    if (heldN_[part] == 0) {
        for (int s = 0; s < S; ++s)
            if (voices_[sl.first + s].gate && !voices_[sl.first + s].seq) release(voices_[sl.first + s]);
        return;
    }
    const int back = held_[part][heldN_[part] - 1]; // fall back to the last key still held
    for (int s = 0; s < S; ++s) {
        twin::Voice& V = voices_[sl.first + s];
        if (!V.active) continue;
        V.note = back;
        V.pitchTgt = static_cast<float>(back);
        if (c.glideSec <= 0.0f) V.pitch = V.pitchTgt;
    }
}

// A sequencer note: like a key, but outside the held-key stack, and released only by the
// sequencer. It plays on the parts seq.target names. In the mono modes the last note wins.
void Twin::seqOn(int pitch, float vel) {
    int parts[2];
    const int np = seqParts(parts);
    for (int i = 0; i < np; ++i) {
        const int part = parts[i];
        const twin::PartCtl& c = ctl_[part];
        const Slice sl = slice(part);
        if (sl.count <= 0) continue;
        if (c.voiceMode == 0) {
            bool fade = false;
            const int v = allocate(sl.first, sl.count, fade);
            twin::Voice& V = voices_[v];
            V.part = part;
            if (fade) {
                V.pendNote = pitch;
                V.pendVel = vel;
                V.pendStack = 0;
                V.pendStackN = 1;
                V.pendGlide = false;
                V.pendSeq = true;
                for (int d = 0; d < kExprDims; ++d) V.pendExpr[d] = kExprNeutral[d];
                V.fade = fadeLen_;
                V.gate = false;
            } else {
                startVoice(v, pitch, vel, 0, 1, false);
                V.seq = true;
            }
            continue;
        }
        const int S = c.voiceMode == 3 ? sl.count : 1;
        const bool legato = seqNoteN_ > 1 && c.voiceMode != 1;
        for (int s = 0; s < S; ++s) {
            twin::Voice& V = voices_[sl.first + s];
            V.part = part;
            if (legato && V.gate) {
                V.note = pitch;
                V.pitchTgt = static_cast<float>(pitch);
                if (c.glideSec <= 0.0f) V.pitch = V.pitchTgt;
            } else
                startVoice(sl.first + s, pitch, vel, s, S, c.glideSec > 0.0f);
            V.seq = true;
        }
    }
}

void Twin::seqOff(int pitch) {
    for (auto& V : voices_) {
        if (V.gate && V.seq && V.note == pitch) release(V);
        if (V.pendNote == pitch && V.pendSeq) V.pendNote = -1;
    }
}

void Twin::allNotesOff() {
    resetAll();
}

} // namespace nota
