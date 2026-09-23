// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota EQ-3 (device kind 16) — a three-band DJ-style isolator, a PERFORMANCE EQ rather
// than a small EQ-8: three fixed bands (Low / Mid / High) split by two crossover frequencies,
// each band played by a gain fader and a KILL that removes it outright.
//
//   in ─▶ LR split @ low/mid ─┬─ low ─▶ allpass @ mid/high ─▶ × low gain  ─┐
//                             └─ rest ─▶ LR split @ mid/high ┬ mid  ▶ × mid  ─┼─▶ × output ─▶ out
//                                                           └ high ▶ × high ─┘
//
// The split is a Linkwitz-Riley tree (24 dB/oct = LR4: two Butterworth-2 sections; 48 dB/oct = LR8:
// a Butterworth-4 twice, sections at Q 0.54 / 1.31) of TPT state-variable filters. The low band
// also runs through the mid/high split summed back (an allpass), so it stays in phase with the
// other two and at unity gains the three bands sum flat. Gains and kills glide over ~5 ms, so a
// kill never clicks.
//
// Range picks the fader law: Isolator (1, the default) spans −24 … +6 dB (0 dB at 0.8, the
// mixer-style isolator the card is drawn for); Classic (0) spans ±15 dB (0 dB at 0.5) — the
// original EQ-3 law.
//
// All params are normalized 0..1 and APPEND ONLY: 0..9 are the original layout, 10 Range is
// appended. A save from before Range carries only 0..9 and was made in the Classic law, so
// the loaders put Range back to Classic for it (paramsRestored, ProjectService, PresetService)
// — older projects, racks and presets open unchanged. Persistence / automation / clone flow
// generically through the base Device.
//
// Telemetry (scopeRead): kTele live values (peaks, per-band levels, the crossovers in use, the
// gains in effect, sample rate, CPU), then the output spectrum — kSpec log bands 20 Hz..20 kHz
// in dB from an FFT over the last ~85 ms, analysed on the reading thread (≤ 30 Hz).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset the meters (peak holds and the spectrum average).
// Header-only, allocation-free after setSampleRate. JUCE-free.

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

