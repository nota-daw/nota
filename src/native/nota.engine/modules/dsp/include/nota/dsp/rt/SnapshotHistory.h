// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SnapshotHistory — epoch-guarded immutable snapshot publication without audio-thread deallocation.

#pragma once

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota::dsp {

template <class T> class SnapshotHistory {
  public:
    T* live() const noexcept { return live_.load(std::memory_order_acquire); }
    std::shared_ptr<T> current() const { return current_; }

    bool hasPendingRetire() const noexcept {
        return epoch_.load(std::memory_order_acquire) < drainTarget_.load(std::memory_order_acquire);
    }

    void publish(std::shared_ptr<T> next) {
        live_.store(next.get(), std::memory_order_seq_cst);
        const uint64_t now = epoch_.load(std::memory_order_seq_cst);
        if (current_) {
            retired_.emplace_back(now, std::move(current_));
            drainTarget_.store(now + 2u, std::memory_order_release);
        }
        current_ = std::move(next);
        retired_.erase(
            std::remove_if(retired_.begin(), retired_.end(), [&](const auto& r) { return now >= r.first + 2u; }),
            retired_.end());
    }

    class RenderScope {
      public:
        explicit RenderScope(SnapshotHistory& h) noexcept : h_(h) {}
        ~RenderScope() { h_.epoch_.fetch_add(1u, std::memory_order_seq_cst); }
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
    std::atomic<uint64_t> drainTarget_{0};
};

} // namespace nota::dsp
