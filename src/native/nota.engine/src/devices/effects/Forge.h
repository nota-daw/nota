// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Forge (device kind 17) — a multi-stage saturation effect ("Nota Forge" mockup,
// 700 × 260). Three saturation stages are all on screen, each with its own algorithm, drive,
// output trim, self-feedback and — since the redesign — its own shape: Bias (asymmetry), Tone
// (a ±12 dB tilt after the stage) and Width (the stage's stereo image, 0..200 %). Four routing
// topologies decide how the stages combine:
//   Serial    1 → 2 → 3;
//   Parallel  all three from the same input, averaged;
//   Mid/Side  stage 1 drives the Mid, stage 2 the Side (its Width scales the side), stage 3 the
//             recombined stereo (M+S);
//   Multiband stage 1 the lows (< 180 Hz), stage 2 the mids, stage 3 the highs (> 2.4 kHz) —
//             a stage that is off lets its band through clean.
// A global Amount drives into the chain; Wet / Output finish it. The original global Bias /
// Tone / Width stay as master offsets (Bias adds to every stage's bias, Tone is the post tilt
// the Env → Tone modulation moves, Width the post image), so older projects sound the same.
// An LFO → Drive (free Hz or tempo-synced) and an envelope → Tone keep the curve moving.
//
// All params are normalized 0..1 and APPEND ONLY: 0..26 are the original layout, the per-stage
// Bias / Tone / Width (27..35) are appended and default neutral. Persistence / automation /
// clone flow generically through the base Device.
// Telemetry (scopeRead): kTele live values, then the harmonic spectra (chain + each stage) of a
// −6 dB test sine through the static chain, then the transfer curves (chain + each stage,
// static and LFO-modulated) — computed from the same shapers process() runs, so the card and
// MCP draw exactly what is heard.
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO and reset the meters.
// Control-rate and modulation-steering math
// (LFO, envelopes, coefficients) stays std::-exact, the shaper tanh uses tanhPrecise
// (downstream per-sample saturation only, never steering); silent stretches auto-sleep
// via isIdle(). Params unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/Oversampler.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Forge : public Device {
  public:
    static constexpr int kStages = 3;
    static constexpr int kAlgos = 6; // 0 Tube, 1 Diode, 2 Tape, 3 Fuzz, 4 Digital, 5 Fold

    enum {
        Amount = 0, // master input drive (0..1 → 0..+30 dB into the stages)
        Tone,       // master post tilt (bipolar, 0.5 = flat; + brighter / − darker); Env → Tone moves it
        Wet,        // dry/wet mix (0..1)
        Output,     // master output gain (0.5 = 0 dB, ±24 dB)
        Bias,       // master asymmetry, added to every stage's bias (bipolar, 0.5 = none)
        Width,      // master stereo width of the wet signal (0..1 → 0..200 %)
        Routing,    // 0 Serial, 1 Parallel, 2 Mid/Side, 3 Multiband
        LfoDrive,   // LFO → stage drive depth (0..1)
        EnvTone,    // envelope → tone depth (0..1)
        LfoRate,    // LFO rate (exp 0.05..20 Hz free, or one of 8 synced divisions)
        LfoSync,    // 0 = free (Hz), 1 = tempo-synced
        // per-stage block of 5 (Type, Drive, Out, FB, On), ×3
        S1Type,
        S1Drive,
        S1Out,
        S1FB,
        S1On,
        S2Type,
        S2Drive,
        S2Out,
        S2FB,
        S2On,
        S3Type,
        S3Drive,
        S3Out,
        S3FB,
        S3On,
        Oversampling, // 0..1 → Off / 2× / 4× / 8× (appended: keeps saved indices stable)
        // per-stage shape block of 3 (Bias, Tone, Width), ×3 — appended by the redesign
        S1Bias,
        S1Tone,
        S1Width,
        S2Bias,
        S2Tone,
        S2Width,
        S3Bias,
        S3Tone,
        S3Width,
        kNumParams
    };
    static constexpr int kLegacyParams = Oversampling + 1; // 27: the layout before the per-stage shape
    static constexpr double kLfoDiv[8] = {8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625}; // beats per cycle
    static constexpr const char* kDivNames[8] = {"2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64"};
    static constexpr const char* kAlgoNames[kAlgos] = {"Tube", "Diode", "Tape", "Fuzz", "Digital", "Fold"};
    static constexpr const char* kRouteNames[4] = {"serial", "parallel", "mid/side", "multiband"};
    static constexpr double kXoLo = 180.0, kXoHi = 2400.0; // multiband crossovers (Hz)

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak, dBFS (falls 20 dB / 300 ms)
        S_OutPeak,    // output peak, dBFS
        S_InRms,      // input RMS (300 ms), dBFS
        S_OutRms,     // output RMS, dBFS
        S_SampleRate,
        S_Cpu,      // share of real time spent in process()
        S_Latency,  // samples
        S_OsFactor, // 1 / 2 / 4 / 8
        S_Lfo,      // live LFO value 0..1
        S_DriveMod, // the drive offset the LFO applies now (±0.3 × depth)
        S_Env,      // envelope follower 0..1
        S_ToneNow,  // the master tilt in effect now, incl. Env → Tone (−1..+1.5)
        S_InLevel,  // input peak (linear, fast fall) — where the signal sits on the curve
        S_ThdChain, // THD % of the −6 dB test sine through the whole device (chain)
        S_ThdS1,
        S_ThdS2,
        S_ThdS3,       // … through stage n alone (with Amount / Wet / Output)
        S_FlavorChain, // 0 clean, 1 odd-heavy, 2 even-heavy, 3 mixed
        S_FlavorS1,
        S_FlavorS2,
        S_FlavorS3,
        S_Signal,   // 1 when the input is above −70 dBFS
        S_Alias,    // 1 when Digital / Fold is on without oversampling (aliasing warning)
        S_Routing,  // 0..3
        S_OnCount,  // stages on
        S_LfoHz,    // the LFO rate in Hz now (synced: from the tempo)
        S_Bpm,      // tempo the synced LFO follows (0 = unknown)
        S_AmountDb, // Amount in dB
        S_OutputDb, // Output in dB
        kTele = 32
    };
    static constexpr int kHarm = 8;     // harmonics 2..9, dB relative to the fundamental
    static constexpr int kHarmSets = 4; // chain, stage 1, stage 2, stage 3
    static constexpr int kHarmOff = kTele;
    static constexpr int kPts = 129;  // transfer-curve points over input −1..+1
    static constexpr int kCurves = 8; // chain, chain mod, s1, s1 mod, s2, s2 mod, s3, s3 mod
    static constexpr int kCurveOff = kHarmOff + kHarm * kHarmSets;
    static constexpr int kScope = kCurveOff + kPts * kCurves;

    Forge();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransport(double beatStart, double spb, bool playing) override;

    // Live LFO value (0..1) on the generic per-device live-scalar channel (kept for older readers).
    float gainReductionDb() const override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: short filter/self-feedback memory only — no audible tail
    // (the oversampler reports zero latency). Settles after the hangover.
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Forge"; }
    int32_t builtinKind() const override { return 17; }
    int32_t latencySamples() const override { return os_.latencySamples(); }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Static saturation transfer (kept in sync with process()). x is the pre-gained input,
    // bias the combined bias −1..+1.
    static float shape(int type, float x, float bias);
    static float driveGain(float driveN) { return 1.0f + driveN * driveN * 20.0f; }

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    // Longest memory past the input: HIIR oversampling ringing plus the contractive
    // self-feedback / one-pole states (all sub-millisecond); 1024 is generous.
    static constexpr uint64_t kMaxFlight = 1024;

    struct Cfg {
        int type[kStages] = {};
        bool on[kStages] = {};
        float drive[kStages] = {}, trim[kStages] = {}, fb[kStages] = {}, bias[kStages] = {};
        float toneDb[kStages] = {}, lowG[kStages] = {}, highG[kStages] = {}, width[kStages] = {};
        float amt = 1, wet = 1, outGain = 1;
        int routing = 0;
    };
    struct Core {
        float tiltCoef = 0, loCoef = 0, hiCoef = 0;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float dbMs(float ms) { return ms > 1e-12f ? 10.0f * std::log10(ms) : -120.0f; }
    int divIndex() const { return std::clamp((int)std::lround(get(LfoRate) * 7.0f), 0, 7); }
    int osIndex() const { return std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3); }
    void setStage(int s, float type, float drive, float out, float fb, float on);
    void clearState();

    Cfg cfg() const;
    static int onCount(const Cfg& c) {
        int n = 0;
        for (int s = 0; s < kStages; ++s) n += c.on[s] ? 1 : 0;
        return n;
    }
    bool aliasing(const Cfg& c) const;
    static const char* roleWord(int routing, int s);
    std::string rateText() const;

    // ---- DSP ---------------------------------------------------------------------------------
    // One stage's shaper on one channel, with self-feedback and the stage tilt. `slot` = stage*2+ch.
    float shapeCh(const Cfg& c, const Core& k, int s, int slot, float driveMod, float x);
    // A stage on a stereo pair: both channels, then the stage's width.
    void stagePair(const Cfg& c, const Core& k, int s, float driveMod, float& l, float& r);
    void route(const Cfg& c, const Core& k, float driveMod, float& l, float& r);
    void split(int ch, float x, const Core& k, float* band);

    // ---- static model (UI / MCP) -------------------------------------------------------------
    // A stage's static (DC) response to the pre-gained input v: the feedback settles on a fixed
    // point (y = f((v + fb·y)·g)), found by a few damped iterations. Tilt / width are frequency /
    // stereo effects and don't bend the curve.
    static float stageStaticY(const Cfg& c, int s, float v, float mod);
    static float finish(const Cfg& c, float x, float y);
    // The whole device on a mono input x (−1..1). Mid/Side: the mono signal is all Mid (S1 then
    // S3); Multiband: a broadband sample — each band's shaper sees it, so the stages average.
    static float chainStatic(const Cfg& c, float x, float mod);
    static float stageFull(const Cfg& c, int s, float x, float mod);

    // Harmonics 2..9 of a −6 dB sine through fn, in dB relative to the fundamental, leaned by a
    // tilt (half the tilt per octave above the fundamental). Returns THD in %.
    template <class Fn> static float harmonics(Fn&& fn, float tiltDb, float* outDb, float& odd, float& even) {
        constexpr int M = 64;
        float ys[M];
        for (int i = 0; i < M; ++i) ys[i] = fn(0.5f * (float)std::sin(2.0 * kPi * i / M));
        float h[kHarm + 1] = {};
        for (int kk = 1; kk <= kHarm + 1; ++kk) {
            double re = 0, im = 0;
            for (int i = 0; i < M; ++i) {
                const double a = 2.0 * kPi * kk * i / M;
                re += ys[i] * std::cos(a);
                im += ys[i] * std::sin(a);
            }
            double v = 2.0 * std::sqrt(re * re + im * im) / M;
            if (kk > 1) v *= std::pow(10.0, tiltDb * std::log2((double)kk) * 0.5 / 20.0);
            h[kk - 1] = (float)v;
        }
        const float h1 = std::max(h[0], 1e-9f);
        odd = even = 0;
        for (int kk = 2; kk <= kHarm + 1; ++kk) {
            const float v = h[kk - 1];
            (kk % 2 ? odd : even) += v * v;
            outDb[kk - 2] = std::max(-120.0f, 20.0f * std::log10(v / h1 + 1e-9f));
        }
        return std::sqrt(odd + even) / h1 * 100.0f;
    }
    static int flavor(float thd, float odd, float even);

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float fb_[kStages * 2] = {};        // per stage/channel self-feedback state
    float tilt_[kStages * 2] = {};      // per stage/channel tilt one-pole
    float lpTone_[2] = {};              // master tilt one-pole
    float mbLo_[2] = {}, mbHi_[2] = {}; // multiband crossover one-poles
    float env_ = 0;
    double lfoPhase_ = 0;
    double beatStart_ = 0, spb_ = 0;
    bool playing_ = false;
    float inPkM_ = 0, outPkM_ = 0, msIn_ = 0, msOut_ = 0, inLvl_ = 0;
    double cpuS_ = 0.0;
    std::atomic<float> lfoLive_{0.5f}, driveModLive_{0.0f}, toneLive_{0.0f}, envA_{0.0f}, inLvlA_{0.0f};
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, inRmsA_{-120.0f}, outRmsA_{-120.0f}, cpuA_{0.0f},
        srA_{44100.0f};
    std::atomic<bool> resetReq_{false};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak saturated-path push (blocks sleep while states ring)

    dsp::Oversampler os_; // nonlinear-stage anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dryL_, dryR_, driveMod_, tone_; // base-rate stashes (sized in setSampleRate)
};

} // namespace nota
