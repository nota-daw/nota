// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chord implementation.

#include "MidiChord.h"

namespace nota {

MidiChord::MidiChord() {
    // Default: Maj7 (+4 +7 +11) with a light 30 ms strum — the mockup's opening state.
    const float semis[kVoices] = {4.0f, 7.0f, 11.0f, 0.0f, 0.0f, 0.0f};
    for (int v = 0; v < kVoices; ++v) {
        p_[Voice1 + v].store(semis[v]);
        p_[Vel1 + v].store(0.0f);
        p_[On1 + v].store(v < 3 ? 1.0f : 0.0f);
    }
    p_[Strum].store(30.0f);
    p_[KeepRoot].store(1.0f);
    p_[Spread].store(0.0f);
    p_[Fold].store(0.0f);
    p_[FoldKey].store(0.0f);
    p_[FoldMode].store(0.0f);
    p_[View].store(0.0f);
    resetLive();
}

const char* MidiChord::paramName(int32_t i) const {
    static const char* const names[kNumParams] = {
        "Voice 1",
        "Voice 2",
        "Voice 3",
        "Voice 4",
        "Voice 5",
        "Voice 6",
        "Strum",
        "Keep Root",
        "Spread",
        "Fold",
        "Vel 1",
        "Vel 2",
        "Vel 3",
        "Vel 4",
        "Vel 5",
        "Vel 6",
        "On 1",
        "On 2",
        "On 3",
        "On 4",
        "On 5",
        "On 6",
        "Fold Key",
        "Fold Mode",
        "View",
    };
    return (i >= 0 && i < kNumParams) ? names[i] : "";
}

float MidiChord::paramMin(int32_t i) const {
    if (i >= Voice1 && i <= Voice6) return -12.0f;
    if (i >= Vel1 && i <= Vel6) return -64.0f;
    return 0.0f;
}

float MidiChord::paramMax(int32_t i) const {
    if (i >= Voice1 && i <= Voice6) return 12.0f;
    if (i >= Vel1 && i <= Vel6) return 63.0f;
    if (i == Strum) return 100.0f;
    if (i == Spread) return 100.0f;
    if (i == FoldKey) return 11.0f;
    return 1.0f; // switches, fold mode, view
}

std::shared_ptr<MidiDevice> MidiChord::clone() const {
    auto c = std::make_shared<MidiChord>();
    for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
    c->setSampleRate(sr_, 0);
    c->setBypassed(bypassed());
    return c;
}

void MidiChord::reset() {
    resetLive();
}

int32_t MidiChord::midiScope(float* out, int32_t maxN) const {
    if (!out || maxN < 4) return 0;
    out[0] = (float)lastRoot_.load(std::memory_order_relaxed);
    out[1] = (float)heldLive_.load(std::memory_order_relaxed);
    out[2] = (float)sounded_.load(std::memory_order_relaxed);
    int n = std::min(lastN_.load(std::memory_order_acquire), std::min(kMaxOut, (int)(maxN - 4) / 2));
    out[3] = (float)n;
    for (int i = 0; i < n; ++i) {
        out[4 + i] = (float)lastPit_[i].load(std::memory_order_relaxed);
        out[4 + n + i] = (float)lastVel_[i].load(std::memory_order_relaxed);
    }
    return 4 + 2 * n;
}

void MidiChord::process(
    const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double, double spb, bool) {
    nOut = 0;
    const int64_t blockEnd = clock_ + frames;
    auto emit = [&](int64_t at, bool on, int pitch, float vel, float dur = 0.0f) {
        if (nOut >= maxOut) return;
        int off = (int)(at - clock_);
        out[nOut++] = {std::clamp(off, 0, std::max(0, frames - 1)), on, pitch, vel, dur};
    };

    // 1) Strummed note-ons that now fall inside this block (still flushed when bypassed,
    //    so a chord started before the bypass completes and its offs stay balanced).
    for (int k = 0; k < nPend_;) {
        if (pend_[k].abs < blockEnd) {
            emit(pend_[k].abs, true, pend_[k].pitch, pend_[k].vel, pend_[k].dur);
            if (pend_[k].chord == chordSeq_) sounded_.fetch_add(1, std::memory_order_relaxed);
            pend_[k] = pend_[--nPend_];
        } else
            ++k;
    }

    if (bypassed()) {
        // Pass through, but a key that was chorded still releases its chord.
        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            if (!e.on && keys_[e.pitch].n > 0) releaseKey(e, clock_ + e.off, emit);
            else
                emit(clock_ + e.off, e.on, e.pitch, e.vel, e.dur);
        }
        clock_ = blockEnd;
        return;
    }

    const bool keepRoot = p_[KeepRoot].load(std::memory_order_relaxed) >= 0.5f;
    const bool fold = p_[Fold].load(std::memory_order_relaxed) >= 0.5f;
    const int key = ((int)std::lround(p_[FoldKey].load(std::memory_order_relaxed)) % 12 + 12) % 12;
    const bool minor = p_[FoldMode].load(std::memory_order_relaxed) >= 0.5f;
    const double spread01 = std::clamp(p_[Spread].load(std::memory_order_relaxed) / 100.0, 0.0, 1.0);
    const int level = std::min(2, (int)std::floor(spread01 * 3.0 + 1e-9));
    const double strumSamp = std::clamp(p_[Strum].load(std::memory_order_relaxed), 0.0f, 200.0f) * 0.001 * sr_;

