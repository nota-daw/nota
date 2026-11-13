// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A level history for an effect card's live graph: two rings of peak dB (input and wet) at a
// fixed 4 ms bucket, about six seconds of each, plus a ring of the latest mono wet samples
// for a spectrum. The audio thread writes it inside process() and publishes the write
// positions and the time once per block; scopeRead() appends it to the scope, oldest first.
// Every slot is a relaxed atomic, so a read on the UI thread never races a write (it may be
// a point or two behind, which a graph never shows). When the engine stops calling the device
// (a settled track), the history goes stale; read() moves it on by the time since the last
// publish, filling with silence, so an old hit scrolls away instead of standing still. Stale
// means no publish for three blocks (at least 60 ms), so a large audio buffer — 4096 frames is
// 85 ms at 48 kHz — still reads as live between its callbacks.
// Only kept while something reads it: read() stamps the time, and once no read has come for
// kWatchNs (the card closed, the device scrolled out of view) begin() says so and the device
// skips the history — its per-sample work costs nothing on a track nobody is looking at. The
// next read opens it again; the gap reads as silence, as any other gap does.
// Fixed size, no allocation, no lock.

#pragma once

#include "dsp/math/FastMath.h"

#include <algorithm>
#include <atomic>
#include <chrono>
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
    static constexpr int64_t kStaleNs = 60'000'000; // the least idle time that means no blocks run
    static constexpr int64_t kWatchNs = 1'000'000'000; // no read for this long: nobody is looking

    void setSampleRate(double sr) {
        sr_ = sr > 0.0 ? sr : 48000.0;
        bucket_ = std::max(1, static_cast<int>(std::lround(sr * kBucketSec)));
        reset();
    }

    void reset() {
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

    // One sample: the input and wet magnitudes (any stereo fold the caller likes) and the mono wet.
    void push(float inAbs, float wetAbs, float wetMono) {
        ++since_;
        spec_[s_].store(std::isfinite(wetMono) ? wetMono : 0.0f, std::memory_order_relaxed);
        if (++s_ >= kSpecN) s_ = 0;
        pkIn_ = std::max(pkIn_, inAbs);
        pkWet_ = std::max(pkWet_, wetAbs);
        if (++cnt_ >= bucket_) close();
    }

    // At the top of every process(). Returns whether the history is kept this block (a read
    // came within kWatchNs); when it isn't, the device skips push() and the rest do nothing.
    // If the engine stopped calling the device for a while (a muted or settled track, or a
    // card that was closed), first scroll the stored rings on by that gap in silence, so the
    // history read() showed moving on stays moved on instead of the old audio coming back.
    bool begin() {
        const int64_t now = nowNs();
        watched_ = now - readNs_.load(std::memory_order_relaxed) < kWatchNs;
        if (!watched_) return false;
        const int64_t pub = pubNs_.load(std::memory_order_relaxed);
        if (pub <= 0) return true;
        const int64_t gap = now - pub;
        if (gap <= staleNs_.load(std::memory_order_relaxed)) return true;
        const double frames = std::min(static_cast<double>(gap) * 1e-9 * sr_, static_cast<double>(kPoints + 1) * bucket_);
        fill(static_cast<int32_t>(frames));
        since_ = 0;
        return true;
    }

    // A block the device skipped (asleep on silence): the graph keeps scrolling through it.
    void silence(int32_t frames) {
        if (!watched_) return;
        fill(frames);
        since_ += frames;
        publish();
    }

  private:
    void fill(int32_t frames) {
        for (int32_t i = 0, n = std::min<int32_t>(frames, kSpecN); i < n; ++i) {
            spec_[s_].store(0.0f, std::memory_order_relaxed);
            if (++s_ >= kSpecN) s_ = 0;
        }
        cnt_ += frames;
        while (cnt_ >= bucket_) {
            cnt_ -= bucket_;
            in_[w_].store(kFloorDb, std::memory_order_relaxed);
            wet_[w_].store(kFloorDb, std::memory_order_relaxed);
            if (++w_ >= kPoints) w_ = 0;
        }
    }

  public:
    void publish() {
        if (!watched_) return;
        // Three of the blocks the device sees, so the gap between two callbacks never reads as stale.
        const auto blockNs = static_cast<int64_t>(static_cast<double>(since_) / sr_ * 1e9);
        staleNs_.store(std::max(kStaleNs, 3 * blockNs), std::memory_order_relaxed);
        since_ = 0;
        wA_.store(w_, std::memory_order_relaxed);
        sA_.store(s_, std::memory_order_relaxed);
        pubNs_.store(nowNs(), std::memory_order_release);
    }

    // Appends input dB[kPoints], wet dB[kPoints] and wet samples[kSpecN], each oldest first.
    // A stale history is moved on by the time since its last publish. Returns the floats
    // written (0 when out is too small).
    int32_t read(float* out, int32_t room) const {
        if (!out || room < kFloats) return 0;
        readNs_.store(nowNs(), std::memory_order_relaxed); // someone is looking: keep the history
        const int64_t pub = pubNs_.load(std::memory_order_acquire);
        const int w = wA_.load(std::memory_order_relaxed), s = sA_.load(std::memory_order_relaxed);
        const int64_t idle = pub > 0 ? nowNs() - pub : 0;
        const bool stale = idle > staleNs_.load(std::memory_order_relaxed);
        const int skip = stale ? static_cast<int>(std::min<int64_t>(kPoints, idle / static_cast<int64_t>(kBucketSec * 1e9))) : 0;
        for (int k = 0; k < kPoints; ++k) {
            if (k >= kPoints - skip) {
                out[k] = out[kPoints + k] = kFloorDb;
                continue;
            }
            const int i = (w + k + skip) % kPoints;
            out[k] = in_[i].load(std::memory_order_relaxed);
            out[kPoints + k] = wet_[i].load(std::memory_order_relaxed);
        }
        for (int k = 0; k < kSpecN; ++k)
            out[2 * kPoints + k] = stale ? 0.0f : spec_[(s + k) % kSpecN].load(std::memory_order_relaxed);
        return kFloats;
    }

  private:
    static int64_t nowNs() {
        return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
    static float toDb(float lin) { return lin > 1.0e-6f ? std::max(kFloorDb, fast_math::linToDb(lin)) : kFloorDb; }
    void close() {
        in_[w_].store(toDb(pkIn_), std::memory_order_relaxed);
        wet_[w_].store(toDb(pkWet_), std::memory_order_relaxed);
        if (++w_ >= kPoints) w_ = 0;
        cnt_ = 0;
        pkIn_ = pkWet_ = 0.0f;
    }

    std::atomic<float> in_[kPoints]{}, wet_[kPoints]{}, spec_[kSpecN]{};
    int w_ = 0, s_ = 0, cnt_ = 0, bucket_ = 176;
    int64_t since_ = 0; // samples since the last publish (the block the device just ran)
    double sr_ = 48000.0;
    float pkIn_ = 0.0f, pkWet_ = 0.0f;
    std::atomic<int> wA_{0}, sA_{0};
    std::atomic<int64_t> pubNs_{0}, staleNs_{kStaleNs};
    mutable std::atomic<int64_t> readNs_{INT64_MIN / 2}; // the last read (UI thread); never read: not watched
    bool watched_ = false;                                 // this block keeps the history (audio thread)
    static_assert(std::atomic<float>::is_always_lock_free, "the audio thread must not lock");
};

} // namespace nota::dsp
