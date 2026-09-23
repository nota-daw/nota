// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Crush (device kind 12) — a bit crusher: bit-depth reduction and sample-rate
// reduction with three characters (Digital hard truncate / Analog soft clip / Fold wavefold),
// a GRIT stage (dither / jitter / noise floor), an anti-alias pre-filter, a post filter,
// dry/wet, auto gain and a DC filter.
//
//   in ─▶ drive ─▶ [anti-alias LP] ─▶ sample & hold (± jitter) ─▶ + dither ─▶ mode shape ─▶
//         quantise ─▶ + noise floor ─▶ post LP ─▶ [DC filter] ─▶ dry/wet ─▶ [auto gain] ─▶ out
//
// Anti-alias is a 4th-order Butterworth low-pass at the reduced rate's Nyquist, so what the
// hold would fold back is taken out first. Auto gain rides the output back to the input's
// loudness (300 ms RMS, ±24 dB). DC filter is a 10 Hz high-pass on the crushed path.
//
// All params are normalized 0..1 and APPEND ONLY: 0..10 are the original layout, Auto Gain
// and DC Filter are appended and default off, so older projects open unchanged. Persistence /
// automation / clone flow generically through the base Device.
// Telemetry (scopeRead): kTele live values, then the spectrum — kBands log bands 20 Hz..20 kHz
// of the driven input (level-matched to the output) and of the crushed output, in dB — from
// an FFT over the last ~85 ms, analysed on the reading thread (≤ 30 Hz).
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

