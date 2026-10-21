// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioThreadPool — lock-free fork-join worker pool for the engine's parallel
// render passes . The audio thread publishes a job (count +
// type-erased callable, no allocation) by bumping a generation counter;
// dedicated workers plus the calling thread itself claim tickets from a
// shared monotonic counter. Workers spin briefly after each job (pause
// hint), then park on a futex word (std::atomic::wait) so an idle engine
// costs no CPU; they hold FTZ/DAZ for their whole lifetime (AR-6: the
// audio path never allocates, locks, or blocks — waking parked workers is a
// non-blocking futex notify, skipped while none are parked).
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
//  - parking is Dekker-style (all seq_cst): a worker bumps sleepers_, then
//    re-checks epoch_ before waiting on wake_; dispatch() stores epoch_,
//    then reads sleepers_. Either the worker sees the new epoch or the
//    dispatcher sees the sleeper and bumps wake_, so no wake-up is lost.
//    Even a missed wake could never hang: the caller claims every ticket
//    nobody else took.

#pragma once

#include "dsp/core/RtAttributes.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <thread>
#include <vector>

namespace nota {

class AudioThreadPool {
  public:
    // Dedicated worker threads (the caller always helps as an extra worker).
    // < 0 selects a default (hardware threads minus one for the caller); 0 runs
    // every job on the caller alone.
    explicit AudioThreadPool(int workers = -1);
    ~AudioThreadPool();

    AudioThreadPool(const AudioThreadPool&) = delete;
    AudioThreadPool& operator=(const AudioThreadPool&) = delete;

    static int defaultWorkers();

    // Dedicated threads (excludes the caller-helper slot).
    int workerCount() const noexcept { return (int)workers_.size(); }

    // Caller thread: whether the next job follows the previous one back-to-back
    // (an offline render: no deadline, finish fast) rather than a buffer period
    // later (a device callback: finish in time, spend little CPU).
    bool backToBack() const noexcept {
        return std::chrono::steady_clock::now() - lastEnd_ < std::chrono::nanoseconds(kLingerGapNs);
    }

    // CPU time the dedicated workers have used since they started, summed, in
    // seconds (per-thread OS clocks: unaffected by other threads in the process).
    // Any thread. Lets tests tell parked workers (~0) from spinning ones.
    double workersCpuSeconds() const;

    // Run fn(workerId, index) for index in [0, n). The caller participates
    // with worker id == workerCount(). Returns when all n are done.
    // fn must not throw, allocate, lock, or block (audio thread).
    // helpers: how many parked workers to wake (< 0 = as many as useful, 0 =
    // none: the caller runs the job alone, joined only by workers that are
    // still spinning from the previous job). Each wake costs the woken thread
    // a context switch, so wake only as many as the job's work pays for.
    template <typename F> void dispatch(int n, F&& fn, int helpers = -1) {
        if (n <= 0) return;
        if (workers_.empty()) {
            for (int i = 0; i < n; ++i) fn(workerCount(), i);
            return;
        }
        // Linger: how long idle workers keep spinning before they park, from the gap
        // since the previous job ended. A device callback dispatches a buffer period
        // apart (ms): park at once, spinning would only burn CPU. An offline render
        // (export, freeze) dispatches back-to-back (us): a parked worker's wake-up
        // would cost more than the spin, so linger about twice the gap.
        // A job that wakes no one (helpers == 0) lets no one linger either: workers
        // that keep spinning would take its tickets anyway.
        {
            const auto now = std::chrono::steady_clock::now();
            const int64_t gapNs = std::chrono::duration_cast<std::chrono::nanoseconds>(now - lastEnd_).count();
            lingerNs_.store(helpers != 0 && gapNs < kLingerGapNs ? std::min<int64_t>(2 * gapNs, kMaxLingerNs) : 0,
                std::memory_order_relaxed);
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
        epoch_.store(g, std::memory_order_seq_cst); // publish (seq_cst: pairs with park())
        // Wake parked workers — at most `helpers`, and never more than there are
        // tickets beyond the one the caller takes itself. Workers left parked stay
        // asleep (their wait sees the bumped word only when a later notify reaches
        // them, and then the epoch tells them what is live). A job that wants every
        // thread (helpers < 0: an offline render) wakes them with one notify_all rather
        // than k notify_one calls, k syscalls on the caller's critical path.
        if (n > 1 && helpers != 0) {
            if (const int asleep = sleepers_.load(std::memory_order_seq_cst); asleep > 0) {
                const int want = std::min(helpers < 0 ? workerCount() : helpers, n - 1);
                wake_.fetch_add(1, std::memory_order_release);
                if (helpers < 0 || want >= asleep)
                    wake_.notify_all(); // one non-blocking futex wake for everyone
                else
                    for (int i = 0; i < want; ++i) wake_.notify_one();
            }
        }
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
                done_.fetch_add(1, std::memory_order_release); // publishes this task's writes
            }
        }
        // Acquire pairs with every task's release increment (a release sequence through
        // the RMWs), so all workers' buffer writes are visible once the count is reached.
        const uint64_t need = doneBase_ + (uint64_t)n;
        while (done_.load(std::memory_order_acquire) < need) NOTA_CPU_PAUSE();
        lastEnd_ = std::chrono::steady_clock::now();
    }

  private:
    static constexpr uint64_t kSlots = 8;
    static constexpr int64_t kLingerGapNs = 200'000; // gaps below this count as back-to-back
    static constexpr int64_t kMaxLingerNs = 100'000;

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
    void park(uint64_t seen);

    std::vector<std::thread> workers_;

    NOTA_CACHE_ALIGNED std::atomic<uint64_t> epoch_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint64_t> claimed_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint64_t> done_{0};
    std::atomic<bool> stop_{false};
    NOTA_CACHE_ALIGNED std::atomic<int> sleepers_{0}; // workers inside park()
    // Futex word, in the width libc++ waits on natively: int32 on Linux, int64 elsewhere
    // (MSVC's WaitOnAddress takes either). Any other width waits on a shared table entry,
    // and its notify_one wakes every waiter: on macOS, dispatch() woke the whole pool.
#if defined(__linux__)
    using WakeWord = int32_t;
#else
    using WakeWord = int64_t;
#endif
    std::atomic<WakeWord> wake_{0};
    std::atomic<int64_t> lingerNs_{0};                // set per dispatch (see dispatch())

    JobSlot slots_[kSlots]; // gen-indexed descriptors (see header comment)
    uint64_t doneBase_ = 0; // caller-thread only (single audio thread)
    std::chrono::steady_clock::time_point lastEnd_{}; // caller-thread only: previous job's end
};

} // namespace nota
