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

#pragma once

#include "Device.h"

#include "signalsmith-linear/fft.h"

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
        View = 0,       // 0 Spectrum · .5 Scope · 1 Waterfall
        Freeze,         // >= .5 holds the capture (rings stop advancing)
        Source,         // 0 L+R · .5 first channel · 1 second channel
        MidSide,        // >= .5 the channel pair is Mid/Side instead of L/R

        FftSize,        // 0 512 · .333 2048 · .667 4096 · 1 16384
        Window,         // 0 Hann · .5 Blackman-Harris · 1 Flat-top
        Average,        // 1..16 frames
        Smooth,         // 0..1 neighbour-bin smoothing
        Decay,          // trace fall, exp 0.05..8 s
        Tilt,           // 0..9 dB/oct pink-slope compensation
        Floor,          // −120..−40 dB
        PeakHold,       // >= .5 draw the peak-hold trace
        PeakTime,       // hold before the peak falls, exp 0.5..30 s

        ScaleTop,       // top of the dB axis, +24..−36 dB
        ScaleRange,     // visible span, 30..120 dB

        TimeDiv,        // exp 0.02..20 ms per division (8 divisions across)
        VoltDiv,        // exp 0.02..2 per division (4 divisions high)
        TrigMode,       // 0 Auto · .5 Normal · 1 Single
        TrigEdge,       // 0 rising · 1 falling
        TrigLevel,      // −1..1
        Holdoff,        // 0..200 ms
        Persist,        // >= .5 afterglow
        PersistTime,    // exp 50..2000 ms
        Traces,         // 1..8 ghost traces
        Bright,         // 0..1 trace brightness

        Cursors,        // >= .5 show the A/B cursors
        CursorA,        // 0..1 across the window
        CursorB,
        CursorSnap,     // >= .5 snap a cursor to the nearest peak

        WfSpeed,        // 0 4 s · .5 12 s · 1 60 s of history
        WfGain,         // −24..+24 dB
        WfFloor,        // −120..−40 dB
        WfContrast,     // 0..1
        WfOverlap,      // 0 0 % · .333 50 % · .667 75 % · 1 87.5 %

        LogFreq,        // >= .5 logarithmic frequency axis
        NoteGrid,       // >= .5 draw the note grid
        Rate,           // display refresh, 10..60 fps
        kNumParams
    };

    // scopeRead payload — packed measurements, not a signal (mirrors Shutter).
    enum {
        M_PeakHz = 0,   // loudest spectral peak
        M_PeakDb,
        M_RmsDb,        // analysis-signal RMS over the FFT window
        M_LevelDb,      // analysis-signal peak over the same window
        M_CrestDb,
        M_Corr,         // L/R correlation, −1..1
        M_Lufs,         // momentary (400 ms) K-weighted loudness
        M_TrigOk,       // 1 = the trace is locked to a trigger
        M_PeriodMs,     // measured period of the scope trace (0 = none)
        M_FreqHz,       // 1 / period
        M_Vpp,
        M_Vrms,
        M_WindowMs,     // scope window (8 divisions)
        M_BinHz,        // FFT resolution
        M_FftN,
        M_FftMs,        // FFT window in ms
        M_CursorAv,     // trace value under cursor A / B
        M_CursorBv,
        M_SampleRate,
        M_Held,         // 1 = frozen or a finished Single capture
        kScope
    };

    // layerWave layers.
    enum { L_Spectrum = 0, L_Peak, L_Trace, L_Raw, L_Waterfall };

    // deviceAction ids.
    enum { A_Rearm = 0, A_ResetPeak, A_ResetTrigger };

    static constexpr int kCurve = 512;     // points in a spectrum curve
    static constexpr int kTrace = 512;     // points in a scope trace
    static constexpr int kWfBins = 256;    // bins in a waterfall row

    Lens() {
        p_[View].store(0.0f);
        p_[Freeze].store(0.0f);
        p_[Source].store(0.0f);
        p_[MidSide].store(0.0f);
        p_[FftSize].store(0.667f);      // 4096
        p_[Window].store(0.0f);         // Hann
        p_[Average].store(0.2f);        // ~4 frames
        p_[Smooth].store(0.35f);
        p_[Decay].store(0.45f);         // ~0.8 s
        p_[Tilt].store(0.0f);
        p_[Floor].store(0.3f);          // −96 dB
        p_[PeakHold].store(1.0f);
        p_[PeakTime].store(0.45f);      // ~4 s
        p_[ScaleTop].store(0.5f);       // −6 dB
        p_[ScaleRange].store(0.667f);   // 90 dB
        p_[TimeDiv].store(0.667f);      // ~2 ms/div
        p_[VoltDiv].store(0.7f);        // ~0.5/div
        p_[TrigMode].store(0.0f);       // Auto
        p_[TrigEdge].store(0.0f);       // rising
        p_[TrigLevel].store(0.59f);     // +0.18
        p_[Holdoff].store(0.06f);       // 12 ms
        p_[Persist].store(1.0f);
        p_[PersistTime].store(0.58f);   // ~420 ms
        p_[Traces].store(0.286f);       // 3
        p_[Bright].store(0.66f);
        p_[Cursors].store(0.0f);
        p_[CursorA].store(0.25f);
        p_[CursorB].store(0.375f);
        p_[CursorSnap].store(0.0f);
        p_[WfSpeed].store(0.5f);        // 12 s
        p_[WfGain].store(0.625f);       // +6 dB
        p_[WfFloor].store(0.375f);      // −90 dB
        p_[WfContrast].store(0.7f);
        p_[WfOverlap].store(0.667f);    // 75 %
        p_[LogFreq].store(1.0f);
        p_[NoteGrid].store(0.0f);
        p_[Rate].store(0.4f);           // 30 fps
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        sr_ = sr > 0 ? sr : 44100.0;
        ringL_.assign(kRing, 0.0f);
        ringR_.assign(kRing, 0.0f);
        ringW_.store(0, std::memory_order_relaxed);
        curve_.assign(kCurve, -200.0f);
        peak_.assign(kCurve, -200.0f);
        peakAge_.assign(kCurve, 0.0f);
        avg_.assign(kMaxFft / 2, -200.0f);
        binDb_.assign(kMaxFft / 2, -200.0f);
        trace_.assign(kTrace, 0.0f);
        rawWin_.assign(kMaxWin, 0.0f);
        rawLen_ = 0;
        wfRow_.assign(kWfBins, 0.0f);
        fftN_ = 0;
        lastAnalyzeS_ = -1.0;
        trigPos_ = 0; trigOk_ = false; armed_ = true; singleDone_ = false;
        kInit();
        lufsBlock_ = std::max(64, (int)std::lround(sr_ * 0.008));   // 8 ms sub-blocks
        lufsRing_.assign(kLufsBlocks, 0.0);
        lufsIdx_ = 0; lufsAcc_ = 0.0; lufsN_ = 0;
    }

    // Analyzer: the signal passes through untouched.
    void process(float* buf, int32_t frames) override {
        const bool frozen = get(Freeze) >= 0.5f;
        double corrLR = 0, corrLL = 0, corrRR = 0;
        uint32_t w = ringW_.load(std::memory_order_relaxed);
        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            if (!frozen) {
                ringL_[w & (kRing - 1)] = l;
                ringR_[w & (kRing - 1)] = r;
                ++w;
            }
            corrLR += (double)l * r; corrLL += (double)l * l; corrRR += (double)r * r;
            // K-weighted loudness (BS.1770): shelf then high-pass, per channel.
            const float kl = kFilter(l, ks_[0]), kr = kFilter(r, ks_[1]);
            lufsAcc_ += (double)kl * kl + (double)kr * kr;
            if (++lufsN_ >= lufsBlock_) {
                lufsRing_[lufsIdx_] = lufsAcc_ / (lufsN_ * 2);
                lufsIdx_ = (lufsIdx_ + 1) % kLufsBlocks;
                lufsAcc_ = 0.0; lufsN_ = 0;
                double sum = 0; for (double e : lufsRing_) sum += e;
                const double ms = sum / kLufsBlocks;
                lufs_.store(ms > 1e-12 ? (float)(-0.691 + 10.0 * std::log10(ms)) : -120.0f, std::memory_order_relaxed);
            }
        }
        if (!frozen) ringW_.store(w, std::memory_order_relaxed);
        if (corrLL > 1e-12 && corrRR > 1e-12) {
            const float c = (float)(corrLR / std::sqrt(corrLL * corrRR));
            const float prev = corr_.load(std::memory_order_relaxed);
            corr_.store(prev + 0.2f * (c - prev), std::memory_order_relaxed);
        }
        frameSeq_.fetch_add(1, std::memory_order_relaxed);
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        std::lock_guard<std::mutex> lock(mx_);
        ensureAnalyzed();
        out[M_PeakHz] = mPeakHz_;
        out[M_PeakDb] = mPeakDb_;
        out[M_RmsDb] = mRmsDb_;
        out[M_LevelDb] = mLevelDb_;
        out[M_CrestDb] = mLevelDb_ - mRmsDb_;
        out[M_Corr] = corr_.load(std::memory_order_relaxed);
        out[M_Lufs] = lufs_.load(std::memory_order_relaxed);
        out[M_TrigOk] = trigOk_ ? 1.0f : 0.0f;
        out[M_PeriodMs] = mPeriodMs_;
        out[M_FreqHz] = mPeriodMs_ > 1e-6f ? 1000.0f / mPeriodMs_ : 0.0f;
        out[M_Vpp] = mVpp_;
        out[M_Vrms] = mVrms_;
        out[M_WindowMs] = (float)(windowSamples() * 1000.0 / sr_);
        out[M_BinHz] = fftN_ > 0 ? (float)(sr_ / fftN_) : 0.0f;
        out[M_FftN] = (float)fftN_;
        out[M_FftMs] = fftN_ > 0 ? (float)(fftN_ * 1000.0 / sr_) : 0.0f;
        out[M_CursorAv] = traceAt(get(CursorA));
        out[M_CursorBv] = traceAt(get(CursorB));
        out[M_SampleRate] = (float)sr_;
        out[M_Held] = (get(Freeze) >= 0.5f || (trigModeIdx() == 2 && singleDone_)) ? 1.0f : 0.0f;
        return kScope;
    }

    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        std::lock_guard<std::mutex> lock(mx_);
        ensureAnalyzed();
        switch (layer) {
            case L_Spectrum: return copyOut(curve_.data(), kCurve, out, maxSamples);
            case L_Peak:     return copyOut(peak_.data(), kCurve, out, maxSamples);
            case L_Trace:    return copyOut(trace_.data(), kTrace, out, maxSamples);
            case L_Raw:      return copyOut(rawWin_.data(), rawLen_, out, maxSamples);
            case L_Waterfall:return copyOut(wfRow_.data(), kWfBins, out, maxSamples);
            default: return 0;
        }
    }

    void deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        switch (id) {
            case A_Rearm: armed_ = true; singleDone_ = false; break;
            case A_ResetPeak:
                for (int i = 0; i < kCurve; ++i) { peak_[i] = -200.0f; peakAge_[i] = 0.0f; }
                break;
            case A_ResetTrigger: trigPos_ = 0; trigOk_ = false; break;
            default: break;
        }
    }

    // Text reports, for MCP (get_device_text) and the smoke test.
    std::string deviceText(int32_t id) const override {
        std::lock_guard<std::mutex> lock(mx_);
        ensureAnalyzed();
        char b[256];
        switch (id) {
            case 0: {   // one-line summary
                std::snprintf(b, sizeof b,
                    "%s · %s · peak %.1f Hz %.1f dB · rms %.1f dB · crest %.1f dB · LUFS %.1f · corr %+.2f",
                    viewName(), sourceName(), mPeakHz_, mPeakDb_, mRmsDb_,
                    mLevelDb_ - mRmsDb_, lufs_.load(std::memory_order_relaxed),
                    corr_.load(std::memory_order_relaxed));
                return b;
            }
            case 1: {   // third-octave band table
                std::string s;
                for (int i = 0; i < kBands; i++) {
                    std::snprintf(b, sizeof b, "%.0f Hz\t%.1f dB\n", kBandHz[i], bandDb(kBandHz[i]));
                    s += b;
                }
                return s;
            }
            case 2: {   // scope measurements
                const double winMs = windowSamples() * 1000.0 / sr_;
                std::snprintf(b, sizeof b,
                    "trigger %s · window %.2f ms · period %.3f ms (%.1f Hz) · Vpp %.3f · Vrms %.3f",
                    trigOk_ ? "locked" : "free-run", winMs, mPeriodMs_,
                    mPeriodMs_ > 1e-6f ? 1000.0f / mPeriodMs_ : 0.0f, mVpp_, mVrms_);
                return b;
            }
            case 3: {   // strongest spectral peaks, with note names
                std::string s;
                for (auto& pk : topPeaks(8)) {
                    std::snprintf(b, sizeof b, "%.1f Hz\t%.1f dB\t%s\n", pk.hz, pk.db, noteName(pk.hz).c_str());
                    s += b;
                }
                return s.empty() ? "(no peaks above the floor)" : s;
            }
            case 4: {   // cursor measurements
                const double winSec = windowSamples() / sr_;
                const double a = get(CursorA) * winSec, bb = get(CursorB) * winSec;
                const double dt = std::fabs(bb - a);
                std::snprintf(b, sizeof b, "A %.3f ms · B %.3f ms · dt %.3f ms (%.1f Hz) · dV %.3f",
                    a * 1000, bb * 1000, dt * 1000, dt > 1e-9 ? 1.0 / dt : 0.0,
                    traceAt(get(CursorB)) - traceAt(get(CursorA)));
                return b;
            }
            default: return {};
        }
    }

    const char* displayName() const override { return "Nota Lens"; }
    int32_t     builtinKind() const override { return 22; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = {
            "View", "Freeze", "Source", "Mid/Side",
            "Spectrum FFT", "Spectrum Window", "Spectrum Average", "Spectrum Smooth",
            "Spectrum Decay", "Spectrum Tilt", "Spectrum Floor", "Spectrum Peak Hold", "Spectrum Peak Time",
            "Scale Top", "Scale Range",
            "Scope Time/Div", "Scope Volt/Div", "Scope Trigger", "Scope Edge", "Scope Level",
            "Scope Holdoff", "Scope Persist", "Scope Persist Time", "Scope Traces", "Scope Bright",
            "Cursor On", "Cursor A", "Cursor B", "Cursor Snap",
            "Waterfall Speed", "Waterfall Gain", "Waterfall Floor", "Waterfall Contrast", "Waterfall Overlap",
            "Display Log Freq", "Display Note Grid", "Display Rate",
        };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // ---- unit helpers, shared with the card so both read the same numbers ----
    static double timeDivSec(float v) { return expMap(v, 0.00002, 0.02); }
    static double voltDiv(float v) { return expMap(v, 0.02, 2.0); }
    static int    fftSizeOf(float v) { static const int n[4] = { 512, 2048, 4096, 16384 }; return n[std::clamp((int)std::lround(v * 3.0f), 0, 3)]; }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kRing = 32768;      // power of two, per channel
    static constexpr int kMaxFft = 16384;
    static constexpr int kMaxWin = 16384;    // longest scope window we keep raw
    static constexpr int kLufsBlocks = 50;   // 50 × 8 ms = 400 ms momentary window
    static constexpr int kBands = 31;        // ISO third-octave centres, 20 Hz … 20 kHz

    static constexpr double kBandHz[kBands] = {
        20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630,
        800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000 };

    struct Peak { double hz; double db; };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static int step(float v, int n) { return std::clamp((int)std::lround(v * (n - 1)), 0, n - 1); }
    int trigModeIdx() const { return step(get(TrigMode), 3); }

    static int32_t copyOut(const float* src, int n, float* out, int32_t maxSamples) {
        const int32_t k = std::min<int32_t>(n, maxSamples);
        if (k > 0) std::copy(src, src + k, out);
        return k;
    }

    // ---- K-weighting (BS.1770) ------------------------------------------
    struct Biquad { double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
    struct KState { double z1[2] = { 0, 0 }, z2[2] = { 0, 0 }; };

    void kInit() {
        {   // stage 1: high shelf, +4 dB at 1681.97 Hz
            const double f0 = 1681.974450955533, Q = 0.7071752369554196, G = 3.999843853973347;
            const double K = std::tan(kPi * f0 / sr_), Vh = std::pow(10.0, G / 20.0);
            const double Vb = std::pow(Vh, 0.4996667741545416);
            const double a0 = 1 + K / Q + K * K;
            kb_[0] = { (Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0,
                       2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0 };
        }
        {   // stage 2: high pass at 38.14 Hz
            const double f0 = 38.13547087602444, Q = 0.5003270373238773;
            const double K = std::tan(kPi * f0 / sr_);
            const double a0 = 1 + K / Q + K * K;
            kb_[1] = { 1.0 / a0, -2.0 / a0, 1.0 / a0, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0 };
        }
        ks_[0] = ks_[1] = {};
    }
    float kFilter(float x, KState& st) const {
        double y = x;
        for (int s = 0; s < 2; ++s) {
            const Biquad& c = kb_[s];
            const double o = c.b0 * y + st.z1[s];
            st.z1[s] = c.b1 * y - c.a1 * o + st.z2[s];
            st.z2[s] = c.b2 * y - c.a2 * o;
            y = o;
        }
        return (float)y;
    }

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
    int  srcIdx() const { return step(get(Source), 3); }

    int windowSamples() const {
        return std::clamp((int)std::lround(timeDivSec(get(TimeDiv)) * 8.0 * sr_), 16, kMaxWin);
    }

    float traceAt(float pos) const {
        const double x = std::clamp((double)pos, 0.0, 1.0) * (kTrace - 1);
        const int i = std::min((int)x, kTrace - 2);
        const double f = x - i;
        return (float)(trace_[i] * (1 - f) + trace_[i + 1] * f);
    }

    // ---- the analysis pass ------------------------------------------------
    // Called under mx_ from every read entry point; recomputes at most once per
    // 1/Rate seconds and only when the audio thread has produced a new block.
    void ensureAnalyzed() const {
        const double now = nowSeconds();
        const double period = 1.0 / std::clamp(10.0 + get(Rate) * 50.0, 10.0, 60.0);
        const uint32_t seq = frameSeq_.load(std::memory_order_relaxed);
        if (lastAnalyzeS_ >= 0 && now - lastAnalyzeS_ < period && seq == lastSeq_) return;
        const double dt = lastAnalyzeS_ < 0 ? period : std::min(now - lastAnalyzeS_, 0.5);
        lastAnalyzeS_ = now; lastSeq_ = seq;

        analyzeSpectrum(dt);
        analyzeScope();
    }

    void analyzeSpectrum(double dt) const {
        const int n = fftSizeOf(get(FftSize));
        if (n != fftN_) {
            fft_.resize((size_t)n);
            freq_.assign((size_t)n / 2, {});
            time_.assign((size_t)n, 0.0f);
            win_.assign((size_t)n, 0.0f);
            std::fill(avg_.begin(), avg_.end(), -200.0f);   // bins mean new frequencies now
            fftN_ = n; winKind_ = -1;
        }
        const int wk = step(get(Window), 3);
        if (wk != winKind_) { buildWindow(wk, n); winKind_ = wk; }

        const uint32_t head = ringW_.load(std::memory_order_relaxed);
        const uint32_t start = head - (uint32_t)n;
        const bool ms = msOn(); const int src = srcIdx();
        double sumSq = 0; float peakLin = 0;
        for (int i = 0; i < n; ++i) {
            const float s = pick(start + (uint32_t)i, ms, src);
            sumSq += (double)s * s;
            peakLin = std::max(peakLin, std::fabs(s));
            time_[i] = s * win_[i];
        }
        mRmsDb_ = (float)db(std::sqrt(sumSq / n));
        mLevelDb_ = (float)db(peakLin);

        fft_.fft(time_.data(), freq_.data());
        const int bins = n / 2;
        const double ref = winSum_ * 0.5;               // full-scale sine → 0 dBFS
        const double alpha = 1.0 / std::max(1.0, std::round(1.0 + get(Average) * 15.0));
        for (int k = 1; k < bins; ++k) {
            const double mag = std::abs(freq_[(size_t)k]);
            const double d = db(mag / std::max(1e-12, ref));
            float& a = avg_[(size_t)k];
            if (a < -190.0f) a = (float)d; else a = (float)(a + alpha * (d - a));
            binDb_[(size_t)k] = a;
        }
        binDb_[0] = binDb_[1];

        // Neighbour smoothing: a widening box over the log axis reads as the
        // classic "1/6 octave" smoothing without a second FFT.
        // Averaged in the POWER domain, not in dB: a dB-domain box drags a narrow peak
        // several dB down and the readout stops matching the signal.
        const int sm = (int)std::lround(get(Smooth) * 6.0);
        if (sm > 0) {
            smTmp_.resize((size_t)bins);
            for (int k = 0; k < bins; ++k) smTmp_[(size_t)k] = (float)std::pow(10.0, binDb_[(size_t)k] * 0.1);
            for (int k = 0; k < bins; ++k) {
                const int w = 1 + (int)(sm * (0.2 + 2.0 * k / (double)bins));
                const int j0 = std::max(0, k - w), j1 = std::min(bins - 1, k + w);
                double run = 0;
                for (int j = j0; j <= j1; ++j) run += smTmp_[(size_t)j];
                binDb_[(size_t)k] = (float)(10.0 * std::log10(std::max(1e-20, run / (j1 - j0 + 1))));
            }
        }

        // Map the bins onto the log-frequency curve, applying tilt and floor,
        // then apply the fall ballistic and the peak hold.
        const double fLo = 20.0, fHi = std::min(20000.0, sr_ * 0.45);
        const double tilt = get(Tilt) * 9.0;
        const double floorDb = -120.0 + get(Floor) * 80.0;
        const double fallDb = dt / std::max(0.01, expMap(get(Decay), 0.05, 8.0)) * 90.0;
        const double holdS = expMap(get(PeakTime), 0.5, 30.0);
        const bool peakOn = get(PeakHold) >= 0.5f;
        double best = -400, bestHz = 0;
        for (int i = 0; i < kCurve; ++i) {
            const double f = fLo * std::pow(fHi / fLo, i / (double)(kCurve - 1));
            const double f0 = i == 0 ? f : std::sqrt(f * fLo * std::pow(fHi / fLo, (i - 1) / (double)(kCurve - 1)));
            const double f1 = i == kCurve - 1 ? f : std::sqrt(f * fLo * std::pow(fHi / fLo, (i + 1) / (double)(kCurve - 1)));
            const double binHz = sr_ / n;
            const int k0 = std::clamp((int)std::floor(f0 / binHz), 1, bins - 1);
            const int k1 = std::clamp((int)std::ceil(f1 / binHz), 1, bins - 1);
            double v;
            if (k1 <= k0) {
                const double x = std::clamp(f / binHz, 1.0, bins - 1.0);
                const int ka = (int)x; const double fr = x - ka;
                v = binDb_[(size_t)ka] * (1 - fr) + binDb_[(size_t)std::min(ka + 1, bins - 1)] * fr;
            } else {
                v = -400;
                for (int k = k0; k <= k1; ++k) v = std::max(v, (double)binDb_[(size_t)k]);
            }
            v += tilt * std::log2(f / 1000.0);
            v = std::max(v, floorDb);
            if (v > best) { best = v; bestHz = f; }

            float& c = curve_[(size_t)i];
            c = v >= c ? (float)v : (float)std::max(v, c - fallDb);
            if (peakOn) {
                if (c >= peak_[(size_t)i]) { peak_[(size_t)i] = c; peakAge_[(size_t)i] = 0.0f; }
                else {
                    peakAge_[(size_t)i] += (float)dt;
                    if (peakAge_[(size_t)i] > holdS)
                        peak_[(size_t)i] = (float)std::max((double)c, peak_[(size_t)i] - fallDb * 0.5);
                }
            } else { peak_[(size_t)i] = c; peakAge_[(size_t)i] = 0.0f; }
        }
        mPeakHz_ = (float)bestHz;
        mPeakDb_ = (float)best;

        // Waterfall row: the same curve, resampled and normalized to 0..1.
        const double wfGain = -24.0 + get(WfGain) * 48.0;
        const double wfFloor = -120.0 + get(WfFloor) * 80.0;
        const double wfTop = std::max(wfFloor + 6.0, -6.0 + wfGain);
        const double contrast = 0.5 + get(WfContrast) * 1.5;
        for (int i = 0; i < kWfBins; ++i) {
            const double x = i * (kCurve - 1) / (double)(kWfBins - 1);
            const int a = (int)x; const double fr = x - a;
            const double v = curve_[(size_t)a] * (1 - fr) + curve_[(size_t)std::min(a + 1, kCurve - 1)] * fr + wfGain;
            wfRow_[(size_t)i] = (float)std::clamp(std::pow(std::clamp((v - wfFloor) / (wfTop - wfFloor), 0.0, 1.0), 1.0 / contrast), 0.0, 1.0);
        }
    }

    void buildWindow(int kind, int n) const {
        double sum = 0;
        for (int i = 0; i < n; ++i) {
            const double x = 2 * kPi * i / (n - 1);
            double w;
            switch (kind) {
                case 1:   // Blackman-Harris
                    w = 0.35875 - 0.48829 * std::cos(x) + 0.14128 * std::cos(2 * x) - 0.01168 * std::cos(3 * x);
                    break;
                case 2:   // Flat top
                    w = 0.21557895 - 0.41663158 * std::cos(x) + 0.277263158 * std::cos(2 * x)
                      - 0.083578947 * std::cos(3 * x) + 0.006947368 * std::cos(4 * x);
                    break;
                default:  // Hann
                    w = 0.5 - 0.5 * std::cos(x);
                    break;
            }
            win_[(size_t)i] = (float)w;
            sum += w;
        }
        winSum_ = sum;
    }

    // Trigger search + trace capture. The trace is pinned to the trigger point
    // (25 % pre-trigger, like the mockup's marker) so it stands still.
    void analyzeScope() const {
        const int win = windowSamples();
        const int pre = win / 4;
        const uint32_t head = ringW_.load(std::memory_order_relaxed);
        const bool frozen = get(Freeze) >= 0.5f;
        const int mode = trigModeIdx();                 // 0 Auto · 1 Normal · 2 Single
        if (frozen || (mode == 2 && singleDone_)) return;   // hold the captured trace

        const float level = (get(TrigLevel) - 0.5f) * 2.0f;
        const bool rising = get(TrigEdge) < 0.5f;
        const int holdoff = (int)std::lround(get(Holdoff) * 0.2 * sr_);
        const bool ms = msOn(); const int src = srcIdx();

        // The newest sample index whose window is fully inside the ring.
        const uint32_t newest = head - (uint32_t)(win - pre) - 1;
        const uint32_t oldest = head - (uint32_t)(kRing - 8);
        const uint32_t limit = newest - (uint32_t)std::min(kRing - win - 8, 4 * win + 2048);

        bool found = false; uint32_t at = 0;
        for (uint32_t t = newest; (int32_t)(t - limit) > 0 && (int32_t)(t - oldest) > 0; --t) {
            if (holdoff > 0 && trigOk_ && (int32_t)(t - trigPos_) < holdoff && (int32_t)(t - trigPos_) > -holdoff) continue;
            const float a = pick(t - 1, ms, src), b = pick(t, ms, src);
            if (rising ? (a < level && b >= level) : (a > level && b <= level)) { found = true; at = t; break; }
        }

        if (!found) {
            if (mode == 0) { at = newest; trigOk_ = false; }   // Auto: free-run
            else return;                                        // Normal / Single: keep the last trace
        } else {
            trigOk_ = true; trigPos_ = at;
            if (mode == 2) { if (!armed_) return; armed_ = false; singleDone_ = true; }
        }

        // Raw window (for export) + the decimated trace, peak-preserving so a
        // transient survives a 400 ms window squeezed into 512 points.
        const uint32_t s0 = at - (uint32_t)pre;
        rawLen_ = std::min(win, kMaxWin);
        for (int i = 0; i < rawLen_; ++i) rawWin_[(size_t)i] = pick(s0 + (uint32_t)i, ms, src);

        double sumSq = 0; float lo = 1e9f, hi = -1e9f;
        for (int i = 0; i < kTrace; ++i) {
            const int a = (int)((int64_t)i * win / kTrace);
            const int b = std::max(a + 1, (int)((int64_t)(i + 1) * win / kTrace));
            float best = 0;
            for (int j = a; j < b && j < win; ++j) {
                const float v = rawWin_[(size_t)std::min(j, rawLen_ - 1)];
                if (std::fabs(v) > std::fabs(best)) best = v;
            }
            trace_[(size_t)i] = best;
        }
        for (int i = 0; i < rawLen_; ++i) {
            const float v = rawWin_[(size_t)i];
            sumSq += (double)v * v; lo = std::min(lo, v); hi = std::max(hi, v);
        }
        mVpp_ = rawLen_ > 0 ? hi - lo : 0.0f;
        mVrms_ = rawLen_ > 0 ? (float)std::sqrt(sumSq / rawLen_) : 0.0f;
        mPeriodMs_ = (float)(measurePeriod() * 1000.0);
    }

    // Period by autocorrelation over the captured window (0 when nothing locks).
    double measurePeriod() const {
        const int n = rawLen_;
        if (n < 32) return 0.0;
        const int maxLag = std::min(n / 2, (int)(sr_ / 20.0));
        const int minLag = std::max(2, (int)(sr_ / 20000.0));
        double e0 = 0; for (int i = 0; i < n; ++i) e0 += (double)rawWin_[(size_t)i] * rawWin_[(size_t)i];
        if (e0 < 1e-9) return 0.0;
        double bestV = 0; int bestLag = 0; bool rose = false;
        for (int lag = minLag; lag < maxLag; ++lag) {
            double s = 0;
            for (int i = 0; i + lag < n; ++i) s += (double)rawWin_[(size_t)i] * rawWin_[(size_t)(i + lag)];
            const double norm = s / e0;
            if (!rose) { if (norm < 0.2) rose = true; continue; }   // skip the lag-0 lobe
            if (norm > bestV) { bestV = norm; bestLag = lag; }
        }
        return bestV > 0.35 && bestLag > 0 ? bestLag / sr_ : 0.0;
    }

    double bandDb(double hz) const {
        const double fLo = 20.0, fHi = std::min(20000.0, sr_ * 0.45);
        if (hz <= fLo) return curve_[0];
        if (hz >= fHi) return curve_[kCurve - 1];
        const double x = std::log(hz / fLo) / std::log(fHi / fLo) * (kCurve - 1);
        const int i = std::min((int)x, kCurve - 2);
        const double f = x - i;
        return curve_[(size_t)i] * (1 - f) + curve_[(size_t)(i + 1)] * f;
    }

    std::vector<Peak> topPeaks(int want) const {
        const double fLo = 20.0, fHi = std::min(20000.0, sr_ * 0.45);
        const double floorDb = -120.0 + get(Floor) * 80.0 + 3.0;
        std::vector<Peak> out;
        for (int i = 2; i < kCurve - 2; ++i) {
            const float v = curve_[(size_t)i];
            if (v <= floorDb) continue;
            if (v < curve_[(size_t)(i - 1)] || v < curve_[(size_t)(i + 1)]) continue;
            if (v < curve_[(size_t)(i - 2)] || v < curve_[(size_t)(i + 2)]) continue;
            out.push_back({ fLo * std::pow(fHi / fLo, i / (double)(kCurve - 1)), v });
        }
        std::sort(out.begin(), out.end(), [](const Peak& a, const Peak& b) { return a.db > b.db; });
        if ((int)out.size() > want) out.resize((size_t)want);
        return out;
    }

    static std::string noteName(double hz) {
        if (hz <= 0) return "—";
        const double midi = 69.0 + 12.0 * std::log2(hz / 440.0);
        const int n = (int)std::lround(midi);
        const int cents = (int)std::lround((midi - n) * 100.0);
        static const char* nm[12] = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        char b[32];
        std::snprintf(b, sizeof b, "%s%d %+d ct", nm[((n % 12) + 12) % 12], n / 12 - 1, cents);
        return b;
    }

    const char* viewName() const { static const char* v[3] = { "Spectrum", "Scope", "Waterfall" }; return v[step(get(View), 3)]; }
    const char* sourceName() const {
        const bool ms = get(MidSide) >= 0.5f;
        static const char* lr[3] = { "L+R", "L", "R" };
        static const char* msn[3] = { "Mid", "Mid", "Side" };
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
    std::atomic<uint32_t> ringW_{ 0 };
    std::atomic<uint32_t> frameSeq_{ 0 };
    std::atomic<float> corr_{ 0.0f }, lufs_{ -120.0f };

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
