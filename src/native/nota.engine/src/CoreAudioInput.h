// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CoreAudio input capture (macOS, M4-3). A HAL AudioUnit bound to the default
// input device delivers interleaved stereo float frames to a callback on its
// own real-time thread. The engine pushes them onto a lock-free ring and
// materialises a clip on the message thread when recording stops.
//
// NOTE: capturing input triggers a microphone-permission (TCC) prompt, which
// only appears for a properly bundled `.app` with NSMicrophoneUsageDescription.
// Under a bare `dotnet run` process start() may fail — callers must handle that.

#pragma once

#include "AudioInput.h"

#include <cstdint>
#include <functional>

namespace nota {

class CoreAudioInput final : public AudioInput {
public:
    CoreAudioInput();
    ~CoreAudioInput() override;

    // Open the given input device (a CoreAudio AudioDeviceID carried as a plain
    // integer; 0 = system default input) and begin delivering frames to `cb`.
    bool   start(InputCallback cb, uint32_t device = 0) override;
    void   stop() override;
    bool   isRunning() const override { return running_; }
    double sampleRate() const override { return sampleRate_; }

    struct Impl;                 // opaque; owns the CoreAudio objects + scratch
    Impl*         impl_ = nullptr;
    InputCallback cb_;
    double        sampleRate_ = 0.0;
    bool          running_ = false;
};

} // namespace nota
