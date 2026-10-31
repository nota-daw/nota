// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// FakeAsioDriver — a scripted AsioDriverApi for nota_asio_selftest (specs/049, research
// R11). Configurable channels, sample types, buffer size, rates and latencies; a scripted
// input signal; switches for "init fails" and "stops calling back". pump(n) drives n buffer
// switches synchronously on the caller's thread and records what the host wrote to every
// output channel, so tests are deterministic on every platform.

#pragma once

#include "AsioDriverApi.h"

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace nota::asio {

class FakeAsioDriver final : public AsioDriverApi {
  public:
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
        // Input channel `ch` at absolute frame `frame` (nullptr = silence).
        std::function<float(int32_t ch, int64_t frame)> inputSignal;
    };

    explicit FakeAsioDriver(Config cfg);

    // ---- AsioDriverApi ----
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

    // ---- test controls ----
    // Run `switches` buffer switches (alternating halves). Does nothing while stopped unless
    // `evenIfStopped` (a late callback after stop()), or while "stops calling back" is set.
    void pump(int32_t switches, bool evenIfStopped = false);
    void setStopsCallingBack(bool on) { silent_ = on; }
    void setPanelOpen(bool on) { panelOpen_ = on; } // the user has the driver's panel open
    // Advance the reported sample position by this many frames per switch (0 = the buffer
    // size); a different value imitates a buffer-size change with no reset message.
    void setPositionStep(int32_t frames) { positionStep_ = frames; }
    int32_t sendMessage(int32_t selector, int32_t value = 0);
    void changeSampleRate(double rate); // the driver's clock moved: sampleRateDidChange
    // Change one output channel's type (before the host opens the driver).
    void setOutputType(int32_t channel, SampleType type);

    // What the host wrote to output `ch`, per switch, as float (in the channel's format).
    const std::vector<float>& written(int32_t ch) const { return written_[static_cast<size_t>(ch)]; }
    // True if any byte the host left in output `ch` after a switch was non-zero.
    bool nonZeroBytes(int32_t ch) const { return nonZero_[static_cast<size_t>(ch)]; }
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
    Callbacks cb_;
    int32_t bufferFrames_ = 0;
    std::vector<BufferInfo> infos_;
    std::vector<std::vector<uint8_t>> storage_; // two halves per BufferInfo
    std::vector<std::vector<float>> written_;
    std::vector<bool> nonZero_;
    int32_t switches_ = 0;
    int32_t outputReadyCalls_ = 0;
    int32_t positionStep_ = 0;
    int64_t position_ = 0;
};

} // namespace nota::asio
