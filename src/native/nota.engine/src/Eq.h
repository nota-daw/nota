// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 (device kind 0) — an eight-band parametric equalizer (mixing-console style). Each
// band has an on/off, a filter type (low cut / low shelf / bell / notch / high shelf / high
// cut), freq / gain / Q, a Slope for the cuts (12 / 24 / 48 dB/oct) and a Channel: the band
// works on the stereo pair, on the Mid or the Side of it, or on the left or right channel
// alone. Scale multiplies every shelf and bell gain at once (0 … 200 %); Output trims the
// result and Auto gain adds the opposite of the average level of the shelves and bells, so an
// edit can be judged at the same loudness.
//
//   in ─▶ band 1 ─▶ band 2 ─▶ … ─▶ band 8 ─▶ auto gain ─▶ output ─▶ out
//   a band: St (L and R) · Mid / Side (encode, filter one half, decode) · L · R
//
// RBJ biquads. A 12 dB/oct cut is one biquad at the band's Q (the original EQ-8); 24 and 48
// dB/oct are Butterworth cascades of two / four sections whose last, sharpest section carries
// the resonance (Q / 0.707). Coefficients are recomputed per block from atomic params, so
// setParam stays lock-free; a band whose slope or channel changes starts from a clean state.
//
// Params are the device's raw units and APPEND ONLY: 0..39 are the original layout (8 bands ×
// On, Type, Freq, Gain, Q), then 40..47 "n Slope" (0 = 12, 1 = 24, 2 = 48 dB/oct), 48..55 "n
// Channel" (0 St, 1 Mid, 2 Side, 3 L, 4 R), 56 Scale (%), 57 Output (dB), 58 Auto Gain, 59
// Analyzer (0 Pre, 1 Post, 2 Off — which spectrum the card draws). Every appended param
// defaults to what the original EQ-8 did, so older projects, racks and presets open unchanged.
//
// Telemetry (scopeRead): kTele live values (in / out peaks, the auto gain in effect, sample
// rate, CPU), then the input (pre) spectrum and the output (post) spectrum — kSpec log bands
// 20 Hz..20 kHz each, in dB, from an FFT over the last ~85 ms, analysed on the reading thread
// (≤ 30 Hz).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset the meters (peak holds and the spectrum averages).
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

class Eq : public Device {
public:
    static constexpr int kBands = 8;
    static constexpr int kPerBand = 5;                 // On, Type, Freq, Gain, Q
    enum { On = 0, Type = 1, Freq = 2, Gain = 3, Q = 4 };
    // Filter types (per band).
    enum { LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5, kNumTypes = 6 };
    // Channels (per band).
    enum { ChStereo = 0, ChMid = 1, ChSide = 2, ChLeft = 3, ChRight = 4, kNumChannels = 5 };

