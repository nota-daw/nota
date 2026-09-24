// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pendulum — a built-in generative "warm electric keys" synth (kind 8) with no direct
// equivalent elsewhere. Held notes form a chord; a set of balls swing like pendulums
// across a triangular field (∧∨). A ball's horizontal position maps to a chord degree;
// as it sweeps it crosses degree boundaries and plays that note, so several balls at
// different rates/phases weave an ever-evolving arpeggio out of one held chord. The
// tone is a soft additive-sine "keys" voice with a detuned chorus partner for warmth.
//
//   held chord ─▶ [ball phases → position → chord degree → trigger on crossing] ─▶ warm keys voices ─▶ out
//
// Sync (bar-locked) or Free (seconds); Rate can go negative (reverse); Motion picks one
// of four swing curves (Linear · Pendulum · Ease · Bounce); Quantize snaps triggers to a
// grid; Chord Sort picks up/down; Spread detunes the balls' rates so they drift apart;
// Pan Spread fans them across the stereo field; Humanize adds gentle level/pitch jitter;
// Hold latches the chord and First note restarts the pattern on a new chord; an optional
// Scale snaps generated notes. Needs the musical clock via Instrument::setTransport
// (samples-per-beat for Sync/Quantize) and setTransportInfo (the bar, for the note strip).
// All params ride the plugin-param interface (normalized 0..1) → automation / persist /
// clone free. Header-only, alloc-free.
//
// Telemetry (scopeRead, kTele floats) for the editor's lanes / note strip and the MCP
// reader — see the layout above kTele. Mirrored by Nota.Application/PendulumModel.cs.

#pragma once

