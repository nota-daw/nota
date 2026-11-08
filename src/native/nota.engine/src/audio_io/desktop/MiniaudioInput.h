// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// miniaudio input capture (Windows WASAPI, Linux). Mirrors CoreAudioInput: delivers
// interleaved float frames of every input channel (specs/086) to a callback on miniaudio's
// capture thread. miniaudio converts the sample format to f32 at the device's own channel
// count; channel names are not available here (the UI shows "Input N").

#pragma once

#include "audio_io/AudioInput.h"

namespace nota {

class MiniaudioInput final : public AudioInput {
  public:
    MiniaudioInput();
    ~MiniaudioInput() override;

    bool start(InputCallback cb, uint32_t device = 0) override;
    void stop() override;
    bool isRunning() const override { return running_; }
    double sampleRate() const override { return sampleRate_; }
    int32_t channelCount() const override;
    std::string channelName(int32_t) const override { return {}; }

    struct Impl; // opaque; owns the ma_device
    Impl* impl_ = nullptr;
    InputCallback cb_;
    double sampleRate_ = 0.0;
    int32_t channels_ = 0;
    bool running_ = false;
};

} // namespace nota