class Crush : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Bits = 0, Rate, Mode, Dither, Jitter, NoiseFloor, PostFilter, DryWet, AntiAlias, Output, Drive,
           AutoGain, DcFilter, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0,   // dry input peak (300 ms fall), dBFS
        S_OutPeak,      // output peak (300 ms fall), dBFS
        S_InRms,        // dry input RMS (300 ms), dBFS
        S_OutRms,       // output RMS (300 ms), dBFS
        S_Crest,        // output crest factor: peak hold over 1 s − RMS, dB
        S_SampleRate,
        S_RateHz,       // the reduced sample rate, Hz
        S_Hold,         // samples each value is held (sr / rate, fractional)
        S_Bits,         // effective bit depth, 1..24
        S_Levels,       // quantiser levels, 2^bits
        S_QuantNoise,   // theoretical quantisation noise of a full-scale sine, dB (−6.02·b − 1.76)
        S_ThdN,         // content added by the crush vs the level-matched input, ratio (0.046 = 4.6 %)
        S_Images,       // added content above the reduced Nyquist vs the whole output, dB (−120 none)
        S_AutoGain,     // the auto-gain correction applied now, dB
        S_Cpu,          // share of real time spent in process()
        S_Latency,      // samples
        S_DrivenPeak,   // peak after Drive (1 s hold), linear
        S_Folds,        // folds the driven peak makes in Fold mode (0 in the others)
        S_Mode,         // 0 Digital, 1 Analog, 2 Fold
        S_Bands,        // kBands
        S_Nyquist,      // the reduced rate's Nyquist, Hz
        S_FilterHz,     // the post filter's cutoff, Hz
        S_Analysed,     // 1 when the spectrum is valid
        S_Signal,       // 1 while signal comes in (above −70 dBFS)
        S_OutPeakHold,  // highest output peak since the meter reset, dBFS
        S_InPeakHold,   // highest input peak since the meter reset, dBFS
        kTele = 32
    };
    static constexpr int kBands = 40;
    static constexpr double kBandLo = 20.0, kBandHi = 20000.0;
    static constexpr int kSpecAt = kTele;                  // input bands, then output bands
    static constexpr int kScope = kTele + 2 * kBands;

    Crush() {
        p_[Bits].store(0.55f);        // ~13.7 bit
        p_[Rate].store(0.35f);        // ~1.9 kHz at 44.1 k
        p_[Mode].store(0.0f);         // Digital
        p_[Dither].store(0.15f);
        p_[Jitter].store(0.0f);
        p_[NoiseFloor].store(0.0f);
        p_[PostFilter].store(0.85f);  // ~10 kHz
        p_[DryWet].store(1.0f);
        p_[AntiAlias].store(0.0f);    // off
        p_[Output].store(0.5f);       // 0 dB
        p_[Drive].store(0.333f);      // ~0 dB pre-crush
        p_[AutoGain].store(0.0f);     // off
        p_[DcFilter].store(0.0f);     // off
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        std::lock_guard<std::mutex> lock(mx_);
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        holdL_ = holdR_ = 0.0f;
        phL_ = phR_ = perL_ = perR_ = 1.0;   // the first sample is taken at once
        lpL_ = lpR_ = 0.0f;
        for (auto& s : aa_) s = {};
        aaHz_ = -1.0;
        dcX_[0] = dcX_[1] = dcY_[0] = dcY_[1] = 0.0f;
        msIn_ = msOut_ = 0.0;
        autoG_ = 1.0f;
        fftN_ = sr_ > 50000.0 ? 8192 : 4096;
        size_t ring = 1;
        while (ring < (size_t)fftN_ * 2) ring <<= 1;
        ringIn_.assign(ring, 0.0f); ringOut_.assign(ring, 0.0f);
        ringMask_ = (uint32_t)(ring - 1);
        ringW_.store(0, std::memory_order_relaxed);
        fft_.resize((size_t)fftN_);
        win_.assign((size_t)fftN_, 0.0f);
        tIn_.assign((size_t)fftN_, 0.0f); tOut_.assign((size_t)fftN_, 0.0f);
        fIn_.assign((size_t)fftN_ / 2, {}); fOut_.assign((size_t)fftN_ / 2, {});
        for (int i = 0; i < fftN_; ++i) win_[(size_t)i] = (float)(0.5 - 0.5 * std::cos(2.0 * kPi * i / fftN_));
        lastAnaS_ = -1.0; lastAnaW_ = 0; anaValid_ = false;
        for (int b = 0; b < kBands; ++b) { aIn_[b] = aOut_[b] = 0.0; bandIn_[b] = bandOut_[b] = -120.0f; }
        thdN_ = 0.0f; images_ = -120.0f;
        resetMeters();
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetReq_.exchange(false, std::memory_order_acq_rel)) resetMeters();

        const float bitsV   = std::clamp(get(Bits), 0.0f, 1.0f);
        const float rateV   = std::clamp(get(Rate), 0.0f, 1.0f);
        const int   mode    = modeIndex();
        const float dither  = std::clamp(get(Dither), 0.0f, 1.0f);
        const float jitter  = std::clamp(get(Jitter), 0.0f, 1.0f);
        const float nf      = std::clamp(get(NoiseFloor), 0.0f, 1.0f);
        const float filtV   = std::clamp(get(PostFilter), 0.0f, 1.0f);
        const float wet     = std::clamp(get(DryWet), 0.0f, 1.0f);
        const bool  aa      = get(AntiAlias) >= 0.5f;
        const bool  autoOn  = get(AutoGain) >= 0.5f;
        const bool  dcOn    = get(DcFilter) >= 0.5f;
        const float outGain = std::pow(10.0f, (get(Output) - 0.5f) * 24.0f / 20.0f);   // ±12 dB
        const float drive   = driveGain();                                              // -12..+24 dB pre-crush

        // Bit depth: 1..24 bits.
        const float bits = bitsOf(bitsV);
        const float scale = std::pow(2.0f, bits) - 1.0f;
        const float invScale = 1.0f / scale;

        // Sample-rate reduction: a fractional sample & hold — a phase accumulator takes a new
        // value every sr / rate samples (not rounded, so Rate sweeps smoothly). 0 → 0.5 kHz, 1 → 0.48·sr.
        const double targetHz = rateHz(rateV);
        const double inc = targetHz / sr_;

        // Post filter: 1-pole lowpass, exp 200..20000 Hz.
        const double filtHz = expMap(filtV, 200.0, 20000.0);
        const float filtCoef = (float)std::exp(-2.0 * kPi * filtHz / sr_);
        const float filtGain = 1.0f - filtCoef;

        // Anti-alias: 4th-order Butterworth LP at the reduced rate's Nyquist (two biquads).
        if (aa) {
            const double hz = std::min(targetHz * 0.45, sr_ * 0.45);
            if (std::fabs(hz - aaHz_) > 1e-3) computeAa(hz);
        }

        const float dithAmp = dither * 0.5f * invScale;   // triangular PDF, ±0.5 LSB at full
        const float nfAmp = nf * 0.003f;                  // noise floor
        const float dcR = (float)std::exp(-2.0 * kPi * 10.0 / sr_);
        const double msC = 1.0 - std::exp(-1.0 / (0.300 * sr_));   // 300 ms RMS
        const float gSlew = (float)(1.0 - std::exp(-1.0 / (0.050 * sr_)));

        float inPk = 0.0f, outPk = 0.0f, drvPk = 0.0f;
        uint32_t w = ringW_.load(std::memory_order_relaxed);

        for (int32_t i = 0; i < frames; ++i) {
            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(dryL), std::fabs(dryR)));

            // --- drive + anti-alias filter (before downsampling) ---
            float inL = dryL * drive, inR = dryR * drive;
            drvPk = std::max(drvPk, std::max(std::fabs(inL), std::fabs(inR)));
            const float drvMono = 0.5f * (inL + inR);
            if (aa) { inL = aaRun(0, inL); inR = aaRun(1, inR); }

            // --- sample-rate reduction: sample & hold; Jitter stretches or shortens each hold
            //     by up to ±50 %, per channel ---
            phL_ += inc; phR_ += inc;
            if (phL_ >= perL_) { phL_ -= perL_; holdL_ = inL; perL_ = jitter > 0.0f ? 1.0 + (whiteUni() * 2.0f - 1.0f) * jitter * 0.5f : 1.0; }
            if (phR_ >= perR_) { phR_ -= perR_; holdR_ = inR; perR_ = jitter > 0.0f ? 1.0 + (whiteUni() * 2.0f - 1.0f) * jitter * 0.5f : 1.0; }
            float qL = holdL_, qR = holdR_;

            // --- dither: triangular PDF noise before quantisation ---
            if (dithAmp > 0.0f) {
                float t1 = whiteUni(), t2 = whiteUni();
                qL += (t1 - t2) * dithAmp;
                t1 = whiteUni(); t2 = whiteUni();
                qR += (t1 - t2) * dithAmp;
            }

            // --- mode shape, then quantise ---
            qL = shape(mode, qL); qR = shape(mode, qR);
            qL = std::round(qL * scale) * invScale;
            qR = std::round(qR * scale) * invScale;

            // --- noise floor ---
            if (nfAmp > 0.0f) {
                qL += (whiteUni() * 2.0f - 1.0f) * nfAmp;
                qR += (whiteUni() * 2.0f - 1.0f) * nfAmp;
            }

            // --- post filter ---
            lpL_ += filtGain * (qL - lpL_);
            lpR_ += filtGain * (qR - lpR_);
            float cL = lpL_, cR = lpR_;

            // --- DC filter ---
            if (dcOn) {
                const float yL = cL - dcX_[0] + dcR * dcY_[0]; dcX_[0] = cL; dcY_[0] = yL; cL = yL;
                const float yR = cR - dcX_[1] + dcR * dcY_[1]; dcX_[1] = cR; dcY_[1] = yR; cR = yR;
            }

            // Analysis ring: the driven input and the crushed path (before the mix).
            ringIn_[w & ringMask_] = drvMono;
            ringOut_[w & ringMask_] = 0.5f * (cL + cR);
            ++w;

            // --- dry/wet, auto gain, output gain ---
            float oL = dryL * (1.0f - wet) + cL * wet;
            float oR = dryR * (1.0f - wet) + cR * wet;
            msIn_ += msC * (0.5 * ((double)dryL * dryL + (double)dryR * dryR) - msIn_);
            msOut_ += msC * (0.5 * ((double)oL * oL + (double)oR * oR) - msOut_);
            float gT = 1.0f;
            if (autoOn) {
                if (msIn_ > 1e-9 && msOut_ > 1e-12) gT = (float)std::clamp(std::sqrt(msIn_ / msOut_), 0.063, 15.85);   // ±24 dB
                else gT = autoG_;   // hold through silence
            }
            autoG_ += gSlew * (gT - autoG_);
            oL *= autoG_ * outGain; oR *= autoG_ * outGain;
            buf[i * 2] = oL; buf[i * 2 + 1] = oR;
            outPk = std::max(outPk, std::max(std::fabs(oL), std::fabs(oR)));
        }
        ringW_.store(w, std::memory_order_release);

        // Publish meters: peaks fall 20 dB per 300 ms after a hit; a 1 s hold feeds the crest.
        if (frames > 0) {
            const float fall = std::pow(10.0f, -(20.0f * frames / (0.3f * (float)sr_)) / 20.0f);
            inPkM_ = std::max(inPk, inPkM_ * fall);
            outPkM_ = std::max(outPk, outPkM_ * fall);
            const float holdFall = std::pow(10.0f, -(3.0f * frames / (float)sr_) / 20.0f);   // 3 dB/s
            outPkHold1s_ = std::max(outPk, outPkHold1s_ * holdFall);
            drvPkHold_ = std::max(drvPk, drvPkHold_ * holdFall);
            inHold_ = std::max(inHold_, inPk); outHold_ = std::max(outHold_, outPk);
        }
        inPkA_.store(db(inPkM_), std::memory_order_relaxed);
        outPkA_.store(db(outPkM_), std::memory_order_relaxed);
        inRmsA_.store(dbMs(msIn_), std::memory_order_relaxed);
        outRmsA_.store(dbMs(msOut_), std::memory_order_relaxed);
        outHold1sA_.store(db(outPkHold1s_), std::memory_order_relaxed);
        drvPkA_.store(drvPkHold_, std::memory_order_relaxed);
        inHoldA_.store(db(inHold_), std::memory_order_relaxed);
        outHoldA_.store(db(outHold_), std::memory_order_relaxed);
        autoGA_.store(20.0f * std::log10(std::max(1e-6f, autoG_)), std::memory_order_relaxed);
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    void deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) override {
        if (id == 0) {
            resetReq_.store(true, std::memory_order_release);
            std::lock_guard<std::mutex> lock(mx_);
            lastAnaS_ = -1.0;   // the next reading starts a fresh average
        }
    }

    // Telemetry block, then the spectrum (input bands, output bands). Writes as much of that
    // layout as fits in maxSamples. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const bool bands = maxSamples > kTele;
        float t[kTele] = {};
        const double sr = sr_;
        const float bits = bitsOf(get(Bits));
        const double rate = rateHz(get(Rate));
        t[S_InPeak]     = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPeak]    = outPkA_.load(std::memory_order_relaxed);
        t[S_InRms]      = inRmsA_.load(std::memory_order_relaxed);
        t[S_OutRms]     = outRmsA_.load(std::memory_order_relaxed);
        const float hold1 = outHold1sA_.load(std::memory_order_relaxed);
        t[S_Crest]      = t[S_OutRms] > -119.0f && hold1 > -119.0f ? std::max(0.0f, hold1 - t[S_OutRms]) : 0.0f;
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_RateHz]     = (float)rate;
        t[S_Hold]       = (float)std::max(1.0, sr / rate);
        t[S_Bits]       = bits;
        t[S_Levels]     = std::pow(2.0f, bits);
        t[S_QuantNoise] = -(6.02f * bits + 1.76f);
        t[S_AutoGain]   = get(AutoGain) >= 0.5f ? autoGA_.load(std::memory_order_relaxed) : 0.0f;
        t[S_Cpu]        = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency]    = 0.0f;
        t[S_DrivenPeak] = drvPkA_.load(std::memory_order_relaxed);
        t[S_Mode]       = (float)modeIndex();
        t[S_Folds]      = modeIndex() == 2 ? (float)foldCount(t[S_DrivenPeak]) : 0.0f;
        t[S_Bands]      = (float)kBands;
        t[S_Nyquist]    = (float)(rate * 0.5);
        t[S_FilterHz]   = (float)expMap(get(PostFilter), 200.0, 20000.0);
        t[S_Signal]     = t[S_InPeak] > -70.0f ? 1.0f : 0.0f;
        t[S_InPeakHold] = inHoldA_.load(std::memory_order_relaxed);
        t[S_OutPeakHold] = outHoldA_.load(std::memory_order_relaxed);

        std::lock_guard<std::mutex> lock(mx_);
        if (bands) t[S_Analysed] = analyse() ? 1.0f : 0.0f;
        else t[S_Analysed] = anaValid_ ? 1.0f : 0.0f;
        t[S_ThdN] = anaValid_ ? thdN_ : 0.0f;
        t[S_Images] = anaValid_ ? images_ : -120.0f;

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int b = 0; b < kBands && n < maxSamples; ++b) out[n++] = bandIn_[b];
        for (int b = 0; b < kBands && n < maxSamples; ++b) out[n++] = bandOut_[b];
        return n;
    }

    const char* displayName() const override { return "Nota Crush"; }
    int32_t     builtinKind() const override { return 12; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Bits: return "Bits"; case Rate: return "Rate"; case Mode: return "Mode";
            case Dither: return "Dither"; case Jitter: return "Jitter"; case NoiseFloor: return "Noise Floor";
            case PostFilter: return "Post Filter"; case DryWet: return "Dry/Wet";
            case AntiAlias: return "Anti-Alias"; case Output: return "Output"; case Drive: return "Drive";
            case AutoGain: return "Auto Gain"; case DcFilter: return "DC Filter";
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
        char b[900];
        static const char* modes[3] = { "Digital", "Analog", "Fold" };
        if (id == 0) {
            const double rate = rateHz(get(Rate));
            std::string s = modes[modeIndex()];
            std::snprintf(b, sizeof b, " - %.1f bit - rate %.0f Hz (hold %.1f) - drive %+.1f dB - wet %.0f %%",
                          bitsOf(get(Bits)), rate, std::max(1.0, sr_ / rate), driveDb(), get(DryWet) * 100.0f);
            s += b;
            if (get(Dither) > 0.001f) { std::snprintf(b, sizeof b, " - dither %.0f %%", get(Dither) * 100.0f); s += b; }
            if (get(Jitter) > 0.001f) { std::snprintf(b, sizeof b, " - jitter %.0f %%", get(Jitter) * 100.0f); s += b; }
            if (get(NoiseFloor) > 0.001f) { std::snprintf(b, sizeof b, " - noise %.0f %%", get(NoiseFloor) * 100.0f); s += b; }
            std::snprintf(b, sizeof b, " - post filter %.0f Hz - output %+.1f dB", expMap(get(PostFilter), 200.0, 20000.0),
                          (get(Output) - 0.5f) * 24.0f);
            s += b;
            if (get(AntiAlias) >= 0.5f) s += " - anti-alias";
            if (get(AutoGain) >= 0.5f) s += " - auto gain";
            if (get(DcFilter) >= 0.5f) s += " - DC filter";
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            std::snprintf(b, sizeof b,
                          "in %s dBFS peak / %s RMS - out %s dBFS peak / %s RMS - crest %.1f dB - peaks since reset in %s, out %s - "
                          "%.1f bit (%.0f levels, quantisation noise %.0f dB) - %.0f Hz of %.0f (hold %.1f samples, Nyquist %.0f Hz) - "
                          "added content (THD+N) %.1f %% - images above Nyquist %s dB - driven peak x%.2f%s - auto gain %+.1f dB - "
                          "CPU %.2f %%",
                          lv(sc[S_InPeak]).c_str(), lv(sc[S_InRms]).c_str(), lv(sc[S_OutPeak]).c_str(), lv(sc[S_OutRms]).c_str(),
                          sc[S_Crest], lv(sc[S_InPeakHold]).c_str(), lv(sc[S_OutPeakHold]).c_str(), sc[S_Bits], sc[S_Levels],
                          sc[S_QuantNoise], sc[S_RateHz], sc[S_SampleRate], sc[S_Hold], sc[S_Nyquist], sc[S_ThdN] * 100.0f,
                          lv(sc[S_Images]).c_str(), sc[S_DrivenPeak], modeIndex() == 2 ? (" - " + std::to_string((int)sc[S_Folds]) + " folds").c_str() : "",
                          sc[S_AutoGain], sc[S_Cpu] * 100.0f);
            return b;
        }
        if (id == 2) {
            return "All params are normalized 0..1. Bits: 1 + v*23 bits (0.55 = 13.7 bit, 0.13 = 4 bit, 0.30 = 7.9 bit). "
                   "Rate: the reduced sample rate, exponential 500 Hz (0) .. 0.48 x the sample rate (1); each value is held "
                   "sr/rate samples (fractional) (0.35 = ~2 kHz at 44.1 k, 0.6 = ~5.5 kHz, 0.8 = ~11 kHz). Mode: 0 Digital (hard truncate), "
                   "0.5 Analog (tanh soft clip, +4 dB), 1 Fold (triangle wavefold - each +6 dB of Drive roughly doubles the folds). Dither: 0..1 = 0..+-0.5 LSB "
                   "triangular noise before quantising. Jitter: 0..1 = the hold length wanders up to +-50 %. Noise Floor: 0..1 "
                   "adds white noise up to -50 dBFS. Post Filter: 1-pole low-pass, exponential 200 Hz (0) .. 20 kHz (1). "
                   "Dry/Wet: 0..1. Anti-Alias: >= 0.5 low-passes the input at the reduced Nyquist (4th order) so less folds "
                   "back. Output: -12..+12 dB (0.5 = 0 dB). Drive: -12..+24 dB into the crusher (0.333 = 0 dB). Auto Gain: "
                   ">= 0.5 rides the output back to the input's loudness (300 ms RMS, +-24 dB). DC Filter: >= 0.5 a 10 Hz "
                   "high-pass on the crushed path.";
        }
        return {};
    }

    // ---- mappings (shared with the card through the guide above) -----------------------
    static float bitsOf(float v) { return 1.0f + std::clamp(v, 0.0f, 1.0f) * 23.0f; }
    double rateHz(float v) const { return expMap(v, 500.0, sr_ * 0.48); }
    float driveDb() const { return -12.0f + get(Drive) * 36.0f; }
    float driveGain() const { return std::pow(10.0f, driveDb() / 20.0f); }
    int modeIndex() const { return std::clamp((int)std::lround(get(Mode) * 2.0f), 0, 2); }

