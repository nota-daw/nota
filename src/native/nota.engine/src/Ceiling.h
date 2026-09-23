// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Ceiling — a look-ahead brick-wall limiter (device kind 14).
// Input gain drives the signal into a hard ceiling; a look-ahead delay lets the gain duck
// *before* each peak reaches the output, a smoothed release (with an optional program-
// dependent auto stage that slows as the reduction deepens) governs recovery, and a final
// safety clip guarantees the sample peak never passes the ceiling. Three voicing characters
// (Clean / Punch / Glue) reshape attack / release, and a stereo-link blend trades a glued
// centre for independent channels.
//
//   in ─▶ gain ─▶ look-ahead delay ────────────────▶ × gain ─▶ [Glue soft clip] ─▶ clip ─▶ out
//                 │                                    ▲                                  │
//   (key) ─▶ SC HP ─▶ peak / 4× true peak ─▶ envelope ─┘               Delta: in − out ◀──┘
//
// True Peak makes the detector read the 4× interpolated peak, so the inter-sample overs a
// DAC or a codec would make are held under the ceiling too. SC HP takes the lows out of the
// detector (the bass stops pumping the mix). Delta plays what the limiter takes away. A
// filtered side-chain from another track is supported like the Compressor. On top of the
// limiting it runs a full BS.1770 / EBU R128 loudness meter — momentary / short-term /
// integrated LUFS, loudness range (LRA), a 4× true peak and PLR — against a Target, so the
// card can answer "is this deliverable" without a second plug-in.
//
// Params are the device's raw units (not 0..1) and APPEND ONLY: 0..6 are the original
// layout, 7.. are appended and default to the old sound, so older projects open unchanged.
// Telemetry (scopeRead): kTele live values, then the 4-second level window (kLvl cells ×
// 7 series: input peak, output peak, max GR, clipped, over-the-ceiling share, release, mean
// GR) and the 60-second loudness window (kLoud cells × M / S / I), oldest → newest.
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset peaks (holds, max GR, clip count, the level window), 1 = reset the
// loudness (integrated, LRA, the loudness window) and the peaks.
// A core Device (JUCE-free); params are atomic / lock-free; allocation-free after setSampleRate.

#pragma once