class Eq3 : public Device {
public:
    enum {
        Low = 0,    // low-band gain   (law: Range)
        Mid,        // mid-band gain
        High,       // high-band gain
        LowKill,    // low band killed  (>= 0.5)
        MidKill,    // mid band killed
        HighKill,   // high band killed
        FreqLo,     // low/mid crossover  (exp 50..2000 Hz, default 250 Hz)
        FreqHi,     // mid/high crossover (exp 500..18000 Hz, default 2.5 kHz)
        Slope,      // 0 = 24 dB/oct (LR4), 1 = 48 dB/oct (LR8)
        Gain,       // output gain (0.5 = 0 dB, ±24 dB)
        Range,      // fader law: 0 = Classic ±15 dB, 1 = Isolator −24 … +6 dB (appended)
        kNumParams
    };
    static constexpr int kLegacyParams = 10;   // the layout before Range

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,      // input peak (300 ms fall), dBFS
        S_OutPeak,         // output peak (300 ms fall), dBFS
        S_Level,           // 3 × band peak after its gain (300 ms fall), dBFS: low, mid, high
        S_SampleRate = 5,
        S_Cpu,             // share of real time spent in process()
        S_Analysed,        // 1 when the spectrum is valid
        S_Latency,         // samples (always 0)
        S_F1,              // low/mid crossover in use, Hz
        S_F2,              // mid/high crossover in use, Hz
        S_Gain,            // 3 × gain in effect now (linear, after the kill glide)
        S_Bands = 14,      // kSpec
        kTele = 16
    };
    static constexpr int kSpec = 96;
    static constexpr double kSpecLo = 20.0, kSpecHi = 20000.0;
    static constexpr int kScope = kTele + kSpec;

    Eq3() {
        p_[Low].store(0.8f);   p_[Mid].store(0.8f);  p_[High].store(0.8f);   // 0 dB (Isolator)
        p_[LowKill].store(0.0f); p_[MidKill].store(0.0f); p_[HighKill].store(0.0f);
        p_[FreqLo].store(0.4363f);   // ~250 Hz
        p_[FreqHi].store(0.4491f);   // ~2.5 kHz
        p_[Slope].store(0.0f);       // 24 dB/oct
        p_[Gain].store(0.5f);        // 0 dB
        p_[Range].store(1.0f);       // Isolator
        for (auto& a : lvlA_) a.store(-120.0f);
        for (auto& a : gA_) a.store(1.0f);
        setSampleRate(44100.0, 0);
    }

    // Musical mappings, shared with the UI / MCP (they mirror these).
    static float gainDb(float v, bool isolator) { return isolator ? -24.0f + 30.0f * v : (v - 0.5f) * 30.0f; }
    static float gainNorm(float db, bool isolator) {
        return std::clamp(isolator ? (db + 24.0f) / 30.0f : db / 30.0f + 0.5f, 0.0f, 1.0f);
    }
    static double freqLoHz(float v) { return expMap(v, 50.0, 2000.0); }
    static double freqHiHz(float v) { return expMap(v, 500.0, 18000.0); }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        for (auto& s : sec_) s.reset();
        gLcur_ = gMcur_ = gHcur_ = 1.0f;
        primed_ = false;
        inPk_ = outPk_ = 0.0f;
        for (auto& v : bandPk_) v = 0.0f;
        fftN_ = sr_ > 50000.0 ? 8192 : 4096;
        size_t ring = 1;
        while (ring < (size_t)fftN_ * 2) ring <<= 1;
        ring_.assign(ring, 0.0f);
        ringMask_ = (uint32_t)(ring - 1);
        ringW_.store(0, std::memory_order_relaxed);
        fft_.resize((size_t)fftN_);
        win_.assign((size_t)fftN_, 0.0f);
        tBuf_.assign((size_t)fftN_, 0.0f);
        fBuf_.assign((size_t)fftN_ / 2, {});
        for (int i = 0; i < fftN_; ++i) win_[(size_t)i] = (float)(0.5 - 0.5 * std::cos(2.0 * kPi * i / fftN_));
        lastAnaS_ = -1.0; lastAnaW_ = 0; anaValid_ = false;
        for (int b = 0; b < kSpec; ++b) { aSpec_[b] = 0.0; spec_[b] = -120.0f; }
    }

    // A loader restored the first `count` params from a save. One made before Range existed
    // was made in the Classic ±15 dB law — keep it sounding the same.
    void paramsRestored(int32_t count) override {
        if (count >= 0 && count <= kLegacyParams) p_[Range].store(0.0f, std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        // Crossover frequencies (keep f1 below f2 so the mid band stays sane).
        double f1 = freqLoHz(get(FreqLo));
        double f2 = freqHiHz(get(FreqHi));
        const double nyq = std::min(20000.0, sr_ * 0.45);
        f1 = std::clamp(f1, 20.0, nyq);
        f2 = std::clamp(f2, 20.0, nyq);
        if (f1 > f2 * 0.98) f1 = f2 * 0.98;
        f1A_.store((float)f1, std::memory_order_relaxed);
        f2A_.store((float)f2, std::memory_order_relaxed);

        // LR4 = Butterworth-2 twice (Q 1/√2); LR8 = Butterworth-4 twice (Q 0.5412, 1.3066).
        const bool lr8 = get(Slope) >= 0.5f;
        const int nSec = lr8 ? 4 : 2;
        Coeffs c1[kMaxSec], c2[kMaxSec];
        for (int s = 0; s < nSec; ++s) {
            const double k = lr8 ? (s % 2 == 0 ? 1.8477590650 : 0.7653668647) : 1.4142135624;   // 1/Q
            c1[s] = coeffsFor(f1, k); c2[s] = coeffsFor(f2, k);
        }

        // Per-band linear target gains (a KILL fully removes the band).
        const bool iso = get(Range) >= 0.5f;
        const float gLtar = get(LowKill)  >= 0.5f ? 0.0f : dbToLin(gainDb(get(Low),  iso));
        const float gMtar = get(MidKill)  >= 0.5f ? 0.0f : dbToLin(gainDb(get(Mid),  iso));
        const float gHtar = get(HighKill) >= 0.5f ? 0.0f : dbToLin(gainDb(get(High), iso));
        if (!primed_) { gLcur_ = gLtar; gMcur_ = gMtar; gHcur_ = gHtar; primed_ = true; }
        const float smooth = (float)(1.0 - std::exp(-1.0 / (0.005 * sr_)));  // ~5 ms glide (click-free kills)
        const float outGain = std::pow(10.0f, (get(Gain) - 0.5f) * 48.0f / 20.0f);

        if (resetPk_.exchange(false, std::memory_order_acq_rel)) { inPk_ = outPk_ = 0.0f; for (auto& v : bandPk_) v = 0.0f; }
        float inPk = 0.0f, outPk = 0.0f, bPk[3] = { 0.0f, 0.0f, 0.0f };
        uint32_t w = ringW_.load(std::memory_order_relaxed);
        const bool ringOk = !ring_.empty();
        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(l), std::fabs(r)));

            gLcur_ += (gLtar - gLcur_) * smooth;
            gMcur_ += (gMtar - gMcur_) * smooth;
            gHcur_ += (gHtar - gHcur_) * smooth;

            double bl[3], br[3];
            splitOne(0, l, nSec, c1, c2, bl);
            splitOne(1, r, nSec, c1, c2, br);
            const double g[3] = { gLcur_, gMcur_, gHcur_ };
            double ol = 0.0, orr = 0.0;
            for (int b = 0; b < 3; ++b) {
                const double xl = g[b] * bl[b], xr = g[b] * br[b];
                ol += xl; orr += xr;
                bPk[b] = std::max(bPk[b], (float)std::max(std::fabs(xl), std::fabs(xr)));
            }
            const float yl = (float)(ol * outGain), yr = (float)(orr * outGain);
            buf[i * 2] = yl; buf[i * 2 + 1] = yr;
            outPk = std::max(outPk, std::max(std::fabs(yl), std::fabs(yr)));
            if (ringOk) ring_[(w++) & ringMask_] = 0.5f * (yl + yr);
        }
        ringW_.store(w, std::memory_order_release);

        // Publish: peaks falling over 300 ms, band levels, gains in effect, CPU.
        const double blockS = frames > 0 ? frames / sr_ : 0.0;
        const float fall = (float)std::exp(-blockS / 0.3);
        inPk_ = std::max(inPk, inPk_ * fall);
        outPk_ = std::max(outPk, outPk_ * fall);
        inPkA_.store(db(inPk_), std::memory_order_relaxed);
        outPkA_.store(db(outPk_), std::memory_order_relaxed);
        for (int b = 0; b < 3; ++b) {
            bandPk_[b] = std::max(bPk[b], bandPk_[b] * fall);
            lvlA_[b].store(db(bandPk_[b]), std::memory_order_relaxed);
        }
        gA_[0].store(gLcur_, std::memory_order_relaxed);
        gA_[1].store(gMcur_, std::memory_order_relaxed);
        gA_[2].store(gHcur_, std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / blockS) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    const char* displayName() const override { return "Nota EQ-3"; }
    int32_t     builtinKind() const override { return 16; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Low: return "Low"; case Mid: return "Mid"; case High: return "High";
            case LowKill: return "Low Kill"; case MidKill: return "Mid Kill"; case HighKill: return "High Kill";
            case FreqLo: return "Low Freq"; case FreqHi: return "High Freq";
            case Slope: return "Slope"; case Gain: return "Gain"; case Range: return "Range";
            default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v)) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    // Telemetry block, then the output spectrum. Writes as much of that layout as fits in
    // maxSamples; a read no longer than kTele skips the FFT. UI / MCP thread.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeak]    = outPkA_.load(std::memory_order_relaxed);
        for (int b = 0; b < 3; ++b) {
            t[S_Level + b] = lvlA_[b].load(std::memory_order_relaxed);
            t[S_Gain + b]  = gA_[b].load(std::memory_order_relaxed);
        }
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency]    = 0.0f;
        t[S_F1]         = f1A_.load(std::memory_order_relaxed);
        t[S_F2]         = f2A_.load(std::memory_order_relaxed);
        t[S_Bands]      = (float)kSpec;
        int32_t n = 0;
        if (maxSamples > kTele) {
            std::lock_guard<std::mutex> lock(mx_);
            t[S_Analysed] = analyse() ? 1.0f : 0.0f;
            for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
            for (int b = 0; b < kSpec && n < maxSamples; ++b) out[n++] = spec_[b];
            return n;
        }
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        return n;
    }

    void deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) override {
        if (id != 0) return;
        std::lock_guard<std::mutex> lock(mx_);
        resetPk_.store(true, std::memory_order_release);   // the audio thread drops its holds
        inPkA_.store(-120.0f, std::memory_order_relaxed);
        outPkA_.store(-120.0f, std::memory_order_relaxed);
        for (auto& a : lvlA_) a.store(-120.0f, std::memory_order_relaxed);
        lastAnaS_ = -1.0; anaValid_ = false;
        for (int b = 0; b < kSpec; ++b) { aSpec_[b] = 0.0; spec_[b] = -120.0f; }
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        static const char* names[3] = { "low", "mid", "high" };
        char b[256];
        const bool iso = get(Range) >= 0.5f;
        if (id == 0) {
            std::snprintf(b, sizeof b, "LR%d %d dB/oct - low/mid %s - mid/high %s", get(Slope) >= 0.5f ? 8 : 4, get(Slope) >= 0.5f ? 48 : 24,
                          hz(freqLoHz(get(FreqLo))).c_str(), hz(freqHiHz(get(FreqHi))).c_str());
            std::string s = b;
            for (int k = 0; k < 3; ++k) {
                if (get(LowKill + k) >= 0.5f) std::snprintf(b, sizeof b, " | %s kill", names[k]);
                else std::snprintf(b, sizeof b, " | %s %+.1f dB", names[k], gainDb(get(Low + k), iso));
                s += b;
            }
            std::snprintf(b, sizeof b, " | range %s | output %+.1f dB", iso ? "isolator -24..+6 dB" : "classic +-15 dB", (get(Gain) - 0.5f) * 48.0f);
            s += b;
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::snprintf(b, sizeof b, "in %s dBFS - out %s dBFS", lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeak]).c_str());
            std::string s = b;
            for (int k = 0; k < 3; ++k) {
                std::snprintf(b, sizeof b, " | %s %s dBFS (gain now %s dB)", names[k], lv(sc[S_Level + k]).c_str(),
                              lv(sc[S_Gain + k] > 1e-6f ? 20.0f * std::log10(sc[S_Gain + k]) : -120.0f).c_str());
                s += b;
            }
            std::snprintf(b, sizeof b, " | crossovers %s / %s | CPU %.2f %%", hz(sc[S_F1]).c_str(), hz(sc[S_F2]).c_str(), sc[S_Cpu] * 100.0f);
            s += b;
            return s;
        }
        if (id == 2) {
            return "All params 0..1. 0 Low, 1 Mid, 2 High: the band gains - with Range = 1 (Isolator, the default) "
                   "dB = -24 + 30 v (0.8 = 0 dB, 0 = -24, 1 = +6); with Range = 0 (Classic) dB = 30 (v - 0.5) (0.5 = 0 dB, "
                   "+-15). 3 Low Kill, 4 Mid Kill, 5 High Kill: >= 0.5 removes the band (5 ms glide). 6 Low Freq: the low/mid "
                   "crossover, 50 * 40^v Hz (0.4363 = 250 Hz). 7 High Freq: the mid/high crossover, 500 * 36^v Hz (0.4491 = "
                   "2.5 kHz); the low/mid crossover is held below it. 8 Slope: 0 = 24 dB/oct (Linkwitz-Riley 4), 1 = 48 dB/oct "
                   "(LR8). 9 Gain: output, (v - 0.5) * 48 dB. 10 Range: the fader law, 0 Classic / 1 Isolator (switching it "
                   "does not move the gain params - re-set them to keep the same dB).";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int32_t kMaxSec = 4;     // cascade depth for LR8 (48 dB/oct)
    static constexpr int32_t kGroups = 6;     // per channel: f1 LP / HP, f2 LP / HP, the low band's f2 allpass

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float dbToLin(float db) { return std::pow(10.0f, db / 20.0f); }
    static float db(float lin) { return lin > 1e-6f ? std::max(-120.0f, 20.0f * std::log10(lin)) : -120.0f; }
    static std::string hz(double f) {
        char t[24];
        if (f >= 1000.0) std::snprintf(t, sizeof t, "%.1f kHz", f / 1000.0); else std::snprintf(t, sizeof t, "%.0f Hz", f);
        return t;
    }

    struct Coeffs { double a1, a2, a3, k; };
    Coeffs coeffsFor(double fc, double k) const {
        const double g = std::tan(kPi * fc / sr_);
        const double a1 = 1.0 / (1.0 + g * (g + k));
        return { a1, g * a1, g * (g * a1), k };
    }

    // TPT state-variable filter section (one 2nd-order Butterworth stage).
    struct Svf {
        double ic1 = 0, ic2 = 0;
        void reset() { ic1 = ic2 = 0; }
        inline void tick(double x, const Coeffs& c, double& lp, double& hp) {
            const double v3 = x - ic2;
            const double v1 = c.a1 * ic1 + c.a2 * v3;
            const double v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
            ic1 = 2.0 * v1 - ic1;
            ic2 = 2.0 * v2 - ic2;
            lp = v2; hp = x - c.k * v1 - v2;
        }
    };

    // Cascade `n` sections taking the lowpass (or highpass) output forward — n identical
    // Butterworth-2 stages give a 2n-th-order Linkwitz-Riley response.
    static inline double cascadeLP(Svf* s, int n, double x, const Coeffs* c) {
        double lp, hp;
        for (int i = 0; i < n; ++i) { s[i].tick(x, c[i], lp, hp); x = lp; }
        return x;
    }
    static inline double cascadeHP(Svf* s, int n, double x, const Coeffs* c) {
        double lp, hp;
        for (int i = 0; i < n; ++i) { s[i].tick(x, c[i], lp, hp); x = hp; }
        return x;
    }

    // Split one sample into the three bands (serial LR crossover tree); the low band passes the
    // mid/high split summed back — an allpass that keeps it in phase with mid + high.
    inline void splitOne(int ch, float x, int nSec, const Coeffs* c1, const Coeffs* c2, double* band) {
        Svf* g = sec_ + ch * (kMaxSec * kGroups);
        const double rest = cascadeHP(g + kMaxSec * 1, nSec, x, c1);
        const double low  = cascadeLP(g + kMaxSec * 0, nSec, x, c1);
        band[0] = cascadeLP(g + kMaxSec * 4, nSec, low, c2) + cascadeHP(g + kMaxSec * 5, nSec, low, c2);
        band[1] = cascadeLP(g + kMaxSec * 2, nSec, rest, c2);
        band[2] = cascadeHP(g + kMaxSec * 3, nSec, rest, c2);
    }

    // ---- output spectrum (reading thread, rate-limited, under mx_) -----------------------
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
    bool analyse() const {
        if (ring_.empty() || fftN_ <= 0) return false;
        const double now = nowSeconds();
        const uint32_t w = ringW_.load(std::memory_order_acquire);
        if (lastAnaS_ >= 0 && (now - lastAnaS_ < 1.0 / 30.0 || w == lastAnaW_)) return anaValid_;
        if (w < (uint32_t)fftN_) return anaValid_;
        const int n = fftN_;
        const uint32_t start = w - (uint32_t)n;
        for (int i = 0; i < n; ++i) tBuf_[(size_t)i] = ring_[(start + (uint32_t)i) & ringMask_] * win_[(size_t)i];
        fft_.fft(tBuf_.data(), fBuf_.data());
        const int half = n / 2;
        const double binHz = sr_ / n;
        const double ref = 0.25 * (double)n * n * 0.25;   // Hann: a full-scale sine's bin power
        const bool stale = lastAnaS_ < 0 || now - lastAnaS_ > 1.2;
        const double a = stale ? 1.0 : 1.0 - std::exp(-(now - lastAnaS_) / 0.2);
        double tot = 0.0;
        for (int b = 0; b < kSpec; ++b) {
            const double lo = kSpecLo * std::pow(kSpecHi / kSpecLo, (double)b / kSpec);
            const double hi = kSpecLo * std::pow(kSpecHi / kSpecLo, (double)(b + 1) / kSpec);
            int k0 = std::max(1, (int)std::ceil(lo / binHz)), k1 = std::min(half - 1, (int)std::ceil(hi / binHz) - 1);
            if (k1 < k0) k0 = k1 = std::clamp((int)std::lround(std::sqrt(lo * hi) / binHz), 1, half - 1);
            double s = 0.0;
            for (int k = k0; k <= k1; ++k) s += std::norm(fBuf_[(size_t)k]);
            tot += s;
            aSpec_[b] += a * (s - aSpec_[b]);
            spec_[b] = aSpec_[b] > ref * 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(aSpec_[b] / ref)) : -120.0f;
        }
        lastAnaS_ = now; lastAnaW_ = w;
        anaValid_ = tot > ref * 1e-10;
        return anaValid_;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    // Per channel: kGroups cascade groups × kMaxSec sections.
    Svf sec_[2 * kMaxSec * kGroups];
    float gLcur_ = 1.0f, gMcur_ = 1.0f, gHcur_ = 1.0f;   // smoothed band gains (click-free)
    bool  primed_ = false;

    // Meters (audio thread) and their published values.
    float inPk_ = 0.0f, outPk_ = 0.0f, bandPk_[3] = {};
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, cpuA_{0.0f}, srA_{44100.0f}, f1A_{250.0f}, f2A_{2500.0f};
    std::atomic<float> lvlA_[3];
    std::atomic<float> gA_[3];
    std::atomic<bool> resetPk_{false};

    // Spectrum: a ring written by the audio thread, the analysis state under mx_.
    std::vector<float> ring_;
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};
    int fftN_ = 0;
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tBuf_;
    mutable std::vector<std::complex<float>> fBuf_;
    mutable double aSpec_[kSpec] = {};
    mutable float spec_[kSpec] = {};
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
