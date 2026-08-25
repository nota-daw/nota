// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "TempoDetect.h"

#include <algorithm>
#include <cmath>
#include <vector>

namespace nota {

namespace {

constexpr double kHopSec = 0.01;     // 10 ms novelty frame
constexpr double kMinBpm = 70.0;     // search range (folded by the prior)
constexpr double kMaxBpm = 180.0;
constexpr double kBpmStep = 0.5;
constexpr double kPriorCenter = 120.0;  // preferred tempo (log2 Gaussian)
constexpr double kPriorWidth = 0.9;
constexpr double kCrestMin = 5.0;       // min peak/mean novelty ratio to trust the estimate

// Onset-novelty envelope shared by detectTempo/detectTransients: per-hop log energy
// (mono), then half-wave-rectified first difference. Log compression keeps loud and
// quiet passages comparable. Returns the hop size, or 0 when the region is too short.
int64_t buildNovelty(const SampleBuffer& src, int64_t offset, int64_t len,
                     double sampleRate, std::vector<double>& novelty) {
    const int64_t hop = std::max<int64_t>(1, static_cast<int64_t>(std::llround(sampleRate * kHopSec)));
    const int64_t frameCount = len / hop;
    if (frameCount < 64) return 0;

    std::vector<double> energy(static_cast<size_t>(frameCount), 0.0);
    for (int64_t f = 0; f < frameCount; ++f) {
        const int64_t s0 = offset + f * hop;
        double acc = 0.0;
        for (int64_t i = 0; i < hop; ++i) {
            double mono = 0.0;
            for (int c = 0; c < src.channels; ++c) mono += src.at(s0 + i, c);
            mono /= src.channels;
            acc += mono * mono;
        }
        energy[static_cast<size_t>(f)] = std::log(1.0 + acc / hop);
    }

    novelty.assign(static_cast<size_t>(frameCount), 0.0);
    for (int64_t f = 1; f < frameCount; ++f) {
        double d = energy[static_cast<size_t>(f)] - energy[static_cast<size_t>(f - 1)];
        novelty[static_cast<size_t>(f)] = d < 0.0 ? 0.0 : d;
    }
    return hop;
}

} // namespace

double detectTempo(const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate) {
    if (src.channels <= 0 || sampleRate <= 0.0 || len <= 0) return 0.0;
    offset = std::max<int64_t>(0, offset);
    len = std::min<int64_t>(len, src.frames - offset);
    if (len <= 0) return 0.0;

    std::vector<double> novelty;
    const int64_t hop = buildNovelty(src, offset, len, sampleRate, novelty);
    if (hop <= 0) return 0.0;
    const int64_t frameCount = static_cast<int64_t>(novelty.size());

    double nmax = 0.0;
    for (double v : novelty) nmax = std::max(nmax, v);
    if (nmax <= 1e-9) return 0.0;   // silence / no transients

    // Reject non-percussive material (e.g. a sustained tone whose windowed energy
    // only ripples): real beats give sharp, sparse onset peaks — a high peak/mean
    // crest factor — while smooth signals stay near-uniform.
    double mean = 0.0;
    for (double v : novelty) mean += v;
    mean /= static_cast<double>(frameCount);
    if (nmax < kCrestMin * (mean + 1e-12)) return 0.0;

    // Remove the DC component so the autocorrelation reflects periodicity, not
    // overall onset density.
    for (auto& v : novelty) v -= mean;

    // Autocorrelation is only needed at the lags that map to the tempo range.
    const double hopSec = static_cast<double>(hop) / sampleRate;
    auto autocorrAt = [&](double lag) -> double {
        const int64_t l0 = static_cast<int64_t>(lag);
        const double frac = lag - l0;
        auto raw = [&](int64_t l) -> double {
            if (l <= 0 || l >= frameCount) return 0.0;
            double acc = 0.0;
            for (int64_t f = l; f < frameCount; ++f)
                acc += novelty[static_cast<size_t>(f)] * novelty[static_cast<size_t>(f - l)];
            return acc / static_cast<double>(frameCount - l);
        };
        return raw(l0) * (1.0 - frac) + raw(l0 + 1) * frac;  // interpolate fractional lag
    };

    double bestBpm = 0.0, bestScore = -1.0;
    for (double bpm = kMinBpm; bpm <= kMaxBpm + 1e-9; bpm += kBpmStep) {
        const double lag = (60.0 / bpm) / hopSec;   // frames per beat
        if (lag < 2.0 || lag >= frameCount) continue;
        const double lg = std::log2(bpm / kPriorCenter) / kPriorWidth;
        const double prior = std::exp(-0.5 * lg * lg);
        const double score = autocorrAt(lag) * prior;
        if (score > bestScore) { bestScore = score; bestBpm = bpm; }
    }
    if (bestScore <= 0.0) return 0.0;
    return bestBpm;
}

int32_t detectTransients(const SampleBuffer& src, int64_t offset, int64_t len,
                         double sampleRate, double* outSrcFrames, int32_t maxCount) {
    if (!outSrcFrames || maxCount <= 0 || src.channels <= 0 || sampleRate <= 0.0 || len <= 0) return 0;
    offset = std::max<int64_t>(0, offset);
    len = std::min<int64_t>(len, src.frames - offset);
    if (len <= 0) return 0;

    std::vector<double> novelty;
    const int64_t hop = buildNovelty(src, offset, len, sampleRate, novelty);
    if (hop <= 0) return 0;
    const int64_t frameCount = static_cast<int64_t>(novelty.size());

    // Adaptive peak-pick: mean + k·std over the whole envelope, local maxima only,
    // with a minimum inter-onset gap so a single hit isn't split into many markers.
    double mean = 0.0;
    for (double v : novelty) mean += v;
    mean /= static_cast<double>(frameCount);
    double var = 0.0;
    for (double v : novelty) { const double d = v - mean; var += d * d; }
    var /= static_cast<double>(frameCount);
    const double thresh = mean + 0.8 * std::sqrt(var);
    if (thresh <= 1e-9) return 0;

    const int64_t minGap = std::max<int64_t>(1, static_cast<int64_t>(std::llround(0.05 * sampleRate / hop)));
    int32_t count = 0;
    int64_t lastF = -minGap - 1;
    for (int64_t f = 1; f + 1 < frameCount && count < maxCount; ++f) {
        const double v = novelty[static_cast<size_t>(f)];
        if (v > thresh && v >= novelty[static_cast<size_t>(f - 1)] && v > novelty[static_cast<size_t>(f + 1)]
            && f - lastF > minGap) {
            outSrcFrames[count++] = static_cast<double>(offset + f * hop);
            lastF = f;
        }
    }
    return count;
}

} // namespace nota
