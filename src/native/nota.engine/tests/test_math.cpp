// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/math/BufferOps.h"
#include "nota/math/ComplexOps.h"
#include "nota/math/CpuFeatures.h"
#include "nota/math/FastMath.h"
#include "nota/math/Fft.h"
#include "nota/math/Random.h"
#include "nota/math/SimdVec.h"
#include "nota/math/StereoOps.h"

#include <cmath>
#include <complex>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <limits>
#include <numbers>
#include <vector>

namespace {

#define CHECK(cond)                                                                                                    \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);                                       \
            return 1;                                                                                                  \
        }                                                                                                              \
    } while (0)

int testCpuFeaturesAndDenormals() {
    const auto caps = nota::math::detectCpuCapabilities();
#if defined(__aarch64__) || defined(_M_ARM64)
    CHECK(caps.neon);
#elif defined(__x86_64__) || defined(_M_X64)
    CHECK(caps.sse2);
#endif
    {
        nota::math::ScopedNoDenormals guard;
        nota::math::cpuPause();
        volatile float tiny = 1.0e-42f;
        volatile float x = tiny + 0.0f;
        (void)x;
    }
    return 0;
}

int testBufferOpsAndSilence() {
    for (int32_t n : {0, 1, 3, 4, 7, 17, 65}) {
        std::vector<float> a(static_cast<size_t>(n));
        std::vector<float> b(static_cast<size_t>(n));
        std::vector<float> out(static_cast<size_t>(n));

        for (int32_t i = 0; i < n; ++i) {
            a[static_cast<size_t>(i)] = static_cast<float>(i - 10) * 0.125f;
            b[static_cast<size_t>(i)] = static_cast<float>((i % 7) - 3) * 0.25f;
        }

        nota::math::copy(out.data(), a.data(), n);
        nota::math::addScaled(out.data(), b.data(), 0.5f, n);
        for (int32_t i = 0; i < n; ++i) {
            const float expected = a[static_cast<size_t>(i)] + b[static_cast<size_t>(i)] * 0.5f;
            CHECK(std::fabs(out[static_cast<size_t>(i)] - expected) < 1e-6f);
        }

        nota::math::clip(out.data(), out.data(), -0.5f, 0.5f, n);
        for (float v : out) CHECK(v >= -0.5f && v <= 0.5f);

        nota::math::clear(out);
        CHECK(nota::math::isSilent(out));

        if (n >= 5) {
            out[static_cast<size_t>(n - 1)] = 0.01f;
            CHECK(!nota::math::isSilent(out));
            out[static_cast<size_t>(n - 1)] = 0.0f;
            out[3] = std::numeric_limits<float>::quiet_NaN();
            CHECK(!nota::math::isSilent(out));
            nota::math::squashNonFinite(out.data(), n);
            CHECK(nota::math::isSilent(out));
        }
    }

    float pk = 0.0f;
    double sq = 0.0;
    std::vector<float> v{0.5f, -1.5f, 0.25f, -0.75f, 2.0f};
    nota::math::peakAndSumSquares(v.data(), static_cast<int32_t>(v.size()), pk, sq);
    CHECK(std::fabs(pk - 2.0f) < 1e-6f);
    CHECK(std::fabs(sq - (0.25 + 2.25 + 0.0625 + 0.5625 + 4.0)) < 1e-5);
    return 0;
}

int testStereoAndComplexOps() {
    for (int32_t frames : {1, 3, 4, 9, 35}) {
        std::vector<float> interleaved(static_cast<size_t>(frames * 2), 0.0f);
        std::vector<float> l(static_cast<size_t>(frames));
        std::vector<float> r(static_cast<size_t>(frames));
        for (int32_t i = 0; i < frames; ++i) {
            l[static_cast<size_t>(i)] = static_cast<float>(i + 1) * 0.01f;
            r[static_cast<size_t>(i)] = -static_cast<float>(i + 1) * 0.02f;
        }
        nota::math::interleave(interleaved.data(), l.data(), r.data(), frames);
        std::vector<float> dl(static_cast<size_t>(frames), 0.0f);
        std::vector<float> dr(static_cast<size_t>(frames), 0.0f);
        nota::math::deinterleave(dl.data(), dr.data(), interleaved.data(), frames);
        for (int32_t i = 0; i < frames; ++i) {
            CHECK(std::fabs(dl[static_cast<size_t>(i)] - l[static_cast<size_t>(i)]) < 1e-6f);
            CHECK(std::fabs(dr[static_cast<size_t>(i)] - r[static_cast<size_t>(i)]) < 1e-6f);
        }

        float pkL = 0.0f, pkR = 0.0f;
        double sqL = 0.0, sqR = 0.0;
        nota::math::scaleStereoMeter(interleaved.data(), interleaved.data(), 0.5f, 0.25f, frames, pkL, pkR, sqL, sqR);
        CHECK(std::fabs(pkL - static_cast<float>(frames) * 0.01f * 0.5f) < 1e-5f);
        CHECK(std::fabs(pkR - static_cast<float>(frames) * 0.02f * 0.25f) < 1e-5f);
    }

    float ar[5]{}, ai[5]{};
    const float xr[5]{2.0f, 1.0f, 0.5f, -1.0f, 0.25f};
    const float xi[5]{3.0f, -2.0f, 1.0f, 0.5f, -0.5f};
    const float hr[5]{4.0f, 0.5f, -1.0f, 2.0f, 1.0f};
    const float hi[5]{5.0f, 1.5f, 0.25f, -1.0f, 0.5f};
    nota::math::accumulatePackedSpectrum(ar, ai, xr, xi, hr, hi, 5);
    CHECK(std::fabs(ar[0] - 8.0f) < 1e-6f);
    CHECK(std::fabs(ai[0] - 15.0f) < 1e-6f);
    CHECK(std::fabs(ar[1] - (1.0f * 0.5f - (-2.0f) * 1.5f)) < 1e-6f);
    CHECK(std::fabs(ai[1] - (1.0f * 1.5f + (-2.0f) * 0.5f)) < 1e-6f);
    return 0;
}

