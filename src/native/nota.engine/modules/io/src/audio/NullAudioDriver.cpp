// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/audio/NullAudioDriver.h"
#include "nota/io/audio/OutputRouter.h"
#include "nota/io/audio/IAudioOutputStream.h"
#include "nota/io/audio/IAudioInputStream.h"
#include "nota/io/audio/AudioIoTypes.h"

#include <atomic>
#include <string_view>
#include <string>
#include <memory>
#include <cstdint>
#include <utility>
#include <vector>

namespace nota::io::audio {
namespace {

class NullOutputStream final : public IAudioOutputStream {
  public:
    bool start(RenderCallback render, const OutputStreamConfig& cfg) override {
        render_ = std::move(render);
        sampleRate_ = cfg.sampleRate > 0.0 ? cfg.sampleRate : 44100.0;
        bufferFrames_ = cfg.bufferFrames > 0 ? cfg.bufferFrames : 256;
        running_.store(true, std::memory_order_release);
        return true;
    }
    void stop() override {
        running_.store(false, std::memory_order_release);
        render_ = nullptr;
    }
    double sampleRate() const override { return running_.load(std::memory_order_acquire) ? sampleRate_ : 0.0; }
    int32_t bufferFrames() const override { return running_.load(std::memory_order_acquire) ? bufferFrames_ : 0; }
    bool isRunning() const override { return running_.load(std::memory_order_acquire); }
    bool silentFallback() const override { return true; }
    void setOutputRouting(const OutputRouting& r) override { stage_.setRouting(r); }

  private:
    RenderCallback render_;
    OutputStage stage_;
    double sampleRate_ = 0.0;
    int32_t bufferFrames_ = 0;
    std::atomic<bool> running_{false};
};

class NullInputStream final : public IAudioInputStream {
  public:
    bool start(InputCallback cb, uint32_t /*device*/, double preferredRate) override {
        cb_ = std::move(cb);
        sampleRate_ = preferredRate > 0.0 ? preferredRate : 44100.0;
        running_.store(true, std::memory_order_release);
        return true;
    }
    void stop() override {
        running_.store(false, std::memory_order_release);
        cb_ = nullptr;
    }
    bool isRunning() const override { return running_.load(std::memory_order_acquire); }
    double sampleRate() const override { return running_.load(std::memory_order_acquire) ? sampleRate_ : 0.0; }
    int32_t channelCount() const override { return running_.load(std::memory_order_acquire) ? 2 : 0; }
    std::string channelName(int32_t index) const override {
        if (!running_.load(std::memory_order_acquire) || index < 0 || index >= 2) return {};
        return index == 0 ? "Null In 1" : "Null In 2";
    }

  private:
    InputCallback cb_;
    double sampleRate_ = 0.0;
    std::atomic<bool> running_{false};
};

} // namespace

DriverCapabilities NullAudioDriver::capabilities() const noexcept {
    return DriverCapabilities{true, true, false, false, false};
}

std::vector<AudioDeviceInfo> NullAudioDriver::enumerateDevices(bool inputScope) const {
    return {AudioDeviceInfo{inputScope ? "null:in" : "null:out", inputScope ? "Null Input" : "Null Output"}};
}

uint32_t NullAudioDriver::resolveDeviceId(std::string_view /*uid*/, bool /*inputScope*/) const {
    return 0;
}

std::string NullAudioDriver::effectiveDeviceUid(std::string_view /*uid*/, bool inputScope) const {
    return inputScope ? "null:in" : "null:out";
}

std::unique_ptr<IAudioOutputStream> NullAudioDriver::createOutputStream() const {
    return std::make_unique<NullOutputStream>();
}

std::unique_ptr<IAudioInputStream> NullAudioDriver::createInputStream() const {
    return std::make_unique<NullInputStream>();
}

} // namespace nota::io::audio
