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
//
// Param layout is APPEND-ONLY: 0..6 keep the pre-almanac slots so old projects load unchanged
// (Random On is then set from the old Random amount by the managed loader).

#pragma once

#include "MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

class MidiVelocity final : public MidiDevice {
public:
    enum Param {
        PDrive = 0,   // was "Scale" — curve/compand shape (1 = linear); old projects map cleanly
        PFixed,       // Fixed-mode output value
        POutLo,       // was "Fixed amt" — output range minimum (0 default = no floor)
        PRandom,      // random amount
        PMode,        // 0 Curve / 1 Compand / 2 Fixed
        POutHi,       // output range maximum
        PRandomDir,   // 0 Both / 1 Up / 2 Down
        PRandomOn,    // Random switch (the amount survives while it is off)
        PView,        // card size: 0 = L, 1 = S (editor state)
        kNumParams
    };
    static constexpr float kDriveMin = 0.25f, kDriveMax = 4.0f;   // 4^(2f-1), f = 0..1
    static constexpr float kRandomSpan = 64.0f;                    // Random 1.0 = ±64 velocity
    static constexpr int   kHist = 12;

    MidiVelocity() {
        p_[PDrive].store(1.0f); p_[PFixed].store(100.0f / 127.0f); p_[POutLo].store(0.0f); p_[PRandom].store(0.25f);
        p_[PMode].store(0.0f); p_[POutHi].store(1.0f); p_[PRandomDir].store(0.0f); p_[PRandomOn].store(0.0f);
        p_[PView].store(0.0f);
    }

    int32_t midiKind() const override { return 4; }
    const char* displayName() const override { return "Nota Velocity"; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case PDrive: return "Drive"; case PFixed: return "Fixed"; case POutLo: return "Out Low"; case PRandom: return "Random";
            case PMode: return "Mode"; case POutHi: return "Out High"; case PRandomDir: return "Random Dir";
            case PRandomOn: return "Random On"; case PView: return "View"; default: return "";
        }
    }
    float paramMin(int32_t i) const override { return i == PDrive ? kDriveMin : 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) { case PDrive: return kDriveMax; case PMode: return 2.0f; case PRandomDir: return 2.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    // Live telemetry: last note in/out velocity (1..127, -1 = none yet). The scope is the last
    // 12 (in, out) pairs oldest → newest, 0..1 (unused slots 0, 0), then [24] = the number of
    // notes shaped since the device was created (the editor spots a new note by it).
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }
    int32_t midiScope(float* out, int32_t maxN) const override {
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

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiVelocity>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    // Transport stop / loop wrap: the note history (the card's dots) stays — it is what was played.
    void reset() override {}

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t, double, double, bool) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; return; }
        // A pre-almanac project may hold a drive below 0.25 — keep playing it as it was.
        const float drive = std::clamp(p_[PDrive].load(std::memory_order_relaxed), 0.05f, kDriveMax);
        const float fixed = std::clamp(p_[PFixed].load(std::memory_order_relaxed), 0.0f, 1.0f);
        float lo = std::clamp(p_[POutLo].load(std::memory_order_relaxed), 0.0f, 1.0f);
        float hi = std::clamp(p_[POutHi].load(std::memory_order_relaxed), 0.0f, 1.0f);
        if (lo > hi) std::swap(lo, hi);
        const float floorV = 1.0f / 127.0f;                           // never a velocity-0 note-on
        lo = std::max(lo, floorV); hi = std::max(hi, floorV);
        const float rnd = p_[PRandomOn].load(std::memory_order_relaxed) >= 0.5f
                        ? std::clamp(p_[PRandom].load(std::memory_order_relaxed), 0.0f, 1.0f) : 0.0f;
        const int   mode = std::clamp((int)std::lround(p_[PMode].load(std::memory_order_relaxed)), 0, 2);
        const int   dir  = std::clamp((int)std::lround(p_[PRandomDir].load(std::memory_order_relaxed)), 0, 2);

        for (int i = 0; i < nIn; ++i) {
            MidiEv e = in[i];
            if (e.on) {
                float shaped = transfer(std::clamp(e.vel, 0.0f, 1.0f), mode, drive, fixed);
                float v = lo + shaped * (hi - lo);
                if (rnd > 0.0f) {
                    rng_ = rng_ * 1664525u + 1013904223u;
                    float u = (rng_ >> 8) / 16777216.0f;                 // 0..1
                    float dev = rnd * kRandomSpan / 127.0f;
                    float r = dir == 1 ? u * dev : dir == 2 ? -u * dev : (u * 2.0f - 1.0f) * dev;
                    v += r;
                }
                v = std::clamp(std::round(std::clamp(v, lo, hi) * 127.0f), 1.0f, 127.0f) / 127.0f;   // whole MIDI steps
                lastIn_.store((int)std::lround(std::clamp(e.vel, 0.0f, 1.0f) * 127.0f), std::memory_order_relaxed);
                lastOut_.store((int)std::lround(v * 127.0f), std::memory_order_relaxed);
                pushHist(std::clamp(e.vel, 0.0f, 1.0f), v);
                count_.fetch_add(1, std::memory_order_relaxed);
                e.vel = v;
            }
            if (nOut < maxOut) out[nOut++] = e;
        }
    }

    // Transfer function, shared with the card's curve drawing.
    static float transfer(float n, int mode, float drive, float fixed) {
        n = std::clamp(n, 0.0f, 1.0f);
        if (mode == 2) return fixed;                                              // Fixed
        if (mode == 1) {                                                          // Compand (contrast around 0.5)
            float c = 2.0f * n - 1.0f;                                            // -1..1
            float s = c < 0 ? -1.0f : 1.0f;
            return std::clamp(0.5f + s * std::pow(std::fabs(c), 1.0f / drive) * 0.5f, 0.0f, 1.0f);
        }
        return std::pow(n, 1.0f / drive);                                         // Curve (gamma)
    }

private:
    // histHead_ = the next write slot, which is also the oldest entry — so scopeRead from
    // head forward wraps chronologically oldest→newest.
    void pushHist(float in, float outv) {
        uint32_t w = histHead_.load(std::memory_order_relaxed);
        histIn_[w] = in; histOut_[w] = outv;
        histHead_.store((w + 1) % kHist, std::memory_order_relaxed);
    }
    float histIn_[kHist] = {}, histOut_[kHist] = {};
    std::atomic<uint32_t> histHead_{0};
    std::atomic<int32_t> lastIn_{-1}, lastOut_{-1};
    std::atomic<uint32_t> count_{0};
    uint32_t rng_ = 0x2545f491u;
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