int testFastMathFftAndWindowing() {
    CHECK(std::fabs(nota::math::dbToLin(0.0f) - 1.0f) < 1e-4f);
    CHECK(std::fabs(nota::math::linToDb(2.0f) - 6.0206f) < 1e-3f);
    const nota::math::Vec4f vx = nota::math::Vec4f::set(-12.0f, -6.0f, 0.0f, 6.0f);
    alignas(16) float vOut[4]{};
    nota::math::linToDb(nota::math::dbToLin(vx)).storeAligned(vOut);
    for (int i = 0; i < 4; ++i) CHECK(std::fabs(vOut[i] - (-12.0f + 6.0f * static_cast<float>(i))) < 1e-2f);

    nota::math::Rng rng(42);
    for (int i = 0; i < 64; ++i) {
        const float b = rng.nextBipolar();
        CHECK(b >= -1.0f && b <= 1.0f);
    }

    constexpr int32_t kN = 128;
    std::vector<float> win(kN);
    nota::math::Windowing::fillHann(win.data(), kN, true);
    CHECK(std::fabs(win[0]) < 1e-6f);
    CHECK(std::fabs(win[kN / 2] - 1.0f) < 1e-6f);
    nota::math::Windowing::fillBlackman(win.data(), kN, false);
    CHECK(std::fabs(win[0]) < 1e-5f);
    CHECK(std::fabs(win[kN - 1]) < 1e-5f);

    std::vector<float> sinc(63);
    nota::math::Windowing::fillKaiserSinc(sinc.data(), 63, 0.45, 7.0);
    double sincSum = 0.0;
    for (float c : sinc) sincSum += c;
    CHECK(std::fabs(sincSum - 1.0) < 1e-5);

    nota::math::RealFft fft(kN);
    CHECK(fft.size() == kN);
    CHECK(fft.halfSize() == kN / 2);
    CHECK(nota::math::RealFft::fastSizeAbove(100) >= 100);

    std::vector<float> in(kN);
    std::vector<float> re(kN / 2), im(kN / 2), back(kN);
    for (int32_t i = 0; i < kN; ++i) {
        in[static_cast<size_t>(i)] =
            0.7f * std::sin(2.0f * std::numbers::pi_v<float> * 5.0f * static_cast<float>(i) / static_cast<float>(kN)) +
            0.2f;
    }
    fft.forward(in.data(), re.data(), im.data());
    fft.inverse(re.data(), im.data(), back.data());
    for (int32_t i = 0; i < kN; ++i) {
        CHECK(std::fabs(back[static_cast<size_t>(i)] / static_cast<float>(kN) - in[static_cast<size_t>(i)]) < 1e-4f);
    }

    std::vector<std::complex<float>> packed(kN / 2);
    fft.forwardPacked(in.data(), packed.data());
    fft.inversePacked(packed.data(), back.data());
    for (int32_t i = 0; i < kN; ++i) {
        CHECK(std::fabs(back[static_cast<size_t>(i)] / static_cast<float>(kN) - in[static_cast<size_t>(i)]) < 1e-4f);
    }
    return 0;
}

} // namespace

int main() {
    if (testCpuFeaturesAndDenormals() != 0) return 1;
    if (testBufferOpsAndSilence() != 0) return 2;
    if (testStereoAndComplexOps() != 0) return 3;
    if (testFastMathFftAndWindowing() != 0) return 4;
    std::printf("test_math: all checks passed\n");
    return 0;
}
