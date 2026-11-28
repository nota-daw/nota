// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/audio/asio/AsioDriverApi.h"
#include "nota/io/audio/asio/AsioSampleFormat.h"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace nota::test {

/// Deterministic in-memory ASIO driver fixture for cross-platform session/routing/format tests.
class FakeAsioDriver final : public nota::io::audio::asio::AsioDriverApi {
  public:
    using BufferInfo = nota::io::audio::asio::BufferInfo;
    using Callbacks = nota::io::audio::asio::Callbacks;
    using ChannelInfo = nota::io::audio::asio::ChannelInfo;
    using SampleType = nota::io::audio::asio::SampleType;

    struct Config {
        int32_t inputs = 2;
        int32_t outputs = 2;
        SampleType inputType = SampleType::Float32LSB;
        SampleType outputType = SampleType::Float32LSB;
        int32_t bufferFrames = 64;
        double sampleRate = 48000.0;
        std::vector<double> sampleRates{44100.0, 48000.0};
        int32_t inputLatency = 0;
        int32_t outputLatency = 0;
        bool initFails = false;
        bool supportsOutputReady = true;
        std::function<float(int32_t ch, int64_t frame)> inputSignal;
    };

    explicit FakeAsioDriver(Config cfg);

    bool init() override;
    std::string driverName() override { return "Fake ASIO"; }
    std::string errorMessage() override { return error_; }
    bool getChannels(int32_t& inputs, int32_t& outputs) override;
    bool getBufferSize(int32_t& minSize, int32_t& maxSize, int32_t& preferred, int32_t& granularity) override;
    bool canSampleRate(double rate) override;
    bool getSampleRate(double& rate) override;
    bool setSampleRate(double rate) override;
    bool getChannelInfo(ChannelInfo& info) override;
    bool createBuffers(BufferInfo* infos, int32_t count, int32_t bufferSize, const Callbacks& cb) override;
    bool disposeBuffers() override;
    bool start() override;
    bool stop() override;
    bool getLatencies(int32_t& input, int32_t& output) override;
    bool controlPanel() override { return true; }
    bool outputReady() override;
    bool panelOpen() const override { return panelOpen_; }

    void pump(int32_t switches, bool evenIfStopped = false);
    void setStopsCallingBack(bool on) { silent_ = on; }
    void setPanelOpen(bool on) { panelOpen_ = on; }
    void setPositionStep(int32_t frames) { positionStep_ = frames; }
    int32_t sendMessage(int32_t selector, int32_t value = 0);
    void changeSampleRate(double rate);
    void setOutputType(int32_t channel, SampleType type);

    const std::vector<float>& written(int32_t ch) const { return written_[static_cast<std::size_t>(ch)]; }
    bool nonZeroBytes(int32_t ch) const { return nonZero_[static_cast<std::size_t>(ch)]; }
    int32_t switches() const { return switches_; }
    int32_t outputReadyCalls() const { return outputReadyCalls_; }
    bool running() const { return running_; }

  private:
    Config cfg_;
    std::vector<SampleType> outTypes_;
    std::string error_;
    bool initialized_ = false;
    bool running_ = false;
    bool silent_ = false;
    bool panelOpen_ = false;
    Callbacks cb_{};
    int32_t bufferFrames_ = 0;
    std::vector<BufferInfo> infos_;
    std::vector<std::vector<uint8_t>> storage_;
    std::vector<std::vector<float>> written_;
    std::vector<bool> nonZero_;
    int32_t switches_ = 0;
    int32_t outputReadyCalls_ = 0;
    int32_t positionStep_ = 0;
    int64_t position_ = 0;
};

} // namespace nota::test
