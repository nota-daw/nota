// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "effects/color/Color.h"
#include <nota/math/CpuFeatures.h>

#if defined(NOTA_SIMD_NEON)
#include <arm_neon.h>
#elif defined(NOTA_SIMD_FMA)
#include <immintrin.h>
#endif

#include <algorithm>
#include <atomic>
#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <cstddef>
#include <complex>
#include <numbers>
#include <utility>
#include <vector>

namespace nota {
namespace color_detail {
constexpr double tau = 2 * std::numbers::pi;
constexpr double butterworthQ = std::numbers::sqrt2 / 2;
constexpr double inputNotchQ = 16, rejectNotchQ = 12.5;
constexpr float silence = 3.16227766e-5f;               // -90 dBFS peak.
constexpr double resonanceCurve = 1.736965594166206;    // log(.3) / log(.5).
constexpr double compensationCurve = .6611980865482415; // log(21.5 / 34) / log(.5).

inline float clean(float value) {
    return std::isfinite(value) && std::abs(value) > 1e-20f ? value : 0.f;
}

inline float dbToGain(double db) {
    return float(std::pow(10., db / 20));
}

inline float approach(float value, float target, float step) {
    return value + std::clamp(target - value, -step, step);
}

inline double wrapPhase(double phase) {
#if defined(NOTA_SIMD_NEON) || defined(NOTA_SIMD_FMA)
    // The configured hops and ratios keep both phase increments below this bound.
    if (std::abs(phase) < 8192.) {
        const double revolutions = phase * (1 / tau);
#if defined(NOTA_SIMD_NEON)
        const double turns = vget_lane_f64(vrndn_f64(vdup_n_f64(revolutions)), 0);
        const double wrapped = std::fma(-turns, tau, phase);
#else
        const auto turns = _mm_round_sd(_mm_setzero_pd(), _mm_set_sd(revolutions), _MM_FROUND_TO_NEAREST_INT);
        const double wrapped = _mm_cvtsd_f64(_mm_fnmadd_sd(turns, _mm_set_sd(tau), _mm_set_sd(phase)));
#endif
        // Resolve quotient rounding near a half-turn with the exact remainder operation.
        if (std::abs(wrapped) < std::numbers::pi - 1e-11) return wrapped == 0. ? std::copysign(0., phase) : wrapped;
    }
#endif
    return std::remainder(phase, tau);
}

inline float cubic(float a, float b, float c, float d, float frac) {
    const float slope = .5f * (c - a);
    const float curvature = b - c + slope;
    const float cubicTerm = .5f * (d - b) + curvature + b - c;
    return ((cubicTerm * frac - (cubicTerm + curvature)) * frac + slope) * frac + b;
}

template <size_t N> float spectrumPower(const std::array<float, N>& power, double first, double last) {
    constexpr int bins = int(N) - 1; // The last entry is a zero sentinel, outside the plotted FFT bins.
    if (first >= bins) return 0;
    const double center = std::min((first + last) * .5, double(bins - 1));
    const int mid = int(center);
    float peak = std::lerp(power[static_cast<size_t>(mid)], power[static_cast<size_t>(mid + 1)], float(center - mid));
    for (int k = std::max(1, int(std::ceil(first))); k < bins && k <= last; ++k)
        peak = std::max(peak, power[static_cast<size_t>(k)]);
    return peak;
}
} // namespace color_detail

using namespace color_detail;
struct Color::PreShifter {
    struct Frame {
        Bands bands{};
        int mask = 0;
    };
    struct Overlap {
        Stereo sum{};
        float weight = 0;
    };

    void prepare(double sampleRate) {
        // Keep enough frequency resolution for the bottom of Color's band.
        const int size =
            static_cast<int>(std::clamp(std::bit_ceil(unsigned(std::lround(.036 * sampleRate))), 512u, 8192u));
        latency.store(size, std::memory_order_relaxed);
        fft.resize(static_cast<size_t>(size));
        delay.resize(static_cast<size_t>(size));
        window.resize(static_cast<size_t>(size));
        time.resize(static_cast<size_t>(size));
        spectrum.resize(static_cast<size_t>(size / 2));
        overlap.resize(static_cast<size_t>(2 * size));
        for (auto& channel : analysisPhase) channel.resize(static_cast<size_t>(size / 2));
        for (auto& channel : synthesisPhase) channel.resize(static_cast<size_t>(size / 2));
        for (int i = 0; i < size; ++i)
            window[static_cast<size_t>(i)] = float(std::sqrt(.5 - .5 * std::cos(tau * i / size)));
        pole = float(std::exp(-1 / (.015 * sampleRate)));
        reset();
    }

