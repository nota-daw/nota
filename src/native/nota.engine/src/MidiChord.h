// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chord — a MIDI effect (midiKind 1), reworked to mockup 3b. Each incoming note is
// played (optionally) plus up to six added voices at fixed semitone offsets (0 = voice
// off), each with its own relative velocity. Extra shaping: Strum (spread the note-ons
// over time), Spread (open the voicing by octaves), Fold (snap added notes into the
// played note's major scale) and Keep Root. Like a classic chord MIDI effect, but with strum + fold.
//
// Param layout is APPEND-ONLY so old projects load unchanged: indices 0..4 stay the
// original Voice 1..5 semitone offsets; index 5 adds Voice 6; 6..9 add the globals; 10..15
// add the per-voice velocities. New params default to neutral (Keep Root on, rest off).

#pragma once

#include "MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>
#include <cstdio>

namespace nota {

class MidiChord final : public MidiDevice {
public:
    static constexpr int kVoices = 6;
    enum Param {
        Voice1 = 0, Voice2, Voice3, Voice4, Voice5, Voice6,   // semitone offsets (0 = off)
        Strum, KeepRoot, Spread, Fold,
        Vel1, Vel2, Vel3, Vel4, Vel5, Vel6,                    // per-voice velocity offset (%)
        kNumParams
    };

    MidiChord() {
        p_[Voice1].store(4.0f);   // major third
        p_[Voice2].store(7.0f);   // fifth
        for (int v = Voice3; v <= Voice6; ++v) p_[v].store(0.0f);
        p_[Strum].store(0.0f);
        p_[KeepRoot].store(1.0f); // pass the played note (matches the pre-rework behaviour)
        p_[Spread].store(0.0f);
        p_[Fold].store(0.0f);
        for (int v = Vel1; v <= Vel6; ++v) p_[v].store(0.0f);
    }

    int32_t midiKind() const override { return 1; }
    const char* displayName() const override { return "Nota Chord"; }

