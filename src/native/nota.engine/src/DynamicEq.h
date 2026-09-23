// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Dynamic EQ-8 (device kind 13) — an eight-band parametric equalizer where each band
// can also react to level, like a per-band compressor / expander. The static half mirrors
// Nota EQ-8: On / Type / Freq / Gain / Q, RBJ biquads, stereo in place. The dynamic half
// adds, per band: a Mode, a Threshold, a signed Range (the extra dB at full engagement —
// negative cuts, positive boosts), Attack / Release and a Key (the band's own signal or the
// device's sidechain source).
//
//   in ─▶ band 1 ─▶ band 2 ─▶ … ─▶ band 8 ─▶ output gain ─▶ out
//   key (self: the input · ext: the sidechain track) ─▶ band-pass at each band ─▶ envelope ─▶
//   level vs threshold ─▶ attack / release ─▶ dynamic gain added to the band's static gain
//
// Modes: Static (0) — a plain EQ band; Duck (1) — engages as the band's level rises ABOVE the
// threshold; Lift (2) — engages as it falls BELOW it. Range carries the direction: the card
// seeds Duck with a cut and Lift with a boost, either can be flipped (a Duck that boosts is
// upward expansion, a Lift that cuts is downward expansion). The detector reaches full
// engagement 6 dB past the threshold. Only shelves and bells take dynamic gain. Dynamics (a
// master switch) parks every band on its static gain at once — an A/B of what the dynamics do.
// Solo auditions one band alone.
//
// Params are the device's raw units (not 0..1) and APPEND ONLY: 0..79 are the eight bands
// (10 fields each: On, Type, Freq, Gain, Q, Mode, Thr, Rng, Atk, Rel), 80 Output, 81
// Sidechain (legacy: every band keyed from the sidechain), 82 Solo (band + 1, 0 = none) —
// the original layout — then 83 Dynamics and 84..91 the per-band Key. Older projects open
// unchanged: Dynamics defaults on, Key defaults to Self.
//
// Telemetry (scopeRead): 0..7 each band's momentary dynamic gain (dB, signed — the original
// layout), 8..15 each band's detector level (dBFS, −120 when silent or off), then the live
// values up to kTele, then the output spectrum — kSpec log bands 20 Hz..20 kHz in dB from an
// FFT over the last ~85 ms, analysed on the reading thread (≤ 30 Hz).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
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

class DynamicEq : public Device {
public:
    static constexpr int kBands = 8;
    static constexpr int kPerBand = 10;
    // Per-band field offsets.
    enum { On = 0, Type = 1, Freq = 2, Gain = 3, Q = 4,
           Mode = 5, Thresh = 6, Range = 7, Attack = 8, Release = 9 };
    // Filter types (mirror Eq.h).
    enum { LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5, kNumTypes = 6 };
    // Dynamic modes: Static, Duck (above the threshold), Lift (below it).
    enum { DynOff = 0, DynAbove = 1, DynBelow = 2 };

    static constexpr int kBandParams = kBands * kPerBand;   // 80
    enum { Output = kBandParams, Sidechain = kBandParams + 1, Solo = kBandParams + 2,
           Dynamics = kBandParams + 3, KeyBase = kBandParams + 4 };
    static constexpr int kNumParams = KeyBase + kBands;     // 92
    // Solo param encoding: 0 = none, n = audition band n-1 (others muted).

    // Telemetry slots (scopeRead).
    enum {
        S_Gain = 0,        // 8 × momentary dynamic gain, dB (signed)
        S_Level = 8,       // 8 × detector level, dBFS (−120 silent / off)
        S_SampleRate = 16,
        S_Cpu,             // share of real time spent in process()
        S_Analysed,        // 1 when the spectrum is valid
        S_InPeak,          // input peak (300 ms fall), dBFS
        S_OutPeak,         // output peak (300 ms fall), dBFS
        S_KeyLive,         // 1 while a sidechain buffer arrives
        S_Bands,           // kSpec
        S_Latency,         // samples (always 0)
        kTele = 32
    };
    static constexpr int kSpec = 96;
    static constexpr double kSpecLo = 20.0, kSpecHi = 20000.0;
    static constexpr int kSpecAt = kTele;
    static constexpr int kScope = kTele + kSpec;

