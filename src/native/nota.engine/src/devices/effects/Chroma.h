// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#pragma once

#include "devices/Device.h"
#include "dsp/primitives/BiquadCascade.h"
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
    int32_t latencySamples() const override { return kWindow; }
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
    static constexpr int kWindow = 1024, kHop = 256, kBins = kWindow / 2, kRing = 2 * kWindow;
    static constexpr int kPitchClasses = 12, kAllNotes = (1 << kPitchClasses) - 1;
    static constexpr int kFirstMidi = 36, kLastMidi = 151, kBankCapacity = 80, kSpectrumBins = 512;
    using Stereo = std::array<float, 2>;
    using Complex = std::complex<float>;
    using FFT = signalsmith::linear::RealFFT<float>;
    using Magnitudes = std::array<float, kBins + 1>;

    struct StereoFilter {
        std::array<dsp::BiquadSection, 2> channel{};
        void tune(dsp::BiquadCoeffs coeff);
        Stereo run(Stereo in);
    };

    struct Bank {
        struct Note {
            std::array<StereoFilter, 2> sections{};
            bool active = false;
        };
        std::array<Note, kLastMidi - kFirstMidi + 1> notes{};
        std::array<int, kBankCapacity> active{};
        int count = 0;

        void tune(const std::array<double, kLastMidi - kFirstMidi + 1>& frequencies, double sr, int mask, double low,
            double high, double q, double gain = 0.);
        Stereo run(Stereo in);
    };

    struct Shifter {
        std::array<Stereo, kWindow> input{};
        std::array<Stereo, kRing> output{};
        std::array<std::array<double, kBins>, 2> analysisPhase{}, synthesisPhase{};
        std::array<std::array<Complex, kBins>, 2> spectrum{};
        double ratio = 1, rmsSum = 0, lastRms = 0, onsetEnvelope = 0;
        std::vector<float> rmsHistory;
        int rmsPosition = 0, refractory = 0;
        bool firstFrame = true;

        void reset();
        void push(Stereo in, uint64_t sample);
        void analyse(Chroma& effect, uint64_t end);
        void synthesize(Chroma& effect, uint64_t end, const Magnitudes* gain);
        void accumulate(Magnitudes& power) const;
        Stereo pop(uint64_t sample);
    };

    struct Path {
        int mask = 0, down = 0, up = 0, common = 0;
        bool dirty = true;
        double tunedLow = -1, tunedHigh = -1, tunedQ = -1, tunedGain = -1;
        Bank shared{}, downInput{}, upInput{}, reject{}, resonator{};
        std::array<Shifter, 2> shift{};
        StereoFilter correctionHigh{}, correctionLow{};
        Magnitudes gateGain{};
        float notchGain = 1, resonanceGain = 1, resonanceGainTarget = 1, resonanceBlend = 0;

        void reset(int newMask);
        void tune(const Chroma& effect);
        void frame(Chroma& effect, uint64_t end, const Magnitudes* dry);
        Stereo process(
            Chroma& effect, Stereo mid, Stereo dry, float transient, Stereo dynamics, uint64_t sample, Stereo& tuned);
        bool active() const { return mask != 0 && mask != kAllNotes; }
    };

    struct Envelope {
        double value = 0;
        double run(double input, double attack, double release);
    };

    void updateControls();
    void updateBand();
    float transientMask(Stereo dry);
    Stereo correctionMask(Stereo mid);
    void analyseDry(uint64_t end);
    void publishSpectrum();
    void publishLine(
        const std::array<Stereo, kWindow>& history, std::array<std::atomic<float>, kSpectrumBins>& destination);
    float pole(double ms) const;

    std::array<std::atomic<float>, kNumParams> p_{};
    std::array<std::atomic<float>, kSpectrumBins> spectrumView_{}, tunedView_{};
    std::atomic<float> srView_{44100};
    FFT fft_;
    std::array<float, kWindow> window_{}, time_{};
    std::array<Complex, kBins> fftBins_{};
    std::array<double, kBins> phaseAdvance_{};
    std::array<double, kLastMidi - kFirstMidi + 1> frequencies_{};
    std::array<double, kSpectrumBins + 1> viewEdges_{};
    std::array<Stereo, kWindow> fullDelay_{}, midDelay_{}, sidesDelay_{}, dryView_{}, tunedViewHistory_{};
    Magnitudes dryMagnitude_{};
    std::array<Path, 2> paths_{};
    StereoFilter low1_{}, low2_{}, high1_{}, high2_{}, midHigh_{}, midLow_{}, finalHigh_{}, finalLow_{};
    dsp::BiquadSection guardHigh_{}, guardLow_{};
    std::array<Envelope, 2> fast_{}, slow_{}, noteMaskEnvelope_{};
    Envelope guardEnvelope_{}, guardRelease_{};

    double sr_ = 44100, low_ = 150, high_ = 8000, targetLow_ = 150, targetHigh_ = 8000;
    double decay_ = .12, resonance_ = 0, resonanceTarget_ = 0, guardRamp_ = 0;
    float amount_ = 1, mix_ = 1, gain_ = 1, morph_ = 0, gate_ = 0, gateThreshold_ = -60;
    float amountTarget_ = 1, mixTarget_ = 1, gainTarget_ = 1, morphTarget_ = 0, gateTarget_ = 0;
    float fastRelease_ = 0, slowAttack_ = 0, slowRelease_ = 0, noteRelease_ = 0;
    float guardAttack_ = 0, guardSlowRelease_ = 0, guardFastRelease_ = 0, onsetPole_ = 0;
    float bandPole_ = 0, resonantPole_ = 0, makeupPole_ = 0, blendStep_ = 0, controlPole_ = 0;
    float amountDelta_ = 0;
    int amountRampLength_ = 0, amountRemaining_ = 0;
    float gateOpen_ = 0, gateClose_ = 0;
    int controlInterval_ = 8, currentPath_ = 0, requestedMask_ = 0, fadeLength_ = 0, fadeRemaining_ = 0;
    int guardLength_ = 0, guardPosition_ = 0;
    uint64_t sample_ = 0, quiet_ = 0;
    bool idle_ = false, snap_ = true;
};

} // namespace nota
