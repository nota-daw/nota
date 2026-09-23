// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Orbit (device kind 9, formerly Auto Pan) — auto-pan and tremolo on one LFO:
// the LFO drives each channel's amplitude, and a Phase offset between the left and right LFOs
// sweeps continuously from tremolo (0°, both channels dip together) to auto-pan (180°, one
// channel loud while the other is quiet). Waveform Sine / Triangle / Saw / Square / S&H;
// Shape sharpens the smooth waves toward a square and, on S&H, is the glide between steps;
// Amount sets the depth, Mix blends dry / wet.
//
//   rate: Free (Rate, 0.01..40 Hz) or Sync (Division, 4/1 … 1/32 incl. dotted and triplets).
//   In Sync the LFO locks to the song position while the transport plays and restarts on the
//   bar (a cycle longer than a bar restarts on the span of bars that holds it); stopped, it
//   free-runs at the synced rate.
//
// S&H steps come from a hash of the cycle index, so the right channel plays the same random
// line as the left, Phase later — at 0° the steps move both channels together (a random
// tremolo), at 180° they pan. The channel gains are slewed over ~1 ms so a Square or a
// transport jump doesn't click.
//
// All params are normalized 0..1 and APPEND ONLY: 0..5 are the original layout; Sync and
// Division are appended and default to Free, so older projects open unchanged. Persistence /
// automation / clone flow generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, the gains now, the pan, the LFO's place in
// its two-cycle window, the S&H steps around it, the rate, the tempo).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = reset the meters.
// The live stereo position (0 = left … 1 = right) also stays on gainReductionDb().
// Header-only, allocation-free, JUCE-free.

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

