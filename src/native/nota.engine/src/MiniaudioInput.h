// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// miniaudio/WASAPI input capture (Windows). Mirrors CoreAudioInput: delivers
// interleaved stereo float frames to a callback on miniaudio's capture thread.
// miniaudio converts the device format to f32/stereo for us, so no manual downmix.

#pragma once

#include "AudioInput.h"

namespace nota {

class MiniaudioInput final : public AudioInput {
public:
    MiniaudioInput();
    ~MiniaudioInput() override;

    bool   start(InputCallback cb, uint32_t device = 0) override;
    void   stop() override;
    bool   isRunning() const override { return running_; }
    double sampleRate() const override { return sampleRate_; }

    struct Impl;                 // opaque; owns the ma_device
    Impl*         impl_ = nullptr;
    InputCallback cb_;
    double        sampleRate_ = 0.0;
    bool          running_ = false;
};

} // namespace nota
