// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in Nota Auto Shift (device kind 10) — a real-time vocal pitch-correction
// insert in the spirit of classic pitch-correction units. Three stages:
//   1. DETECT  — an NSDF pitch tracker (McLeod-style: normalized square-difference,
//                first peak above 90% of the global max) on the mono sum, run at a
//                control-rate hop, with sub-sample parabolic peak refinement. NSDF +
//                first-peak picking avoids the octave errors that plague plain
//                autocorrelation; the detected note is lightly smoothed.
//   2. SNAP    — the (smoothed) pitch is pulled toward the nearest note of the chosen
//                Key + Scale by Amount, glided in at the Speed, plus a manual Shift.
//   3. SHIFT   — a single-read-pointer granular pitch shifter that crossfades only when
//                the read pointer wraps, so it is transparent (no comb) at unity and
//                click-free across the whole correction range — no bypass discontinuity.
//
// The detected pitch is published two ways for the UI: an instantaneous smoothed value
// via gainReductionDb() (the needle) and a scrolling history ring via scopeRead() (the
// pitch ribbon). Header-only, allocation-free, JUCE-free. Params normalized 0..1 →
// persist / clone / automation flow generically through the base Device.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class AutoShift : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    // Range clamps the retune so detection octave-errors can't jump; Formant applies a
    // shift-linked spectral tilt (formant compensation). KeySource/Follow are UI mode
    // flags (the card drives Key/Scale for Auto / Follow-scale-device); the DSP just
    // snaps to the current Key + Scale. v2 blob appends 4 floats.
    enum { Key = 0, Scale, Amount, Speed, Shift, Mix, Range, Formant, KeySource, Follow, kNumParams };

    AutoShift() {
        p_[Key].store(0.0f);       // C
        p_[Scale].store(0.25f);    // Major (0..4 in .25 steps)
        p_[Amount].store(1.0f);    // full correction
        p_[Speed].store(0.15f);    // fairly fast retune
        p_[Shift].store(0.5f);     // no manual transpose
        p_[Mix].store(1.0f);
        p_[Range].store(0.3636f);  // ±5 st correction range (1 + v*11)
        p_[Formant].store(0.0f);   // no formant compensation
        p_[KeySource].store(0.5f); // Manual (0 Auto / 0.5 Manual / 1 MIDI)
        p_[Follow].store(0.0f);    // don't follow a scale device
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (int c = 0; c < 2; ++c) for (int i = 0; i < kBuf; ++i) ring_[c][i] = 0.0f;
        for (int i = 0; i < kBuf; ++i) det_[i] = 0.0f;
        for (int i = 0; i < kHist; ++i) { histDet_[i] = 0.0f; histCorr_[i] = 0.0f; }
        rw_ = 0; hop_ = 0; histW_ = 0;
        d_ = kFade + kGrain * 0.5; fadeD_ = 0.0; fadeCnt_ = 0;
        curRatio_ = 1.0; targetRatio_ = 1.0; smMidi_ = 60.0; havePitch_ = false;
        lpF_[0] = lpF_[1] = 0.0;
        lpCoef_ = 1.0 - std::exp(-2.0 * kPi * 900.0 / sr_);   // formant-tilt split ≈ 900 Hz
        minLag_ = std::max(2, (int)(sr_ / 1000.0));
        maxLag_ = std::min(kWin - 2, (int)(sr_ / 70.0));
    }

    // Instantaneous smoothed detected pitch (MIDI/127), 0 = unvoiced — the LIVE readout.
    float gainReductionDb() const override { return pitchPub_.load(std::memory_order_relaxed); }

    // Scrolling pitch history, oldest→newest, interleaved [detected/127, corrected/127]
    // (0 = unvoiced) — the two-trace pitch plot.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const int pairs = std::min(maxSamples / 2, kHist);
        const int w = histW_.load(std::memory_order_relaxed);
        const int start = (w - pairs) & (kHist - 1);
        for (int k = 0; k < pairs; ++k) {
            const int idx = (start + k) & (kHist - 1);
            out[2 * k]     = histDet_[idx];
            out[2 * k + 1] = histCorr_[idx];
        }
        return pairs * 2;
    }

    void process(float* buf, int32_t frames) override {
        const int   key    = std::clamp((int)std::lround(get(Key) * 11.0f), 0, 11);
        const uint16_t mask = kScaleMask[std::clamp((int)std::lround(get(Scale) * 4.0f), 0, 4)];
        const float amount  = std::clamp(get(Amount), 0.0f, 1.0f);
        const double shiftSt = ((double)get(Shift) - 0.5) * 24.0;   // ±12 st
        const float mix     = std::clamp(get(Mix), 0.0f, 1.0f);
        const double rangeSt = 1.0 + (double)std::clamp(get(Range), 0.0f, 1.0f) * 11.0;   // 1..12 st
        const float formant = std::clamp(get(Formant), 0.0f, 1.0f);
        const double tau    = expMap(get(Speed), 0.002, 0.30);      // retune glide time (s)
        const double smooth = 1.0 - std::exp(-1.0 / (tau * sr_));

        for (int32_t i = 0; i < frames; ++i) {
            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            det_[rw_] = 0.5f * (dryL + dryR);
            ring_[0][rw_] = dryL; ring_[1][rw_] = dryR;

            if (++hop_ >= kHop) { hop_ = 0; detect(key, mask, amount, shiftSt, rangeSt); }

            curRatio_ += (targetRatio_ - curRatio_) * smooth;   // glide
            grainStep(curRatio_);
            float wetL = grainOut(0), wetR = grainOut(1);

            // Formant compensation: a shift-linked spectral tilt. Shifting up brightens the
            // formants; boosting lows / cutting highs by the same octave count pushes them
            // back toward the original envelope (and the inverse when shifting down).
            if (formant > 0.001f) {
                const double tilt = std::clamp((double)formant * std::log2(std::max(1e-6, curRatio_)) * 0.6, -0.85, 0.85);
                lpF_[0] += lpCoef_ * (wetL - lpF_[0]); const float loL = (float)lpF_[0], hiL = wetL - loL;
                lpF_[1] += lpCoef_ * (wetR - lpF_[1]); const float loR = (float)lpF_[1], hiR = wetR - loR;
                wetL = (float)(loL * (1.0 + tilt) + hiL * (1.0 - tilt));
                wetR = (float)(loR * (1.0 + tilt) + hiR * (1.0 - tilt));
            }

            rw_ = (rw_ + 1) & (kBuf - 1);
            buf[i * 2]     = dryL * (1.0f - mix) + wetL * mix;
            buf[i * 2 + 1] = dryR * (1.0f - mix) + wetR * mix;
        }
    }

    const char* displayName() const override { return "Nota Auto Shift"; }
    int32_t     builtinKind() const override { return 10; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Key: return "Key"; case Scale: return "Scale"; case Amount: return "Amount";
            case Speed: return "Speed"; case Shift: return "Shift"; case Mix: return "Mix";
            case Range: return "Range"; case Formant: return "Formant"; case KeySource: return "Key Source";
            case Follow: return "Follow Scale"; default: return "";
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

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kHalfPi = 1.5707963267948966;
    static constexpr int kBuf = 4096;   // audio ring (power of two)
    static constexpr int kWin = 1024;   // analysis window
    static constexpr int kHop = 256;    // detection hop
    static constexpr int kGrain = 1024; // granular grain length
    static constexpr int kFade = 256;   // granular crossfade length
    static constexpr int kHist = 512;   // pitch-history ring (~3 s) for the UI plot
    // Scale masks (bit i set = semitone i above the key is in-scale):
    // Chromatic / Major / Minor / Pentatonic Major / Pentatonic Minor.
    static constexpr uint16_t kScaleMask[5] = { 0x0FFF, 0x0AB5, 0x05AD, 0x0295, 0x04A9 };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }

    // Nearest in-scale note to a continuous MIDI pitch (prefers the closer side).
    static double snapMidi(double m, int key, uint16_t mask) {
        const int base = (int)std::lround(m);
        for (int d = 0; d <= 6; ++d)
            for (int s = (d == 0 ? 0 : d), step = 0; step < (d == 0 ? 1 : 2); ++step, s = -d) {
                const int note = base + s;
                const int pc = ((note - key) % 12 + 12) % 12;
                if (mask & (uint16_t)(1u << pc)) return (double)note;
            }
        return m;
    }

    // NSDF pitch tracker over the most recent window; updates targetRatio_ + UI feeds.
    void detect(int key, uint16_t mask, float amount, double shiftSt, double rangeSt) {
        for (int i = 0; i < kWin; ++i) win_[i] = det_[(rw_ - kWin + i) & (kBuf - 1)];
        double pe[kWin + 1]; pe[0] = 0.0;
        for (int i = 0; i < kWin; ++i) pe[i + 1] = pe[i] + (double)win_[i] * win_[i];

        double globalMax = 0.0;
        for (int lag = minLag_; lag <= maxLag_; ++lag) {
            double r = 0.0;
            for (int i = 0; i < kWin - lag; ++i) r += (double)win_[i] * win_[i + lag];
            const double m = pe[kWin - lag] + (pe[kWin] - pe[lag]);   // Σx[i]² + Σx[i+lag]²
            const double nsdf = m > 1e-9 ? 2.0 * r / m : 0.0;
            nsdf_[lag] = (float)nsdf;
            if (nsdf > globalMax) globalMax = nsdf;
        }

        // First local maximum above 90% of the global peak → the true period (avoids
        // choosing a stronger sub-octave peak at 2× the lag).
        int bestLag = 0;
        if (globalMax > 0.0) {
            const double thr = 0.90 * globalMax;
            for (int lag = minLag_ + 1; lag < maxLag_; ++lag)
                if (nsdf_[lag] > thr && nsdf_[lag] >= nsdf_[lag - 1] && nsdf_[lag] >= nsdf_[lag + 1]) { bestLag = lag; break; }
        }

        if (globalMax > 0.5 && bestLag > 0) {
            double lagF = bestLag;
            const double nm1 = nsdf_[bestLag - 1], n0 = nsdf_[bestLag], np1 = nsdf_[bestLag + 1];
            const double denom = nm1 - 2 * n0 + np1;
            if (std::fabs(denom) > 1e-9) lagF += 0.5 * (nm1 - np1) / denom;
            const double f0 = sr_ / std::max(1.0, lagF);
            double detMidi = std::clamp(69.0 + 12.0 * std::log2(f0 / 440.0), 24.0, 108.0);
            if (!havePitch_) { smMidi_ = detMidi; havePitch_ = true; }
            else smMidi_ += (detMidi - smMidi_) * 0.5;              // light smoothing → less warble

            const double snapped = snapMidi(smMidi_, key, mask);
            double corr = amount * (snapped - smMidi_);
            corr = std::clamp(corr, -rangeSt, rangeSt);            // correction range guard
            targetRatio_ = std::pow(2.0, (corr + shiftSt) / 12.0);
            pushHist((float)(smMidi_ / 127.0), (float)((smMidi_ + corr) / 127.0));
        } else {
            targetRatio_ = std::pow(2.0, shiftSt / 12.0);           // unvoiced → manual shift only
            havePitch_ = false;
            pushHist(0.0f, 0.0f);
        }
    }

    void pushHist(float det, float corr) {
        pitchPub_.store(det, std::memory_order_relaxed);
        const int w = histW_.load(std::memory_order_relaxed);
        histDet_[w & (kHist - 1)] = det;
        histCorr_[w & (kHist - 1)] = corr;
        histW_.store((w + 1) & (kHist - 1), std::memory_order_relaxed);
    }

    // --- single-read-pointer granular pitch shifter -------------------------
    // d_ is the read delay behind the write head. It drifts at (1 - ratio) per sample;
    // when it leaves the [kFade, kFade+kGrain] band the pointer jumps by one grain and
    // an equal-power crossfade to the jumped position hides the seam. At ratio == 1 the
    // delay is constant → no jumps → bit-transparent (aside from a fixed latency).
    void grainStep(double ratio) {
        d_ += (1.0 - ratio);
        if (fadeCnt_ > 0) { fadeD_ += (1.0 - ratio); --fadeCnt_; }
        else if (d_ < (double)kFade)               { fadeD_ = d_; d_ += kGrain; fadeCnt_ = kFade; }
        else if (d_ > (double)(kFade + kGrain))    { fadeD_ = d_; d_ -= kGrain; fadeCnt_ = kFade; }
    }
    float grainOut(int c) {
        const float main = readFrac(c, d_);
        if (fadeCnt_ <= 0) return main;
        const double m = (double)(kFade - fadeCnt_) / (double)kFade;   // 0 → 1 (old → new)
        const float alt = readFrac(c, fadeD_);
        return alt * (float)std::cos(m * kHalfPi) + main * (float)std::sin(m * kHalfPi);
    }
    inline float readFrac(int c, double delay) const {
        if (delay < 1.0) delay = 1.0;
        double rp = (double)rw_ - delay;
        while (rp < 0.0) rp += kBuf;
        const int i0 = (int)rp & (kBuf - 1);
        const int i1 = (i0 + 1) & (kBuf - 1);
        const double fr = rp - std::floor(rp);
        return (float)(ring_[c][i0] * (1.0 - fr) + ring_[c][i1] * fr);
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float ring_[2][kBuf] = {};
    float det_[kBuf] = {};
    float win_[kWin] = {};
    float nsdf_[kWin] = {};
    float histDet_[kHist] = {};
    float histCorr_[kHist] = {};
    std::atomic<int> histW_{0};
    int rw_ = 0, hop_ = 0, minLag_ = 44, maxLag_ = 630;
    double d_ = 0.0, fadeD_ = 0.0; int fadeCnt_ = 0;
    double curRatio_ = 1.0, targetRatio_ = 1.0, smMidi_ = 60.0;
    bool havePitch_ = false;
    double lpF_[2] = {0.0, 0.0}, lpCoef_ = 0.1;   // formant-tilt one-pole state
    std::atomic<float> pitchPub_{0.0f};
};

} // namespace nota
