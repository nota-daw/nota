// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioThreadPool — real-time lock-free worker thread pool for parallel track
// rendering in the Nota C++20 engine. Workers are pre-allocated on the message
// thread; task dispatch and barrier synchronization use C++20 atomics without
// heap allocations or mutex locks, fully adhering to Real-Time Audio Rule AR-6.

#pragma once

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <thread>
#if defined(__APPLE__)
#include <pthread.h>
#endif
#include "Simd.h"

namespace nota {

struct AudioTask {
  void (*fn)(void *ctx) = nullptr;
  void *ctx = nullptr;
};

class AudioThreadPool {
public:
  static constexpr int kMaxWorkers = 16;

  AudioThreadPool() = default;
  ~AudioThreadPool() { shutdown(); }

  // Non-copyable, non-movable
  AudioThreadPool(const AudioThreadPool &) = delete;
  AudioThreadPool &operator=(const AudioThreadPool &) = delete;

  // Configure the worker pool. Must be called on the message thread (not audio
  // thread).
  void configure(int numThreads) {
    shutdown();
    const unsigned hw = std::thread::hardware_concurrency();
    const int target =
        numThreads > 0 ? numThreads : static_cast<int>(hw > 0 ? hw : 4);
    // Reserve 1 core for the main audio thread and OS
    const int count = std::clamp(target - 1, 0, kMaxWorkers);
    if (count <= 0)
      return;

    stopping_.store(false, std::memory_order_relaxed);
    generation_.store(0, std::memory_order_relaxed);
    numWorkers_ = count;

    workers_.reserve(static_cast<size_t>(count));
    for (int i = 0; i < count; ++i) {
      workers_.emplace_back([this, i] { workerLoop(i); });
    }
  }

  // Shut down worker threads. Must be called on message thread.
  void shutdown() {
    if (workers_.empty())
      return;
    stopping_.store(true, std::memory_order_release);
    generation_.fetch_add(1, std::memory_order_release);
    generation_.notify_all();

    for (auto &w : workers_) {
      if (w.joinable())
        w.join();
    }
    workers_.clear();
    numWorkers_ = 0;
  }

  int workerCount() const noexcept { return numWorkers_; }

  // Execute a batch of tasks across worker threads. Caller thread participates
  // in execution. Real-time safe: lock-free, zero heap allocations, zero
  // mutexes (AR-6).
  void runTasks(const AudioTask *tasks, int numTasks) noexcept {
    if (!tasks || numTasks <= 0)
      return;
    if (numWorkers_ <= 0 || numTasks <= 2) {
      // For 1-2 tasks, avoid worker thread wake latency and execute inline
      for (int i = 0; i < numTasks; ++i) {
        if (tasks[i].fn)
          tasks[i].fn(tasks[i].ctx);
      }
      return;
    }

    activeTasks_ = tasks;
    totalTasks_.store(static_cast<uint32_t>(numTasks),
                      std::memory_order_relaxed);
    nextTaskIndex_.store(0, std::memory_order_relaxed);
    completedTasks_.store(0, std::memory_order_release);

    // Wake workers with a new generation ticket
    const uint32_t gen =
        generation_.fetch_add(1, std::memory_order_release) + 1;
    generation_.notify_all();

    // Caller (main audio thread) works on tasks too
    workOnTasks(gen);

    // Spin wait without yielding the audio callback thread
    while (completedTasks_.load(std::memory_order_acquire) <
           static_cast<uint32_t>(numTasks)) {
      NOTA_CPU_PAUSE();
    }
    activeTasks_ = nullptr;
  }

private:
  void workOnTasks(uint32_t /*gen*/) noexcept {
    const uint32_t total = totalTasks_.load(std::memory_order_relaxed);
    while (true) {
      const uint32_t idx =
          nextTaskIndex_.fetch_add(1, std::memory_order_relaxed);
      if (idx >= total)
        break;
      const AudioTask &t = activeTasks_[idx];
      if (t.fn)
        t.fn(t.ctx);
      completedTasks_.fetch_add(1, std::memory_order_release);
    }
  }

  void workerLoop(int /*workerId*/) {
#if defined(__APPLE__)
    pthread_set_qos_class_self_np(QOS_CLASS_USER_INTERACTIVE, 0);
#endif
    uint32_t lastGen = 0;
    while (!stopping_.load(std::memory_order_relaxed)) {
      // Wait for new work generation
      uint32_t curGen = generation_.load(std::memory_order_acquire);
      while (curGen == lastGen && !stopping_.load(std::memory_order_relaxed)) {
        generation_.wait(curGen, std::memory_order_relaxed);
        curGen = generation_.load(std::memory_order_acquire);
      }
      if (stopping_.load(std::memory_order_relaxed))
        break;
      lastGen = curGen;

      // Execute tasks from current batch
      workOnTasks(curGen);
    }
  }

  std::vector<std::thread> workers_;
  std::atomic<bool> stopping_{false};
  std::atomic<uint32_t> generation_{0};
  std::atomic<uint32_t> nextTaskIndex_{0};
  std::atomic<uint32_t> completedTasks_{0};
  std::atomic<uint32_t> totalTasks_{0};
  const AudioTask *activeTasks_{nullptr};
  int numWorkers_{0};
};

} // namespace nota
