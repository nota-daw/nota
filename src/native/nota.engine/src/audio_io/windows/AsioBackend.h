// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AsioBackend — AudioBackend over the process-wide AsioSession (specs/049, research R2).
// start() finds the configured driver (CLSID, then name), loads it through ComAsioDriver,
// applies the requested sample rate if the driver supports it, opens the chosen output
// pair and starts. The driver decides the buffer size (FR-008). stop() releases the driver
// so other programs can use it (FR-012). A failed start() leaves a reason for the engine's
// WASAPI fallback (FR-011).

#pragma once

#include "audio_io/AudioBackend.h"
#include "audio_io/asio/AsioSession.h"

#include <functional>
#include <string>

namespace nota::asio {

// The one ASIO session of the process, shared by AsioBackend and AsioInput.
AsioSession& sharedAsioSession();

} // namespace nota::asio

namespace nota {

class AsioBackend final : public AudioBackend {
  public:
    ~AsioBackend() override;

    bool start(RenderCallback render, const BackendConfig& cfg) override;
    void stop() override;
    double sampleRate() const override;
    int32_t bufferFrames() const override;
    bool isRunning() const override;
    void setXrunCallback(std::function<void()> cb) override { xrun_ = std::move(cb); }

    int32_t inputLatencyFrames() const override;
    int32_t outputLatencyFrames() const override;
    bool sharesInputClock() const override { return true; }
    int32_t pollDeviceEvent() override;
    const char* fallbackReason() const override { return reason_.c_str(); }

  private:
    std::function<void()> xrun_;
    std::string reason_;
};

} // namespace nota
