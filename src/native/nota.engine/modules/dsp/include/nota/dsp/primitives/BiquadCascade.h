// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// BiquadCascade — Direct Form II Transposed biquad sections and 64-sample block cascade.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <cstdint>

namespace nota::dsp {

struct BiquadCoeffs {
    double b0 = 1.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0;
};

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
    BandPass = 7,
};

enum class BiquadChannel : uint8_t { Stereo = 0, Left = 1, Right = 2, Mid = 3, Side = 4 };

class NOTA_DSP_API BiquadCascade {
  public:
    static constexpr int32_t kMaxSections = 8;
    static constexpr int32_t kBlock = 64;

    BiquadCascade() = default;

    void setStages(int32_t n) noexcept;
    int32_t stages() const noexcept { return stages_; }
    BiquadSection* bank(int32_t b) noexcept { return st_[b]; }
    void setSection(int32_t i, const BiquadCoeffs& c) noexcept;
    void reset() noexcept;

    double processSampleL(double x) NOTA_RT_SAFE { return runBank(0, x); }
    double processSampleR(double x) NOTA_RT_SAFE { return runBank(1, x); }

    void processFrame(double& l, double& r, BiquadChannel ch) NOTA_RT_SAFE {
        switch (ch) {
        case BiquadChannel::Stereo:
            l = runBank(0, l);
            r = runBank(1, r);
            break;
        case BiquadChannel::Left: l = runBank(0, l); break;
        case BiquadChannel::Right: r = runBank(1, r); break;
        case BiquadChannel::Mid: {
            const double side = (l - r) * 0.5;
            const double f = runBank(0, (l + r) * 0.5);
            l = f + side;
            r = f - side;
            break;
        }
        case BiquadChannel::Side: {
            const double mid = (l + r) * 0.5;
            const double f = runBank(0, (l - r) * 0.5);
            l = mid + f;
            r = mid - f;
            break;
        }
        }
    }

    void processBlock(float* buf, int32_t frames, BiquadChannel ch) NOTA_RT_SAFE;

    static BiquadCoeffs lowpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs highpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs bell(double sr, double f0, double gainDb, double q) NOTA_RT_SAFE;
    static BiquadCoeffs lowShelf(double sr, double f0, double gainDb) NOTA_RT_SAFE;
    static BiquadCoeffs highShelf(double sr, double f0, double gainDb) NOTA_RT_SAFE;
    static BiquadCoeffs notch(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs allpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs bandpass(double sr, double f0, double q) NOTA_RT_SAFE;
    static BiquadCoeffs fromType(BiquadType t, double sr, double f0, double gainDb, double q) NOTA_RT_SAFE;
    static int32_t butterworthQs(double* out, int32_t slope) NOTA_RT_SAFE;

  private:
    BiquadSection st_[2][kMaxSections];
    int32_t stages_ = 0;

    double runBank(int32_t bank, double x) NOTA_RT_SAFE {
        for (int32_t k = 0; k < stages_; ++k) x = st_[bank][k].run(x);
        return x;
    }
};

} // namespace nota::dsp
