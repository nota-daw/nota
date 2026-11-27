// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/dsp/rt/DeviceTelemetry.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/Types.h"
#include "nota/media/AudioCodecTypes.h"

#include <cstdint>
#include <memory>
#include <utility>

namespace nota::engine {

struct StreamingStats {
    int32_t activeStreams = 0;
    int64_t streamThresholdBytes = 0;
    int64_t streamCacheBytes = 0;
    int64_t residentSampleBytes = 0;
    int64_t pagesFaulted = 0;
    int64_t pagesServedAsync = 0;
    int64_t pagesServedSync = 0;
    int64_t rtUnderruns = 0;
};

struct HybridStats {
    int32_t active = 0;
    int32_t liveTracks = 0;
    int32_t anticipativeTracks = 0;
    int32_t lookaheadFrames = 0;
    int32_t chunkFrames = 0;
    int64_t cacheHits = 0;
    int64_t cacheMisses = 0;
    int64_t invalidations = 0;
};

} // namespace nota::engine

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Diagnostics {
  public:
    Diagnostics() = default;
    explicit Diagnostics(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    void notifyXrun();
    int32_t xrunCount() const;
    float cpuLoad() const;

    void setTelemetryMode(dsp::TelemetryMode mode);
    dsp::TelemetryMode telemetryMode() const;

    bool setRenderThreads(int32_t count);
    int32_t renderThreads() const;
    double renderThreadsCpuSeconds() const;

    void setStreamThresholdBytes(int64_t bytes);
    int64_t streamThresholdBytes() const;
    void setSampleLoadPolicy(media::SampleLoadPolicy policy);
    media::SampleLoadPolicy sampleLoadPolicy() const;
    media::AudioDecodeOptions decodeOptions() const noexcept;
    bool isSampleStreaming(int64_t sampleId) const;
    engine::StreamingStats streamingStats() const;

    void setHybridBufferingMode(engine::HybridMode mode);
    engine::HybridMode hybridBufferingMode() const;
    engine::HybridStats hybridStats() const;

    void renderOffline(float* out, int32_t frames);
    void renderOffline(float* out, int32_t frames, double sampleRate);

    int32_t servicePluginLoads();
    int32_t waitPluginLoads(int32_t timeoutMs);

    void setDeferWarpBuild(bool defer);
    int64_t warpBuildStep(int32_t maxFrames);
    int64_t warpBuildRemainingFrames() const;
    int64_t warpBuildPoll();

    void recomputeRouting();
    int32_t sidechainRoutesFree() const;
    bool sidechainRouteInUse(int32_t sourceTrackId, int32_t part) const;
    static int32_t pluginEditorSleepSelftest(int32_t catalogIndex);

    bool xrunSelfTest();
    bool graphRetireSelfTest();
    bool automationSelfTest();
    int32_t tempoLaneSelfTest();
    int32_t masterAutomationSelfTest();
    bool automationCurveSelfTest();
    bool automationWriteSelfTest();
    bool automationDeviceRemapSelfTest();
    int32_t midiCtlSelfTest();
    bool pluginAutomationSelfTest();
    bool modulationSelfTest();
    bool rackSelfTest();
    bool rackDeviceSelfTest();
    bool timedEventsSelfTest();
    int32_t articulationSelfTest();
    bool drumRackSelfTest();
    bool audioRecordSelfTest();
    bool sessionAudioRecordSelfTest();
    bool monitorSharedClockSelfTest();
    static int32_t sessionPassSelfTest();
    static int32_t midiSurfaceSelfTest();

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
