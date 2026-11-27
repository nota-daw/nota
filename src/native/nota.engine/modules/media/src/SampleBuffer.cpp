// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstddef>

namespace nota::media {

int64_t SampleBuffer::nextId() {
    static std::atomic<int64_t> counter{1};
    return counter.fetch_add(1, std::memory_order_relaxed);
}

void SampleBuffer::computeBlockPeakAndMeanSq(const float* interleaved, int32_t channels, int64_t frameCount,
    float& outMin, float& outMax, float& outMeanSq) noexcept {
    if (!interleaved || channels <= 0 || frameCount <= 0) {
        outMin = 0.0f;
        outMax = 0.0f;
        outMeanSq = 0.0f;
        return;
    }
    float mn = 1.0f;
    float mx = -1.0f;
    double sq = 0.0;
    for (int64_t f = 0; f < frameCount; ++f) {
        const float* p = interleaved + static_cast<size_t>(f) * static_cast<size_t>(channels);
        const float l = p[0];
        const float r = channels >= 2 ? p[1] : l;
        const float mid = 0.5f * (l + r);
        mn = std::min(mn, mid);
        mx = std::max(mx, mid);
        const double dmid = 0.5 * (static_cast<double>(l) + static_cast<double>(r));
        sq += dmid * dmid;
    }
    outMin = mn;
    outMax = mx;
    outMeanSq = static_cast<float>(sq / static_cast<double>(frameCount));
}

void SampleBuffer::readStereoFromSource(int64_t frame, float& l, float& r) const {
    if (source) {
        source->readStereo(frame, l, r);
        return;
    }
    l = r = 0.0f;
}

int64_t SampleBuffer::readAllInterleaved(float* out, int64_t capFloats) const {
    if (!samples.empty()) {
        const int64_t total = static_cast<int64_t>(samples.size());
        if (out && capFloats > 0) std::copy_n(samples.data(), std::min(total, capFloats), out);
        return total;
    }
    return source ? source->readAllInterleaved(out, capFloats) : 0;
}

void SampleBuffer::buildPeakTable() {
    updatePeakTable(0, frames);
}

void SampleBuffer::updatePeakTable(int64_t f0, int64_t f1) {
    if (!hasPeakTable() || !hasRmsTable()) {
        peakTable.assign(static_cast<size_t>(peakBlocks() * 2), 0.0f);
        rmsTable.assign(static_cast<size_t>(peakBlocks()), 0.0f);
        fillSentinel(0, peakBlocks());
    }
    f0 = std::max<int64_t>(0, f0);
    f1 = std::min(f1, frames);
    if (f1 <= f0) return;
    for (int64_t b = f0 / kPeakBlock; b <= (f1 - 1) / kPeakBlock; ++b) {
        const int64_t s0 = b * kPeakBlock;
        const int64_t s1 = std::min(frames, (b + 1) * kPeakBlock);
        float mn = 0.0f;
        float mx = 0.0f;
        scanRange(s0, s1, mn, mx);
        peakTable[static_cast<size_t>(b) * 2] = mn;
        peakTable[static_cast<size_t>(b) * 2 + 1] = mx;
        rmsTable[static_cast<size_t>(b)] = static_cast<float>(scanSquares(s0, s1) / static_cast<double>(s1 - s0));
    }
}

void SampleBuffer::fillSentinel(int64_t b0, int64_t b1) {
    for (int64_t b = b0; b < b1; ++b) {
        peakTable[static_cast<size_t>(b) * 2] = 1.0f;
        peakTable[static_cast<size_t>(b) * 2 + 1] = -1.0f;
        if (hasRmsTable()) rmsTable[static_cast<size_t>(b)] = 0.0f;
    }
}

void SampleBuffer::peakRange(int64_t f0, int64_t f1, float& mn, float& mx) const {
    f0 = std::max<int64_t>(0, f0);
    f1 = std::min(f1, frames);
    mn = 1.0f;
    mx = -1.0f;
    if (f1 <= f0) {
        mn = mx = 0.0f;
        return;
    }
    if (f1 - f0 < 4 * kPeakBlock || !hasPeakTable()) {
        scanRange(f0, f1, mn, mx);
        return;
    }
    const int64_t b0 = (f0 + kPeakBlock - 1) / kPeakBlock;
    const int64_t b1 = f1 / kPeakBlock;
    float a = 0.0f;
    float z = 0.0f;
    scanRange(f0, b0 * kPeakBlock, a, z);
    mn = std::min(mn, a);
    mx = std::max(mx, z);
    for (int64_t b = b0; b < b1; ++b) {
        mn = std::min(mn, peakTable[static_cast<size_t>(b) * 2]);
        mx = std::max(mx, peakTable[static_cast<size_t>(b) * 2 + 1]);
    }
    scanRange(b1 * kPeakBlock, f1, a, z);
    mn = std::min(mn, a);
    mx = std::max(mx, z);
    if (mn > mx) mn = mx = 0.0f;
}

float SampleBuffer::rmsRange(int64_t f0, int64_t f1) const {
    f0 = std::max<int64_t>(0, f0);
    f1 = std::min(f1, frames);
    if (f1 <= f0) return 0.0f;
    double sum = 0.0;
    if (f1 - f0 < 4 * kPeakBlock || !hasRmsTable()) {
        sum = scanSquares(f0, f1);
    } else {
        const int64_t b0 = (f0 + kPeakBlock - 1) / kPeakBlock;
        const int64_t b1 = f1 / kPeakBlock;
        sum = scanSquares(f0, b0 * kPeakBlock) + scanSquares(b1 * kPeakBlock, f1);
        for (int64_t b = b0; b < b1; ++b) sum += static_cast<double>(rmsTable[static_cast<size_t>(b)]) * kPeakBlock;
    }
    return static_cast<float>(std::sqrt(sum / static_cast<double>(f1 - f0)));
}

double SampleBuffer::scanSquares(int64_t f0, int64_t f1) const {
    double sum = 0.0;
    int32_t hint = -1;
    for (int64_t f = f0; f < f1; ++f) {
        float l = 0.0f;
        float r = 0.0f;
        readStereoFast(f, hint, true, l, r);
        const double mid = 0.5 * (static_cast<double>(l) + r);
        sum += mid * mid;
    }
    return sum;
}

void SampleBuffer::scanRange(int64_t f0, int64_t f1, float& mn, float& mx) const {
    mn = 1.0f;
    mx = -1.0f;
    int32_t hint = -1;
    for (int64_t f = f0; f < f1; ++f) {
        float l = 0.0f;
        float r = 0.0f;
        readStereoFast(f, hint, true, l, r);
        const float mid = 0.5f * (l + r);
        mn = std::min(mn, mid);
        mx = std::max(mx, mid);
    }
}

} // namespace nota::media