namespace nota {

class AutoPan : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Amount, Waveform, Shape, Phase, Mix, Sync, Division, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,   // input peak (300 ms fall), dBFS
        S_OutPeakL,     // output peak, left (300 ms fall), dBFS
        S_OutPeakR,     // output peak, right, dBFS
        S_GainL,        // the left gain now (after Mix), linear 0..1
        S_GainR,        // the right gain now, linear
        S_Pan,          // the stereo position now, (gR − gL) / (gR + gL): −1 left … +1 right
        S_WinPhase,     // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,       // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,          // the tempo the Sync rate follows
        S_Playing,      // 1 while the transport plays
        S_Locked,       // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_Sh0,          // the S&H steps of the cycles window−1, window, window+1, window+2 (−1..1)
        S_Sh1,
        S_Sh2,
        S_Sh3,
        S_PanMin,       // the pan's swing over the window, −1..1
        S_PanMax,
        S_DivBeats,     // the Sync division, beats per cycle
        S_BarBeats,     // beats per bar (the restart span's unit)
        S_Cpu,          // share of real time spent in process()
        S_Signal,       // 1 while signal comes in (above −70 dBFS)
        S_Floor,        // the lowest gain (1 − Amount·Mix), linear
        S_Cycles,       // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        kTele = 32
    };

    static constexpr int kDivs = 16;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = { 16.0, 8.0, 4.0, 3.0, 2.0, 4.0 / 3.0, 1.5, 1.0, 2.0 / 3.0, 0.75,
                                                 0.5, 1.0 / 3.0, 0.375, 0.25, 1.0 / 6.0, 0.125 };
    static constexpr const char* kDivNames[kDivs] = { "4/1", "2/1", "1/1", "1/2D", "1/2", "1/2T", "1/4D", "1/4", "1/4T",
                                                      "1/8D", "1/8", "1/8T", "1/16D", "1/16", "1/16T", "1/32" };
    static constexpr int kDefaultDiv = 10;   // 1/8

    AutoPan() {
        p_[Rate].store(0.60f);      // ~1.45 Hz
        p_[Amount].store(0.70f);
        p_[Waveform].store(0.0f);   // Sine
        p_[Shape].store(0.0f);      // smooth
        p_[Phase].store(0.5f);      // 180° → classic auto-pan
        p_[Mix].store(1.0f);
        p_[Sync].store(0.0f);       // Free (Hz)
        p_[Division].store((float)kDefaultDiv / (kDivs - 1));
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        cycle_ = 0; frac_ = 0.0;
        gLs_ = gRs_ = -1.0f;   // snap to the first target
        resetMeters();
    }

    // Musical clock for Sync (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override {
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : 0.0; playing_ = playing;
    }
    void setTransportInfo(const TransportInfo& ti) override {
        if (ti.tsNum > 0 && ti.tsDenom > 0) barBeats_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 0.5, 64.0);
    }

    // Live stereo position (0 = hard left, 0.5 = centre, 1 = hard right) — the old pan dot's feed.
    float gainReductionDb() const override { return panPos_.load(std::memory_order_relaxed); }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (restartReq_.exchange(false, std::memory_order_acquire)) { cycle_ = 0; frac_ = 0.0; }
        if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

        const float  amount = std::clamp(get(Amount), 0.0f, 1.0f);
        const int    wave   = waveIndex();
        const float  shape  = std::clamp(get(Shape), 0.0f, 1.0f);
        const double phOff  = std::clamp((double)get(Phase), 0.0, 1.0);   // 0..1 cycle → 0..360°
        const float  mix    = std::clamp(get(Mix), 0.0f, 1.0f);
        const bool   sync   = get(Sync) >= 0.5f;
        const double div    = kDivBeats[divIndex()];
        const double spb    = spb_ > 0.0 ? spb_ : sr_ * 0.5;              // 120 BPM until the engine says otherwise
        const double rateHz = sync ? sr_ / (spb * div) : expMap(get(Rate), 0.01, 40.0);
        const double inc    = rateHz / sr_;
        const bool   locked = sync && playing_ && spb_ > 0.0;
        // The restart span: the bar, or as many bars as a cycle longer than one needs.
        const double span   = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
        const float  slew   = 1.0f - std::exp(-1.0f / (0.001f * (float)sr_));   // ~1 ms

        float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;
        for (int32_t i = 0; i < frames; ++i) {
            if (locked) {
                const double beat  = beatStart_ + (double)i / spb_;
                const double spans = std::floor(beat / span);
                const double c     = (beat - spans * span) / div;
                const double k     = std::floor(c);
                cycle_ = (int64_t)spans * 4096 + (int64_t)k;
                frac_  = c - k;
            }

            const float lfoL = lfoAt(wave, shape, cycle_, frac_);
            const double fr  = frac_ + phOff;
            const float lfoR = fr >= 1.0 ? lfoAt(wave, shape, cycle_ + 1, fr - 1.0) : lfoAt(wave, shape, cycle_, fr);

            // Amplitude per channel: lfo = +1 → full, lfo = −1 → (1 − amount); Mix blends toward 1.
            const float tL = 1.0f - mix * amount * 0.5f * (1.0f - lfoL);
            const float tR = 1.0f - mix * amount * 0.5f * (1.0f - lfoR);
            if (gLs_ < 0.0f) { gLs_ = tL; gRs_ = tR; }
            gLs_ += slew * (tL - gLs_);
            gRs_ += slew * (tR - gRs_);

            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(dryL), std::fabs(dryR)));
            const float oL = dryL * gLs_, oR = dryR * gRs_;
            buf[i * 2] = oL; buf[i * 2 + 1] = oR;
            outPkL = std::max(outPkL, std::fabs(oL));
            outPkR = std::max(outPkR, std::fabs(oR));

            if (!locked) {
                frac_ += inc;
                if (frac_ >= 1.0) { const double w = std::floor(frac_); frac_ -= w; cycle_ += (int64_t)w; }
            }
        }

        if (frames > 0) {
            const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
            inPkM_ = std::max(inPk, inPkM_ * fall);
            outPkLM_ = std::max(outPkL, outPkLM_ * fall);
            outPkRM_ = std::max(outPkR, outPkRM_ * fall);
        }
        const float gL = gLs_ < 0.0f ? 1.0f : gLs_, gR = gRs_ < 0.0f ? 1.0f : gRs_;
        inPkA_.store(db(inPkM_), std::memory_order_relaxed);
        outPkLA_.store(db(outPkLM_), std::memory_order_relaxed);
        outPkRA_.store(db(outPkRM_), std::memory_order_relaxed);
        gainLA_.store(gL, std::memory_order_relaxed);
        gainRA_.store(gR, std::memory_order_relaxed);
        panPos_.store(0.5f + 0.5f * (gR - gL), std::memory_order_relaxed);
        cycleA_.store(cycle_, std::memory_order_relaxed);
        fracA_.store((float)frac_, std::memory_order_relaxed);
        rateA_.store((float)rateHz, std::memory_order_relaxed);
        bpmA_.store((float)(60.0 * sr_ / spb), std::memory_order_relaxed);
        playingA_.store(playing_ ? 1 : 0, std::memory_order_relaxed);
        lockedA_.store(locked ? 1 : 0, std::memory_order_relaxed);
        barA_.store((float)barBeats_, std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    enum { A_Restart = 0, A_ResetMeters = 1 };   // deviceAction ids
    void deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) override {
        if (id == A_Restart) restartReq_.store(true, std::memory_order_release);
        else if (id == A_ResetMeters) resetReq_.store(true, std::memory_order_release);
    }

    // Telemetry block. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        const int64_t cyc = cycleA_.load(std::memory_order_relaxed);
        const double  fr  = fracA_.load(std::memory_order_relaxed);
        const int64_t base = cyc - (((cyc % 2) + 2) % 2);   // the window starts on an even cycle
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeakL]   = outPkLA_.load(std::memory_order_relaxed);
        t[S_OutPeakR]   = outPkRA_.load(std::memory_order_relaxed);
        t[S_GainL]      = gainLA_.load(std::memory_order_relaxed);
        t[S_GainR]      = gainRA_.load(std::memory_order_relaxed);
        t[S_Pan]        = panOf(t[S_GainL], t[S_GainR]);
        t[S_WinPhase]   = (float)((double)(cyc - base) + fr);
        t[S_RateHz]     = rateA_.load(std::memory_order_relaxed);
        t[S_Bpm]        = bpmA_.load(std::memory_order_relaxed);
        t[S_Playing]    = (float)playingA_.load(std::memory_order_relaxed);
        t[S_Locked]     = (float)lockedA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        for (int k = 0; k < 4; ++k) t[S_Sh0 + k] = hashBip(base - 1 + k);
        t[S_DivBeats]   = (float)kDivBeats[divIndex()];
        t[S_BarBeats]   = barA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Signal]     = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
        t[S_Floor]      = 1.0f - std::clamp(get(Amount), 0.0f, 1.0f) * std::clamp(get(Mix), 0.0f, 1.0f);
        t[S_Cycles]     = (float)cyc;

        // The pan's swing over the two-cycle window (the S&H window uses its own steps).
        const int wave = waveIndex();
        const float amount = get(Amount), mix = get(Mix), shape = get(Shape);
        const double off = std::clamp((double)get(Phase), 0.0, 1.0);
        float mn = 1.0f, mx = -1.0f;
        for (int s = 0; s < 128; ++s) {
            const double p = s / 64.0;
            const int64_t k = (int64_t)std::floor(p);
            const double f = p - (double)k, f2 = f + off;
            const float l = lfoAt(wave, shape, base + k, f);
            const float r = f2 >= 1.0 ? lfoAt(wave, shape, base + k + 1, f2 - 1.0) : lfoAt(wave, shape, base + k, f2);
            const float gl = 1.0f - mix * amount * 0.5f * (1.0f - l), gr = 1.0f - mix * amount * 0.5f * (1.0f - r);
            const float pn = panOf(gl, gr);
            mn = std::min(mn, pn); mx = std::max(mx, pn);
        }
        t[S_PanMin] = mn; t[S_PanMax] = mx;

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        return n;
    }

    const char* displayName() const override { return "Nota Orbit"; }
    int32_t     builtinKind() const override { return 9; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Rate: return "Rate"; case Amount: return "Amount"; case Waveform: return "Waveform";
            case Shape: return "Shape"; case Phase: return "Phase"; case Mix: return "Mix";
            case Sync: return "Sync"; case Division: return "Division"; default: return "";
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

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        static const char* waves[5] = { "Sine", "Triangle", "Saw", "Square", "S&H" };
        char b[900];
        const double deg = std::clamp(get(Phase), 0.0f, 1.0f) * 360.0;
        if (id == 0) {
            std::string s = waves[waveIndex()];
            if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
            else std::snprintf(b, sizeof b, " - %.2f Hz", expMap(get(Rate), 0.01, 40.0));
            s += b;
            std::snprintf(b, sizeof b, " - %s %.0f %% - phase %.0f deg (%s) - amount %.0f %% - mix %.0f %%",
                          waveIndex() == 4 ? "glide" : "shape", get(Shape) * 100.0f, deg, modeWord(deg),
                          get(Amount) * 100.0f, get(Mix) * 100.0f);
            s += b;
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            auto gdb = [](float g) { char t[24]; if (g < 0.001f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", 20.0f * std::log10(g)); return std::string(t); };
            std::snprintf(b, sizeof b,
                          "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - gain L %s dB, R %s dB - pan %+.0f %% "
                          "(swing %+.0f .. %+.0f %%) - floor %s dB - in %s dBFS - out L %s, R %s dBFS - %.1f BPM, bar %.2g beats - "
                          "transport %s - CPU %.2f %%",
                          sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
                          sc[S_Locked] > 0.5f ? " locked to the song" : get(Sync) >= 0.5f ? " free-running at the synced rate" : "",
                          sc[S_WinPhase], gdb(sc[S_GainL]).c_str(), gdb(sc[S_GainR]).c_str(), sc[S_Pan] * 100.0f,
                          sc[S_PanMin] * 100.0f, sc[S_PanMax] * 100.0f, gdb(sc[S_Floor]).c_str(), lv(sc[S_InPeak]).c_str(),
                          lv(sc[S_OutPeakL]).c_str(), lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats],
                          sc[S_Playing] > 0.5f ? "playing" : "stopped", sc[S_Cpu] * 100.0f);
            return b;
        }
        if (id == 2) {
            std::string divs;
            for (int i = 0; i < kDivs; ++i) {
                std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
                divs += b;
            }
            return "All params are normalized 0..1. Rate: the free LFO rate, exponential 0.01 Hz (0) .. 40 Hz (1) "
                   "(0.40 = 0.28 Hz, 0.60 = 1.45 Hz, 0.70 = 3.3 Hz, 0.80 = 7.6 Hz). Amount: the depth, 0..1 - each channel dips "
                   "to 1 - Amount at the LFO's low point. Waveform: 0 Sine, 0.25 Triangle, 0.5 Saw, 0.75 Square, 1 S&H "
                   "(random steps, one per cycle; the right channel plays the same steps Phase later). Shape: sharpens Sine / "
                   "Triangle / Saw toward a square (0 = pure); on S&H it is the glide between steps (0 = hard steps, 1 = a "
                   "glide over the whole cycle); Square ignores it. Phase: the right LFO's offset from the left, 0..1 = "
                   "0..360 deg - 0 = tremolo (both together), 0.5 = 180 deg auto-pan, 0.25 = 90 deg. Mix: 0 = dry .. 1 = "
                   "full effect. Sync: >= 0.5 takes the rate from the tempo (Division) and locks the LFO to the song "
                   "position while the transport plays, restarting on the bar (a 2/1 or 4/1 cycle on 2 or 4 bars). "
                   "Division (with Sync): " + divs + ".";
        }
        return {};
    }

    // ---- shared with the card through the guide above ----------------------------------
    int waveIndex() const { return std::clamp((int)std::lround(get(Waveform) * 4.0f), 0, 4); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }

private:
    static constexpr double kPi = 3.14159265358979323846;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float panOf(float gl, float gr) { return (gr - gl) / std::max(gl + gr, 0.001f); }
    static const char* modeWord(double deg) {
        return deg <= 15.0 || deg >= 345.0 ? "tremolo" : std::fabs(deg - 180.0) <= 15.0 ? "auto-pan" : "offset pan";
    }

    // The S&H step of cycle k, −1..1 — a hash, so the same cycle always draws the same step.
    static float hashBip(int64_t k) {
        uint32_t x = (uint32_t)(uint64_t)k * 0x9E3779B9u ^ 0x53A9C1u;
        x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
        return (float)(x >> 8) / 8388608.0f - 1.0f;
    }

    // LFO value in [−1, 1] at cycle k, phase f (0..1). Shape sharpens the smooth waves toward
    // a square; on S&H it is the glide from the previous step; Square ignores it.
    static float lfoAt(int wave, float shape, int64_t k, double f) {
        double v;
        switch (wave) {
            case 1:  v = 4.0 * std::fabs(f - 0.5) - 1.0; break;      // triangle
            case 2:  v = 2.0 * f - 1.0; break;                       // saw
            case 3:  return f < 0.5 ? 1.0f : -1.0f;                  // square
            case 4: {                                                // sample & hold (+ glide)
                const float a = hashBip(k - 1), bb = hashBip(k);
                double t = shape > 1.0e-3f ? std::min(1.0, f / shape) : 1.0;
                t = t * t * (3.0 - 2.0 * t);
                return (float)(a + (bb - a) * t);
            }
            default: v = std::sin(2.0 * kPi * f); break;             // sine
        }
        if (shape > 1.0e-3f) {
            const double kk = 1.0 + shape * 12.0;
            const double sharp = std::tanh(kk * v) / std::tanh(kk);
            v = v * (1.0 - shape) + sharp * shape;
        }
        return (float)v;
    }

    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    float gLs_ = -1.0f, gRs_ = -1.0f;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, resetReq_{false};
    std::atomic<float> panPos_{0.5f};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> gainLA_{1.0f}, gainRA_{1.0f}, fracA_{0.0f}, rateA_{1.45f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0};
};

} // namespace nota
