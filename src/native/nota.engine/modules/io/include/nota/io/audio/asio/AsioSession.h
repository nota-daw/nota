// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/OutputRouter.h"
#include "nota/io/audio/asio/AsioDriverApi.h"
#include "nota/io/audio/asio/AsioSampleFormat.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::io::audio::asio {

/// Platform-independent ASIO duplex session driving a single injected AsioDriverApi.
class NOTA_IO_API AsioSession {
  public:
    enum class State { Closed, Prepared, Running, Failed };
    enum class Failure { None, NotInstalled, LoadFailed, InitFailed, UnsupportedFormat, NoOutputs, Timeout, Stopped };
    enum DeviceEvent : int32_t { kEventNone = 0, kEventResetRequested = 1, kEventDriverFailed = 2 };

    struct Options {
        double sampleRate = 0.0;
        int32_t outputPair = 0;
        int32_t inputPair = 0;
    };

    AsioSession() = default;
    ~AsioSession();
    AsioSession(const AsioSession&) = delete;
    AsioSession& operator=(const AsioSession&) = delete;

    bool open(std::unique_ptr<AsioDriverApi> driver, const Options& opt);
    void fail(Failure why, std::string detail = {});

    void setRenderCallback(RenderCallback cb);
    void setXrunCallback(XrunCallback cb);
    bool setInputCallback(const InputCallback* cb);

    bool start();
    void stop();
    void close();

    State state() const { return state_; }
    bool isRunning() const { return state_ == State::Running; }
    Failure failure() const { return failure_; }
    std::string failureText() const;
    const std::string& driverName() const { return driverName_; }

    double sampleRate() const { return sampleRate_; }
    int32_t bufferFrames() const { return bufferFrames_; }
    int32_t inputLatency() const { return inputLatency_; }
    int32_t outputLatency() const { return outputLatency_; }
    bool sampleRateRefused() const { return rateRefused_; }
    double driverChangedRate() const { return driverRate_; }
    bool hasInputs() const { return inCount_ > 0; }
    int32_t inputCount() const { return inCount_; }
    bool wedged() const { return wedged_; }
    const std::vector<ChannelInfo>& channels(bool input) const { return input ? inChannels_ : outChannels_; }
    int32_t outputPair() const { return outL_; }
    void setOutputRouting(const OutputRouting& r) { routing_.store(packRouting(r), std::memory_order_relaxed); }
    int32_t inputPair() const { return inPair_; }

    bool openControlPanel();
    bool panelOpen() const { return driver_ && driver_->panelOpen(); }

    int32_t pollEvent();
    void setWatchdogTimeout(std::chrono::milliseconds t) { watchdog_ = t; }

    void bufferSwitch(int32_t index, int64_t samplePosition);
    void sampleRateChanged(double rate);
    int32_t message(int32_t selector, int32_t value);

  private:
    struct Io {
        AsioDriverApi* driver = nullptr;
        int32_t frames = 0;
        bool outputReady = false;
        std::vector<BufferInfo> buffers;
        std::vector<SampleType> outTypes;
        std::vector<SampleType> inTypes;
        int32_t outL = -1, outR = -1;
        std::vector<int32_t> inBufs;
        int32_t inCount = 0;
        std::vector<float> interleaved;
        std::vector<float> cueInterleaved;
        std::vector<float> inInterleaved;
        RenderCallback render;
    };

    void releaseDriver();
    bool failOpen(Failure why, std::string detail);
    bool quiesce();

    std::unique_ptr<AsioDriverApi> driver_;
    std::unique_ptr<Io> io_;
    std::atomic<Io*> live_{nullptr};
    bool wedged_ = false;
    State state_ = State::Closed;
    Failure failure_ = Failure::None;
    std::string failureDetail_;
    std::string driverName_;
    bool buffersCreated_ = false;
    bool started_ = false;

    double sampleRate_ = 0.0;
    int32_t bufferFrames_ = 0;
    int32_t inputLatency_ = 0;
    int32_t outputLatency_ = 0;
    bool rateRefused_ = false;
    double driverRate_ = 0.0;

    std::vector<ChannelInfo> inChannels_, outChannels_;
    int32_t outL_ = -1, outR_ = -1;
    int32_t inPair_ = -1;
    int32_t inCount_ = 0;

    RenderCallback render_;
    XrunCallback xrun_;
    std::atomic<const InputCallback*> input_{nullptr};
    std::atomic<uint64_t> routing_{packRouting(OutputRouting{})};

    std::atomic<bool> inSwitch_{false};
    std::atomic<bool> resetRequested_{false};
    std::atomic<int64_t> lastTickNs_{0};
    int64_t lastPosition_ = -1;
    std::chrono::milliseconds watchdog_{2000};
};

} // namespace nota::io::audio::asio
