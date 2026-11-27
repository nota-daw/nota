// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/BiquadCascade.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota::dsp {
namespace {

BiquadCoeffs norm(double b0, double b1, double b2, double a0, double a1, double a2) noexcept {
    return {b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0};
}

} // namespace

void BiquadCascade::setStages(int32_t n) noexcept {
    stages_ = n < 0 ? 0 : (n > kMaxSections ? kMaxSections : n);
}

void BiquadCascade::setSection(int32_t i, const BiquadCoeffs& c) noexcept {
    if (i >= 0 && i < kMaxSections) {
        st_[0][i].c = c;
        st_[1][i].c = c;
    }
}

void BiquadCascade::reset() noexcept {
    for (auto& bank : st_) {
        for (auto& s : bank) s.reset();
    }
}

void BiquadCascade::processBlock(float* buf, int32_t frames, BiquadChannel ch) NOTA_RT_SAFE {
    if (buf == nullptr || frames <= 0 || stages_ <= 0) return;
    if (ch == BiquadChannel::Stereo) {
        int32_t i = 0;
        while (i < frames) {
            const int32_t m = std::min<int32_t>(kBlock, frames - i);
            for (int32_t k = 0; k < m; ++k) {
                const int32_t o = (i + k) * 2;
                buf[o] = static_cast<float>(runBank(0, static_cast<double>(buf[o])));
                buf[o + 1] = static_cast<float>(runBank(1, static_cast<double>(buf[o + 1])));
            }
            i += m;
        }
        return;
    }
    int32_t i = 0;
    while (i < frames) {
        const int32_t m = std::min<int32_t>(kBlock, frames - i);
        for (int32_t k = 0; k < m; ++k) {
            const int32_t o = (i + k) * 2;
            const float a = buf[o];
            const float b = buf[o + 1];
            if (ch == BiquadChannel::Mid || ch == BiquadChannel::Side) {
                const float mid = (a + b) * 0.5f;
                const float side = (a - b) * 0.5f;
                if (ch == BiquadChannel::Mid) {
                    const float f = static_cast<float>(runBank(0, static_cast<double>(mid)));
                    buf[o] = f + side;
                    buf[o + 1] = f - side;
                } else {
                    const float f = static_cast<float>(runBank(0, static_cast<double>(side)));
                    buf[o] = mid + f;
                    buf[o + 1] = mid - f;
                }
            } else if (ch == BiquadChannel::Left) {
                buf[o] = static_cast<float>(runBank(0, static_cast<double>(a)));
            } else {
                buf[o + 1] = static_cast<float>(runBank(1, static_cast<double>(b)));
            }
        }
        i += m;
    }
}

BiquadCoeffs BiquadCascade::lowpass(double sr, double f0, double q) NOTA_RT_SAFE {
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    return norm((1.0 - cw) / 2.0, 1.0 - cw, (1.0 - cw) / 2.0, 1.0 + alpha, -2.0 * cw, 1.0 - alpha);
}

BiquadCoeffs BiquadCascade::highpass(double sr, double f0, double q) NOTA_RT_SAFE {
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    return norm((1.0 + cw) / 2.0, -(1.0 + cw), (1.0 + cw) / 2.0, 1.0 + alpha, -2.0 * cw, 1.0 - alpha);
}

BiquadCoeffs BiquadCascade::bell(double sr, double f0, double gainDb, double q) NOTA_RT_SAFE {
    const double A = std::pow(10.0, gainDb / 40.0);
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    return norm(1.0 + alpha * A, -2.0 * cw, 1.0 - alpha * A, 1.0 + alpha / A, -2.0 * cw, 1.0 - alpha / A);
}

BiquadCoeffs BiquadCascade::notch(double sr, double f0, double q) NOTA_RT_SAFE {
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    return norm(1.0, -2.0 * cw, 1.0, 1.0 + alpha, -2.0 * cw, 1.0 - alpha);
}

BiquadCoeffs BiquadCascade::allpass(double sr, double f0, double q) NOTA_RT_SAFE {
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    return norm(1.0 - alpha, -2.0 * cw, 1.0 + alpha, 1.0 + alpha, -2.0 * cw, 1.0 - alpha);
}

BiquadCoeffs BiquadCascade::bandpass(double sr, double f0, double q) NOTA_RT_SAFE {
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    return norm(alpha, 0.0, -alpha, 1.0 + alpha, -2.0 * cw, 1.0 - alpha);
}

BiquadCoeffs BiquadCascade::lowShelf(double sr, double f0, double gainDb) NOTA_RT_SAFE {
    const double A = std::pow(10.0, gainDb / 40.0);
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / 2.0 * std::sqrt(2.0);
    const double tsa = 2.0 * std::sqrt(A) * alpha;
    return norm(A * ((A + 1.0) - (A - 1.0) * cw + tsa), 2.0 * A * ((A - 1.0) - (A + 1.0) * cw),
        A * ((A + 1.0) - (A - 1.0) * cw - tsa), (A + 1.0) + (A - 1.0) * cw + tsa, -2.0 * ((A - 1.0) + (A + 1.0) * cw),
        (A + 1.0) + (A - 1.0) * cw - tsa);
}

BiquadCoeffs BiquadCascade::highShelf(double sr, double f0, double gainDb) NOTA_RT_SAFE {
    const double A = std::pow(10.0, gainDb / 40.0);
    const double w0 = 2.0 * 3.14159265358979 * f0 / sr;
    const double cw = std::cos(w0);
    const double sw = std::sin(w0);
    const double alpha = sw / 2.0 * std::sqrt(2.0);
    const double tsa = 2.0 * std::sqrt(A) * alpha;
    return norm(A * ((A + 1.0) + (A - 1.0) * cw + tsa), -2.0 * A * ((A - 1.0) + (A + 1.0) * cw),
        A * ((A + 1.0) + (A - 1.0) * cw - tsa), (A + 1.0) - (A - 1.0) * cw + tsa, 2.0 * ((A - 1.0) - (A + 1.0) * cw),
        (A + 1.0) - (A - 1.0) * cw - tsa);
}

BiquadCoeffs BiquadCascade::fromType(BiquadType t, double sr, double f0, double gainDb, double q) NOTA_RT_SAFE {
    switch (t) {
    case BiquadType::LowPass: return lowpass(sr, f0, q);
    case BiquadType::HighPass: return highpass(sr, f0, q);
    case BiquadType::Bell: return bell(sr, f0, gainDb, q);
    case BiquadType::LowShelf: return lowShelf(sr, f0, gainDb);
    case BiquadType::HighShelf: return highShelf(sr, f0, gainDb);
    case BiquadType::Notch: return notch(sr, f0, q);
    case BiquadType::Allpass: return allpass(sr, f0, q);
    case BiquadType::BandPass: return bandpass(sr, f0, q);
    }
    return {};
}

int32_t BiquadCascade::butterworthQs(double* out, int32_t slope) NOTA_RT_SAFE {
    if (out == nullptr) return 0;
    if (slope == 1) {
        out[0] = 0.54119610;
        out[1] = 1.30656296;
        return 2;
    }
    if (slope >= 2) {
        out[0] = 0.50979558;
        out[1] = 0.60134489;
        out[2] = 0.89997622;
        out[3] = 2.56291545;
        return 4;
    }
    return 0;
}

} // namespace nota::dsp
