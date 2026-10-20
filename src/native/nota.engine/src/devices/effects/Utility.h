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
// Allocation-free after setSampleRate. Params keep real units (Gain dB,
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
// Control-rate math kept std::-exact. Silent stretches
// auto-sleep via isIdle() in hangover form: sleep engages only after input AND output
// stay ≤ −100 dB past the 3 s measurement windows (plus margin), so the K-weighting,
// crossover, and limiter states have rung out and the auto-match ride holds; the band
// ring keeps advancing through zeros and the 100 ms block windows advance on silence,
// so meters and the ride resume exactly on wake.

#pragma once

#include "devices/Device.h"

#include <signalsmith-linear/fft.h>

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
    enum {
        Gain = 0,
        Balance,
        Width,
        ChannelMode,
        MonoFreq,
        MonoBelow,
        Mute,
        InvertL,
        InvertR,
        WidthMode, // 0 L/R (side × width), 1 M/S (mid ↔ side balance; 200 % = side only)
        MonoSlope, // 0 6 dB/oct (the original one-pole), 1 12, 2 24
        AutoMatch, // the output rides to the reference (the input's level or Target)
        MatchTo,   // 0 input, 1 Target
        Target,    // −36..0, in the Meter's unit (LUFS / dBFS peak / dBFS RMS)
        Meter,     // 0 LUFS-S, 1 Peak, 2 RMS
        TpLimit,   // true-peak safety limiter at TP Ceiling
        TpCeiling, // −6..0 dBTP
        kNumParams
    };

    // Telemetry slots (scopeRead). Levels in dB (−120 = silence).
    enum {
        S_InPk = 0, // input peak (decaying ~300 ms), linear
        S_OutPk,    // output peak (decaying), linear
        S_Cpu,      // share of real time spent in process()
        S_SampleRate,
        S_Corr,   // output L/R correlation −1..+1
        S_InLufs, // short-term loudness (3 s), LUFS
        S_OutLufs,
        S_InRms, // RMS over 300 ms, dBFS
        S_OutRms,
        S_InPeak, // sample peak held over 1 s, dBFS
        S_OutPeak,
        S_TruePeak, // output true peak held over 3 s, dBTP
        S_InMom,    // momentary loudness (400 ms), LUFS
        S_OutMom,
        S_AutoDb,  // the auto-match gain now, dB
        S_LimDb,   // limiter gain reduction in the last 100 ms, dB (≥ 0)
        S_InLevel, // input / output in the Meter's unit (LUFS-S / 1 s peak / 300 ms RMS)
        S_OutLevel,
        S_InMatch, // input / output over 3 s in the Meter's unit (what Gain match uses)
        S_OutMatch,
        S_EnergyBal, // output L vs R energy, dB (+ = left louder)
        S_WidthNow,  // measured output width: side / mid amplitude, %
        S_EffWidth,  // configured width above the mono cutoff, % (the width law applied)
        S_MonoHz,    // the mono cutoff (0 when Mono Below is off)
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
        S_Reserved1,
        S_Reserved2,
        S_Reserved3,
        S_Reserved4,
        kTele = 48
    };
    static constexpr int kHist = 80; // 8 s of 100 ms blocks
    static constexpr int kHistSeries = 6;
    enum { H_InLufs = 0, H_OutLufs, H_InPeak, H_OutPeak, H_InRms, H_OutRms };
    static constexpr int kBands = 32;
    static constexpr int kBandSeries = 5;
    enum {
        B_Pan = 0, // −1 left … +1 right: where the band's energy sits
        B_Width,   // side share sqrt(S² / (M² + S²)): 0 mono, .71 uncorrelated, 1 anti-phase
        B_Corr,    // L/R correlation in the band
        B_Level,   // band level, dB re full scale
        B_Set
    }; // the configured width at the band centre, %
    static constexpr int kResp = 96;
    static constexpr double kBandLo = 30.0, kBandHi = 16000.0, kRespLo = 20.0, kRespHi = 20000.0;
    static constexpr int kHistAt = kTele;
    static constexpr int kBandAt = kHistAt + kHistSeries * kHist;
    static constexpr int kRespAt = kBandAt + kBandSeries * kBands;
    static constexpr int kScopeTotal = kRespAt + kResp;
    enum { A_GainMatch = 0, A_ResetMeters = 1 }; // deviceAction ids

    Utility();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: short filter memory only — no audible tail. isIdle() goes
    // high after the hangover (see the .cpp); setParam/deviceAction wake.
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Telemetry, the histories, the bands and the configured width (see the header note).
    // Writes as much of that layout as fits in maxSamples. Torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Utility"; }
    int32_t builtinKind() const override { return 4; }
    int32_t paramCount() const override { return kNumParams; }

    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    // Hangover before sleep: past the 3 s measurement windows so the ride holds.
    static constexpr double kIdleSec = 4.0;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool autoOn() const { return get(AutoMatch) >= 0.5f; }
    int meterIdx() const { return std::clamp((int)std::lround(get(Meter)), 0, 2); }
    static float lufs(double ms) {
        return ms > 1e-12 ? (float)std::max(-120.0, -0.691 + 10.0 * std::log10(ms)) : -120.0f;
    }
    static float dbPow(double ms) { return ms > 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(ms)) : -120.0f; }
    static float dbLin(float lin) { return lin > 1e-6f ? std::max(-120.0f, 20.0f * std::log10(lin)) : -120.0f; }
    static double bandHz(int b) { return kBandLo * std::pow(kBandHi / kBandLo, (b + 0.5) / kBands); }
    static double respHz(int k) { return kRespLo * std::pow(kRespHi / kRespLo, (double)k / (kResp - 1)); }

    // Mid / side gains of the width law. L/R: the side is scaled (the original law). M/S:
    // mid and side are traded — the side drops below 100 %, the mid above it; 200 % = side only.
    void widthGains(float& gm, float& gs) const;
    // The configured width at hz: side / mid gain ratio × the mono high-pass, in % (≤ 400).
    double effWidth(double hz) const;

    // Level readouts in the Meter's unit.
    float levelIn(int m) const {
        return m == 0   ? inLufsA_.load(std::memory_order_relaxed)
               : m == 1 ? inPeak1A_.load(std::memory_order_relaxed)
                        : inRmsA_.load(std::memory_order_relaxed);
    }
    float levelOut(int m) const {
        return m == 0   ? outLufsA_.load(std::memory_order_relaxed)
               : m == 1 ? outPeak1A_.load(std::memory_order_relaxed)
                        : outRmsA_.load(std::memory_order_relaxed);
    }
    float matchIn(int m) const {
        return m == 0   ? inLufsA_.load(std::memory_order_relaxed)
               : m == 1 ? inPeak3A_.load(std::memory_order_relaxed)
                        : inRms3A_.load(std::memory_order_relaxed);
    }
    float matchOut(int m) const {
        return m == 0   ? outLufsA_.load(std::memory_order_relaxed)
               : m == 1 ? outPeak3A_.load(std::memory_order_relaxed)
                        : outRms3A_.load(std::memory_order_relaxed);
    }
    float matchPre(int m) const {
        return m == 0   ? preLufsA_.load(std::memory_order_relaxed)
               : m == 1 ? prePeak3A_.load(std::memory_order_relaxed)
                        : preRms3A_.load(std::memory_order_relaxed);
    }

    // One-shot gain match (message thread): Gain is set so the output — measured before Gain
    // and the auto-match ride, so whatever Gain was over the window does not matter — meets
    // the reference over the last 3 s. Silence (or a muted window) leaves Gain alone.
    void gainMatch();

    // ---- mono-below crossover on the side ---------------------------------------------
    struct Biquad {
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
        float run(float x) {
            const float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
        void clear() { z1 = z2 = 0; }
    };
    void setCrossover(float hz, int slope);
    float crossover(float side);

    // ---- K-weighting (BS.1770): high shelf then the 38 Hz high-pass ----------------------
    struct KW {
        float z1a = 0, z1b = 0, z2a = 0, z2b = 0;
    };
    float kweight(KW& s, float x) const;
    void computeKWeighting();

    // ---- true peak --------------------------------------------------------------------
    // Meter: 4× Catmull-Rom between the last two samples (one sample late, fine for a meter).
    struct Tp {
        float y0 = 0, y1 = 0, y2 = 0;
    };
    static float catmull(float y0, float y1, float y2, float y3, float t);
    static float truePeak(Tp& s, float x);
    // Limiter: the inter-sample peak between the previous sample and this one, with the next
    // sample extrapolated — no look-ahead, so no latency.
    static float isp(Tp& s, float x);

    // ---- 100 ms blocks: loudness windows, histories and the auto-match ride --------------
    static constexpr int kWin = 30; // 3 s of blocks
    void endBlock();

    void clearHistory();
    // Audio thread (or before the device runs): meters, holds, histories, the auto-match ride.
    void resetMeters();
    void resetDsp();

    // ---- band analysis (message thread, rate-limited) -----------------------------------
    static double nowSeconds();
    // Powers of one FFT frame ending at `end` (frames written) into per-band sums.
    void frame(uint32_t end, double (&pl)[kBands], double (&pr)[kBands], double (&pm)[kBands], double (&ps)[kBands],
        double (&px)[kBands]) const;
    // Refresh the band readings: a fresh average of six frames over the last second when the
    // last analysis is stale, else a one-second EMA on the newest frame. Returns whether the
    // readings are valid.
    bool analyse() const;

    std::atomic<float> p_[kNumParams] = {};
    double sr_ = 44100.0;

    // Audio-thread state.
    bool first_ = true;
    float sGain_ = 1, sGL_ = 1, sGR_ = 1, sGm_ = 1, sGs_ = 1, sAuto_ = 1;
    float smCoef_ = 0.004f, autoCoef_ = 0.0008f, relCoef_ = 0.0003f;
    float curMonoHz_ = -1.0f;
    int curSlope_ = -1;
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
    double wPreK_[kWin] = {}, wPreSq_[kWin] = {}; // the output before Gain and the ride
    float wPrePk_[kWin] = {};
    int winW_ = 0, winFill_ = 0;
    double cpuS_ = 0.0;
    std::atomic<bool> resetReq_{false};

    // Auto-sleep hangover.
    double quietSec_ = 0.0;
    bool idle_ = false;

    // Published telemetry (audio thread writes, UI reads lock-free; torn reads OK).
    std::atomic<float> inPkA_{0}, outPkA_{0}, cpuA_{0}, srA_{44100.0f}, corr_{1.0f};
    std::atomic<float> inLufsA_{-120}, outLufsA_{-120}, inMomA_{-120}, outMomA_{-120}, inRmsA_{-120}, outRmsA_{-120},
        inRms3A_{-120}, outRms3A_{-120}, inPeak1A_{-120}, outPeak1A_{-120}, inPeak3A_{-120}, outPeak3A_{-120},
        truePk3A_{-120}, preLufsA_{-120}, preRms3A_{-120}, prePeak3A_{-120}, limDbA_{0}, autoDbA_{0}, balDbA_{0},
        widthA_{0};
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
