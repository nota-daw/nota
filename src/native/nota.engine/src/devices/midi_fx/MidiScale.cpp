// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Scale implementation.

#include "MidiScale.h"

namespace nota {

MidiScale::MidiScale() {
    p_[PRoot].store(0.0f);
    p_[PScale].store(1.0f);
    p_[PTranspose].store(0.0f); // C minor — the mockup's opening state
    // The Custom mask starts as major (a sane place to begin editing).
    static const int maj[] = {0, 2, 4, 5, 7, 9, 11};
    for (int i = 0; i < 12; ++i) p_[PMask0 + i].store(0.0f);
    for (int d : maj) p_[PMask0 + d].store(1.0f);
    p_[PFold].store(0.0f);
    p_[PFollowKey].store(0.0f);
    p_[PRangeLo].store(0.0f);
    p_[PRangeHi].store(127.0f);
    p_[PLearn].store(0.0f);
    p_[PView].store(0.0f);
    resetLive();
}

const char* MidiScale::paramName(int32_t i) const {
    static const char* const names[kNumParams] = {
        "Root",
        "Scale",
        "Transpose",
        "Mask 0",
        "Mask 1",
        "Mask 2",
        "Mask 3",
        "Mask 4",
        "Mask 5",
        "Mask 6",
        "Mask 7",
        "Mask 8",
        "Mask 9",
        "Mask 10",
        "Mask 11",
        "Fold",
        "Follow Key",
        "Range Low",
        "Range High",
        "Learn",
        "View",
    };
    return (i >= 0 && i < kNumParams) ? names[i] : "";
}

float MidiScale::paramMax(int32_t i) const {
    switch (i) {
    case PRoot: return 11.0f;
    case PScale: return (float)kCustom;
    case PTranspose: return 24.0f;
    case PFold: return 2.0f;
    case PRangeLo:
    case PRangeHi: return 127.0f;
    default: return 1.0f; // masks / follow / learn / view
    }
}

int32_t MidiScale::midiScope(float* out, int32_t maxN) const {
    if (!out || maxN < 8) return 0;
    out[0] = (float)lastIn_.load(std::memory_order_relaxed);
    out[1] = (float)lastOut_.load(std::memory_order_relaxed);
    out[2] = (float)rootLive_.load(std::memory_order_relaxed);
    out[3] = (float)maskLive_.load(std::memory_order_relaxed);
    out[4] = (float)heldIn_.load(std::memory_order_relaxed);
    out[5] = (float)heldOut_.load(std::memory_order_relaxed);
    out[6] = p_[PLearn].load(std::memory_order_relaxed) >= 0.5f ? 1.0f : 0.0f;
    out[7] = (float)(onCount_.load(std::memory_order_relaxed) & 0xFFFFFF);
    int n = std::min<int32_t>(maxN, kScopeN);
    for (int i = 8; i < n; ++i) {
        int k = i - 8;
        out[i] = (float)((k < 12 ? inHits_[k] : outHits_[k - 12]).load(std::memory_order_relaxed) & 0xFFFFFF);
    }
    return n;
}

std::shared_ptr<MidiDevice> MidiScale::clone() const {
    auto c = std::make_shared<MidiScale>();
    for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
    c->setBypassed(bypassed());
    return c;
}

void MidiScale::reset() {
    resetLive();
}

void MidiScale::process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t, double, double, bool) {
    nOut = 0;
    auto emit = [&](const MidiEv& e, int pitch) {
        if (nOut >= maxOut) return;
        MidiEv o = e;
        o.pitch = pitch;
        out[nOut++] = o;
    };

    const bool learn = p_[PLearn].load(std::memory_order_relaxed) >= 0.5f;
    if (learn && !learnWas_) learnFresh_ = true; // a new Learn pass starts a fresh mask
    learnWas_ = learn;
    const bool follow = p_[PFollowKey].load(std::memory_order_relaxed) >= 0.5f && !learn;
    const bool bypass = bypassed();

    for (int i = 0; i < nIn; ++i) {
        const MidiEv& e = in[i];
        if (e.pitch < 0 || e.pitch > 127) continue;
        if (!e.on) {
            release(e, emit);
            continue;
        }
        if (outFor_[e.pitch] >= 0) release({e.off, false, e.pitch, 0.0f}, emit); // a retriggered key lets go first

        int p = e.pitch;
        if (!bypass) {
            if (learn) learnNote(e.pitch);
            else {
                if (follow) followNote(e.pitch);
                p = map(e.pitch);
            }
        }
        if (p < 0 || p > 127) continue;
        outFor_[e.pitch] = (int8_t)p;
        ++outRef_[p];
        emit(e, p);
        lastIn_.store(e.pitch, std::memory_order_relaxed);
        lastOut_.store(p, std::memory_order_relaxed);
        onCount_.fetch_add(1, std::memory_order_relaxed);
        inHits_[e.pitch % 12].fetch_add(1, std::memory_order_relaxed);
        outHits_[p % 12].fetch_add(1, std::memory_order_relaxed);
    }
    publish();
}

