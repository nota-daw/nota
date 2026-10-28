// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The subset of the ASIO driver interface (IASIO) that AsioSession uses (specs/049,
// research R3/R11), in our own types so the session builds on every platform. Windows
// adapts the real COM driver (windows/ComAsioDriver); the self-test uses FakeAsioDriver.
// Every call except outputReady() comes from the session's control side (never the audio
// thread); outputReady() is called from inside the buffer switch, as ASIO specifies.

#pragma once

#include "AsioSampleFormat.h"

#include <cstdint>
#include <string>

namespace nota::asio {

// asioMessage selectors (values from the SDK's asio.h).
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
    void* buffers[2] = {nullptr, nullptr}; // filled by createBuffers
};

// Plain function pointers: ASIO callbacks carry no user data, so the session routes them
// through one static instance (one driver per process).
struct Callbacks {
    // `samplePosition` is the driver's running frame count, or -1 if it didn't report one.
    void (*bufferSwitch)(int32_t index, int64_t samplePosition) = nullptr;
    void (*sampleRateDidChange)(double rate) = nullptr;
    int32_t (*message)(int32_t selector, int32_t value) = nullptr;
};

class AsioDriverApi {
  public:
    virtual ~AsioDriverApi() = default;

    virtual bool init() = 0;
    virtual std::string driverName() = 0;
    virtual std::string errorMessage() = 0; // the driver's own text after a failure, may be ""
    virtual bool getChannels(int32_t& inputs, int32_t& outputs) = 0;
    virtual bool getBufferSize(int32_t& minSize, int32_t& maxSize, int32_t& preferred, int32_t& granularity) = 0;
    virtual bool canSampleRate(double rate) = 0;
    virtual bool getSampleRate(double& rate) = 0;
    virtual bool setSampleRate(double rate) = 0;
    virtual bool getChannelInfo(ChannelInfo& info) = 0; // index + isInput in, the rest out
    virtual bool createBuffers(BufferInfo* infos, int32_t count, int32_t bufferSize, const Callbacks& cb) = 0;
    virtual bool disposeBuffers() = 0;
    virtual bool start() = 0;
    virtual bool stop() = 0;
    virtual bool getLatencies(int32_t& input, int32_t& output) = 0;
    virtual bool controlPanel() = 0; // may return before the panel closes
    virtual bool outputReady() = 0;  // false = not supported by this driver
    // True once a call timed out: the driver is abandoned and every later call fails fast.
    virtual bool timedOut() const { return false; }
};

} // namespace nota::asio