#include "Device.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Ceiling : public Device {
public:
    enum { Ceil = 0, Gain, Release, AutoRelease, Character, Lookahead, StereoLink,
           TruePeak, Delta, ScHp, Target, kNumParams };

    // Telemetry slots (scopeRead). 0..6 keep the original layout.
    enum {
        S_InPeak = 0,   // input peak after Gain, this block, dBFS
        S_OutPeak,      // output peak, this block, dBFS
        S_Gr,           // gain reduction, this block, dB (>= 0)
        S_LufsM,        // momentary loudness (400 ms), LUFS
        S_LufsS,        // short-term loudness (3 s), LUFS
        S_LufsI,        // integrated (gated) loudness since the loudness reset, LUFS
        S_TruePeak,     // output true peak (4×), this block, dBTP
        S_PeakInHold,   // highest input peak since the peak reset, dBFS
        S_PeakOutHold,  // highest output peak since the peak reset
        S_MaxGrHold,    // deepest reduction since the peak reset, dB
        S_TpHold,       // highest output true peak since the peak reset, dBTP
        S_Lra,          // loudness range, LU (0 until there is enough)
        S_Plr,          // peak-to-loudness ratio: true-peak hold − integrated, dB
        S_OverPct,      // share of the level window the input was over the ceiling, 0..1
        S_AvgGr,        // mean reduction over the level window (while there is signal), dB
        S_WinMaxGr,     // deepest reduction in the level window, dB
        S_RelNow,       // the release in use now, ms (character and auto stage included)
        S_RelMin,       // shortest release used in the window, ms
        S_RelMax,       // longest release used in the window, ms
        S_Clips,        // transients that reached the clip stage since the peak reset
        S_SampleRate,
        S_Cpu,          // share of real time spent in process()
        S_Latency,      // samples
        S_WinInPeak,    // highest input peak in the level window, dBFS
        S_WinOutPeak,   // highest output peak in the level window, dBFS
        S_LvlCells,     // kLvl
        S_LoudCells,    // kLoud
        S_LvlSeconds,   // the level window's length, s
        S_LoudSeconds,  // the loudness window's length, s
        S_Delta,        // 1 while Delta plays the difference
        S_Measured, // seconds of audio in the integrated reading
        S_MaxM,         // highest momentary loudness since the loudness reset
        S_MaxS,         // highest short-term loudness since the loudness reset
        kTele = 40
    };
    static constexpr int kLvl = 64, kLvlSeries = 7, kLoud = 120, kLoudSeries = 3;
    static constexpr double kLvlSeconds = 4.0, kLoudSeconds = 60.0;
    // Level window series (each kLvl long, after kTele).
    enum { L_In = 0, L_Out, L_GrMax, L_Clip, L_Over, L_Rel, L_GrAvg };
    static constexpr int kLvlAt = kTele;
    static constexpr int kLoudAt = kTele + kLvlSeries * kLvl;
    static constexpr int kScope = kLoudAt + kLoudSeries * kLoud;

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store((float)sr_, std::memory_order_relaxed);
        laMax_ = static_cast<int>(sr_ * 0.010) + 2;          // 10 ms max look-ahead
        laBuf_.assign(static_cast<size_t>(laMax_) * 2, 0.0f); laW_ = 0;

        // BS.1770-4 K-weighting: a high-shelf pre-filter then a 38 Hz high-pass.
        computeKWeighting();
        for (int c = 0; c < 2; ++c) { z1_[c][0] = z1_[c][1] = z2_[c][0] = z2_[c][1] = 0.0f; }

        momN_ = std::max(1, (int)std::lround(sr_ * 0.400));   // momentary 400 ms
        shortN_ = std::max(1, (int)std::lround(sr_ * 3.000)); // short-term 3 s
        msRing_.assign(static_cast<size_t>(shortN_), 0.0f); msW_ = 0;
        momSum_ = 0.0; shortSum_ = 0.0;
        hopN_ = std::max(1, (int)std::lround(sr_ * 0.100));   // gate / LRA every 100 ms
        hop_ = 0;
        histCount_.assign(kHistBins, 0.0);
        histSum_.assign(kHistBins, 0.0);
        lraHist_.assign(kHistBins, 0.0);
        tpOut_ = {}; tpDet_ = {};
        for (auto& s : hpZ_) s[0] = s[1] = 0.0f;
        hpHz_ = -1.0f;
        env_ = 1.0f; grPrev_ = 0.0f;
        lvlCellN_ = std::max(1, (int)std::lround(sr_ * kLvlSeconds / kLvl));
        clipHoldN_ = std::max(1, (int)std::lround(sr_ * 0.020));
        clearLevel(); clearLoudness();
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetPeaksReq_.exchange(false, std::memory_order_acq_rel)) clearLevel();
        if (resetLoudReq_.exchange(false, std::memory_order_acq_rel)) { clearLoudness(); clearLevel(); }

        const double sr = sr_;
        const int ch = std::clamp((int)std::lround(get(Character)), 0, 2);
        // Clean / Punch / Glue — attack ms, release multiplier, soft-clip amount.
        static const float chAtk[3]  = { 0.20f, 2.0f, 0.6f };
        static const float chRel[3]  = { 1.0f, 0.65f, 2.6f };
        static const float chSoft[3] = { 0.0f, 0.0f, 0.6f };

        const float ceilDb  = get(Ceil);
        const float ceilLin = std::pow(10.0f, ceilDb / 20.0f);
        const float inGain  = std::pow(10.0f, get(Gain) / 20.0f);
        const float relMs   = std::max(1.0f, get(Release)) * chRel[ch];
        const bool  autoRel = get(AutoRelease) > 0.5f;
        const float link    = std::clamp(get(StereoLink) * 0.01f, 0.0f, 1.0f);
        const float soft    = chSoft[ch];
        const bool  tpMode  = get(TruePeak) > 0.5f;
        const bool  delta   = get(Delta) > 0.5f;
        const int   la      = lookaheadSamples();

        // Side-chain high-pass on the detector (RBJ, Q 0.707); off at its bottom (20 Hz).
        const float hpHz = std::clamp(get(ScHp), 20.0f, 500.0f);
        const bool  hpOn = hpHz > 20.5f;
        if (hpOn && std::fabs(hpHz - hpHz_) > 1e-3f) computeHp(hpHz);

        const double atkC = std::exp(-1.0 / (std::max(0.05f, chAtk[ch]) * 0.001 * sr));
        double relC = std::exp(-1.0 / (relMs * 0.001 * sr));
        float relEff = relMs;

        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);

        float inPk = 0.0f, outPk = 0.0f, tpPk = 0.0f, maxGr = 0.0f;

        for (int32_t i = 0; i < frames; ++i) {
            // Control rate: the auto stage slows the release as the reduction deepens
            // (×1 at 1 dB … ×5 at 7 dB and deeper), so dense passages don't pump.
            if ((i & 15) == 0) {
                const float t = autoRel ? std::clamp((grPrev_ - 1.0f) / 6.0f, 0.0f, 1.0f) : 0.0f;
                const float r = relMs * (1.0f + 4.0f * t);
                if (std::fabs(r - relEff) > 0.01f || i == 0) { relEff = r; relC = std::exp(-1.0 / (relEff * 0.001 * sr)); }
            }

            float l = buf[i * 2] * inGain, r = buf[i * 2 + 1] * inGain;
            const float aL = std::fabs(l), aR = std::fabs(r);

            // Detector: optionally high-passed, sample or 4× true peak, plus the key.
            float xl = l, xr = r;
            if (hpOn) { xl = hp(0, xl); xr = hp(1, xr); }
            float dL = tpMode ? tpDetect(0, xl) : std::fabs(xl);
            float dR = tpMode ? tpDetect(1, xr) : std::fabs(xr);
            float det = std::max(dL, dR);
            if (sc) {
                float kl = sc[i * 2] * scGain, kr = sc[i * 2 + 1] * scGain;
                if (hpOn) { kl = hp(2, kl); kr = hp(3, kr); }
                det = std::max(det, std::max(std::fabs(kl), std::fabs(kr)));
            }
            const float aIn = std::max(aL, aR);
            if (aIn > inPk) inPk = aIn;

            // Peak follower with fast attack; look-ahead delay is what makes it duck
            // before the transient.
            if (det > env_) env_ = (float)(atkC * env_ + (1.0 - atkC) * det);
            else env_ = (float)(relC * env_ + (1.0 - relC) * det);

            float gain = env_ > ceilLin ? ceilLin / env_ : 1.0f;   // linked target gain
            grPrev_ = gain < 1.0f ? -20.0f * std::log10(std::max(1e-6f, gain)) : 0.0f;
            if (grPrev_ > maxGr) maxGr = grPrev_;

            // Stereo link: 100 % = one gain for both; less = ease each channel toward
            // its own headroom so a hard pan doesn't pull the other side down.
            float gL = gain, gR = gain;
            if (link < 0.999f) {
                float tL = dL > ceilLin ? ceilLin / dL : 1.0f;
                float tR = dR > ceilLin ? ceilLin / dR : 1.0f;
                gL = link * gain + (1.0f - link) * std::min(gain < 1.0f ? gain : 1.0f, tL);
                gR = link * gain + (1.0f - link) * std::min(gain < 1.0f ? gain : 1.0f, tR);
            }

            // Look-ahead: apply the gain (already ducked) to the delayed sample.
            laBuf_[laW_ * 2] = l; laBuf_[laW_ * 2 + 1] = r;
            int rp = laW_ - la; if (rp < 0) rp += laMax_;
            const float dryL = laBuf_[rp * 2], dryR = laBuf_[rp * 2 + 1];
            float ol = dryL * gL, or_ = dryR * gR;
            if (++laW_ >= laMax_) laW_ = 0;

            // Character soft-clip (Glue) then the hard safety ceiling — brick wall. A
            // transient that reaches the clip (Punch lets the attack through) is counted.
            if (soft > 0.0f) { ol = softClip(ol, ceilLin, soft); or_ = softClip(or_, ceilLin, soft); }
            const bool clipping = std::max(std::fabs(ol), std::fabs(or_)) > ceilLin * 1.0001f;
            ol = std::clamp(ol, -ceilLin, ceilLin);
            or_ = std::clamp(or_, -ceilLin, ceilLin);
            if (clipping) {
                if (clipHold_ <= 0) ++clips_;
                clipHold_ = clipHoldN_;
                cClip_ = 1.0f;
            } else if (clipHold_ > 0) --clipHold_;

            buf[i * 2]     = delta ? dryL - ol : ol;
            buf[i * 2 + 1] = delta ? dryR - or_ : or_;

            const float aOut = std::max(std::fabs(ol), std::fabs(or_));
            outPk = std::max(outPk, aOut);
            tpPk = std::max(tpPk, std::max(truePeak(0, ol), truePeak(1, or_)));

            // Level window cell.
            cIn_ = std::max(cIn_, aIn); cOut_ = std::max(cOut_, aOut);
            cGrMax_ = std::max(cGrMax_, grPrev_); cGrSum_ += grPrev_;
            if (aIn > ceilLin) ++cOver_;
            cRelSum_ += relEff;
            if (++cN_ >= lvlCellN_) pushLevelCell();

            // K-weighted loudness on the limited output (what actually ships).
            float kl = kWeight(0, ol), kr = kWeight(1, or_);
            float ms = kl * kl + kr * kr;
            shortSum_ += ms - msRing_[msW_];
            msRing_[msW_] = ms;
            int momTail = msW_ - momN_; if (momTail < 0) momTail += shortN_;
            momSum_ += ms - msRing_[momTail];
            if (++msW_ >= shortN_) msW_ = 0;
            ++loudSamples_;
            if (++hop_ >= hopN_) { hop_ = 0; onHop(); }
        }

        // Publish meters (dB; peaks in dBFS, GR positive-down).
        const float inDb = db(inPk), outDb = db(outPk), tpDb = db(tpPk);
        inPeak_.store(inDb, std::memory_order_relaxed);
        outPeak_.store(outDb, std::memory_order_relaxed);
        truePeak_.store(tpDb, std::memory_order_relaxed);
        gr_.store(maxGr, std::memory_order_relaxed);
        if (frames > 0) {
            pkInHold_ = std::max(pkInHold_, inDb); pkOutHold_ = std::max(pkOutHold_, outDb);
            maxGrHold_ = std::max(maxGrHold_, maxGr); tpHold_ = std::max(tpHold_, tpDb);
        }
        pkInHoldA_.store(pkInHold_, std::memory_order_relaxed);
        pkOutHoldA_.store(pkOutHold_, std::memory_order_relaxed);
        maxGrHoldA_.store(maxGrHold_, std::memory_order_relaxed);
        tpHoldA_.store(tpHold_, std::memory_order_relaxed);
        clipsA_.store((float)clips_, std::memory_order_relaxed);
        relNowA_.store(relEff, std::memory_order_relaxed);
        deltaA_.store(delta ? 1.0f : 0.0f, std::memory_order_relaxed);
        scBuf_ = nullptr;
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    // GR (dB, >=0) for the shell titlebar meter, like the Compressor.
    float gainReductionDb() const override { return gr_.load(std::memory_order_relaxed); }

    void deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) override {
        if (id == 0) resetPeaksReq_.store(true, std::memory_order_release);
        else if (id == 1) resetLoudReq_.store(true, std::memory_order_release);
    }

    // Telemetry block, then the level window and the loudness window, oldest → newest.
    // Writes as much of that layout as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        t[S_InPeak]   = inPeak_.load(std::memory_order_relaxed);
        t[S_OutPeak]  = outPeak_.load(std::memory_order_relaxed);
        t[S_Gr]       = gr_.load(std::memory_order_relaxed);
        t[S_LufsM]    = lufsM_.load(std::memory_order_relaxed);
        t[S_LufsS]    = lufsS_.load(std::memory_order_relaxed);
        t[S_LufsI]    = lufsI_.load(std::memory_order_relaxed);
        t[S_TruePeak] = truePeak_.load(std::memory_order_relaxed);
        t[S_PeakInHold]  = pkInHoldA_.load(std::memory_order_relaxed);
        t[S_PeakOutHold] = pkOutHoldA_.load(std::memory_order_relaxed);
        t[S_MaxGrHold]   = maxGrHoldA_.load(std::memory_order_relaxed);
        t[S_TpHold]      = tpHoldA_.load(std::memory_order_relaxed);
        t[S_Lra]         = lraA_.load(std::memory_order_relaxed);
        t[S_Plr]         = t[S_LufsI] > -119.0f && t[S_TpHold] > -119.0f ? t[S_TpHold] - t[S_LufsI] : 0.0f;
        t[S_RelNow]      = relNowA_.load(std::memory_order_relaxed);
        t[S_Clips]       = clipsA_.load(std::memory_order_relaxed);
        t[S_SampleRate]  = srA_.load(std::memory_order_relaxed);
        t[S_Cpu]         = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency]     = (float)latencySamples();
        t[S_LvlCells]    = (float)kLvl;
        t[S_LoudCells]   = (float)kLoud;
        t[S_LvlSeconds]  = (float)kLvlSeconds;
        t[S_LoudSeconds] = (float)kLoudSeconds;
        t[S_Delta]       = deltaA_.load(std::memory_order_relaxed);
        t[S_Measured] = measuredA_.load(std::memory_order_relaxed);
        t[S_MaxM]        = maxMA_.load(std::memory_order_relaxed);
        t[S_MaxS]        = maxSA_.load(std::memory_order_relaxed);

        // Window statistics from the level cells.
        const int lh = lvlHead_.load(std::memory_order_relaxed);
        double overSum = 0.0, grSum = 0.0; int grN = 0, cells = 0;
        float winGr = 0.0f, winIn = -120.0f, winOut = -120.0f, relMin = 1e9f, relMax = 0.0f;
        for (int k = 0; k < kLvl; ++k) {
            const int c = (lh + k) % kLvl;
            if (lvl_[L_In][c] <= -119.0f && lvl_[L_Rel][c] <= 0.0f) continue;   // empty cell
            ++cells;
            overSum += lvl_[L_Over][c];
            winGr = std::max(winGr, lvl_[L_GrMax][c]);
            winIn = std::max(winIn, lvl_[L_In][c]);
            winOut = std::max(winOut, lvl_[L_Out][c]);
            if (lvl_[L_In][c] > -70.0f) { grSum += lvl_[L_GrAvg][c]; ++grN; }
            if (lvl_[L_GrMax][c] > 0.1f && lvl_[L_Rel][c] > 0.0f) {
                relMin = std::min(relMin, lvl_[L_Rel][c]); relMax = std::max(relMax, lvl_[L_Rel][c]);
            }
        }
        t[S_OverPct]    = cells > 0 ? (float)(overSum / cells) : 0.0f;
        t[S_AvgGr]      = grN > 0 ? (float)(grSum / grN) : 0.0f;
        t[S_WinMaxGr]   = winGr;
        t[S_WinInPeak]  = winIn;
        t[S_WinOutPeak] = winOut;
        if (relMax <= 0.0f) relMin = relMax = t[S_RelNow];
        t[S_RelMin] = relMin; t[S_RelMax] = relMax;

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int s = 0; s < kLvlSeries; ++s)
            for (int k = 0; k < kLvl && n < maxSamples; ++k) out[n++] = lvl_[s][(lh + k) % kLvl];
        const int dh = loudHead_.load(std::memory_order_relaxed);
        for (int s = 0; s < kLoudSeries; ++s)
            for (int k = 0; k < kLoud && n < maxSamples; ++k) out[n++] = loud_[s][(dh + k) % kLoud];
        return n;
    }

    int32_t latencySamples() const override { return lookaheadSamples(); }

    // Side-chain: the detector may also key off another track (Compressor plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    const char* displayName() const override { return "Nota Ceiling"; }
    int32_t     builtinKind() const override { return 14; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = { "Ceiling", "Gain", "Release", "AutoRelease", "Character", "Lookahead", "StereoLink",
                                              "True Peak", "Delta", "SC HP", "Target" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) { case Ceil: return -12.0f; case Gain: return -12.0f; case Release: return 1.0f; case ScHp: return 20.0f;
            case Target: return -30.0f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        switch (i) { case Ceil: return 0.0f; case Gain: return 24.0f; case Release: return 1000.0f; case Character: return 2.0f;
            case Lookahead: return 10.0f; case StereoLink: return 100.0f; case ScHp: return 500.0f; case Target: return -5.0f;
            default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        char b[1000];
        static const char* chars[3] = { "Clean", "Punch", "Glue" };
        if (id == 0) {
            std::string s = chars[std::clamp((int)std::lround(get(Character)), 0, 2)];
            std::snprintf(b, sizeof b, " - gain %+.1f dB - ceiling %.1f dB%s", get(Gain), get(Ceil), get(TruePeak) > 0.5f ? " TP" : "");
            s += b;
            if (get(AutoRelease) > 0.5f) std::snprintf(b, sizeof b, " - release auto (from %.0f ms)", get(Release));
            else std::snprintf(b, sizeof b, " - release %.0f ms", get(Release));
            s += b;
            std::snprintf(b, sizeof b, " - look-ahead %.1f ms - link %.0f %%", get(Lookahead), get(StereoLink)); s += b;
            if (get(ScHp) > 20.5f) { std::snprintf(b, sizeof b, " - SC HP %.0f Hz", get(ScHp)); s += b; }
            if (scTrackId_.load(std::memory_order_relaxed) >= 0) {
                std::snprintf(b, sizeof b, " - key from track %d", scTrackId_.load(std::memory_order_relaxed)); s += b;
            }
            std::snprintf(b, sizeof b, " - target %.0f LUFS", get(Target)); s += b;
            if (get(Delta) > 0.5f) s += " - DELTA (playing what it removes)";
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto lv = [](float v) { char t[24]; if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf"); else std::snprintf(t, sizeof t, "%.1f", v); return std::string(t); };
            const float fromTarget = sc[S_LufsI] > -119.0f ? sc[S_LufsI] - get(Target) : 0.0f;
            std::snprintf(b, sizeof b,
                          "GR %.1f dB (window avg %.1f, max %.1f; max since reset %.1f) - in %s dBFS, out %s dBFS - peaks since reset "
                          "in %s, out %s, true peak %s dBTP - over the ceiling %.0f %% of the last 4 s - release now %.0f ms (%.0f..%.0f) - "
                          "%.0f transients reached the clip - LUFS M %s, S %s, I %s (%.0f s measured, %s LU from target %.0f) - "
                          "LRA %.1f LU - PLR %.1f dB - latency %.0f samples",
                          sc[S_Gr], sc[S_AvgGr], sc[S_WinMaxGr], sc[S_MaxGrHold], lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeak]).c_str(),
                          lv(sc[S_PeakInHold]).c_str(), lv(sc[S_PeakOutHold]).c_str(), lv(sc[S_TpHold]).c_str(), sc[S_OverPct] * 100.0f,
                          sc[S_RelNow], sc[S_RelMin], sc[S_RelMax], sc[S_Clips], lv(sc[S_LufsM]).c_str(), lv(sc[S_LufsS]).c_str(),
                          lv(sc[S_LufsI]).c_str(), sc[S_Measured], sc[S_LufsI] > -119.0f ? lv(fromTarget).c_str() : "n/a",
                          get(Target), sc[S_Lra], sc[S_Plr], sc[S_Latency]);
            std::string s = b;
            if (sc[S_Delta] > 0.5f) s += " - DELTA on";
            return s;
        }
        if (id == 2) {
            return "Params are in their own units (not 0..1). Ceiling: -12..0 dB, the output never passes it (dBTP with True Peak). "
                   "Gain: -12..+24 dB into the limiter. Release: 1..1000 ms (times the character: Clean x1, Punch x0.65, Glue x2.6). "
                   "AutoRelease: >= 0.5 slows the release as the reduction deepens (x1 at 1 dB .. x5 at 7 dB). Character: 0 Clean "
                   "(transparent, 0.2 ms attack), 1 Punch (2 ms attack - lets the attack through to the clip), 2 Glue (slow, with a "
                   "soft knee under the ceiling). Lookahead: 0..10 ms (the latency). StereoLink: 0..100 % (100 = one gain for both "
                   "channels). True Peak: >= 0.5 detects the 4x inter-sample peak. Delta: >= 0.5 plays what the limiter removes "
                   "(in - out). SC HP: 20..500 Hz high-pass on the detector (20 = off). Target: -30..-5 LUFS, the loudness the "
                   "meters compare against (-14 streaming, -16 podcast, -23 broadcast); it does not change the sound.";
        }
        return {};
    }

    Ceiling() {
        p_[Ceil].store(-1.0f); p_[Gain].store(0.0f); p_[Release].store(120.0f); p_[AutoRelease].store(0.0f);
        p_[Character].store(0.0f); p_[Lookahead].store(3.0f); p_[StereoLink].store(100.0f);
        p_[TruePeak].store(0.0f); p_[Delta].store(0.0f); p_[ScHp].store(20.0f); p_[Target].store(-14.0f);
        setSampleRate(44100.0, 0);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kHistBins = 800;     // −70.0 … +10.0 LUFS at 0.1 LU
    static constexpr double kHistLo = -70.0;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    // The look-ahead in samples; True Peak needs at least the interpolator's delay so the
    // inter-sample reading still lands before its sample does.
    int lookaheadSamples() const {
        int la = (int)std::lround(get(Lookahead) * 0.001 * sr_);
        if (get(TruePeak) > 0.5f) la = std::max(la, kTpDelay);
        return std::clamp(la, 0, laMax_ - 1);
    }

    static float softClip(float x, float ceil, float amt) {
        // Gentle tanh knee that engages near the ceiling; blended by amount.
        float s = ceil * std::tanh(x / std::max(1e-6f, ceil));
        return x + amt * (s - x);
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float lufs(double ms) { return ms > 1e-10 ? (float)(-0.691 + 10.0 * std::log10(ms)) : -120.0f; }

    // 4× true peak (BS.1770-4 Annex 2 style): a 16-tap windowed-sinc interpolator per phase over
    // a short history; the peak is the highest of the sample and the three points after it. The
    // reading is kTpDelay samples behind its input (the filter's centre).
    static constexpr int kTpTaps = 16, kTpDelay = 8;
    struct TpState { std::array<float, kTpTaps> h {}; int w = 0; };
    static const std::array<std::array<float, kTpTaps>, 3>& tpCoefs() {
        static const auto c = [] {
            std::array<std::array<float, kTpTaps>, 3> k {};
            for (int p = 0; p < 3; ++p) {
                const double t = (p + 1) * 0.25;
                double sum = 0.0;
                for (int i = 0; i < kTpTaps; ++i) {
                    const double u = i - (kTpDelay - 1) - t;
                    const double sinc = std::fabs(u) < 1e-9 ? 1.0 : std::sin(kPi * u) / (kPi * u);
                    const double win = std::fabs(u) < 8.5 ? 0.5 * (1.0 + std::cos(kPi * u / 8.5)) : 0.0;
                    k[(size_t)p][(size_t)i] = (float)(sinc * win); sum += sinc * win;
                }
                for (auto& v : k[(size_t)p]) v = (float)(v / sum);
            }
            return k;
        }();
        return c;
    }
    static float tpPush(TpState& s, float x) {
        s.h[(size_t)s.w] = x;
        s.w = (s.w + 1) % kTpTaps;                     // s.w is now the oldest sample
        const auto& c = tpCoefs();
        float pk = std::fabs(s.h[(size_t)((s.w + kTpDelay - 1) % kTpTaps)]);
        for (int p = 0; p < 3; ++p) {
            float acc = 0.0f;
            for (int i = 0; i < kTpTaps; ++i) acc += c[(size_t)p][(size_t)i] * s.h[(size_t)((s.w + i) % kTpTaps)];
            pk = std::max(pk, std::fabs(acc));
        }
        return pk;
    }
    // Output true-peak meter.
    float truePeak(int c, float x) { return tpPush(tpOut_[(size_t)c], x); }
    // Detector true peak (before the look-ahead, so the gain is down before the over arrives);
    // the plain sample peak is taken now, the inter-sample one kTpDelay samples later.
    float tpDetect(int c, float x) { return std::max(std::fabs(x), tpPush(tpDet_[(size_t)c], x)); }

    void computeHp(float hz) {
        hpHz_ = hz;
        const double w = 2.0 * kPi * hz / sr_, cw = std::cos(w), al = std::sin(w) / (2.0 * 0.7071);
        const double a0 = 1.0 + al;
        hb0_ = (float)((1.0 + cw) / 2.0 / a0); hb1_ = (float)(-(1.0 + cw) / a0); hb2_ = hb0_;
        ha1_ = (float)(-2.0 * cw / a0); ha2_ = (float)((1.0 - al) / a0);
    }
    float hp(int c, float x) {
        auto& z = hpZ_[(size_t)c];
        const float y = hb0_ * x + z[0];
        z[0] = hb1_ * x - ha1_ * y + z[1];
        z[1] = hb2_ * x - ha2_ * y;
        return y;
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
          a1_[1] = (float)(2.0 * (K * K - 1.0) / a0);
          a2_[1] = (float)((1.0 - K / Q + K * K) / a0);
          b0_[1] = (float)(1.0 / a0); b1_[1] = (float)(-2.0 / a0); b2_[1] = (float)(1.0 / a0); }
    }

    // Every 100 ms: publish M / S, gate the 400 ms block (75 % overlap) into the integrated
    // reading, feed the short-term value to the loudness range, and step the 60 s window.
    void onHop() {
        const float m = loudSamples_ >= momN_ ? lufs(momSum_ / momN_) : -120.0f;
        const float s = loudSamples_ >= momN_ ? lufs(shortSum_ / std::max<int64_t>(1, std::min<int64_t>(loudSamples_, shortN_))) : -120.0f;
        lufsM_.store(m, std::memory_order_relaxed);
        lufsS_.store(s, std::memory_order_relaxed);
        if (m > maxM_) { maxM_ = m; maxMA_.store(m, std::memory_order_relaxed); }
        if (loudSamples_ >= shortN_ && s > maxS_) { maxS_ = s; maxSA_.store(s, std::memory_order_relaxed); }

        if (loudSamples_ >= momN_ && m > -70.0f) {                       // absolute gate
            const int bin = std::clamp((int)((m - kHistLo) * 10.0), 0, kHistBins - 1);
            histCount_[bin] += 1.0; histSum_[bin] += momSum_ / momN_;
            double sumMs = 0.0, n = 0.0;
            for (int k = 0; k < kHistBins; ++k) { sumMs += histSum_[k]; n += histCount_[k]; }
            if (n >= 1.0) {
                const double relGate = lufs(sumMs / n) - 10.0;           // relative gate
                const int gb = std::clamp((int)std::ceil((relGate - kHistLo) * 10.0), 0, kHistBins - 1);
                double gSum = 0.0, gN = 0.0;
                for (int k = gb; k < kHistBins; ++k) { gSum += histSum_[k]; gN += histCount_[k]; }
                if (gN >= 1.0) lufsI_.store(lufs(gSum / gN), std::memory_order_relaxed);
            }
            measuredA_.store((float)((double)loudSamples_ / sr_), std::memory_order_relaxed);
        }
        // Loudness range (EBU Tech 3342): short-term values, −70 absolute and −20 LU relative
        // gates, the spread between the 10th and the 95th percentile.
        if (loudSamples_ >= shortN_ && s > -70.0f) {
            const int bin = std::clamp((int)((s - kHistLo) * 10.0), 0, kHistBins - 1);
            lraHist_[bin] += 1.0;
            double e = 0.0, n = 0.0;
            for (int k = 0; k < kHistBins; ++k) if (lraHist_[k] > 0.0) {
                e += lraHist_[k] * std::pow(10.0, (kHistLo + (k + 0.5) * 0.1) / 10.0); n += lraHist_[k];
            }
            if (n >= 2.0) {
                const double gate = 10.0 * std::log10(e / n) - 20.0;
                const int gb = std::clamp((int)std::ceil((gate - kHistLo) * 10.0), 0, kHistBins - 1);
                double cnt = 0.0;
                for (int k = gb; k < kHistBins; ++k) cnt += lraHist_[k];
                if (cnt >= 2.0) {
                    double acc = 0.0; int lo = -1, hi = -1;
                    for (int k = gb; k < kHistBins; ++k) {
                        acc += lraHist_[k];
                        if (lo < 0 && acc >= 0.10 * cnt) lo = k;
                        if (hi < 0 && acc >= 0.95 * cnt) { hi = k; break; }
                    }
                    if (lo >= 0 && hi >= 0) lraA_.store((float)((hi - lo) * 0.1), std::memory_order_relaxed);
                }
            }
        }
        // 60 s window: one cell every 5 hops (0.5 s).
        if (++hopInCell_ >= 5) {
            hopInCell_ = 0;
            const int h = loudHead_.load(std::memory_order_relaxed);
            loud_[0][h] = m; loud_[1][h] = s; loud_[2][h] = lufsI_.load(std::memory_order_relaxed);
            loudHead_.store((h + 1) % kLoud, std::memory_order_relaxed);
        }
    }

    void pushLevelCell() {
        const int h = lvlHead_.load(std::memory_order_relaxed);
        lvl_[L_In][h] = db(cIn_); lvl_[L_Out][h] = db(cOut_);
        lvl_[L_GrMax][h] = cGrMax_; lvl_[L_Clip][h] = cClip_;
        lvl_[L_Over][h] = (float)cOver_ / (float)std::max(1, cN_);
        lvl_[L_Rel][h] = (float)(cRelSum_ / std::max(1, cN_));
        lvl_[L_GrAvg][h] = (float)(cGrSum_ / std::max(1, cN_));
        lvlHead_.store((h + 1) % kLvl, std::memory_order_relaxed);
        cIn_ = cOut_ = cGrMax_ = cClip_ = 0.0f; cGrSum_ = cRelSum_ = 0.0; cOver_ = 0; cN_ = 0;
    }

    void clearLevel() {
        for (int s = 0; s < kLvlSeries; ++s) lvl_[s].fill(0.0f);
        lvl_[L_In].fill(-120.0f); lvl_[L_Out].fill(-120.0f);
        cIn_ = cOut_ = cGrMax_ = cClip_ = 0.0f; cGrSum_ = cRelSum_ = 0.0; cOver_ = 0; cN_ = 0;
        pkInHold_ = pkOutHold_ = tpHold_ = -120.0f; maxGrHold_ = 0.0f; clips_ = 0; clipHold_ = 0;
        pkInHoldA_.store(-120.0f); pkOutHoldA_.store(-120.0f); tpHoldA_.store(-120.0f); maxGrHoldA_.store(0.0f); clipsA_.store(0.0f);
    }
    void clearLoudness() {
        std::fill(histCount_.begin(), histCount_.end(), 0.0);
        std::fill(histSum_.begin(), histSum_.end(), 0.0);
        std::fill(lraHist_.begin(), lraHist_.end(), 0.0);
        for (auto& s : loud_) s.fill(-120.0f);
        loudSamples_ = 0; hopInCell_ = 0; maxM_ = maxS_ = -120.0f;
        std::fill(msRing_.begin(), msRing_.end(), 0.0f); momSum_ = shortSum_ = 0.0; msW_ = 0; hop_ = 0;
        lufsI_.store(-120.0f); lufsM_.store(-120.0f); lufsS_.store(-120.0f); lraA_.store(0.0f);
        measuredA_.store(0.0f); maxMA_.store(-120.0f); maxSA_.store(-120.0f);
    }

    double sr_ = 44100.0;
    std::vector<float> laBuf_; int laMax_ = 0, laW_ = 0;
    float env_ = 1.0f, grPrev_ = 0.0f;

    // K-weighting state (2 stages × 2 channels); true-peak interpolators; side-chain HP.
    float b0_[2] = {}, b1_[2] = {}, b2_[2] = {}, a1_[2] = {}, a2_[2] = {};
    float z1_[2][2] = {}, z2_[2][2] = {};
    std::array<TpState, 2> tpOut_ {}, tpDet_ {};
    float hb0_ = 1.0f, hb1_ = 0.0f, hb2_ = 0.0f, ha1_ = 0.0f, ha2_ = 0.0f, hpHz_ = -1.0f;
    std::array<std::array<float, 2>, 4> hpZ_ {};

    // Loudness.
    std::vector<float> msRing_; int msW_ = 0, momN_ = 0, shortN_ = 0;
    double momSum_ = 0.0, shortSum_ = 0.0;
    int hopN_ = 0, hop_ = 0, hopInCell_ = 0;
    int64_t loudSamples_ = 0;
    std::vector<double> histCount_, histSum_, lraHist_;
    float maxM_ = -120.0f, maxS_ = -120.0f;

    // Level window cell accumulators, holds, clip counting.
    int lvlCellN_ = 1, cN_ = 0, cOver_ = 0, clipHoldN_ = 1, clipHold_ = 0;
    float cIn_ = 0.0f, cOut_ = 0.0f, cGrMax_ = 0.0f, cClip_ = 0.0f;
    double cGrSum_ = 0.0, cRelSum_ = 0.0;
    float pkInHold_ = -120.0f, pkOutHold_ = -120.0f, maxGrHold_ = 0.0f, tpHold_ = -120.0f;
    int64_t clips_ = 0;
    double cpuS_ = 0.0;

    std::array<std::array<float, kLvl>, kLvlSeries> lvl_ {};
    std::array<std::array<float, kLoud>, kLoudSeries> loud_ {};
    std::atomic<int> lvlHead_{0}, loudHead_{0};

    std::atomic<float> inPeak_{-120.0f}, outPeak_{-120.0f}, truePeak_{-120.0f}, gr_{0.0f};
    std::atomic<float> lufsM_{-120.0f}, lufsS_{-120.0f}, lufsI_{-120.0f}, lraA_{0.0f};
    std::atomic<float> pkInHoldA_{-120.0f}, pkOutHoldA_{-120.0f}, maxGrHoldA_{0.0f}, tpHoldA_{-120.0f}, clipsA_{0.0f};
    std::atomic<float> relNowA_{120.0f}, deltaA_{0.0f}, srA_{44100.0f}, cpuA_{0.0f}, measuredA_{0.0f}, maxMA_{-120.0f}, maxSA_{-120.0f};
    std::atomic<bool> resetPeaksReq_{false}, resetLoudReq_{false};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