uint16_t MidiScale::presetMask(int sc) {
    // major, natural minor, harmonic minor, dorian, phrygian, lydian, mixolydian, penta maj, penta min, chromatic
    static const uint16_t m[kNumScales] = {2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095};
    return m[std::clamp(sc, 0, kNumScales - 1)];
}

uint16_t MidiScale::mask() const {
    int sc = (int)std::lround(p_[PScale].load(std::memory_order_relaxed));
    if (sc < kCustom) return presetMask(sc);
    uint16_t m = 0;
    for (int i = 0; i < 12; ++i)
        if (p_[PMask0 + i].load(std::memory_order_relaxed) >= 0.5f) m |= (uint16_t)(1 << i);
    return m;
}

int MidiScale::map(int pitch) const {
    const int lo = std::clamp((int)std::lround(p_[PRangeLo].load(std::memory_order_relaxed)), 0, 127);
    const int hi = std::clamp((int)std::lround(p_[PRangeHi].load(std::memory_order_relaxed)), 0, 127);
    if (pitch < std::min(lo, hi) || pitch > std::max(lo, hi)) return pitch;
    const uint16_t bits = mask();
    const int fold = std::clamp((int)std::lround(p_[PFold].load(std::memory_order_relaxed)), 0, 2);
    const int tr = (int)std::lround(p_[PTranspose].load(std::memory_order_relaxed));
    return (bits ? quantize(pitch, root(), bits, fold) : pitch) + tr;
}

int MidiScale::quantize(int pitch, int root, uint16_t bits, int fold) {
    const int pc = ((pitch - root) % 12 + 12) % 12;
    auto has = [&](int x) { return (bits & (1 << ((x % 12 + 12) % 12))) != 0; };
    if (fold == 1) {
        for (int d = 0; d < 12; ++d)
            if (has(pc - d)) return pitch - d;
    } // Down
    else if (fold == 2) {
        for (int d = 0; d < 12; ++d)
            if (has(pc + d)) return pitch + d;
    } // Up
    else {
        for (int d = 0; d < 7; ++d) {
            if (has(pc + d)) return pitch + d;
            if (has(pc - d)) return pitch - d;
        }
    } // Nearest (a tie goes up)
    return pitch;
}

void MidiScale::learnNote(int pitch) {
    const int r = root();
    if (learnFresh_) {
        for (int i = 0; i < 12; ++i) p_[PMask0 + i].store(0.0f, std::memory_order_relaxed);
        p_[PScale].store((float)kCustom, std::memory_order_relaxed);
        learnFresh_ = false;
    }
    p_[PMask0 + ((pitch - r) % 12 + 12) % 12].store(1.0f, std::memory_order_relaxed);
}

void MidiScale::followNote(int pitch) {
    for (auto& h : hist_) h *= 0.92f;
    hist_[pitch % 12] += 1.0f;
    const uint16_t bits = mask();
    if (!bits || bits == 0xFFF) return; // chromatic / empty: every root fits alike
    auto score = [&](int r) {
        float s = 0.0f;
        for (int pc = 0; pc < 12; ++pc) {
            const int rel = ((pc - r) % 12 + 12) % 12;
            s += hist_[pc] * ((bits & (1 << rel)) ? (rel == 0                       ? 1.6f
                                                        : rel == 7 && (bits & 0x80) ? 1.25f
                                                                                    : 1.0f)
                                                  : -0.8f);
        }
        return s;
    };
    const int cur = root();
    int best = cur;
    float bs = score(cur);
    for (int r = 0; r < 12; ++r) {
        float s = score(r);
        if (s > bs + 0.35f) {
            bs = s;
            best = r;
        }
    }
    if (best != cur) p_[PRoot].store((float)best, std::memory_order_relaxed);
}

void MidiScale::publish() {
    uint16_t hin = 0, hout = 0;
    for (int k = 0; k < 128; ++k)
        if (outFor_[k] >= 0) {
            hin |= (uint16_t)(1 << (k % 12));
            hout |= (uint16_t)(1 << (outFor_[k] % 12));
        }
    heldIn_.store(hin, std::memory_order_relaxed);
    heldOut_.store(hout, std::memory_order_relaxed);
    rootLive_.store(root(), std::memory_order_relaxed);
    maskLive_.store(mask(), std::memory_order_relaxed);
}

void MidiScale::resetLive() {
    for (auto& o : outFor_) o = -1;
    for (auto& r : outRef_) r = 0;
    for (auto& h : hist_) h = 0.0f;
    learnFresh_ = p_[PLearn].load(std::memory_order_relaxed) >= 0.5f;
    heldIn_.store(0, std::memory_order_relaxed);
    heldOut_.store(0, std::memory_order_relaxed);
}

} // namespace nota
