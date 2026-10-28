// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The spectrum the EQ cards draw (specs/046). Reads the latest samples of a device's mono ring,
// runs a Hann-windowed FFT (4096 points, 8192 above 50 kHz) and maps it onto kPoints log-spaced
// points from 20 Hz to 20 kHz. Each point reads the energy around it through a raised-cosine
// window ±1/6 octave wide (1/6 octave effective), so a sine reads its own level at its own point
// and pink noise reads flat; where the window holds no bin the point interpolates the two
// nearest bins. Ballistics: a fast rise, a fixed 300 ms-per-20 dB
// fall, and an optional peak that holds 1.5 s. A ring that has stopped advancing counts as
// silence, so the curve falls away instead of freezing.
//
// Not real-time code: the owner calls analyse() from its telemetry read, under its own lock,
// never from the audio thread.

#pragma once

#include <signalsmith-linear/fft.h>

#include <complex>
#include <cstdint>
#include <vector>

namespace nota::dsp {

class SpectrumAnalyzer {
  public:
    static constexpr int kPoints = 256;
    static constexpr double kLo = 20.0, kHi = 20000.0;
    static constexpr float kFloorDb = -150.0f;
    static constexpr double kHalfOct = 1.0 / 6.0;  // half-width of a point's window, octaves
    static constexpr double kRiseS = 0.020;        // rise time constant
    static constexpr double kFallDbPerS = 20.0 / 0.3;
    static constexpr double kPeakHoldS = 1.5;
    static constexpr double kPeakFallDbPerS = kFallDbPerS * 0.5;
    // A pause past 1.5 callback periods (within these bounds) is the input stopping, not a
    // read that fell between two callbacks (2048 frames at 44.1 kHz arrive 46 ms apart).
    static constexpr double kStalledMinS = 0.03, kStalledMaxS = 0.25;
    // The held peak may not fall away from a neighbouring point faster than this (dB per point,
    // ~51 dB/octave): each point's hold runs out on its own, and without it the line breaks
    // into notches and spikes on drums.
    static constexpr float kPeakSlopeDb = 2.0f;

    /// FFT size for a sample rate (the ring must hold at least this many samples).
    static int fftSizeFor(double sampleRate) { return sampleRate > 50000.0 ? 8192 : 4096; }

    void prepare(double sampleRate);
    void reset();

    /// Analyses the fftSize() samples that end at write index `w` of a power-of-two ring
    /// (`mask` = size − 1), `dt` seconds after the previous call (≤ 0: the first, which snaps).
    void analyse(const float* ring, uint32_t mask, uint32_t w, double dt, bool peakHold);

    const float* curve() const { return curve_.data(); }
    const float* peak() const { return peak_.data(); }
    int fftSize() const { return n_; }
    /// True once something above the floor has been seen since the last reset or silence.
    bool valid() const { return valid_; }

  private:
    struct Point {
        int first = 0, count = 0; // weights_[first .. first + count) apply to bins k0 ..
        int k0 = 1;
        double x = 1.0;           // fractional bin of the point's centre (count 0: interpolate)
    };

    double sr_ = 44100.0;
    int n_ = 0;
    signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_, time_;
    std::vector<std::complex<float>> freq_;
    std::vector<double> pow_;
    std::vector<Point> pts_;
    std::vector<float> weights_;
    std::vector<float> curve_ = std::vector<float>(kPoints, kFloorDb);
    std::vector<float> peak_ = std::vector<float>(kPoints, kFloorDb);
    std::vector<float> peakAge_ = std::vector<float>(kPoints, 0.0f);
    double ref_ = 1.0, refBin_ = 1.0;
    uint32_t lastW_ = 0;
    double stalled_ = 0.0;   // seconds the write index hasn't moved
    double gap_ = 0.0;       // seconds between its last two moves (≈ the callback period)
    bool primed_ = false, valid_ = false;
};

} // namespace nota::dsp
