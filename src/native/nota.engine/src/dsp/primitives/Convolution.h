// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Convolution — zero-latency uniformly-partitioned FFT convolution
// . Extracted verbatim from Chamber.h's ConvKernel: a 64-tap
// direct-FIR head (zero latency) plus uniformly partitioned overlap-add FFT
// stages (signalsmith RealFFT) over the IR tail. Kernels are built on a worker
// thread and handed to the audio thread; transplant() carries input history
// across rebuilds so re-shaped IRs re-render tails instead of restarting.
// Shared by Chamber (its only user today).

#pragma once

#include <signalsmith-linear/fft.h>

#include <cstdint>
#include <memory>
#include <vector>

namespace nota {
namespace dsp {

class ConvKernel {
  public:
    struct Path {
        int in = 0, out = 0, k = 0;
    };

    double sampleRate = 0.0;
    int latency = 0;      // samples this kernel adds (0 = zero-latency head)
    int block = 0;        // big partition size
    float seconds = 0.0f; // processed IR length

    // h: processed responses at the engine rate (equal lengths); paths route input → output.
    // Built on a worker thread (allocates); the audio thread only runs process().
    static std::unique_ptr<ConvKernel> build(const std::vector<std::vector<float>>& h, const std::vector<Path>& paths,
        int nIn, bool monoIn, bool zeroLatency, int bigB, double sr);

    // Convolve n frames, ADDING into outL/outR. A null input means silence (a fading kernel).
    void process(const float* inL, const float* inR, float* outL, float* outR, int n);

    void clear();

    // Same partition layout → input history can be handed from one kernel to the
    // next, so a re-shaped IR re-renders the whole tail instead of starting over.
    bool compatible(const ConvKernel& o) const;
    void transplant(const ConvKernel& o);
    // Samples until every stage's output is exact after a transplant.
    int warmup() const;
    bool silent() const;

    ConvKernel() = default;

  private:
    static constexpr int kFir = 64, kScratch = 256, kMaxStages = 2;

    struct Fir {
        int f = 0, nIn = 0, nK = 0, pos = 0, quiet = 1 << 30;
        std::vector<float> taps; // [nK][f], time-reversed
        std::vector<float> hist; // [nIn][2f] mirrored ring
        void init(int f_, int nIn_, int nK_);
        void setKernel(int k, const float* h, int len);
        void clear();
        void run(const float* a, const float* b, float* oL, float* oR, int n, const Path* paths, int nPaths);
    };

    // One uniformly partitioned overlap-add stage over IR[offset, offset + P·B).
    struct Stage {
        int B = 0, P = 0, nIn = 0, nK = 0;
        signalsmith::linear::RealFFT<float> fft;
        std::vector<float> Hr, Hi;       // [nK][P][B], pre-scaled by 1/2B
        std::vector<float> Xr, Xi;       // [nIn][P][B] frequency-domain delay line
        std::vector<uint8_t> live;       // [nIn][P] slot holds non-zero input
        std::vector<float> in, out, ovl; // [nIn][B] · [2][B] · [2][B]
        std::vector<float> time, ar, ai; // [2B] · [2][B] · [2][B]
        int pos = 0, fill = 0, liveSlots = 0, quietBlocks = 2;

        void init(int b, int p, int nIn_, int nK_, const std::vector<std::vector<float>>& h, int offset, int len);
        void clear();
        bool silent() const;
        void transplant(const Stage& o);
        void run(const float* a, const float* b, float* oL, float* oR, int n, const Path* paths, int nPaths,
            const bool* used);
        void compute(const Path* paths, int nPaths, const bool* used);
    };

    int nIn_ = 2, nPaths_ = 0, nStages_ = 0;
    bool monoIn_ = false;
    bool used_[2] = {false, false};
    Path paths_[4] = {};
    Fir fir_;
    Stage stages_[kMaxStages];
    std::vector<float> scratch_;
};

} // namespace dsp
} // namespace nota
