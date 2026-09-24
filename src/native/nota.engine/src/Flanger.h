// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Flanger (device kind 23, mockup "Nota Flanger") — a comb filter on a short,
// LFO-modulated delay line. Per channel:
//
//   w[n] = x[n] + Feedback · w[n − τ]        (the delay line, fed back)
//   wet  = ±w[n − τ]                          (the wet polarity follows the feedback's sign)
//   out  = (dry · x + wet_gain · wet) / (dry + wet_gain)
//
// so the wet path is ±e^{−jωτ} / (1 − fb·e^{−jωτ}) and, summed with the dry signal, carves a
// harmonic series of notches that the LFO sweeps up and down. Positive feedback sharpens the
// peaks at the multiples of 1/τ (the classic "jet"); negative feedback flips the wet polarity
// with it, so the comb mirrors — peaks on the odd multiples of 1/2τ, notches on the multiples
// of 1/τ (a hollow, tube-like tone). Without the flip, negative feedback against a positive
// wet path would all but cancel the comb. Near ±95 % the comb rings metallically. The polarity
// glides with the other params, so sweeping Feedback through 0 doesn't click.
//
//   τ(t) = Delay · (1 + 0.92 · Depth · lfo(t))      Delay 0.1 … 8 ms (exponential)
//   LFO: Sine / Triangle / Saw, free (Rate 0.02 … 8 Hz) or Sync (Division 2/1 … 1/16 incl. dotted
//   and triplets). In Sync the LFO locks to the song position while the transport plays and
//   restarts on the bar (a cycle longer than a bar restarts on the span of bars that holds it);
//   stopped, it free-runs at the synced rate. Stereo offsets the right channel's LFO 0 … 180°.
//   Mix: 0 = dry, 0.5 = dry and wet equal (the deepest notches), 1 = wet only (a pure vibrato).
//
// The delay is read with 4-point Hermite interpolation (no linear-interp dulling of the highs),
// never shorter than 2 samples. Delay, Depth, Feedback and Mix are smoothed over ~15 ms so
// automation doesn't zipper.
//
// All params are normalized 0..1 and APPEND ONLY. Persistence / automation / clone flow
// generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, τ now per channel, the LFO's place in its
// two-cycle window, the rate, the tempo, the comb's notch / null / peak).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = clear the delay line (stops a
// ringing comb), 2 = reset the meters.
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
#include <cstring>
#include <string>