    void reset() {
        std::fill(delay.begin(), delay.end(), Frame{});
        std::fill(overlap.begin(), overlap.end(), Overlap{});
        position = 0;
        cents = blend = 0;
        snap = first = true;
    }

    int delayedMask() const { return delay[position & (delay.size() - 1)].mask; }

    Bands process(Frame frame, float target, PreOutput& pre) {
        const size_t size = delay.size(), hop = size / 4;
        auto delayed = std::exchange(delay[position & (size - 1)], frame);
        auto rendered = std::exchange(overlap[position & (overlap.size() - 1)], {});
        cents = snap || std::abs(cents - target) < .001f ? target : target + pole * (cents - target);
        const float weight = std::min(1.f, 2 * std::abs(cents));
        blend = snap || std::abs(blend - weight) < 1e-5f ? weight : weight + pole * (blend - weight);
        snap = false;
        pre.mix = blend;
        for (int ch = 0; ch < 2; ++ch) {
            const float shifted =
                rendered.weight > .001f ? clean(rendered.sum[static_cast<size_t>(ch)] / rendered.weight) : 0;
            pre.tuned[static_cast<size_t>(ch)] = blend * shifted;
            pre.delta[static_cast<size_t>(ch)] =
                pre.tuned[static_cast<size_t>(ch)] - blend * delayed.bands.mid[static_cast<size_t>(ch)];
            delayed.bands.mid[static_cast<size_t>(ch)] += pre.delta[static_cast<size_t>(ch)];
        }
        if ((++position & (hop - 1)) == 0) synthesize(std::exp2(double(cents) / 1200));
        return delayed.bands;
    }

    void synthesize(double ratio) {
        const int size = int(delay.size()), bins = size / 2;
        const float normalization = 1.f / static_cast<float>(size);
        // Re-sample each synthesis frame at the exact ratio. Re-weight the
        // overlap because the resampled frames have different lengths.
        for (int ch = 0; ch < 2; ++ch) {
            for (int i = 0; i < size; ++i)
                time[static_cast<size_t>(i)] =
                    delay[(position + static_cast<uint64_t>(i)) & (static_cast<uint64_t>(size - 1))]
                        .bands.mid[static_cast<size_t>(ch)] *
                    window[static_cast<size_t>(i)];
            fft.fft(time.data(), spectrum.data());
            spectrum[0] = {}; // DC and Nyquist are outside the processed band.
            for (int k = 1; k < bins; ++k) {
                const double phase = std::arg(spectrum[static_cast<size_t>(k)]);
                if (first) synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] = phase;
                else {
                    const double advance = (tau / 4) * k;
                    const double delta =
                        wrapPhase(phase - analysisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] - advance);
                    synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] = wrapPhase(
                        synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] + ratio * (advance + delta));
                }
                analysisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] = phase;
                spectrum[static_cast<size_t>(k)] =
                    k * ratio < bins ? std::polar(std::abs(spectrum[static_cast<size_t>(k)]),
                                           float(synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)]))
                                     : Complex{};
            }
            fft.ifft(spectrum.data(), time.data());
            for (int i = 0; i < size; ++i) time[static_cast<size_t>(i)] *= window[static_cast<size_t>(i)];
            for (int i = 0, length = int(std::ceil(size / ratio)); i < length; ++i) {
                const double read = i * ratio;
                const int j = std::min(int(read), size - 1), next = std::min(j + 1, size - 1);
                const float fraction = float(read - j);
                auto& out = overlap[(position + static_cast<uint64_t>(i)) & (overlap.size() - 1)];
                out.sum[static_cast<size_t>(ch)] +=
                    normalization * cubic(time[static_cast<size_t>(std::max(0, j - 1))], time[static_cast<size_t>(j)],
                                        time[static_cast<size_t>(next)],
                                        time[static_cast<size_t>(std::min(j + 2, size - 1))], fraction);
                if (ch == 0)
                    out.weight += std::lerp(window[static_cast<size_t>(j)] * window[static_cast<size_t>(j)],
                        window[static_cast<size_t>(next)] * window[static_cast<size_t>(next)], fraction);
            }
        }
        first = false;
    }

    FFT fft;
    std::atomic<int> latency{0};
    std::vector<Frame> delay;
    std::vector<Overlap> overlap;
    std::vector<float> window, time;
    std::vector<Complex> spectrum;
    std::array<std::vector<double>, 2> analysisPhase, synthesisPhase;
    uint64_t position = 0;
    float pole = 0, cents = 0, blend = 0;
    bool snap = true, first = true;
};

} // namespace nota
