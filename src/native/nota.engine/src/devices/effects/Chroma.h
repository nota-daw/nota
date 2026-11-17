// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Stereo-linked spectral retuning. All working storage is prepared off-thread.
#pragma once

#include "devices/Device.h"
#include <signalsmith-linear/fft.h>
#include <array>
#include <complex>
#include <vector>

namespace nota {

class Chroma final : public Device {
  public:
    enum {
        Root,
        Scale,
        Amount,
        Resonance,
        Decay,
        Low,
        High,
        Mix,
        Output,
        NoteC,
        Morph = NoteC + 12,
        Gate,
        GateThreshold,
        kNumParams
    };
    Chroma();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void process(float* buf, int32_t frames) override;
    int32_t latencySamples() const override { return n_; }
    const char* displayName() const override { return "Nota Chroma"; }
    int32_t builtinKind() const override { return 27; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override { idle_ = false; }
    int32_t scopeRead(float* out, int32_t max) const override;
    static double expMap(double v, double lo, double hi);
    int noteMask() const;

  private:
    using Complex = std::complex<float>;
    void frame();
    double targetHz(double hz, int mask) const;
    std::array<std::atomic<float>, kNumParams> p_{};
    signalsmith::linear::RealFFT<float> fft_;
    double sr_ = 44100;
    int n_ = 2048, hop_ = 256, bins_ = 1024;
    uint64_t count_ = 0, quiet_ = 0;
    bool idle_ = false, snap_ = true, firstFrame_ = true;
    float mixS_ = 1, gainS_ = 1;
    double amountS_ = 1, resS_ = .25, lowS_ = 150, highS_ = 8000;
    double morphS_ = 0, gateS_ = 0, thresholdS_ = -60;
    std::vector<float> window_, time_, energy_, previousEnergy_, gateGain_;
    std::array<std::vector<float>, 2> input_, ola_, previousPhase_;
    std::array<std::vector<Complex>, 2> spectrum_, wet_, tail_, tunedViewBins_, tunedTail_;
    std::vector<double> rotation_, previousRotation_, shift_, previousShift_, peakHz_, previousPeakHz_;
    std::array<double, 513> viewEdges_{};
    std::vector<int> peaks_, owner_, reference_, previousReference_, activePeaks_, previousPeaks_, matched_;
    int peakCount_ = 0;
    static constexpr int kSpectrumBins = 512;
    std::array<std::atomic<float>, kSpectrumBins> spectrumView_{}, tunedView_{};
    std::atomic<float> srView_{44100}, latencyView_{2048};
};

} // namespace nota
