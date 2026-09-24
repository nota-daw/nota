// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Chorus (device kind 25, mockup "Nota Chorus") — modulated delay voices in three
// modes:
//
//   Classic   two voices, L and R, each on its own channel's delay line; the right voice's LFO
//             runs Offset (0 … 180°) behind the left.     τ = 8 ms ± 5 ms · Amount
//   Ensemble  three voices 120° apart (V1 left, V2 centre, V3 right) — the string-machine sound.
//             τ = 12 ms ± 6 ms · Amount (Offset is not used)
//   Vibrato   the Classic pair on a short line, wet only (Mix is locked to 100 %).
//             τ = 3 ms ± 2.5 ms · Amount
//
// Per voice τ(t) = base + dep · sin(2π(p + φ)); the delay's slope detunes the voice by
// 1200 · log2(1 − dτ/dt) cents, at most ±1200 · log2(1 + dep · 2π · rate). The wet path:
//
//   in → HPF (off, or 20 Hz … 2 kHz, 12 dB/oct) → + Feedback · wet → Warmth → delay line
//   voices → wet L / R → Width (M/S: 0 = mono, 100 % = as is, 200 % = side +6 dB)
//   out = (dry · x + wet_gain · wet) / (dry + wet_gain)   (Mix 0.5 = equal; Vibrato = wet only)
//
// Warmth is a bucket-brigade flavour on the line input: a one-pole low-pass that closes from
// 20 kHz to ~2.5 kHz and a soft tanh saturation that grows with it. Feedback is bipolar ±90 %.
// LFO: a sine, free (Rate 0.02 … 8 Hz) or Sync (Division 4/1 … 1/16 incl. triplets). In Sync the
// LFO locks to the song position while the transport plays and restarts on the bar (a cycle longer
// than a bar restarts on the span of bars that holds it); stopped, it free-runs at the synced rate.
//
// The delay is read with 4-point Hermite interpolation. Amount, Feedback, Offset, Width, Warmth and
// Mix glide over ~15 ms; a Mode change fades the wet out and back in (~8 ms each way) instead of
// jumping the delay.
//
// All params are normalized 0..1 and APPEND ONLY. Persistence / automation / clone flow
// generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, τ and cents now per voice, the LFO's place in
// its two-cycle window, the rate, the tempo, the sweep and the detune range).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = clear the delay lines (stops a
// ringing feedback loop), 2 = reset the meters.
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

