// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// BiquadCascade — Direct Form II Transposed biquad sections and block cascade
// . Coefficients use Eq.h's proven RBJ/Orfanidis calculators;
// the DF2T state update matches Eq.h's State::run exactly. Block processing in
// 64-sample chunks keeps the channel-routing branch out of the inner loop.
// Shared by Eq, DynamicEq, Eq3 and the Amp/Keys cabinets.

#pragma once

#include <cstdint>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

struct BiquadCoeffs {
    double b0 = 1.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0;
};

// One DF2T section. Message thread writes coefficients; audio thread runs.
struct BiquadSection {
    BiquadCoeffs c;
    double z1 = 0.0, z2 = 0.0;

    void reset() noexcept { z1 = z2 = 0.0; }
    double run(double x) NOTA_RT_SAFE {
        const double y = c.b0 * x + z1;
        z1 = c.b1 * x - c.a1 * y + z2;
        z2 = c.b2 * x - c.a2 * y;
        return y;
    }
};

enum class BiquadType : uint8_t {
    LowPass = 0,
    HighPass = 1,
    Bell = 2,
    LowShelf = 3,
    HighShelf = 4,
    Notch = 5,
    Allpass = 6,
    BandPass = 7 // constant 0 dB peak-governed BPF (detectors)
};

enum class BiquadChannel : uint8_t { Stereo = 0, Left = 1, Right = 2, Mid = 3, Side = 4 };

class BiquadCascade {
  public:
    static constexpr int kMaxSections = 8;
    static constexpr int kBlock = 64;

    BiquadCascade() = default;

    void setStages(int n) noexcept;
    int stages() const noexcept { return stages_; }
    // Direct access to a bank's sections (bank 0 = L/Mid/Side, 1 = R), for hosts that
    // flatten several cascades into one chain.
    BiquadSection* bank(int b) noexcept { return st_[b]; }
    void setSection(int i, const BiquadCoeffs& c) noexcept;
    void reset() noexcept;

    double processSampleL(double x) NOTA_RT_SAFE { return runBank(0, x); }
    double processSampleR(double x) NOTA_RT_SAFE { return runBank(1, x); }
    // One stereo frame with channel routing, in double (for sample-major series chains).
    void processFrame(double& l, double& r, BiquadChannel ch) NOTA_RT_SAFE {
        switch (ch) {
        case BiquadChannel::Stereo:
            l = runBank(0, l);
            r = runBank(1, r);
            break;
        case BiquadChannel::Left: l = runBank(0, l); break;
        case BiquadChannel::Right: r = runBank(1, r); break;
        case BiquadChannel::Mid: {
            const double side = (l - r) * 0.5, f = runBank(0, (l + r) * 0.5);
            l = f + side;
            r = f - side;
            break;
        }
        case BiquadChannel::Side: {
            const double mid = (l + r) * 0.5, f = runBank(0, (l - r) * 0.5);
            l = mid + f;
            r = mid - f;
            break;
        }
        }
    }
    // Interleaved stereo block with channel routing (branch hoisted outside
    // the 64-sample chunks). Left/Mid/Side run on bank 0, Right on bank 1.
    void processBlock(float* buf, int32_t frames, BiquadChannel ch) NOTA_RT_SAFE;

    // RBJ cookbook calculators (proven in Eq.h). q > 0; gainDb for Bell/Shelf.
    static BiquadCoeffs lowpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs highpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs bell(double sr, double f0, double gainDb, double q) NOTA_RT_SAFE;
    static BiquadCoeffs lowShelf(double sr, double f0, double gainDb) NOTA_RT_SAFE;
    static BiquadCoeffs highShelf(double sr, double f0, double gainDb) NOTA_RT_SAFE;
    static BiquadCoeffs notch(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs allpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs bandpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs fromType(BiquadType t, double sr, double f0, double gainDb, double q) NOTA_RT_SAFE;

    // Butterworth cut Q tables (Eq.h): 24 dB (2 sections) / 48 dB (4 sections).
    static int butterworthQs(double* out, int slope) NOTA_RT_SAFE;

  private:
    // Per-channel state banks sharing one coefficient set (no L/R crosstalk).
    BiquadSection st_[2][kMaxSections];
    int stages_ = 0;

    double runBank(int bank, double x) NOTA_RT_SAFE {
        for (int k = 0; k < stages_; ++k) x = st_[bank][k].run(x);
        return x;
    }
};

} // namespace dsp
} // namespace nota
