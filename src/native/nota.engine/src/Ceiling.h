// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Ceiling (mockup 3o) — a look-ahead brick-wall limiter (device kind 14).
// Input gain drives the signal into a hard ceiling; a look-ahead delay lets the
// gain duck *before* each peak reaches the output, a smoothed release (with an
// optional program-dependent auto stage) governs recovery, and a final safety
// clip guarantees the sample peak never passes the ceiling. Three voicing
// "characters" (Clean / Punch / Glue) reshape attack/release, and a stereo-link
// blend trades a glued centre for independent channels. A filtered side-chain
// detector is supported like the Compressor. On top of limiting it runs a full
// BS.1770 loudness meter — momentary/short-term/integrated LUFS plus a 4× true
// peak — so the card can answer "is this deliverable" without a second plug-in.
// A core Device (JUCE-free); params are atomic / lock-free, appended by index.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Ceiling : public Device {
public:
    enum { Ceil = 0, Gain, Release, AutoRelease, Character, Lookahead, StereoLink, kNumParams };
    // Packed scope telemetry (scopeRead): instantaneous per-block meters. The UI
    // builds the scrolling 4-second history and the peak-holds from these.
    enum { S_InPeak = 0, S_OutPeak, S_Gr, S_LufsM, S_LufsS, S_LufsI, S_TruePeak, kScope };

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        laMax_ = static_cast<int>(sr_ * 0.010) + 2;          // 10 ms max look-ahead
        laBuf_.assign(static_cast<size_t>(laMax_) * 2, 0.0f); laW_ = 0;

        // BS.1770-4 K-weighting: a high-shelf pre-filter then a 38 Hz high-pass.
        computeKWeighting();
        for (int c = 0; c < 2; ++c) { z1_[c][0] = z1_[c][1] = z2_[c][0] = z2_[c][1] = 0.0f; }

        momN_ = std::max(1, (int)std::lround(sr_ * 0.400));   // momentary 400 ms
        shortN_ = std::max(1, (int)std::lround(sr_ * 3.000)); // short-term 3 s
        msRing_.assign(static_cast<size_t>(shortN_), 0.0f); msW_ = 0;
        momSum_ = 0.0; shortSum_ = 0.0;
        hopN_ = std::max(1, (int)std::lround(sr_ * 0.100));   // integrate every 100 ms
        hop_ = 0;
        histCount_.assign(kHistBins, 0.0);
        histSum_.assign(kHistBins, 0.0);
        blockSum_ = 0.0; blockN_ = 0;
        for (int c = 0; c < 2; ++c) { tp0_[c] = tp1_[c] = tp2_[c] = 0.0f; }
        env_ = 1.0f; grPrev_ = 0.0f;
    }

    void process(float* buf, int32_t frames) override {
        const double sr = sr_;
        const int ch = std::clamp((int)std::lround(p_[Character].load(std::memory_order_relaxed)), 0, 2);
        // Clean / Punch / Glue — attack ms, release multiplier, soft-clip amount.
        static const float chAtk[3]  = { 0.20f, 2.0f, 0.6f };
        static const float chRel[3]  = { 1.0f, 0.65f, 2.6f };
        static const float chSoft[3] = { 0.0f, 0.0f, 0.6f };

        const float ceilDb  = p_[Ceil].load(std::memory_order_relaxed);
        const float ceilLin = std::pow(10.0f, ceilDb / 20.0f);
        const float inGain  = std::pow(10.0f, p_[Gain].load(std::memory_order_relaxed) / 20.0f);
        const float relMs   = std::max(1.0f, p_[Release].load(std::memory_order_relaxed)) * chRel[ch];
        const bool  autoRel = p_[AutoRelease].load(std::memory_order_relaxed) > 0.5f;
        const float link    = std::clamp(p_[StereoLink].load(std::memory_order_relaxed) * 0.01f, 0.0f, 1.0f);
        const float soft    = chSoft[ch];
        int la = std::clamp((int)std::lround(p_[Lookahead].load(std::memory_order_relaxed) * 0.001 * sr), 0, laMax_ - 1);

        const double atkC = std::exp(-1.0 / (std::max(0.05f, chAtk[ch]) * 0.001 * sr));
        const double relC = std::exp(-1.0 / (relMs * 0.001 * sr));
        const double relCs = std::exp(-1.0 / (relMs * 5.0 * 0.001 * sr));   // slow auto stage

        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);

        float inPk = 0.0f, outPk = 0.0f, tpPk = 0.0f, maxGr = 0.0f;

        for (int32_t i = 0; i < frames; ++i) {
            float l = buf[i * 2] * inGain, r = buf[i * 2 + 1] * inGain;
            const float aL = std::fabs(l), aR = std::fabs(r);
            float det = std::max(aL, aR);
            if (sc) det = std::max(det, std::max(std::fabs(sc[i * 2]), std::fabs(sc[i * 2 + 1])) * scGain);
            if (det > inPk) inPk = det;

            // Peak follower with fast attack; look-ahead delay is what makes it duck
            // before the transient. Auto-release adds a slow stage on deep reduction.
            if (det > env_) env_ = (float)(atkC * env_ + (1.0 - atkC) * det);
            else { double rc = (autoRel && grPrev_ > 2.0f) ? relCs : relC; env_ = (float)(rc * env_ + (1.0 - rc) * det); }

            float gain = env_ > ceilLin ? ceilLin / env_ : 1.0f;   // linked target gain
            grPrev_ = gain < 1.0f ? -20.0f * std::log10(std::max(1e-6f, gain)) : 0.0f;
            if (grPrev_ > maxGr) maxGr = grPrev_;

            // Stereo link: 100 % = one gain for both; less = ease each channel toward
            // its own headroom so a hard pan doesn't pull the other side down.
            float gL = gain, gR = gain;
            if (link < 0.999f) {
                float tL = aL > ceilLin ? ceilLin / aL : 1.0f;
                float tR = aR > ceilLin ? ceilLin / aR : 1.0f;
                gL = link * gain + (1.0f - link) * std::min(gain < 1.0f ? gain : 1.0f, tL);
                gR = link * gain + (1.0f - link) * std::min(gain < 1.0f ? gain : 1.0f, tR);
            }

            // Look-ahead: apply the gain (already ducked) to the delayed sample.
            laBuf_[laW_ * 2] = l; laBuf_[laW_ * 2 + 1] = r;
            int rp = laW_ - la; if (rp < 0) rp += laMax_;
            float ol = laBuf_[rp * 2] * gL, or_ = laBuf_[rp * 2 + 1] * gR;
            if (++laW_ >= laMax_) laW_ = 0;

            // Character soft-clip (Glue) then the hard safety ceiling — brick wall.
            if (soft > 0.0f) { ol = softClip(ol, ceilLin, soft); or_ = softClip(or_, ceilLin, soft); }
            ol = std::clamp(ol, -ceilLin, ceilLin);
            or_ = std::clamp(or_, -ceilLin, ceilLin);
            buf[i * 2] = ol; buf[i * 2 + 1] = or_;

            outPk = std::max(outPk, std::max(std::fabs(ol), std::fabs(or_)));
            tpPk = std::max(tpPk, std::max(truePeak(0, ol), truePeak(1, or_)));

            // K-weighted loudness on the output (what actually ships).
            float kl = kWeight(0, ol), kr = kWeight(1, or_);
            float ms = kl * kl + kr * kr;
            shortSum_ += ms - msRing_[msW_];
            msRing_[msW_] = ms;
            int momTail = msW_ - momN_; if (momTail < 0) momTail += shortN_;
            momSum_ += ms - msRing_[momTail];
            if (++msW_ >= shortN_) msW_ = 0;
            blockSum_ += ms; ++blockN_;
            if (++hop_ >= hopN_) { hop_ = 0; integrateBlock(); }
        }

        // Publish meters (dB; peaks in dBFS, GR positive-down).
        inPeak_.store(db(inPk), std::memory_order_relaxed);
        outPeak_.store(db(outPk), std::memory_order_relaxed);
        truePeak_.store(db(tpPk), std::memory_order_relaxed);
        gr_.store(maxGr, std::memory_order_relaxed);
        lufsM_.store(lufs(momSum_ / std::max(1, momN_)), std::memory_order_relaxed);
        lufsS_.store(lufs(shortSum_ / std::max(1, shortN_)), std::memory_order_relaxed);
        scBuf_ = nullptr;
    }

    // GR (dB, >=0) for the shell titlebar meter, like the Compressor.
    float gainReductionDb() const override { return gr_.load(std::memory_order_relaxed); }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        out[S_InPeak]   = inPeak_.load(std::memory_order_relaxed);
        out[S_OutPeak]  = outPeak_.load(std::memory_order_relaxed);
        out[S_Gr]       = gr_.load(std::memory_order_relaxed);
        out[S_LufsM]    = lufsM_.load(std::memory_order_relaxed);
        out[S_LufsS]    = lufsS_.load(std::memory_order_relaxed);
        out[S_LufsI]    = lufsI_.load(std::memory_order_relaxed);
        out[S_TruePeak] = truePeak_.load(std::memory_order_relaxed);
        return kScope;
    }

    int32_t latencySamples() const override {
        return std::clamp((int)std::lround(p_[Lookahead].load(std::memory_order_relaxed) * 0.001 * sr_), 0, laMax_ - 1);
    }

    // Side-chain: the detector may also key off another track (Compressor plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    const char* displayName() const override { return "Nota Ceiling"; }
    int32_t     builtinKind() const override { return 14; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Ceiling", "Gain", "Release", "AutoRelease", "Character", "Lookahead", "StereoLink" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) { case Ceil: return -12.0f; case Gain: return -12.0f; case Release: return 1.0f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        switch (i) { case Ceil: return 0.0f; case Gain: return 24.0f; case Release: return 1000.0f; case Character: return 2.0f;
            case Lookahead: return 10.0f; case StereoLink: return 100.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    Ceiling() {
        p_[Ceil].store(-1.0f); p_[Gain].store(0.0f); p_[Release].store(120.0f); p_[AutoRelease].store(0.0f);
        p_[Character].store(0.0f); p_[Lookahead].store(3.0f); p_[StereoLink].store(100.0f);
        setSampleRate(44100.0, 0);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kHistBins = 800;     // −70.0 … +10.0 LUFS at 0.1 LU
    static constexpr double kHistLo = -70.0;

    static float softClip(float x, float ceil, float amt) {
        // Gentle tanh knee that engages near the ceiling; blended by amount.
        float s = ceil * std::tanh(x / std::max(1e-6f, ceil));
        return x + amt * (s - x);
    }
    static float db(float lin) { return 20.0f * std::log10(std::max(1e-6f, lin)); }
    static float lufs(double ms) { return ms > 1e-10 ? (float)(-0.691 + 10.0 * std::log10(ms)) : -120.0f; }

    // 4× oversampled true-peak estimate via Catmull-Rom between output samples.
    float truePeak(int c, float x) {
        float y0 = tp0_[c], y1 = tp1_[c], y2 = tp2_[c], y3 = x;   // x_{-2..+1}
        float pk = std::fabs(y2);
        for (int k = 1; k < 4; ++k) {
            float t = k * 0.25f;
            float a = y1, b = 0.5f * (y2 - y0), d = 0.5f * (y3 - y1);
            float cc = y2;
            float v = a + t * (b + t * ((3 * (cc - a) - 2 * b - d) + t * (2 * (a - cc) + b + d)));
            pk = std::max(pk, std::fabs(v));
        }
        tp0_[c] = y1; tp1_[c] = y2; tp2_[c] = y3;
        return pk;
    }

    // Cascaded K-weighting biquads (Transposed Direct Form II), per channel.
    float kWeight(int c, float x) {
        float y1 = b0_[0] * x + z1_[c][0];
        z1_[c][0] = b1_[0] * x - a1_[0] * y1 + z1_[c][1];
        z1_[c][1] = b2_[0] * x - a2_[0] * y1;
        float y2 = b0_[1] * y1 + z2_[c][0];
        z2_[c][0] = b1_[1] * y1 - a1_[1] * y2 + z2_[c][1];
        z2_[c][1] = b2_[1] * y1 - a2_[1] * y2;
        return y2;
    }

    void computeKWeighting() {
        // Stage 1: high-shelf. Stage 2: high-pass. ITU-R BS.1770-4 prototypes.
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
          b0_[1] = 1.0f; b1_[1] = -2.0f; b2_[1] = 1.0f;
          a1_[1] = (float)(2.0 * (K * K - 1.0) / a0);
          a2_[1] = (float)((1.0 - K / Q + K * K) / a0);
          // fold a0 into b's
          b0_[1] = (float)(1.0 / a0); b1_[1] = (float)(-2.0 / a0); b2_[1] = (float)(1.0 / a0); }
    }

    // One 400 ms gating block -> histogram -> gated integrated loudness.
    void integrateBlock() {
        double blockMs = blockSum_ / std::max(1, blockN_);
        blockSum_ = 0.0; blockN_ = 0;
        double L = lufs(blockMs);
        if (L <= -70.0) return;                       // absolute gate
        int bin = std::clamp((int)((L - kHistLo) * 10.0), 0, kHistBins - 1);
        histCount_[bin] += 1.0; histSum_[bin] += blockMs;

        // Ungated mean over gated set, then a relative gate 10 LU below it.
        double sumMs = 0.0, n = 0.0;
        for (int b = 0; b < kHistBins; ++b) { sumMs += histSum_[b]; n += histCount_[b]; }
        if (n < 1.0) return;
        double relGate = lufs(sumMs / n) - 10.0;
        int gb = std::clamp((int)((relGate - kHistLo) * 10.0), 0, kHistBins - 1);
        double gSum = 0.0, gN = 0.0;
        for (int b = gb; b < kHistBins; ++b) { gSum += histSum_[b]; gN += histCount_[b]; }
        if (gN >= 1.0) lufsI_.store(lufs(gSum / gN), std::memory_order_relaxed);
    }

    double sr_ = 44100.0;
    std::vector<float> laBuf_; int laMax_ = 0, laW_ = 0;
    float env_ = 1.0f, grPrev_ = 0.0f;

    // K-weighting state (2 stages × 2 channels).
    float b0_[2] = {}, b1_[2] = {}, b2_[2] = {}, a1_[2] = {}, a2_[2] = {};
    float z1_[2][2] = {}, z2_[2][2] = {};
    float tp0_[2] = {}, tp1_[2] = {}, tp2_[2] = {};

    std::vector<float> msRing_; int msW_ = 0, momN_ = 0, shortN_ = 0;
    double momSum_ = 0.0, shortSum_ = 0.0;
    int hopN_ = 0, hop_ = 0;
    std::vector<double> histCount_, histSum_;
    double blockSum_ = 0.0; int blockN_ = 0;

    std::atomic<float> inPeak_{-120.0f}, outPeak_{-120.0f}, truePeak_{-120.0f}, gr_{0.0f};
    std::atomic<float> lufsM_{-120.0f}, lufsS_{-120.0f}, lufsI_{-120.0f};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