class Chorus : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Mode, Sync, Division, Offset, Hpf, Amount, Feedback, Width, Warmth, Mix, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,   // input peak (300 ms fall), dBFS
        S_OutPeakL,     // output peak, left (300 ms fall), dBFS
        S_OutPeakR,     // output peak, right, dBFS
        S_Tau1,         // each voice's delay now, ms (V3 = 0 outside Ensemble)
        S_Tau2,
        S_Tau3,
        S_Cents1,       // each voice's detune now, cents
        S_Cents2,
        S_Cents3,
        S_WinPhase,     // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,       // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,          // the tempo the Sync rate follows
        S_Playing,      // 1 while the transport plays
        S_Locked,       // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_BaseMs,       // the mode's centre delay, ms
        S_TauMin,       // the sweep's shortest / longest delay, ms
        S_TauMax,
        S_MaxCents,     // the largest detune the sweep reaches, ± cents
        S_Voices,       // 2 or 3
        S_Mode,         // 0 Classic, 1 Ensemble, 2 Vibrato
        S_HpHz,         // the wet high-pass corner, Hz (0 = off)
        S_WidthPct,     // Width, %
        S_DivBeats,     // the Sync division, beats per cycle
        S_BarBeats,     // beats per bar (the restart span's unit)
        S_Cpu,          // share of real time spent in process()
        S_Signal,       // 1 while signal comes in (above −70 dBFS)
        S_Cycles,       // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        kTele = 32
    };

    static constexpr int kModes = 3;
    static constexpr const char* kModeNames[kModes] = { "Classic", "Ensemble", "Vibrato" };
    // Per mode: the centre delay and the swing at Amount 100 %, ms.
    static constexpr double kBaseMs[kModes] = { 8.0, 12.0, 3.0 };
    static constexpr double kDepMs[kModes]  = { 5.0, 6.0, 2.5 };

    static constexpr int kDivs = 9;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = { 16.0, 8.0, 4.0, 2.0, 1.0, 2.0 / 3.0, 0.5, 1.0 / 3.0, 0.25 };
    static constexpr const char* kDivNames[kDivs] = { "4/1", "2/1", "1/1", "1/2", "1/4", "1/4T", "1/8", "1/8T", "1/16" };
    static constexpr int kDefaultDiv = 2;   // 1/1

    // Ranges (mirrored by the card and MCP).
    static constexpr double kRateLo = 0.02, kRateHi = 8.0;       // Hz, exponential
    static constexpr double kHpLo = 20.0, kHpHi = 2000.0;        // Hz, exponential; below 0.01 = off
    static constexpr double kFbMax = 0.90;                       // feedback ±90 %
    static constexpr double kWidthMax = 2.0;                     // 200 %

    Chorus() {
        p_[Rate].store(0.6157f);                                  // 0.80 Hz
        p_[Mode].store(0.0f);                                     // Classic
        p_[Sync].store(0.0f);                                     // Free (Hz)
        p_[Division].store((float)kDefaultDiv / (kDivs - 1));
        p_[Offset].store(1.0f);                                   // 180°
        p_[Hpf].store(0.0f);                                      // off
        p_[Amount].store(0.40f);
        p_[Feedback].store(0.5f);                                 // 0
        p_[Width].store(0.5f);                                    // 100 %
        p_[Warmth].store(0.0f);
        p_[Mix].store(0.50f);
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        cycle_ = 0; frac_ = 0.0;
        clearLines();
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
        if (clearReq_.exchange(false, std::memory_order_acquire)) clearLines();
        if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

        const int    modeT  = modeIndex();
        const double amount = std::clamp((double)get(Amount), 0.0, 1.0);
        const double fb     = feedback();
        const double offT   = offsetDeg() / 360.0;
        const double width  = widthPct() / 100.0;
        const double warmth = std::clamp((double)get(Warmth), 0.0, 1.0);
        const bool   sync   = get(Sync) >= 0.5f;
        const double div    = kDivBeats[divIndex()];
        const double spb    = spb_ > 0.0 ? spb_ : sr_ * 0.5;              // 120 BPM until the engine says otherwise
        const double rateHz = sync ? sr_ / (spb * div) : rateFreeHz();
        const double inc    = rateHz / sr_;
        const bool   locked = sync && playing_ && spb_ > 0.0;
        const double span   = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
        const double sm     = 1.0 - std::exp(-1.0 / (0.015 * sr_));        // ~15 ms parameter glide
        const double fadeStep = 1.0 / (0.008 * sr_);                       // ~8 ms mode fade
        const double msToS  = sr_ * 0.001;
        const double maxD   = (double)(kBuf - 4);
        const double hpHz   = hpfHz();
        const bool   hpOn   = hpHz > 0.0;

        if (snap_) {
            mode_ = modeT; amountS_ = amount; fbS_ = fb; offS_ = offT; widthS_ = width; warmS_ = warmth;
            const MixW mw = mixWeights(modeT);
            dryS_ = mw.dry; wetS_ = mw.wet; fade_ = 1.0; hpS_ = hpOn ? hpHz : 0.0;
            snap_ = false;
        }

        float inPk = 0.0f, outPkL = 0.0f, outPkR = 0.0f;
        double tau[3] = { 0.0, 0.0, 0.0 };
        for (int32_t i = 0; i < frames; ++i) {
            if (locked) {
                const double beat  = beatStart_ + (double)i / spb_;
                const double spans = std::floor(beat / span);
                const double c     = (beat - spans * span) / div;
                const double k     = std::floor(c);
                cycle_ = (int64_t)spans * 4096 + (int64_t)k;
                frac_  = c - k;
            }
            // A Mode change: fade the wet out, switch while it is silent, fade back in.
            if (modeT != mode_) {
                fade_ -= fadeStep;
                if (fade_ <= 0.0) { fade_ = 0.0; mode_ = modeT; }
            } else if (fade_ < 1.0) {
                fade_ = std::min(1.0, fade_ + fadeStep);
            }
            const MixW mw = mixWeights(mode_);
            amountS_ += sm * (amount - amountS_);
            fbS_     += sm * (fb - fbS_);
            offS_    += sm * (offT - offS_);
            widthS_  += sm * (width - widthS_);
            warmS_   += sm * (warmth - warmS_);
            dryS_    += sm * (mw.dry - dryS_);
            wetS_    += sm * (mw.wet - wetS_);

            // Control-rate coefficients: the HPF (TPT SVF, Q 0.707) and the warmth low-pass.
            if ((i & 15) == 0) {
                if (hpOn) {
                    hpS_ = hpS_ > 0.0 ? hpS_ + 16.0 * sm * (hpHz - hpS_) : hpHz;
                    const double g = std::tan(kPi * std::min(hpS_, sr_ * 0.45) / sr_);
                    hpG_ = g; hpK_ = 1.4142135623730951; hpA1_ = 1.0 / (1.0 + g * (g + hpK_));
                } else {
                    hpS_ = 0.0;
                }
                const double lpHz = 20000.0 * std::pow(0.125, warmS_);   // 20 kHz … 2.5 kHz
                lpA_ = 1.0 - std::exp(-2.0 * kPi * std::min(lpHz, sr_ * 0.45) / sr_);
            }

            const double base = kBaseMs[mode_], dep = kDepMs[mode_] * amountS_;
            const int nv = mode_ == 1 ? 3 : 2;
            for (int v = 0; v < nv; ++v)
                tau[v] = base + dep * std::sin(2.0 * kPi * (frac_ + voicePhase(mode_, v, offS_)));
            if (nv == 2) tau[2] = 0.0;

            const float xL = buf[i * 2], xR = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(xL), std::fabs(xR)));

            // Voices → wet L / R.
            double wL, wR;
            const double d0 = std::clamp(tau[0] * msToS, 2.0, maxD), d1 = std::clamp(tau[1] * msToS, 2.0, maxD);
            if (nv == 3) {
                const double d2 = std::clamp(tau[2] * msToS, 2.0, maxD);
                const double v1 = readLine(lineL_, d0);
                const double v2 = 0.5 * (readLine(lineL_, d1) + readLine(lineR_, d1));
                const double v3 = readLine(lineR_, d2);
                wL = (v1 + kCentre * v2) * kEnsNorm;
                wR = (v3 + kCentre * v2) * kEnsNorm;
            } else {
                wL = readLine(lineL_, d0);
                wR = readLine(lineR_, d1);
            }

            // The line input: HPF'd dry + feedback, through the warmth stage.
            double hL = xL, hR = xR;
            if (hpOn) { hL = hpTick(hpL_, xL); hR = hpTick(hpR_, xR); }
            lineL_[w_] = sanitize((float)warm(lpL_, hL + fbS_ * wL));
            lineR_[w_] = sanitize((float)warm(lpR_, hR + fbS_ * wR));
            w_ = (w_ + 1) & kMask;

            // Width on the wet (M/S).
            const double m = 0.5 * (wL + wR), s = 0.5 * (wL - wR) * widthS_;
            const double wet = wetS_ * fade_;
            const double norm = 1.0 / std::max(dryS_ + wetS_, 1e-3);
            const float oL = (float)((dryS_ * xL + wet * (m + s)) * norm);
            const float oR = (float)((dryS_ * xR + wet * (m - s)) * norm);
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
            const double dep = kDepMs[mode_] * amountS_;
            for (int v = 0; v < 3; ++v) {
                tauA_[v].store((float)tau[v], std::memory_order_relaxed);
                const bool live = v < (mode_ == 1 ? 3 : 2);
                centsA_[v].store(live ? (float)centsAt(dep, rateHz, frac_ + voicePhase(mode_, v, offS_)) : 0.0f,
                                 std::memory_order_relaxed);
            }
            modeA_.store(mode_, std::memory_order_relaxed);
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
        const int     md  = modeIndex();
        const double  bms = kBaseMs[md], dep = depMs();
        const float   rate = rateA_.load(std::memory_order_relaxed);
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeakL]   = outPkLA_.load(std::memory_order_relaxed);
        t[S_OutPeakR]   = outPkRA_.load(std::memory_order_relaxed);
        const bool fresh = modeA_.load(std::memory_order_relaxed) == md && tauA_[0].load(std::memory_order_relaxed) > 0.0f;
        for (int v = 0; v < 3; ++v) {
            const bool live = v < voices();
            t[S_Tau1 + v]   = !live ? 0.0f : fresh ? tauA_[v].load(std::memory_order_relaxed) : (float)bms;
            t[S_Cents1 + v] = !live || !fresh ? 0.0f : centsA_[v].load(std::memory_order_relaxed);
        }
        t[S_WinPhase]   = (float)((double)(cyc - base) + fr);
        t[S_RateHz]     = rate;
        t[S_Bpm]        = bpmA_.load(std::memory_order_relaxed);
        t[S_Playing]    = (float)playingA_.load(std::memory_order_relaxed);
        t[S_Locked]     = (float)lockedA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_BaseMs]     = (float)bms;
        t[S_TauMin]     = (float)(bms - dep);
        t[S_TauMax]     = (float)(bms + dep);
        t[S_MaxCents]   = (float)maxCents(dep, rate);
        t[S_Voices]     = (float)voices();
        t[S_Mode]       = (float)md;
        t[S_HpHz]       = (float)hpfHz();
        t[S_WidthPct]   = (float)widthPct();
        t[S_DivBeats]   = (float)kDivBeats[divIndex()];
        t[S_BarBeats]   = barA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Signal]     = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
        t[S_Cycles]     = (float)cyc;
        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        return n;
    }

    const char* displayName() const override { return "Nota Chorus"; }
    int32_t     builtinKind() const override { return 25; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Rate: return "Rate"; case Mode: return "Mode"; case Sync: return "Sync"; case Division: return "Division";
            case Offset: return "Offset"; case Hpf: return "HPF"; case Amount: return "Amount"; case Feedback: return "Feedback";
            case Width: return "Width"; case Warmth: return "Warmth"; case Mix: return "Mix";
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
        char b[1400];
        const int md = modeIndex();
        if (id == 0) {
            std::string s = kModeNames[md];
            if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
            else std::snprintf(b, sizeof b, " - %.2f Hz", rateFreeHz());
            s += b;
            if (md == 1) s += " - 3 voices 120 deg apart";
            else { std::snprintf(b, sizeof b, " - offset %.0f deg", offsetDeg()); s += b; }
            const double hp = hpfHz();
            if (hp > 0.0) { std::snprintf(b, sizeof b, " - HPF %.0f Hz", hp); s += b; }
            else s += " - HPF off";
            std::snprintf(b, sizeof b, " - amount %.0f %% - feedback %+.0f %% - width %.0f %% - warmth %.0f %%",
                          get(Amount) * 100.0f, feedback() * 100.0, widthPct(), get(Warmth) * 100.0f);
            s += b;
            if (md == 2) s += " - mix wet (vibrato)";
            else { std::snprintf(b, sizeof b, " - mix %.0f %%", get(Mix) * 100.0f); s += b; }
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::string s;
            std::snprintf(b, sizeof b, "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - %d voices:",
                          sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
                          sc[S_Locked] > 0.5f ? " locked to the song" : get(Sync) >= 0.5f ? " free-running at the synced rate" : "",
                          sc[S_WinPhase], (int)sc[S_Voices]);
            s += b;
            for (int v = 0; v < (int)sc[S_Voices]; ++v) {
                std::snprintf(b, sizeof b, " %s %.2f ms %+.1f ct%s", voiceName(md, v), sc[S_Tau1 + v], sc[S_Cents1 + v],
                              v + 1 < (int)sc[S_Voices] ? "," : "");
                s += b;
            }
            std::snprintf(b, sizeof b,
                          " - sweep %.2f .. %.2f ms - detune up to +/-%.1f ct - in %s dBFS - out L %s, R %s dBFS - "
                          "%.1f BPM, bar %.2g beats - transport %s - CPU %.2f %%",
                          sc[S_TauMin], sc[S_TauMax], sc[S_MaxCents], lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeakL]).c_str(),
                          lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats], sc[S_Playing] > 0.5f ? "playing" : "stopped",
                          sc[S_Cpu] * 100.0f);
            s += b;
            return s;
        }
        if (id == 2) {
            std::string divs;
            for (int i = 0; i < kDivs; ++i) {
                std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
                divs += b;
            }
            return "All params are normalized 0..1. Mode: 0 Classic (2 voices L / R, tau 8 ms +/- 5 ms * Amount), 0.5 Ensemble "
                   "(3 voices 120 deg apart, left / centre / right, tau 12 +/- 6 ms), 1 Vibrato (the L / R pair on 3 +/- 2.5 ms, "
                   "wet only - Mix is ignored). Rate: the free LFO rate, exponential 0.02 Hz (0) .. 8 Hz (1) (0.269 = 0.1 Hz, "
                   "0.384 = 0.2 Hz, 0.537 = 0.5 Hz, 0.616 = 0.8 Hz, 0.653 = 1 Hz, 0.769 = 2 Hz, 0.884 = 4 Hz, 0.938 = 5.5 Hz). "
                   "Offset: the right voice's LFO behind the left, 0..1 = 0..180 deg (Classic / Vibrato). HPF: the wet path's "
                   "high-pass, 0 = off, else exponential 20 Hz .. 2 kHz (0.15 = 40 Hz, 0.35 = 100 Hz, 0.5 = 200 Hz, 0.65 = 400 Hz). "
                   "Amount: how far the delay swings, 0..1 of the mode's range. Feedback: bipolar, (v - 0.5) * 1.8 = -90 % .. +90 % "
                   "(0.5 = none); past 60 % it rings like a flanger. Width: the wet's stereo width, 0..1 = 0..200 % (0.5 = 100 %; "
                   "0 = mono; above 100 % lifts the side up to +6 dB). Warmth: 0 = clean, 1 = a dark, saturated bucket-brigade "
                   "line (low-pass 20 kHz .. 2.5 kHz). Mix: 0 = dry, 0.5 = dry and wet equal, 1 = wet only. Sync: >= 0.5 takes "
                   "the rate from the tempo (Division) and locks the LFO to the song position while the transport plays, "
                   "restarting on the bar. Division (with Sync): " + divs + ".";
        }
        return {};
    }

    // ---- shared with the card / MCP through the guide above ----------------------------
    int modeIndex() const { return std::clamp((int)std::lround(get(Mode) * 2.0f), 0, 2); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }
    int voices() const { return modeIndex() == 1 ? 3 : 2; }
    double rateFreeHz() const { return expMap(get(Rate), kRateLo, kRateHi); }
    double hpfHz() const { const float v = get(Hpf); return v < 0.01f ? 0.0 : expMap(v, kHpLo, kHpHi); }
    double feedback() const { return (std::clamp((double)get(Feedback), 0.0, 1.0) - 0.5) * 2.0 * kFbMax; }
    double offsetDeg() const { return std::clamp((double)get(Offset), 0.0, 1.0) * 180.0; }
    double widthPct() const { return std::clamp((double)get(Width), 0.0, 1.0) * kWidthMax * 100.0; }
    double depMs() const { return kDepMs[modeIndex()] * std::clamp((double)get(Amount), 0.0, 1.0); }

    // The detune of a voice whose delay swings ±dep ms at rate Hz, at LFO phase p (cycles): the
    // read head's speed is 1 − dτ/dt.
    static double centsAt(double depMs, double rateHz, double p) {
        const double dt = depMs * 0.001 * 2.0 * kPi * rateHz * std::cos(2.0 * kPi * p);
        return 1200.0 * std::log2(std::max(1.0 - dt, 0.01));
    }
    static double maxCents(double depMs, double rateHz) {
        return 1200.0 * std::log2(1.0 + depMs * 0.001 * 2.0 * kPi * rateHz);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kCentre = 0.70710678118654752;   // the centre voice into each side (−3 dB)
    static constexpr double kEnsNorm = 1.0 / 1.4142135623730951;
    static constexpr int kBuf = 16384;                         // ≥ 21 ms at 768 kHz
    static constexpr int kMask = kBuf - 1;

    struct MixW { double dry, wet; };
    MixW mixWeights(int mode) const {
        if (mode == 2) return { 0.0, 1.0 };                    // Vibrato: wet only
        const double mix = std::clamp((double)get(Mix), 0.0, 1.0);
        return { std::min(1.0, 2.0 * (1.0 - mix)), std::min(1.0, 2.0 * mix) };
    }
    // The LFO phase of voice v (cycles): Ensemble spreads three voices 120° apart; the pair runs
    // the right voice `off` cycles behind the left.
    static double voicePhase(int mode, int v, double off) {
        if (mode == 1) return v / 3.0;
        return v == 0 ? 0.0 : off;
    }
    static const char* voiceName(int mode, int v) {
        if (mode == 1) return v == 0 ? "V1" : v == 1 ? "V2" : "V3";
        return v == 0 ? "L" : "R";
    }

    // TPT SVF high-pass, one channel.
    struct Svf { double s1 = 0.0, s2 = 0.0; };
    double hpTick(Svf& f, double x) {
        const double hp = (x - (hpK_ + hpG_) * f.s1 - f.s2) * hpA1_;
        const double v1 = hpG_ * hp, bp = v1 + f.s1; f.s1 = bp + v1;
        const double v2 = hpG_ * bp, lp = v2 + f.s2; f.s2 = lp + v2;
        return hp;
    }
    // The warmth stage: a one-pole low-pass and a soft saturation, both fading in with Warmth.
    double warm(double& z, double x) const {
        if (warmS_ < 1e-4) { z = x; return x; }
        z += lpA_ * (x - z);
        const double k = 1.0 + 2.5 * warmS_;
        return z + warmS_ * (std::tanh(k * z) / k - z);
    }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float sanitize(float v) { return std::isfinite(v) && std::fabs(v) > 1e-20f ? std::clamp(v, -64.0f, 64.0f) : 0.0f; }

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

    void clearLines() {
        std::memset(lineL_, 0, sizeof lineL_); std::memset(lineR_, 0, sizeof lineR_); w_ = 0;
        hpL_ = hpR_ = Svf{}; lpL_ = lpR_ = 0.0;
    }
    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float lineL_[kBuf] = {}, lineR_[kBuf] = {};
    int w_ = 0;
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    bool snap_ = true;
    int mode_ = 0;
    double fade_ = 1.0;
    double amountS_ = 0.4, fbS_ = 0.0, offS_ = 0.5, widthS_ = 1.0, warmS_ = 0.0, dryS_ = 1.0, wetS_ = 1.0;
    double hpS_ = 0.0, hpG_ = 0.0, hpK_ = 1.4142135623730951, hpA1_ = 1.0, lpA_ = 1.0;
    Svf hpL_, hpR_;
    double lpL_ = 0.0, lpR_ = 0.0;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, clearReq_{false}, resetReq_{false};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> tauA_[3] = {}, centsA_[3] = {};
    std::atomic<float> fracA_{0.0f}, rateA_{0.8f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0}, modeA_{0};
};

} // namespace nota
