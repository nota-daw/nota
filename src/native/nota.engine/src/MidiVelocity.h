// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Velocity — a MIDI effect (midiKind 4), reworked to mockup 3b. Reshapes note-on
// velocity through a transfer curve: Curve (drive / gamma — boost or attenuate soft notes),
// Compand (expand/compress contrast around the middle) or Fixed (force a value). The result
// is remapped into an output range and can be humanised with directional randomness. The
// last note (in→out) and the last 12 output velocities are published for the editor.
//
// Param layout is APPEND-ONLY: 0..3 keep the original slots so old projects load unchanged.

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
        kNumParams
    };
    MidiVelocity() {
        p_[PDrive].store(1.0f); p_[PFixed].store(0.8f); p_[POutLo].store(0.0f); p_[PRandom].store(0.0f);
        p_[PMode].store(0.0f); p_[POutHi].store(1.0f); p_[PRandomDir].store(0.0f);
    }

    int32_t midiKind() const override { return 4; }
    const char* displayName() const override { return "Nota Velocity"; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case PDrive: return "Drive"; case PFixed: return "Fixed"; case POutLo: return "Out Low"; case PRandom: return "Random";
            case PMode: return "Mode"; case POutHi: return "Out High"; case PRandomDir: return "Random Dir"; default: return "";
        }
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) { case PDrive: return 2.0f; case PMode: return 2.0f; case PRandomDir: return 2.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    // Live telemetry: last note in/out velocity (0..127), and the last 12 (in,out) pairs.
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }
    int32_t midiScope(float* out, int32_t maxN) const override {
        if (!out || maxN <= 0) return 0;
        const int n = std::min(maxN, kHist * 2);
        const uint32_t head = histHead_.load(std::memory_order_relaxed);
        int w = 0;
        for (int k = 0; k < kHist && w + 1 < n; ++k) {
            int idx = (int)((head + k) % kHist);
            out[w++] = histIn_[idx];
            out[w++] = histOut_[idx];
        }
        return w;
    }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiVelocity>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    void reset() override { for (int i = 0; i < kHist; ++i) { histIn_[i] = 0; histOut_[i] = 0; } }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t, double, double, bool) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; return; }
        const float drive = std::clamp(p_[PDrive].load(std::memory_order_relaxed), 0.05f, 2.0f);
        const float fixed = std::clamp(p_[PFixed].load(std::memory_order_relaxed), 0.0f, 1.0f);
        float lo = std::clamp(p_[POutLo].load(std::memory_order_relaxed), 0.0f, 1.0f);
        float hi = std::clamp(p_[POutHi].load(std::memory_order_relaxed), 0.0f, 1.0f);
        if (lo > hi) std::swap(lo, hi);
        const float rnd = std::clamp(p_[PRandom].load(std::memory_order_relaxed), 0.0f, 1.0f);
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
                    float dev = rnd * 0.5f;
                    float r = dir == 1 ? u * dev : dir == 2 ? -u * dev : (u * 2.0f - 1.0f) * dev;
                    v += r;
                }
                v = std::clamp(v, lo, hi);
                const int outVel = (int)std::lround(v * 127.0f);
                lastIn_.store((int)std::lround(std::clamp(e.vel, 0.0f, 1.0f) * 127.0f), std::memory_order_relaxed);
                lastOut_.store(outVel, std::memory_order_relaxed);
                pushHist(std::clamp(e.vel, 0.0f, 1.0f), v);
                e.vel = std::clamp(v, 0.0f, 1.0f);
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
    static constexpr int kHist = 12;
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
    uint32_t rng_ = 0x2545f491u;
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
