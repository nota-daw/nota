// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chamber (device kind 20) — a hybrid reverb: a convolution engine and
// an algorithmic engine side by side, crossfaded by Blend (in parallel, or
// with the convolution feeding the algorithm in Serial). Split per
// IR synthesis lives in ChamberIrSynth.h; the zero-latency partitioned
// convolution kernel (ConvKernel) is TU-local to Chamber.cpp; the FDN tank
// uses the shared dsp::hadamard8 (identical 1/sqrt(8) unitary diffusion).
// FastMath only downstream (ducking gain, soft-clip knee, shimmer windows);
// delay reads, loop gains and control rate stay std::-exact.

#pragma once

#include "media/AudioFile.h"
#include "ChamberIrSynth.h"
#include "devices/Device.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace nota {
namespace chamber {

// Zero-latency partitioned convolution kernel. Fully defined in Chamber.cpp;
// the header only holds pointers to it.
class ConvKernel;

struct DLine {
    std::vector<float> b;
    int mask = 0, w = 0;
    void init(int maxLen) {
        int sz = 16;
        while (sz < maxLen + 8) sz <<= 1;
        b.assign(static_cast<size_t>(sz), 0.0f);
        mask = sz - 1;
        w = 0;
    }
    void clear() {
        std::fill(b.begin(), b.end(), 0.0f);
        w = 0;
    }
    void push(float x) {
        b[static_cast<size_t>(w)] = x;
        w = (w + 1) & mask;
    }
    float tap(int d) const { return b[static_cast<size_t>((w - 1 - d) & mask)]; } // d = 0 → newest
    float lin(float d) const {
        const int i = static_cast<int>(d);
        const float f = d - static_cast<float>(i);
        const float a = tap(i);
        return a + f * (tap(i + 1) - a);
    }
    float cubic(float d) const {
        const int i = static_cast<int>(d);
        const float f = d - static_cast<float>(i);
        const float xm1 = tap(i - 1), x0 = tap(i), x1 = tap(i + 1), x2 = tap(i + 2);
        const float c1 = 0.5f * (x1 - xm1), c2 = xm1 - 2.5f * x0 + 2.0f * x1 - 0.5f * x2,
                    c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        return ((c3 * f + c2) * f + c1) * f + x0;
    }
};

// Two crossfaded read heads sweeping a 60 ms window (sin² windows sum to one).
struct PitchShift {
    DLine d;
    float phase = 0.0f;
    int W = 2048;
    void init(double sr) {
        W = std::max(64, static_cast<int>(0.06 * sr));
        d.init(W + 8);
        phase = 0.0f;
    }
    void clear() {
        d.clear();
        phase = 0.0f;
    }
    float process(float x, float ratio);
};

class Algo {
  public:
    static constexpr int N = 8;
    struct Params {
        int mode = 0;
        float decay = 3.0f, size = 1.0f, diffuse = 0.7f, damp = 4500.0f, lowMul = 1.0f, highMul = 0.5f;
        float rate = 0.4f, depth = 0.3f, shimAmt = 0.0f, shimRatio = 2.0f;
        bool freeze = false, vintage = false, cubic = false, shimFb = true;
    };

    void prepare(double sr);
    void reset();
    // A clean start (delays re-primed at their targets) — for the offline preview render.
    void restart() {
        primed_ = false;
        lfoCos_ = 1.0f;
        lfoSin_ = 0.0f;
        reset();
    }
    // Tail length the engine is set to (mid band, seconds) — for the UI readout.
    static float modeScale(int mode) { return mode == 1 ? 0.3f : mode == 2 ? 1.6f : mode == 3 ? 1.1f : 1.0f; }
    void process(const float* inL, const float* inR, float* outL, float* outR, int n, const Params& p);

  private:
    struct Mode {
        float dly[N];
        float diff, ap, er, hiCut, gain;
    };
    static const Mode& mode(int m);
    static constexpr float kApMs[N] = {3.1f, 4.7f, 6.3f, 7.9f, 3.9f, 5.5f, 7.1f, 8.7f};

