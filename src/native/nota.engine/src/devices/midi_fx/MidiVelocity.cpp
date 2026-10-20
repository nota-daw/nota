// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Velocity implementation.

#include "MidiVelocity.h"

namespace nota {

MidiVelocity::MidiVelocity() {
    p_[PDrive].store(1.0f);
    p_[PFixed].store(100.0f / 127.0f);
    p_[POutLo].store(0.0f);
    p_[PRandom].store(0.25f);
    p_[PMode].store(0.0f);
    p_[POutHi].store(1.0f);
    p_[PRandomDir].store(0.0f);
    p_[PRandomOn].store(0.0f);
    p_[PView].store(0.0f);
}

const char* MidiVelocity::paramName(int32_t i) const {
    switch (i) {
    case PDrive: return "Drive";
    case PFixed: return "Fixed";
    case POutLo: return "Out Low";
    case PRandom: return "Random";
    case PMode: return "Mode";
    case POutHi: return "Out High";
    case PRandomDir: return "Random Dir";
    case PRandomOn: return "Random On";
    case PView: return "View";
    default: return "";
    }
}

float MidiVelocity::paramMax(int32_t i) const {
    switch (i) {
    case PDrive: return kDriveMax;
    case PMode: return 2.0f;
    case PRandomDir: return 2.0f;
    default: return 1.0f;
    }
}

int32_t MidiVelocity::midiScope(float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    const uint32_t head = histHead_.load(std::memory_order_relaxed);
    int w = 0;
    for (int k = 0; k < kHist && w + 1 < maxN; ++k) {
        int idx = (int)((head + k) % kHist);
        out[w++] = histIn_[idx];
        out[w++] = histOut_[idx];
    }
    if (w < maxN && w == kHist * 2) out[w++] = (float)count_.load(std::memory_order_relaxed);
    return w;
}

std::shared_ptr<MidiDevice> MidiVelocity::clone() const {
    auto c = std::make_shared<MidiVelocity>();
    for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
    c->setBypassed(bypassed());
    return c;
}

void MidiVelocity::process(
    const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t, double, double, bool) {
    nOut = 0;
    if (bypassed()) {
        for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i];
        return;
    }
    // A pre-almanac project may hold a drive below 0.25 — keep playing it as it was.
    const float drive = std::clamp(p_[PDrive].load(std::memory_order_relaxed), 0.05f, kDriveMax);
    const float fixed = std::clamp(p_[PFixed].load(std::memory_order_relaxed), 0.0f, 1.0f);
    float lo = std::clamp(p_[POutLo].load(std::memory_order_relaxed), 0.0f, 1.0f);
    float hi = std::clamp(p_[POutHi].load(std::memory_order_relaxed), 0.0f, 1.0f);
    if (lo > hi) std::swap(lo, hi);
    const float floorV = 1.0f / 127.0f; // never a velocity-0 note-on
    lo = std::max(lo, floorV);
    hi = std::max(hi, floorV);
    const float rnd = p_[PRandomOn].load(std::memory_order_relaxed) >= 0.5f
                          ? std::clamp(p_[PRandom].load(std::memory_order_relaxed), 0.0f, 1.0f)
                          : 0.0f;
    const int mode = std::clamp((int)std::lround(p_[PMode].load(std::memory_order_relaxed)), 0, 2);
    const int dir = std::clamp((int)std::lround(p_[PRandomDir].load(std::memory_order_relaxed)), 0, 2);

    for (int i = 0; i < nIn; ++i) {
        MidiEv e = in[i];
        if (e.on) {
            float shaped = transfer(std::clamp(e.vel, 0.0f, 1.0f), mode, drive, fixed);
            float v = lo + shaped * (hi - lo);
            if (rnd > 0.0f) {
                rng_ = rng_ * 1664525u + 1013904223u;
                float u = (rng_ >> 8) / 16777216.0f; // 0..1
                float dev = rnd * kRandomSpan / 127.0f;
                float r = dir == 1 ? u * dev : dir == 2 ? -u * dev : (u * 2.0f - 1.0f) * dev;
                v += r;
            }
            v = std::clamp(std::round(std::clamp(v, lo, hi) * 127.0f), 1.0f, 127.0f) / 127.0f; // whole MIDI steps
            lastIn_.store((int)std::lround(std::clamp(e.vel, 0.0f, 1.0f) * 127.0f), std::memory_order_relaxed);
            lastOut_.store((int)std::lround(v * 127.0f), std::memory_order_relaxed);
            pushHist(std::clamp(e.vel, 0.0f, 1.0f), v);
            count_.fetch_add(1, std::memory_order_relaxed);
            e.vel = v;
        }
        if (nOut < maxOut) out[nOut++] = e;
    }
}

float MidiVelocity::transfer(float n, int mode, float drive, float fixed) {
    n = std::clamp(n, 0.0f, 1.0f);
    if (mode == 2) return fixed;   // Fixed
    if (mode == 1) {               // Compand (contrast around 0.5)
        float c = 2.0f * n - 1.0f; // -1..1
        float s = c < 0 ? -1.0f : 1.0f;
        return std::clamp(0.5f + s * std::pow(std::fabs(c), 1.0f / drive) * 0.5f, 0.0f, 1.0f);
    }
    return std::pow(n, 1.0f / drive); // Curve (gamma)
}

void MidiVelocity::pushHist(float in, float outv) {
    uint32_t w = histHead_.load(std::memory_order_relaxed);
    histIn_[w] = in;
    histOut_[w] = outv;
    histHead_.store((w + 1) % kHist, std::memory_order_relaxed);
}

} // namespace nota