namespace nota {

class Flanger : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Delay, Depth, Feedback, Mix, Waveform, Sync, Division, Stereo, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,   // input peak (300 ms fall), dBFS
        S_OutPeakL,     // output peak, left (300 ms fall), dBFS
        S_OutPeakR,     // output peak, right, dBFS
        S_TauL,         // the left delay now, ms
        S_TauR,         // the right delay now, ms
        S_WinPhase,     // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,       // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,          // the tempo the Sync rate follows
        S_Playing,      // 1 while the transport plays
        S_Locked,       // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_BaseMs,       // the Delay param, ms (the centre of the sweep)
        S_TauMin,       // the sweep's shortest / longest delay, ms
        S_TauMax,
        S_NotchHz,      // the first notch of the left comb now, Hz
        S_NotchMinHz,   // the first notch's sweep, Hz (at τ max … τ min)
        S_NotchMaxHz,
        S_NullDb,       // how deep the notches go, dB
        S_PeakDb,       // the comb's peaks, dB
        S_DivBeats,     // the Sync division, beats per cycle
        S_BarBeats,     // beats per bar (the restart span's unit)
        S_Cpu,          // share of real time spent in process()
        S_Signal,       // 1 while signal comes in (above −70 dBFS)
        S_Cycles,       // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        kTele = 32
    };

    static constexpr int kDivs = 9;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = { 8.0, 4.0, 2.0, 1.5, 1.0, 2.0 / 3.0, 0.5, 1.0 / 3.0, 0.25 };
    static constexpr const char* kDivNames[kDivs] = { "2/1", "1/1", "1/2", "1/4D", "1/4", "1/4T", "1/8", "1/8T", "1/16" };
    static constexpr int kDefaultDiv = 1;   // 1/1

    // Ranges (mirrored by the card and MCP).
    static constexpr double kRateLo = 0.02, kRateHi = 8.0;       // Hz, exponential
    static constexpr double kDelayLo = 0.1, kDelayHi = 8.0;      // ms, exponential
    static constexpr double kFbMax = 0.95;                       // feedback ±95 %
    static constexpr double kDepthScale = 0.92;                  // τ swings Delay · (1 ± 0.92 · Depth)

    Flanger() {
        p_[Rate].store(0.384f);                                   // 0.20 Hz
        p_[Delay].store(0.735f);                                  // 2.5 ms
        p_[Depth].store(0.80f);
        p_[Feedback].store((float)(0.5 + 0.70 / (2.0 * kFbMax))); // +70 %
        p_[Mix].store(0.50f);
        p_[Waveform].store(0.5f);                                 // Triangle
        p_[Sync].store(0.0f);                                     // Free (Hz)
        p_[Division].store((float)kDefaultDiv / (kDivs - 1));
        p_[Stereo].store(0.5f);                                   // 90°
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        cycle_ = 0; frac_ = 0.0;
        clearLine();
        snap_ = true;
        resetMeters();
    }

    // Musical clock for Sync (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override {
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : 0.0; playing_ = playing;
    }
    void setTransportInfo(const TransportInfo& ti) override {
        if (ti.tsNum > 0 && ti.tsDenom > 0) barBeats_ = std::clamp(ti.tsNum * 4.0 / ti.tsDenom, 0.5, 64.0);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (restartReq_.exchange(false, std::memory_order_acquire)) { cycle_ = 0; frac_ = 0.0; }
        if (clearReq_.exchange(false, std::memory_order_acquire)) clearLine();
        if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

        const int    wave   = waveIndex();
        const double base   = baseMs();
        const double depth  = std::clamp((double)get(Depth), 0.0, 1.0);
        const double fb     = feedback();
        const double mix    = std::clamp((double)get(Mix), 0.0, 1.0);
        const double dryT   = std::min(1.0, 2.0 * (1.0 - mix)), wetT = std::min(1.0, 2.0 * mix);
        const double phOff  = stereoDeg() / 360.0;
        const bool   sync   = get(Sync) >= 0.5f;
        const double div    = kDivBeats[divIndex()];
        const double spb    = spb_ > 0.0 ? spb_ : sr_ * 0.5;              // 120 BPM until the engine says otherwise
        const double rateHz = sync ? sr_ / (spb * div) : rateFreeHz();
        const double inc    = rateHz / sr_;
        const bool   locked = sync && playing_ && spb_ > 0.0;
        const double span   = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
        const double sm     = 1.0 - std::exp(-1.0 / (0.015 * sr_));        // ~15 ms parameter glide
        const double msToS  = sr_ * 0.001;
        const double maxD   = (double)(kBuf - 4);

        const double wetSignT = fb < 0.0 ? -wetT : wetT;
        if (snap_) { baseS_ = base; depthS_ = depth; fbS_ = fb; dryS_ = dryT; wetS_ = wetSignT; snap_ = false; }

        float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;
        double tauL = 0.0, tauR = 0.0;
        for (int32_t i = 0; i < frames; ++i) {
            if (locked) {
                const double beat  = beatStart_ + (double)i / spb_;
                const double spans = std::floor(beat / span);
                const double c     = (beat - spans * span) / div;
                const double k     = std::floor(c);
                cycle_ = (int64_t)spans * 4096 + (int64_t)k;
                frac_  = c - k;
            }
            baseS_  += sm * (base - baseS_);
            depthS_ += sm * (depth - depthS_);
            fbS_    += sm * (fb - fbS_);
            dryS_   += sm * (dryT - dryS_);
            wetS_   += sm * (wetSignT - wetS_);

            const double lfoL = lfo(wave, frac_);
            const double lfoR = lfo(wave, frac_ + phOff);
            tauL = baseS_ * (1.0 + kDepthScale * depthS_ * lfoL);
            tauR = baseS_ * (1.0 + kDepthScale * depthS_ * lfoR);
            const double dL = std::clamp(tauL * msToS, 2.0, maxD);
            const double dR = std::clamp(tauR * msToS, 2.0, maxD);

            const float xL = buf[i * 2], xR = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(xL), std::fabs(xR)));
            const float yL = readLine(lineL_, dL), yR = readLine(lineR_, dR);
            lineL_[w_] = sanitize(xL + (float)fbS_ * yL);
            lineR_[w_] = sanitize(xR + (float)fbS_ * yR);
            w_ = (w_ + 1) & kMask;

            const double norm = 1.0 / std::max(dryS_ + std::fabs(wetS_), 1e-3);
            const float oL = (float)((dryS_ * xL + wetS_ * yL) * norm);
            const float oR = (float)((dryS_ * xR + wetS_ * yR) * norm);
            buf[i * 2] = oL; buf[i * 2 + 1] = oR;
            outPkL = std::max(outPkL, std::fabs(oL));
            outPkR = std::max(outPkR, std::fabs(oR));

            if (!locked) {
                frac_ += inc;
                if (frac_ >= 1.0) { const double wr = std::floor(frac_); frac_ -= wr; cycle_ += (int64_t)wr; }
            }
        }

        if (frames > 0) {
            const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
            inPkM_ = std::max(inPk, inPkM_ * fall);
            outPkLM_ = std::max(outPkL, outPkLM_ * fall);
            outPkRM_ = std::max(outPkR, outPkRM_ * fall);
            tauLA_.store((float)tauL, std::memory_order_relaxed);
            tauRA_.store((float)tauR, std::memory_order_relaxed);
        }
        inPkA_.store(db(inPkM_), std::memory_order_relaxed);
        outPkLA_.store(db(outPkLM_), std::memory_order_relaxed);
        outPkRA_.store(db(outPkRM_), std::memory_order_relaxed);
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

    enum { A_Restart = 0, A_Clear = 1, A_ResetMeters = 2 };   // deviceAction ids
    void deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) override {
        if (id == A_Restart) restartReq_.store(true, std::memory_order_release);
        else if (id == A_Clear) clearReq_.store(true, std::memory_order_release);
        else if (id == A_ResetMeters) resetReq_.store(true, std::memory_order_release);
    }

    // Telemetry block. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        const int64_t cyc = cycleA_.load(std::memory_order_relaxed);
        const double  fr  = fracA_.load(std::memory_order_relaxed);
        const int64_t base = cyc - (((cyc % 2) + 2) % 2);   // the window starts on an even cycle
        const double  bms = baseMs(), dep = std::clamp((double)get(Depth), 0.0, 1.0);
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeakL]   = outPkLA_.load(std::memory_order_relaxed);
        t[S_OutPeakR]   = outPkRA_.load(std::memory_order_relaxed);
        t[S_TauL]       = tauLA_.load(std::memory_order_relaxed);
        t[S_TauR]       = tauRA_.load(std::memory_order_relaxed);
        if (t[S_TauL] <= 0.0f) t[S_TauL] = t[S_TauR] = (float)bms;   // before the first block
        t[S_WinPhase]   = (float)((double)(cyc - base) + fr);
        t[S_RateHz]     = rateA_.load(std::memory_order_relaxed);
        t[S_Bpm]        = bpmA_.load(std::memory_order_relaxed);
        t[S_Playing]    = (float)playingA_.load(std::memory_order_relaxed);
        t[S_Locked]     = (float)lockedA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_BaseMs]     = (float)bms;
        t[S_TauMin]     = (float)(bms * (1.0 - kDepthScale * dep));
        t[S_TauMax]     = (float)(bms * (1.0 + kDepthScale * dep));
        const Comb cb   = comb();
        t[S_NotchHz]    = (float)(cb.notchTheta / (2.0 * kPi) / (std::max(1e-6f, t[S_TauL]) * 0.001));
        t[S_NotchMinHz] = (float)(cb.notchTheta / (2.0 * kPi) / (std::max(1e-6f, t[S_TauMax]) * 0.001));
        t[S_NotchMaxHz] = (float)(cb.notchTheta / (2.0 * kPi) / (std::max(1e-6f, t[S_TauMin]) * 0.001));
        t[S_NullDb]     = (float)cb.nullDb;
        t[S_PeakDb]     = (float)cb.peakDb;
        t[S_DivBeats]   = (float)kDivBeats[divIndex()];
        t[S_BarBeats]   = barA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Signal]     = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
        t[S_Cycles]     = (float)cyc;
        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        return n;
    }

    const char* displayName() const override { return "Nota Flanger"; }
    int32_t     builtinKind() const override { return 23; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Rate: return "Rate"; case Delay: return "Delay"; case Depth: return "Depth";
            case Feedback: return "Feedback"; case Mix: return "Mix"; case Waveform: return "Waveform";
            case Sync: return "Sync"; case Division: return "Division"; case Stereo: return "Stereo";
            default: return "";
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
        static const char* waves[3] = { "Sine", "Triangle", "Saw" };
        char b[1400];
        if (id == 0) {
            std::string s = waves[waveIndex()];
            if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
            else std::snprintf(b, sizeof b, " - %.2f Hz", rateFreeHz());
            s += b;
            std::snprintf(b, sizeof b, " - stereo %.0f deg - delay %.2f ms - depth %.0f %% - feedback %+.0f %% (%s) - mix %.0f %%",
                          stereoDeg(), baseMs(), get(Depth) * 100.0f, feedback() * 100.0, modeWord(),
                          get(Mix) * 100.0f);
            s += b;
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::snprintf(b, sizeof b,
                          "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - delay L %.2f ms, R %.2f ms "
                          "(sweep %.2f .. %.2f ms) - first notch %.0f Hz (sweeps %.0f .. %.0f Hz), %.1f dB deep - comb peaks %+.1f dB - "
                          "in %s dBFS - out L %s, R %s dBFS - %.1f BPM, bar %.2g beats - transport %s - CPU %.2f %%",
                          sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
                          sc[S_Locked] > 0.5f ? " locked to the song" : get(Sync) >= 0.5f ? " free-running at the synced rate" : "",
                          sc[S_WinPhase], sc[S_TauL], sc[S_TauR], sc[S_TauMin], sc[S_TauMax], sc[S_NotchHz],
                          sc[S_NotchMinHz], sc[S_NotchMaxHz], sc[S_NullDb], sc[S_PeakDb], lv(sc[S_InPeak]).c_str(),
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
            return "All params are normalized 0..1. Rate: the free LFO rate, exponential 0.02 Hz (0) .. 8 Hz (1) "
                   "(0.269 = 0.1 Hz, 0.384 = 0.2 Hz, 0.537 = 0.5 Hz, 0.653 = 1 Hz, 0.769 = 2 Hz, 0.884 = 4 Hz). Delay: the "
                   "centre of the sweep, exponential 0.1 ms (0) .. 8 ms (1) (0.263 = 0.32 ms, 0.409 = 0.6 ms, 0.525 = 1 ms, "
                   "0.567 = 1.2 ms, 0.683 = 2 ms, 0.735 = 2.5 ms, 0.776 = 3 ms, 0.841 = 4 ms, 0.892 = 5 ms). Depth: how far "
                   "the delay swings, 0..1 - tau = Delay * (1 +/- 0.92 * Depth). Feedback: bipolar, (v - 0.5) * 1.9 = -95 % .. "
                   "+95 % (0.5 = none, 0.868 = +70 %, 0.132 = -70 %); positive sharpens the peaks at multiples of 1/delay "
                   "(jet), negative also flips the wet polarity so the peaks move to the odd multiples of 1/(2 delay) "
                   "(hollow); past 85 % the comb rings. Mix: 0 = dry, 0.5 = dry and wet equal (the "
                   "deepest notches), 1 = wet only (a pitch vibrato). Waveform: 0 Sine, 0.5 Triangle, 1 Saw. Stereo: the right "
                   "LFO's offset from the left, 0..1 = 0..180 deg (0 = mono sweep, 0.5 = 90 deg, 1 = opposite). Sync: >= 0.5 "
                   "takes the rate from the tempo (Division) and locks the LFO to the song position while the transport "
                   "plays, restarting on the bar. Division (with Sync): " + divs + ".";
        }
        return {};
    }

    // ---- shared with the card / MCP through the guide above ----------------------------
    int waveIndex() const { return std::clamp((int)std::lround(get(Waveform) * 2.0f), 0, 2); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }
    double rateFreeHz() const { return expMap(get(Rate), kRateLo, kRateHi); }
    double baseMs() const { return expMap(get(Delay), kDelayLo, kDelayHi); }
    double feedback() const { return (std::clamp((double)get(Feedback), 0.0, 1.0) - 0.5) * 2.0 * kFbMax; }
    double stereoDeg() const { return std::clamp((double)get(Stereo), 0.0, 1.0) * 180.0; }

    // |dry ± wet·H(θ)| / (dry + wet), H = e^{−jθ} / (1 − fb·e^{−jθ}), θ = ωτ — the comb's magnitude
    // (the wet polarity follows the feedback's sign, as in process()).
    static double combMag(double dry, double wet, double fb, double th) {
        const double c = std::cos(th), s = std::sin(th);
        return combMagSigned(dry, fb < 0.0 ? -wet : wet, fb, c, s);
    }

private:
    static double combMagSigned(double dry, double wet, double fb, double c, double s) {
        const double nr = wet * c, ni = -wet * s, dr = 1.0 - fb * c, di = fb * s, den = dr * dr + di * di;
        const double re = dry + (nr * dr + ni * di) / den, im = (ni * dr - nr * di) / den;
        return std::sqrt(re * re + im * im) / std::max(dry + std::fabs(wet), 0.001);
    }
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kBuf = 16384;                 // ≥ 15.4 ms at 768 kHz
    static constexpr int kMask = kBuf - 1;

    struct Comb { double notchTheta, nullDb, peakDb; };
    // The first notch (the phase θ = ωτ in (0, 2π] where the comb dips deepest — π for positive
    // feedback, 2π for negative), its depth and the peak level.
    Comb comb() const {
        const double mix = std::clamp((double)get(Mix), 0.0, 1.0), fb = feedback();
        const double dry = std::min(1.0, 2.0 * (1.0 - mix)), wet = std::min(1.0, 2.0 * mix);
        double best = kPi, bm = 1e9;
        for (int i = 1; i <= 180; ++i) {
            const double th = i / 180.0 * 2.0 * kPi, m = combMag(dry, wet, fb, th);
            if (m < bm) { bm = m; best = th; }
        }
        const double pk = combMag(dry, wet, fb, fb >= 0.0 ? 0.0 : kPi);
        return { best, 20.0 * std::log10(std::max(bm, 1e-4)), 20.0 * std::log10(std::max(pk, 1e-4)) };
    }

    const char* modeWord() const {
        const double fb = feedback();
        return std::fabs(fb) >= 0.85 ? "resonant" : fb <= -0.15 ? "negative" : fb >= 0.15 ? "positive" : "through";
    }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float sanitize(float v) { return std::isfinite(v) && std::fabs(v) > 1e-20f ? std::clamp(v, -64.0f, 64.0f) : 0.0f; }

    // LFO in [−1, 1] at phase p (cycles; wraps) — the mockup's shapes: all start at 0 rising.
    static double lfo(int wave, double p) {
        const double f = p - std::floor(p);
        if (wave == 0) return std::sin(2.0 * kPi * f);
        if (wave == 1) return f < 0.25 ? 4.0 * f : f < 0.75 ? 2.0 - 4.0 * f : 4.0 * f - 4.0;
        const double g = f + 0.5;
        return 2.0 * (g - std::floor(g)) - 1.0;
    }

    // 4-point Hermite read `d` samples behind the write head (d ≥ 2, so every tap is written).
    float readLine(const float* line, double d) const {
        const double r = (double)w_ - d;
        const double fl = std::floor(r);
        const float t = (float)(r - fl);
        const int i0 = (int)((int64_t)fl & kMask);
        const float ym1 = line[(i0 - 1) & kMask], y0 = line[i0], y1 = line[(i0 + 1) & kMask], y2 = line[(i0 + 2) & kMask];
        const float c1 = 0.5f * (y1 - ym1);
        const float c2 = ym1 - 2.5f * y0 + 2.0f * y1 - 0.5f * y2;
        const float c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);
        return ((c3 * t + c2) * t + c1) * t + y0;
    }

    void clearLine() { std::memset(lineL_, 0, sizeof lineL_); std::memset(lineR_, 0, sizeof lineR_); w_ = 0; }
    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float lineL_[kBuf] = {}, lineR_[kBuf] = {};
    int w_ = 0;
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    bool snap_ = true;
    double baseS_ = 2.5, depthS_ = 0.8, fbS_ = 0.7, dryS_ = 1.0, wetS_ = 1.0;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, clearReq_{false}, resetReq_{false};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> tauLA_{0.0f}, tauRA_{0.0f}, fracA_{0.0f}, rateA_{0.2f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0};
};

} // namespace nota