#include "Instrument.h"
#include "TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class PendulumSynth final : public Instrument {
public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        Balls = 0, Rate, Sync, Division, FreeRate, Motion, Quantize, ChordSort, Spread,
        NoteLen, Tone, Attack, Release, Detune, Volume,
        Wave, Bright, FM,
        // appended for the mockup 2l rework:
        Decay, PanSpread, Humanize, Hold, FirstNote, Root, ScaleMode, Reset,
        kNumParams
    };

    PendulumSynth() {
        set(Balls, 0.4f);       // 4 balls
        set(Rate, 0.72f);       // moderate forward
        set(Sync, 1.0f);        // synced
        set(Division, 0.25f);   // 1/2 note cycle
        set(FreeRate, 0.5f);    // ~1.3 s in Free
        set(Motion, 1.0f / 3);  // Pendulum
        set(Quantize, 0.5f);    // 1/16
        set(ChordSort, 0.0f);   // Up
        set(Spread, 0.25f);
        set(NoteLen, 0.4f);
        set(Tone, 0.42f);       // warm
        set(Attack, 0.14f);
        set(Release, 0.4f);
        set(Detune, 0.3f);
        set(Volume, 0.8f);
        set(Wave, 0.0f);        // Keys
        set(Bright, 0.4f);
        set(FM, 0.0f);
        set(Decay, 0.45f);
        set(PanSpread, 0.35f);
        set(Humanize, 0.2f);
        set(Hold, 0.0f);
        set(FirstNote, 0.0f);
        set(Root, 0.0f);        // C
        set(ScaleMode, 0.0f);   // Off
        set(Reset, 0.0f);
        resetPhases();
        for (int st = 0; st < kMaxSteps; ++st) { stepPitch_[st] = -1; stepBar_[st] = -8; }
        for (int b = 0; b < kMaxBalls; ++b) { ballPitch_[b] = -1; ballSince_[b] = 1e9; }
        for (auto& t : tele_) t.store(0.0f, std::memory_order_relaxed);
    }

    int32_t kind() const override { return 8; }
    const char* displayName() const override { return "Nota Pendulum"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }
    void setTransport(double beatStart, double spb, bool playing) override {
        if (spb > 0) spb_ = spb;
        playing_ = playing;
        if (playing && std::isfinite(beatStart)) beat_ = beatStart;   // stopped: the clock free-runs on
    }
    void setTransportInfo(const TransportInfo& ti) override {
        if (ti.tsNum > 0 && ti.tsDenom > 0) beatsPerBar_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 1.0, 8.0);
    }

    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // ---- telemetry ----------------------------------------------------------
    // Head (kHead): [0] sounding voices · [1] balls · [2] held-chord size · [3] last
    // generated pitch (−1 none yet) · [4] place in the bar 0..1 · [5] steps per bar (1/16
    // grid) · [6] seconds per swing at the base rate (0 = stopped) · [7] ball that fired
    // last (−1) · [8] seconds since the last note · [9] notes fired this bar · [10] beats
    // per bar · [11] transport rolling (0/1).
    // Then kMaxBalls × kBallStride: phase 0..1 · position 0..1 · direction (+1 → high,
    // −1 → low, 0 still) · pitch now (−1) · seconds to the wall ahead (−1 never) · seconds
    // since this ball fired · signed rate (1 = +100 %) · chord degree (−1).
    // Then kMaxSteps × 2 — the bar grid: pitch (−1 empty) · age in bars (0 this bar, 1 the
    // last). Then kMaxVoices level of each sounding voice (0 = silent slot).
    static constexpr int kHead = 12, kBallStride = 8, kMaxSteps = 32, kMaxLevels = 8;
    static constexpr int kBallsAt = kHead;
    static constexpr int kStepsAt = kBallsAt + 6 * kBallStride;
    static constexpr int kLevelsAt = kStepsAt + kMaxSteps * 2;
    static constexpr int kTele = kLevelsAt + kMaxLevels;
    int32_t scopeRead(float* out, int32_t maxN) const override {
        if (!out || maxN <= 0) return 0;
        const int n = std::min<int>(maxN, kTele);
        for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
        return n;
    }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "balls", "rate", "sync", "division", "freerate", "motion", "quantize", "chordsort", "spread",
            "notelen", "tone", "attack", "release", "detune", "volume", "wave", "bright", "fm",
            "decay", "panspread", "humanize", "hold", "firstnote", "root", "scalemode", "reset" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        // Display names group by their head in the automation menu (Balls › Count …);
        // the ids are what persists, so names may change.
        static const char* nm[] = {
            "Balls Count", "Motion Rate", "Motion Sync", "Motion Division", "Motion Time", "Motion Curve",
            "Balls Quantize", "Balls Sort", "Spread Rate",
            "Voice Length", "Voice Tone", "Voice Attack", "Voice Release", "Spread Detune", "Volume",
            "Voice Wave", "Voice Bright", "Voice FM",
            "Voice Decay", "Spread Pan", "Spread Humanize", "Balls Hold", "Balls Restart", "Scale Root",
            "Scale Mode", "Balls Reset" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            float v = pn_[i].load(std::memory_order_relaxed);
            std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
        }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data) return;
        const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
        for (int i = 0; i < n; ++i) {
            float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float));
            if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<PendulumSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- note events: held notes form the chord the balls arpeggiate --------
    void noteOn(int32_t pitch, float /*velocity*/) override {
        const bool hold  = get(Hold) >= 0.5f;
        const bool first = physCount_ == 0;                 // first key of a new press generation
        if (hold && first) heldCount_ = 0;                  // latched chord: a new press replaces it
        ++physCount_;
        const bool wasEmpty = heldCount_ == 0;
        for (int i = 0; i < heldCount_; ++i) if (held_[i] == pitch) { publishHeld(); return; }
        if (heldCount_ < kHeld) { held_[heldCount_++] = pitch; sortHeld(); }
        if (wasEmpty && get(FirstNote) >= 0.5f) resetPhases();   // restart the pattern on a fresh chord
        publishHeld();
    }
    void noteOff(int32_t pitch) override {
        if (physCount_ > 0) --physCount_;
        if (get(Hold) >= 0.5f) return;                      // latched: leave the chord sounding
        int w = 0;
        for (int r = 0; r < heldCount_; ++r) if (held_[r] != pitch) held_[w++] = held_[r];
        heldCount_ = w;
        if (heldCount_ == 0) for (int i = 0; i < kMaxBalls; ++i) lastIdx_[i] = -1;
        publishHeld();
    }
    void allNotesOff() override {
        heldCount_ = 0; physCount_ = 0;
        for (auto& v : voices_) v.active = false;
        for (int i = 0; i < kMaxBalls; ++i) lastIdx_[i] = -1;
        publishHeld();
    }

    // Live held chord (message-thread read of an audio-thread snapshot), for the editor's
    // pendulum field to label its pitch lanes. Returns the count written into out.
    int32_t heldNotes(int32_t* out, int32_t maxN) const override {
        if (!out || maxN <= 0) return 0;
        const int n = std::min<int>(heldN_.load(std::memory_order_relaxed), maxN);
        for (int i = 0; i < n; ++i) out[i] = heldAtom_[i].load(std::memory_order_relaxed);
        return n;
    }

    void render(float* out, int32_t frames) override {
        // Momentary Reset: restart every ball's swing, then self-clear the trigger.
        if (get(Reset) >= 0.5f) { resetPhases(); set(Reset, 0.0f); }

        const int    count    = std::clamp(2 + (int)std::lround(get(Balls) * 4.0f), 2, kMaxBalls);   // 2..6
        const double rateMult = ((double)get(Rate) - 0.5) * 4.0;                                       // ±2, sign = direction
        const bool   synced   = get(Sync) >= 0.5f;
        const double divBeats = kDivision[std::clamp((int)std::lround(get(Division) * 4.0f), 0, 4)];
        const double freeSec  = expMap(get(FreeRate), 0.1, 4.0);
        const int    motion   = std::clamp((int)std::lround(get(Motion) * 3.0f), 0, 3);
        const int    quant    = std::clamp((int)std::lround(get(Quantize) * 2.0f), 0, 2);              // 0 off,1 1/16,2 1/8
        const bool   sortDown = get(ChordSort) >= 0.5f;
        const double spread   = get(Spread);
        const double panAmt   = get(PanSpread);
        const double human    = get(Humanize);
        const double noteSec  = expMap(get(NoteLen), 0.05, 1.5);
        const int    gateLen  = std::max(1, (int)(noteSec * sampleRate_));
        const double toneCut  = expMap(get(Tone), 300.0, 12000.0);
        const float  atkInc   = (float)(1.0 / (expMap(get(Attack), 0.001, 1.0) * sampleRate_));
        const float  decInc   = (float)((1.0 - kSustain) / (expMap(get(Decay), 0.01, 2.0) * sampleRate_));
        const float  relInc   = (float)(1.0 / (expMap(get(Release), 0.02, 3.0) * sampleRate_));
        const double detCents = get(Detune) * 14.0;                                                   // ±cents on the partner
        const double detUp    = std::exp2(detCents / 1200.0), detDn = std::exp2(-detCents / 1200.0);
        const float  volume   = get(Volume);
        const double lpCoef   = 1.0 - std::exp(-2.0 * kPi * toneCut / sampleRate_);
        const int    wave     = std::clamp((int)std::lround(get(Wave) * 4.0f), 0, 4);
        const double hi       = 0.3 + (double)get(Bright);          // upper-partial gain (0.3..1.3)
        const double fmAmt    = (double)get(FM) * 2.5;              // phase-modulation index
        const int    root     = std::clamp((int)std::lround(get(Root) * 11.0f), 0, 11);
        const int    scale    = std::clamp((int)std::lround(get(ScaleMode) * 5.0f), 0, 5);            // 0 off..5 penta

        // Pendulum cycle length (samples) → per-sample phase increment (signed).
        const double cycleSamp = std::max(64.0, synced ? divBeats * spb_ : freeSec * sampleRate_);
        const double baseInc   = rateMult / cycleSamp;
        const double gridSamp  = quant == 0 ? 0.0 : (quant == 1 ? 0.25 : 0.5) * spb_;

        detUp_ = detUp; detDn_ = detDn;
        const double beatInc = spb_ > 0 ? 1.0 / spb_ : 0.0;
        const int    steps   = std::clamp((int)std::lround(beatsPerBar_ * 4.0), 1, kMaxSteps);

        for (int32_t i = 0; i < frames; ++i) {
            beat_ += beatInc;
            sinceNote_ += 1.0;
            for (int b = 0; b < kMaxBalls; ++b) ballSince_[b] += 1.0;
            // Grid gate for Quantize: evaluate crossings continuously (Off) or on grid ticks.
            bool evaluate = true;
            if (gridSamp > 0.0) { gridAcc_ += 1.0; if (gridAcc_ >= gridSamp) { gridAcc_ -= gridSamp; evaluate = true; } else evaluate = false; }

            for (int b = 0; b < count; ++b) {
                const double inc = baseInc * (1.0 + spread * (double)b * 0.37);     // per-ball rate spread
                ballPhase_[b] += inc;
                ballPhase_[b] -= std::floor(ballPhase_[b]);
                if (evaluate && heldCount_ > 0) {
                    const double pos = position(ballPhase_[b], motion);            // 0..1 across the field
                    int deg = std::clamp((int)(pos * heldCount_), 0, heldCount_ - 1);
                    if (deg != lastIdx_[b]) {
                        lastIdx_[b] = deg;
                        int pitch = sortDown ? held_[heldCount_ - 1 - deg] : held_[deg];
                        if (scale > 0) pitch = snapToScale(pitch, root, scale);
                        const double pan = std::clamp((pos - 0.5) * 2.0 * panAmt, -1.0, 1.0);
                        const float  lvl = 1.0f + (float)(human * 0.4 * rndBi());  // level jitter
                        const double det = 1.0 + human * 0.012 * rndBi();          // micro-detune jitter
                        trigger(pitch, gateLen, (float)pan, lvl, det);
                        noteFired(b, pitch, steps);
                    }
                }
            }

            // --- render active warm-keys voices ---
            float l = 0.0f, r = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;
                // AD(S)R gate: attack → decay to sustain → hold, release after the note length.
                if (v.gate > 0) {
                    --v.gate;
                    if (v.stage == 0)      { v.env += atkInc; if (v.env >= 1.0f) { v.env = 1.0f; v.stage = 1; } }
                    else if (v.stage == 1) { v.env -= decInc; if (v.env <= kSustain) { v.env = (float)kSustain; v.stage = 2; } }
                    if (v.gate == 0) v.stage = 3;
                } else {
                    v.env -= relInc; if (v.env <= 0.0f) { v.env = 0.0f; v.active = false; continue; }
                }

                double fm = fmAmt > 0.0 ? fmAmt * std::sin(kTwoPi * v.phM) : 0.0;
                v.phM += v.incM; if (v.phM >= 1.0) v.phM -= 1.0;
                double a = osc(wave, v.phA + fm, hi), c = osc(wave, v.phB + fm, hi);
                v.phA += v.incA; if (v.phA >= 1.0) v.phA -= 1.0;
                v.phB += v.incB; if (v.phB >= 1.0) v.phB -= 1.0;
                v.lpA += lpCoef * (a - v.lpA);
                v.lpB += lpCoef * (c - v.lpB);
                const float e = v.env * v.env * v.lvl;                              // soft, humanized level
                l += (float)v.lpA * e * v.gL;
                r += (float)v.lpB * e * v.gR;
            }
            const float g = volume * 0.22f;
            out[i * 2]     += l * g;
            out[i * 2 + 1] += r * g;
        }

        publish(count, baseInc, spread, motion, steps, rateMult, cycleSamp);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr double kSustain = 0.62;
    static constexpr int kVoices = 24;
    static constexpr int kMaxBalls = 6;
    static constexpr int kHeld = 16;
    static constexpr double kDivision[5] = { 4.0, 2.0, 1.0, 0.5, 0.25 };   // 1/1, 1/2, 1/4, 1/8, 1/16 (beats/cycle)

    struct Voice {
        bool   active = false;
        double phA = 0.0, phB = 0.0, incA = 0.0, incB = 0.0;
        double phM = 0.0, incM = 0.0;   // FM modulator
        double lpA = 0.0, lpB = 0.0;
        float  env = 0.0f;
        int    gate = 0;
        int    stage = 0;               // 0 attack, 1 decay, 2 sustain, 3 release
        float  gL = 0.707f, gR = 0.707f;
        float  lvl = 1.0f;
    };

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }

    void sortHeld() { std::sort(held_, held_ + heldCount_); }
    void resetPhases() { for (int i = 0; i < kMaxBalls; ++i) { ballPhase_[i] = (double)i / kMaxBalls; lastIdx_[i] = -1; } }
    void publishHeld() {
        const int n = std::min(heldCount_, kHeld);
        for (int i = 0; i < n; ++i) heldAtom_[i].store(held_[i], std::memory_order_relaxed);
        heldN_.store(n, std::memory_order_relaxed);
    }

    // Cheap deterministic ±1 noise for Humanize (audio thread, alloc-free).
    double rndBi() { rng_ = rng_ * 1664525u + 1013904223u; return (double)(rng_ >> 8) / 8388608.0 - 1.0; }

    // Snap a pitch to the nearest tone of a root+scale (search outward from the pitch).
    static int snapToScale(int pitch, int root, int scale) {
        static const uint16_t masks[6] = {
            0x0FFF,   // 0 unused (all)
            2741,     // Major       {0,2,4,5,7,9,11}
            1453,     // Minor       {0,2,3,5,7,8,10}
            1709,     // Dorian      {0,2,3,5,7,9,10}
            1717,     // Mixolydian  {0,2,4,5,7,9,10}
            1193 };   // Minor Penta {0,3,5,7,10}
        const uint16_t m = masks[std::clamp(scale, 0, 5)];
        for (int d = 0; d < 12; ++d) {
            for (int s = (d == 0 ? 0 : -1); s <= 1; s += 2) {
                const int cand = pitch + s * d;
                const int rel = ((cand - root) % 12 + 12) % 12;
                if (m & (1u << rel)) return cand;
                if (d == 0) break;
            }
        }
        return pitch;
    }

    // Position 0..1 from a ball's phase, one of four swing curves; all go 0→1→0 over the
    // cycle. 0 Linear (constant speed) · 1 Pendulum (sinusoidal, slow at the ends) ·
    // 2 Ease (eased in/out of the linear sweep) · 3 Bounce (settles with a small bounce).
    static double position(double ph, int type) {
        const double tri = 1.0 - 2.0 * std::fabs(ph - 0.5);
        switch (type) {
            case 1:  return 0.5 - 0.5 * std::cos(kTwoPi * ph);
            case 2:  return tri * tri * (3.0 - 2.0 * tri);                 // smoothstep(tri)
            case 3:  { double b = 1.0 - std::pow(1.0 - tri, 2.0);          // ease-out with a bounce ripple
                       return std::clamp(b + 0.06 * std::sin(kTwoPi * 3.0 * tri) * (1.0 - tri), 0.0, 1.0); }
            default: return tri;
        }
    }

    // Voice oscillator with selectable timbre (Wave) and upper-partial gain hi (Bright).
    // 0 Keys (soft sines) · 1 Glass (bright odd partials) · 2 Saw · 3 Square/hollow ·
    // 4 Bell (inharmonic partials). Phase may exceed [0,1) (FM) — sines are periodic;
    // saw/square take the fractional part.
    static double osc(int tb, double ph, double hi) {
        const double s1 = std::sin(kTwoPi * ph);
        switch (tb) {
            case 1:  return (s1 + hi * 0.9 * std::sin(kTwoPi * 3 * ph) + hi * 0.55 * std::sin(kTwoPi * 5 * ph)
                                + hi * 0.30 * std::sin(kTwoPi * 7 * ph)) / (1.0 + hi * 1.75);
            case 2:  { double f = ph - std::floor(ph); return (2.0 * f - 1.0) * 0.7; }
            case 3:  { double f = ph - std::floor(ph); return (f < 0.5 ? 0.55 : -0.55) + 0.28 * hi * std::sin(kTwoPi * 3 * ph); }
            case 4:  return (s1 + hi * 0.8 * std::sin(kTwoPi * 2.76 * ph) + hi * 0.5 * std::sin(kTwoPi * 5.40 * ph)
                                + hi * 0.28 * std::sin(kTwoPi * 8.93 * ph)) / (1.0 + hi * 1.6);
            default: return (s1 + hi * 0.5 * std::sin(kTwoPi * 2 * ph) + hi * 0.22 * std::sin(kTwoPi * 3 * ph)) / (1.0 + hi * 0.72);
        }
    }

    // Bar bookkeeping for a generated note: the step it lands on, its bar, who fired it.
    void noteFired(int ball, int pitch, int steps) {
        const double bar = std::floor(beat_ / beatsPerBar_);
        const double inBar = beat_ / beatsPerBar_ - bar;
        const int st = std::clamp((int)(inBar * steps), 0, steps - 1);
        stepPitch_[st] = pitch; stepBar_[st] = (int64_t)bar;
        if ((int64_t)bar != countBar_) { countBar_ = (int64_t)bar; barNotes_ = 0; }
        ++barNotes_;
        lastPitch_ = pitch; lastBall_ = ball; sinceNote_ = 0.0; ballSince_[ball] = 0.0;
        ballPitch_[ball] = pitch;
    }

    // Once per block: the snapshot the editor and the MCP reader see.
    void publish(int count, double baseInc, double spread, int motion, int steps, double rateMult, double cycleSamp) {
        auto put = [this](int i, double v) { tele_[i].store((float)v, std::memory_order_relaxed); };
        const double sr = sampleRate_;
        int active = 0, lv = 0;
        for (const auto& v : voices_) {
            if (!v.active) continue;
            ++active;
            if (lv < kMaxLevels) put(kLevelsAt + lv++, std::clamp(v.env * v.env * v.lvl, 0.0f, 1.0f));
        }
        for (; lv < kMaxLevels; ++lv) put(kLevelsAt + lv, 0.0);
        activeVoices_.store(active, std::memory_order_relaxed);

        const double bar = std::floor(beat_ / beatsPerBar_);
        if ((int64_t)bar != countBar_) { countBar_ = (int64_t)bar; barNotes_ = 0; }
        put(0, active); put(1, count); put(2, heldCount_); put(3, lastPitch_);
        put(4, beat_ / beatsPerBar_ - bar); put(5, steps);
        put(6, std::fabs(rateMult) > 1e-6 ? cycleSamp / std::fabs(rateMult) / sr : 0.0);
        put(7, lastBall_); put(8, sinceNote_ / sr); put(9, barNotes_); put(10, beatsPerBar_); put(11, playing_ ? 1 : 0);

        for (int b = 0; b < kMaxBalls; ++b) {
            const int at = kBallsAt + b * kBallStride;
            const double inc = baseInc * (1.0 + spread * (double)b * 0.37);
            const double ph = ballPhase_[b];
            // Walls sit at phase 0 (low end) and 0.5 (high end) for every swing curve.
            double dir = 0.0, toWall = -1.0;
            if (std::fabs(inc) > 1e-12) {
                const bool up = inc > 0;
                const bool rising = up ? ph < 0.5 : ph > 0.5;
                dir = rising ? 1.0 : -1.0;
                const double next = up ? (ph < 0.5 ? 0.5 : 1.0) : (ph > 0.5 ? 0.5 : 0.0);
                toWall = std::fabs(next - ph) / std::fabs(inc) / sr;
            }
            const int deg = b < count ? lastIdx_[b] : -1;
            int pitch = -1;
            if (deg >= 0 && deg < heldCount_) pitch = ballPitch_[b];
            put(at + 0, ph); put(at + 1, position(ph, motion)); put(at + 2, dir); put(at + 3, pitch);
            put(at + 4, toWall); put(at + 5, ballSince_[b] / sr);
            put(at + 6, rateMult * 0.5 * (1.0 + spread * (double)b * 0.37)); put(at + 7, deg);
        }
        for (int st = 0; st < kMaxSteps; ++st) {
            const int64_t age = (int64_t)bar - stepBar_[st];
            const bool live = st < steps && stepPitch_[st] >= 0 && age >= 0 && age <= 1;
            put(kStepsAt + st * 2, live ? stepPitch_[st] : -1);
            put(kStepsAt + st * 2 + 1, live ? (double)age : -1.0);
        }
    }

    void trigger(int pitch, int gateLen, float pan, float lvl, double detJit) {
        Voice* v = findFreeVoice();
        const double f = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        v->incA = f * detUp_ * detJit / sampleRate_;
        v->incB = f * detDn_ * detJit / sampleRate_;
        v->incM = f * 2.0 / sampleRate_;                // FM modulator ratio 2:1
        v->phA = 0.0; v->phB = 0.5; v->phM = 0.0; v->lpA = v->lpB = 0.0;
        v->env = 0.0f; v->gate = gateLen; v->stage = 0; v->active = true;
        v->lvl = std::clamp(lvl, 0.2f, 1.6f);
        const float t = (pan + 1.0f) * 0.25f * (float)kPi;               // equal-power pan
        v->gL = std::cos(t); v->gR = std::sin(t);
    }
    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.env < q->env) q = &v;   // steal the quietest
        return q;
    }

    std::atomic<float> pn_[kNumParams];
    double sampleRate_ = 44100.0, spb_ = 22050.0;   // spb default ≈ 120 BPM @ 44.1k
    Voice  voices_[kVoices];
    int    held_[kHeld] = {}; int heldCount_ = 0; int physCount_ = 0;
    std::atomic<int32_t> heldAtom_[kHeld]; std::atomic<int32_t> heldN_{0};
    double ballPhase_[kMaxBalls] = {}; int lastIdx_[kMaxBalls] = {};
    double gridAcc_ = 0.0;
    double detUp_ = 1.0, detDn_ = 1.0;
    uint32_t rng_ = 0x1234567u;

    // Musical clock (follows the transport while it rolls, free-runs when stopped) and
    // the telemetry state behind scopeRead.
    double beat_ = 0.0, beatsPerBar_ = 4.0;
    bool   playing_ = false;
    int    stepPitch_[kMaxSteps] = {}; int64_t stepBar_[kMaxSteps] = {};
    int64_t countBar_ = 0; int barNotes_ = 0;
    int    lastPitch_ = -1, lastBall_ = -1;
    int    ballPitch_[kMaxBalls] = {};
    double sinceNote_ = 1e9, ballSince_[kMaxBalls] = {};
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> tele_[kTele];
};

} // namespace nota