    double sr_ = 48000.0;
    bool primed_ = false;
    DLine line_[N], diff_[4][N], ap_[N];
    PitchShift shL_, shR_;
    float dCur_[N] = {}, ddCur_[4][N] = {}, frac_[4][N] = {}, flip_[4][N] = {};
    float bl1_[N] = {}, bl2_[N] = {}, inj_[N] = {};
    float lfoC_[N] = {}, lfoS_[N] = {}, lfoCos_ = 1.0f, lfoSin_ = 0.0f;
    float vinL_[2] = {}, vinR_[2] = {}, hcL_ = 0.0f, hcR_ = 0.0f;
    float shHpL_ = 0.0f, shHpR_ = 0.0f, shLpL_ = 0.0f, shLpR_ = 0.0f;
};

// ============================================================================
// Small helpers: biquad EQ
// ============================================================================

struct Biquad {
    float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    float z1[2] = {}, z2[2] = {};
    void clear() { z1[0] = z1[1] = z2[0] = z2[1] = 0.0f; }
    float run(int c, float x);
    void set(double B0, double B1, double B2, double A0, double A1, double A2);
    void hp(double f, double sr);
    void lp(double f, double sr);
    void lowShelf(double f, double db, double sr);
    void highShelf(double f, double db, double sr);
};

} // namespace chamber

// ============================================================================
// The device
// ============================================================================

class Chamber final : public Device {
  public:
    enum {
        Blend = 0,
        DryWet,
        ConvOn,
        AlgoOn,
        DryLevel,
        Routing,
        IrSelect,
        IrStart,
        IrDecay,
        IrAttack,
        IrSize,
        ConvPredelay,
        ConvSync,
        IrReverse,
        IrTrueStereo,
        AlgoMode,
        AlgoDecay,
        AlgoSize,
        AlgoDiffusion,
        AlgoDamping,
        AlgoPredelay,
        AlgoSync,
        AlgoLowDecay,
        AlgoHighDecay,
        Freeze,
        FreezeIn,
        AlgoVintage,
        ModRate,
        ModDepth,
        ShimmerAmount,
        ShimmerPitch,
        ShimmerFeedback,
        EqLowCut,
        EqLowGain,
        EqHighShelf,
        EqHighCut,
        EqPosition,
        DuckAmount,
        DuckRelease,
        Output,
        Width,
        BassMono,
        Quality,
        WetOnly,
        ZeroLatency,
        kNumParams
    };
    // Packed scope telemetry (lock-free, UI-polled).
    enum {
        S_WetL = 0,
        S_WetR,
        S_OutL,
        S_OutR,
        S_DuckDb,
        S_Cpu,
        S_Latency,
        S_SampleRate,
        S_Building,
        S_IrSeconds,
        S_IrRate,
        S_IrChannels,
        S_KernelSeconds,
        S_Bpm,
        S_Frozen,
        S_Block,
        S_UserIr,
        S_InPeak,
        S_Kernels,
        S_ResultSeconds,
        S_PreviewSeconds,
        S_PreviewGen,
        kScope
    };
    // deviceText ids.
    enum { T_IrName = 0, T_IrCategory = 1, T_UserName = 2, T_IrList = 10 };

    static constexpr int kIrCount = chamber::kNumBuiltinIrs + 1; // the last slot = the user IR
    static constexpr int kSyncDivs = 13;

