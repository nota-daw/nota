// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Vocoder (device kind 26) — a classic channel vocoder with two inputs:
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
// All params are normalized 0..1 and APPEND ONLY. Persistence /
// automation / clone flow generically through the base Device.
// Telemetry (scopeRead): key peak, output peaks, the band count, then per band the envelope,
// the carrier level (both 0..1 on a 54 dB scale) and the analysis centre in Hz.
// deviceText: 0 = status line.

#pragma once

#include "devices/Device.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <string>

namespace nota {

class Vocoder : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum {
        Bands = 0,
        Low,
        High,
        Q,
        Formant,
        Attack,
        Release,
        ModGain,
        Carrier,
        Tilt,
        Pitch,
        Unvoiced,
        Mix,
        Gain,
        Hold,
        kNumParams
    };
    enum { CarrierSaw = 0, CarrierNoise = 1, CarrierTrack = 2 };

    static constexpr int kMaxBands = 32, kMinBands = 8;
    // Telemetry layout (scopeRead).
    enum {
        S_KeyPeak = 0,
        S_OutPeakL,
        S_OutPeakR,
        S_Bands,
        S_Env = 4,
        S_Carrier = S_Env + kMaxBands,
        S_Centre = S_Carrier + kMaxBands,
        kTele = S_Centre + kMaxBands
    };

    // Ranges (mirrored by the card, the presets and MCP).
    static constexpr double kLowLo = 20.0, kLowHi = 1000.0;      // Hz, exponential
    static constexpr double kHighLo = 2000.0, kHighHi = 20000.0; // Hz, exponential
    static constexpr double kQLo = 1.0, kQHi = 10.0;
    static constexpr double kAttLo = 0.5, kAttHi = 100.0;  // ms, exponential
    static constexpr double kRelLo = 5.0, kRelHi = 1000.0; // ms, exponential
    static constexpr double kModGainDb = 24.0, kTiltDb = 12.0, kGainDb = 12.0, kFormantSt = 12.0;
    static constexpr int kPitchSteps = 84; // C0 … C7
    static constexpr double kC0 = 16.351597831287414;

    Vocoder();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // --- sidechain (the carrier track) ----------------------------------------------------
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }
    void setPreviewKey(const float* interleaved, int32_t frames) override {
        pvBuf_ = interleaved;
        pvFrames_ = frames;
    }

    void process(float* buf, int32_t frames) override;
    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    std::string deviceText(int32_t id) const override;
    const char* displayName() const override { return "Nota Vocoder"; }
    int32_t builtinKind() const override { return 26; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // Unit mappings, shared with the smoke test.
    static double expMap(double v, double lo, double hi);
    static double lowHz(double v) { return expMap(v, kLowLo, kLowHi); }
    static double highHz(double v) { return expMap(v, kHighLo, kHighHi); }

  private:
    static constexpr int kCtrl = 32;
    static constexpr double kEnvMax = 1.0; // a full-scale band: a loop through a return cannot run away
    static constexpr double kNorm = 7.0;   // wet make-up (calibrated in the smoke test: ≈ the carrier's level)
    static constexpr double kVoice =
        6.5; // Saw / Noise: envelope → weight (calibrated: a voice at −20 dBFS → ≈ the same out)
    static constexpr double kAgcFloor = 0.003; // the AGC's floor: a band total this low counts as silence
    static constexpr double kWeightMax = 1.5;  // a band's weight cap while the AGC catches up with an onset
    static constexpr double kUvFloor = 0.003;  // modulator level under which Unvoiced stays shut
    static constexpr double kUvGain = 3.0;     // noise level against the carrier's

    // TPT state-variable band-pass, constant peak gain (Cytomic).
    struct Coef {
        double a1 = 0, a2 = 0, a3 = 0, k = 1;
    };
    struct Svf {
        double ic1 = 0, ic2 = 0;
        double tick(double x, const Coef& c) {
            const double v3 = x - ic2;
            const double v1 = c.a1 * ic1 + c.a2 * v3;
            const double v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
            ic1 = 2.0 * v1 - ic1;
            ic2 = 2.0 * v2 - ic2;
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
        double tick(double x, double a) {
            const double y = a * (z + x - x1);
            x1 = x;
            z = y;
            return y;
        }
        void clear() { z = x1 = 0; }
    };

    float get(int i) const;
    static double dbToLin(double db);
    // Linear to ±0.5, then a tanh knee that never passes ±1 — a hot modulator cannot clip the wet path.
    static double soft(double x);
    static float disp(double v);
    static Coef bp(double f, double q, double sr);
    static void addWeight(int src, double w, double& trk, double& saw, double& nz);

    void computeBands();
    void clearBands();
    void reset();
    double sawTick(double hz);
    double noise();
    void tiltTick(double& l, double& r, double db);
    void flushDenormals();

    std::atomic<float> p_[kNumParams];
    std::atomic<float> tele_[kTele] = {};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    const float* pvBuf_ = nullptr; // preview key (setPreviewKey), one block
    int32_t pvFrames_ = 0;

    double sr_ = 44100.0;
    std::array<Band, kMaxBands> band_{};
    int n_ = 16, ctrl_ = 0, carCur_ = 0, carPrev_ = 0;
    bool snap_ = true, ducking_ = false;
    double duck_ = 1.0, xf_ = 1.0, norm_ = 1.0, agc_ = 0.0, passS_ = 1.0;
    double lowS_ = 0, highS_ = 0, qS_ = 0, fmtS_ = 0.5, tiltS_ = 0, pitchS_ = 0, uvS_ = 0, mixS_ = 1, gainS_ = 1,
           modGS_ = 1;
    double carEnv_ = 0.0, sawPh_ = 0.0, tiltZL_ = 0.0, tiltZR_ = 0.0, tiltA_ = 0.0, uvHpA_ = 0.0, uvHf_ = 0.0,
           uvAll_ = 0.0;
    Hp1 hpM_, hpM2_, hpNL_, hpNL2_, hpNR_, hpNR2_;
    uint32_t rng_ = 0x9E3779B9u;
    float keyPk_ = 0.0f, outPkL_ = 0.0f, outPkR_ = 0.0f;
};

} // namespace nota
