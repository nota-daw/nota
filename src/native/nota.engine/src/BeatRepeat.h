// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Beat Repeat (device kind 11, mockup "Nota Beat Repeat") — a tempo-synced
// glitch / stutter effect in the spirit of classic beat-repeat units. Every Interval (bar-
// synced, shifted by Offset) it rolls Chance; when it fires it captures a Grid-length slice
// of the incoming audio (the capture pass plays through as it is) and then loops it for the
// rest of the Gate, each repeat transposed (Pitch, then Pitch Decay per repeat), faded
// (Decay), filtered (Filter On · Type LP / BP / HP · Freq · Width in octaves, optionally
// narrowing and following the pitch down with every repeat) and re-levelled (Volume). Three
// output modes — Mix (repeats over the dry), Insert (repeats replace the dry while active)
// and Gate (only the burst sounds). Variation lets the grid float a few steps per trigger,
// Triplet turns the grid into triplets, Repeat forces a repeat of the current slice for as
// long as it is on (Latch makes the card's button latch instead of momentary).
//
//   in ─▶ ring ──▶ [capture: dry] ─▶ [repeat: read slice · pitch · filter · decay · volume]
//          │                                                   │
//          └──────────────── dry ─▶ mode (Mix / Insert / Gate) ◀┘ ─▶ dry/wet Mix ─▶ out
//
// Needs the musical clock: the engine hands it the block's beat position via setTransport()
// and the time signature via setTransportInfo() right before process(). Header-only,
// allocation-free after setSampleRate, JUCE-free. Params normalized 0..1, APPEND ONLY
// (0..15 are the original layout; the appended ones default to the old sound).
// Telemetry (scopeRead): kTele live values, then the timeline window (kCells columns of
// input peak dB · kind 0 dry / 1 capture / 2 repeat · repeat gain) and the last interval's
// input peaks (kWave columns), both left → right in musical time.
// The interval phase is also published through gainReductionDb() (older viewers).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset (stop the repeat, clear the timeline and meters), 1 = trigger a
// repeat now (as if the interval fired, ignoring Chance).

#pragma once

