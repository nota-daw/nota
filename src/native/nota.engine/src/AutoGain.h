// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota AutoGain (device kind 18, mockup "Nota AutoGain") — a loudness-matching utility.
// It measures the incoming programme loudness (ITU-R BS.1770 K-weighting) and applies the
// gain needed to hit a TARGET (LUFS), so an A/B is fair and gain staging stops being a
// guess. AUTO tracks a moving window; turning AUTO off freezes the current correction
// (the card's MATCH button). A true-peak-safe stage keeps the boost from clipping.
//
// Sidechain (added beyond the mockup): route a reference track and the target becomes that
// track's measured loudness — i.e. match THIS signal to the loudness of the reference. Uses
// the same Device sidechain plumbing as the Compressor / Ceiling.
//
// A core Device (JUCE-free); params normalized 0..1, denormalized in process(); persistence
// / automation / clone flow generically. Telemetry is published via scopeRead for the UI.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class AutoGain : public Device {
public:
    enum {
        Target = 0,  // target loudness (0..1 → −36..0 LUFS)
        Scale,       // 0 Momentary (0.4 s) / 1 Short (Window) / 2 Integrated (slow)
        Auto,        // >=0.5 track continuously; else hold the current correction (MATCH)
        Trim,        // manual offset over the auto result (bipolar, ±12 dB)
        Response,    // 0 Fast / 1 Slow — how fast the correction glides
        Window,      // measurement window (0..1 → exp 0.4..10 s) used in Short scale
        MaxGain,     // maximum |correction| (0..1 → 0..24 dB)
        Safe,        // >=0.5 true-peak safety limiter on the output
        Ceiling,     // safety ceiling (0..1 → −6..0 dBTP)
        kNumParams
    };
    // Packed scope telemetry (scopeRead): instantaneous meters; the UI builds the scrolling
    // loudness history and peak-holds from these.
    enum { S_InLufs = 0, S_OutLufs, S_InMom, S_Target, S_Applied, S_TruePeak, S_Corr, S_ScLufs, S_Desired, kScope };

    AutoGain() {
        p_[Target].store(0.611f);   // −14 LUFS
        p_[Scale].store(0.5f);      // Short
        p_[Auto].store(1.0f);
        p_[Trim].store(0.5f);       // 0 dB
        p_[Response].store(1.0f);   // Slow
        p_[Window].store(0.626f);   // ~3 s
        p_[MaxGain].store(0.5f);    // 12 dB
        p_[Safe].store(1.0f);
        p_[Ceiling].store(0.833f);  // −1 dBTP
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        computeKWeighting();
        kIn_[0] = kIn_[1] = kSc_[0] = kSc_[1] = kOut_[0] = kOut_[1] = KW{};
        msWin_ = msScWin_ = msInShort_ = msOutShort_ = msInMom_ = 1e-7;
        cLR_ = cLL_ = cRR_ = 1e-7;
        appliedDb_ = 0.0f; safeEnv_ = 0.0f;
        tp0_[0] = tp1_[0] = tp2_[0] = tp0_[1] = tp1_[1] = tp2_[1] = 0.0f;
    }

    // Sidechain: the target may key off another track's loudness (Compressor plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    void process(float* buf, int32_t frames) override {
        const double targetLufsP = -36.0 + (double)get(Target) * 36.0;
        const int scale = std::clamp((int)std::lround(get(Scale) * 2.0f), 0, 2);
        const bool autoOn = get(Auto) >= 0.5f;
        const float trimDb = (get(Trim) - 0.5f) * 24.0f;
        const bool slow = get(Response) >= 0.5f;
        const double winSec = expMap(get(Window), 0.4, 10.0);
        const float maxGainDb = get(MaxGain) * 24.0f;
        const bool safe = get(Safe) >= 0.5f;
        const float ceilLin = std::pow(10.0f, (-6.0f + get(Ceiling) * 6.0f) / 20.0f);

        // Measurement window (as a one-pole time constant) per Scale.
        const double measTau = scale == 0 ? 0.4 : scale == 2 ? 10.0 : winSec;
        const float measCoef = coefFor(measTau);
        const float momCoef = coefFor(0.4), shortCoef = coefFor(3.0), corrCoef = coefFor(0.4);
        // Gain glide + safety limiter time constants.
        const float glide = coefFor(slow ? 2.0 : 0.25);
        const float safeAtk = coefFor(0.002), safeRel = coefFor(0.10);

        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const bool useSc = sc != nullptr;

        float applied = appliedDb_;

        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];

            // K-weighted input mean square (BS.1770) → measurement + display windows.
            const float kl = kweight(kIn_[0], l), kr = kweight(kIn_[1], r);
            const double ms = (double)kl * kl + (double)kr * kr;
            msWin_ += (ms - msWin_) * measCoef;
            msInShort_ += (ms - msInShort_) * shortCoef;
            msInMom_ += (ms - msInMom_) * momCoef;

            // Reference loudness: sidechain if routed, else the Target param.
            double targetLufs = targetLufsP;
            if (useSc) {
                const float sl = kweight(kSc_[0], sc[i * 2]), sr2 = kweight(kSc_[1], sc[i * 2 + 1]);
                const double sms = (double)sl * sl + (double)sr2 * sr2;
                msScWin_ += (sms - msScWin_) * measCoef;
                targetLufs = lufs(msScWin_);
            }

            const double inLufs = lufs(msWin_);
            double desired = targetLufs - inLufs;                  // dB correction to hit target
            desired = std::clamp(desired, -(double)maxGainDb, (double)maxGainDb);
            desiredDb_ = (float)desired;

            if (autoOn) applied += (float)(desired - applied) * glide;  // glide toward target
            // else: hold the frozen correction (MATCH)

            const float gainDb = applied + trimDb;
            const float gainLin = std::pow(10.0f, gainDb / 20.0f);

            float ol = l * gainLin, or_ = r * gainLin;

            // True-peak safety: a fast peak limiter guaranteeing |out| ≤ ceiling.
            if (safe) {
                const float pk = std::max(std::fabs(ol), std::fabs(or_));
                if (pk > safeEnv_) safeEnv_ = pk + safeAtk * (safeEnv_ - pk);
                else safeEnv_ = pk + safeRel * (safeEnv_ - pk);
                if (safeEnv_ > ceilLin) { const float g = ceilLin / safeEnv_; ol *= g; or_ *= g; }
                ol = std::clamp(ol, -ceilLin, ceilLin);
                or_ = std::clamp(or_, -ceilLin, ceilLin);
            }

            buf[i * 2] = ol; buf[i * 2 + 1] = or_;

            // Output loudness + true peak + correlation (on what actually ships).
            const float ko = kweight(kOut_[0], ol), koR = kweight(kOut_[1], or_);
            msOutShort_ += ((double)ko * ko + (double)koR * koR - msOutShort_) * shortCoef;
            tpPk_ = std::max(tpPk_ * 0.9995f, std::max(truePeak(0, ol), truePeak(1, or_)));
            cLR_ += ((double)ol * or_ - cLR_) * corrCoef;
            cLL_ += ((double)ol * ol - cLL_) * corrCoef;
            cRR_ += ((double)or_ * or_ - cRR_) * corrCoef;
        }

        appliedDb_ = applied;

        // Publish meters.
        inLufs_.store(lufs(msInShort_), std::memory_order_relaxed);
        outLufs_.store(lufs(msOutShort_), std::memory_order_relaxed);
        inMom_.store(lufs(msInMom_), std::memory_order_relaxed);
        target_.store((float)(useSc ? lufs(msScWin_) : targetLufsP), std::memory_order_relaxed);
        applied_.store(appliedDb_ + trimDb, std::memory_order_relaxed);
        truePeak_.store(db(tpPk_), std::memory_order_relaxed);
        const double corr = cLR_ / std::sqrt(std::max(1e-12, cLL_ * cRR_));
        corr_.store((float)std::clamp(corr, -1.0, 1.0), std::memory_order_relaxed);
        scLufs_.store(useSc ? lufs(msScWin_) : -120.0f, std::memory_order_relaxed);
        desired_.store(desiredDb_ + trimDb, std::memory_order_relaxed);
        scBuf_ = nullptr;
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        out[S_InLufs] = inLufs_.load(std::memory_order_relaxed);
        out[S_OutLufs] = outLufs_.load(std::memory_order_relaxed);
        out[S_InMom] = inMom_.load(std::memory_order_relaxed);
        out[S_Target] = target_.load(std::memory_order_relaxed);
        out[S_Applied] = applied_.load(std::memory_order_relaxed);
        out[S_TruePeak] = truePeak_.load(std::memory_order_relaxed);
        out[S_Corr] = corr_.load(std::memory_order_relaxed);
        out[S_ScLufs] = scLufs_.load(std::memory_order_relaxed);
        out[S_Desired] = desired_.load(std::memory_order_relaxed);
        return kScope;
    }

    const char* displayName() const override { return "Nota Level"; }
    int32_t     builtinKind() const override { return 18; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Target", "Scale", "Auto", "Trim", "Response", "Window", "Max Gain", "Safe", "Ceiling" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

private:
    static constexpr double kPi = 3.14159265358979323846;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    float coefFor(double tauSec) const { return (float)(1.0 - std::exp(-1.0 / (std::max(1e-4, tauSec) * sr_))); }
    static float db(float lin) { return 20.0f * std::log10(std::max(1e-6f, lin)); }
    static float lufs(double ms) { return ms > 1e-10 ? (float)(-0.691 + 10.0 * std::log10(ms)) : -120.0f; }

    // BS.1770 K-weighting: high-shelf then 38 Hz high-pass, cascaded biquads (TDF-II).
    struct KW { float z1a = 0, z1b = 0, z2a = 0, z2b = 0; };
    float kweight(KW& s, float x) {
        float y1 = b0_[0] * x + s.z1a;
        s.z1a = b1_[0] * x - a1_[0] * y1 + s.z1b;
        s.z1b = b2_[0] * x - a2_[0] * y1;
        float y2 = b0_[1] * y1 + s.z2a;
        s.z2a = b1_[1] * y1 - a1_[1] * y2 + s.z2b;
        s.z2b = b2_[1] * y1 - a2_[1] * y2;
        return y2;
    }
    void computeKWeighting() {
        { double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
          double K = std::tan(kPi * f0 / sr_);
          double Vh = std::pow(10.0, G / 20.0), Vb = std::pow(Vh, 0.4996667741545416);
          double a0 = 1.0 + K / Q + K * K;
          b0_[0] = (float)((Vh + Vb * K / Q + K * K) / a0);
          b1_[0] = (float)(2.0 * (K * K - Vh) / a0);
          b2_[0] = (float)((Vh - Vb * K / Q + K * K) / a0);
          a1_[0] = (float)(2.0 * (K * K - 1.0) / a0);
          a2_[0] = (float)((1.0 - K / Q + K * K) / a0); }
        { double f0 = 38.13547087602444, Q = 0.5003270373238773;
          double K = std::tan(kPi * f0 / sr_);
          double a0 = 1.0 + K / Q + K * K;
          a1_[1] = (float)(2.0 * (K * K - 1.0) / a0);
          a2_[1] = (float)((1.0 - K / Q + K * K) / a0);
          b0_[1] = (float)(1.0 / a0); b1_[1] = (float)(-2.0 / a0); b2_[1] = (float)(1.0 / a0); }
    }

    // 4× oversampled true-peak via Catmull-Rom between output samples.
    float truePeak(int c, float x) {
        float y0 = tp0_[c], y1 = tp1_[c], y2 = tp2_[c], y3 = x;
        float pk = std::fabs(y2);
        for (int k = 1; k < 4; ++k) {
            float t = k * 0.25f;
            float a = y1, b = 0.5f * (y2 - y0), d = 0.5f * (y3 - y1), cc = y2;
            float v = a + t * (b + t * ((3 * (cc - a) - 2 * b - d) + t * (2 * (a - cc) + b + d)));
            pk = std::max(pk, std::fabs(v));
        }
        tp0_[c] = y1; tp1_[c] = y2; tp2_[c] = y3;
        return pk;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};

    // K-weighting coeffs (shared) + per-signal states.
    float b0_[2] = {}, b1_[2] = {}, b2_[2] = {}, a1_[2] = {}, a2_[2] = {};
    KW kIn_[2], kSc_[2], kOut_[2];
    float tp0_[2] = {}, tp1_[2] = {}, tp2_[2] = {};

    // Loudness EMAs (mean square) + correlation EMAs + limiter env.
    double msWin_ = 1e-7, msScWin_ = 1e-7, msInShort_ = 1e-7, msOutShort_ = 1e-7, msInMom_ = 1e-7;
    double cLR_ = 1e-7, cLL_ = 1e-7, cRR_ = 1e-7;
    float appliedDb_ = 0.0f, desiredDb_ = 0.0f, safeEnv_ = 0.0f, tpPk_ = 0.0f;

    // Sidechain source buffer (engine hands it in before process()).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;

    // Published meters.
    std::atomic<float> inLufs_{-120.0f}, outLufs_{-120.0f}, inMom_{-120.0f}, target_{-14.0f},
        applied_{0.0f}, truePeak_{-120.0f}, corr_{0.0f}, scLufs_{-120.0f}, desired_{0.0f};
};

} // namespace nota
