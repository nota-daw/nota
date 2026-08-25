// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// miniaudio/WASAPI output backend (Windows). Mirrors CoreAudioBackend: pulls
// interleaved stereo float from the engine's render callback. A zeroed
// BackendConfig resolves to the system default device; a non-zero cfg.device is
// a 1-based index into the enumerated playback devices (see MiniaudioDevices.h).

#pragma once

#include "AudioBackend.h"

namespace nota {

class MiniaudioBackend final : public AudioBackend {
public:
    MiniaudioBackend();
    ~MiniaudioBackend() override;

    bool    start(RenderCallback render, const BackendConfig& cfg) override;
    void    stop() override;
    double  sampleRate() const override { return sampleRate_; }
    int32_t bufferFrames() const override { return bufferFrames_; }
    void    setXrunCallback(std::function<void()> cb) override { onXrun_ = std::move(cb); }
    bool    isRunning() const override { return running_; }
    bool    exclusiveFallback() const override { return exclusiveFallback_; }

    // Opaque to callers; public so the file-local data callback can reach it.
    struct Impl;

    Impl*                 impl_ = nullptr;
    RenderCallback        render_;
    std::function<void()> onXrun_;
    double                sampleRate_ = 0.0;
    int32_t               bufferFrames_ = 0;
    bool                  running_ = false;
    bool                  exclusiveFallback_ = false;  // true if requested exclusive, fell back to shared
};

} // namespace nota
