// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Random — a MIDI effect (midiKind 5), reworked to mockup 3b. With probability Chance,
// a note is humanised across up to five dimensions: Note (±semitones), Velocity, Timing
// (micro-shift), Skip (drop it) and Octave — each with its own amount. The random values are
// shaped by a Distribution (Gauss / Even / Walk), drawn Per note or Per bar, optionally kept
// in scale, and reproducible when the Seed is Locked. A note-on and its note-off transpose
// identically (offset remembered per pitch), and skipped notes drop both events.
//
// Param layout is APPEND-ONLY: 0 Chance / 1 Note Range stay put so old projects load unchanged.

#pragma once

#include "MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>
#include <cstring>

namespace nota {

class MidiRandom final : public MidiDevice {
public:
    enum Param {
        PChance = 0, PNoteRange, PVelAmt, PTimeAmt, PSkip, POctAmt,
        PDist, PRate, PStayInScale, PLocked, PSeed,
        kNumParams
    };
    MidiRandom() {
        p_[PChance].store(0.5f); p_[PNoteRange].store(7.0f);
        p_[PVelAmt].store(0.0f); p_[PTimeAmt].store(0.0f); p_[PSkip].store(0.0f); p_[POctAmt].store(0.0f);
        p_[PDist].store(0.0f); p_[PRate].store(0.0f); p_[PStayInScale].store(0.0f); p_[PLocked].store(0.0f); p_[PSeed].store(1.0f);
    }

    int32_t midiKind() const override { return 5; }
    const char* displayName() const override { return "Nota Random"; }

    void setSampleRate(double sr, int32_t) override { sr_ = sr > 0 ? sr : 44100.0; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case PChance: return "Chance"; case PNoteRange: return "Note Range"; case PVelAmt: return "Vel Amt";
            case PTimeAmt: return "Time Amt"; case PSkip: return "Skip"; case POctAmt: return "Oct Amt";
            case PDist: return "Dist"; case PRate: return "Rate"; case PStayInScale: return "Stay In Scale";
            case PLocked: return "Locked"; case PSeed: return "Seed"; default: return "";
        }
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) { case PNoteRange: return 12.0f; case PDist: return 2.0f; case PSeed: return 999.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }
    void  reset() override {
        std::memset(offset_, 0, sizeof(offset_)); std::memset(skipped_, 0, sizeof(skipped_));
        walk_ = 0.0f; noteCounter_ = 0; lastBar_ = INT64_MIN; intBeat_ = 0.0;
    }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiRandom>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t frames, double beatStart, double spb, bool playing) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; intBeat_ = playing ? beatStart : intBeat_; return; }
        const double b0 = playing ? beatStart : intBeat_;
        const float chance = std::clamp(p_[PChance].load(std::memory_order_relaxed), 0.0f, 1.0f);
        const int   nr   = std::clamp((int)std::lround(p_[PNoteRange].load(std::memory_order_relaxed)), 0, 12);
        const float velAmt = std::clamp(p_[PVelAmt].load(std::memory_order_relaxed), 0.0f, 1.0f);
        const float timeAmt = std::clamp(p_[PTimeAmt].load(std::memory_order_relaxed), 0.0f, 1.0f);
        const float skipAmt = std::clamp(p_[PSkip].load(std::memory_order_relaxed), 0.0f, 1.0f);
        const float octAmt = std::clamp(p_[POctAmt].load(std::memory_order_relaxed), 0.0f, 1.0f);
        const int   dist = std::clamp((int)std::lround(p_[PDist].load(std::memory_order_relaxed)), 0, 2);
        const bool  perBar = p_[PRate].load(std::memory_order_relaxed) >= 0.5f;
        const bool  stay = p_[PStayInScale].load(std::memory_order_relaxed) >= 0.5f;
        const bool  locked = p_[PLocked].load(std::memory_order_relaxed) >= 0.5f;
        const uint32_t seed = (uint32_t)std::lround(p_[PSeed].load(std::memory_order_relaxed));

        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            if (e.pitch < 0 || e.pitch > 127) { if (nOut < maxOut) out[nOut++] = e; continue; }
            if (e.on) {
                const double noteBeat = b0 + (double)e.off / spb;
                // Locked → key the randomness on the note's musical position + pitch so it is
                // reproducible across playbacks with no running state; unlocked → free RNG.
                const uint32_t beatKey = hash(hash(seed, (uint32_t)std::llround(std::max(0.0, noteBeat) * 480.0)), (uint32_t)e.pitch);
                uint32_t ns = locked ? hash(beatKey, 0xA5A5A5A5u) : rng_;
                bool active = next(ns) < chance;
                bool skip = active && skipAmt > 0.0f && next(ns) < skipAmt;
                if (!locked) rng_ = ns;

                int pitchOff = 0, oct = 0, timeSamp = 0; float velOff = 0.0f;
                if (active) {
                    if (perBar) {
                        int64_t bar = (int64_t)std::floor(noteBeat / 4.0);
                        if (bar != lastBar_) { lastBar_ = bar; drawSet(locked ? hash(seed ^ 0x5bd1e995u, (uint32_t)bar) : rng_, dist, nr, octAmt, velAmt, timeAmt, locked); }
                        pitchOff = barPitch_; oct = barOct_; velOff = barVel_; timeSamp = barTime_;
                    } else {
                        uint32_t os = locked ? hash(beatKey, 0x3c6ef35fu) : rng_;
                        pitchOff = pickPitch(dist, os, nr, !locked); oct = pickOct(os, octAmt); velOff = pickVel(os, velAmt); timeSamp = pickTime(os, timeAmt);
                        if (!locked) rng_ = os;
                    }
                }
                ++noteCounter_;

                if (skip) { skipped_[e.pitch] = 1; continue; }
                skipped_[e.pitch] = 0;
                int delta = stay ? foldToMajor(pitchOff + oct) : pitchOff + oct;
                int outPitch = std::clamp(e.pitch + delta, 0, 127);
                offset_[e.pitch] = (int8_t)std::clamp(outPitch - e.pitch, -120, 120);
                float outVel = std::clamp(e.vel + velOff, 0.0f, 1.0f);
                int outOff = std::clamp((int)e.off + timeSamp, 0, frames - 1);
                if (nOut < maxOut) out[nOut++] = { outOff, true, outPitch, outVel };
            } else {
                if (skipped_[e.pitch]) { skipped_[e.pitch] = 0; continue; }   // drop the off for a skipped note
                int p = std::clamp(e.pitch + offset_[e.pitch], 0, 127);
                if (nOut < maxOut) out[nOut++] = { e.off, false, p, e.vel };
            }
        }
        intBeat_ = b0 + frames / spb;
    }

