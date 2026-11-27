// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/asio/AsioSampleFormat.h"

#include <cstdint>
#include <string>

namespace nota::io::audio::asio {

enum MessageSelector : int32_t {
    kSelectorSupported = 1,
    kEngineVersion = 2,
    kResetRequest = 3,
    kBufferSizeChange = 4,
    kResyncRequest = 5,
    kLatenciesChanged = 6,
    kSupportsTimeInfo = 7,
    kSupportsTimeCode = 8,
    kOverload = 15,
};

struct ChannelInfo {
    int32_t index = 0;
    bool isInput = false;
    SampleType type = SampleType::Float32LSB;
    std::string name;
};

struct BufferInfo {
    bool isInput = false;
    int32_t channel = 0;
    void* buffers[2] = {nullptr, nullptr};
};

struct Callbacks {
    void (*bufferSwitch)(int32_t index, int64_t samplePosition) = nullptr;
    void (*sampleRateDidChange)(double rate) = nullptr;
    int32_t (*message)(int32_t selector, int32_t value) = nullptr;
};

class NOTA_IO_API AsioDriverApi {
  public:
    virtual ~AsioDriverApi() = default;

    virtual bool init() = 0;
    virtual std::string driverName() = 0;
    virtual std::string errorMessage() = 0;
    virtual bool getChannels(int32_t& inputs, int32_t& outputs) = 0;
    virtual bool getBufferSize(int32_t& minSize, int32_t& maxSize, int32_t& preferred, int32_t& granularity) = 0;
    virtual bool canSampleRate(double rate) = 0;
    virtual bool getSampleRate(double& rate) = 0;
    virtual bool setSampleRate(double rate) = 0;
    virtual bool getChannelInfo(ChannelInfo& info) = 0;
    virtual bool createBuffers(BufferInfo* infos, int32_t count, int32_t bufferSize, const Callbacks& cb) = 0;
    virtual bool disposeBuffers() = 0;
    virtual bool start() = 0;
    virtual bool stop() = 0;
    virtual bool getLatencies(int32_t& input, int32_t& output) = 0;
    virtual bool controlPanel() = 0;
    virtual bool outputReady() = 0;
    virtual bool probeOutputReady() { return outputReady(); }
    virtual bool timedOut() const { return false; }
    virtual bool panelOpen() const { return false; }
};

} // namespace nota::io::audio::asio