#include "Device.h"
#include "TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class BeatRepeat : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum {
        Interval = 0,   // 1/8 · 1/4 · 1/2 · 1 bar · 2 bars · 4 bars (6 steps)
        Offset,         // trigger point inside the interval, 16ths of it
        Grid,           // slice: 1/4 · 1/8 · 1/16 · 1/32 · (legacy 1/8T · 1/16T) — 6 steps
        Variation,      // the grid floats ±round(6v) steps (×2 / ÷2) per trigger
        Chance,         // probability an interval fires
        Gate,           // burst length, 16ths of the interval
        Pitch,          // repeat transpose ±12 st (bipolar)
        PitchDecay,     // 0..6 st lower per repeat
        Volume,         // repeat level ±12 dB (bipolar)
        Decay,          // per-repeat fade (0..55 %)
        FilterOn,       // >=0.5 the repeats go through the filter
        FilterFreq,     // 50..18000 Hz (exp)
        FilterWidth,    // 0.5..3.5 octaves
        Mode,           // 0 Mix · 0.5 Insert · 1 Gate
        Mix,            // effect dry/wet
        Repeat,         // >=0.5 repeat the current slice now, for as long as it is on (was "Latch")
        // ---- appended (redesign) ----
        Latch,          // >=0.5 the card's Repeat button latches (UI mode, saved with the device)
        Triplet,        // >=0.5 the grid is in triplets (×2/3)
        FilterType,     // 0 LP · 0.5 BP (the old filter) · 1 HP
        FilterNarrow,   // >=0.5 each repeat narrows the band and follows the pitch down
        kNumParams
    };

    // Telemetry slots (scopeRead).
    enum {
        S_InDb = 0,     // input level, dBFS
        S_RepDb,        // repeat (wet) level, dBFS (−120 when not repeating)
        S_OutDb,        // output level, dBFS
        S_State,        // 0 idle (dry), 1 capture, 2 repeat
        S_Pass,         // pass in the burst (1 = the capture, 2.. = repeats)
        S_Passes,       // passes the burst will take (gate / slice; 0 = until released)
        S_Phase,        // interval phase 0..1 (offset applied)
        S_WinPhase,     // timeline window phase 0..1
        S_WinBeats,     // timeline window, beats (2 intervals)
        S_IntervalBeats,
        S_SliceBeats,   // the current / last burst's slice, beats (after variation)
        S_SliceMs,
        S_Bpm,
        S_Bar,          // 1-based bar
        S_BeatInBar,    // 1-based beat within the bar
        S_SampleRate,
        S_Cpu,          // share of real time spent in process()
        S_Latency,      // samples (0)
        S_Forced,       // 1 = Repeat holds the burst
        S_Gain,         // the current repeat's gain (decay × volume), linear
        S_PitchSt,      // the current repeat's pitch, semitones
        S_FilterHz,     // the current repeat's filter centre, Hz
        S_FilterOct,    // the current repeat's filter width, octaves
        S_Triggers,     // bursts since the last reset
        S_Playing,
        S_TsNum,
        S_BarBeats,     // beats per bar
        S_Cells,        // kCells
        S_WaveN,        // kWave
        S_Skipped,      // intervals Chance skipped since the last reset
        kTele = 32
    };
    static constexpr int kCells = 64;
    static constexpr int kWave = 128;
    static constexpr int kCellAt = kTele;                       // level dB · kind · gain
    static constexpr int kWaveAt = kTele + 3 * kCells;          // input peak (linear)
    static constexpr int kScope = kWaveAt + kWave;

    enum { A_Reset = 0, A_Trigger = 1 };

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
        p_[FilterFreq].store(0.5f); // 949 Hz
        p_[FilterWidth].store(0.5f);// 2 oct
        p_[Mode].store(0.5f);       // Insert (index 1 of 3)
        p_[Mix].store(1.0f);        // full wet
        p_[Repeat].store(0.0f);
        p_[Latch].store(0.0f);
        p_[Triplet].store(0.0f);
        p_[FilterType].store(0.5f); // BP — the original filter
        p_[FilterNarrow].store(0.0f);
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        size_t need = 1;
        while ((double)need < sr_ * 6.0) need <<= 1;            // ≥ 6 s: a half-note slice at 30 BPM
        for (auto& r : ring_) r.assign(need, 0.0f);
        mask_ = (int)need - 1;
        srA_.store((float)sr_, std::memory_order_relaxed);
        resetState();
    }

    void setTransport(double beatStart, double spb, bool playing) override {
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : spb_; playing_ = playing;
    }
    void setTransportInfo(const TransportInfo& ti) override {
        if (ti.tsNum > 0) tsNum_ = ti.tsNum;
        if (ti.tsDenom > 0) tsDen_ = ti.tsDenom;
    }

    // Interval phase in [0,1) — kept for older viewers of the timeline playhead.
    float gainReductionDb() const override { return phasePub_.load(std::memory_order_relaxed); }

    void deviceAction(int32_t id, int32_t /*iarg*/, float) override {
        if (id == A_Reset) resetReq_.store(true, std::memory_order_relaxed);
        else if (id == A_Trigger) triggerReq_.store(true, std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetReq_.exchange(false, std::memory_order_relaxed)) {
            repeating_ = false; forced_ = false; xf_ = 0.0f; triggers_ = 0; skipped_ = 0;
            for (auto& c : cells_) std::fill(c.begin(), c.end(), 0.0f);
            for (auto& v : cells_[0]) v = -120.0f;
            std::fill(wave_.begin(), wave_.end(), 0.0f);
            inEnv_ = repEnv_ = outEnv_ = 0.0f;
            svf_[0] = svf_[1] = {};
        }

        const double barBeats   = (double)tsNum_ * 4.0 / (double)tsDen_;
        const double intervalBeats = intervalBeatsFor(barBeats);
        const double offsetBeats   = offsetSteps() / 16.0 * intervalBeats;
        const double winBeats      = intervalBeats * 2.0;
        const float  chance   = std::clamp(get(Chance), 0.0f, 1.0f);
        const int    mode     = idx(Mode, 3);                             // 0 Mix, 1 Insert, 2 Gate
        const float  mixDW    = std::clamp(get(Mix), 0.0f, 1.0f);
        const bool   forceOn  = get(Repeat) >= 0.5f;
        const float  volLin   = std::pow(10.0f, ((float)get(Volume) - 0.5f) * 24.0f / 20.0f);
        const float  mRel     = coef(80.0f);
        const float  xfStep   = (float)(1.0 / std::max(1.0, 0.003 * sr_));   // 3 ms dry ↔ repeat
        const double fade     = std::max(8.0, 0.0015 * sr_);                  // slice-edge declick

        // Manual trigger (device action) — fires on the next sample like an interval would.
        bool trigNow = triggerReq_.exchange(false, std::memory_order_relaxed);

        for (int32_t i = 0; i < frames; ++i) {
            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            ring_[0][(size_t)wp_] = dryL; ring_[1][(size_t)wp_] = dryR;

            const double beat = beatStart_ + (double)i / spb_;
            const double rel  = (beat - offsetBeats) / intervalBeats;
            const double phase = rel - std::floor(rel);

            // Interval boundary → roll Chance → a new burst (unless Repeat holds one).
            if (playing_) {
                const double idxNow = std::floor(rel);
                if (prevIdx_ > -1e17 && idxNow > prevIdx_ && !forceOn) {
                    if (chance > 0.0f && uni() < chance) startBurst(intervalBeats, false);
                    else ++skipped_;
                }
                prevIdx_ = idxNow;
            } else {
                prevIdx_ = -1e18;
                if (repeating_ && !forced_) repeating_ = false;          // stopping the transport ends a synced burst
            }
            if (trigNow) { startBurst(intervalBeats, false); trigNow = false; }

            // Repeat: hold the current burst (or start one) for as long as it is on.
            if (forceOn) { if (!repeating_) startBurst(intervalBeats, true); forced_ = true; }
            else if (forced_) { forced_ = false; repeating_ = false; }

            float wetL = 0.0f, wetR = 0.0f;
            bool inRepeat = false;
            if (repeating_) {
                if (capturing_) {
                    if (++capElapsed_ >= (int64_t)sliceLen_) { capturing_ = false; beginPass(2); }
                } else {
                    inRepeat = true;
                    const double pos = captureBase_ + repeatPhase_;
                    float rl = readFrac(0, pos), rr = readFrac(1, pos);
                    if (filterOn_) { rl = svfRun(0, rl); rr = svfRun(1, rr); }
                    const double edge = std::min({ 1.0, repeatPhase_ / fade, (sliceLen_ - repeatPhase_) / fade });
                    const float g = (float)(passGain_ * std::max(0.0, edge)) * volLin;
                    wetL = rl * g; wetR = rr * g;
                    repeatPhase_ += passRate_;
                    if (repeatPhase_ >= sliceLen_) { repeatPhase_ -= sliceLen_; beginPass(pass_ + 1); }
                }
                if (!forced_ && ++burstElapsed_ >= (int64_t)gateSamples_) repeating_ = false;
            }

            // Dry ↔ repeat crossfade (Insert / Gate replace the dry while a repeat plays).
            const float xt = inRepeat ? 1.0f : 0.0f;
            xf_ = xt > xf_ ? std::min(xt, xf_ + xfStep) : std::max(xt, xf_ - xfStep);

            float outL, outR;
            if (mode == 0) { outL = dryL + wetL; outR = dryR + wetR; }                                   // Mix
            else if (mode == 1) { outL = dryL * (1.0f - xf_) + wetL; outR = dryR * (1.0f - xf_) + wetR; } // Insert
            else {                                                                                        // Gate
                const float g = (repeating_ && capturing_) ? 1.0f : 0.0f;
                gateDry_ = g > gateDry_ ? std::min(g, gateDry_ + xfStep) : std::max(g, gateDry_ - xfStep);
                outL = dryL * gateDry_ + wetL; outR = dryR * gateDry_ + wetR;
            }
            const float yl = dryL * (1.0f - mixDW) + outL * mixDW;
            const float yr = dryR * (1.0f - mixDW) + outR * mixDW;
            buf[i * 2] = yl; buf[i * 2 + 1] = yr;

            // Meters.
            const float ain = std::max(std::fabs(dryL), std::fabs(dryR));
            const float arep = std::max(std::fabs(wetL), std::fabs(wetR));
            const float aout = std::max(std::fabs(yl), std::fabs(yr));
            inEnv_ = ain > inEnv_ ? ain : ain + mRel * (inEnv_ - ain);
            repEnv_ = arep > repEnv_ ? arep : arep + mRel * (repEnv_ - arep);
            outEnv_ = aout > outEnv_ ? aout : aout + mRel * (outEnv_ - aout);

            // Timeline (2 intervals) and the interval's waveform, while the transport runs.
            if (playing_ && beat >= 0.0) {
                const double wp = std::fmod(beat, winBeats) / winBeats;
                const int c = std::clamp((int)(wp * kCells), 0, kCells - 1);
                if (c != cell_) { cell_ = c; cellPeak_ = 0.0f; }
                cellPeak_ = std::max(cellPeak_, ain);
                cells_[0][(size_t)c] = db(cellPeak_);
                const int kind = !repeating_ ? 0 : capturing_ ? 1 : 2;
                if (kind >= cells_[1][(size_t)c] || c != lastKindCell_) cells_[1][(size_t)c] = (float)kind;
                cells_[2][(size_t)c] = kind == 2 ? (float)passGain_ : kind == 1 ? 1.0f : 0.0f;
                lastKindCell_ = c;
                winPhase_ = (float)wp;

                const double ip = std::fmod(beat, intervalBeats) / intervalBeats;
                const int w = std::clamp((int)(ip * kWave), 0, kWave - 1);
                if (w != waveCol_) { waveCol_ = w; wave_[(size_t)w] = 0.0f; }
                wave_[(size_t)w] = std::max(wave_[(size_t)w], ain);
                bar_ = (float)(std::floor(beat / barBeats) + 1.0);
                beatInBar_ = (float)(std::fmod(beat, barBeats) + 1.0);
            }
            wp_ = (wp_ + 1) & mask_;
            phasePub_.store((float)phase, std::memory_order_relaxed);
        }

        // Publish.
        inDb_.store(db(inEnv_), std::memory_order_relaxed);
        repDb_.store(db(repEnv_), std::memory_order_relaxed);
        outDb_.store(db(outEnv_), std::memory_order_relaxed);
        stateA_.store(!repeating_ ? 0.0f : capturing_ ? 1.0f : 2.0f, std::memory_order_relaxed);
        passA_.store(repeating_ ? (float)pass_ : 0.0f, std::memory_order_relaxed);
        passesA_.store(forced_ ? 0.0f : (float)passes_, std::memory_order_relaxed);
        winPhaseA_.store(winPhase_, std::memory_order_relaxed);
        winBeatsA_.store((float)winBeats, std::memory_order_relaxed);
        intervalA_.store((float)intervalBeats, std::memory_order_relaxed);
        sliceBeatsA_.store((float)sliceBeats_, std::memory_order_relaxed);
        bpmA_.store((float)(60.0 * sr_ / std::max(1.0, spb_)), std::memory_order_relaxed);
        barA_.store(bar_, std::memory_order_relaxed);
        beatA_.store(beatInBar_, std::memory_order_relaxed);
        forcedA_.store(forced_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        gainA_.store(repeating_ && !capturing_ ? (float)passGain_ * volLin : 0.0f, std::memory_order_relaxed);
        pitchA_.store((float)passPitch_, std::memory_order_relaxed);
        fHzA_.store((float)passHz_, std::memory_order_relaxed);
        fOctA_.store((float)passOct_, std::memory_order_relaxed);
        trigA_.store((float)triggers_, std::memory_order_relaxed);
        skipA_.store((float)skipped_, std::memory_order_relaxed);
        playA_.store(playing_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        tsA_.store((float)tsNum_, std::memory_order_relaxed);
        barBeatsA_.store((float)barBeats, std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    // Telemetry block, then the timeline cells and the interval waveform. Writes as much of
    // that layout as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        t[S_InDb] = inDb_.load(std::memory_order_relaxed);
        t[S_RepDb] = repDb_.load(std::memory_order_relaxed);
        t[S_OutDb] = outDb_.load(std::memory_order_relaxed);
        t[S_State] = stateA_.load(std::memory_order_relaxed);
        t[S_Pass] = passA_.load(std::memory_order_relaxed);
        t[S_Passes] = passesA_.load(std::memory_order_relaxed);
        t[S_Phase] = phasePub_.load(std::memory_order_relaxed);
        t[S_WinPhase] = winPhaseA_.load(std::memory_order_relaxed);
        t[S_WinBeats] = winBeatsA_.load(std::memory_order_relaxed);
        t[S_IntervalBeats] = intervalA_.load(std::memory_order_relaxed);
        t[S_SliceBeats] = sliceBeatsA_.load(std::memory_order_relaxed);
        t[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
        t[S_SliceMs] = t[S_SliceBeats] * 60000.0f / std::max(1.0f, t[S_Bpm]);
        t[S_Bar] = barA_.load(std::memory_order_relaxed);
        t[S_BeatInBar] = beatA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency] = 0.0f;
        t[S_Forced] = forcedA_.load(std::memory_order_relaxed);
        t[S_Gain] = gainA_.load(std::memory_order_relaxed);
        t[S_PitchSt] = pitchA_.load(std::memory_order_relaxed);
        t[S_FilterHz] = fHzA_.load(std::memory_order_relaxed);
        t[S_FilterOct] = fOctA_.load(std::memory_order_relaxed);
        t[S_Triggers] = trigA_.load(std::memory_order_relaxed);
        t[S_Playing] = playA_.load(std::memory_order_relaxed);
        t[S_TsNum] = tsA_.load(std::memory_order_relaxed);
        t[S_BarBeats] = barBeatsA_.load(std::memory_order_relaxed);
        t[S_Cells] = (float)kCells;
        t[S_WaveN] = (float)kWave;
        t[S_Skipped] = skipA_.load(std::memory_order_relaxed);

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int s = 0; s < 3; ++s)
            for (int k = 0; k < kCells && n < maxSamples; ++k) out[n++] = cells_[(size_t)s][(size_t)k];
        for (int k = 0; k < kWave && n < maxSamples; ++k) out[n++] = wave_[(size_t)k];
        return n;
    }

    const char* displayName() const override { return "Nota Beat Repeat"; }
    int32_t     builtinKind() const override { return 11; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = { "Interval", "Offset", "Grid", "Variation", "Chance", "Gate", "Pitch",
                                              "Pitch Decay", "Volume", "Decay", "Filter On", "Filter Freq", "Filter Width",
                                              "Mode", "Mix", "Repeat", "Latch", "Triplet", "Filter Type", "Filter Narrow" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        char b[900];
        static const char* modes[3] = { "Mix", "Insert", "Gate" };
        static const char* ivl[6] = { "1/8", "1/4", "1/2", "1 bar", "2 bars", "4 bars" };
        static const char* types[3] = { "LP", "BP", "HP" };
        if (id == 0) {
            std::string s = modes[idx(Mode, 3)];
            std::snprintf(b, sizeof b, " - every %s - grid %s - offset %d/16 - gate %d/16 - chance %.0f %%",
                          ivl[idx(Interval, 6)], gridName().c_str(), offsetSteps(), gateSteps(), get(Chance) * 100.0f);
            s += b;
            if (varSteps() > 0) { std::snprintf(b, sizeof b, " - variation +/-%d steps", varSteps()); s += b; }
            std::snprintf(b, sizeof b, " - pitch %+.0f st", ((double)get(Pitch) - 0.5) * 24.0); s += b;
            if (get(PitchDecay) > 0.001f) { std::snprintf(b, sizeof b, " (-%.1f st per repeat)", get(PitchDecay) * 6.0f); s += b; }
            std::snprintf(b, sizeof b, " - decay %.0f %% - volume %+.1f dB", get(Decay) * 100.0f, (get(Volume) - 0.5f) * 24.0f); s += b;
            if (get(FilterOn) >= 0.5f) {
                std::snprintf(b, sizeof b, " - %s filter %s, %.1f oct%s", types[idx(FilterType, 3)], hzText(filterHz()).c_str(),
                              filterOct(), get(FilterNarrow) >= 0.5f ? ", narrowing" : "");
                s += b;
            }
            std::snprintf(b, sizeof b, " - mix %.0f %%", get(Mix) * 100.0f); s += b;
            if (get(Repeat) >= 0.5f) s += " - REPEAT held";
            if (get(Latch) >= 0.5f) s += " - latch";
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            const int st = std::clamp((int)std::lround(sc[S_State]), 0, 2);
            static const char* states[3] = { "idle", "capturing", "repeating" };
            std::string passes = sc[S_Passes] > 0.5f ? std::to_string((int)sc[S_Passes]) : std::string("held");
            std::snprintf(b, sizeof b,
                          "%s - pass %d of %s - slice %.0f ms (%.3g beats) - repeat gain %.2f - pitch %+.1f st - filter %s %.1f oct - "
                          "in %.1f dB - repeat %.1f dB - out %.1f dB - bar %.0f beat %.2f - %.0f bursts, %.0f skipped since reset - %s",
                          states[st], (int)sc[S_Pass], passes.c_str(), sc[S_SliceMs], sc[S_SliceBeats], sc[S_Gain], sc[S_PitchSt],
                          hzText(sc[S_FilterHz]).c_str(), sc[S_FilterOct], sc[S_InDb], sc[S_RepDb], sc[S_OutDb], sc[S_Bar],
                          sc[S_BeatInBar], sc[S_Triggers], sc[S_Skipped], sc[S_Playing] > 0.5f ? "playing" : "stopped");
            return b;
        }
        if (id == 2) {
            return "All params 0..1. Interval: round(5v) -> 1/8, 1/4, 1/2 note, 1, 2, 4 bars (0.6 = 1 bar; bars follow the time "
                   "signature). Offset: round(16v)/16 of the interval (where it fires). Grid: round(5v) -> 1/4, 1/8, 1/16, 1/32 "
                   "(0.4 = 1/16; 0.8 / 1.0 = legacy 1/8T / 1/16T), Triplet >= 0.5 makes it triplets. Variation: the grid moves "
                   "up to round(6v) doubling / halving steps per trigger. Chance: probability an interval fires. Gate: "
                   "round(16v)/16 of the interval (the burst's length; at least one slice). Pitch: (v-0.5)*24 st on the repeats. "
                   "Pitch Decay: 6v st lower per repeat. Volume: (v-0.5)*24 dB on the repeats. Decay: each repeat x(1-0.55v). "
                   "Filter On: the repeats go through the filter. Filter Type: 0 LP, 0.5 BP, 1 HP. Filter Freq: 50*360^v Hz "
                   "(0.5 = 949 Hz). Filter Width: 0.5+3v octaves (0.5 = 2 oct). Filter Narrow: each repeat narrows the band "
                   "x0.75 and moves it with the pitch. Mode: 0 Mix (repeats over the dry), 0.5 Insert (repeats replace the "
                   "dry), 1 Gate (only the burst sounds). Mix: dry/wet. Repeat: >= 0.5 repeats the current slice until "
                   "released. Latch: the card's Repeat button latches. Toggles: >= 0.5 = on.";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kGridStraight[4] = { 1.0, 0.5, 0.25, 0.125 };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int   idx(int p, int n) const { return std::clamp((int)std::lround(get(p) * (n - 1)), 0, n - 1); }
    int   offsetSteps() const { return std::clamp((int)std::lround(get(Offset) * 16.0f), 0, 16) % 16; }
    int   gateSteps() const { return std::clamp((int)std::lround(get(Gate) * 16.0f), 0, 16); }
    int   varSteps() const { return std::clamp((int)std::lround(get(Variation) * 6.0f), 0, 6); }
    double intervalBeatsFor(double barBeats) const {
        const double t[6] = { 0.5, 1.0, 2.0, barBeats, 2.0 * barBeats, 4.0 * barBeats };
        return t[idx(Interval, 6)];
    }
    // Grid 0..3 straight, 4/5 = the legacy 1/8T, 1/16T; Triplet turns any straight grid to triplets.
    double gridBeats() const {
        const int g = idx(Grid, 6);
        if (g >= 4) return kGridStraight[g - 3] * 2.0 / 3.0;
        return kGridStraight[g] * (get(Triplet) >= 0.5f ? 2.0 / 3.0 : 1.0);
    }
    std::string gridName() const {
        static const char* s[4] = { "1/4", "1/8", "1/16", "1/32" };
        const int g = idx(Grid, 6);
        const bool trip = g >= 4 || get(Triplet) >= 0.5f;
        return std::string(s[g >= 4 ? g - 3 : g]) + (trip ? "T" : "");
    }
    double filterHz() const { return expMap(get(FilterFreq), 50.0, 18000.0); }
    double filterOct() const { return 0.5 + (double)get(FilterWidth) * 3.0; }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    float coef(float ms) const { return (float)std::exp(-1.0 / (std::max(0.01f, ms) * 0.001 * sr_)); }
    float uni() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f; }
    static std::string hzText(double hz) {
        char b[32];
        if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.1f kHz", hz / 1000.0); else std::snprintf(b, sizeof b, "%.0f Hz", hz);
        return b;
    }

    // A new burst: the capture pass (the slice plays through as it is) and then the repeats.
    void startBurst(double intervalBeats, bool forced) {
        double gb = gridBeats();
        if (const int r = varSteps(); r > 0) {
            const int k = std::clamp((int)std::floor(uni() * (2 * r + 1)), 0, 2 * r) - r;
            gb = std::clamp(gb * std::pow(2.0, (double)-k), 1.0 / 16.0, 2.0);
        }
        sliceBeats_ = gb;
        sliceLen_ = std::max(16.0, gb * spb_);
        sliceLen_ = std::min(sliceLen_, (double)mask_ - 4.0);
        const double gate = (double)gateSteps() / 16.0 * intervalBeats * spb_;
        gateSamples_ = std::max(sliceLen_, gate);
        passes_ = std::max(1, (int)std::lround(gateSamples_ / sliceLen_));
        captureBase_ = wp_;
        capElapsed_ = 0; burstElapsed_ = 0; repeatPhase_ = 0.0;
        capturing_ = true; repeating_ = true; forced_ = forced;
        pass_ = 1; passGain_ = 1.0; passPitch_ = ((double)get(Pitch) - 0.5) * 24.0; passRate_ = 1.0;
        passHz_ = filterHz(); passOct_ = filterOct();
        ++triggers_;
    }

    // Pass p >= 2 is repeat p−1: its gain, pitch and filter.
    void beginPass(int p) {
        pass_ = p;
        const int r = std::max(0, p - 2);                                  // repeats before this one
        const double base = ((double)get(Pitch) - 0.5) * 24.0;
        passPitch_ = base - (double)get(PitchDecay) * 6.0 * r;
        passRate_ = std::pow(2.0, passPitch_ / 12.0);
        passGain_ = std::pow(1.0 - (double)get(Decay) * 0.55, (double)(p - 1));
        filterOn_ = get(FilterOn) >= 0.5f;
        const bool narrow = get(FilterNarrow) >= 0.5f;
        passOct_ = filterOct() * (narrow ? std::pow(0.75, (double)r) : 1.0);
        passOct_ = std::max(0.2, passOct_);
        passHz_ = filterHz() * (narrow ? std::pow(2.0, (passPitch_ - base) / 12.0) : 1.0);
        passHz_ = std::clamp(passHz_, 20.0, 20000.0);
        // TPT SVF coefficients for this pass (Q from the bandwidth in octaves).
        const double n2 = std::pow(2.0, passOct_);
        double q = std::sqrt(n2) / (n2 - 1.0);
        const int type = idx(FilterType, 3);
        if (type != 1) q = std::max(0.5, q);
        fG_ = std::tan(kPi * std::min(passHz_, sr_ * 0.45) / sr_);
        fK_ = 1.0 / q;
        fA1_ = 1.0 / (1.0 + fG_ * (fG_ + fK_));
        fType_ = type;
    }

    inline float readFrac(int c, double pos) const {
        double rp = std::fmod(pos, (double)(mask_ + 1));
        if (rp < 0.0) rp += (double)(mask_ + 1);
        const int i0 = (int)rp & mask_;
        const int i1 = (i0 + 1) & mask_;
        const double fr = rp - std::floor(rp);
        return (float)(ring_[c][(size_t)i0] * (1.0 - fr) + ring_[c][(size_t)i1] * fr);
    }
    // TPT state-variable filter: LP / BP (peak ≈ unity) / HP.
    struct SvfSt { double ic1 = 0.0, ic2 = 0.0; };
    inline float svfRun(int c, float in) {
        SvfSt& s = svf_[c];
        const double v3 = in - s.ic2;
        const double v1 = fA1_ * s.ic1 + fA1_ * fG_ * v3;
        const double v2 = s.ic2 + fG_ * v1;
        s.ic1 = 2.0 * v1 - s.ic1;
        s.ic2 = 2.0 * v2 - s.ic2;
        if (fType_ == 0) return (float)v2;
        if (fType_ == 2) return (float)(in - fK_ * v1 - v2);
        return (float)(fK_ * v1);
    }

    void resetState() {
        for (auto& r : ring_) std::fill(r.begin(), r.end(), 0.0f);
        wp_ = 0; repeating_ = capturing_ = forced_ = false; prevIdx_ = -1e18; rng_ = 0x2F6E10A3u;
        svf_[0] = svf_[1] = {};
        xf_ = gateDry_ = 0.0f;
        inEnv_ = repEnv_ = outEnv_ = 0.0f;
        for (auto& c : cells_) std::fill(c.begin(), c.end(), 0.0f);
        for (auto& v : cells_[0]) v = -120.0f;
        std::fill(wave_.begin(), wave_.end(), 0.0f);
        cell_ = waveCol_ = lastKindCell_ = -1; cellPeak_ = 0.0f;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    std::vector<float> ring_[2];
    int mask_ = 0, wp_ = 0;
    SvfSt svf_[2];
    double fG_ = 0.1, fK_ = 1.0, fA1_ = 1.0; int fType_ = 1; bool filterOn_ = false;
    // Transport (set each block).
    double beatStart_ = 0.0, spb_ = 22050.0; bool playing_ = false;
    int tsNum_ = 4, tsDen_ = 4;
    // Burst state.
    bool repeating_ = false, capturing_ = false, forced_ = false;
    double prevIdx_ = -1e18;
    double captureBase_ = 0.0, repeatPhase_ = 0.0, sliceLen_ = 0.0, gateSamples_ = 0.0, sliceBeats_ = 0.25;
    int64_t capElapsed_ = 0, burstElapsed_ = 0;
    int pass_ = 0, passes_ = 0;
    double passGain_ = 1.0, passPitch_ = 0.0, passRate_ = 1.0, passHz_ = 949.0, passOct_ = 2.0;
    float xf_ = 0.0f, gateDry_ = 0.0f;
    uint32_t rng_ = 0x2F6E10A3u;
    int64_t triggers_ = 0, skipped_ = 0;
    // Meters / telemetry (audio writes, UI reads).
    float inEnv_ = 0.0f, repEnv_ = 0.0f, outEnv_ = 0.0f;
    std::vector<float> cells_[3] = { std::vector<float>(kCells, -120.0f), std::vector<float>(kCells, 0.0f), std::vector<float>(kCells, 0.0f) };
    std::vector<float> wave_ = std::vector<float>(kWave, 0.0f);
    int cell_ = -1, waveCol_ = -1, lastKindCell_ = -1; float cellPeak_ = 0.0f, winPhase_ = 0.0f, bar_ = 1.0f, beatInBar_ = 1.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> resetReq_{false}, triggerReq_{false};
    std::atomic<float> phasePub_{0.0f};
    std::atomic<float> inDb_{-120.0f}, repDb_{-120.0f}, outDb_{-120.0f}, stateA_{0.0f}, passA_{0.0f}, passesA_{0.0f},
        winPhaseA_{0.0f}, winBeatsA_{8.0f}, intervalA_{4.0f}, sliceBeatsA_{0.25f}, bpmA_{120.0f}, barA_{1.0f}, beatA_{1.0f},
        srA_{44100.0f}, cpuA_{0.0f}, forcedA_{0.0f}, gainA_{0.0f}, pitchA_{0.0f}, fHzA_{949.0f}, fOctA_{2.0f},
        trigA_{0.0f}, skipA_{0.0f}, playA_{0.0f}, tsA_{4.0f}, barBeatsA_{4.0f};
};

} // namespace nota
