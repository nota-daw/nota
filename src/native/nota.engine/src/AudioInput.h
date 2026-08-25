// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio input capture abstraction. macOS ships a CoreAudio (HAL) implementation
// (CoreAudioInput); the Windows port ships a miniaudio/WASAPI one. The engine
// talks only to this interface (via createAudioInput() in AudioBackendFactory)
// so the capture layer stays swappable, mirroring AudioBackend for output.

#pragma once

#include <cstdint>
#include <functional>

namespace nota {

// Called on the backend's real-time capture thread; MUST be real-time safe (no
// alloc/lock/IO). `interleaved` holds `numFrames` stereo frames (2 floats each).
using InputCallback = std::function<void(const float* interleaved, int32_t numFrames)>;

class AudioInput {
public:
    virtual ~AudioInput() = default;

    // Open the given input device (a backend-specific handle carried as a plain
    // integer; 0 = system default input) and begin delivering frames to `cb`.
    virtual bool   start(InputCallback cb, uint32_t device = 0) = 0;
    virtual void   stop() = 0;
    virtual bool   isRunning() const = 0;
    virtual double sampleRate() const = 0;
};

} // namespace nota
