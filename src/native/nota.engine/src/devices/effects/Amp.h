// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Valve (device kind 6, was "Amp") — a guitar-amp insert. A preamp
// waveshaper (tanh cascade, per-model drive + bias — or an even-harmonics-only stage)
// feeds a Bass / Middle (sweepable) / Treble / Presence tone stack with Bright and Deep
// switches, then a speaker cabinet (five cabs with their low resonance and presence
// peak; a Dynamic / Condenser / Ribbon mic at a distance, on or off axis, at the cap or
// the edge), low / high cuts, auto gain compensation, a noise gate keyed on the input,
// the master Output and Dry/Wet. Seven voicings from Clean to Heavy plus a Bass amp.
//
//   in ─▶ pre-HP ─▶ bright ─▶ [oversampled preamp] ─▶ tone stack ─▶ deep ─▶ cabinet
//      ─▶ low / high cut ─▶ makeup · auto-comp ─▶ output ─▶ gate ─▶ mix ─▶ out
//
// The preamp waveshaper runs on fast_math::tanhPrecise with a
// hoisted kTanh06; biquad coefficients and telemetry stay std::-exact. Silent
// stretches auto-sleep via isIdle(). Params unchanged (0..13 keep their raw units).

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/Oversampler.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <complex>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Amp : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum {
        Model = 0,
        Gain,
        Bass,
        Middle,
        Treble,
        Presence,
        Output,
        Mix,
        CabOn,
        CabType,
        Mic,
        Axis,
        Gate,
        Oversampling, // 0..1 → Off/2×/4×/8×
        MidFreq,      // middle peak 200·10^v Hz (0.512 = 650 Hz, the old fixed one)
        MicDistance,  // 1·30^v cm (0.473 = 5 cm = neutral proximity)
        MicPosition,  // 0 cap, 1 edge (darker)
        LowCut,       // 0 off, else 20·15^v Hz (12 dB/oct)
        HighCut,      // 1 off, else 2000·10^v Hz (12 dB/oct)
        EvenOnly,     // preamp adds even harmonics only
        AutoComp,     // output level follows the input's
        Bright,       // bright cap: treble lift before the preamp, strongest at low gain
        Deep,         // low resonance boost after the tone stack
        kNumParams
    };

    // Telemetry slots (scopeRead).
    enum {
        S_InPk = 0, // input peak (decaying), linear
        S_OutPk,    // output peak (decaying), linear
        S_Cpu,      // share of real time spent in process()
        S_SampleRate,
        S_Thd, // THD of the preamp at the test level, 0..1
        S_H1,  // harmonic levels 1..7, dB relative to the fundamental (H1 = 0)
        S_H7 = S_H1 + 6,
        S_TestLvl,  // the level the THD was measured at (the input peak, or −6 dBFS)
        S_CompDb,   // auto-comp gain now, dB
        S_GateGain, // gate gain now 0..1 (1 = open, or no gate)
        S_AliasDb,  // estimated aliasing of a 2.5 kHz tone at the test level, dB re the fundamental
        S_Drive,    // effective preamp drive
        S_Model,
        S_OsFactor,  // 1 / 2 / 4 / 8
        S_CabLossDb, // the cabinet now vs on axis at 4 kHz, dB
        S_MidHz,     // middle peak frequency
        S_CabLpHz,   // cabinet band-pass edges
        S_CabHpHz,
        S_GateThrDb, // gate threshold dBFS (−120 = off)
        S_RespLoHz,  // response grid
        S_RespHiHz,
        S_RespN,
        S_DistCm, // mic distance
        S_Reserved0,
        S_Reserved1,
        S_Reserved2,
        S_Reserved3,
        S_Reserved4,
        kTele = 32
    };
    static constexpr int kResp = 96;
    static constexpr double kRespLo = 30.0, kRespHi = 16000.0;
    static constexpr int kScopeTotal = kTele + 4 * kResp;
    enum { A_Reset = 0 }; // deviceAction ids

    Amp();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: short filter memory only (biquads / gate / DC blocker ring out
    // in milliseconds) — no audible tail. Settles after the hangover (see the .cpp).
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Telemetry block, then the four responses (see the header note). Writes as much of that
    // layout as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Valve"; }
    int32_t builtinKind() const override { return 6; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kModels = 7, kCabs = 5, kMics = 3;
    static constexpr int kHarm = 96, kDft = 256;
    static constexpr double kAliasHz = 2500.0;
    static constexpr float kMidFreqDefault = 0.5119f; // log10(650/200)
    static constexpr float kDistDefault = 0.4732f;    // ln 5 / ln 30
    static constexpr const char* kModelName[kModels] = {"Clean", "Boost", "Blues", "Rock", "Lead", "Heavy", "Bass"};
    static constexpr const char* kCabName[kCabs] = {"Match", "1x12 Open", "2x12 Combo", "4x12 Closed", "1x15 Bass"};
    static constexpr const char* kMicName[kMics] = {"dynamic", "condenser", "ribbon"};
    static constexpr const char* kOsName[4] = {"off", "2x", "4x", "8x"};
    // tanh(0.6): the even-harmonics generator's operating point, hoisted out of the
    // per-sample path (precomputing a constant is always safe).
    static constexpr float kTanh06 = 0.53704957f;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    static constexpr uint64_t kMaxFlight = 512;     // oversampler transient + filter ring

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static std::string hzText(double hz);
    static double respHz(int k) { return kRespLo * std::pow(kRespHi / kRespLo, (double)k / (kResp - 1)); }

    void resetState();

    // Per-voicing character: preamp drive, clipping asymmetry + stage count, the
    // pre-distortion low-cut (tightness) and the speaker-cabinet band edges.
    struct AmpModel {
        float driveMul;
        float bias;
        int stages;
        float preHpHz;
        float cabHpHz;
        float cabLpHz;
    };
    static constexpr AmpModel kModel[kModels] = {
        /* Clean */ {1.5f, 0.00f, 1, 20.0f, 70.0f, 7000.0f},
        /* Boost */ {3.0f, 0.05f, 1, 30.0f, 75.0f, 6500.0f},
        /* Blues */ {6.0f, 0.10f, 1, 45.0f, 80.0f, 6000.0f},
        /* Rock  */ {12.0f, 0.15f, 2, 70.0f, 85.0f, 5200.0f},
        /* Lead  */ {22.0f, 0.20f, 2, 95.0f, 90.0f, 5000.0f},
        /* Heavy */ {40.0f, 0.28f, 3, 120.0f, 95.0f, 4500.0f},
        /* Bass  */ {4.0f, 0.00f, 1, 20.0f, 40.0f, 3800.0f},
    };
    // Cabinet / mic voicing: multipliers on the model's band-pass edges, plus each cab's low
    // resonance and presence peak. Index 0 = the model-matched cab (no peaks — the old sound).
    //                                        Match   1×12   2×12   4×12   1×15
    static constexpr float kCabLp[kCabs] = {1.00f, 0.90f, 1.00f, 1.15f, 0.72f};
    static constexpr float kCabHp[kCabs] = {1.00f, 1.10f, 1.00f, 0.90f, 0.70f};
    static constexpr float kCabResHz[kCabs] = {100.f, 125.f, 105.f, 90.f, 70.f};
    static constexpr float kCabResDb[kCabs] = {0.0f, 2.0f, 3.0f, 4.5f, 4.0f};
    static constexpr float kCabPkHz[kCabs] = {2500.f, 2200.f, 2600.f, 2900.f, 1800.f};
    static constexpr float kCabPkDb[kCabs] = {0.0f, 3.0f, 3.0f, 4.0f, 2.0f};
    //                                        Dyn    Cond   Ribbon
    static constexpr float kMicLp[kMics] = {1.00f, 1.28f, 0.80f};
    static constexpr float kMicProx[kMics] = {
        2.0f, 0.8f, 3.0f}; // dB of low shelf per halving of the distance under 5 cm

    // A snapshot of the params in musical terms (process() and the message thread alike).
    struct Params {
        int model, cab, mic, os;
        float g01, bass, middle, treble, presence, mix, axis, gate;
        double midHz, distCm, lowCutHz, highCutHz;
        bool cabOn, edge, even, autoComp, bright, deep;
    };
    Params params() const;

    // The memoryless preamp: the model's tanh cascade, or the even-harmonics-only stage.
    struct Shaper {
        float drive = 1.0f, bias = 0.0f, biasDC = 0.0f, makeup = 1.0f, evenAmt = 0.0f;
        int stages = 1;
        bool even = false;
        float eval(float x) const;
    };
    static Shaper shaperFor(const Params& P);

    // Every linear filter of the chain, from the params (process() and the responses alike).
    struct Chain {
        struct C {
            float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
        };
        C preHP, bright, bass, mid, treb, pres, deep;
        C cabHP, cabLP, cabRes, cabPk, prox, distSh, edge, lowCut, highCut;
        bool cabOn = true;
        double cabLpHz = 0, cabHpHz = 0;
    };
    static Chain chainFor(const Params& P, double sr);
    static double magDb(const Chain::C& c, double sr, double hz);
    static double toneDb(const Chain& c, double sr, double hz);
    static double cabDb(const Chain& c, double sr, double hz);

    // Harmonic amplitudes 1..kHarm of the preamp driven by a sine of amplitude `a` (DFT, kDft pts).
    static void harmonics(const Shaper& sh, float a, double* h);

    // RBJ biquad (transposed direct form II). Coeffs computed off-thread of state.
    struct Biquad {
        using Coef = Chain::C;
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
        void reset() { z1 = z2 = 0; }
        void set(const Coef& c) {
            b0 = c.b0;
            b1 = c.b1;
            b2 = c.b2;
            a1 = c.a1;
            a2 = c.a2;
        }
        float process(float x);
        static Coef norm(double b0, double b1, double b2, double a0, double a1, double a2);
        static double w0(double sr, double f);
        static Coef lowpass(double sr, double f, double q);
        static Coef highpass(double sr, double f, double q);
        static Coef peak(double sr, double f, double dB, double q);
        static Coef lowShelf(double sr, double f, double dB);
        static Coef highShelf(double sr, double f, double dB);
    };

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Biquad preHP_[2], bright_[2], bassSh_[2], midPk_[2], trebSh_[2], presSh_[2], deep_[2];
    Biquad cabHP_[2], cabLP_[2], cabRes_[2], cabPk_[2], prox_[2], distSh_[2], edge_[2], lowCut_[2], highCut_[2];
    float gateEnv_[2] = {0, 0}, gateGain_[2] = {1, 1};
    float dcX_[2] = {0, 0}, dcY_[2] = {0, 0};
    double inMs_ = 0.0, wetMs_ = 0.0;
    float compG_ = 1.0f;
    std::atomic<bool> resetReq_{false};

    // Telemetry.
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{0}, outPkA_{0}, cpuA_{0}, srA_{44100.0f}, compA_{1}, gateA_{1};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak wet push (blocks sleep while the filters ring)

    dsp::Oversampler os_; // preamp-waveshaper anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dry_; // interleaved dry stash (sized in setSampleRate)
};

} // namespace nota
