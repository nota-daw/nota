// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SampleBuffer implementation — peak-table math

#include "SampleBuffer.h"

namespace nota {

int64_t SampleBuffer::nextId() {
    static std::atomic<int64_t> counter{1};
    return counter.fetch_add(1, std::memory_order_relaxed);
}

void SampleBuffer::readStereo(int64_t frame, float& l, float& r) const {
    if (frame < 0 || frame >= frames) {
        l = r = 0.0f;
        return;
    }
    const float* p = &samples[frame * channels];
    if (channels == 1) {
        l = r = p[0];
    } else {
        l = p[0];
        r = p[1];
    }
}

void SampleBuffer::buildPeakTable() {
    updatePeakTable(0, frames);
}

void SampleBuffer::updatePeakTable(int64_t f0, int64_t f1) {
    if (!hasPeakTable()) {
        peakTable.assign(static_cast<size_t>(peakBlocks() * 2), 0.0f);
        fillSentinel(0, peakBlocks());
    }
    f0 = std::max<int64_t>(0, f0);
    f1 = std::min(f1, frames);
    if (f1 <= f0) return;
    for (int64_t b = f0 / kPeakBlock; b <= (f1 - 1) / kPeakBlock; ++b) {
        float mn, mx;
        scanRange(b * kPeakBlock, std::min(frames, (b + 1) * kPeakBlock), mn, mx);
        peakTable[b * 2] = mn;
        peakTable[b * 2 + 1] = mx;
    }
}

void SampleBuffer::fillSentinel(int64_t b0, int64_t b1) {
    for (int64_t b = b0; b < b1; ++b) {
        peakTable[b * 2] = 1.0f;
        peakTable[b * 2 + 1] = -1.0f;
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
    const int64_t b0 = (f0 + kPeakBlock - 1) / kPeakBlock, b1 = f1 / kPeakBlock; // whole blocks
    float a, z;
    scanRange(f0, b0 * kPeakBlock, a, z);
    mn = std::min(mn, a);
    mx = std::max(mx, z);
    for (int64_t b = b0; b < b1; ++b) {
        mn = std::min(mn, peakTable[b * 2]);
        mx = std::max(mx, peakTable[b * 2 + 1]);
    }
    scanRange(b1 * kPeakBlock, f1, a, z);
    mn = std::min(mn, a);
    mx = std::max(mx, z);
    if (mn > mx) mn = mx = 0.0f;
}

void SampleBuffer::scanRange(int64_t f0, int64_t f1, float& mn, float& mx) const {
    mn = 1.0f;
    mx = -1.0f;
    for (int64_t f = f0; f < f1; ++f) {
        float l, r;
        readStereo(f, l, r);
        const float mid = 0.5f * (l + r);
        mn = std::min(mn, mid);
        mx = std::max(mx, mid);
    }
}

} // namespace nota
