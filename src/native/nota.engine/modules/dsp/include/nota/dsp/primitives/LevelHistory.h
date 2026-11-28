// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// LevelHistory — lock-free scrolling input/wet level rings and spectrum history for effect cards.

#pragma once

#include "nota/math/FastMath.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {

class LevelHistory {
  public:
    static constexpr int32_t kPoints = 1536;
    static constexpr int32_t kSpecN = 2048;
    static constexpr double kBucketSec = 0.004;
    static constexpr int32_t kFloats = 2 * kPoints + kSpecN;
    static constexpr float kFloorDb = -120.0f;
    static constexpr int64_t kStaleNs = 60'000'000;
    static constexpr int64_t kWatchNs = 1'000'000'000;

    void setSampleRate(double sr) noexcept {
        sr_ = sr > 0.0 ? sr : 48000.0;
        bucket_ = std::max<int32_t>(1, static_cast<int32_t>(std::lround(sr_ * kBucketSec)));
        reset();
    }

    void reset() noexcept {
        for (auto& v : in_) v.store(kFloorDb, std::memory_order_relaxed);
        for (auto& v : wet_) v.store(kFloorDb, std::memory_order_relaxed);
        for (auto& v : spec_) v.store(0.0f, std::memory_order_relaxed);
        w_ = s_ = cnt_ = 0;
        since_ = 0;
        pkIn_ = pkWet_ = 0.0f;
        wA_.store(0, std::memory_order_relaxed);
        sA_.store(0, std::memory_order_relaxed);
        pubNs_.store(0, std::memory_order_relaxed);
        staleNs_.store(kStaleNs, std::memory_order_relaxed);
        watched_ = false;
    }

    void push(float inAbs, float wetAbs, float wetMono) noexcept {
        ++since_;
        spec_[static_cast<std::size_t>(s_)].store(std::isfinite(wetMono) ? wetMono : 0.0f, std::memory_order_relaxed);
        if (++s_ >= kSpecN) s_ = 0;
        pkIn_ = std::max(pkIn_, inAbs);
        pkWet_ = std::max(pkWet_, wetAbs);
        if (++cnt_ >= bucket_) close();
    }

    bool begin() noexcept {
        const int64_t now = nowNs();
        watched_ = (now - readNs_.load(std::memory_order_relaxed)) < kWatchNs;
        if (!watched_) return false;
        const int64_t pub = pubNs_.load(std::memory_order_relaxed);
        if (pub <= 0) return true;
        const int64_t gap = now - pub;
        if (gap <= staleNs_.load(std::memory_order_relaxed)) return true;
        const double frames = std::min(
            static_cast<double>(gap) * 1e-9 * sr_, static_cast<double>(kPoints + 1) * static_cast<double>(bucket_));
        fill(static_cast<int32_t>(frames));
        since_ = 0;
        return true;
    }

    void silence(int32_t frames) noexcept {
        if (!watched_) return;
        fill(frames);
        since_ += static_cast<int64_t>(frames);
        publish();
    }

    void publish() noexcept {
        if (!watched_) return;
        const auto blockNs = static_cast<int64_t>(static_cast<double>(since_) / sr_ * 1e9);
        staleNs_.store(std::max(kStaleNs, 3 * blockNs), std::memory_order_relaxed);
        since_ = 0;
        wA_.store(w_, std::memory_order_relaxed);
        sA_.store(s_, std::memory_order_relaxed);
        pubNs_.store(nowNs(), std::memory_order_release);
    }

    int32_t read(float* out, int32_t room) const noexcept {
        if (!out || room < kFloats) return 0;
        readNs_.store(nowNs(), std::memory_order_relaxed);
        const int64_t pub = pubNs_.load(std::memory_order_acquire);
        const int32_t w = wA_.load(std::memory_order_relaxed);
        const int32_t s = sA_.load(std::memory_order_relaxed);
        const int64_t idle = pub > 0 ? nowNs() - pub : 0;
        const bool stale = idle > staleNs_.load(std::memory_order_relaxed);
        const int32_t skip =
            stale ? static_cast<int32_t>(std::min<int64_t>(kPoints, idle / static_cast<int64_t>(kBucketSec * 1e9))) : 0;
        for (int32_t k = 0; k < kPoints; ++k) {
            if (k >= kPoints - skip) {
                out[k] = out[kPoints + k] = kFloorDb;
                continue;
            }
            const int32_t i = (w + k + skip) % kPoints;
            out[k] = in_[static_cast<std::size_t>(i)].load(std::memory_order_relaxed);
            out[kPoints + k] = wet_[static_cast<std::size_t>(i)].load(std::memory_order_relaxed);
        }
        for (int32_t k = 0; k < kSpecN; ++k) {
            out[2 * kPoints + k] =
                stale ? 0.0f : spec_[static_cast<std::size_t>((s + k) % kSpecN)].load(std::memory_order_relaxed);
        }
        return kFloats;
    }

  private:
    void fill(int32_t frames) noexcept {
        for (int32_t i = 0, n = std::min<int32_t>(frames, kSpecN); i < n; ++i) {
            spec_[static_cast<std::size_t>(s_)].store(0.0f, std::memory_order_relaxed);
            if (++s_ >= kSpecN) s_ = 0;
        }
        cnt_ += frames;
        while (cnt_ >= bucket_) {
            cnt_ -= bucket_;
            in_[static_cast<std::size_t>(w_)].store(kFloorDb, std::memory_order_relaxed);
            wet_[static_cast<std::size_t>(w_)].store(kFloorDb, std::memory_order_relaxed);
            if (++w_ >= kPoints) w_ = 0;
        }
    }

    static int64_t nowNs() noexcept {
        return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch())
            .count();
    }

    static float toDb(float lin) noexcept { return lin > 1.0e-6f ? std::max(kFloorDb, math::linToDb(lin)) : kFloorDb; }

    void close() noexcept {
        in_[static_cast<std::size_t>(w_)].store(toDb(pkIn_), std::memory_order_relaxed);
        wet_[static_cast<std::size_t>(w_)].store(toDb(pkWet_), std::memory_order_relaxed);
        if (++w_ >= kPoints) w_ = 0;
        cnt_ = 0;
        pkIn_ = pkWet_ = 0.0f;
    }

    std::atomic<float> in_[kPoints]{};
    std::atomic<float> wet_[kPoints]{};
    std::atomic<float> spec_[kSpecN]{};
    int32_t w_ = 0;
    int32_t s_ = 0;
    int32_t cnt_ = 0;
    int32_t bucket_ = 176;
    int64_t since_ = 0;
    double sr_ = 48000.0;
    float pkIn_ = 0.0f;
    float pkWet_ = 0.0f;
    std::atomic<int32_t> wA_{0};
    std::atomic<int32_t> sA_{0};
    std::atomic<int64_t> pubNs_{0};
    std::atomic<int64_t> staleNs_{kStaleNs};
    mutable std::atomic<int64_t> readNs_{INT64_MIN / 2};
    bool watched_ = false;
    static_assert(std::atomic<float>::is_always_lock_free, "the audio thread must not lock");
};

} // namespace nota::dsp