private:
    static uint32_t hash(uint32_t a, uint32_t b) { uint32_t h = a * 2654435761u + b * 40503u + 0x9e3779b9u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; return h; }
    static float u01(uint32_t& s) { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216.0f; }
    float next(uint32_t& s) { return u01(s); }

    // acc = accumulate the random walk (unlocked); locked mode draws statelessly so it stays
    // reproducible, in which case Walk falls back to an even draw.
    int pickPitch(int dist, uint32_t& s, int range, bool acc = true) {
        if (range <= 0) return 0;
        float u;
        if (dist == 2 && acc) { walk_ += (u01(s) * 2.0f - 1.0f) * 0.34f; walk_ = std::clamp(walk_, -1.0f, 1.0f); u = walk_; }
        else if (dist == 1 || dist == 2) u = u01(s) * 2.0f - 1.0f;
        else u = ((u01(s) + u01(s) + u01(s)) / 3.0f - 0.5f) * 2.0f;   // ~Gauss
        return (int)std::lround(u * range);
    }
    int pickOct(uint32_t& s, float octAmt) { int n = (int)std::lround(octAmt * 2.0f); if (n <= 0) return 0; int k = (int)(u01(s) * (2 * n + 1)) - n; return k * 12; }
    float pickVel(uint32_t& s, float velAmt) { return (u01(s) * 2.0f - 1.0f) * velAmt * 0.5f; }
    int pickTime(uint32_t& s, float timeAmt) { double ms = (u01(s) * 2.0 - 1.0) * timeAmt * 50.0; return (int)std::lround(ms / 1000.0 * sr_); }
    void drawSet(uint32_t s, int dist, int nr, float octAmt, float velAmt, float timeAmt, bool locked) {
        barPitch_ = pickPitch(dist, s, nr, !locked); barOct_ = pickOct(s, octAmt); barVel_ = pickVel(s, velAmt); barTime_ = pickTime(s, timeAmt);
        if (!locked) rng_ = s;
    }
    static int foldToMajor(int iv) {
        static const int deg[7] = { 0, 2, 4, 5, 7, 9, 11 };
        int within = ((iv % 12) + 12) % 12, octs = (iv - within) / 12, best = 0, bd = 99;
        for (int d = 0; d < 7; ++d) { int df = std::abs(deg[d] - within); if (df < bd) { bd = df; best = deg[d]; } }
        return octs * 12 + best;
    }

    uint32_t rng_ = 0x9e3779b9u;
    float    walk_ = 0.0f;
    uint32_t noteCounter_ = 0;
    int64_t  lastBar_ = INT64_MIN;
    int      barPitch_ = 0, barOct_ = 0, barTime_ = 0; float barVel_ = 0.0f;
    double   sr_ = 44100.0, intBeat_ = 0.0;
    int8_t   offset_[128] = {};
    uint8_t  skipped_[128] = {};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
