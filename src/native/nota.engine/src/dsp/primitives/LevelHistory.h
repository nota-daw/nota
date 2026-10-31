// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A level history for an effect card's live graph: two rings of peak dB (input and wet) at a
// fixed 4 ms bucket, about six seconds of each, plus a ring of the latest mono wet samples
// for a spectrum. The audio thread writes it inside process() and publishes the write
// positions once per block; scopeRead() appends it to the scope, oldest first. Like the
// Compressor's envelope rings, a read can race a write by a point or two, which a graph
// never shows. Fixed size, no allocation.

#pragma once

#include "dsp/math/FastMath.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota::dsp {

class LevelHistory {
  public:
    static constexpr int kPoints = 1536;      // 6.1 s at 4 ms per point
    static constexpr int kSpecN = 2048;       // wet samples for the card's spectrum
    static constexpr double kBucketSec = 0.004;
    static constexpr int kFloats = 2 * kPoints + kSpecN;
    static constexpr float kFloorDb = -120.0f;

    void setSampleRate(double sr) {
        bucket_ = std::max(1, static_cast<int>(std::lround(sr * kBucketSec)));
        reset();
    }

    void reset() {
        std::fill(std::begin(in_), std::end(in_), kFloorDb);
        std::fill(std::begin(wet_), std::end(wet_), kFloorDb);
        std::fill(std::begin(spec_), std::end(spec_), 0.0f);
        w_ = s_ = cnt_ = 0;
        pkIn_ = pkWet_ = 0.0f;
        wA_.store(0, std::memory_order_relaxed);
        sA_.store(0, std::memory_order_relaxed);
    }

    // One sample: the input and wet magnitudes (any stereo fold the caller likes) and the mono wet.
    void push(float inAbs, float wetAbs, float wetMono) {
        spec_[s_] = std::isfinite(wetMono) ? wetMono : 0.0f;
        if (++s_ >= kSpecN) s_ = 0;
        pkIn_ = std::max(pkIn_, inAbs);
        pkWet_ = std::max(pkWet_, wetAbs);
        if (++cnt_ >= bucket_) close();
    }

    // A block the device skipped (asleep on silence): the graph keeps scrolling through it.
    void silence(int32_t frames) {
        for (int32_t i = 0, n = std::min<int32_t>(frames, kSpecN); i < n; ++i) {
            spec_[s_] = 0.0f;
            if (++s_ >= kSpecN) s_ = 0;
        }
        cnt_ += frames;
        while (cnt_ >= bucket_) {
            cnt_ -= bucket_;
            in_[w_] = wet_[w_] = kFloorDb;
            if (++w_ >= kPoints) w_ = 0;
        }
        publish();
    }

    void publish() {
        wA_.store(w_, std::memory_order_relaxed);
        sA_.store(s_, std::memory_order_relaxed);
    }

    // Appends input dB[kPoints], wet dB[kPoints] and wet samples[kSpecN], each oldest first.
    // Returns the floats written (0 when out is too small).
    int32_t read(float* out, int32_t room) const {
        if (!out || room < kFloats) return 0;
        const int w = wA_.load(std::memory_order_relaxed), s = sA_.load(std::memory_order_relaxed);
        for (int k = 0; k < kPoints; ++k) {
            const int i = (w + k) % kPoints;
            out[k] = in_[i];
            out[kPoints + k] = wet_[i];
        }
        for (int k = 0; k < kSpecN; ++k) out[2 * kPoints + k] = spec_[(s + k) % kSpecN];
        return kFloats;
    }

  private:
    static float toDb(float lin) { return lin > 1.0e-6f ? std::max(kFloorDb, fast_math::linToDb(lin)) : kFloorDb; }
    void close() {
        in_[w_] = toDb(pkIn_);
        wet_[w_] = toDb(pkWet_);
        if (++w_ >= kPoints) w_ = 0;
        cnt_ = 0;
        pkIn_ = pkWet_ = 0.0f;
    }

    float in_[kPoints] = {}, wet_[kPoints] = {}, spec_[kSpecN] = {};
    int w_ = 0, s_ = 0, cnt_ = 0, bucket_ = 176;
    float pkIn_ = 0.0f, pkWet_ = 0.0f;
    std::atomic<int> wA_{0}, sA_{0};
};

} // namespace nota::dsp
