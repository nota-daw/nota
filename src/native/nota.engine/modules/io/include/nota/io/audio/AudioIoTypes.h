// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/dsp/rt/OutputRouting.h"

#include <cstdint>
#include <functional>
#include <string>

namespace nota::io::audio {

inline constexpr int32_t kMaxInputChannels = 64;
inline constexpr int32_t kMaxOutputChannels = nota::dsp::kMaxOutputChannels;

enum class AudioSystem : int32_t {
    Native = 0,
    Wasapi = 0,
    Asio = 1,
};

enum class AudioDeviceEvent : int32_t {
    None = 0,
    ResetRequested = 1,
    DriverFailed = 2,
};

struct DriverCapabilities {
    bool hasOutput = true;
    bool hasInput = true;
    bool sharesInputClock = false;
    bool supportsExclusive = false;
    bool supportsControlPanel = false;
};

using RenderCallback = std::function<void(float* main, float* cue, int32_t numFrames)>;
using InputCallback = std::function<void(const float* interleaved, int32_t numFrames, int32_t numChannels)>;
using XrunCallback = std::function<void()>;

struct OutputStreamConfig {
    uint32_t device = 0;
    double sampleRate = 0.0;
    int32_t bufferFrames = 0;
    bool wasapiExclusive = false;
    std::string asioDriverClsid;
    std::string asioDriverName;
    int32_t asioOutputPair = 0;
    int32_t asioInputPair = 0;
};

struct AudioConfig {
    std::string outputDeviceUid;
    std::string inputDeviceUid;
    double sampleRate = 0.0;
    int32_t bufferFrames = 0;
    bool wasapiExclusive = false;
    AudioSystem audioSystem = AudioSystem::Wasapi;
    std::string asioDriverClsid;
    std::string asioDriverName;
    int32_t asioOutputPair = 0;
    int32_t asioInputPair = 0;
};

struct AudioDeviceInfo {
    std::string uid;
    std::string name;
};

} // namespace nota::io::audio
