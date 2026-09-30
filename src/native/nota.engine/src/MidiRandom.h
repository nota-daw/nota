// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Random — a MIDI effect (midiKind 5), almanac rework (mockups 1a / 1b). With probability
// Chance a note is varied across up to five dimensions: Note (±semitones), Velocity (±64),
// Timing (a 0..100 ms delay — a real-time effect can only play late), Skip (drop it) and
// Octave (±2). The random values are shaped by a Distribution — Gauss (a bell), Even (flat)
// or Walk (a bounded random walk that drifts from note to note) — drawn Per note or once Per
// bar, and optionally snapped into C major (Stay in scale).
//
// The roll is keyed on the Seed and the bar: every bar is a fresh roll, and each note's draw
// is a pure function of (seed, bar, its position in the bar, pitch) — so the editor can show
// exactly what will play. Unlocked, a new pass (after a stop or a loop wrap) re-rolls too;
// Locked holds one roll (the bar that was showing when Lock was pressed — "Lock Bar") and
// repeats it every bar, reproducibly. A note-on and its note-off move identically (transpose
// and delay remembered per pitch), and a skipped note drops both events.
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
        PView,      // editor card size (0 = L, 1 = S) — not a sound param
        PLockBar,   // the bar whose roll Lock holds
        kNumParams
    };
    static constexpr double kBarBeats = 4.0;
    static constexpr double kMaxDelayMs = 100.0;

    MidiRandom() {
        p_[PChance].store(0.5f); p_[PNoteRange].store(7.0f);
        p_[PVelAmt].store(0.0f); p_[PTimeAmt].store(0.0f); p_[PSkip].store(0.0f); p_[POctAmt].store(0.0f);
        p_[PDist].store(0.0f); p_[PRate].store(0.0f); p_[PStayInScale].store(0.0f); p_[PLocked].store(0.0f); p_[PSeed].store(1.0f);
        p_[PView].store(0.0f); p_[PLockBar].store(0.0f);
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
            case PLocked: return "Locked"; case PSeed: return "Seed"; case PView: return "View"; case PLockBar: return "Lock Bar";
            default: return "";
        }
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) {
            case PNoteRange: return 12.0f; case PDist: return 2.0f; case PSeed: return 999.0f; case PLockBar: return 9999.0f;
            default: return 1.0f;
        }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }
    void  reset() override {
        std::memset(offset_, 0, sizeof(offset_)); std::memset(skipped_, 0, sizeof(skipped_)); std::memset(delay_, 0, sizeof(delay_));
        nQueue_ = 0; lastRollKey_ = UINT64_MAX; intBeat_ = 0.0;
        pass_.fetch_add(1, std::memory_order_relaxed);
    }

    // Editor / MCP telemetry: [0] the bar being rolled, [1] notes in, [2] notes changed,
    // [3] notes skipped (counts since the device was created), [4] last input pitch, [5] its
    // output pitch (−1 = skipped), [6] input velocity 0..1, [7] output velocity, [8] its delay
    // in ms, [9] events waiting in the delay queue.
    int32_t midiScope(float* out, int32_t maxN) const override {
        const float v[10] = {
            (float)bar_.load(std::memory_order_relaxed), (float)notesIn_.load(std::memory_order_relaxed),
            (float)changed_.load(std::memory_order_relaxed), (float)skippedN_.load(std::memory_order_relaxed),
            (float)lastIn_.load(std::memory_order_relaxed), (float)lastOut_.load(std::memory_order_relaxed),
            lastInVel_.load(std::memory_order_relaxed), lastOutVel_.load(std::memory_order_relaxed),
            lastDelayMs_.load(std::memory_order_relaxed), (float)queued_.load(std::memory_order_relaxed) };
        const int n = std::min<int32_t>(maxN, 10);
        for (int i = 0; i < n; ++i) out[i] = v[i];
        return n;
    }
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiRandom>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->sr_ = sr_;
        c->setBypassed(bypassed());
        return c;
    }

    // ---- the roll (shared with the editor's model, which ports these exactly) -------------------
    static uint32_t hash(uint32_t a, uint32_t b) { uint32_t h = a * 2654435761u + b * 40503u + 0x9e3779b9u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; return h; }
    static float u01(uint32_t& s) { s = s * 1664525u + 1013904223u; return (float)(s >> 8) / 16777216.0f; }
    // One draw in −1..1 from the distribution. Walk steps the caller's walk state (reflecting
    // at ±1); Gauss is a Box–Muller normal scaled so ±1 ≈ 2.6 σ.
    static float draw(int dist, uint32_t& s, float& walk) {
        if (dist == 1) return u01(s) * 2.0f - 1.0f;
        if (dist == 2) {
            walk += (u01(s) * 2.0f - 1.0f) * 0.45f;
            if (walk > 1.0f) walk = 2.0f - walk;
            if (walk < -1.0f) walk = -2.0f - walk;
            return walk;
        }
        const float a = std::max(u01(s), 1e-9f), b = u01(s);
        const float g = std::sqrt(-2.0f * std::log(a)) * std::cos(6.2831853f * b);
        return std::clamp(g / 2.6f, -1.0f, 1.0f);
    }
    static int snapToCMajor(int p) {
        static const bool in[12] = { true, false, true, false, true, true, false, true, false, true, false, true };
        return in[((p % 12) + 12) % 12] ? p : p - 1;
    }
    static uint32_t rollKey(uint32_t seed, uint32_t barKey) { return hash(seed * 7919u + 17u, barKey * 104729u + 3u); }
    static uint32_t noteKey(uint32_t roll, int tick, int pitch) { return hash(roll, (uint32_t)tick * 128u + (uint32_t)pitch); }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t frames, double beatStart, double spb, bool playing) override {
        nOut = 0;
        const double b0 = playing ? beatStart : intBeat_;
        const int64_t blockStart = clock_;
        const bool by = bypassed();
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
        const uint32_t seed = (uint32_t)std::lround(std::max(0.0f, p_[PSeed].load(std::memory_order_relaxed)));
        const uint32_t lockBar = (uint32_t)std::lround(std::max(0.0f, p_[PLockBar].load(std::memory_order_relaxed)));
        const uint32_t pass = locked ? 0u : pass_.load(std::memory_order_relaxed);

        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            if (e.pitch < 0 || e.pitch > 127) { push(out, nOut, maxOut, e); continue; }
            if (!e.on) {
                const int p = e.pitch;
                if (skipped_[p]) { skipped_[p] = 0; continue; }                // the off of a skipped note
                MidiEv off{ e.off, false, std::clamp(p + offset_[p], 0, 127), e.vel };
                if (delay_[p] > 0) enqueue(blockStart + e.off + delay_[p], off); else push(out, nOut, maxOut, off);
                offset_[p] = 0; delay_[p] = 0;
                continue;
            }
            if (by) { offset_[e.pitch] = 0; delay_[e.pitch] = 0; skipped_[e.pitch] = 0; push(out, nOut, maxOut, e); continue; }

            const double noteBeat = std::max(0.0, b0 + (double)e.off / spb);
            const int64_t bar = (int64_t)std::floor(noteBeat / kBarBeats + 1e-9);
            const uint32_t barKey = locked ? lockBar : (uint32_t)bar;
            const uint32_t roll = rollKey(seed, barKey) ^ (pass * 0x85ebca6bu);
            const uint64_t rk = ((uint64_t)barKey << 32) ^ (uint64_t)(uint32_t)bar ^ ((uint64_t)pass << 20);
            if (rk != lastRollKey_) {                 // a new bar: walks restart, the per-bar set re-draws
                lastRollKey_ = rk;
                walk_[0] = walk_[1] = walk_[2] = walk_[3] = 0.0f;
                uint32_t bs = hash(roll, 0xB0B5EEDu); float w[4] = { 0, 0, 0, 0 };
                for (int d = 0; d < 4; ++d) barD_[d] = draw(dist, bs, w[d]);
            }
            const int tick = (int)std::lround((noteBeat - (double)bar * kBarBeats) * 480.0);
            const uint32_t nk = noteKey(roll, tick, e.pitch);
            uint32_t hs = hash(nk, 0xA5A5A5A5u);
            const bool hit = u01(hs) < chance;
            const bool skip = hit && u01(hs) < skipAmt;
            float D[4] = { 0, 0, 0, 0 };
            if (perBar) { for (int d = 0; d < 4; ++d) D[d] = barD_[d]; }
            else { uint32_t ds = hash(nk, 0x3c6ef35fu); for (int d = 0; d < 4; ++d) D[d] = draw(dist, ds, walk_[d]); }

            int outPitch = e.pitch; float outVel = e.vel; int32_t delay = 0;
            if (hit) {
                outPitch = e.pitch + (int)std::lround(D[0] * nr) + 12 * (int)std::lround(D[3] * octAmt * 2.0f);
                if (stay) outPitch = snapToCMajor(outPitch);
                outPitch = std::clamp(outPitch, 0, 127);
                outVel = std::clamp(e.vel + D[1] * velAmt * 64.0f / 127.0f, 1.0f / 127.0f, 1.0f);
                delay = (int32_t)std::lround(std::fabs(D[2]) * timeAmt * kMaxDelayMs * 0.001 * sr_);
            }
            bar_.store((int32_t)bar, std::memory_order_relaxed);
            notesIn_.fetch_add(1, std::memory_order_relaxed);
            lastIn_.store(e.pitch, std::memory_order_relaxed); lastInVel_.store(e.vel, std::memory_order_relaxed);
            if (skip) {
                skipped_[e.pitch] = 1; offset_[e.pitch] = 0; delay_[e.pitch] = 0;
                skippedN_.fetch_add(1, std::memory_order_relaxed);
                lastOut_.store(-1, std::memory_order_relaxed); lastOutVel_.store(0.0f, std::memory_order_relaxed); lastDelayMs_.store(0.0f, std::memory_order_relaxed);
                continue;
            }
            if (outPitch != e.pitch || std::fabs(outVel - e.vel) > 0.5f / 127.0f || delay > 0) changed_.fetch_add(1, std::memory_order_relaxed);
            lastOut_.store(outPitch, std::memory_order_relaxed); lastOutVel_.store(outVel, std::memory_order_relaxed);
            lastDelayMs_.store((float)(delay * 1000.0 / sr_), std::memory_order_relaxed);
            skipped_[e.pitch] = 0;
            offset_[e.pitch] = (int8_t)std::clamp(outPitch - e.pitch, -120, 120);
            delay_[e.pitch] = delay;
            MidiEv on{ e.off, true, outPitch, outVel, e.dur };
            if (delay > 0) enqueue(blockStart + e.off + delay, on); else push(out, nOut, maxOut, on);
        }

        // Delayed events falling due in this block.
        for (int k = 0; k < nQueue_;) {
            if (queue_[k].due < blockStart + frames) {
                MidiEv ev = queue_[k].ev;
                ev.off = (int32_t)std::clamp<int64_t>(queue_[k].due - blockStart, 0, frames - 1);
                push(out, nOut, maxOut, ev);
                queue_[k] = queue_[--nQueue_];
            } else ++k;
        }
        queued_.store(nQueue_, std::memory_order_relaxed);
        // Keep the block sorted by offset (stable; offs before ons at the same sample).
        for (int a = 1; a < nOut; ++a) {
            MidiEv v = out[a]; int b = a - 1;
            while (b >= 0 && (out[b].off > v.off || (out[b].off == v.off && out[b].on && !v.on))) { out[b + 1] = out[b]; --b; }
            out[b + 1] = v;
        }
        clock_ += frames;
        intBeat_ = b0 + frames / spb;
    }

private:
    struct Pending { int64_t due; MidiEv ev; };
    static constexpr int kQueue = 512;

    static void push(MidiEv* out, int& nOut, int maxOut, const MidiEv& e) { if (nOut < maxOut) out[nOut++] = e; }
    void enqueue(int64_t due, const MidiEv& e) {
        if (nQueue_ < kQueue) queue_[nQueue_++] = { due, e };
    }

    double   sr_ = 44100.0, intBeat_ = 0.0;
    int64_t  clock_ = 0;
    uint64_t lastRollKey_ = UINT64_MAX;
    float    walk_[4] = { 0, 0, 0, 0 };
    float    barD_[4] = { 0, 0, 0, 0 };
    int8_t   offset_[128] = {};
    uint8_t  skipped_[128] = {};
    int32_t  delay_[128] = {};
    Pending  queue_[kQueue];
    int      nQueue_ = 0;
    std::atomic<uint32_t> pass_{0};
    std::atomic<int32_t>  bar_{0}, notesIn_{0}, changed_{0}, skippedN_{0}, lastIn_{-1}, lastOut_{-1}, queued_{0};
    std::atomic<float>    lastInVel_{0.0f}, lastOutVel_{0.0f}, lastDelayMs_{0.0f};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
