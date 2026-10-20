// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Lens (device kind 22, mockup "Nota Lens") — an analyzer: FFT spectrum,
// triggered oscilloscope and waterfall in one card. The audio passes through
// untouched; process() only captures L/R into rings and runs the K-weighted
// loudness follower. Everything expensive (FFT, the log-frequency curve, trigger
// search, measurements) happens lazily on the message thread inside
// ensureAnalyzed(), rate-limited by the Display Rate parameter — so the audio
// thread stays a memcpy and the UI (or MCP) pulls whatever it needs:
//
//   scopeRead   → packed measurements (peak, RMS, crest, correlation, LUFS,
//                 trigger state, period, Vpp, cursors …)
//   layerWave   → L_Spectrum / L_Peak curves, the scope trace, the raw triggered
//                 window (for WAV export) and the newest waterfall row
//   deviceText  → the same analysis as text, for MCP
//   deviceAction→ re-arm Single, reset peak hold, reset the trigger
//
// A core Device (JUCE-free); params normalized 0..1, denormalized where used.
// Persistence / automation / clone flow generically through the base Device.
// Verbatim — no FastMath (the message-thread analysis
// feeds measurements, and steering-adjacent trigger/period math stays std::-exact).
// No auto-sleep: process() must keep capturing rings and the loudness follower
// through silence (silence is a valid measurement — noise floor, trigger
// free-run, Single re-arm), and it is already ~memcpy-cheap, so skipping would
// corrupt telemetry for no audible gain. isIdle() stays false; hasTail() is
// false (the signal passes through untouched).

#pragma once

#include "devices/Device.h"

#include <signalsmith-linear/fft.h>

#include <algorithm>
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

class Lens : public Device {
  public:
    enum {
        View = 0, // 0 Spectrum · .5 Scope · 1 Waterfall
        Freeze,   // >= .5 holds the capture (rings stop advancing)
        Source,   // 0 L+R · .5 first channel · 1 second channel
        MidSide,  // >= .5 the channel pair is Mid/Side instead of L/R

        FftSize,  // 0 512 · .333 2048 · .667 4096 · 1 16384
        Window,   // 0 Hann · .5 Blackman-Harris · 1 Flat-top
        Average,  // 1..16 frames
        Smooth,   // 0..1 neighbour-bin smoothing
        Decay,    // trace fall, exp 0.05..8 s
        Tilt,     // 0..9 dB/oct pink-slope compensation
        Floor,    // −120..−40 dB
        PeakHold, // >= .5 draw the peak-hold trace
        PeakTime, // hold before the peak falls, exp 0.5..30 s

        ScaleTop,   // top of the dB axis, +24..−36 dB
        ScaleRange, // visible span, 30..120 dB

        TimeDiv,     // exp 0.02..20 ms per division (8 divisions across)
        VoltDiv,     // exp 0.02..2 per division (4 divisions high)
        TrigMode,    // 0 Auto · .5 Normal · 1 Single
        TrigEdge,    // 0 rising · 1 falling
        TrigLevel,   // −1..1
        Holdoff,     // 0..200 ms
        Persist,     // >= .5 afterglow
        PersistTime, // exp 50..2000 ms
        Traces,      // 1..8 ghost traces
        Bright,      // 0..1 trace brightness

        Cursors, // >= .5 show the A/B cursors
        CursorA, // 0..1 across the window
        CursorB,
        CursorSnap, // >= .5 snap a cursor to the nearest peak

        WfSpeed,    // 0 4 s · .5 12 s · 1 60 s of history
        WfGain,     // −24..+24 dB
        WfFloor,    // −120..−40 dB
        WfContrast, // 0..1
        WfOverlap,  // 0 0 % · .333 50 % · .667 75 % · 1 87.5 %

        LogFreq,  // >= .5 logarithmic frequency axis
        NoteGrid, // >= .5 draw the note grid
        Rate,     // display refresh, 10..60 fps
        kNumParams
    };

    // scopeRead payload — packed measurements, not a signal (mirrors Shutter).
    enum {
        M_PeakHz = 0, // loudest spectral peak
        M_PeakDb,
        M_RmsDb,   // analysis-signal RMS over the FFT window
        M_LevelDb, // analysis-signal peak over the same window
        M_CrestDb,
        M_Corr,     // L/R correlation, −1..1
        M_Lufs,     // momentary (400 ms) K-weighted loudness
        M_TrigOk,   // 1 = the trace is locked to a trigger
        M_PeriodMs, // measured period of the scope trace (0 = none)
        M_FreqHz,   // 1 / period
        M_Vpp,
        M_Vrms,
        M_WindowMs, // scope window (8 divisions)
        M_BinHz,    // FFT resolution
        M_FftN,
        M_FftMs,    // FFT window in ms
        M_CursorAv, // trace value under cursor A / B
        M_CursorBv,
        M_SampleRate,
        M_Held, // 1 = frozen or a finished Single capture
        kScope
    };

    // layerWave layers.
    enum { L_Spectrum = 0, L_Peak, L_Trace, L_Raw, L_Waterfall };

    // deviceAction ids.
    enum { A_Rearm = 0, A_ResetPeak, A_ResetTrigger };

    static constexpr int kCurve = 512;  // points in a spectrum curve
    static constexpr int kTrace = 512;  // points in a scope trace
    static constexpr int kWfBins = 256; // bins in a waterfall row

    Lens();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Analyzer: the signal passes through untouched.
    void process(float* buf, int32_t frames) override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Text reports, for MCP (get_device_text) and the smoke test.
    std::string deviceText(int32_t id) const override;

