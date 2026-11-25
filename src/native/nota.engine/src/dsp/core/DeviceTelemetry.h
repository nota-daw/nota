// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceTelemetry — shared, lock-free per-device CPU timing telemetry (AR-6 safe).
// Centralizes the OS clock reads and EMA smoothing previously duplicated across built-in
// effects and instruments, skipping wall-clock queries whenever no viewer is inspecting
// the device card (OnDemand mode).

#pragma once

#include "dsp/core/RtAttributes.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>

namespace nota {
namespace dsp {

// Matches NotaTelemetryMode in include/nota/nota_engine.h.
enum class TelemetryMode : int32_t {
    OnDemand = 0,
    AlwaysOff = 1,
    AlwaysOn = 2,
};

class DeviceTelemetry {
  public:
    using Clock = std::chrono::steady_clock;

    // Lease duration (in rendered audio seconds) kept active after each observe() / scopeRead()
    // call so open device cards measure continuously without calling steady_clock in observe().
    static constexpr double kLeaseSeconds = 3.0;

    // RAII block timer placed at the entry of process()/render(). Measures Clock::now() only
    // when active (OnDemand inside an active lease, or AlwaysOn); keeps owner_ == nullptr when
    // idle so no `&this->telemetry_` alias escapes into the caller's hot DSP loop.
    class ScopedBlockTimer {
      public:
        ScopedBlockTimer(DeviceTelemetry& owner, int32_t frames, double sampleRate, double smoothing = 0.1) noexcept {
            if (frames <= 0) return;
            const TelemetryMode mode = owner.mode_.load(std::memory_order_relaxed);
            const uint32_t req = owner.observeRequests_.load(std::memory_order_relaxed);
            if (mode == TelemetryMode::OnDemand && owner.rtLeaseFrames_ == 0 && req == owner.lastSeenObserve_
                && owner.smoothedCpu_ == 0.0) {
                return;
            }
            beginSlow(owner, frames, sampleRate, smoothing, mode, req);
        }

        ~ScopedBlockTimer() noexcept {
            if (owner_) finishSlow();
        }

        ScopedBlockTimer(const ScopedBlockTimer&) = delete;
        ScopedBlockTimer& operator=(const ScopedBlockTimer&) = delete;

      private:
        NOTA_NOINLINE void beginSlow(DeviceTelemetry& owner, int32_t frames, double sampleRate, double smoothing,
            TelemetryMode mode, uint32_t req) noexcept {
            frames_ = frames;
            sampleRate_ = sampleRate > 0.0 ? sampleRate : 44100.0;
            smoothing_ = smoothing;
            if (req != owner.lastSeenObserve_) {
                owner.lastSeenObserve_ = req;
                owner.rtLeaseFrames_ = leaseFramesFor(sampleRate_);
            }

            bool active = false;
            if (mode == TelemetryMode::AlwaysOn) {
                active = true;
            } else if (mode == TelemetryMode::OnDemand && owner.rtLeaseFrames_ > 0) {
                active = true;
            } else if (owner.smoothedCpu_ > 0.0 || mode == TelemetryMode::AlwaysOff) {
                owner.smoothedCpu_ = 0.0;
                owner.cpuLoad_.store(0.0f, std::memory_order_relaxed);
            }

            if (owner.rtLeaseFrames_ > 0) {
                owner.rtLeaseFrames_ = std::max<int64_t>(0, owner.rtLeaseFrames_ - frames_);
            }

            if (active) {
                owner_ = &owner;
                startedAt_ = Clock::now();
            }
        }

        NOTA_NOINLINE void finishSlow() noexcept {
            if (frames_ <= 0 || sampleRate_ <= 0.0) return;
            const double elapsed = std::chrono::duration<double>(Clock::now() - startedAt_).count();
            const double budget = static_cast<double>(frames_) / sampleRate_;
            if (budget > 0.0) {
                owner_->smoothedCpu_ += smoothing_ * ((elapsed / budget) - owner_->smoothedCpu_);
                owner_->cpuLoad_.store(static_cast<float>(owner_->smoothedCpu_), std::memory_order_relaxed);
            }
        }

        DeviceTelemetry* owner_ = nullptr;
        int32_t frames_ = 0;
        double sampleRate_ = 44100.0;
        double smoothing_ = 0.1;
        Clock::time_point startedAt_{};
    };

    void setMode(TelemetryMode mode) noexcept {
        mode_.store(mode, std::memory_order_relaxed);
        if (mode == TelemetryMode::AlwaysOff) {
            cpuLoad_.store(0.0f, std::memory_order_relaxed);
        }
    }

    TelemetryMode mode() const noexcept { return mode_.load(std::memory_order_relaxed); }

    // Called off the audio thread (UI/tests/MCP) from scopeRead() to keep block timing
    // active while a viewer inspects telemetry. Lock-free and allocation-free.
    void observe() const noexcept { observeRequests_.fetch_add(1, std::memory_order_relaxed); }

    float observeAndReadCpu() const noexcept {
        observe();
        return cpuLoad_.load(std::memory_order_relaxed);
    }

    float cpuLoad() const noexcept { return cpuLoad_.load(std::memory_order_relaxed); }

    void reset() noexcept {
        smoothedCpu_ = 0.0;
        rtLeaseFrames_ = 0;
        cpuLoad_.store(0.0f, std::memory_order_relaxed);
    }

  private:
    static int64_t leaseFramesFor(double sampleRate) noexcept {
        const double sr = sampleRate > 0.0 ? sampleRate : 44100.0;
        return static_cast<int64_t>(std::clamp(sr * kLeaseSeconds, 4096.0, 4800000.0));
    }

    std::atomic<TelemetryMode> mode_{TelemetryMode::OnDemand};
    mutable std::atomic<uint32_t> observeRequests_{0};
    uint32_t lastSeenObserve_ = 0;
    int64_t rtLeaseFrames_ = 0;
    double smoothedCpu_ = 0.0;
    std::atomic<float> cpuLoad_{0.0f};
};

} // namespace dsp
} // namespace nota