    for (int i = 0; i < nIn; ++i) {
        const MidiEv& e = in[i];
        const int64_t absEv = clock_ + e.off;
        if (!e.on) {
            releaseKey(e, absEv, emit);
            continue;
        }

        // A retriggered key first releases what it played before.
        if (keys_[e.pitch].n > 0) releaseKey({e.off, false, e.pitch, 0.0f}, absEv, emit);

        // Build the chord for this key: root + active shifts, deduplicated, low → high.
        int pit[kMaxOut];
        float vel[kMaxOut];
        bool played[kMaxOut];
        int n = 0;
        auto add = [&](int p, float v, bool isRoot) {
            if (p < 0 || p > 127) return;
            for (int k = 0; k < n; ++k)
                if (pit[k] == p) {
                    if (isRoot) {
                        vel[k] = v;
                        played[k] = true;
                    }
                    return;
                }
            pit[n] = p;
            vel[n] = v;
            played[n] = isRoot;
            ++n;
        };
        if (keepRoot) add(e.pitch, e.vel, true);
        const int vIn = (int)std::lround(std::clamp(e.vel, 0.0f, 1.0f) * 127.0f);
        int k = 0;
        for (int v = 0; v < kVoices; ++v) {
            if (p_[On1 + v].load(std::memory_order_relaxed) < 0.5f) continue;
            int semi = std::clamp((int)std::lround(p_[Voice1 + v].load(std::memory_order_relaxed)), -24, 24);
            int p = e.pitch + semi;
            if (fold) p = foldInto(p, key, minor);
            if ((k % 2 == 0 && level >= 1) || (k % 2 == 1 && level >= 2)) p += 12;
            ++k;
            int vv = std::clamp(vIn + (int)std::lround(p_[Vel1 + v].load(std::memory_order_relaxed)), 1, 127);
            add(p, vv / 127.0f, false);
        }
        sortByPitch(pit, vel, played, n);

        // Publish the chord for the editor before any of it sounds.
        ++chordSeq_;
        lastRoot_.store(e.pitch, std::memory_order_relaxed);
        sounded_.store(0, std::memory_order_relaxed);
        for (int q = 0; q < n; ++q) {
            lastPit_[q].store(pit[q], std::memory_order_relaxed);
            lastVel_[q].store((int)std::lround(vel[q] * 127.0f), std::memory_order_relaxed);
        }
        lastN_.store(n, std::memory_order_release);

        Key& kk = keys_[e.pitch];
        kk.n = 0;
        for (int q = 0; q < n; ++q) {
            kk.out[kk.n++] = pit[q];
            if (outCount_[pit[q]]++ > 0) {
                sounded_.fetch_add(1, std::memory_order_relaxed);
                continue;
            } // already sounding
            int64_t at = absEv + (int64_t)std::llround(strumSamp * q);
            // A clip note's length carries over (shortened by the strum delay) for devices after us.
            const float dur =
                e.dur > 0.0f && spb > 0.0 ? std::max(1.0f / 256.0f, e.dur - (float)(strumSamp * q / spb)) : 0.0f;
            if (at < blockEnd) {
                emit(at, true, pit[q], vel[q], dur);
                sounded_.fetch_add(1, std::memory_order_relaxed);
            } else if (nPend_ < kMaxPend)
                pend_[nPend_++] = {at, pit[q], vel[q], chordSeq_, dur};
        }
        ++held_;
    }
    heldLive_.store(held_, std::memory_order_relaxed);
    clock_ = blockEnd;
}

int MidiChord::foldInto(int p, int key, bool minor) {
    static const bool maj[12] = {1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1};
    static const bool min[12] = {1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1, 0};
    int deg = ((p - key) % 12 + 12) % 12;
    return (minor ? min : maj)[deg] ? p : p - 1;
}

void MidiChord::sortByPitch(int* pit, float* vel, bool* pl, int n) {
    for (int a = 1; a < n; ++a) {
        int p = pit[a];
        float v = vel[a];
        bool r = pl[a];
        int b = a - 1;
        for (; b >= 0 && pit[b] > p; --b) {
            pit[b + 1] = pit[b];
            vel[b + 1] = vel[b];
            pl[b + 1] = pl[b];
        }
        pit[b + 1] = p;
        vel[b + 1] = v;
        pl[b + 1] = r;
    }
}

bool MidiChord::cancelPending(int pitch) {
    for (int k = 0; k < nPend_; ++k)
        if (pend_[k].pitch == pitch) {
            pend_[k] = pend_[--nPend_];
            return true;
        }
    return false;
}

void MidiChord::resetLive() {
    nPend_ = 0;
    held_ = 0;
    for (auto& k : keys_) k.n = 0;
    for (auto& c : outCount_) c = 0;
    heldLive_.store(0, std::memory_order_relaxed);
}

} // namespace nota
