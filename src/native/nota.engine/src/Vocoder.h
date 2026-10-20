// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Vocoder (device kind 26, nota-daw/nota#113, specs/028-vocoder-effect) — a
// classic channel vocoder with two inputs:
//
//   modulator  this track's audio (the voice), summed to mono, trimmed by Mod gain
//   carrier    an internal band-limited saw at Pitch (Saw, the default: a robot voice straight
//              away), white noise (Noise), or another track (Track) through the device sidechain
//              (the engine hands us its previous block, pre-FX or post-FX per the tap); Tilt
//              shelves Saw / Track around 1 kHz before the bands
//
//   modulator → N analysis band-passes → envelope followers (Attack / Release, Hold freezes)
//   carrier   → N synthesis band-passes (centres × 2^(Formant/12)) × envelopes → wet. With a
//               Track carrier the envelopes are divided by their slow energy (an AGC), so the
//               result sits at that track's level whatever the voice's; with Saw / Noise they
//               are not, so the result follows the voice's level
//   wet += high-passed noise gated by the modulator's sibilance (Unvoiced)
//   out  = equal-power Mix of this track (dry) and wet, then Gain; a Track carrier with no
//          track picked passes this track through unchanged
//
// Bands: 8–32, log-spaced between Low and High; each is two cascaded TPT SVF band-pass
// sections (constant peak gain, 4th order) of Q. The wet sum passes a soft knee above −6 dBFS. Coefficients follow smoothed params every
// kCtrl samples, so Low / High / Q / Formant can be swept while playing. A Bands change ducks
// the wet path out and back in (5 ms each way) around a state reset. A Carrier change
// crossfades the two sources over 10 ms. Zero latency. In the browser's preset audition (no other
// tracks) the rig's setPreviewKey hands it its own phrase, which a Track carrier then uses.
//
// All params are normalized 0..1 and APPEND ONLY (contracts/device-params.md). Persistence /
// automation / clone flow generically through the base Device.
// Telemetry (scopeRead): key peak, output peaks, the band count, then per band the envelope,
// the carrier level (both 0..1 on a 54 dB scale) and the analysis centre in Hz.
// deviceText: 0 = status line.
// Header-only, allocation-free, JUCE-free.

#pragma once

#include "Device.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>

