// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SnapshotHistory — lifetime of an immutable snapshot the message thread publishes
// and the audio thread reads through a raw pointer (a rack's chains, a Rhythm
// Machine's voice FX). Replaced snapshots are retired with the audio thread's render
// epoch and freed once two more renders have finished, so none can still be walking
// them: the engine's preview / Mosaic pattern. A fixed-size history isn't a grace
// period: a kit load commits dozens of times inside one audio block (#177).
//
// Threads. publish() and everything else that isn't marked runs on the message
// thread. The audio thread only calls live() and ends each render with a
// RenderScope, so it neither touches the retired list nor frees memory. While the
// owner isn't being rendered, retired snapshots simply wait.

#pragma once

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota {

template <class T> class SnapshotHistory {
  public:
    // Audio-thread view of the current snapshot; valid until the end of the render
    // that read it.
    T* live() const { return live_.load(std::memory_order_acquire); }

    // Publish `next` and retire the current one; frees retirees no render can still hold.
    void publish(std::shared_ptr<T> next) {
        live_.store(next.get(), std::memory_order_seq_cst);
        // Read after the swap: a render that saw the old pointer has finished by epoch now + 2.
        const uint64_t now = epoch_.load(std::memory_order_seq_cst);
        if (current_) retired_.emplace_back(now, std::move(current_));
        current_ = std::move(next);
        retired_.erase(
            std::remove_if(retired_.begin(), retired_.end(), [&](const auto& r) { return now >= r.first + 2; }),
            retired_.end());
    }

    // Audio thread: one per render/process call, ending that call's render epoch on
    // every return path.
    class RenderScope {
      public:
        explicit RenderScope(SnapshotHistory& h) : h_(h) {}
        ~RenderScope() { h_.epoch_.fetch_add(1, std::memory_order_seq_cst); }
        RenderScope(const RenderScope&) = delete;
        RenderScope& operator=(const RenderScope&) = delete;

      private:
        SnapshotHistory& h_;
    };

  private:
    std::shared_ptr<T> current_;
    std::vector<std::pair<uint64_t, std::shared_ptr<T>>> retired_;
    std::atomic<T*> live_{nullptr};
    std::atomic<uint64_t> epoch_{0};
};

} // namespace nota
