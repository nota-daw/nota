// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioThreadPool implementation. Thread management and the worker
// spin-then-park loop live here; dispatch() is a header
// template (type-erased via a non-owning trampoline — no std::function,
// no allocation on the audio path).

#include "AudioThreadPool.h"

#include "dsp/core/ScopedNoDenormals.h"

#include <algorithm>
#include <chrono>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#elif defined(__APPLE__)
#include <mach/mach.h>
#include <pthread.h>
#include <sys/sysctl.h>
#else
#include <pthread.h>
#include <time.h>
#endif

namespace nota {

int AudioThreadPool::defaultWorkers() {
    int hw = (int)std::thread::hardware_concurrency();
#if defined(__APPLE__)
    // Performance cores only: a job split onto efficiency cores waits for its
    // slowest part, and the same work costs those cores more CPU time.
    int perf = 0;
    size_t size = sizeof perf;
    if (sysctlbyname("hw.perflevel0.physicalcpu", &perf, &size, nullptr, 0) == 0 && perf > 0)
        hw = std::min(hw, perf);
#endif
    if (hw <= 2) return 0;       // tiny machines: render inline
    return std::min(hw - 1, 16); // cap: past this the fork-join overhead wins
}

AudioThreadPool::AudioThreadPool(int workers) {
    int w = workers < 0 ? defaultWorkers() : workers;
    w = std::clamp(w, 0, 64);
    for (int i = 0; i < w; ++i) workers_.emplace_back([this, i] { workerLoop(i); });
}

AudioThreadPool::~AudioThreadPool() {
    stop_.store(true, std::memory_order_seq_cst);
    epoch_.fetch_add(1, std::memory_order_seq_cst);
    wake_.fetch_add(1, std::memory_order_release); // wake parked workers so they observe stop_
    wake_.notify_all();
    for (auto& t : workers_)
        if (t.joinable()) t.join();
}

double AudioThreadPool::workersCpuSeconds() const {
    double total = 0.0;
    for (auto& t : const_cast<std::vector<std::thread>&>(workers_)) {
#if defined(_WIN32)
        FILETIME created, exited, kernel, user;
        if (GetThreadTimes((HANDLE)t.native_handle(), &created, &exited, &kernel, &user)) {
            auto ticks = [](const FILETIME& f) { return ((uint64_t)f.dwHighDateTime << 32) | f.dwLowDateTime; };
            total += (double)(ticks(kernel) + ticks(user)) * 1e-7; // 100 ns units
        }
#elif defined(__APPLE__)
        const mach_port_t port = pthread_mach_thread_np(t.native_handle());
        thread_basic_info_data_t info;
        mach_msg_type_number_t count = THREAD_BASIC_INFO_COUNT;
        if (thread_info(port, THREAD_BASIC_INFO, (thread_info_t)&info, &count) == KERN_SUCCESS)
            total += info.user_time.seconds + info.user_time.microseconds * 1e-6 + info.system_time.seconds +
                     info.system_time.microseconds * 1e-6;
#else
        clockid_t clock;
        timespec ts{};
        if (pthread_getcpuclockid(t.native_handle(), &clock) == 0 && clock_gettime(clock, &ts) == 0)
            total += ts.tv_sec + ts.tv_nsec * 1e-9;
#endif
    }
    return total;
}

// Block until wake_ moves, unless a job (or stop) arrived since `seen`.
void AudioThreadPool::park(uint64_t seen) {
    const WakeWord w = wake_.load(std::memory_order_acquire);
    sleepers_.fetch_add(1, std::memory_order_seq_cst);
    if (epoch_.load(std::memory_order_seq_cst) == seen && !stop_.load(std::memory_order_seq_cst))
        wake_.wait(w, std::memory_order_acquire);
    sleepers_.fetch_sub(1, std::memory_order_relaxed);
}

void AudioThreadPool::workerLoop(int id) {
    // Pause-spins before parking, plus the linger dispatch() picked from the
    // gap between jobs. With a live device the next job is a buffer period
    // away, and a fixed longer spin (measured: 20, 50, 100 us) only burned CPU
    // there; offline renders get the linger instead.
    using Clock = std::chrono::steady_clock;
    constexpr int kSpinPauses = 64;
    dsp::ScopedNoDenormals ftz; // whole thread lifetime: FTZ + DAZ
    uint64_t seen = 0;
    int spins = 0;
    Clock::time_point idleSince{};
    for (;;) {
        const uint64_t e = epoch_.load(std::memory_order_acquire);
        if (e == seen) { // idle: brief pause-spin, then park on the futex word
            if (stop_.load(std::memory_order_relaxed)) return;
            if (spins < kSpinPauses) {
                if (++spins == kSpinPauses) idleSince = Clock::now();
                NOTA_CPU_PAUSE();
                continue;
            }
            if (const int64_t ns = lingerNs_.load(std::memory_order_relaxed);
                ns > 0 && Clock::now() - idleSince < std::chrono::nanoseconds(ns)) {
                NOTA_CPU_PAUSE();
                continue;
            }
            park(seen);
            spins = 0;
            continue;
        }
        seen = e;
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
                done_.fetch_add(1, std::memory_order_release); // publishes this task's writes
            }
        }
        spins = 0;
    }
}

} // namespace nota
