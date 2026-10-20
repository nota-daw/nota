// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioThreadPool implementation . Thread management,
// worker spin loop, and scratch sizing live here; dispatch() is a header
// template (type-erased via a non-owning trampoline — no std::function,
// no allocation on the audio path).

#include "AudioThreadPool.h"

#include "dsp/core/ScopedNoDenormals.h"

#include <algorithm>

namespace nota {

int AudioThreadPool::defaultWorkers() {
    const int hw = (int)std::thread::hardware_concurrency();
    if (hw <= 2) return 0;       // tiny machines: render inline
    return std::min(hw - 1, 16); // cap: past this the fork-join overhead wins
}

AudioThreadPool::AudioThreadPool(int workers) {
    int w = workers < 0 ? defaultWorkers() : workers;
    w = std::clamp(w, 0, 64);
    scratch_.resize((size_t)w + 1); // +1 caller slot
    for (int i = 0; i < w; ++i) workers_.emplace_back([this, i] { workerLoop(i); });
}

AudioThreadPool::~AudioThreadPool() {
    stop_.store(true, std::memory_order_relaxed);
    epoch_.fetch_add(1, std::memory_order_release); // wake sleepers so they observe stop_
    for (auto& t : workers_)
        if (t.joinable()) t.join();
}

void AudioThreadPool::reserveScratch(size_t floats) {
    if (floats <= scratchFloats_) return;
    scratchFloats_ = floats;
    for (auto& s : scratch_) s.resize(floats); // message thread only: may allocate
}

void AudioThreadPool::workerLoop(int id) {
    dsp::ScopedNoDenormals ftz; // whole thread lifetime: FTZ + DAZ
    uint64_t seen = 0;
    int spins = 0;
    for (;;) {
        const uint64_t e = epoch_.load(std::memory_order_acquire);
        if (e == seen) { // parked: brief pause-spin, then yield (no mutex, no block)
            if (++spins < 256) {
                NOTA_CPU_PAUSE();
            } else {
                std::this_thread::yield();
                spins = 0;
            }
            if (stop_.load(std::memory_order_relaxed)) return;
            continue;
        }
        seen = e;
        spins = 0;
        if (stop_.load(std::memory_order_relaxed)) return;
        // Same check-then-CAS claim loop as the caller (see header
        // comment): overshoot probes never consume, so a lagging probe
        // can never eat the next job's head tickets.
        for (;;) {
            const uint64_t t = claimed_.load(std::memory_order_relaxed);
            const uint64_t lg = epoch_.load(std::memory_order_acquire);
            if (stop_.load(std::memory_order_relaxed)) return;
            const JobSlot& s = slots_[lg & (kSlots - 1)];
            if (t - s.base >= (uint64_t)s.n) {
                if (epoch_.load(std::memory_order_acquire) == lg) break; // stable: truly beyond
                continue;
            }
            uint64_t expect = t;
            if (claimed_.compare_exchange_weak(expect, t + 1, std::memory_order_relaxed)) {
                s.call(s.ctx, id, (int)(t - s.base));
                done_.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }
}

} // namespace nota