namespace nota {

class Vocoder : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Bands = 0, Low, High, Q, Formant, Attack, Release, ModGain, Carrier, Tilt, Pitch,
           Unvoiced, Mix, Gain, Hold, kNumParams };
    enum { CarrierSaw = 0, CarrierNoise = 1, CarrierTrack = 2 };

    static constexpr int kMaxBands = 32, kMinBands = 8;
    // Telemetry layout (scopeRead).
    enum { S_KeyPeak = 0, S_OutPeakL, S_OutPeakR, S_Bands, S_Env = 4, S_Carrier = S_Env + kMaxBands,
           S_Centre = S_Carrier + kMaxBands, kTele = S_Centre + kMaxBands };

    // Ranges (mirrored by the card, the presets and MCP).
    static constexpr double kLowLo = 20.0, kLowHi = 1000.0;          // Hz, exponential
    static constexpr double kHighLo = 2000.0, kHighHi = 20000.0;     // Hz, exponential
    static constexpr double kQLo = 1.0, kQHi = 10.0;
    static constexpr double kAttLo = 0.5, kAttHi = 100.0;            // ms, exponential
    static constexpr double kRelLo = 5.0, kRelHi = 1000.0;           // ms, exponential
    static constexpr double kModGainDb = 24.0, kTiltDb = 12.0, kGainDb = 12.0, kFormantSt = 12.0;
    static constexpr int    kPitchSteps = 84;                        // C0 … C7
    static constexpr double kC0 = 16.351597831287414;

    Vocoder() {
        p_[Bands].store(8.0f / 24.0f);       // 16
        p_[Low].store(0.3544f);              // 80 Hz
        p_[High].store(0.6021f);             // 8 kHz
        p_[Q].store(4.0f / 9.0f);            // 5.0
        p_[Formant].store(0.5f);             // 0 st
        p_[Attack].store(0.2616f);           // 2 ms
        p_[Release].store(0.3925f);          // 40 ms
        p_[ModGain].store(0.5f);             // 0 dB
        p_[Carrier].store(0.0f);             // Saw
        p_[Tilt].store(0.625f);              // +3 dB
        p_[Pitch].store(36.0f / kPitchSteps);// C3
        p_[Unvoiced].store(0.35f);
        p_[Mix].store(1.0f);
        p_[Gain].store(0.5f);                // 0 dB
        p_[Hold].store(0.0f);
        setSidechainTapPre(1);               // a carrier track is taken before its effects
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        reset();
    }

    // --- sidechain (the carrier track) ----------------------------------------------------
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }
    void    setPreviewKey(const float* interleaved, int32_t frames) override { pvBuf_ = interleaved; pvFrames_ = frames; }

    void process(float* buf, int32_t frames) override {
        const double sr = sr_;
        // The engine only refreshes the key buffer while a source is set; drop a stale one.
        const float* sc = (scTrackId_.load(std::memory_order_relaxed) >= 0 && scFrames_ >= frames) ? scBuf_ : nullptr;
        if (!sc && pvBuf_ && pvFrames_ >= frames) sc = pvBuf_;           // the preset audition: its own phrase
        scBuf_ = nullptr; pvBuf_ = nullptr;

        const double smP   = 1.0 - std::exp(-(double)kCtrl / (0.020 * sr));   // control-rate glide, ~20 ms
        const double smA   = 1.0 - std::exp(-1.0 / (0.010 * sr));             // per-sample glide, ~10 ms
        const double att   = std::exp(-1.0 / (expMap(get(Attack), kAttLo, kAttHi) * 0.001 * sr));
        const double rel   = std::exp(-1.0 / (expMap(get(Release), kRelLo, kRelHi) * 0.001 * sr));
        const double modG  = dbToLin((get(ModGain) - 0.5) * 2.0 * kModGainDb);
        const double scG   = dbToLin(sidechainGainDb());                       // the carrier track's trim
        const double tiltT = (get(Tilt) - 0.5) * 2.0 * kTiltDb;
        const double pitchT = std::log2(kC0) + std::round(get(Pitch) * kPitchSteps) / 12.0;
        const double uvT   = get(Unvoiced);
        const double mixT  = get(Mix);
        const double gainT = dbToLin((get(Gain) - 0.5) * 2.0 * kGainDb);
        const bool   hold  = get(Hold) >= 0.5f;
        const int    carT  = std::clamp((int)std::lround(get(Carrier) * 2.0f), 0, 2);
        const int    nT    = std::clamp(kMinBands + (int)std::lround(get(Bands) * 24.0f), kMinBands, kMaxBands);
        const double duckStep = 1.0 / (0.005 * sr);
        const double xfStep   = 1.0 / (0.010 * sr);
        const double uvAtt = std::exp(-1.0 / (0.001 * sr)), uvRel = std::exp(-1.0 / (0.030 * sr));
        const double lvRel = std::exp(-1.0 / (0.100 * sr));
        const double agcAtt = std::exp(-1.0 / (0.005 * sr)), agcRel = std::exp(-1.0 / (0.400 * sr));

        if (snap_) {                                         // first block: no glides from the defaults
            lowS_ = get(Low); highS_ = get(High); qS_ = get(Q); fmtS_ = get(Formant);
            tiltS_ = tiltT; pitchS_ = pitchT; uvS_ = uvT; mixS_ = mixT; gainS_ = gainT; modGS_ = modG;
            carCur_ = carPrev_ = carT; xf_ = 1.0; n_ = nT; duck_ = 1.0;
            passS_ = (carT == CarrierTrack && !sc) ? 1.0 : 0.0;
            computeBands(); snap_ = false;
        }
        if (carT != carCur_ && xf_ >= 1.0) { carPrev_ = carCur_; carCur_ = carT; xf_ = 0.0; }

        float keyPk = keyPk_, outPkL = outPkL_, outPkR = outPkR_;
        const float pkFall = (float)std::exp(-(double)frames / (0.300 * sr));
        keyPk *= pkFall; outPkL *= pkFall; outPkR *= pkFall;

        for (int32_t i = 0; i < frames; ++i) {
            if (--ctrl_ <= 0) {                               // control rate: params → band layout
                ctrl_ = kCtrl;
                lowS_  += (get(Low) - lowS_) * smP;
                highS_ += (get(High) - highS_) * smP;
                qS_    += (get(Q) - qS_) * smP;
                fmtS_  += (get(Formant) - fmtS_) * smP;
                if (nT != n_) {                               // Bands moved: duck out, reset, duck in
                    if (duck_ <= 0.0) { n_ = nT; clearBands(); }
                    else ducking_ = true;
                } else ducking_ = false;
                computeBands();
            }
            if (ducking_) duck_ = std::max(0.0, duck_ - duckStep);
            else if (duck_ < 1.0) duck_ = std::min(1.0, duck_ + duckStep);

            tiltS_  += (tiltT - tiltS_) * smA;
            pitchS_ += (pitchT - pitchS_) * smA;
            uvS_    += (uvT - uvS_) * smA;
            mixS_   += (mixT - mixS_) * smA;
            gainS_  += (gainT - gainS_) * smA;
            modGS_  += (modG - modGS_) * smA;
            if (xf_ < 1.0) xf_ = std::min(1.0, xf_ + xfStep);

            // Modulator: this track, mono.
            const double inL = buf[2 * i], inR = buf[2 * i + 1];
            const double m = 0.5 * (inL + inR) * modGS_;
            keyPk = std::max(keyPk, (float)std::fabs(m));

            // Carrier: crossfade between the previous and the current source.
            const double saw = sawTick(std::exp2(pitchS_));
            const double nzL = noise(), nzR = noise();
            const double tL = sc ? (double)sc[2 * i] * scG : 0.0, tR = sc ? (double)sc[2 * i + 1] * scG : 0.0;
            double wTrk = 0.0, wSaw = 0.0, wNz = 0.0;
            addWeight(carCur_, xf_, wTrk, wSaw, wNz);
            addWeight(carPrev_, 1.0 - xf_, wTrk, wSaw, wNz);
            double cL = wTrk * tL + wSaw * saw, cR = wTrk * tR + wSaw * saw;
            tiltTick(cL, cR, tiltS_);
            cL += wNz * nzL; cR += wNz * nzR;

            // Bands. A Track carrier weighs each band by its envelope over the voice's recent
            // energy (a slow AGC with a floor), so the result sits at that track's level whatever
            // the voice's level; Saw / Noise weigh by the envelope itself, so the robot follows the
            // voice's level. A pause in the voice closes the bands either way.
            const double wInt = wSaw + wNz;
            const double inv = wTrk / std::max(agc_, kAgcFloor) + wInt * kVoice;
            double wetL = 0.0, wetR = 0.0, total = 0.0;
            for (int b = 0; b < n_; ++b) {
                Band& bd = band_[b];
                const double a = bd.an.tick(bd.an2.tick(m, bd.ca), bd.ca);
                if (!hold) {
                    const double x = std::fabs(a);
                    const double c = x > bd.env ? att : rel;
                    bd.env = std::min(kEnvMax, x + (bd.env - x) * c);
                }
                total += bd.env * bd.env;
                if (!bd.live) continue;
                const double sL = bd.sl.tick(bd.sl2.tick(cL, bd.cs), bd.cs);
                const double sR = bd.sr.tick(bd.sr2.tick(cR, bd.cs), bd.cs);
                const double lx = std::fabs(sL);
                bd.lvl = lx > bd.lvl ? lx : lx + (bd.lvl - lx) * lvRel;
                const double w = std::min(kWeightMax, bd.env * inv);
                wetL += sL * w; wetR += sR * w;
            }
            total = std::sqrt(total);                 // the envelopes' energy, not their sum: the result keeps
            if (!hold) agc_ = total + (agc_ - total) * (total > agc_ ? agcAtt : agcRel);   // its level whether the voice is narrow or wide
            wetL *= norm_; wetR *= norm_;

            // Unvoiced: high-passed noise where the modulator is sibilant.
            const double hf = hpM_.tick(hpM2_.tick(m, uvHpA_), uvHpA_);
            const double hx = std::fabs(hf), fx = std::fabs(m);
            uvHf_  = hx + (uvHf_ - hx) * (hx > uvHf_ ? uvAtt : uvRel);
            uvAll_ = fx + (uvAll_ - fx) * (fx > uvAll_ ? uvAtt : uvRel);
            const double cx = 0.5 * (std::fabs(cL) + std::fabs(cR));        // the carrier's level gates it too:
            carEnv_ = cx + (carEnv_ - cx) * (cx > carEnv_ ? uvAtt : lvRel);   // a silent carrier stays silent
            if (uvS_ > 1e-4) {
                double r = uvAll_ > 1e-9 ? std::min(1.0, uvHf_ / uvAll_) : 0.0;
                r *= r;
                const double lvl = wTrk + wInt * std::min(2.0, agc_ * kVoice);                       // at the wet path's level
                const double g = uvS_ * r * std::min(1.0, uvAll_ / kUvFloor) * kUvGain * carEnv_ * lvl;
                wetL += g * hpNL_.tick(hpNL2_.tick(nzL, uvHpA_), uvHpA_);
                wetR += g * hpNR_.tick(hpNR2_.tick(nzR, uvHpA_), uvHpA_);
            }
            wetL = soft(wetL) * duck_; wetR = soft(wetR) * duck_;

            // Equal-power Mix of this track (dry) and the wet path, then Gain. A Track carrier with
            // no track picked passes this track unchanged (a 10 ms fade either way).
            const double passT = (carCur_ == CarrierTrack && !sc) ? 1.0 : 0.0;
            passS_ += (passT - passS_) * smA;
            if (std::fabs(passT - passS_) < 1e-5) passS_ = passT;
            const double dG = std::cos(mixS_ * 1.5707963267948966), wG = std::sin(mixS_ * 1.5707963267948966);
            const double vL = (dG * inL + wG * wetL) * gainS_, vR = (dG * inR + wG * wetR) * gainS_;
            const float oL = (float)(passS_ * inL + (1.0 - passS_) * vL);
            const float oR = (float)(passS_ * inR + (1.0 - passS_) * vR);
            buf[2 * i] = std::isfinite(oL) ? oL : 0.0f;
            buf[2 * i + 1] = std::isfinite(oR) ? oR : 0.0f;
            outPkL = std::max(outPkL, std::fabs(buf[2 * i]));
            outPkR = std::max(outPkR, std::fabs(buf[2 * i + 1]));
        }
        flushDenormals();

        keyPk_ = keyPk; outPkL_ = outPkL; outPkR_ = outPkR;
        tele_[S_KeyPeak].store(keyPk, std::memory_order_relaxed);
        tele_[S_OutPeakL].store(outPkL, std::memory_order_relaxed);
        tele_[S_OutPeakR].store(outPkR, std::memory_order_relaxed);
        tele_[S_Bands].store((float)n_, std::memory_order_relaxed);
        for (int b = 0; b < kMaxBands; ++b) {
            const bool on = b < n_;
            tele_[S_Env + b].store(on ? disp(band_[b].env) : 0.0f, std::memory_order_relaxed);
            tele_[S_Carrier + b].store(on && band_[b].live ? disp(band_[b].lvl) : 0.0f, std::memory_order_relaxed);
            tele_[S_Centre + b].store(on ? (float)band_[b].fa : 0.0f, std::memory_order_relaxed);
        }
    }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = tele_[k].load(std::memory_order_relaxed);
        return n;
    }

    std::string deviceText(int32_t id) const override {
        if (id != 0) return {};
        static const char* car[] = { "saw", "noise", "track" };
        char b[320];
        const int c = std::clamp((int)std::lround(get(Carrier) * 2.0f), 0, 2);
        std::snprintf(b, sizeof b, "%d bands %.0f-%.0f Hz - Q %.1f - formant %+d st - attack %.1f ms - release %.0f ms"
                      " - carrier %s - unvoiced %.0f %% - mix %.0f %%%s%s",
                      kMinBands + (int)std::lround(get(Bands) * 24.0f), lowHz(get(Low)), highHz(get(High)),
                      kQLo + get(Q) * (kQHi - kQLo), (int)std::lround((get(Formant) - 0.5) * 2.0 * kFormantSt),
                      expMap(get(Attack), kAttLo, kAttHi), expMap(get(Release), kRelLo, kRelHi), car[c],
                      get(Unvoiced) * 100.0, get(Mix) * 100.0, get(Hold) >= 0.5f ? " - hold" : "",
                      std::lround(get(Carrier) * 2.0f) == CarrierTrack && sidechainSourceTrackId() < 0 ? " - no carrier track (passes through)" : "");
        return b;
    }

    const char* displayName() const override { return "Nota Vocoder"; }
    int32_t     builtinKind() const override { return 26; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = { "Bands", "Low", "High", "Q", "Formant", "Attack", "Release",
            "Mod gain", "Carrier", "Tilt", "Pitch", "Unvoiced", "Mix", "Gain", "Hold" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    // Unit mappings, shared with the smoke test.
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    static double lowHz(double v)  { return expMap(v, kLowLo, kLowHi); }
    static double highHz(double v) { return expMap(v, kHighLo, kHighHi); }

private:
    static constexpr int    kCtrl = 32;
    static constexpr double kEnvMax = 1.0;      // a full-scale band: a loop through a return cannot run away
    static constexpr double kNorm = 7.0;        // wet make-up (calibrated in the smoke test: ≈ the carrier's level)
    static constexpr double kVoice = 6.5;       // Saw / Noise: envelope → weight (calibrated: a voice at −20 dBFS → ≈ the same out)
    static constexpr double kAgcFloor = 0.003;  // the AGC's floor: a band total this low counts as silence
    static constexpr double kWeightMax = 1.5;   // a band's weight cap while the AGC catches up with an onset
    static constexpr double kUvFloor = 0.003;   // modulator level under which Unvoiced stays shut
    static constexpr double kUvGain = 3.0;      // noise level against the carrier's

    // TPT state-variable band-pass, constant peak gain (Cytomic).
    struct Coef { double a1 = 0, a2 = 0, a3 = 0, k = 1; };
    struct Svf {
        double ic1 = 0, ic2 = 0;
        double tick(double x, const Coef& c) {
            const double v3 = x - ic2;
            const double v1 = c.a1 * ic1 + c.a2 * v3;
            const double v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
            ic1 = 2.0 * v1 - ic1; ic2 = 2.0 * v2 - ic2;
            return c.k * v1;
        }
        void clear() { ic1 = ic2 = 0; }
    };
    struct Band {
        Svf an, an2, sl, sl2, sr, sr2;
        Coef ca, cs;
        double fa = 1000.0, env = 0.0, lvl = 0.0;
        bool live = true;
    };
    // One-pole high-pass (two in series make the 12 dB/oct unvoiced filters).
    struct Hp1 {
        double z = 0, x1 = 0;
        double tick(double x, double a) { const double y = a * (z + x - x1); x1 = x; z = y; return y; }
        void clear() { z = x1 = 0; }
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double dbToLin(double db) { return std::pow(10.0, db / 20.0); }
    // Linear to ±0.5, then a tanh knee that never passes ±1 — a hot modulator cannot clip the wet path.
    static double soft(double x) {
        const double a = std::fabs(x);
        if (a <= 0.5) return x;
        return std::copysign(0.5 + 0.5 * std::tanh((a - 0.5) * 2.0), x);
    }
    static float disp(double v) {                    // 0..1 over −54 … 0 dB (rectified level)
        if (v <= 1e-9) return 0.0f;
        return (float)std::clamp((20.0 * std::log10(v) + 54.0) / 54.0, 0.0, 1.0);
    }
    static Coef bp(double f, double q, double sr) {
        Coef c;
        const double g = std::tan(3.141592653589793 * std::min(f, 0.49 * sr) / sr);
        c.k = 1.0 / q;
        c.a1 = 1.0 / (1.0 + g * (g + c.k));
        c.a2 = g * c.a1;
        c.a3 = g * c.a2;
        return c;
    }
    static void addWeight(int src, double w, double& trk, double& saw, double& nz) {
        if (src == CarrierTrack) trk += w; else if (src == CarrierNoise) nz += w; else saw += w;
    }

    void computeBands() {
        double lo = lowHz(lowS_), hi = highHz(highS_);
        lo = std::min(lo, hi * 0.5);                 // at least an octave
        const double q = kQLo + qS_ * (kQHi - kQLo);
        const double shift = std::exp2(std::round((fmtS_ - 0.5) * 2.0 * kFormantSt) / 12.0);
        const double nyq = 0.45 * sr_;
        for (int b = 0; b < n_; ++b) {
            Band& bd = band_[b];
            bd.fa = std::min(lo * std::pow(hi / lo, (b + 0.5) / n_), nyq);
            const double fs = bd.fa * shift;
            bd.live = fs <= nyq;
            bd.ca = bp(bd.fa, q, sr_);
            bd.cs = bp(std::min(fs, nyq), q, sr_);
        }
        norm_ = kNorm * std::sqrt(n_ / 16.0) * std::sqrt(q / 5.0);   // L2 weights: √N brings a spread voice back to level
    }
    void clearBands() {
        for (auto& bd : band_) { bd.an.clear(); bd.an2.clear(); bd.sl.clear(); bd.sl2.clear(); bd.sr.clear(); bd.sr2.clear(); bd.env = bd.lvl = 0.0; }
    }
    void reset() {
        clearBands();
        hpM_.clear(); hpM2_.clear(); hpNL_.clear(); hpNL2_.clear(); hpNR_.clear(); hpNR2_.clear();
        tiltZL_ = tiltZR_ = 0.0; sawPh_ = 0.0; uvHf_ = uvAll_ = carEnv_ = agc_ = 0.0;
        uvHpA_ = 1.0 / (1.0 + 2.0 * 3.141592653589793 * 4000.0 / sr_);
        tiltA_ = std::exp(-2.0 * 3.141592653589793 * 1000.0 / sr_);
        ctrl_ = 0; snap_ = true; ducking_ = false;
        keyPk_ = outPkL_ = outPkR_ = 0.0f;
    }

    double sawTick(double hz) {                      // polyBLEP saw
        const double dt = std::min(0.5, hz / sr_);
        double t = sawPh_;
        double y = 2.0 * t - 1.0;
        if (t < dt) { const double x = t / dt; y -= x + x - x * x - 1.0; }
        else if (t > 1.0 - dt) { const double x = (t - 1.0) / dt; y -= x * x + x + x + 1.0; }
        sawPh_ += dt; if (sawPh_ >= 1.0) sawPh_ -= 1.0;
        return 0.5 * y;
    }
    double noise() {                                 // xorshift32 → [−1, 1)
        rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5;
        return (double)(int32_t)rng_ * (1.0 / 2147483648.0);
    }
    void tiltTick(double& l, double& r, double db) {  // shelf pair around 1 kHz: lows −db/2, highs +db/2
        const double gh = std::pow(10.0, db / 40.0), gl = 1.0 / gh;
        tiltZL_ = l + (tiltZL_ - l) * tiltA_;
        tiltZR_ = r + (tiltZR_ - r) * tiltA_;
        l = tiltZL_ * gl + (l - tiltZL_) * gh;
        r = tiltZR_ * gl + (r - tiltZR_) * gh;
    }
    void flushDenormals() {
        auto f = [](double& v) { if (std::fabs(v) < 1e-20) v = 0.0; };
        for (auto& bd : band_)
            for (Svf* s : { &bd.an, &bd.an2, &bd.sl, &bd.sl2, &bd.sr, &bd.sr2 }) { f(s->ic1); f(s->ic2); }
        for (auto& bd : band_) { f(bd.env); f(bd.lvl); }
        f(uvHf_); f(uvAll_); f(carEnv_); f(agc_); f(tiltZL_); f(tiltZR_);
    }

    std::atomic<float> p_[kNumParams];
    std::atomic<float> tele_[kTele] = {};
    std::atomic<int32_t> scTrackId_{ -1 };
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    const float* pvBuf_ = nullptr;      // preview key (setPreviewKey), one block
    int32_t pvFrames_ = 0;

    double sr_ = 44100.0;
    std::array<Band, kMaxBands> band_{};
    int n_ = 16, ctrl_ = 0, carCur_ = 0, carPrev_ = 0;
    bool snap_ = true, ducking_ = false;
    double duck_ = 1.0, xf_ = 1.0, norm_ = 1.0, agc_ = 0.0, passS_ = 1.0;
    double lowS_ = 0, highS_ = 0, qS_ = 0, fmtS_ = 0.5, tiltS_ = 0, pitchS_ = 0, uvS_ = 0, mixS_ = 1, gainS_ = 1, modGS_ = 1;
    double carEnv_ = 0.0, sawPh_ = 0.0, tiltZL_ = 0.0, tiltZR_ = 0.0, tiltA_ = 0.0, uvHpA_ = 0.0, uvHf_ = 0.0, uvAll_ = 0.0;
    Hp1 hpM_, hpM2_, hpNL_, hpNL2_, hpNR_, hpNR2_;
    uint32_t rng_ = 0x9E3779B9u;
    float keyPk_ = 0.0f, outPkL_ = 0.0f, outPkR_ = 0.0f;
};

} // namespace nota
