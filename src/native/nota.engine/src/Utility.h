// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Utility (device kind 4) — routing, stereo field and levels (the "Nota
// Utility" mockup, 700 × 260). A stereo utility: channel mode (Stereo / Left / Right /
// Swap), mid/side width with two laws (L/R: the side is scaled, wider gets louder; M/S:
// mid and side are traded, 200 % = side only), mono below a cutoff at 6 / 12 / 24 dB/oct,
// L/R balance, per-channel phase invert, mute, output gain; level matching (a one-shot
// Gain match and a continuous Auto Match, to the input's level or to a Target in the
// meter's unit — LUFS-S, sample peak or RMS) and a zero-latency true-peak safety limiter.
//
//   in ─▶ meters ─▶ channel mode ─▶ M/S ─▶ mono below (side HP) ─▶ width ─▶ L/R
//      ─▶ balance ─▶ phase ─▶ gain · auto match · mute ─▶ TP limiter ─▶ meters ─▶ out
//
// Header-only, allocation-free after setSampleRate. Params keep real units (Gain dB,
// Balance −1..1, Width %, Mono Freq Hz, choices as indices, Target / TP Ceiling dB); the
// first nine are the original layout with their original defaults, the rest are appended
// and default to the old sound, so older projects and presets load unchanged.
//
// Telemetry (scopeRead): kTele live values; kHist-point level histories (in / out ×
// LUFS-S / peak / RMS, 100 ms apart, oldest first); kBands per-band readings of the output
// (pan, width share, correlation, level, the configured width) from an FFT over the last
// second; the configured width over frequency on a kResp-point log grid. A read shorter
// than kBandAt skips the FFT. deviceText: 0 status line, 1 live reading, 2 parameter guide.
// deviceAction: 0 gain match, 1 reset the meters (histories, holds, the auto-match ride).

#pragma once

#include "Device.h"

#include "signalsmith-linear/fft.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <complex>
#include <cstdint>
#include <cstdio>
#include <mutex>
#include <string>
#include <vector>

