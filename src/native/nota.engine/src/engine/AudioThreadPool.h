// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioThreadPool — lock-free fork-join worker pool for the engine's parallel
// render passes . The audio thread publishes a job (count +
// type-erased callable, no allocation) by bumping a generation counter;
// dedicated workers plus the calling thread itself claim tickets from a
// shared monotonic counter. Workers spin (pause hint, then yield — never
// block on a mutex), hold FTZ/DAZ for their whole lifetime, and own
// pre-allocated scratch buffers sized once on the message thread (AR-6: the
// audio path never allocates, locks, or blocks).
//
// Race discipline (three measured crash/hang bugs killed two earlier
// designs — fetch_add claim + counter resets, then fetch_add + monotonic
// counters with post-fetch epoch checks):
//  - claimed/done counters are MONOTONIC (never reset). Resets race with a
//    lagging fetch_add that slips between the reset and the publish bump
//    (consumed-but-misattributed tickets break the completion count and run
//    stale callables = UB).
//  - claiming is CHECK-THEN-CAS, never blind fetch_add: an overshoot probe
//    (beyond the live job's end) must not consume a ticket, because that
//    ticket may be the next job's head — a stale overshoot fetch between the
//    base record and the publish bump eats it and hangs the next job.
//  - the CAS itself is the freshness proof: success means the counter still
//    holds the validated value, hence no claims (and therefore no job
//    completions, and therefore no publishes, and therefore no descriptor
//    writes) happened since the check — the validated slot fields are
//    intact through call + done_++. A failed CAS means someone else
//    progressed; the loop simply re-validates (lock-free system-wide
//    progress, bounded retries with W+1 <= 18 threads).
//  - job descriptors live in generation-indexed slots; a publish writes its
//    slot before the epoch bump (release) and is gated on the previous
//    job's completion, so a live ticket's slot is stable while held.

#pragma once

#include "dsp/core/RtAttributes.h"

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <thread>
#include <vector>

namespace nota {

class AudioThreadPool {
  public:
    // Dedicated worker threads (the caller always helps as an extra worker).
    // <= 0 selects a default (hardware threads minus one for the caller).
    explicit AudioThreadPool(int workers = -1);
    ~AudioThreadPool();

    AudioThreadPool(const AudioThreadPool&) = delete;
    AudioThreadPool& operator=(const AudioThreadPool&) = delete;

    static int defaultWorkers();

    // Dedicated threads (excludes the caller-helper slot).
    int workerCount() const noexcept { return (int)workers_.size(); }

    // Scratch management. Message thread only: grows each worker's buffer
    // (plus one caller slot) to at least `floats` floats.
    void reserveScratch(size_t floats);
    size_t scratchCapacity() const noexcept { return scratchFloats_; }

    // Worker-private scratch. Audio thread safe (no allocation, no locks).
    // id in [0, workerCount()], where workerCount() is the CALLER slot.
    // Caller must have reserved enough via reserveScratch().
    float* workerScratchBuffer(int worker) NOTA_RT_SAFE { return scratch_[(size_t)worker].data(); }
    const float* workerScratchBuffer(int worker) const NOTA_RT_SAFE { return scratch_[(size_t)worker].data(); }

    // Run fn(workerId, index) for index in [0, n). The caller participates
    // with worker id == workerCount(). Returns when all n are done.
    // fn must not throw, allocate, lock, or block (audio thread).
    template <typename F> void dispatch(int n, F&& fn) {
        if (n <= 0) return;
        if (workers_.empty()) {
            for (int i = 0; i < n; ++i) fn(workerCount(), i);
            return;
        }
        // Publish into this generation's slot BEFORE the bump (release):
        // workers observe the slot through the epoch's acquire chain.
        const uint64_t g = epoch_.load(std::memory_order_relaxed) + 1;
        JobSlot& s = slots_[g & (kSlots - 1)];
        s.call = &callTrampoline<F>;
        s.ctx = (void*)&fn;
        s.n = n;
        s.base = claimed_.load(std::memory_order_relaxed);
        doneBase_ = done_.load(std::memory_order_relaxed);
        epoch_.store(g, std::memory_order_release); // publish: workers wake
        // Claim loop: validate against the live slot, claim by CAS, run.
        // The caller needs no stop checks (single audio thread: no
        // concurrent publish can invalidate its own job mid-flight).
        const int self = workerCount();
        for (;;) {
            uint64_t t = claimed_.load(std::memory_order_relaxed);
            uint64_t lg = epoch_.load(std::memory_order_acquire);
            const JobSlot& ls = slots_[lg & (kSlots - 1)];
            if (t - ls.base >= (uint64_t)ls.n) {
                if (epoch_.load(std::memory_order_acquire) == lg) break; // stable: truly beyond
                continue; // view moved under us — re-evaluate, nothing consumed
            }
            if (claimed_.compare_exchange_weak(t, t + 1, std::memory_order_relaxed)) {
                fn(self, (int)(t - ls.base));
                done_.fetch_add(1, std::memory_order_relaxed);
            }
        }
        const uint64_t need = doneBase_ + (uint64_t)n;
        while (done_.load(std::memory_order_acquire) < need) NOTA_CPU_PAUSE();
    }

  private:
    static constexpr uint64_t kSlots = 8;

    template <typename F> static void callTrampoline(void* ctx, int worker, int index) {
        (*static_cast<F*>(ctx))(worker, index);
    }

    using JobCall = void (*)(void*, int, int);
    struct JobSlot {
        JobCall call = nullptr;
        void* ctx = nullptr;
        int n = 0;
        uint64_t base = 0;
    };

    void workerLoop(int id);

    std::vector<std::thread> workers_;
    std::vector<std::vector<float>> scratch_; // [workerCount()+1], message-thread sized
    size_t scratchFloats_ = 0;

    NOTA_CACHE_ALIGNED std::atomic<uint64_t> epoch_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint64_t> claimed_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint64_t> done_{0};
    std::atomic<bool> stop_{false};

    JobSlot slots_[kSlots]; // gen-indexed descriptors (see header comment)
    uint64_t doneBase_ = 0; // caller-thread only (single audio thread)
};

} // namespace nota
