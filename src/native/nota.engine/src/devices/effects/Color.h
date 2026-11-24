// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#pragma once

#include "devices/Device.h"
#include "devices/MidiDevice.h"
#include "dsp/primitives/BiquadCascade.h"
#include <signalsmith-linear/fft.h>
#include <array>
#include <complex>
#include <memory>
#include <vector>

namespace nota {

class Color final : public Device {
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
        RetiredMode,
        MuteLow,
        MuteHigh,
        View,
        FftSize,
        Pre,
        Post,
        PreEnabled,
        kNumParams
    };
    Color();
    ~Color() override;
    void setSampleRate(double sr, int32_t maxBlock) override;
    void process(float* buf, int32_t frames) override;
    int32_t latencySamples() const override;
    const char* displayName() const override { return "Nota Color"; }
    int32_t builtinKind() const override { return 27; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;
    bool isLatencyConfigurationParam(int32_t i) const override { return i == FftSize || i == Gate || i == PreEnabled; }
    bool isPresetParam(int32_t i) const override { return i != FftSize; }
    bool acceptsSidechain() const override { return true; }
    bool wantsMidiKey() const override { return true; }
    bool wantsMidiKeyWhenSilent() const override { return true; }
    int32_t sidechainSourceTrackId() const override { return midiSource_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override;
    void setMidiKeyBlock(const MidiKeyBlock& block) override;
    bool hasTail() const override { return true; }
    bool isIdle() const override {
        return idle_ && requestedFft() == activeFft_ && requestedGate() == activeGate_ &&
               requestedPre() == activePre_;
    }
    void wake() override;
    int32_t scopeRead(float* out, int32_t max) const override;
    static double expMap(double v, double lo, double hi);
    int noteMask() const;

  private:
    static constexpr std::array kFftSizes{512, 1024, 2048};
    static constexpr int kDefaultFft = 1, kScopeWindow = kFftSizes[kDefaultFft], kScopeHop = kScopeWindow / 4;
    static constexpr int kMaxWindow = kFftSizes.back(), kMaxBins = kMaxWindow / 2, kRing = 2 * kMaxWindow;
    static constexpr float kScopeNormalizer = .450158f * kScopeWindow;
    static constexpr int kPitchClasses = 12, kAllNotes = (1 << kPitchClasses) - 1;
    static constexpr int kFirstMidi = 36, kLastMidi = 151, kBankCapacity = 80, kSpectrumBins = 512;
    using Stereo = std::array<float, 2>;
    using Complex = std::complex<float>;
    using FFT = signalsmith::linear::RealFFT<float>;
    using Magnitudes = std::array<float, kMaxBins + 1>;
    using ScopeHistory = std::array<Stereo, kScopeWindow>;
    using ScopeLevels = std::array<std::atomic<float>, kSpectrumBins>;
    using ViewEdges = std::array<double, kSpectrumBins + 1>;

    struct FftConfig {
        FFT fft;
        std::array<float, kMaxWindow> window{};
        std::array<double, kMaxBins> phaseAdvance{};
        int size = 0, hop = 0, bins = 0;
        float onsetPole = 0, gateOpen = 0, gateClose = 0;
    };

    struct StereoFilter {
        std::array<dsp::BiquadSection, 2> channel{};
        void tune(dsp::BiquadCoeffs coeff);
        Stereo run(Stereo in);
    };

    struct OutputFrame {
        Stereo dry{}, wet{}, tuned{}, sides{}, muted{};
        float effectWeight = 0, gatedFraction = 0;
    };

    // The second STFT operates on the completed wet band. The frame ring also
    // delays the parallel paths so they meet the resynthesized wet at Mix.
    struct SpectralGate {
        void reset();
        OutputFrame push(const OutputFrame& frame, uint64_t sample, int window);
        void analyse(Color& effect, uint64_t end);
        float gainAt(double bin) const;

      private:
        std::array<OutputFrame, kMaxWindow> input{};
        std::array<Stereo, kRing> output{};
        Magnitudes gain{};
        bool firstFrame = true;
    };

    // Telemetry on the same taps as the short spectrum, with its own filter and FFT state.
    struct BassSpectrum {
        static constexpr int kFftSize = 1024, kFftBins = kFftSize / 2;
        struct Line {
            std::array<StereoFilter, 3> lowpass{};
            std::array<Stereo, kFftSize> history{};
            std::array<float, kSpectrumBins> viewPower{};

            Stereo filter(Stereo sample);
        };

        void prepare(double sr, const ViewEdges& edges);
        void reset();
        void push(Stereo drySample, Stereo tunedSample);
        void analyse(Line& line, const ViewEdges& edges);

        Line dry{}, tuned{};
        std::array<float, kSpectrumBins> shortWeights{};
        bool refresh = true;

      private:
        FFT fft;
        std::array<float, kFftSize> window{}, time{};
        std::array<Complex, kFftBins> bins{};
        std::array<float, kFftBins + 1> power{};
        double binsPerHz = 0;
        float powerScale = 0;
        int stride = 1, untilNext = 1, write = 0, windowSize = 0, viewBins = 0;
    };

    struct NoteTuning {
        double frequency = 0, sinOmega = 0, cosOmega = 0;
        dsp::BiquadCoeffs inputNotch{}, rejectNotch{};

        dsp::BiquadCoeffs bell(double q, double gain) const;
    };
    using NoteTunings = std::array<NoteTuning, kLastMidi - kFirstMidi + 1>;

    struct Bank {
        enum class Shape { InputNotch, RejectNotch, Bell };
        struct alignas(16) Section {
            std::array<double, 2> z1{}, z2{};
        };
        struct Note {
            dsp::BiquadCoeffs coeffs{};
            std::array<Section, 2> sections{};
            double frequency = 0;
            bool active = false;
        };
        std::array<Note, kLastMidi - kFirstMidi + 1> notes{};
        std::array<int, kBankCapacity> active{};
        int count = 0;

        void tune(const NoteTunings& tunings, double sr, int mask, double low, double high, Shape shape,
            double q = 0, double gain = 1);
        Stereo run(Stereo in);
    };

    struct Shifter {
        std::array<Stereo, kMaxWindow> input{};
        std::array<Stereo, kRing> output{};
        std::array<std::array<double, kMaxBins>, 2> analysisPhase{}, synthesisPhase{};
        std::array<std::array<Complex, kMaxBins>, 2> spectrum{};
        double ratio = 1, rmsSum = 0, lastRms = 0, onsetEnvelope = 0;
        std::vector<float> rmsHistory;
        int rmsPosition = 0, refractory = 0;
        bool firstFrame = true;

        void reset();
        void push(Stereo in, uint64_t sample);
        void analyse(Color& effect, uint64_t end);
        void synthesize(Color& effect, uint64_t end, const Magnitudes* gain);
        void accumulate(Magnitudes& power, int bins) const;
        Stereo pop(uint64_t sample);
    };

    struct Path {
        int mask = 0, down = 0, up = 0, common = 0;
        bool dirty = true;
        double tunedLow = -1, tunedHigh = -1, tunedQ = -1, tunedGain = -1, tunedPost = 0;
        Bank shared{}, downInput{}, upInput{}, reject{}, resonator{};
        std::array<Shifter, 2> shift{};
        StereoFilter correctionHigh{}, correctionLow{};
        float notchGain = 1, resonanceGain = 1, resonanceGainTarget = 1, resonanceBlend = 0;

        void reset(int newMask);
        void tune(const Color& effect);
        void frame(Color& effect, uint64_t end, const Magnitudes* dry);
        Stereo process(
            Color& effect, Stereo mid, Stereo dry, float transient, Stereo dynamics, uint64_t sample, Stereo& tuned);
        bool active() const { return mask != 0 && mask != kAllNotes; }
    };

    struct Envelope {
        double value = 0;
        double run(double input, double attack, double release);
    };

    void updateControls(int mask, bool midi);
    int requestedFft() const;
    bool requestedGate() const { return getParam(Gate) >= .5f; }
    bool requestedPre() const { return getParam(PreEnabled) >= .5f; }
    void resetProcessing(int fftIndex, bool gateEnabled, bool preEnabled);
    void prepareNoteTunings(NoteTunings& tunings, double cents);
    void updatePost();
    FftConfig& processingFft() { return fftConfigs_[activeFft_]; }
    void updateBand();
    float transientMask(Stereo dry);
    Stereo correctionMask(Stereo mid);
    void analyseDry(uint64_t end);
    void clearSpectrum();
    void publishSpectrum();
    void publishLine(const ScopeHistory& history, const BassSpectrum::Line& bass, ScopeLevels& destination, bool gated = false);
    float pole(double ms) const;

    std::array<std::atomic<float>, kNumParams> p_{};
    ScopeLevels spectrumView_{}, tunedView_{};
    std::atomic<float> srView_{44100};
    std::array<FftConfig, kFftSizes.size()> fftConfigs_{};
    std::array<float, kMaxWindow> time_{};
    std::array<Complex, kMaxBins> fftBins_{};
    NoteTunings noteTunings_{}, shiftedNoteTunings_{};
    double postCents_ = 0, postSmooth_ = 0;
    ViewEdges viewEdges_{};
    BassSpectrum bassSpectrum_{};
    struct Bands {
        Stereo full{}, mid{}, low{}, high{};
    };
    struct PreOutput {
        Stereo delta{}, tuned{};
        float mix = 0;
    };
    struct PreShifter;
    std::unique_ptr<PreShifter> pre_;
    std::array<Bands, kMaxWindow> bandDelay_{};
    std::array<PreOutput, kMaxWindow> preOutputDelay_{};
    SpectralGate spectralGate_{};
    ScopeHistory dryView_{}, tunedViewHistory_{};
    Magnitudes dryMagnitude_{};
    std::array<Path, 2> paths_{};
    StereoFilter low1_{}, low2_{}, high1_{}, high2_{}, midHigh_{}, midLow_{}, finalHigh_{}, finalLow_{};
    dsp::BiquadSection guardHigh_{}, guardLow_{};
    std::array<Envelope, 2> fast_{}, slow_{}, noteMaskEnvelope_{};
    Envelope guardEnvelope_{}, guardRelease_{};

    double sr_ = 44100, low_ = 150, high_ = 8000, targetLow_ = 150, targetHigh_ = 8000;
    double decay_ = .12, resonance_ = 0, resonanceTarget_ = 0, guardRamp_ = 0;
    float amount_ = 1, mix_ = 1, gain_ = 1, morph_ = 0, gateThreshold_ = -60;
    float amountTarget_ = 1, mixTarget_ = 1, gainTarget_ = 1, morphTarget_ = 0;
    float muteLow_ = 0, muteHigh_ = 0, muteLowTarget_ = 0, muteHighTarget_ = 0, muteStep_ = 0;
    float fastRelease_ = 0, slowAttack_ = 0, slowRelease_ = 0, noteRelease_ = 0;
    float guardAttack_ = 0, guardSlowRelease_ = 0, guardFastRelease_ = 0;
    float bandPole_ = 0, resonantPole_ = 0, makeupPole_ = 0, blendStep_ = 0, controlPole_ = 0;
    float amountDelta_ = 0;
    int amountRampLength_ = 0, amountRemaining_ = 0;
    int activeFft_ = kDefaultFft;
    bool activeGate_ = false, activePre_ = false;
    int controlInterval_ = 8, currentPath_ = 0, requestedMask_ = 0, fadeLength_ = 0, fadeRemaining_ = 0;
    int guardLength_ = 0, guardPosition_ = 0;
    uint64_t sample_ = 0, quiet_ = 0;
    bool idle_ = false, snap_ = true;
    int fadeWait_ = 0, activeFadeLength_ = 1;
    std::atomic<int32_t> midiSource_{-1};
    std::atomic<int> midiMaskView_{0};
    MidiKeyNotes midiStart_{};
    std::array<MidiEv, 1024> midiEvents_{};
    int32_t midiBlockSource_ = -1, midiEventCount_ = 0;
    HeldMidiKeyReader heldKeys_; // it reads the held notes, also while its track is silent
};

} // namespace nota
