// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RenderExecutor — decoupled task collaborator bridging multi-chain racks and voices to the audio pool.

#pragma once

#include "nota/dsp/Export.h"

#include <cstdint>

namespace nota::dsp {

struct RenderExecutorHost {
    using RunFn = void (*)(void* ctx, int32_t index);

    void* pool = nullptr;
    bool (*inTopDispatch)(void* pool) noexcept = nullptr;
    void (*dispatchTop)(void* pool, int32_t count, RunFn fn, void* ctx, int32_t helpers) = nullptr;
    bool (*tryRunSub)(void* pool, int32_t count, RunFn fn, void* ctx) noexcept = nullptr;
    int32_t (*workerCount)(void* pool) noexcept = nullptr;
};

class NOTA_DSP_API RenderExecutor {
  public:
    struct ThreadState {
        const RenderExecutorHost* host = nullptr;
        bool allow = false;
    };

    class ScopedHost {
      public:
        ScopedHost(const RenderExecutorHost* host, bool allowParallel) noexcept {
            ThreadState& st = state();
            prev_ = st;
            st.host = host;
            st.allow = allowParallel;
        }
        ~ScopedHost() noexcept { state() = prev_; }
        ScopedHost(const ScopedHost&) = delete;
        ScopedHost& operator=(const ScopedHost&) = delete;

      private:
        ThreadState prev_;
    };

    static void setCurrentAllowed(bool allowParallel) noexcept { state().allow = allowParallel; }

    static bool isParallelAllowed() noexcept {
        const ThreadState& st = state();
        return st.allow && st.host != nullptr && st.host->pool != nullptr;
    }

    static int32_t availableConcurrency() noexcept {
        const ThreadState& st = state();
        if (!st.allow || st.host == nullptr || st.host->pool == nullptr || !st.host->workerCount) return 1;
        return 1 + st.host->workerCount(st.host->pool);
    }

    template <typename Fn>
    static void parallelFor(int32_t count, bool eligible, Fn&& fn, int32_t maxHelpers = -1) noexcept {
        if (count <= 0) return;
        const ThreadState& st = state();
        if (!eligible || count == 1 || !st.allow || st.host == nullptr || st.host->pool == nullptr) {
            for (int32_t i = 0; i < count; ++i) fn(i);
            return;
        }
        auto trampoline = [](void* raw, int32_t index) { (*static_cast<Fn*>(raw))(index); };
        const RenderExecutorHost& h = *st.host;
        if (h.inTopDispatch && h.inTopDispatch(h.pool)) {
            if (h.tryRunSub && h.tryRunSub(h.pool, count, trampoline, static_cast<void*>(&fn))) return;
            for (int32_t i = 0; i < count; ++i) fn(i);
            return;
        }
        if (h.dispatchTop) {
            h.dispatchTop(h.pool, count, trampoline, static_cast<void*>(&fn), maxHelpers);
            return;
        }
        for (int32_t i = 0; i < count; ++i) fn(i);
    }

  private:
    static ThreadState& state() noexcept;
};

} // namespace nota::dsp
