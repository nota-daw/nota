// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Phaser (device kind 24, mockup "Nota Phaser") — a chain of first-order all-pass
// stages whose corner frequency an LFO sweeps. Per channel:
//
//   A(z)  = Π_N (a + z⁻¹) / (1 + a·z⁻¹),   a = (tan(π·fc/sr) − 1) / (tan(π·fc/sr) + 1)
//   w     = x + Feedback · y                 (the chain's output fed back, solved delay-free)
//   y     = A · w
//   wet   = ±y                               (the wet polarity follows the feedback's sign)
//   out   = (dry · x + wet_gain · wet) / (dry + wet_gain)
//
// Each stage turns the phase 0 → −π, so N stages reach −Nπ and, summed with the dry signal, cut
// N/2 notches where the phase is an odd multiple of π (positive feedback) — or N/2 − 1 where it
// is an even multiple (negative feedback flips the wet polarity with it, so the notches move and
// widen instead of the comb flattening out). The loop is solved without a unit delay: every stage
// is y = a·x + s, so the chain is y = g·w + S (g = aᴺ) and y = (g·x + S) / (1 − fb·g), which
// makes the response exactly A / (1 − fb·A) — the curve the card draws.
//
//   fc(t) = Center · 2^(3 · Depth · lfo(t))     Center 50 Hz … 5 kHz (exponential), ±3 oct at Depth 1
//   LFO: Sine / Triangle / Saw, free (Rate 0.02 … 8 Hz) or Sync (Division 4/1 … 1/16 incl. dotted
//   and triplet). In Sync the LFO locks to the song position while the transport plays and restarts
//   on the bar (a cycle longer than a bar restarts on the span of bars that holds it); stopped, it
//   free-runs at the synced rate. Stereo offsets the right channel's LFO 0 … 180°.
//   Stages: 2 / 4 / 6 / 8 / 12. A change fades the wet path out and back in (~6 ms) so it doesn't click.
//   Mix: 0 = dry, 0.5 = dry and wet equal (the deepest notches), 1 = wet only (phase shift alone).
//
// Center, Depth, Feedback and Mix glide over ~15 ms so automation doesn't zipper; the coefficients
// are recomputed every 4 samples.
//
// All params are normalized 0..1 and APPEND ONLY. Persistence / automation / clone flow
// generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, fc now per channel, the LFO's place in its
// two-cycle window, the rate, the tempo, the notches / null / peak).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = clear the stages (stops a
// whistling loop), 2 = reset the meters.
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

