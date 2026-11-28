// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/IAudioInputStream.h"
#include "nota/io/audio/OutputRouter.h"

#include <cstdint>
#include <memory>
#include <string>

namespace nota::io::audio {

/// Audio output playback stream abstraction (supports optional duplex coupled input and driver panels).
class NOTA_IO_API IAudioOutputStream {
  public:
    virtual ~IAudioOutputStream() = default;

    virtual bool start(RenderCallback render, const OutputStreamConfig& cfg) = 0;
    virtual void stop() = 0;
    virtual double sampleRate() const = 0;
    virtual int32_t bufferFrames() const = 0;
    virtual bool isRunning() const = 0;

    virtual bool exclusiveFallback() const { return false; }
    virtual bool silentFallback() const { return false; }
    virtual void setXrunCallback(XrunCallback cb) { (void)cb; }

    virtual int32_t inputLatencyFrames() const { return 0; }
    virtual int32_t outputLatencyFrames() const { return 0; }
    virtual bool sharesInputClock() const { return false; }
    virtual int32_t pollDeviceEvent() { return 0; }
    virtual const char* fallbackReason() const { return ""; }
    virtual bool sampleRateRefused() const { return false; }
    virtual void* ioWorkgroup() const { return nullptr; }

    virtual int32_t outputChannelCount() const { return isRunning() ? 2 : 0; }
    virtual std::string outputChannelName(int32_t /*index*/) const { return {}; }
    virtual void setOutputRouting(const OutputRouting& r) { (void)r; }
    virtual double deviceChangedRate() const { return 0.0; }

    /// Optional driver-coupled capture stream (e.g. ASIO full duplex on a single clock).
    [[nodiscard]] virtual std::unique_ptr<IAudioInputStream> createCoupledInputStream() { return nullptr; }

    /// Optional driver-level channel inspection and native control panel hooks (used by ASIO).
    virtual int32_t driverChannelCount(bool /*isInput*/) const { return 0; }
    virtual std::string driverChannelName(bool /*isInput*/, int32_t /*index*/) const { return {}; }
    virtual bool openControlPanel() { return false; }
    virtual bool isControlPanelOpen() const { return false; }
};

} // namespace nota::io::audio
