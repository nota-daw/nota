// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CoreAudio output backend (macOS). Uses a HALOutput AudioUnit so a specific
// device, sample rate and buffer size can be requested (M7-1); with a zeroed
// BackendConfig it falls back to the system default output device. Opens every output
// channel of the device and places the engine's Main and Cue buses on them (specs/089).

#pragma once

#include "audio_io/AudioBackend.h"

namespace nota {

class CoreAudioBackend final : public AudioBackend {
  public:
    CoreAudioBackend();
    ~CoreAudioBackend() override;

    bool start(RenderCallback render, const BackendConfig& cfg) override;
    void stop() override;
    double sampleRate() const override { return sampleRate_; }
    int32_t bufferFrames() const override { return bufferFrames_; }
    void setXrunCallback(std::function<void()> cb) override { onXrun_ = std::move(cb); }
    bool isRunning() const override { return running_; }
    void* ioWorkgroup() const override;
    int32_t outputChannelCount() const override;
    std::string outputChannelName(int32_t index) const override;
    void setOutputRouting(const OutputRouting& r) override;
    int32_t pollDeviceEvent() override;

    // Opaque to callers; public so the file-local CoreAudio callback can reach it.
    struct Impl;

    Impl* impl_ = nullptr;
    RenderCallback render_;
    std::function<void()> onXrun_; // device overload notifier (M7-8)
    double sampleRate_ = 0.0;
    int32_t bufferFrames_ = 0;
    bool running_ = false;
};

} // namespace nota
