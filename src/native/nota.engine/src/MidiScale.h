// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Scale — a MIDI effect (midiKind 2), reworked to mockup 3b. Snaps incoming pitches
// into a chosen scale, either from a preset or a fully editable 12-note mask (Custom), and
// Folds out-of-scale notes to the Nearest / Down / Up in-scale note. Extras: a Range limiter
// (notes outside pass through), Follow Key (root tracks the notes you play, from a decaying
// pitch-class histogram) and Learn (build the Custom mask from what you just played). The
// last remapped note is published for the card's IN→OUT readout.
//
// Param layout is APPEND-ONLY: indices 0..2 keep the original Root / Scale / Transpose, so
// old projects load unchanged; 3..19 add the mask, fold, follow-key, range and learn.

#pragma once

#include "MidiDevice.h"
#include <algorithm>
#include <cstdio>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

class MidiScale final : public MidiDevice {
public:
    enum Param {
        PRoot = 0, PScale, PTranspose,
        PMask0, PMask1, PMask2, PMask3, PMask4, PMask5, PMask6, PMask7, PMask8, PMask9, PMask10, PMask11,
        PFold, PFollowKey, PRangeLo, PRangeHi, PLearn,
        kNumParams
    };
    static constexpr int kNumScales = 10;   // 0..9 presets; 10 = Custom (mask params)
    static constexpr int kCustom = 10;

    MidiScale() {
        p_[PRoot].store(0.0f); p_[PScale].store(1.0f); p_[PTranspose].store(0.0f);   // C, minor
        // Custom mask defaults to major (a sane starting point when the user switches to Custom).
        static const int maj[] = { 0, 2, 4, 5, 7, 9, 11 };
        for (int i = 0; i < 12; ++i) p_[PMask0 + i].store(0.0f);
        for (int d : maj) p_[PMask0 + d].store(1.0f);
        p_[PFold].store(0.0f); p_[PFollowKey].store(0.0f);
        p_[PRangeLo].store(0.0f); p_[PRangeHi].store(127.0f);   // full range (matches the old always-quantize)
        p_[PLearn].store(0.0f);
    }

    int32_t midiKind() const override { return 2; }
    const char* displayName() const override { return "Nota Scale"; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case PRoot: return "Root"; case PScale: return "Scale"; case PTranspose: return "Transpose";
            case PFold: return "Fold"; case PFollowKey: return "Follow Key";
            case PRangeLo: return "Range Low"; case PRangeHi: return "Range High"; case PLearn: return "Learn";
            default:
                if (i >= PMask0 && i <= PMask11) { static thread_local char b[12]; std::snprintf(b, sizeof(b), "Mask %d", i - PMask0); return b; }
                return "";
        }
    }
    float paramMin(int32_t i) const override { return i == PTranspose ? -24.0f : 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) {
            case PRoot: return 11.0f; case PScale: return kCustom; case PTranspose: return 24.0f;
            case PFold: return 2.0f; case PRangeLo: case PRangeHi: return 127.0f;
            default: return 1.0f;   // masks / follow / learn
        }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    // Live IN→OUT readout for the card (last remapped note-on); -1 = none yet.
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiScale>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    void reset() override { for (auto& h : hist_) h = 0.0f; }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t, double, double, bool) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; return; }

        // Learn (one-shot): build the Custom mask from the recent-input histogram.
        if (p_[PLearn].load(std::memory_order_relaxed) >= 0.5f) {
            doLearn(); p_[PLearn].store(0.0f, std::memory_order_relaxed);
        }
        int root = std::clamp((int)std::lround(p_[PRoot].load(std::memory_order_relaxed)), 0, 11);
        // Follow Key: root tracks the dominant recent pitch class.
        if (p_[PFollowKey].load(std::memory_order_relaxed) >= 0.5f) {
            int dom = dominantPc(); if (dom >= 0) { root = dom; p_[PRoot].store((float)dom, std::memory_order_relaxed); }
        }
        const uint16_t bits = effectiveMask();
        const int  fold = std::clamp((int)std::lround(p_[PFold].load(std::memory_order_relaxed)), 0, 2);
        const int  tr   = (int)std::lround(p_[PTranspose].load(std::memory_order_relaxed));
        const int  lo   = std::clamp((int)std::lround(p_[PRangeLo].load(std::memory_order_relaxed)), 0, 127);
        const int  hi   = std::clamp((int)std::lround(p_[PRangeHi].load(std::memory_order_relaxed)), 0, 127);

        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            int p = e.pitch;
            if (bits && e.pitch >= std::min(lo, hi) && e.pitch <= std::max(lo, hi))
                p = quantize(e.pitch, root, bits, fold) + tr;
            if (p < 0 || p > 127) continue;
            if (nOut < maxOut) out[nOut++] = { e.off, e.on, p, e.vel };
            if (e.on) {
                lastIn_.store(e.pitch, std::memory_order_relaxed);
                lastOut_.store(p, std::memory_order_relaxed);
                for (auto& h : hist_) h *= 0.9f;                 // decay
                hist_[((e.pitch % 12) + 12) % 12] += 1.0f;        // remember what was played
            }
        }
    }

private:
    static uint16_t presetMask(int sc) {
        static const uint16_t m[kNumScales] = {
            2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095,
            // major, natural minor, harmonic minor, dorian, phrygian, lydian, mixolydian, penta maj, penta min, chromatic
        };
        return m[std::clamp(sc, 0, kNumScales - 1)];
    }
    uint16_t effectiveMask() const {
        int sc = (int)std::lround(p_[PScale].load(std::memory_order_relaxed));
        if (sc < kCustom) return presetMask(sc);
        uint16_t m = 0;
        for (int i = 0; i < 12; ++i) if (p_[PMask0 + i].load(std::memory_order_relaxed) >= 0.5f) m |= (uint16_t)(1 << i);
        return m;
    }
    static int quantize(int pitch, int root, uint16_t bits, int fold) {
        int pc = ((pitch - root) % 12 + 12) % 12;
        auto has = [&](int x) { return (bits & (1 << (((x) % 12 + 12) % 12))) != 0; };
        if (fold == 1) { for (int d = 0; d < 12; ++d) if (has(pc - d)) return pitch - d; }        // Down
        else if (fold == 2) { for (int d = 0; d < 12; ++d) if (has(pc + d)) return pitch + d; }   // Up
        else { for (int d = 0; d < 7; ++d) { if (has(pc + d)) return pitch + d; if (has(pc - d)) return pitch - d; } }  // Nearest
        return pitch;
    }
    int dominantPc() const {
        int best = -1; float bv = 1e-4f;
        for (int i = 0; i < 12; ++i) if (hist_[i] > bv) { bv = hist_[i]; best = i; }
        return best;
    }
    void doLearn() {
        float mx = 0; for (float h : hist_) mx = std::max(mx, h);
        if (mx < 1e-4f) return;   // nothing played — keep the current mask
        int root = std::clamp((int)std::lround(p_[PRoot].load(std::memory_order_relaxed)), 0, 11);
        for (int i = 0; i < 12; ++i) {
            int rel = ((i - root) % 12 + 12) % 12;   // store relative to root
            p_[PMask0 + rel].store(hist_[i] > 0.12f * mx ? 1.0f : 0.0f, std::memory_order_relaxed);
        }
        p_[PScale].store((float)kCustom, std::memory_order_relaxed);
    }

    float hist_[12] = {};
    std::atomic<int32_t> lastIn_{-1}, lastOut_{-1};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
