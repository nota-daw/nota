// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio input capture abstraction. macOS ships a CoreAudio (HAL) implementation
// (CoreAudioInput); Windows and Linux a miniaudio one, and Windows also AsioInput,
// which shares the running ASIO driver with AsioBackend instead of opening a device
// (specs/049). The engine talks only to this interface (via createAudioInput() in
// AudioBackendFactory) so the capture layer stays swappable, mirroring AudioBackend.

#pragma once

#include <cstdint>
#include <functional>
#include <string>

namespace nota {

// Device inputs the engine can address; a device with more is clipped to this many (specs/086).
inline constexpr int32_t kMaxInputChannels = 64;

// Called on the backend's real-time capture thread; MUST be real-time safe (no
// alloc/lock/IO). `interleaved` holds `numFrames` frames of every device input channel
// (`numChannels` floats each — the device's real count, which may exceed
// kMaxInputChannels; the channels past it are never addressed) — the engine picks the
// columns its tracks use (specs/086).
using InputCallback = std::function<void(const float* interleaved, int32_t numFrames, int32_t numChannels)>;

class AudioInput {
  public:
    virtual ~AudioInput() = default;

    // Open the given input device (a backend-specific handle carried as a plain
    // integer; 0 = system default input) and begin delivering frames to `cb`.
    virtual bool start(InputCallback cb, uint32_t device = 0) = 0;
    virtual void stop() = 0;
    virtual bool isRunning() const = 0;
    virtual double sampleRate() const = 0;
    // The open device's input channels (0 when not running), clipped to kMaxInputChannels,
    // and the name the device gives one ("" when it gives none). Control thread only.
    virtual int32_t channelCount() const = 0;
    virtual std::string channelName(int32_t index) const = 0;
};

} // namespace nota