namespace nota {

class Utility : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Gain = 0, Balance, Width, ChannelMode, MonoFreq, MonoBelow, Mute, InvertL, InvertR,
           WidthMode,   // 0 L/R (side × width), 1 M/S (mid ↔ side balance; 200 % = side only)
           MonoSlope,   // 0 6 dB/oct (the original one-pole), 1 12, 2 24
           AutoMatch,   // the output rides to the reference (the input's level or Target)
           MatchTo,     // 0 input, 1 Target
           Target,      // −36..0, in the Meter's unit (LUFS / dBFS peak / dBFS RMS)
           Meter,       // 0 LUFS-S, 1 Peak, 2 RMS
           TpLimit,     // true-peak safety limiter at TP Ceiling
           TpCeiling,   // −6..0 dBTP
           kNumParams };

    // Telemetry slots (scopeRead). Levels in dB (−120 = silence).
    enum {
        S_InPk = 0,    // input peak (decaying ~300 ms), linear
        S_OutPk,       // output peak (decaying), linear
        S_Cpu,         // share of real time spent in process()
        S_SampleRate,
        S_Corr,        // output L/R correlation −1..+1
        S_InLufs,      // short-term loudness (3 s), LUFS
        S_OutLufs,
        S_InRms,       // RMS over 300 ms, dBFS
        S_OutRms,
        S_InPeak,      // sample peak held over 1 s, dBFS
        S_OutPeak,
        S_TruePeak,    // output true peak held over 3 s, dBTP
        S_InMom,       // momentary loudness (400 ms), LUFS
        S_OutMom,
        S_AutoDb,      // the auto-match gain now, dB
        S_LimDb,       // limiter gain reduction in the last 100 ms, dB (≥ 0)
        S_InLevel,     // input / output in the Meter's unit (LUFS-S / 1 s peak / 300 ms RMS)
        S_OutLevel,
        S_InMatch,     // input / output over 3 s in the Meter's unit (what Gain match uses)
        S_OutMatch,
        S_EnergyBal,   // output L vs R energy, dB (+ = left louder)
        S_WidthNow,    // measured output width: side / mid amplitude, %
        S_EffWidth,    // configured width above the mono cutoff, % (the width law applied)
        S_MonoHz,      // the mono cutoff (0 when Mono Below is off)
        S_Meter,
        S_HistN,
        S_HistSec,
        S_Bands,
        S_BandLo,
        S_BandHi,
        S_RespN,
        S_RespLo,
        S_RespHi,
        S_Analysed,    // 1 when the band block below is filled
        S_InTruePeak,  // input sample peak over 3 s, dBFS (the headroom reference)
        S_OutPreMatch, // the output over 3 s before Gain and the auto-match ride, in the Meter's unit
        S_Reserved1, S_Reserved2, S_Reserved3, S_Reserved4,
        kTele = 48
    };
    static constexpr int kHist = 80;                  // 8 s of 100 ms blocks
    static constexpr int kHistSeries = 6;
    enum { H_InLufs = 0, H_OutLufs, H_InPeak, H_OutPeak, H_InRms, H_OutRms };
    static constexpr int kBands = 32;
    static constexpr int kBandSeries = 5;
    enum { B_Pan = 0,     // −1 left … +1 right: where the band's energy sits
           B_Width,       // side share sqrt(S² / (M² + S²)): 0 mono, .71 uncorrelated, 1 anti-phase
           B_Corr,        // L/R correlation in the band
           B_Level,       // band level, dB re full scale
           B_Set };       // the configured width at the band centre, %
    static constexpr int kResp = 96;
    static constexpr double kBandLo = 30.0, kBandHi = 16000.0, kRespLo = 20.0, kRespHi = 20000.0;
    static constexpr int kHistAt = kTele;
    static constexpr int kBandAt = kHistAt + kHistSeries * kHist;
    static constexpr int kRespAt = kBandAt + kBandSeries * kBands;
    static constexpr int kScopeTotal = kRespAt + kResp;
    enum { A_GainMatch = 0, A_ResetMeters = 1 };      // deviceAction ids

    Utility() {
        p_[Gain].store(0.0f);        // 0 dB
        p_[Balance].store(0.0f);     // centre
        p_[Width].store(100.0f);     // 100 %
        p_[ChannelMode].store(0.0f); // Stereo
        p_[MonoFreq].store(120.0f);  // 120 Hz
        p_[MonoBelow].store(0.0f);   // off
        p_[Mute].store(0.0f);
        p_[InvertL].store(0.0f);
        p_[InvertR].store(0.0f);
        p_[WidthMode].store(0.0f);   // L/R — the original width law
        p_[MonoSlope].store(0.0f);   // 6 dB/oct — the original one-pole
        p_[AutoMatch].store(0.0f);
        p_[MatchTo].store(0.0f);     // the input
        p_[Target].store(-14.0f);
        p_[Meter].store(0.0f);       // LUFS-S
        p_[TpLimit].store(0.0f);
        p_[TpCeiling].store(-1.0f);
        clearHistory();
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        sr_ = sr > 0 ? sr : 44100.0;
        computeKWeighting();
        blockLen_ = std::max(1, (int)std::lround(sr_ * 0.1));
        fftN_ = sr_ > 50000.0 ? 8192 : 4096;
        size_t ring = 1;
        while (ring < (size_t)(sr_ * 1.1) + (size_t)fftN_) ring <<= 1;
        ringL_.assign(ring, 0.0f); ringR_.assign(ring, 0.0f);
        ringMask_ = (uint32_t)(ring - 1);
        fft_.resize((size_t)fftN_);
        win_.assign((size_t)fftN_, 0.0f);
        tl_.assign((size_t)fftN_, 0.0f); tr_.assign((size_t)fftN_, 0.0f);
        fl_.assign((size_t)fftN_ / 2, {}); fr_.assign((size_t)fftN_ / 2, {});
        winSum_ = 0.0;
        for (int i = 0; i < fftN_; ++i) {
            win_[(size_t)i] = (float)(0.5 - 0.5 * std::cos(2.0 * kPi * i / fftN_));
            winSum_ += win_[(size_t)i];
        }
        lastAnaS_ = -1.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        curMonoHz_ = -1.0f;
        first_ = true;
        resetDsp();
        resetMeters();
    }

    void deviceAction(int32_t id, int32_t, float) override {
        if (id == A_GainMatch) gainMatch();
        else if (id == A_ResetMeters) resetReq_.store(true, std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetReq_.exchange(false, std::memory_order_relaxed)) resetMeters();

        const int   mode   = std::clamp((int)std::lround(get(ChannelMode)), 0, 3);
        const bool  mute   = get(Mute) >= 0.5f;
        const bool  invL   = get(InvertL) >= 0.5f;
        const bool  invR   = get(InvertR) >= 0.5f;
        const bool  monoOn = get(MonoBelow) >= 0.5f;
        const bool  tpOn   = get(TpLimit) >= 0.5f;
        const float bal    = std::clamp(get(Balance), -1.0f, 1.0f);
        const float gainT  = mute ? 0.0f : std::pow(10.0f, std::clamp(get(Gain), -24.0f, 24.0f) / 20.0f);
        const float ceil   = std::pow(10.0f, std::clamp(get(TpCeiling), -6.0f, 0.0f) / 20.0f);
        float gm, gs;
        widthGains(gm, gs);
        // Constant-power-ish linear balance: attenuate the opposite side.
        const float gLT = bal <= 0.0f ? 1.0f : 1.0f - bal;
        const float gRT = bal >= 0.0f ? 1.0f : 1.0f + bal;
        const float autoT = autoOn() ? std::pow(10.0f, autoDb_ / 20.0f) : 1.0f;
        if (!autoOn()) autoDb_ = 0.0f;

        const float monoHz = std::clamp(get(MonoFreq), 20.0f, 2000.0f);
        const int   slope  = std::clamp((int)std::lround(get(MonoSlope)), 0, 2);
        if (monoHz != curMonoHz_ || slope != curSlope_) setCrossover(monoHz, slope);

        if (first_) { sGain_ = gainT; sGL_ = gLT; sGR_ = gRT; sGm_ = gm; sGs_ = gs; sAuto_ = autoT; first_ = false; }
        const float sm = smCoef_, sa = autoCoef_;
        const float rel = relCoef_;

        float inPk = 0, outPk = 0;
        double sLL = 0, sRR = 0, sLR = 0, sMM = 0, sSS = 0;
        const bool ringOk = !ringL_.empty();
        uint32_t w = ringW_.load(std::memory_order_relaxed);

        for (int32_t i = 0; i < frames; ++i) {
            float l = buf[i * 2], r = buf[i * 2 + 1];
            const float al = std::fabs(l), ar = std::fabs(r);
            inPk = std::max(inPk, std::max(al, ar));
            { const float kl = kweight(kIn_[0], l), kr = kweight(kIn_[1], r);
              blkInK_ += (double)kl * kl + (double)kr * kr; }
            blkInSq_ += (double)l * l + (double)r * r;
            blkInPk_ = std::max(blkInPk_, std::max(al, ar));

            switch (mode) {                       // channel mode
                case 1: r = l; break;             // Left  → both
                case 2: l = r; break;             // Right → both
                case 3: { float t = l; l = r; r = t; break; }  // Swap
                default: break;                   // Stereo
            }

            sGm_ += (gm - sGm_) * sm;  sGs_ += (gs - sGs_) * sm;
            sGL_ += (gLT - sGL_) * sm; sGR_ += (gRT - sGR_) * sm;
            sGain_ += (gainT - sGain_) * sm;
            sAuto_ += (autoT - sAuto_) * sa;

            float mid = 0.5f * (l + r), side = 0.5f * (l - r);
            if (monoOn) side = crossover(side);   // bass → mono
            mid *= sGm_; side *= sGs_;
            l = mid + side; r = mid - side;
            l *= sGL_; r *= sGR_;                  // balance
            if (invL) l = -l;
            if (invR) r = -r;
            const float g = sGain_ * sAuto_;
            l *= g; r *= g;                        // output gain / auto match / mute
            blkG2_ += (double)g * g;

            if (tpOn) {                            // zero-latency true-peak safety limiter
                const float est = std::max(isp(limH_[0], l), isp(limH_[1], r));
                limEnv_ += (1.0f - limEnv_) * rel;
                if (est * limEnv_ > ceil) limEnv_ = ceil / est;
                l *= limEnv_; r *= limEnv_;
                l = std::clamp(l, -ceil, ceil); r = std::clamp(r, -ceil, ceil);
                blkLimMin_ = std::min(blkLimMin_, limEnv_);
            } else {
                limEnv_ = 1.0f;
            }
            buf[i * 2] = l; buf[i * 2 + 1] = r;

            const float ol = std::fabs(l), orr = std::fabs(r);
            outPk = std::max(outPk, std::max(ol, orr));
            sLL += (double)l * l; sRR += (double)r * r; sLR += (double)l * r;
            const double m = 0.5 * ((double)l + r), s = 0.5 * ((double)l - r);
            sMM += m * m; sSS += s * s;
            { const float kl = kweight(kOut_[0], l), kr = kweight(kOut_[1], r);
              blkOutK_ += (double)kl * kl + (double)kr * kr; }
            blkOutSq_ += (double)l * l + (double)r * r;
            blkOutPk_ = std::max(blkOutPk_, std::max(ol, orr));
            blkOutTp_ = std::max(blkOutTp_, std::max(truePeak(tpM_[0], l), truePeak(tpM_[1], r)));

            if (ringOk) { ringL_[w & ringMask_] = l; ringR_[w & ringMask_] = r; }
            ++w;
            if (++blkCount_ >= blockLen_) endBlock();
        }
        ringW_.store(w, std::memory_order_release);

        // Correlation, balance and width of the output; hold the last values while silent.
        if (sLL > 1e-9 && sRR > 1e-9) {
            const float c = std::clamp((float)(sLR / std::sqrt(sLL * sRR)), -1.0f, 1.0f);
            corr_.store(corr_.load(std::memory_order_relaxed) * 0.8f + c * 0.2f, std::memory_order_relaxed);
        }
        if (sLL + sRR > 1e-9) {
            const float a = frames > 0 ? 1.0f - std::exp(-(float)frames / (float)(0.5 * sr_)) : 0.0f;
            emaL_ += a * ((float)sLL / std::max(1, frames) - emaL_);
            emaR_ += a * ((float)sRR / std::max(1, frames) - emaR_);
            emaM_ += a * ((float)sMM / std::max(1, frames) - emaM_);
            emaS_ += a * ((float)sSS / std::max(1, frames) - emaS_);
            balDbA_.store(emaL_ > 1e-12f && emaR_ > 1e-12f ? 10.0f * std::log10(emaL_ / emaR_) : 0.0f, std::memory_order_relaxed);
            widthA_.store(emaM_ > 1e-12f ? 100.0f * std::sqrt(emaS_ / emaM_) : (emaS_ > 1e-12f ? 400.0f : 0.0f), std::memory_order_relaxed);
        }
        const float fall = std::exp(-(float)frames / (float)(0.3 * sr_));   // ~300 ms peak fall
        inPkA_.store(std::max(inPk, inPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
        outPkA_.store(std::max(outPk, outPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    // Telemetry, the histories, the bands and the configured width (see the header note).
    // Writes as much of that layout as fits in maxSamples. Torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        t[S_InPk] = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPk] = outPkA_.load(std::memory_order_relaxed);
        t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Corr] = corr_.load(std::memory_order_relaxed);
        t[S_InLufs] = inLufsA_.load(std::memory_order_relaxed);
        t[S_OutLufs] = outLufsA_.load(std::memory_order_relaxed);
        t[S_InRms] = inRmsA_.load(std::memory_order_relaxed);
        t[S_OutRms] = outRmsA_.load(std::memory_order_relaxed);
        t[S_InPeak] = inPeak1A_.load(std::memory_order_relaxed);
        t[S_OutPeak] = outPeak1A_.load(std::memory_order_relaxed);
        t[S_TruePeak] = truePk3A_.load(std::memory_order_relaxed);
        t[S_InMom] = inMomA_.load(std::memory_order_relaxed);
        t[S_OutMom] = outMomA_.load(std::memory_order_relaxed);
        t[S_AutoDb] = autoOn() ? autoDbA_.load(std::memory_order_relaxed) : 0.0f;
        t[S_LimDb] = get(TpLimit) >= 0.5f ? limDbA_.load(std::memory_order_relaxed) : 0.0f;
        const int meter = meterIdx();
        t[S_InLevel] = levelIn(meter);
        t[S_OutLevel] = levelOut(meter);
        t[S_InMatch] = matchIn(meter);
        t[S_OutMatch] = matchOut(meter);
        t[S_EnergyBal] = balDbA_.load(std::memory_order_relaxed);
        t[S_WidthNow] = widthA_.load(std::memory_order_relaxed);
        t[S_EffWidth] = (float)effWidth(1e9);
        t[S_MonoHz] = get(MonoBelow) >= 0.5f ? std::clamp(get(MonoFreq), 20.0f, 2000.0f) : 0.0f;
        t[S_Meter] = (float)meter;
        t[S_HistN] = (float)kHist;
        t[S_HistSec] = kHist * 0.1f;
        t[S_Bands] = (float)kBands;
        t[S_BandLo] = (float)kBandLo;
        t[S_BandHi] = (float)kBandHi;
        t[S_RespN] = (float)kResp;
        t[S_RespLo] = (float)kRespLo;
        t[S_RespHi] = (float)kRespHi;
        t[S_InTruePeak] = inPeak3A_.load(std::memory_order_relaxed);
        t[S_OutPreMatch] = matchPre(meter);

        const bool bands = maxSamples >= kBandAt + kBandSeries * kBands;
        if (bands) t[S_Analysed] = analyse() ? 1.0f : 0.0f;

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        const int hw = histW_.load(std::memory_order_acquire);
        for (int s = 0; s < kHistSeries; ++s)
            for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = hist_[s][(size_t)((hw + k) % kHist)];
        if (!bands) return n;
        {
            std::lock_guard<std::mutex> lock(mx_);
            for (int s = 0; s < kBandSeries; ++s)
                for (int b = 0; b < kBands && n < maxSamples; ++b) {
                    float v = 0.0f;
                    switch (s) {
                        case B_Pan: v = bandPan_[b]; break;
                        case B_Width: v = bandWidth_[b]; break;
                        case B_Corr: v = bandCorr_[b]; break;
                        case B_Level: v = bandLevel_[b]; break;
                        default: v = (float)effWidth(bandHz(b)); break;
                    }
                    out[n++] = v;
                }
        }
        for (int k = 0; k < kResp && n < maxSamples; ++k) out[n++] = (float)effWidth(respHz(k));
        return n;
    }

    const char* displayName() const override { return "Nota Utility"; }
    int32_t     builtinKind() const override { return 4; }
    int32_t     paramCount() const override { return kNumParams; }

    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = { "Gain", "Balance", "Width", "Channel Mode",
                                              "Mono Freq", "Mono Below", "Mute", "Invert L", "Invert R",
                                              "Width Mode", "Mono Slope", "Auto Match", "Match To", "Target", "Meter",
                                              "TP Limit", "TP Ceiling" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) {
            case Gain: return -24.0f; case Balance: return -1.0f; case MonoFreq: return 20.0f;
            case Target: return -36.0f; case TpCeiling: return -6.0f;
            default: return 0.0f;
        }
    }
    float paramMax(int32_t i) const override {
        switch (i) {
            case Gain: return 24.0f; case Balance: return 1.0f; case Width: return 400.0f;
            case ChannelMode: return 3.0f; case MonoFreq: return 2000.0f;
            case MonoSlope: case Meter: return 2.0f;
            case Target: case TpCeiling: return 0.0f;
            default: return 1.0f;
        }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v)) p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        char b[768];
        static const char* kMode[4] = { "Stereo", "Left", "Right", "Swap" };
        static const char* kMeterName[3] = { "LUFS-S", "Peak", "RMS" };
        static const char* kUnit[3] = { "LUFS", "dBFS", "dBFS" };
        const int meter = meterIdx();
        if (id == 0) {
            std::string s = kMode[std::clamp((int)std::lround(get(ChannelMode)), 0, 3)];
            std::snprintf(b, sizeof b, " - %s width %.0f %%", get(WidthMode) >= 0.5f ? "M/S" : "L/R", get(Width));
            s += b;
            if (get(MonoBelow) >= 0.5f) {
                static const int kSl[3] = { 6, 12, 24 };
                std::snprintf(b, sizeof b, " - mono below %.0f Hz (%d dB/oct)", get(MonoFreq), kSl[std::clamp((int)std::lround(get(MonoSlope)), 0, 2)]);
                s += b;
            }
            const float bal = get(Balance);
            if (std::fabs(bal) > 0.005f) { std::snprintf(b, sizeof b, " - balance %s %.0f %%", bal < 0 ? "L" : "R", std::fabs(bal) * 100.0f); s += b; }
            if (get(InvertL) >= 0.5f) s += " - phase L inverted";
            if (get(InvertR) >= 0.5f) s += " - phase R inverted";
            if (get(Mute) >= 0.5f) s += " - muted";
            std::snprintf(b, sizeof b, " - gain %+.1f dB", get(Gain)); s += b;
            if (autoOn()) {
                if (get(MatchTo) >= 0.5f) std::snprintf(b, sizeof b, " - auto match to %.1f %s (%s)", get(Target), kUnit[meter], kMeterName[meter]);
                else std::snprintf(b, sizeof b, " - auto match to the input (%s)", kMeterName[meter]);
                s += b;
            }
            if (get(TpLimit) >= 0.5f) { std::snprintf(b, sizeof b, " - true-peak limit %.1f dBTP", get(TpCeiling)); s += b; }
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            char balText[32];
            if (std::fabs(sc[S_EnergyBal]) < 0.25f) std::snprintf(balText, sizeof balText, "L=R");
            else std::snprintf(balText, sizeof balText, "%s +%.1f dB", sc[S_EnergyBal] > 0 ? "L" : "R", std::fabs(sc[S_EnergyBal]));
            std::snprintf(b, sizeof b,
                          "in %.1f LUFS-S / %.1f dBFS peak / %.1f dBFS RMS - out %.1f LUFS-S / %.1f dBFS peak / %.1f dBFS RMS - "
                          "true peak %.1f dBTP - %s delta %+.1f dB - correlation %+.2f - width %.0f %% - balance %s - "
                          "auto match %+.1f dB - limiter %.1f dB",
                          sc[S_InLufs], sc[S_InPeak], sc[S_InRms], sc[S_OutLufs], sc[S_OutPeak], sc[S_OutRms], sc[S_TruePeak],
                          kMeterName[meter], sc[S_OutLevel] > -119.0f && sc[S_InLevel] > -119.0f ? sc[S_OutLevel] - sc[S_InLevel] : 0.0f,
                          sc[S_Corr], sc[S_WidthNow],
                          balText, sc[S_AutoDb], sc[S_LimDb]);
            return b;
        }
        if (id == 2) {
            return "Gain -24..24 dB. Balance -1 (left) .. 1 (right). Width 0..400 %, 100 = unchanged. Channel Mode: 0 Stereo, 1 Left "
                   "(left on both), 2 Right, 3 Swap. Mono Freq 20..2000 Hz, Mono Below 0/1 (the side below it is removed). Mute, "
                   "Invert L, Invert R: 0/1. Width Mode: 0 L/R (the side is scaled by Width, wider gets louder), 1 M/S (mid and side "
                   "are traded: below 100 % the side drops, above it the mid drops, 200 % = side only). Mono Slope: 0 = 6, 1 = 12, "
                   "2 = 24 dB/oct. Auto Match 0/1: the output level rides to the reference. Match To: 0 the input's level, 1 Target. "
                   "Target -36..0 in the Meter's unit. Meter: 0 LUFS-S (short-term loudness), 1 Peak (sample peak, dBFS), 2 RMS "
                   "(dBFS). TP Limit 0/1: a zero-latency true-peak safety limiter at TP Ceiling (-6..0 dBTP). Toggles: >= 0.5 = on. "
                   "device_action 0 = gain match once (Gain moves so the output matches the reference over the last 3 s), "
                   "1 = reset the meters.";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool  autoOn() const { return get(AutoMatch) >= 0.5f; }
    int   meterIdx() const { return std::clamp((int)std::lround(get(Meter)), 0, 2); }
    static float lufs(double ms) { return ms > 1e-12 ? (float)std::max(-120.0, -0.691 + 10.0 * std::log10(ms)) : -120.0f; }
    static float dbPow(double ms) { return ms > 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(ms)) : -120.0f; }
    static float dbLin(float lin) { return lin > 1e-6f ? std::max(-120.0f, 20.0f * std::log10(lin)) : -120.0f; }
    static double bandHz(int b) { return kBandLo * std::pow(kBandHi / kBandLo, (b + 0.5) / kBands); }
    static double respHz(int k) { return kRespLo * std::pow(kRespHi / kRespLo, (double)k / (kResp - 1)); }

    // Mid / side gains of the width law. L/R: the side is scaled (the original law). M/S:
    // mid and side are traded — the side drops below 100 %, the mid above it; 200 % = side only.
    void widthGains(float& gm, float& gs) const {
        const float w = std::clamp(get(Width) / 100.0f, 0.0f, 4.0f);
        if (get(WidthMode) >= 0.5f) { gm = std::clamp(2.0f - w, 0.0f, 1.0f); gs = std::min(w, 1.0f); }
        else { gm = 1.0f; gs = w; }
    }
    // The configured width at hz: side / mid gain ratio × the mono high-pass, in % (≤ 400).
    double effWidth(double hz) const {
        float gm, gs;
        widthGains(gm, gs);
        double ratio = gm > 1e-4f ? (double)gs / gm : (gs > 1e-4f ? 4.0 : 0.0);
        ratio = std::min(ratio, 4.0);
        if (get(MonoBelow) >= 0.5f) {
            const double fc = std::clamp(get(MonoFreq), 20.0f, 2000.0f), x = hz / fc;
            const int slope = std::clamp((int)std::lround(get(MonoSlope)), 0, 2);
            const double h1 = x / std::sqrt(1.0 + x * x), h2 = x * x / std::sqrt(1.0 + x * x * x * x);
            ratio *= slope == 0 ? h1 : slope == 1 ? h2 : h2 * h2;
        }
        return ratio * 100.0;
    }

    // Level readouts in the Meter's unit.
    float levelIn(int m) const  { return m == 0 ? inLufsA_.load(std::memory_order_relaxed) : m == 1 ? inPeak1A_.load(std::memory_order_relaxed) : inRmsA_.load(std::memory_order_relaxed); }
    float levelOut(int m) const { return m == 0 ? outLufsA_.load(std::memory_order_relaxed) : m == 1 ? outPeak1A_.load(std::memory_order_relaxed) : outRmsA_.load(std::memory_order_relaxed); }
    float matchIn(int m) const  { return m == 0 ? inLufsA_.load(std::memory_order_relaxed) : m == 1 ? inPeak3A_.load(std::memory_order_relaxed) : inRms3A_.load(std::memory_order_relaxed); }
    float matchOut(int m) const { return m == 0 ? outLufsA_.load(std::memory_order_relaxed) : m == 1 ? outPeak3A_.load(std::memory_order_relaxed) : outRms3A_.load(std::memory_order_relaxed); }
    float matchPre(int m) const { return m == 0 ? preLufsA_.load(std::memory_order_relaxed) : m == 1 ? prePeak3A_.load(std::memory_order_relaxed) : preRms3A_.load(std::memory_order_relaxed); }

    // One-shot gain match (message thread): Gain is set so the output — measured before Gain
    // and the auto-match ride, so whatever Gain was over the window does not matter — meets
    // the reference over the last 3 s. Silence (or a muted window) leaves Gain alone.
    void gainMatch() {
        const int m = meterIdx();
        const float pre = matchPre(m);
        const float ref = get(MatchTo) >= 0.5f ? get(Target) : matchIn(m);
        if (pre <= -70.0f || ref <= -70.0f) return;
        setParam(Gain, std::clamp(ref - pre, -24.0f, 24.0f));
    }

    // ---- mono-below crossover on the side ---------------------------------------------
    struct Biquad {
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
        float run(float x) { const float y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y; return y; }
        void clear() { z1 = z2 = 0; }
    };
    void setCrossover(float hz, int slope) {
        const bool reset = slope != curSlope_;
        curMonoHz_ = hz; curSlope_ = slope;
        lpCoef_ = 1.0f - std::exp(-2.0f * (float)kPi * hz / (float)sr_);
        const double w0 = 2.0 * kPi * hz / sr_, cw = std::cos(w0), al = std::sin(w0) / (2.0 * 0.70710678);
        const double a0 = 1.0 + al;
        for (auto* q : { &hp1_, &hp2_ }) {
            q->b0 = (float)((1.0 + cw) / 2.0 / a0); q->b1 = (float)(-(1.0 + cw) / a0); q->b2 = q->b0;
            q->a1 = (float)(-2.0 * cw / a0); q->a2 = (float)((1.0 - al) / a0);
            if (reset) q->clear();
        }
        if (reset) lpSide_ = 0.0f;
    }
    float crossover(float side) {
        switch (curSlope_) {
            case 1: return hp1_.run(side);
            case 2: return hp2_.run(hp1_.run(side));
            default: lpSide_ += lpCoef_ * (side - lpSide_); return side - lpSide_;
        }
    }

    // ---- K-weighting (BS.1770): high shelf then the 38 Hz high-pass ----------------------
    struct KW { float z1a = 0, z1b = 0, z2a = 0, z2b = 0; };
    float kweight(KW& s, float x) const {
        const float y1 = kb0_[0] * x + s.z1a;
        s.z1a = kb1_[0] * x - ka1_[0] * y1 + s.z1b;
        s.z1b = kb2_[0] * x - ka2_[0] * y1;
        const float y2 = kb0_[1] * y1 + s.z2a;
        s.z2a = kb1_[1] * y1 - ka1_[1] * y2 + s.z2b;
        s.z2b = kb2_[1] * y1 - ka2_[1] * y2;
        return y2;
    }
    void computeKWeighting() {
        { const double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
          const double K = std::tan(kPi * f0 / sr_);
          const double Vh = std::pow(10.0, G / 20.0), Vb = std::pow(Vh, 0.4996667741545416);
          const double a0 = 1.0 + K / Q + K * K;
          kb0_[0] = (float)((Vh + Vb * K / Q + K * K) / a0);
          kb1_[0] = (float)(2.0 * (K * K - Vh) / a0);
          kb2_[0] = (float)((Vh - Vb * K / Q + K * K) / a0);
          ka1_[0] = (float)(2.0 * (K * K - 1.0) / a0);
          ka2_[0] = (float)((1.0 - K / Q + K * K) / a0); }
        { const double f0 = 38.13547087602444, Q = 0.5003270373238773;
          const double K = std::tan(kPi * f0 / sr_);
          const double a0 = 1.0 + K / Q + K * K;
          ka1_[1] = (float)(2.0 * (K * K - 1.0) / a0);
          ka2_[1] = (float)((1.0 - K / Q + K * K) / a0);
          kb0_[1] = (float)(1.0 / a0); kb1_[1] = (float)(-2.0 / a0); kb2_[1] = (float)(1.0 / a0); }
    }

    // ---- true peak --------------------------------------------------------------------
    // Meter: 4× Catmull-Rom between the last two samples (one sample late, fine for a meter).
    struct Tp { float y0 = 0, y1 = 0, y2 = 0; };
    static float catmull(float y0, float y1, float y2, float y3, float t) {
        const float a = y1, b = 0.5f * (y2 - y0), d = 0.5f * (y3 - y1), c = y2;
        return a + t * (b + t * ((3 * (c - a) - 2 * b - d) + t * (2 * (a - c) + b + d)));
    }
    static float truePeak(Tp& s, float x) {
        float pk = std::fabs(s.y2);
        for (int k = 1; k < 4; ++k) pk = std::max(pk, std::fabs(catmull(s.y0, s.y1, s.y2, x, k * 0.25f)));
        s.y0 = s.y1; s.y1 = s.y2; s.y2 = x;
        return pk;
    }
    // Limiter: the inter-sample peak between the previous sample and this one, with the next
    // sample extrapolated — no look-ahead, so no latency.
    static float isp(Tp& s, float x) {
        const float y3 = 2.0f * x - s.y2;
        float pk = std::fabs(x);
        for (int k = 1; k < 4; ++k) pk = std::max(pk, std::fabs(catmull(s.y1, s.y2, x, y3, k * 0.25f)));
        s.y0 = s.y1; s.y1 = s.y2; s.y2 = x;
        return pk;
    }

    // ---- 100 ms blocks: loudness windows, histories and the auto-match ride --------------
    static constexpr int kWin = 30;   // 3 s of blocks
    void endBlock() {
        const int n = blkCount_;
        const int at = winW_;
        wInK_[at] = blkInK_ / n; wOutK_[at] = blkOutK_ / n;
        wInSq_[at] = blkInSq_ / (2.0 * n); wOutSq_[at] = blkOutSq_ / (2.0 * n);
        wInPk_[at] = blkInPk_; wOutPk_[at] = blkOutPk_; wOutTp_[at] = blkOutTp_;
        // The output with the block's gain divided out (0 = muted: no reading).
        const double g2 = blkG2_ / n;
        wPreK_[at] = g2 > 1e-10 ? wOutK_[at] / g2 : 0.0;
        wPreSq_[at] = g2 > 1e-10 ? wOutSq_[at] / g2 : 0.0;
        wPrePk_[at] = g2 > 1e-10 ? (float)(blkOutPk_ / std::sqrt(g2)) : 0.0f;
        winW_ = (winW_ + 1) % kWin;
        winFill_ = std::min(winFill_ + 1, kWin);
        const float limDb = blkLimMin_ < 1.0f ? -20.0f * std::log10(std::max(1e-6f, blkLimMin_)) : 0.0f;
        blkInK_ = blkOutK_ = blkInSq_ = blkOutSq_ = blkG2_ = 0.0;
        blkInPk_ = blkOutPk_ = blkOutTp_ = 0.0f;
        blkLimMin_ = 1.0f;
        blkCount_ = 0;

        // Mean / max over the last `k` blocks.
        auto mean = [&](const double* a, int k) { k = std::min(k, winFill_); double s = 0; for (int j = 0; j < k; ++j) s += a[(at - j + kWin) % kWin]; return k > 0 ? s / k : 0.0; };
        auto peak = [&](const float* a, int k) { k = std::min(k, winFill_); float s = 0; for (int j = 0; j < k; ++j) s = std::max(s, a[(at - j + kWin) % kWin]); return s; };
        const float inL = lufs(mean(wInK_, kWin)), outL = lufs(mean(wOutK_, kWin));
        const float inM = lufs(mean(wInK_, 4)), outM = lufs(mean(wOutK_, 4));
        const float inR = dbPow(mean(wInSq_, 3)), outR = dbPow(mean(wOutSq_, 3));
        const float inR3 = dbPow(mean(wInSq_, kWin)), outR3 = dbPow(mean(wOutSq_, kWin));
        const float inP1 = dbLin(peak(wInPk_, 10)), outP1 = dbLin(peak(wOutPk_, 10));
        const float inP3 = dbLin(peak(wInPk_, kWin)), outP3 = dbLin(peak(wOutPk_, kWin));
        inLufsA_.store(inL, std::memory_order_relaxed);   outLufsA_.store(outL, std::memory_order_relaxed);
        inMomA_.store(inM, std::memory_order_relaxed);    outMomA_.store(outM, std::memory_order_relaxed);
        inRmsA_.store(inR, std::memory_order_relaxed);    outRmsA_.store(outR, std::memory_order_relaxed);
        inRms3A_.store(inR3, std::memory_order_relaxed);  outRms3A_.store(outR3, std::memory_order_relaxed);
        inPeak1A_.store(inP1, std::memory_order_relaxed); outPeak1A_.store(outP1, std::memory_order_relaxed);
        inPeak3A_.store(inP3, std::memory_order_relaxed); outPeak3A_.store(outP3, std::memory_order_relaxed);
        truePk3A_.store(dbLin(peak(wOutTp_, kWin)), std::memory_order_relaxed);
        preLufsA_.store(lufs(mean(wPreK_, kWin)), std::memory_order_relaxed);
        preRms3A_.store(dbPow(mean(wPreSq_, kWin)), std::memory_order_relaxed);
        prePeak3A_.store(dbLin(peak(wPrePk_, kWin)), std::memory_order_relaxed);
        limDbA_.store(limDb, std::memory_order_relaxed);

        const int hw = histW_.load(std::memory_order_relaxed);
        hist_[H_InLufs][(size_t)hw] = inL;  hist_[H_OutLufs][(size_t)hw] = outL;
        hist_[H_InPeak][(size_t)hw] = dbLin(wInPk_[at]); hist_[H_OutPeak][(size_t)hw] = dbLin(wOutPk_[at]);
        hist_[H_InRms][(size_t)hw] = inR;   hist_[H_OutRms][(size_t)hw] = outR;
        histW_.store((hw + 1) % kHist, std::memory_order_release);

        // Auto match: ride toward the reference, measured fast (momentary / 1 s peak / 300 ms
        // RMS) on the output before Gain and the ride, so the loop does not chase itself;
        // silence holds; the limiter working stops it from pushing up.
        if (autoOn()) {
            const int m = meterIdx();
            const float ref = get(MatchTo) >= 0.5f ? get(Target) : (m == 0 ? inM : m == 1 ? inP1 : inR);
            const float pre = m == 0 ? lufs(mean(wPreK_, 4)) : m == 1 ? dbLin(peak(wPrePk_, 10)) : dbPow(mean(wPreSq_, 3));
            if (ref > -70.0f && pre > -70.0f && get(Mute) < 0.5f) {
                float want = std::clamp(ref - (pre + std::clamp(get(Gain), -24.0f, 24.0f)), -24.0f, 24.0f);
                if (limDb > 0.5f && want > autoDb_) want = autoDb_;
                autoDb_ += (want - autoDb_) * (1.0f - std::exp(-0.1f / 1.5f));
            }
        } else {
            autoDb_ = 0.0f;
        }
        autoDbA_.store(autoDb_, std::memory_order_relaxed);
    }

    void clearHistory() {
        for (auto& s : hist_) s.fill(-120.0f);
        histW_.store(0, std::memory_order_relaxed);
    }
    // Audio thread (or before the device runs): meters, holds, histories, the auto-match ride.
    void resetMeters() {
        for (int k = 0; k < kWin; ++k) {
            wInK_[k] = wOutK_[k] = wInSq_[k] = wOutSq_[k] = wPreK_[k] = wPreSq_[k] = 0.0;
            wInPk_[k] = wOutPk_[k] = wOutTp_[k] = wPrePk_[k] = 0.0f;
        }
        winW_ = 0; winFill_ = 0;
        blkInK_ = blkOutK_ = blkInSq_ = blkOutSq_ = blkG2_ = 0.0;
        blkInPk_ = blkOutPk_ = blkOutTp_ = 0.0f; blkLimMin_ = 1.0f; blkCount_ = 0;
        clearHistory();
        for (auto* a : { &inLufsA_, &outLufsA_, &inMomA_, &outMomA_, &inRmsA_, &outRmsA_, &inRms3A_, &outRms3A_,
                         &inPeak1A_, &outPeak1A_, &inPeak3A_, &outPeak3A_, &truePk3A_, &preLufsA_, &preRms3A_, &prePeak3A_ })
            a->store(-120.0f, std::memory_order_relaxed);
        inPkA_.store(0.0f, std::memory_order_relaxed); outPkA_.store(0.0f, std::memory_order_relaxed);
        limDbA_.store(0.0f, std::memory_order_relaxed);
        autoDb_ = 0.0f; autoDbA_.store(0.0f, std::memory_order_relaxed);
    }
    void resetDsp() {
        for (int c = 0; c < 2; ++c) { kIn_[c] = KW{}; kOut_[c] = KW{}; tpM_[c] = Tp{}; limH_[c] = Tp{}; }
        hp1_.clear(); hp2_.clear(); lpSide_ = 0.0f; limEnv_ = 1.0f;
        smCoef_ = (float)(1.0 - std::exp(-1.0 / (0.005 * sr_)));
        autoCoef_ = (float)(1.0 - std::exp(-1.0 / (0.03 * sr_)));
        relCoef_ = (float)(1.0 - std::exp(-1.0 / (0.08 * sr_)));
        emaL_ = emaR_ = emaM_ = emaS_ = 0.0f;
    }

    // ---- band analysis (message thread, rate-limited) -----------------------------------
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
    // Powers of one FFT frame ending at `end` (frames written) into per-band sums.
    void frame(uint32_t end, double (&pl)[kBands], double (&pr)[kBands], double (&pm)[kBands], double (&ps)[kBands], double (&px)[kBands]) const {
        const int n = fftN_;
        const uint32_t start = end - (uint32_t)n;
        for (int i = 0; i < n; ++i) {
            const uint32_t k = (start + (uint32_t)i) & ringMask_;
            tl_[(size_t)i] = ringL_[k] * win_[(size_t)i];
            tr_[(size_t)i] = ringR_[k] * win_[(size_t)i];
        }
        fft_.fft(tl_.data(), fl_.data());
        fft_.fft(tr_.data(), fr_.data());
        const double binHz = sr_ / n;
        for (int b = 0; b < kBands; ++b) {
            const double lo = kBandLo * std::pow(kBandHi / kBandLo, (double)b / kBands);
            const double hi = kBandLo * std::pow(kBandHi / kBandLo, (double)(b + 1) / kBands);
            int k0 = std::max(1, (int)std::ceil(lo / binHz)), k1 = std::min(n / 2 - 1, (int)std::ceil(hi / binHz) - 1);
            if (k1 < k0) k0 = k1 = std::clamp((int)std::lround(bandHz(b) / binHz), 1, n / 2 - 1);
            double sl = 0, sr = 0, smm = 0, sss = 0, sx = 0;
            for (int k = k0; k <= k1; ++k) {
                const std::complex<float> L = fl_[(size_t)k], R = fr_[(size_t)k];
                sl += std::norm(L); sr += std::norm(R);
                smm += 0.25 * std::norm(L + R); sss += 0.25 * std::norm(L - R);
                sx += (double)L.real() * R.real() + (double)L.imag() * R.imag();
            }
            pl[b] += sl; pr[b] += sr; pm[b] += smm; ps[b] += sss; px[b] += sx;
        }
    }
    // Refresh the band readings: a fresh average of six frames over the last second when the
    // last analysis is stale, else a one-second EMA on the newest frame. Returns whether the
    // readings are valid.
    bool analyse() const {
        std::lock_guard<std::mutex> lock(mx_);
        if (ringL_.empty() || fftN_ <= 0) return false;
        const double now = nowSeconds();
        const uint32_t w = ringW_.load(std::memory_order_acquire);
        if (lastAnaS_ >= 0 && (now - lastAnaS_ < 1.0 / 30.0 || w == lastAnaW_)) return anaValid_;
        const bool stale = lastAnaS_ < 0 || now - lastAnaS_ > 1.2;
        double pl[kBands] = {}, pr[kBands] = {}, pm[kBands] = {}, ps[kBands] = {}, px[kBands] = {};
        int frames = 0;
        if (stale) {
            const uint32_t hop = (uint32_t)std::max(1.0, sr_ / 6.0);
            for (int j = 0; j < 6; ++j) {
                const uint32_t back = (uint32_t)j * hop;
                if ((uint64_t)w < (uint64_t)back + (uint64_t)fftN_ && j > 0) break;
                frame(w - back, pl, pr, pm, ps, px);
                ++frames;
            }
        } else {
            frame(w, pl, pr, pm, ps, px);
            frames = 1;
        }
        const double inv = 1.0 / std::max(1, frames);
        const double a = stale ? 1.0 : 1.0 - std::exp(-(now - lastAnaS_) / 1.0);
        for (int b = 0; b < kBands; ++b) {
            aPL_[b] += a * (pl[b] * inv - aPL_[b]); aPR_[b] += a * (pr[b] * inv - aPR_[b]);
            aPM_[b] += a * (pm[b] * inv - aPM_[b]); aPS_[b] += a * (ps[b] * inv - aPS_[b]);
            aPX_[b] += a * (px[b] * inv - aPX_[b]);
        }
        lastAnaS_ = now; lastAnaW_ = w;
        const double ref = (winSum_ * 0.5) * (winSum_ * 0.5);   // a full-scale sine → 0 dB
        for (int b = 0; b < kBands; ++b) {
            const double l = aPL_[b], r = aPR_[b], tot = l + r;
            if (tot < ref * 1e-12) { bandPan_[b] = 0; bandWidth_[b] = 0; bandCorr_[b] = 1; bandLevel_[b] = -120; continue; }
            const double al = std::sqrt(l), ar = std::sqrt(r);
            bandPan_[b] = (float)((ar - al) / (ar + al));
            bandWidth_[b] = (float)std::sqrt(aPS_[b] / std::max(1e-30, aPM_[b] + aPS_[b]));
            bandCorr_[b] = l > 1e-30 && r > 1e-30 ? (float)std::clamp(aPX_[b] / std::sqrt(l * r), -1.0, 1.0) : 0.0f;
            bandLevel_[b] = (float)std::max(-120.0, 10.0 * std::log10(0.5 * tot / ref));
        }
        anaValid_ = true;
        return true;
    }

    std::atomic<float> p_[kNumParams] = {};
    double sr_ = 44100.0;

    // Audio-thread state.
    bool  first_ = true;
    float sGain_ = 1, sGL_ = 1, sGR_ = 1, sGm_ = 1, sGs_ = 1, sAuto_ = 1;
    float smCoef_ = 0.004f, autoCoef_ = 0.0008f, relCoef_ = 0.0003f;
    float curMonoHz_ = -1.0f; int curSlope_ = -1;
    float lpSide_ = 0.0f, lpCoef_ = 0.0f;
    Biquad hp1_, hp2_;
    float kb0_[2] = {}, kb1_[2] = {}, kb2_[2] = {}, ka1_[2] = {}, ka2_[2] = {};
    KW kIn_[2], kOut_[2];
    Tp tpM_[2], limH_[2];
    float limEnv_ = 1.0f;
    float autoDb_ = 0.0f;
    float emaL_ = 0, emaR_ = 0, emaM_ = 0, emaS_ = 0;
    int blockLen_ = 4410, blkCount_ = 0;
    double blkInK_ = 0, blkOutK_ = 0, blkInSq_ = 0, blkOutSq_ = 0, blkG2_ = 0;
    float blkInPk_ = 0, blkOutPk_ = 0, blkOutTp_ = 0, blkLimMin_ = 1.0f;
    double wInK_[kWin] = {}, wOutK_[kWin] = {}, wInSq_[kWin] = {}, wOutSq_[kWin] = {};
    float wInPk_[kWin] = {}, wOutPk_[kWin] = {}, wOutTp_[kWin] = {};
    double wPreK_[kWin] = {}, wPreSq_[kWin] = {};   // the output before Gain and the ride
    float wPrePk_[kWin] = {};
    int winW_ = 0, winFill_ = 0;
    double cpuS_ = 0.0;
    std::atomic<bool> resetReq_{false};

    // Published telemetry (audio thread writes, UI reads lock-free; torn reads OK).
    std::atomic<float> inPkA_{0}, outPkA_{0}, cpuA_{0}, srA_{44100.0f}, corr_{1.0f};
    std::atomic<float> inLufsA_{-120}, outLufsA_{-120}, inMomA_{-120}, outMomA_{-120}, inRmsA_{-120}, outRmsA_{-120},
                       inRms3A_{-120}, outRms3A_{-120}, inPeak1A_{-120}, outPeak1A_{-120}, inPeak3A_{-120}, outPeak3A_{-120},
                       truePk3A_{-120}, preLufsA_{-120}, preRms3A_{-120}, prePeak3A_{-120},
                       limDbA_{0}, autoDbA_{0}, balDbA_{0}, widthA_{0};
    std::array<std::array<float, kHist>, kHistSeries> hist_{};
    std::atomic<int> histW_{0};

    // Output ring for the band analysis (sized in setSampleRate).
    std::vector<float> ringL_, ringR_;
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};

    // Band analysis (message thread; guarded by mx_).
    mutable std::mutex mx_;
    int fftN_ = 0;
    double winSum_ = 0.0;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tl_, tr_;
    mutable std::vector<std::complex<float>> fl_, fr_;
    mutable double aPL_[kBands] = {}, aPR_[kBands] = {}, aPM_[kBands] = {}, aPS_[kBands] = {}, aPX_[kBands] = {};
    mutable float bandPan_[kBands] = {}, bandWidth_[kBands] = {}, bandCorr_[kBands] = {}, bandLevel_[kBands] = {};
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