    const char* displayName() const override { return "Nota Lens"; }
    int32_t builtinKind() const override { return 22; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // ---- unit helpers, shared with the card so both read the same numbers ----
    static double timeDivSec(float v) { return expMap(v, 0.00002, 0.02); }
    static double voltDiv(float v) { return expMap(v, 0.02, 2.0); }
    static int fftSizeOf(float v) {
        static const int n[4] = {512, 2048, 4096, 16384};
        return n[std::clamp((int)std::lround(v * 3.0f), 0, 3)];
    }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kRing = 32768; // power of two, per channel
    static constexpr int kMaxFft = 16384;
    static constexpr int kMaxWin = 16384;  // longest scope window we keep raw
    static constexpr int kLufsBlocks = 50; // 50 × 8 ms = 400 ms momentary window
    static constexpr int kBands = 31;      // ISO third-octave centres, 20 Hz … 20 kHz

    static constexpr double kBandHz[kBands] = {20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500,
        630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000};

    struct Peak {
        double hz;
        double db;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static int step(float v, int n) { return std::clamp((int)std::lround(v * (n - 1)), 0, n - 1); }
    int trigModeIdx() const { return step(get(TrigMode), 3); }

    static int32_t copyOut(const float* src, int n, float* out, int32_t maxSamples) {
        const int32_t k = std::min<int32_t>(n, maxSamples);
        if (k > 0) std::copy(src, src + k, out);
        return k;
    }

    // ---- K-weighting (BS.1770) ------------------------------------------
    struct Biquad {
        double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    };
    struct KState {
        double z1[2] = {0, 0}, z2[2] = {0, 0};
    };

    void kInit();
    float kFilter(float x, KState& st) const;

    // ---- analysis signal --------------------------------------------------
    // The channel pair is L/R or Mid/Side; Source picks the sum or one of them.
    // The selection is hoisted out of the sample loops (pick), so a 16 k FFT
    // window costs two array reads per sample, not four atomic loads.
    float pick(uint32_t idx, bool ms, int src) const {
        const size_t i = idx & (kRing - 1);
        const float l = ringL_[i], r = ringR_[i];
        if (ms) return src == 2 ? 0.5f * (l - r) : 0.5f * (l + r);
        return src == 1 ? l : src == 2 ? r : 0.5f * (l + r);
    }
    bool msOn() const { return get(MidSide) >= 0.5f; }
    int srcIdx() const { return step(get(Source), 3); }

    int windowSamples() const;
    float traceAt(float pos) const;

    // ---- the analysis pass ------------------------------------------------
    // Called under mx_ from every read entry point; recomputes at most once per
    // 1/Rate seconds and only when the audio thread has produced a new block.
    void ensureAnalyzed() const;
    void analyzeSpectrum(double dt) const;
    void buildWindow(int kind, int n) const;

    // Trigger search + trace capture. The trace is pinned to the trigger point
    // (25 % pre-trigger, like the mockup's marker) so it stands still.
    void analyzeScope() const;

    // Period by autocorrelation over the captured window (0 when nothing locks).
    double measurePeriod() const;
    double bandDb(double hz) const;
    std::vector<Peak> topPeaks(int want) const;
    static std::string noteName(double hz);

    const char* viewName() const {
        static const char* v[3] = {"Spectrum", "Scope", "Waterfall"};
        return v[step(get(View), 3)];
    }
    const char* sourceName() const {
        const bool ms = get(MidSide) >= 0.5f;
        static const char* lr[3] = {"L+R", "L", "R"};
        static const char* msn[3] = {"Mid", "Mid", "Side"};
        return (ms ? msn : lr)[step(get(Source), 3)];
    }

    static double db(double lin) { return 20.0 * std::log10(std::max(1e-10, lin)); }
    static double nowSeconds() {
        // A monotonic clock without <chrono> in the hot path's type soup.
        using clock = std::chrono::steady_clock;
        return std::chrono::duration<double>(clock::now().time_since_epoch()).count();
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};

    // Capture rings (audio thread writes, message thread reads).
    std::vector<float> ringL_, ringR_;
    std::atomic<uint32_t> ringW_{0};
    std::atomic<uint32_t> frameSeq_{0};
    std::atomic<float> corr_{0.0f}, lufs_{-120.0f};

    // Loudness follower (audio thread only).
    Biquad kb_[2];
    KState ks_[2];
    std::vector<double> lufsRing_;
    int lufsBlock_ = 353, lufsIdx_ = 0, lufsN_ = 0;
    double lufsAcc_ = 0.0;

    // Analysis state — message thread only, under mx_.
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    mutable std::vector<std::complex<float>> freq_;
    mutable std::vector<float> time_, win_, avg_, binDb_, smTmp_;
    mutable std::vector<float> curve_, peak_, peakAge_, trace_, rawWin_, wfRow_;
    mutable double winSum_ = 1.0, lastAnalyzeS_ = -1.0;
    mutable uint32_t lastSeq_ = 0xFFFFFFFFu;
    mutable int fftN_ = 0, winKind_ = -1, rawLen_ = 0;
    mutable uint32_t trigPos_ = 0;
    mutable bool trigOk_ = false, armed_ = true, singleDone_ = false;
    mutable float mPeakHz_ = 0, mPeakDb_ = -120, mRmsDb_ = -120, mLevelDb_ = -120;
    mutable float mPeriodMs_ = 0, mVpp_ = 0, mVrms_ = 0;
};

} // namespace nota