    Chamber();
    ~Chamber() override;
    Chamber(const Chamber&) = delete;
    Chamber& operator=(const Chamber&) = delete;

    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransport(double beatStart, double spb, bool playing) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: convolution + FDN tails — the engine-visible tail. A
    // frozen loop never sleeps (infinite tail). Settles after the hangover;
    // kernel hand-off (flushSpill/install) runs before the skip so IR
    // re-shapes still land while sleeping.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Chamber"; }
    int32_t builtinKind() const override { return 20; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override;
    std::string deviceText(int32_t id) const override;

    bool loadFile(const std::string& path) override;
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;

  private:
    static constexpr int kChunk = 64;
    static constexpr int kRampLen = 1024;
    static constexpr int kXfLen = 1536;
    static constexpr int kQuietSamples = 4096;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    static constexpr double kMaxUserSec = 20.0;
    static constexpr uint32_t kMagic = 0x424D4843u; // "CHMB"

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int irIndex() const {
        return std::clamp(static_cast<int>(std::lround(get(IrSelect) * (kIrCount - 1))), 0, kIrCount - 1);
    }
    int qualityIndex() const { return std::clamp(static_cast<int>(std::lround(get(Quality) * 2.0f)), 0, 2); }
    float shimRatio() const;
    static int bigBlock(double sr) { return sr <= 50000.0 ? 1024 : sr <= 100000.0 ? 2048 : 4096; }
    float predelaySamples(int timeParam, int syncParam) const;

    void updateEq();
    void runEq(float* l, float* r, int n);

    void install(chamber::ConvKernel* k);
    void clearConv();
    void retire(chamber::ConvKernel* k);
    void flushSpill();

    struct Sig {
        int ir = -1, start = 0, end = 0, attack = 0, size = 0, rev = 0, ts = 0, zl = 0, q = 0, sr = 0;
        uint32_t gen = 0;
        bool operator==(const Sig& o) const;
        bool operator!=(const Sig& o) const { return !(*this == o); }
    };
    chamber::Algo::Params algoParams() const;

    static constexpr int kEnvBins = 512;
    static constexpr double kPreviewSr = 16000.0;
    static std::vector<float> envelope(const std::vector<std::vector<float>>& chans, const std::vector<int>& use);
    std::vector<int> previewSignature() const;
    void renderAlgoPreview();
    Sig signature() const;
    std::unique_ptr<chamber::ConvKernel> makeKernel(const Sig& s);
    bool rebuild(bool direct);

    void startWorker();
    void stopWorker();
    void workerLoop();
    void drainRetired();

    // ---- state ----
    std::atomic<float> p_[kNumParams] = {};
    double sr_ = 44100.0, spb_ = 22050.0;
    bool prepared_ = false;
    std::atomic<double> srA_{44100.0};

    chamber::Algo algo_;
    chamber::DLine convPre_[2], algoPre_[2];
    float convPreCur_ = -1.0f, algoPreCur_ = -1.0f;
    chamber::Biquad eq_[4];
    bool eqOn_[4] = {};
    float eqV_[4] = {-1.0f, -1.0f, -1.0f, -1.0f};
    double eqSr_ = 0.0;
    bool convActive_ = true, algoActive_ = true;
    float convG_ = 0.0f, algoG_ = 0.0f, feedG_ = 1.0f, wetS_ = 0.0f, dryS_ = 1.0f, outS_ = 1.0f, widthS_ = 1.0f;
    float duckEnv_ = 0.0f, duckGain_ = 1.0f, msLp1_[2] = {};
    double cpuS_ = 0.0;
    int algoQuiet_ = 0;
    float dL_[kChunk] = {}, dR_[kChunk] = {}, fL_[kChunk] = {}, fR_[kChunk] = {}, cL_[kChunk] = {}, cR_[kChunk] = {};
    float aL_[kChunk] = {}, aR_[kChunk] = {}, tL_[kChunk] = {}, tR_[kChunk] = {}, wL_[kChunk] = {}, wR_[kChunk] = {};
    float xL_[kChunk] = {}, xR_[kChunk] = {};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak engine feed (blocks sleep while tails ring)

    // kernels: audio-thread owned (cur_ / fade_ / ramp_), handed over through ready_,
    // returned through the SPSC ring (audio → worker) for deletion.
    chamber::ConvKernel* cur_ = nullptr;
    chamber::ConvKernel* fade_ = nullptr;
    chamber::ConvKernel* ramp_ = nullptr;
    chamber::ConvKernel* xfOld_ = nullptr;
    int rampPos_ = 0, xfPos_ = 0, xfWarm_ = 0;
    std::atomic<chamber::ConvKernel*> ready_{nullptr};
    static constexpr uint32_t kRing = 32;
    chamber::ConvKernel* ring_[kRing] = {};
    std::atomic<uint32_t> rHead_{0}, rTail_{0};
    chamber::ConvKernel* spill_[8] = {};

    // worker
    std::thread worker_;
    std::atomic<bool> stop_{false};
    std::mutex wMu_;
    std::condition_variable wCv_;
    std::mutex buildMu_;
    Sig builtSig_;
    bool built_ = false;
    std::atomic<bool> pending_{false};

    // IR sources (message / worker threads; never touched by process()).
    mutable std::mutex srcMu_;
    std::shared_ptr<const chamber::SourceIr> userIr_, shownSrc_;
    std::vector<float> convEnv_, algoEnv_; // RESULT lane + algorithm echogram (srcMu_)
    float convEnvSec_ = 0.0f, algoEnvSec_ = 0.0f;
    std::atomic<uint32_t> previewGen_{0};
    std::unique_ptr<chamber::Algo> preview_; // worker-only offline tank
    std::vector<int> previewSig_;            // worker-only
    std::atomic<uint32_t> userGen_{0};

    // meters
    std::atomic<float> mWet_{0.0f}, mOutL_{0.0f}, mOutR_{0.0f}, mIn_{0.0f}, mDuck_{0.0f}, cpuA_{0.0f}, kernSecA_{0.0f},
        bpmA_{120.0f};
    std::atomic<int> latA_{0}, kernA_{0};
};

} // namespace nota