    static constexpr int kBandParams = kBands * kPerBand;   // 40 — the original layout
    enum { SlopeBase = kBandParams, ChannelBase = SlopeBase + kBands,
           Scale = ChannelBase + kBands, Output, AutoGain, Analyzer, kNumParams };   // 60
    enum { AnaPre = 0, AnaPost = 1, AnaOff = 2 };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,      // input peak (300 ms fall), dBFS
        S_OutPeak,         // output peak (300 ms fall), dBFS
        S_AutoGain,        // the auto gain in effect now, dB
        S_SampleRate,
        S_Cpu,             // share of real time spent in process()
        S_Analysed,        // 1 when the spectra are valid
        S_Latency,         // samples (always 0)
        S_Bands,           // kSpec
        kTele = 16
    };
    static constexpr int kSpec = 96;
    static constexpr double kSpecLo = 20.0, kSpecHi = 20000.0;
    static constexpr int kPreAt = kTele, kPostAt = kTele + kSpec;
    static constexpr int kScope = kTele + 2 * kSpec;

    Eq() {
        // Four musically-spread bands enabled and flat; the rest are ready but off.
        setBand(0, 1, LowShelf,   100.0f,  0.0f, 0.70f);
        setBand(1, 1, Bell,       300.0f,  0.0f, 0.70f);
        setBand(2, 1, Bell,      2000.0f,  0.0f, 0.70f);
        setBand(3, 1, HighShelf, 8000.0f,  0.0f, 0.70f);
        setBand(4, 0, Bell,        60.0f,  0.0f, 0.70f);
        setBand(5, 0, Bell,       800.0f,  0.0f, 0.70f);
        setBand(6, 0, Bell,      5000.0f,  0.0f, 0.70f);
        setBand(7, 0, HighCut,  16000.0f,  0.0f, 0.70f);
        for (int b = 0; b < kBands; ++b) { p_[SlopeBase + b].store(0.0f); p_[ChannelBase + b].store(0.0f); }
        p_[Scale].store(100.0f);
        p_[Output].store(0.0f);
        p_[AutoGain].store(0.0f);
        p_[Analyzer].store((float)AnaPost);
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        for (auto& ch : s_) for (auto& bd : ch) for (auto& st : bd) st = {};
        for (auto& c : cfgKey_) c = -1;
        autoSr_ = -1.0; autoDb_ = 0.0; autoCur_ = 1.0; outCur_ = 1.0; primed_ = false;
        inPk_ = outPk_ = 0.0f;
        fftN_ = sr_ > 50000.0 ? 8192 : 4096;
        size_t ring = 1;
        while (ring < (size_t)fftN_ * 2) ring <<= 1;
        for (auto& r : ring_) r.assign(ring, 0.0f);
        ringMask_ = (uint32_t)(ring - 1);
        ringW_.store(0, std::memory_order_relaxed);
        fft_.resize((size_t)fftN_);
        win_.assign((size_t)fftN_, 0.0f);
        tBuf_.assign((size_t)fftN_, 0.0f);
        fBuf_.assign((size_t)fftN_ / 2, {});
        for (int i = 0; i < fftN_; ++i) win_[(size_t)i] = (float)(0.5 - 0.5 * std::cos(2.0 * kPi * i / fftN_));
        lastAnaS_ = -1.0; lastAnaW_ = 0; anaValid_ = false;
        for (int k = 0; k < 2; ++k) for (int b = 0; b < kSpec; ++b) { aSpec_[k][b] = 0.0; spec_[k][b] = -120.0f; }
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        const double sr = sr_;
        const double scale = clampd(get(Scale), 0.0, 200.0) / 100.0;

        // Per-band config + coefficient cascades, once per block.
        bool on[kBands];
        int  ty[kBands], ch[kBands], nSec[kBands];
        Coeffs c[kBands][kMaxSec];
        for (int b = 0; b < kBands; ++b) {
            on[b] = bandOn(b);
            if (!on[b]) { cfgKey_[b] = -1; continue; }
            ty[b] = typeOf(b);
            ch[b] = channelOf(b);
            nSec[b] = cascade(c[b], ty[b], slopeOf(b), sr, clampd(bp(b, Freq), 20.0, sr * 0.49),
                              clampd(bp(b, Gain), -18.0, 18.0) * scale, clampd(bp(b, Q), 0.1, 18.0));
            // A band whose cascade length or channel changes starts clean (its old state means
            // something else); a type or frequency move keeps it, as the original EQ-8 did.
            const int key = nSec[b] * 10 + ch[b];
            if (key != cfgKey_[b]) { for (auto& chs : s_) for (auto& st : chs[b]) st = {}; cfgKey_[b] = key; }
        }

        // Auto gain: the opposite of the average curve level, re-measured only when a param moves.
        const bool autoOn = get(AutoGain) >= 0.5f;
        if (autoOn) {
            bool moved = autoSr_ != sr;
            for (int i = 0; i < kAutoKeys && !moved; ++i) moved = autoKey_[i] != get(i);
            if (moved) {
                for (int i = 0; i < kAutoKeys; ++i) autoKey_[i] = get(i);
                autoSr_ = sr;
                autoDb_ = clampd(-averageDb(on, ty, ch, c, sr), -12.0, 12.0);
            }
        }
        const double autoTarget = autoOn ? std::pow(10.0, autoDb_ / 20.0) : 1.0;
        const double outTarget = std::pow(10.0, clampd(get(Output), -12.0, 12.0) / 20.0);
        if (!primed_) { autoCur_ = autoTarget; outCur_ = outTarget; primed_ = true; }
        const double glide = 1.0 - std::exp(-1.0 / (0.02 * sr));   // ~20 ms, click-free

        float inPk = 0.0f, outPk = 0.0f;
        uint32_t w = ringW_.load(std::memory_order_relaxed);
        const bool ringOk = !ring_[0].empty();
        for (int32_t i = 0; i < frames; ++i) {
            double l = buf[i * 2], r = buf[i * 2 + 1];
            inPk = std::max(inPk, (float)std::max(std::fabs(l), std::fabs(r)));
            if (ringOk) ring_[0][w & ringMask_] = (float)(0.5 * (l + r));

            for (int b = 0; b < kBands; ++b) {
                if (!on[b]) continue;
                const int n = nSec[b];
                switch (ch[b]) {
                    case ChLeft:  l = run(s_[0][b], c[b], n, l); break;
                    case ChRight: r = run(s_[1][b], c[b], n, r); break;
                    case ChMid: {
                        double m = 0.5 * (l + r); const double sd = 0.5 * (l - r);
                        m = run(s_[0][b], c[b], n, m);
                        l = m + sd; r = m - sd;
                        break;
                    }
                    case ChSide: {
                        const double m = 0.5 * (l + r); double sd = 0.5 * (l - r);
                        sd = run(s_[0][b], c[b], n, sd);
                        l = m + sd; r = m - sd;
                        break;
                    }
                    default:
                        l = run(s_[0][b], c[b], n, l);
                        r = run(s_[1][b], c[b], n, r);
                        break;
                }
            }
            autoCur_ += (autoTarget - autoCur_) * glide;
            outCur_ += (outTarget - outCur_) * glide;
            const double g = autoCur_ * outCur_;
            const float ol = (float)(l * g), orr = (float)(r * g);
            buf[i * 2] = ol; buf[i * 2 + 1] = orr;
            outPk = std::max(outPk, std::max(std::fabs(ol), std::fabs(orr)));
            if (ringOk) ring_[1][w & ringMask_] = 0.5f * (ol + orr);
            ++w;
        }
        ringW_.store(w, std::memory_order_release);

        const double blockS = frames > 0 ? frames / sr : 0.0;
        if (resetPk_.exchange(false, std::memory_order_acquire)) inPk_ = outPk_ = 0.0f;
        const float fall = (float)std::exp(-blockS / 0.3);
        inPk_ = std::max(inPk, inPk_ * fall);
        outPk_ = std::max(outPk, outPk_ * fall);
        inPkA_.store(db(inPk_), std::memory_order_relaxed);
        outPkA_.store(db(outPk_), std::memory_order_relaxed);
        autoA_.store(autoOn ? (float)autoDb_ : 0.0f, std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / blockS) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    const char* displayName() const override { return "Nota EQ-8"; }
    int32_t     builtinKind() const override { return 0; } // M7-6 (project compat)

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        if (i == Scale) return "Scale";
        if (i == Output) return "Output";
        if (i == AutoGain) return "Auto Gain";
        if (i == Analyzer) return "Analyzer";
        static thread_local char buf[16];
        if (i >= SlopeBase && i < ChannelBase) { std::snprintf(buf, sizeof(buf), "%d Slope", i - SlopeBase + 1); return buf; }
        if (i >= ChannelBase && i < Scale) { std::snprintf(buf, sizeof(buf), "%d Channel", i - ChannelBase + 1); return buf; }
        if (i < 0 || i >= kBandParams) return "";
        const int b = i / kPerBand, f = i % kPerBand;
        const char* fld = f == On ? "On" : f == Type ? "Type" : f == Freq ? "Freq" : f == Gain ? "Gain" : "Q";
        std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
        return buf;
    }
    float paramMin(int32_t i) const override {
        if (i == Output) return -12.0f;
        if (i < 0 || i >= kBandParams) return 0.0f;   // Slope, Channel, Scale, Auto Gain, Analyzer
        switch (i % kPerBand) { case On: return 0.0f; case Type: return 0.0f; case Freq: return 20.0f;
                                case Gain: return -18.0f; default: return 0.1f; }
    }
    float paramMax(int32_t i) const override {
        if (i >= SlopeBase && i < ChannelBase) return 2.0f;
        if (i >= ChannelBase && i < Scale) return kNumChannels - 1;
        if (i == Scale) return 200.0f;
        if (i == Output) return 12.0f;
        if (i == AutoGain) return 1.0f;
        if (i == Analyzer) return 2.0f;
        switch (i % kPerBand) { case On: return 1.0f; case Type: return kNumTypes - 1; case Freq: return 20000.0f;
                                case Gain: return 18.0f; default: return 18.0f; }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v))
            p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    }

    // Telemetry block, then the pre and post spectra. Writes as much of that layout as fits
    // in maxSamples; a read no longer than kTele skips the FFT. UI / MCP thread.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeak]    = outPkA_.load(std::memory_order_relaxed);
        t[S_AutoGain]   = autoA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency]    = 0.0f;
        t[S_Bands]      = (float)kSpec;
        int32_t n = 0;
        if (maxSamples > kTele) {
            std::lock_guard<std::mutex> lock(mx_);
            t[S_Analysed] = analyse() ? 1.0f : 0.0f;
            for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
            for (int s = 0; s < 2; ++s)
                for (int b = 0; b < kSpec && n < maxSamples; ++b) out[n++] = spec_[s][b];
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
        lastAnaS_ = -1.0; anaValid_ = false;
        for (int k = 0; k < 2; ++k) for (int b = 0; b < kSpec; ++b) { aSpec_[k][b] = 0.0; spec_[k][b] = -120.0f; }
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        static const char* types[kNumTypes] = { "Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut" };
        static const char* chans[kNumChannels] = { "stereo", "mid", "side", "left", "right" };
        static const char* anas[3] = { "pre", "post", "off" };
        char b[256];
        if (id == 0) {
            int nOn = 0;
            for (int k = 0; k < kBands; ++k) if (bandOn(k)) ++nOn;
            std::snprintf(b, sizeof b, "%d bands on - scale %.0f %%", nOn, get(Scale));
            std::string s = b;
            for (int k = 0; k < kBands; ++k) {
                if (!bandOn(k)) continue;
                const int ty = typeOf(k);
                std::snprintf(b, sizeof b, " | B%d %s %.0f Hz", k + 1, types[ty], bp(k, Freq)); s += b;
                if (hasGain(ty)) { std::snprintf(b, sizeof b, " %+.1f dB", bp(k, Gain)); s += b; }
                std::snprintf(b, sizeof b, " Q %.2f", bp(k, Q)); s += b;
                if (ty == LowCut || ty == HighCut) { std::snprintf(b, sizeof b, " %d dB/oct", 12 << slopeOf(k)); s += b; }
                if (channelOf(k) != ChStereo) { std::snprintf(b, sizeof b, " %s", chans[channelOf(k)]); s += b; }
            }
            std::snprintf(b, sizeof b, " | output %+.1f dB%s | analyzer %s", get(Output), get(AutoGain) >= 0.5f ? " + auto gain" : "",
                          anas[std::clamp((int)std::lround(get(Analyzer)), 0, 2)]);
            s += b;
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::snprintf(b, sizeof b, "in %s dBFS - out %s dBFS - auto gain %+.1f dB%s - CPU %.2f %%", lv(sc[S_InPeak]).c_str(),
                          lv(sc[S_OutPeak]).c_str(), sc[S_AutoGain], get(AutoGain) >= 0.5f ? "" : " (off)", sc[S_Cpu] * 100.0f);
            return b;
        }
        if (id == 2) {
            return "Raw units. Band n (1..8) params are named \"n <field>\": at index (n-1)*5 + field On (0/1), Type (0 Low cut, "
                   "1 Low shelf, 2 Bell, 3 Notch, 4 High shelf, 5 High cut), Freq 20..20000 Hz, Gain -18..+18 dB (shelves and bells "
                   "only), Q 0.1..18 (the resonance on the cuts, 0.71 = flat); 40..47 \"n Slope\" 0 = 12, 1 = 24, 2 = 48 dB/oct "
                   "(cuts only); 48..55 \"n Channel\" 0 Stereo, 1 Mid, 2 Side, 3 Left, 4 Right. 56 Scale 0..200 % multiplies every "
                   "shelf and bell gain. 57 Output -12..+12 dB. 58 Auto Gain 0/1 adds the opposite of the average level of the "
                   "shelves and bells (cuts and notches do not count; max +-12 dB). 59 Analyzer 0 Pre, 1 Post, 2 Off (display only).";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kMaxSec = 4;
    static constexpr int kAutoKeys = Scale + 1;   // band params, slopes, channels and Scale shape the curve

    struct Coeffs { double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
    struct State {
        double z1 = 0, z2 = 0;
        double run(const Coeffs& c, double x) {
            double y = c.b0 * x + z1;
            z1 = c.b1 * x - c.a1 * y + z2;
            z2 = c.b2 * x - c.a2 * y;
            return y;
        }
    };
    static double run(State* st, const Coeffs* c, int n, double x) {
        for (int k = 0; k < n; ++k) x = st[k].run(c[k], x);
        return x;
    }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    float bp(int b, int f) const { return p_[b * kPerBand + f].load(std::memory_order_relaxed); }
    bool bandOn(int b) const { return bp(b, On) > 0.5f; }
    int typeOf(int b) const { return std::clamp(static_cast<int>(std::lround(bp(b, Type))), 0, kNumTypes - 1); }
    int slopeOf(int b) const { return std::clamp(static_cast<int>(std::lround(get(SlopeBase + b))), 0, 2); }
    int channelOf(int b) const { return std::clamp(static_cast<int>(std::lround(get(ChannelBase + b))), 0, kNumChannels - 1); }
    static bool hasGain(int ty) { return ty == LowShelf || ty == Bell || ty == HighShelf; }
    void setBand(int b, float on, int type, float freq, float gain, float q) {
        p_[b * kPerBand + On].store(on);
        p_[b * kPerBand + Type].store(static_cast<float>(type));
        p_[b * kPerBand + Freq].store(freq);
        p_[b * kPerBand + Gain].store(gain);
        p_[b * kPerBand + Q].store(q);
    }

    static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static Coeffs norm(double b0, double b1, double b2, double a0, double a1, double a2) {
        return {b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0};
    }

    // A band's sections: one biquad, or for a 24 / 48 dB cut a Butterworth cascade whose last
    // (sharpest) section carries the resonance. Returns the section count.
    static int cascade(Coeffs* out, int type, int slope, double sr, double f0, double gainDb, double q) {
        if ((type == LowCut || type == HighCut) && slope > 0) {
            static const double q24[2] = { 0.54119610, 1.30656296 };
            static const double q48[4] = { 0.50979558, 0.60134489, 0.89997622, 2.56291545 };
            const int n = slope == 1 ? 2 : 4;
            const double* bq = slope == 1 ? q24 : q48;
            const double res = q / 0.70710678;
            for (int k = 0; k < n; ++k) {
                const double qk = k == n - 1 ? clampd(bq[k] * res, 0.1, 40.0) : bq[k];
                out[k] = type == LowCut ? highpass(sr, f0, qk) : lowpass(sr, f0, qk);
            }
            return n;
        }
        out[0] = coeffs(type, sr, f0, gainDb, q);
        return 1;
    }
    static Coeffs coeffs(int type, double sr, double f0, double gainDb, double q) {
        switch (type) {
            case LowCut:    return highpass(sr, f0, q);
            case LowShelf:  return lowShelf(sr, f0, gainDb);
            case Notch:     return notch(sr, f0, q);
            case HighShelf: return highShelf(sr, f0, gainDb);
            case HighCut:   return lowpass(sr, f0, q);
            default:        return peaking(sr, f0, gainDb, q);
        }
    }
    static double magDb(const Coeffs& k, double w) {
        const double cw = std::cos(w), sw = std::sin(w), c2 = std::cos(2 * w), s2 = std::sin(2 * w);
        const double nr = k.b0 + k.b1 * cw + k.b2 * c2, ni = -(k.b1 * sw + k.b2 * s2);
        const double dr = 1 + k.a1 * cw + k.a2 * c2, di = -(k.a1 * sw + k.a2 * s2);
        return 10.0 * std::log10(std::max(1e-12, (nr * nr + ni * ni) / std::max(1e-12, dr * dr + di * di)));
    }
    // The tonal lift of the curve — the average level of its shelves and bells over 20 Hz..20 kHz
    // (log-spaced). Cuts and notches take content away rather than colour it, so they are left
    // out (a high-pass must not make auto gain louder). Stereo and Mid bands count fully, a
    // left or right band half, a Side band not at all (the Side is the quiet half).
    static double averageDb(const bool* on, const int* ty, const int* ch, const Coeffs (*c)[kMaxSec], double sr) {
        constexpr int N = 64;
        double sum = 0.0;
        for (int i = 0; i < N; ++i) {
            const double f = 20.0 * std::pow(1000.0, (i + 0.5) / N);
            if (f >= sr * 0.49) continue;
            const double w = 2.0 * kPi * f / sr;
            double m = 0.0;
            for (int b = 0; b < kBands; ++b) {
                if (!on[b] || ch[b] == ChSide || !hasGain(ty[b])) continue;
                const double v = magDb(c[b][0], w);
                m += (ch[b] == ChLeft || ch[b] == ChRight) ? 0.5 * v : v;
            }
            sum += m;
        }
        return sum / N;
    }
    static Coeffs peaking(double sr, double f0, double gainDb, double q) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm(1 + alpha * A, -2 * cw, 1 - alpha * A, 1 + alpha / A, -2 * cw, 1 - alpha / A);
    }
    static Coeffs notch(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm(1, -2 * cw, 1, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs highpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return norm(A * ((A + 1) - (A - 1) * cw + tsa), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - tsa),
                    (A + 1) + (A - 1) * cw + tsa, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - tsa);
    }
    static Coeffs highShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return norm(A * ((A + 1) + (A - 1) * cw + tsa), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - tsa),
                    (A + 1) - (A - 1) * cw + tsa, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - tsa);
    }

    // ---- spectra (reading thread, rate-limited, under mx_) -------------------------------
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
    bool analyse() const {
        if (ring_[0].empty() || fftN_ <= 0) return false;
        const double now = nowSeconds();
        const uint32_t w = ringW_.load(std::memory_order_acquire);
        if (lastAnaS_ >= 0 && (now - lastAnaS_ < 1.0 / 30.0 || w == lastAnaW_)) return anaValid_;
        if (w < (uint32_t)fftN_) return anaValid_;
        const int n = fftN_;
        const uint32_t start = w - (uint32_t)n;
        const int half = n / 2;
        const double binHz = sr_ / n;
        const double ref = 0.25 * (double)n * n * 0.25;   // Hann: a full-scale sine's bin power
        const bool stale = lastAnaS_ < 0 || now - lastAnaS_ > 1.2;
        const double a = stale ? 1.0 : 1.0 - std::exp(-(now - lastAnaS_) / 0.2);
        double tot = 0.0;
        for (int s = 0; s < 2; ++s) {
            for (int i = 0; i < n; ++i) tBuf_[(size_t)i] = ring_[s][(start + (uint32_t)i) & ringMask_] * win_[(size_t)i];
            fft_.fft(tBuf_.data(), fBuf_.data());
            for (int b = 0; b < kSpec; ++b) {
                const double lo = kSpecLo * std::pow(kSpecHi / kSpecLo, (double)b / kSpec);
                const double hi = kSpecLo * std::pow(kSpecHi / kSpecLo, (double)(b + 1) / kSpec);
                int k0 = std::max(1, (int)std::ceil(lo / binHz)), k1 = std::min(half - 1, (int)std::ceil(hi / binHz) - 1);
                if (k1 < k0) k0 = k1 = std::clamp((int)std::lround(std::sqrt(lo * hi) / binHz), 1, half - 1);
                double e = 0.0;
                for (int k = k0; k <= k1; ++k) e += std::norm(fBuf_[(size_t)k]);
                tot += e;
                aSpec_[s][b] += a * (e - aSpec_[s][b]);
                spec_[s][b] = aSpec_[s][b] > ref * 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(aSpec_[s][b] / ref)) : -120.0f;
            }
        }
        lastAnaS_ = now; lastAnaW_ = w;
        anaValid_ = tot > ref * 1e-10;
        return anaValid_;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    State s_[2][kBands][kMaxSec] = {};             // [channel (or M/S half)][band][section]
    int   cfgKey_[kBands] = {};                    // type / slope / channel the state belongs to
    float  autoKey_[kAutoKeys] = {};               // auto gain: the params it was measured for
    double autoSr_ = -1.0, autoDb_ = 0.0;
    double autoCur_ = 1.0, outCur_ = 1.0;          // smoothed auto / output gains
    bool   primed_ = false;

    // Meters (audio thread) and their published values.
    float inPk_ = 0.0f, outPk_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, autoA_{0.0f}, cpuA_{0.0f}, srA_{44100.0f};
    std::atomic<bool> resetPk_{false};

    // Spectra: pre / post rings written by the audio thread, the analysis state under mx_.
    std::vector<float> ring_[2];
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};
    int fftN_ = 0;
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tBuf_;
    mutable std::vector<std::complex<float>> fBuf_;
    mutable double aSpec_[2][kSpec] = {};
    mutable float spec_[2][kSpec] = {};
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