    void setSampleRate(double sr, int32_t) override { sr_ = sr > 0 ? sr : 44100.0; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Voice1: return "Voice 1"; case Voice2: return "Voice 2"; case Voice3: return "Voice 3";
            case Voice4: return "Voice 4"; case Voice5: return "Voice 5"; case Voice6: return "Voice 6";
            case Strum: return "Strum"; case KeepRoot: return "Keep Root"; case Spread: return "Spread"; case Fold: return "Fold";
            case Vel1: return "Vel 1"; case Vel2: return "Vel 2"; case Vel3: return "Vel 3";
            case Vel4: return "Vel 4"; case Vel5: return "Vel 5"; case Vel6: return "Vel 6";
            default: return "";
        }
    }
    float paramMin(int32_t i) const override {
        if (i >= Voice1 && i <= Voice6) return -24.0f;
        if (i >= Vel1 && i <= Vel6) return -100.0f;
        return 0.0f;   // strum / keeproot / spread / fold
    }
    float paramMax(int32_t i) const override {
        if (i >= Voice1 && i <= Voice6) return 24.0f;
        if (i >= Vel1 && i <= Vel6) return 100.0f;
        if (i == Strum) return 120.0f;
        if (i == Spread) return 100.0f;
        return 1.0f;   // keeproot / fold
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiChord>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    void reset() override { nPend_ = 0; clock_ = 0; }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t frames, double, double, bool) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; clock_ += frames; return; }

        const int64_t blockEnd = clock_ + frames;
        const bool  keepRoot = p_[KeepRoot].load(std::memory_order_relaxed) >= 0.5f;
        const bool  fold     = p_[Fold].load(std::memory_order_relaxed) >= 0.5f;
        const double spread01 = std::clamp(p_[Spread].load(std::memory_order_relaxed) / 100.0, 0.0, 1.0);
        const double strumSamp = std::max(0.0f, p_[Strum].load(std::memory_order_relaxed)) * 0.001 * sr_;
        int  semi[kVoices]; float velo[kVoices];
        for (int v = 0; v < kVoices; ++v) {
            semi[v] = (int)std::lround(p_[Voice1 + v].load(std::memory_order_relaxed));
            velo[v] = p_[Vel1 + v].load(std::memory_order_relaxed);
        }

        // 1) Drain pending (strummed) note-ons that now fall inside this block.
        for (int k = 0; k < nPend_;) {
            if (pend_[k].abs < blockEnd) {
                if (nOut < maxOut) { int off = (int)(pend_[k].abs - clock_); out[nOut++] = { off < 0 ? 0 : off, true, pend_[k].pitch, pend_[k].vel }; }
                pend_[k] = pend_[--nPend_];
            } else ++k;
        }

        // 2) Process input events.
        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            const int64_t absEv = clock_ + e.off;

            // Build the output pitches for this key (root + active added voices).
            int  pit[kVoices + 1]; float vel[kVoices + 1]; int n = 0;
            if (keepRoot) { pit[n] = e.pitch; vel[n] = e.vel; ++n; }
            for (int v = 0; v < kVoices; ++v) {
                if (semi[v] == 0) continue;
                int p = e.pitch + (fold ? foldToMajor(semi[v]) : semi[v]);
                if (spread01 > 0.0) p += 12 * (int)std::floor(spread01 * (v + 1) + 1e-9);   // open the voicing
                if (p < 0 || p > 127) continue;
                float vl = std::clamp(e.vel * (1.0f + velo[v] / 100.0f), 0.0f, 1.0f);
                pit[n] = p; vel[n] = vl; ++n;
            }

            if (e.on) {
                // Strum: order the sounding notes low→high and delay each by its rank.
                sortByPitch(pit, vel, n);
                for (int k = 0; k < n; ++k) {
                    int64_t at = absEv + (int64_t)std::llround(strumSamp * k);
                    if (at < blockEnd) { if (nOut < maxOut) { int off = (int)(at - clock_); out[nOut++] = { off < 0 ? 0 : off, true, pit[k], vel[k] }; } }
                    else if (nPend_ < kMaxPend) pend_[nPend_++] = { at, pit[k], vel[k] };
                }
            } else {
                // Note-off: for each output pitch, cancel a still-pending on, else emit the off.
                for (int k = 0; k < n; ++k) {
                    if (cancelPending(pit[k])) continue;
                    if (nOut < maxOut) out[nOut++] = { e.off, false, pit[k], e.vel };
                }
            }
        }
        clock_ = blockEnd;
    }

private:
    static int foldToMajor(int iv) {
        static const int deg[7] = { 0, 2, 4, 5, 7, 9, 11 };
        int within = ((iv % 12) + 12) % 12;
        int octs = (iv - within) / 12;
        int best = deg[0], bd = 99;
        for (int d = 0; d < 7; ++d) { int diff = std::abs(deg[d] - within); if (diff < bd) { bd = diff; best = deg[d]; } }
        return octs * 12 + best;
    }
    static void sortByPitch(int* pit, float* vel, int n) {
        for (int a = 1; a < n; ++a) { int p = pit[a]; float v = vel[a]; int b = a - 1;
            for (; b >= 0 && pit[b] > p; --b) { pit[b + 1] = pit[b]; vel[b + 1] = vel[b]; }
            pit[b + 1] = p; vel[b + 1] = v; }
    }
    bool cancelPending(int pitch) {
        for (int k = 0; k < nPend_; ++k) if (pend_[k].pitch == pitch) { pend_[k] = pend_[--nPend_]; return true; }
        return false;
    }

    static constexpr int kMaxPend = 256;
    struct Pend { int64_t abs; int32_t pitch; float vel; };
    Pend    pend_[kMaxPend];
    int     nPend_ = 0;
    int64_t clock_ = 0;
    double  sr_ = 44100.0;
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