private:
    static constexpr double kPi = 3.14159265358979323846;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float dbMs(double ms) { return ms > 1e-12 ? (float)(10.0 * std::log10(ms)) : -120.0f; }

    static float shape(int mode, float x) {
        switch (mode) {
            case 1: return std::tanh(x * 1.5f) / 0.905f;   // Analog: soft clip
            case 2: return wavefold(x * 2.5f, 0.7f);       // Fold
            default: return x;                             // Digital
        }
    }
    // A triangle fold: straight through ±threshold, then reflected back and forth for as far as
    // the input goes (a 4·threshold period), normalised to ±1. More drive, more folds.
    static inline float wavefold(float x, float threshold) {
        const float period = 4.0f * threshold;
        float u = std::fmod(x + threshold, period);
        if (u < 0.0f) u += period;
        return 1.0f - std::fabs(u - 2.0f * threshold) / threshold;
    }
    // How many times a peak of `pk` (after Drive) folds over in Fold mode.
    static int foldCount(float pk) {
        const float x = pk * 2.5f;
        return x <= 0.7f ? 0 : std::min(99, (int)std::floor((x - 0.7f) / 1.4f) + 1);
    }

    // RBJ Butterworth sections (Q 0.5412 / 1.3066) for the 4th-order anti-alias low-pass.
    struct Biquad { float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
    struct BqState { float z1 = 0, z2 = 0; };
    void computeAa(double hz) {
        aaHz_ = hz;
        static const double qs[2] = { 0.54119610, 1.30656296 };
        for (int s = 0; s < 2; ++s) {
            const double wv = 2.0 * kPi * hz / sr_, cw = std::cos(wv), al = std::sin(wv) / (2.0 * qs[s]), a0 = 1.0 + al;
            auto& q = aaC_[s];
            q.b0 = (float)((1.0 - cw) / 2.0 / a0); q.b1 = (float)((1.0 - cw) / a0); q.b2 = q.b0;
            q.a1 = (float)(-2.0 * cw / a0); q.a2 = (float)((1.0 - al) / a0);
        }
    }
    float aaRun(int c, float x) {
        for (int s = 0; s < 2; ++s) {
            const auto& q = aaC_[s];
            auto& z = aa_[(size_t)(c * 2 + s)];
            const float y = q.b0 * x + z.z1;
            z.z1 = q.b1 * x - q.a1 * y + z.z2;
            z.z2 = q.b2 * x - q.a2 * y;
            x = y;
        }
        return x;
    }

    void resetMeters() {
        inPkM_ = outPkM_ = outPkHold1s_ = drvPkHold_ = 0.0f;
        inHold_ = outHold_ = 0.0f;
    }

    // ---- spectrum analysis (reading thread, rate-limited, under mx_) ---------------------
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
    bool analyse() const {
        if (ringIn_.empty() || fftN_ <= 0) return false;
        const double now = nowSeconds();
        const uint32_t w = ringW_.load(std::memory_order_acquire);
        if (lastAnaS_ >= 0 && (now - lastAnaS_ < 1.0 / 30.0 || w == lastAnaW_)) return anaValid_;
        if (w < (uint32_t)fftN_) return anaValid_;
        const int n = fftN_;
        const uint32_t start = w - (uint32_t)n;
        for (int i = 0; i < n; ++i) {
            const uint32_t k = (start + (uint32_t)i) & ringMask_;
            tIn_[(size_t)i] = ringIn_[k] * win_[(size_t)i];
            tOut_[(size_t)i] = ringOut_[k] * win_[(size_t)i];
        }
        fft_.fft(tIn_.data(), fIn_.data());
        fft_.fft(tOut_.data(), fOut_.data());
        const int half = n / 2;

        // Level-match the input to the output on the input's strongest bins (the mode's gain,
        // the drive after the fold …), so what is left over is what the crush added.
        double pkIn = 0.0;
        for (int k = 1; k < half; ++k) pkIn = std::max(pkIn, (double)std::norm(fIn_[(size_t)k]));
        double sIn = 0.0, sOut = 0.0;
        if (pkIn > 0.0)
            for (int k = 1; k < half; ++k)
                if (std::norm(fIn_[(size_t)k]) >= pkIn * 0.1) { sIn += std::norm(fIn_[(size_t)k]); sOut += std::norm(fOut_[(size_t)k]); }
        const double g = sIn > 1e-20 ? std::clamp(sOut / sIn, 1e-4, 1e4) : 1.0;

        const double binHz = sr_ / n;
        const double nyq = rateHz(get(Rate)) * 0.5;
        double added = 0.0, addedHi = 0.0, totIn = 0.0, totOut = 0.0;
        for (int k = 1; k < half; ++k) {
            const double pi = std::norm(fIn_[(size_t)k]) * g, po = std::norm(fOut_[(size_t)k]);
            const double ex = std::max(0.0, po - pi);
            added += ex; totIn += pi; totOut += po;
            if (k * binHz > nyq) addedHi += ex;
        }
        const double ref = 0.25 * (double)n * n * 0.25;   // Hann: a full-scale sine's bin power
        const bool stale = lastAnaS_ < 0 || now - lastAnaS_ > 1.2;
        const double a = stale ? 1.0 : 1.0 - std::exp(-(now - lastAnaS_) / 0.25);
        for (int b = 0; b < kBands; ++b) {
            const double lo = kBandLo * std::pow(kBandHi / kBandLo, (double)b / kBands);
            const double hi = kBandLo * std::pow(kBandHi / kBandLo, (double)(b + 1) / kBands);
            int k0 = std::max(1, (int)std::ceil(lo / binHz)), k1 = std::min(half - 1, (int)std::ceil(hi / binHz) - 1);
            if (k1 < k0) k0 = k1 = std::clamp((int)std::lround(std::sqrt(lo * hi) / binHz), 1, half - 1);
            double si = 0.0, so = 0.0;
            for (int k = k0; k <= k1; ++k) { si += std::norm(fIn_[(size_t)k]) * g; so += std::norm(fOut_[(size_t)k]); }
            aIn_[b] += a * (si - aIn_[b]);
            aOut_[b] += a * (so - aOut_[b]);
            bandIn_[b] = aIn_[b] > ref * 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(aIn_[b] / ref)) : -120.0f;
            bandOut_[b] = aOut_[b] > ref * 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(aOut_[b] / ref)) : -120.0f;
        }
        const float thd = totIn > ref * 1e-9 ? (float)std::sqrt(added / totIn) : 0.0f;
        const float img = totOut > ref * 1e-9 && addedHi > totOut * 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(addedHi / totOut)) : -120.0f;
        thdN_ = stale ? thd : thdN_ + (float)a * (thd - thdN_);
        images_ = stale || images_ <= -119.0f || img <= -119.0f ? img : images_ + (float)a * (img - images_);
        lastAnaS_ = now; lastAnaW_ = w;
        anaValid_ = totIn > ref * 1e-9 || totOut > ref * 1e-9;
        return anaValid_;
    }

    float whiteUni() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float holdL_ = 0.0f, holdR_ = 0.0f;
    double phL_ = 1.0, phR_ = 1.0, perL_ = 1.0, perR_ = 1.0;
    float lpL_ = 0.0f, lpR_ = 0.0f;
    Biquad aaC_[2];
    BqState aa_[4];
    double aaHz_ = -1.0;
    float dcX_[2] = {}, dcY_[2] = {};
    double msIn_ = 0.0, msOut_ = 0.0;
    float autoG_ = 1.0f;
    uint32_t rng_ = 0xDEADBEEFu;

    // Meters (audio thread) and their published copies.
    float inPkM_ = 0.0f, outPkM_ = 0.0f, outPkHold1s_ = 0.0f, drvPkHold_ = 0.0f, inHold_ = 0.0f, outHold_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, inRmsA_{-120.0f}, outRmsA_{-120.0f}, outHold1sA_{-120.0f};
    std::atomic<float> drvPkA_{0.0f}, inHoldA_{-120.0f}, outHoldA_{-120.0f}, autoGA_{0.0f}, cpuA_{0.0f}, srA_{44100.0f};
    std::atomic<bool> resetReq_{false};

    // Spectrum: rings written by the audio thread, the analysis state under mx_.
    std::vector<float> ringIn_, ringOut_;
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};
    int fftN_ = 0;
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tIn_, tOut_;
    mutable std::vector<std::complex<float>> fIn_, fOut_;
    mutable double aIn_[kBands] = {}, aOut_[kBands] = {};
    mutable float bandIn_[kBands] = {}, bandOut_[kBands] = {};
    mutable float thdN_ = 0.0f, images_ = -120.0f;
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