    DynamicEq() {
        // Neutral default: musically spread bands, all flat — adding the device is
        // transparent. Every band carries working dynamics (−24 dB, 6 dB cut, 10 / 120 ms)
        // so switching a band to Duck or Lift reacts at once.
        band(0, 1, LowCut,      30.0f,  0.0f, 0.71f);
        band(1, 1, Bell,       120.0f,  0.0f, 1.00f);
        band(2, 1, Bell,       800.0f,  0.0f, 0.90f);
        band(3, 1, Bell,      3000.0f,  0.0f, 1.50f);
        band(4, 0, Bell,       200.0f,  0.0f, 1.00f);
        band(5, 0, Bell,      6000.0f,  0.0f, 1.20f);
        band(6, 1, HighShelf,10000.0f,  0.0f, 0.71f);
        band(7, 1, HighCut,  20000.0f,  0.0f, 0.71f);
        p_[Output].store(0.0f);
        p_[Sidechain].store(0.0f);
        p_[Solo].store(0.0f);
        p_[Dynamics].store(1.0f);
        for (int b = 0; b < kBands; ++b) p_[KeyBase + b].store(0.0f);
        for (int b = 0; b < kBands; ++b) { lvlA_[b].store(-120.0f); gr_[b].store(0.0f); }
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        for (auto& ch : s_) for (auto& st : ch) st = {};
        for (auto& st : detState_) st = {};
        for (int b = 0; b < kBands; ++b) { detEnv_[b] = 0.0; dynDb_[b] = 0.0; lvlDisp_[b] = -120.0; }
        inPk_ = outPk_ = 0.0f;
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

    bool    acceptsSidechain() const override { return true; }
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        const double sr = sr_;
        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const double scGain = std::pow(10.0, sidechainGainDb() / 20.0);
        const bool allExt = get(Sidechain) >= 0.5f;
        const bool dynMaster = get(Dynamics) >= 0.5f;
        const double outGain = std::pow(10.0, clampd(get(Output), -18.0, 18.0) / 20.0);

        // Per-band config + detector coefficients (recomputed per block).
        bool   on[kBands], dyn[kBands], hasGain[kBands], ext[kBands];
        int    mode[kBands], type[kBands];
        double f0[kBands], gStat[kBands], q[kBands], thr[kBands], range[kBands], atkC[kBands], relC[kBands];
        Coeffs det[kBands];
        for (int b = 0; b < kBands; ++b) {
            on[b]      = bp(b, On) > 0.5f;
            type[b]    = typeOf(b);
            hasGain[b] = (type[b] == LowShelf || type[b] == Bell || type[b] == HighShelf);
            f0[b]      = clampd(bp(b, Freq), 20.0, sr * 0.49);
            gStat[b]   = clampd(bp(b, Gain), -18.0, 18.0);
            q[b]       = clampd(bp(b, Q), 0.1, 18.0);
            mode[b]    = modeOf(b);
            thr[b]     = clampd(bp(b, Thresh), -60.0, 6.0);
            range[b]   = clampd(bp(b, Range), -18.0, 18.0);
            const double atkMs = clampd(bp(b, Attack), 0.1, 300.0);
            const double relMs = clampd(bp(b, Release), 5.0, 2000.0);
            atkC[b]    = 1.0 - std::exp(-1.0 / (atkMs * 0.001 * sr));
            relC[b]    = 1.0 - std::exp(-1.0 / (relMs * 0.001 * sr));
            ext[b]     = sc && (allExt || get(KeyBase + b) >= 0.5f);
            det[b]     = bandpass(sr, f0[b], std::max(q[b], 0.5));
        }
        // Solo: audition a single band (mute the rest, force the soloed one on).
        const int solo = std::clamp(static_cast<int>(std::lround(get(Solo))), 0, kBands) - 1;
        if (solo >= 0)
            for (int b = 0; b < kBands; ++b) on[b] = (b == solo);
        for (int b = 0; b < kBands; ++b) {
            dyn[b] = dynMaster && on[b] && hasGain[b] && mode[b] != DynOff;
            if (!dyn[b]) dynDb_[b] = 0.0;                        // parked bands read flat
        }
        const double detC = 1.0 - std::exp(-1.0 / (0.005 * sr));   // ~5 ms detector envelope

        // Apply coefficients — recomputed at control rate (dynamic gain moves them).
        Coeffs cf[kBands];
        for (int b = 0; b < kBands; ++b)
            if (on[b]) cf[b] = coeffs(type[b], sr, f0[b], gStat[b] + dynDb_[b], q[b]);

        double lvlMax[kBands];
        for (int b = 0; b < kBands; ++b) lvlMax[b] = -120.0;
        float inPk = 0.0f, outPk = 0.0f;
        uint32_t w = ringW_.load(std::memory_order_relaxed);
        const bool ringOk = !ring_.empty();

        int ctrl = 0;
        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(l), std::fabs(r)));

            // --- detection + per-band dynamic gain ---
            const double self = 0.5 * (l + r);
            const double key = sc ? 0.5 * (sc[i * 2] + sc[i * 2 + 1]) * scGain : 0.0;
            for (int b = 0; b < kBands; ++b) {
                if (!on[b]) continue;
                const double d = detState_[b].run(det[b], ext[b] ? key : self);
                detEnv_[b] += (std::fabs(d) - detEnv_[b]) * detC;
                const double lvl = 20.0 * std::log10(detEnv_[b] + 1e-9);
                if (lvl > lvlMax[b]) lvlMax[b] = lvl;
                if (!dyn[b]) continue;
                const double over = (mode[b] == DynAbove) ? (lvl - thr[b]) : (thr[b] - lvl);
                const double engage = clampd(over / kKneeDb, 0.0, 1.0);
                const double target = range[b] * engage;
                const double c = (std::fabs(target) > std::fabs(dynDb_[b])) ? atkC[b] : relC[b];
                dynDb_[b] += (target - dynDb_[b]) * c;
            }

            // --- refresh apply coeffs for dynamic bands at control rate ---
            if (--ctrl <= 0) {
                ctrl = kCtrl;
                for (int b = 0; b < kBands; ++b)
                    if (dyn[b]) cf[b] = coeffs(type[b], sr, f0[b], gStat[b] + dynDb_[b], q[b]);
            }

            // --- apply cascade, both channels ---
            for (int ch = 0; ch < 2; ++ch) {
                double x = buf[i * 2 + ch];
                for (int b = 0; b < kBands; ++b)
                    if (on[b]) x = s_[ch][b].run(cf[b], x);
                buf[i * 2 + ch] = static_cast<float>(x * outGain);
            }
            const float ol = buf[i * 2], orr = buf[i * 2 + 1];
            outPk = std::max(outPk, std::max(std::fabs(ol), std::fabs(orr)));
            if (ringOk) ring_[(w++) & ringMask_] = 0.5f * (ol + orr);
        }
        ringW_.store(w, std::memory_order_release);
        scBuf_ = nullptr; scFrames_ = 0;
        keyLive_.store(sc ? 1.0f : 0.0f, std::memory_order_relaxed);

        // Publish: the momentary dynamic gain (signed dB) and a peak-held detector level that
        // falls 40 dB/s, so the threshold marker reads steadily at the UI's 60 Hz.
        const double blockS = frames > 0 ? frames / sr : 0.0;
        for (int b = 0; b < kBands; ++b) {
            gr_[b].store(static_cast<float>(dynDb_[b]), std::memory_order_relaxed);
            if (!on[b]) { lvlDisp_[b] = -120.0; detEnv_[b] = 0.0; }
            else lvlDisp_[b] = std::max(std::max(lvlMax[b], -120.0), lvlDisp_[b] - 40.0 * blockS);
            lvlA_[b].store(static_cast<float>(lvlDisp_[b]), std::memory_order_relaxed);
        }
        const float fall = (float)std::exp(-blockS / 0.3);
        inPk_ = std::max(inPk, inPk_ * fall);
        outPk_ = std::max(outPk, outPk_ * fall);
        inPkA_.store(db(inPk_), std::memory_order_relaxed);
        outPkA_.store(db(outPk_), std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / blockS) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    const char* displayName() const override { return "Nota Dynamic EQ-8"; }
    int32_t     builtinKind() const override { return 13; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        if (i == Output) return "Output";
        if (i == Sidechain) return "Sidechain";
        if (i == Solo) return "Solo";
        if (i == Dynamics) return "Dynamics";
        static thread_local char buf[16];
        if (i >= KeyBase && i < kNumParams) { std::snprintf(buf, sizeof(buf), "%d Key", i - KeyBase + 1); return buf; }
        if (i < 0 || i >= kBandParams) return "";
        const int b = i / kPerBand, f = i % kPerBand;
        const char* fld = f == On ? "On" : f == Type ? "Type" : f == Freq ? "Freq" : f == Gain ? "Gain"
                        : f == Q ? "Q" : f == Mode ? "Mode" : f == Thresh ? "Thr" : f == Range ? "Rng"
                        : f == Attack ? "Atk" : "Rel";
        std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
        return buf;
    }
    float paramMin(int32_t i) const override {
        if (i == Output) return -18.0f;
        if (i < 0 || i >= kBandParams) return 0.0f;           // Sidechain, Solo, Dynamics, Key
        switch (i % kPerBand) {
            case Freq: return 20.0f;
            case Gain: return -18.0f;
            case Q: return 0.1f;
            case Thresh: return -60.0f;
            case Range: return -18.0f;
            case Attack: return 0.1f;
            case Release: return 5.0f;
            default: return 0.0f;                              // On, Type, Mode
        }
    }
    float paramMax(int32_t i) const override {
        if (i == Output) return 18.0f;
        if (i == Solo) return kBands;
        if (i < 0 || i >= kBandParams) return 1.0f;           // Sidechain, Dynamics, Key
        switch (i % kPerBand) {
            case On: return 1.0f;
            case Type: return kNumTypes - 1;
            case Freq: return 20000.0f;
            case Gain: return 18.0f;
            case Q: return 18.0f;
            case Mode: return 2.0f;
            case Thresh: return 6.0f;
            case Range: return 18.0f;
            case Attack: return 300.0f;
            default: return 2000.0f;                           // Release
        }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v)) p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    }

    // Telemetry block, then the output spectrum. Writes as much of that layout as fits in
    // maxSamples (an 8-float read gets the eight gains, as before). UI / MCP thread.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        for (int b = 0; b < kBands; ++b) {
            t[S_Gain + b] = gr_[b].load(std::memory_order_relaxed);
            t[S_Level + b] = lvlA_[b].load(std::memory_order_relaxed);
        }
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeak]    = outPkA_.load(std::memory_order_relaxed);
        t[S_KeyLive]    = keyLive_.load(std::memory_order_relaxed);
        t[S_Bands]      = (float)kSpec;
        t[S_Latency]    = 0.0f;
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

    // Summary GR (largest reduction across bands) for the shell meter.
    float gainReductionDb() const override {
        float g = 0.0f;
        for (int b = 0; b < kBands; ++b) { float v = gr_[b].load(std::memory_order_relaxed); if (v < g) g = v; }
        return g;
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        static const char* types[kNumTypes] = { "Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut" };
        static const char* modes[3] = { "static", "duck", "lift" };
        char b[320];
        if (id == 0) {
            int nOn = 0, nDyn = 0;
            for (int k = 0; k < kBands; ++k) { if (bandOn(k)) ++nOn; if (bandDyn(k)) ++nDyn; }
            std::snprintf(b, sizeof b, "%d bands on, %d dynamic - dynamics %s", nOn, nDyn, get(Dynamics) >= 0.5f ? "on" : "off (all static)");
            std::string s = b;
            for (int k = 0; k < kBands; ++k) {
                if (!bandOn(k)) continue;
                const int ty = typeOf(k);
                const bool g = ty == LowShelf || ty == Bell || ty == HighShelf;
                std::snprintf(b, sizeof b, " | B%d %s %.0f Hz", k + 1, types[ty], bp(k, Freq)); s += b;
                if (g) { std::snprintf(b, sizeof b, " %+.1f dB", bp(k, Gain)); s += b; }
                std::snprintf(b, sizeof b, " Q %.2f", bp(k, Q)); s += b;
                if (g && modeOf(k) != DynOff) {
                    std::snprintf(b, sizeof b, " %s thr %.0f dB range %+.1f dB atk %.1f ms rel %.0f ms key %s",
                                  modes[modeOf(k)], bp(k, Thresh), bp(k, Range), bp(k, Attack), bp(k, Release), keyExt(k) ? "ext" : "self");
                    s += b;
                }
            }
            const int solo = std::clamp((int)std::lround(get(Solo)), 0, kBands);
            if (solo > 0) { std::snprintf(b, sizeof b, " | solo B%d", solo); s += b; }
            std::snprintf(b, sizeof b, " | output %+.1f dB", get(Output)); s += b;
            if (scTrackId_.load(std::memory_order_relaxed) >= 0) s += " - sidechain routed";
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::snprintf(b, sizeof b, "in %s dBFS - out %s dBFS - key %s", lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeak]).c_str(),
                          sc[S_KeyLive] > 0.5f ? "live" : "none");
            std::string s = b;
            for (int k = 0; k < kBands; ++k) {
                if (!bandOn(k)) continue;
                std::snprintf(b, sizeof b, " | B%d level %s dBFS", k + 1, lv(sc[S_Level + k]).c_str()); s += b;
                if (bandDyn(k)) {
                    std::snprintf(b, sizeof b, " (thr %.0f) gain now %+.1f of %+.1f dB", bp(k, Thresh), sc[S_Gain + k], bp(k, Range));
                    s += b;
                }
            }
            std::snprintf(b, sizeof b, " | CPU %.2f %%", sc[S_Cpu] * 100.0f); s += b;
            return s;
        }
        if (id == 2) {
            return "Raw units. Band n (1..8) params are named \"n <field>\" at index (n-1)*10 + field: On (0/1), Type "
                   "(0 Low cut, 1 Low shelf, 2 Bell, 3 Notch, 4 High shelf, 5 High cut), Freq 20..20000 Hz, Gain -18..+18 dB "
                   "(shelves and bells only), Q 0.1..18 (the resonance on the cuts), Mode (0 Static, 1 Duck - engages as the "
                   "band's level rises above Thr, 2 Lift - engages as it falls below Thr), Thr -60..+6 dBFS, Rng -18..+18 dB "
                   "(the extra gain at full engagement, 6 dB past the threshold: negative cuts, positive boosts; Duck normally "
                   "cuts, Lift boosts), Atk 0.1..300 ms, Rel 5..2000 ms. Only shelves and bells are dynamic. 80 Output -18..+18 "
                   "dB. 81 Sidechain 0/1 keys every band from the sidechain source (legacy). 82 Solo 0 = none, n = hear band n "
                   "alone. 83 Dynamics 0/1 master switch (0 = every band static). 84..91 \"n Key\" 0 = the band hears this "
                   "track, 1 = the sidechain source track (set with set_device_sidechain).";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kCtrl = 16;         // control-rate coeff refresh (samples)
    static constexpr double kKneeDb = 6.0;   // detection range from threshold to full engagement

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

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    float bp(int b, int f) const { return p_[b * kPerBand + f].load(std::memory_order_relaxed); }
    int typeOf(int b) const { return std::clamp(static_cast<int>(std::lround(bp(b, Type))), 0, kNumTypes - 1); }
    int modeOf(int b) const { return std::clamp(static_cast<int>(std::lround(bp(b, Mode))), 0, 2); }
    bool bandOn(int b) const { return bp(b, On) > 0.5f; }
    bool keyExt(int b) const { return get(Sidechain) >= 0.5f || get(KeyBase + b) >= 0.5f; }
    bool bandDyn(int b) const {
        const int ty = typeOf(b);
        return get(Dynamics) >= 0.5f && bandOn(b) && (ty == LowShelf || ty == Bell || ty == HighShelf) && modeOf(b) != DynOff;
    }
    void band(int b, float on, int type, float freq, float gain, float q,
              int mode = DynOff, float thr = -24.0f, float range = -6.0f, float atk = 10.0f, float rel = 120.0f) {
        p_[b * kPerBand + On].store(on);
        p_[b * kPerBand + Type].store(static_cast<float>(type));
        p_[b * kPerBand + Freq].store(freq);
        p_[b * kPerBand + Gain].store(gain);
        p_[b * kPerBand + Q].store(q);
        p_[b * kPerBand + Mode].store(static_cast<float>(mode));
        p_[b * kPerBand + Thresh].store(thr);
        p_[b * kPerBand + Range].store(range);
        p_[b * kPerBand + Attack].store(atk);
        p_[b * kPerBand + Release].store(rel);
    }

    static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static Coeffs normC(double b0, double b1, double b2, double a0, double a1, double a2) {
        return {b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0};
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
    static Coeffs bandpass(double sr, double f0, double q) {   // constant 0 dB peak BPF (detector)
        const double w0 = 2.0 * kPi * clampd(f0, 20.0, sr * 0.49) / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC(alpha, 0.0, -alpha, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs peaking(double sr, double f0, double gainDb, double q) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC(1 + alpha * A, -2 * cw, 1 - alpha * A, 1 + alpha / A, -2 * cw, 1 - alpha / A);
    }
    static Coeffs notch(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC(1, -2 * cw, 1, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs highpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return normC(A * ((A + 1) - (A - 1) * cw + tsa), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - tsa),
                     (A + 1) + (A - 1) * cw + tsa, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - tsa);
    }
    static Coeffs highShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return normC(A * ((A + 1) + (A - 1) * cw + tsa), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - tsa),
                     (A + 1) - (A - 1) * cw + tsa, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - tsa);
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
    State  s_[2][kBands] = {};                 // [channel][band] apply biquad state
    State  detState_[kBands] = {};             // detector bandpass state (mono)
    double detEnv_[kBands] = {};               // detector envelope (linear)
    double dynDb_[kBands] = {};                // smoothed dynamic gain per band (dB)
    double lvlDisp_[kBands] = {};              // peak-held detector level (dB)
    std::atomic<float> gr_[kBands];            // published momentary gain (dB)
    std::atomic<float> lvlA_[kBands];          // published detector level (dBFS)
    float inPk_ = 0.0f, outPk_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, cpuA_{0.0f}, srA_{44100.0f}, keyLive_{0.0f};

    // Sidechain (the device's key source; the engine hands the buffer right before process).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;

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
