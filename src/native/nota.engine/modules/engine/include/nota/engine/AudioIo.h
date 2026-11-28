// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/dsp/rt/OutputRouting.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/io/audio/AudioIoTypes.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::AudioIo {
  public:
    AudioIo() = default;
    explicit AudioIo(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    bool start();
    void stop();
    double sampleRate() const;
    bool isRunning() const;
    int32_t bufferFrames() const;
    bool exclusiveFallback() const;
    bool silentFallback() const;

    void renderDeviceBlock(float* out, int32_t frames);
    void renderDeviceBlock(float* mainOut, float* cueOut, int32_t frames);

    io::audio::AudioConfig config() const;
    void setOutputDevice(const std::string& uid);
    void setInputDevice(const std::string& uid);
    void setSampleRate(double sr);
    void setBufferFrames(int32_t frames);
    void setWasapiExclusive(bool on);
    bool applyConfig();

    void setSystem(io::audio::AudioSystem system);
    io::audio::AudioSystem activeSystem() const;
    std::string fallbackReason() const;
    void setAsioDriver(const std::string& clsid, const std::string& name);
    void setAsioPair(bool input, int32_t firstChannel);
    bool openControlPanel();
    bool isControlPanelOpen() const;

    bool setOutputRouting(const dsp::OutputRouting& routing);
    dsp::OutputRouting outputRouting() const;
    int32_t outputChannelCount() const;
    std::string outputChannelName(int32_t index) const;
    int32_t outputRoutingState() const;
    void setTestOutputDevice(int32_t channels);

    int32_t inputChannelCount() const;
    std::string inputChannelName(int32_t index) const;
    int32_t probeInput();

    int32_t inputLatencyFrames() const;
    int32_t outputLatencyFrames() const;
    int32_t driverChannelCount(bool isInput) const;
    std::string driverChannelName(bool isInput, int32_t index) const;
    int32_t pollDeviceEvent();

    static double latencyCompensatedStart(
        double startBeat, int32_t latencyFrames, double samplesPerBeat, double* trimFrames) noexcept;

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
