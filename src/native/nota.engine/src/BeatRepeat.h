// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in Nota Beat Repeat (device kind 11) — a tempo-synced glitch/stutter effect in
// the spirit of classic beat-repeat units. Every Interval (bar-synced, + Offset) it rolls
// Chance to engage; when it does it captures a Grid-length slice of the incoming audio
// and loops it for the Gate duration, optionally transposing (Pitch + Pitch Decay),
// fading (Decay), band-filtering and re-leveling (Volume) the repeats. Three output
// modes — Mix (repeats over the dry), Insert (repeats replace the dry while active) and
// Gate (only the repeats sound). Grid can be randomized per trigger by Variation.
//
// Needs the musical clock: the engine hands it the block's beat position via
// setTransport() (see Device::setTransport) right before process(). The interval phase
// + an "active" flag are published through gainReductionDb() so the editor's bar viz
// scrubs live. Header-only, allocation-free, JUCE-free. Params normalized 0..1.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class BeatRepeat : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    // v2 appends Mix (dry/wet) and Latch (force a repeat now, held or latched).
    enum { Interval = 0, Offset, Grid, Variation, Chance, Gate, Pitch, PitchDecay,
           Volume, Decay, FilterOn, FilterFreq, FilterWidth, Mode, Mix, Latch, kNumParams };

    BeatRepeat() {
        p_[Interval].store(0.6f);   // 1 Bar (index 3 of 6)
        p_[Offset].store(0.0f);
        p_[Grid].store(0.4f);       // 1/16 (index 2 of 6)
        p_[Variation].store(0.0f);
        p_[Chance].store(1.0f);
        p_[Gate].store(0.5f);
        p_[Pitch].store(0.5f);      // 0 st
        p_[PitchDecay].store(0.0f);
        p_[Volume].store(0.5f);     // 0 dB
        p_[Decay].store(0.0f);
        p_[FilterOn].store(0.0f);
        p_[FilterFreq].store(0.5f);
        p_[FilterWidth].store(0.5f);
        p_[Mode].store(0.5f);       // Insert (index 1 of 3)
        p_[Mix].store(1.0f);        // full wet
        p_[Latch].store(0.0f);      // manual repeat off
    }

    static constexpr int kSlots = 64;   // timeline resolution for the UI

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (int c = 0; c < 2; ++c) { for (int i = 0; i < kBuf; ++i) ring_[c][i] = 0.0f; svfIc1_[c] = svfIc2_[c] = 0.0; }
        for (int i = 0; i < kSlots; ++i) slots_[i] = 0.0f;
        wp_ = 0; repeating_ = false; prevIdx_ = -1e18; rng_ = 0x2F6E10A3u;
    }

    void setTransport(double beatStart, double spb, bool playing) override {
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : 1.0; playing_ = playing;
    }

    // Interval phase in [0,1) — the timeline playhead.
    float gainReductionDb() const override { return phasePub_.load(std::memory_order_relaxed); }

    // Timeline: per-slot repeat-gain envelope over one interval window (0 = dry/well,
    // 1 = captured slice, 0<g<1 = a repeat generation fading with Decay).
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const int n = std::min(maxSamples, kSlots);
        for (int k = 0; k < n; ++k) out[k] = slots_[k];
        return n;
    }

    void process(float* buf, int32_t frames) override {
        const double intervalBeats = kInterval[idx(Interval, 6)];
        const double gridBeats     = kGrid[idx(Grid, 6)];
        const double offsetBeats   = (double)get(Offset) * intervalBeats;
        const float  chance   = std::clamp(get(Chance), 0.0f, 1.0f);
        const float  gateFrac = std::clamp(get(Gate), 0.0f, 1.0f);
        const float  variation= std::clamp(get(Variation), 0.0f, 1.0f);
        const double basePitch= ((double)get(Pitch) - 0.5) * 24.0;      // ±12 st
        const double pDecay   = (double)get(PitchDecay) * 6.0;          // st dropped per cycle
        const float  volLin   = std::pow(10.0f, ((float)get(Volume) - 0.5f) * 24.0f / 20.0f);
        const double decayMul = 1.0 - (double)get(Decay) * 0.55;        // per-cycle amplitude
        const bool   filterOn = get(FilterOn) >= 0.5f;
        const int    mode     = idx(Mode, 3);                            // 0 Mix, 1 Insert, 2 Gate
        const float  mixDW    = std::clamp(get(Mix), 0.0f, 1.0f);        // effect dry/wet
        const bool   latch    = get(Latch) >= 0.5f;                       // force repeats now
        // Band filter coefficients (control-rate).
        const double fc = expMap(get(FilterFreq), 50.0, 18000.0);
        const double q  = 0.5 + (double)get(FilterWidth) * 8.0;
        const double g  = std::tan(kPi * std::min(fc, sr_ * 0.49) / sr_);
        const double k  = 1.0 / q;
        const double a1 = 1.0 / (1.0 + g * (g + k));

        for (int32_t i = 0; i < frames; ++i) {
            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            ring_[0][wp_] = dryL; ring_[1][wp_] = dryR;

            const double beat = beatStart_ + (double)i / spb_;
            const double rel  = (beat - offsetBeats) / intervalBeats;
            double phase = rel - std::floor(rel);                        // 0..1 within interval

            // Interval boundary → roll Chance → (re)trigger a burst.
            if (playing_) {
                const double idxNow = std::floor(rel);
                if (prevIdx_ > -1e17 && idxNow > prevIdx_ && chance > 0.0f) {
                    if (whiteUni() < chance) startBurst(intervalBeats, gridBeats, gateFrac, variation, basePitch);
                }
                prevIdx_ = idxNow;
            } else if (!latch) { repeating_ = false; prevIdx_ = -1e18; }

            // Manual latch / Repeat-hold: force a burst now and keep re-triggering.
            if (latch && !repeating_) startBurst(intervalBeats, gridBeats, gateFrac, variation, basePitch);

            float wetL = dryL, wetR = dryR;
            if (repeating_) {
                const double pos = captureBase_ + repeatPhase_;
                wetL = readFrac(0, pos); wetR = readFrac(1, pos);
                if (filterOn) { wetL = svf(0, wetL, g, a1, k); wetR = svf(1, wetR, g, a1, k); }
                wetL *= (float)cycleGain_ * volLin; wetR *= (float)cycleGain_ * volLin;

                const double rate = std::pow(2.0, cyclePitch_ / 12.0);
                repeatPhase_ += rate;
                if (repeatPhase_ >= sliceLen_) {                         // slice wrapped → next repeat cycle
                    repeatPhase_ -= sliceLen_;
                    ++repeatCount_;
                    cycleGain_ *= decayMul;
                    cyclePitch_ -= pDecay;
                }
                if (++burstElapsed_ >= gateSamples_) repeating_ = false;
            }

            float outL, outR;
            if (mode == 2)      { outL = repeating_ ? wetL : 0.0f;      outR = repeating_ ? wetR : 0.0f; }        // Gate
            else if (mode == 1) { outL = repeating_ ? wetL : dryL;     outR = repeating_ ? wetR : dryR; }         // Insert
            else                { const bool add = repeating_ && repeatCount_ >= 1;                                // Mix
                                  outL = dryL + (add ? wetL : 0.0f);   outR = dryR + (add ? wetR : 0.0f); }
            buf[i * 2]     = dryL * (1.0f - mixDW) + outL * mixDW;
            buf[i * 2 + 1] = dryR * (1.0f - mixDW) + outR * mixDW;

            // Timeline: record the repeat-gain envelope at the current playhead slot.
            if (playing_) {
                int slot = (int)(phase * kSlots);
                if (slot < 0) slot = 0; else if (slot >= kSlots) slot = kSlots - 1;
                slots_[slot] = repeating_ ? (float)cycleGain_ : 0.0f;
            }
            wp_ = (wp_ + 1) & (kBuf - 1);
            phasePub_.store((float)phase, std::memory_order_relaxed);
        }
    }

    const char* displayName() const override { return "Nota Beat Repeat"; }
    int32_t     builtinKind() const override { return 11; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Interval: return "Interval"; case Offset: return "Offset"; case Grid: return "Grid";
            case Variation: return "Variation"; case Chance: return "Chance"; case Gate: return "Gate";
            case Pitch: return "Pitch"; case PitchDecay: return "Pitch Decay"; case Volume: return "Volume";
            case Decay: return "Decay"; case FilterOn: return "Filter On"; case FilterFreq: return "Filter Freq";
            case FilterWidth: return "Filter Width"; case Mode: return "Mode"; case Mix: return "Mix";
            case Latch: return "Latch"; default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kBuf = 1 << 17;   // ~3 s at 44.1k — holds a grid slice at slow tempi
    // Interval / Grid tables in beats (assumes 4/4; 1 Bar = 4 beats).
    static constexpr double kInterval[6] = { 0.5, 1.0, 2.0, 4.0, 8.0, 16.0 };
    static constexpr double kGrid[6]     = { 1.0, 0.5, 0.25, 0.125, 1.0 / 3.0, 1.0 / 6.0 };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int   idx(int p, int n) const { return std::clamp((int)std::lround(get(p) * (n - 1)), 0, n - 1); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    float whiteUni() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f; }

    void startBurst(double intervalBeats, double gridBeats, float gateFrac, float variation, double basePitch) {
        double gb = gridBeats;
        if (variation > 0.0f && whiteUni() < variation) {               // randomize the grid a step
            const double mul = whiteUni() < 0.5f ? 0.5 : 2.0;
            gb = std::clamp(gb * mul, kGrid[3], kGrid[0]);
        }
        sliceLen_    = std::max(4.0, gb * spb_);
        gateSamples_ = std::max(sliceLen_, (double)gateFrac * intervalBeats * spb_);
        captureBase_ = wp_;
        repeatPhase_ = 0.0; burstElapsed_ = 0; repeatCount_ = 0;
        cycleGain_ = 1.0; cyclePitch_ = basePitch;
        repeating_ = true;
    }

    inline float readFrac(int c, double pos) const {
        double rp = pos;
        while (rp < 0.0) rp += kBuf;
        const int i0 = (int)rp & (kBuf - 1);
        const int i1 = (i0 + 1) & (kBuf - 1);
        const double fr = rp - std::floor(rp);
        return (float)(ring_[c][i0] * (1.0 - fr) + ring_[c][i1] * fr);
    }
    // TPT state-variable band-pass (one per channel).
    inline float svf(int c, float in, double g, double a1, double k) {
        const double v3 = in - svfIc2_[c];
        const double v1 = a1 * svfIc1_[c] + a1 * g * v3;
        const double v2 = svfIc2_[c] + g * v1;
        svfIc1_[c] = 2.0 * v1 - svfIc1_[c];
        svfIc2_[c] = 2.0 * v2 - svfIc2_[c];
        return (float)(k * v1);   // band-pass, scaled so peak ≈ unity
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float ring_[2][kBuf] = {};
    float slots_[kSlots] = {};   // published timeline envelope (audio writes, UI reads)
    double svfIc1_[2] = {}, svfIc2_[2] = {};
    int wp_ = 0;
    // Transport (set each block).
    double beatStart_ = 0.0, spb_ = 1.0; bool playing_ = false;
    // Burst state.
    bool repeating_ = false;
    double prevIdx_ = -1e18;
    double captureBase_ = 0.0, repeatPhase_ = 0.0, sliceLen_ = 0.0, gateSamples_ = 0.0;
    double cycleGain_ = 1.0, cyclePitch_ = 0.0;
    int burstElapsed_ = 0, repeatCount_ = 0;
    uint32_t rng_ = 0x2F6E10A3u;
    std::atomic<float> phasePub_{0.0f};
};

} // namespace nota