class Phaser : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Center, Depth, Feedback, Mix, Waveform, Sync, Division, Stereo, Stages, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,   // input peak (300 ms fall), dBFS
        S_OutPeakL,     // output peak, left (300 ms fall), dBFS
        S_OutPeakR,     // output peak, right, dBFS
        S_FcL,          // the left corner frequency now, Hz
        S_FcR,          // the right corner frequency now, Hz
        S_WinPhase,     // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,       // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,          // the tempo the Sync rate follows
        S_Playing,      // 1 while the transport plays
        S_Locked,       // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_CenterHz,     // the Center param, Hz (the middle of the sweep)
        S_FcMin,        // the sweep's lowest / highest corner, Hz
        S_FcMax,
        S_NotchHz,      // the first notch of the left channel now, Hz
        S_NotchMinHz,   // the first notch's sweep, Hz (at fc min … fc max)
        S_NotchMaxHz,
        S_NullDb,       // how deep the notches go, dB
        S_PeakDb,       // the peaks between them, dB
        S_DivBeats,     // the Sync division, beats per cycle
        S_BarBeats,     // beats per bar (the restart span's unit)
        S_Cpu,          // share of real time spent in process()
        S_Signal,       // 1 while signal comes in (above −70 dBFS)
        S_Cycles,       // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        S_Stages,       // the all-pass stages running
        S_Notches,      // the notches they cut (N/2, or N/2 − 1 with negative feedback)
        kTele = 32
    };

    static constexpr int kDivs = 9;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = { 16.0, 8.0, 4.0, 2.0, 1.5, 1.0, 2.0 / 3.0, 0.5, 0.25 };
    static constexpr const char* kDivNames[kDivs] = { "4/1", "2/1", "1/1", "1/2", "1/4D", "1/4", "1/4T", "1/8", "1/16" };
    static constexpr int kDefaultDiv = 2;   // 1/1

    static constexpr int kStageOpts = 5;
    static constexpr int kStageCounts[kStageOpts] = { 2, 4, 6, 8, 12 };
    static constexpr int kMaxStages = 12;

    // Ranges (mirrored by the card and MCP).
    static constexpr double kRateLo = 0.02, kRateHi = 8.0;       // Hz, exponential
    static constexpr double kCenterLo = 50.0, kCenterHi = 5000.0; // Hz, exponential
    static constexpr double kFbMax = 0.95;                       // feedback ±95 %
    static constexpr double kDepthOct = 3.0;                     // fc swings ±3 oct at Depth 1

    Phaser() {
        p_[Rate].store(0.5f);                                     // 0.40 Hz
        p_[Center].store(0.602f);                                 // 800 Hz
        p_[Depth].store(0.70f);                                   // ±2.1 oct
        p_[Feedback].store((float)(0.5 + 0.40 / (2.0 * kFbMax))); // +40 %
        p_[Mix].store(0.50f);
        p_[Waveform].store(0.0f);                                 // Sine
        p_[Sync].store(0.0f);                                     // Free (Hz)
        p_[Division].store((float)kDefaultDiv / (kDivs - 1));
        p_[Stereo].store(0.5f);                                   // 90°
        p_[Stages].store(0.25f);                                  // 4 stages
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        cycle_ = 0; frac_ = 0.0;
        clearStages();
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
        if (clearReq_.exchange(false, std::memory_order_acquire)) clearStages();
        if (resetReq_.exchange(false, std::memory_order_acquire)) resetMeters();

        const int    wave   = waveIndex();
        const double lc     = std::log2(centerHz());
        const double depth  = std::clamp((double)get(Depth), 0.0, 1.0);
        const double fb     = feedback();
        const double mix    = std::clamp((double)get(Mix), 0.0, 1.0);
        const double dryT   = std::min(1.0, 2.0 * (1.0 - mix)), wetT = std::min(1.0, 2.0 * mix);
        const double phOff  = stereoDeg() / 360.0;
        const int    stT    = stageCount();
        const bool   sync   = get(Sync) >= 0.5f;
        const double div    = kDivBeats[divIndex()];
        const double spb    = spb_ > 0.0 ? spb_ : sr_ * 0.5;              // 120 BPM until the engine says otherwise
        const double rateHz = sync ? sr_ / (spb * div) : rateFreeHz();
        const double inc    = rateHz / sr_;
        const bool   locked = sync && playing_ && spb_ > 0.0;
        const double span   = barBeats_ * std::max(1.0, std::ceil(div / barBeats_ - 1e-9));
        const double sm     = 1.0 - std::exp(-1.0 / (0.015 * sr_));        // ~15 ms parameter glide
        const double fade   = 1.0 / (0.003 * sr_);                          // ~3 ms each way for a stage change
        const double fcHi   = std::log2(0.45 * sr_), fcLo = std::log2(10.0);

        const double wetSignT = fb < 0.0 ? -wetT : wetT;
        if (snap_) { lcS_ = lc; depthS_ = depth; fbS_ = fb; dryS_ = dryT; wetS_ = wetSignT; stages_ = stT; stGain_ = 1.0; snap_ = false; }

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
            lcS_    += sm * (lc - lcS_);
            depthS_ += sm * (depth - depthS_);
            fbS_    += sm * (fb - fbS_);
            dryS_   += sm * (dryT - dryS_);
            wetS_   += sm * (wetSignT - wetS_);

            // A stage-count change: fade the wet path out, switch on silence, fade back in.
            if (stT != stages_) {
                stGain_ -= fade;
                if (stGain_ <= 0.0) {
                    stGain_ = 0.0;
                    for (int s = std::min(stages_, stT); s < kMaxStages; ++s) sL_[s] = sR_[s] = 0.0;
                    stages_ = stT;
                }
            } else if (stGain_ < 1.0) stGain_ = std::min(1.0, stGain_ + fade);

            if ((ctl_++ & 3) == 0) {
                const double swing = kDepthOct * depthS_;
                fcL_ = std::exp2(std::clamp(lcS_ + swing * lfo(wave, frac_), fcLo, fcHi));
                fcR_ = std::exp2(std::clamp(lcS_ + swing * lfo(wave, frac_ + phOff), fcLo, fcHi));
                aL_ = coef(fcL_); aR_ = coef(fcR_);
            }

            const float xL = buf[i * 2], xR = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(xL), std::fabs(xR)));
            const double yL = chain(sL_, aL_, xL, fbS_);
            const double yR = chain(sR_, aR_, xR, fbS_);

            const double wg   = wetS_ * stGain_;
            const double norm = 1.0 / std::max(dryS_ + std::fabs(wetS_), 1e-3);
            const float oL = (float)((dryS_ * xL + wg * yL) * norm);
            const float oR = (float)((dryS_ * xR + wg * yR) * norm);
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
            fcLA_.store((float)fcL_, std::memory_order_relaxed);
            fcRA_.store((float)fcR_, std::memory_order_relaxed);
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
        const double  sr  = srA_.load(std::memory_order_relaxed);
        const double  ch  = centerHz(), dep = std::clamp((double)get(Depth), 0.0, 1.0);
        const double  fb  = feedback();
        const int     n   = stageCount();
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeakL]   = outPkLA_.load(std::memory_order_relaxed);
        t[S_OutPeakR]   = outPkRA_.load(std::memory_order_relaxed);
        t[S_FcL]        = fcLA_.load(std::memory_order_relaxed);
        t[S_FcR]        = fcRA_.load(std::memory_order_relaxed);
        if (t[S_FcL] <= 0.0f) t[S_FcL] = t[S_FcR] = (float)ch;   // before the first block
        t[S_WinPhase]   = (float)((double)(cyc - base) + fr);
        t[S_RateHz]     = rateA_.load(std::memory_order_relaxed);
        t[S_Bpm]        = bpmA_.load(std::memory_order_relaxed);
        t[S_Playing]    = (float)playingA_.load(std::memory_order_relaxed);
        t[S_Locked]     = (float)lockedA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = (float)sr;
        t[S_CenterHz]   = (float)ch;
        t[S_FcMin]      = (float)(ch * std::exp2(-kDepthOct * dep));
        t[S_FcMax]      = (float)(ch * std::exp2(kDepthOct * dep));
        t[S_NotchHz]    = (float)firstNotchHz(t[S_FcL], n, fb, sr);
        t[S_NotchMinHz] = (float)firstNotchHz(t[S_FcMin], n, fb, sr);
        t[S_NotchMaxHz] = (float)firstNotchHz(t[S_FcMax], n, fb, sr);
        const double mix = std::clamp((double)get(Mix), 0.0, 1.0);
        const double dry = std::min(1.0, 2.0 * (1.0 - mix)), wet = std::min(1.0, 2.0 * mix);
        t[S_NullDb]     = (float)(20.0 * std::log10(std::max(mag(dry, wet, fb, fb < 0.0 ? 2.0 * kPi : kPi), 1e-4)));
        t[S_PeakDb]     = (float)(20.0 * std::log10(std::max(mag(dry, wet, fb, fb >= 0.0 ? 0.0 : kPi), 1e-4)));
        t[S_DivBeats]   = (float)kDivBeats[divIndex()];
        t[S_BarBeats]   = barA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Signal]     = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
        t[S_Cycles]     = (float)cyc;
        t[S_Stages]     = (float)n;
        t[S_Notches]    = (float)notchCount(n, fb);
        int32_t k = 0;
        for (int i = 0; i < kTele && k < maxSamples; ++i) out[k++] = t[i];
        return k;
    }

    const char* displayName() const override { return "Nota Phaser"; }
    int32_t     builtinKind() const override { return 24; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Rate: return "Rate"; case Center: return "Center"; case Depth: return "Depth";
            case Feedback: return "Feedback"; case Mix: return "Mix"; case Waveform: return "Waveform";
            case Sync: return "Sync"; case Division: return "Division"; case Stereo: return "Stereo";
            case Stages: return "Stages";
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
        char b[1600];
        if (id == 0) {
            const int n = stageCount();
            std::string s;
            std::snprintf(b, sizeof b, "%d stages (%d notches) - %s", n, notchCount(n, feedback()), waves[waveIndex()]);
            s += b;
            if (get(Sync) >= 0.5f) std::snprintf(b, sizeof b, " - sync %s", kDivNames[divIndex()]);
            else std::snprintf(b, sizeof b, " - %.2f Hz", rateFreeHz());
            s += b;
            std::snprintf(b, sizeof b, " - stereo %.0f deg - center %.0f Hz - depth +/-%.1f oct - feedback %+.0f %% (%s) - mix %.0f %%",
                          stereoDeg(), centerHz(), kDepthOct * get(Depth), feedback() * 100.0, modeWord(), get(Mix) * 100.0f);
            s += b;
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::snprintf(b, sizeof b,
                          "LFO %.2f Hz (period %.0f ms)%s - window phase %.2f of 2 cycles - fc L %.0f Hz, R %.0f Hz "
                          "(sweep %.0f .. %.0f Hz) - %d stages, %d notches - first notch %.0f Hz (sweeps %.0f .. %.0f Hz), "
                          "%.1f dB deep - peaks %+.1f dB - in %s dBFS - out L %s, R %s dBFS - %.1f BPM, bar %.2g beats - "
                          "transport %s - CPU %.2f %%",
                          sc[S_RateHz], 1000.0 / std::max(1e-3f, sc[S_RateHz]),
                          sc[S_Locked] > 0.5f ? " locked to the song" : get(Sync) >= 0.5f ? " free-running at the synced rate" : "",
                          sc[S_WinPhase], sc[S_FcL], sc[S_FcR], sc[S_FcMin], sc[S_FcMax], (int)sc[S_Stages], (int)sc[S_Notches],
                          sc[S_NotchHz], sc[S_NotchMinHz], sc[S_NotchMaxHz], sc[S_NullDb], sc[S_PeakDb], lv(sc[S_InPeak]).c_str(),
                          lv(sc[S_OutPeakL]).c_str(), lv(sc[S_OutPeakR]).c_str(), sc[S_Bpm], sc[S_BarBeats],
                          sc[S_Playing] > 0.5f ? "playing" : "stopped", sc[S_Cpu] * 100.0f);
            return b;
        }
        if (id == 2) {
            std::string divs, sts;
            for (int i = 0; i < kDivs; ++i) {
                std::snprintf(b, sizeof b, "%s%.3f = %s", i ? ", " : "", (double)i / (kDivs - 1), kDivNames[i]);
                divs += b;
            }
            for (int i = 0; i < kStageOpts; ++i) {
                std::snprintf(b, sizeof b, "%s%.2f = %d", i ? ", " : "", (double)i / (kStageOpts - 1), kStageCounts[i]);
                sts += b;
            }
            return "All params are normalized 0..1. Rate: the free LFO rate, exponential 0.02 Hz (0) .. 8 Hz (1) "
                   "(0.269 = 0.1 Hz, 0.384 = 0.2 Hz, 0.5 = 0.4 Hz, 0.537 = 0.5 Hz, 0.653 = 1 Hz, 0.769 = 2 Hz, 0.884 = 4 Hz). "
                   "Center: the middle of the sweep, exponential 50 Hz (0) .. 5 kHz (1) (0.151 = 100 Hz, 0.301 = 200 Hz, "
                   "0.5 = 500 Hz, 0.602 = 800 Hz, 0.65 = 1 kHz, 0.801 = 2 kHz, 1 = 5 kHz). Depth: how far the LFO swings the "
                   "corner, 0..1 = 0 .. +/-3 octaves - fc = Center * 2^(3 * Depth * lfo). Feedback: bipolar, (v - 0.5) * 1.9 = "
                   "-95 % .. +95 % (0.5 = none, 0.711 = +40 %, 0.184 = -60 %); positive sharpens the peaks between the notches, "
                   "negative also flips the wet polarity so the notches move and widen (one fewer); past 85 % the phaser "
                   "whistles. Mix: 0 = dry, 0.5 = dry and wet equal (the deepest notches), 1 = wet only (phase shift, almost no "
                   "notches). Waveform: 0 Sine, 0.5 Triangle, 1 Saw. Stereo: the right LFO's offset from the left, 0..1 = "
                   "0..180 deg (0 = mono sweep, 0.5 = 90 deg, 1 = opposite). Stages (all-pass stages; notches = stages / 2): "
                   + sts + ". Sync: >= 0.5 takes the rate from the tempo (Division) and locks the LFO to the song position "
                   "while the transport plays, restarting on the bar. Division (with Sync): " + divs + ".";
        }
        return {};
    }

    // ---- shared with the card / MCP through the guide above ----------------------------
    int waveIndex() const { return std::clamp((int)std::lround(get(Waveform) * 2.0f), 0, 2); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }
    int stageIndex() const { return std::clamp((int)std::lround(get(Stages) * (kStageOpts - 1)), 0, kStageOpts - 1); }
    int stageCount() const { return kStageCounts[stageIndex()]; }
    double rateFreeHz() const { return expMap(get(Rate), kRateLo, kRateHi); }
    double centerHz() const { return expMap(get(Center), kCenterLo, kCenterHi); }
    double feedback() const { return (std::clamp((double)get(Feedback), 0.0, 1.0) - 0.5) * 2.0 * kFbMax; }
    double stereoDeg() const { return std::clamp((double)get(Stereo), 0.0, 1.0) * 180.0; }
    static int notchCount(int n, double fb) { return fb < 0.0 ? n / 2 - 1 : n / 2; }

    // |dry ± wet·H(θ)| / (dry + wet), H = e^{−jθ} / (1 − fb·e^{−jθ}), θ = the chain's phase lag —
    // the phaser's magnitude (the wet polarity follows the feedback's sign, as in process()).
    static double mag(double dry, double wet, double fb, double th) {
        const double w = fb < 0.0 ? -wet : wet, c = std::cos(th), s = std::sin(th);
        const double nr = w * c, ni = -w * s, dr = 1.0 - fb * c, di = fb * s, den = dr * dr + di * di;
        const double re = dry + (nr * dr + ni * di) / den, im = (ni * dr - nr * di) / den;
        return std::sqrt(re * re + im * im) / std::max(dry + wet, 0.001);
    }
    // The chain's phase lag at f for a corner fc: N · 2·atan(tan(πf/sr) / tan(πfc/sr)) (bilinear all-pass).
    static double phaseLag(double f, double fc, int n, double sr) {
        const double nyq = 0.5 * sr;
        const double tf = std::tan(kPi * std::min(f, nyq * 0.9999) / sr), tc = std::tan(kPi * std::min(fc, nyq * 0.9999) / sr);
        return n * 2.0 * std::atan(tf / std::max(tc, 1e-9));
    }
    // The frequency where the chain's lag reaches θ (inverse of phaseLag).
    static double freqAtLag(double th, double fc, int n, double sr) {
        const double tc = std::tan(kPi * std::min(fc, 0.49999 * sr) / sr);
        return sr / kPi * std::atan(tc * std::tan(th / (2.0 * n)));
    }
    // The first notch: lag π (positive feedback) or 2π (negative).
    static double firstNotchHz(double fc, int n, double fb, double sr) {
        return freqAtLag(fb < 0.0 ? 2.0 * kPi : kPi, fc, n, sr);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;

    const char* modeWord() const {
        const double fb = feedback();
        return std::fabs(fb) >= 0.85 ? "resonant" : fb <= -0.15 ? "negative" : fb >= 0.15 ? "positive" : "clean";
    }

    double coef(double fc) const {
        const double t = std::tan(kPi * fc / sr_);
        return (t - 1.0) / (t + 1.0);
    }

    // One channel through the chain with its feedback loop solved delay-free: each stage is
    // y = a·x + s (s' = x − a·y), so the chain is y = g·w + S and w = x + fb·y.
    double chain(double* s, double a, double x, double fb) const {
        const int n = stages_;
        double g = 1.0, S = 0.0;
        for (int k = 0; k < n; ++k) { S = a * S + s[k]; g *= a; }
        const double y = (g * x + S) / (1.0 - fb * g);
        double v = x + fb * y;
        for (int k = 0; k < n; ++k) {
            const double o = a * v + s[k];
            s[k] = sanitize(v - a * o);
            v = o;
        }
        return v;
    }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static double sanitize(double v) { return std::isfinite(v) && std::fabs(v) > 1e-20 ? std::clamp(v, -64.0, 64.0) : 0.0; }

    // LFO in [−1, 1] at phase p (cycles; wraps) — the mockup's shapes: all start at 0 rising.
    static double lfo(int wave, double p) {
        const double f = p - std::floor(p);
        if (wave == 0) return std::sin(2.0 * kPi * f);
        if (wave == 1) return f < 0.25 ? 4.0 * f : f < 0.75 ? 2.0 - 4.0 * f : 4.0 * f - 4.0;
        const double g = f + 0.5;
        return 2.0 * (g - std::floor(g)) - 1.0;
    }

    void clearStages() { for (int k = 0; k < kMaxStages; ++k) sL_[k] = sR_[k] = 0.0; }
    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    double sL_[kMaxStages] = {}, sR_[kMaxStages] = {};
    double aL_ = 0.0, aR_ = 0.0, fcL_ = 800.0, fcR_ = 800.0;
    uint32_t ctl_ = 0;
    int stages_ = 4;
    double stGain_ = 1.0;
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    bool snap_ = true;
    double lcS_ = 9.64, depthS_ = 0.7, fbS_ = 0.4, dryS_ = 1.0, wetS_ = 1.0;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, clearReq_{false}, resetReq_{false};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> fcLA_{0.0f}, fcRA_{0.0f}, fracA_{0.0f}, rateA_{0.4f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0};
};

} // namespace nota
