// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Device — insert audio effect interface & single-process lineage/reader registry.

#pragma once

#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/Export.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/rt/AudioSpan.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::devices {
class RackCore;
} // namespace nota::devices

namespace nota::dsp {

namespace capabilities {
class IRack;
} // namespace capabilities

struct NOTA_DSP_API HeldMidiKeyReader {
    HeldMidiKeyReader() noexcept;
    HeldMidiKeyReader(const HeldMidiKeyReader&) noexcept;
    HeldMidiKeyReader& operator=(const HeldMidiKeyReader&) noexcept { return *this; }
    ~HeldMidiKeyReader();
};

NOTA_DSP_API bool heldMidiKeysRead() noexcept;
NOTA_DSP_API uint64_t nextDeviceLineage() noexcept;

class NOTA_DSP_API Device {
  public:
    virtual const nota::devices::RackCore* asRack() const { return nullptr; }
    virtual const capabilities::IRack* asRackCapability() const { return nullptr; }
    virtual ~Device();

    uint64_t lineage() const noexcept { return lineage_; }
    void inheritLineage(uint64_t id) noexcept { lineage_ = id; }

    virtual void setSampleRate(double sr, int32_t maxBlock) = 0;
    virtual void ensureBlockCapacity(int32_t /*frames*/) {}

    virtual void process(float* buf, int32_t frames) = 0;

    virtual bool prefersPlanar() const { return false; }
    virtual void processPlanar(PlanarAudioSpan /*io*/, int32_t /*frames*/) {}

    virtual int32_t latencySamples() const { return 0; }

    virtual void openEditor() {}
    virtual void closeEditor() {}
    virtual bool editorOpen() const { return false; }
    virtual void setEditorOpenForTest(bool /*open*/) {}

    virtual std::vector<uint8_t> getState() const { return {}; }
    virtual void setState(const uint8_t* /*data*/, int32_t /*size*/) {}

    virtual const char* displayName() const { return "Device"; }
    virtual int32_t builtinKind() const { return -1; }
    virtual bool hostsPlugin() const { return builtinKind() < 0; }
    virtual bool supportsParallelRender() const { return !hostsPlugin(); }
    virtual std::string pluginIdentifier() const { return {}; }

    virtual int32_t pluginLoadState() const { return 0; }
    virtual int32_t pluginLoadReasonCode() const { return 0; }
    virtual std::string pluginLoadReasonArg() const { return {}; }
    virtual void pluginLoadAction(int32_t /*action*/) {}

    virtual int32_t paramCount() const { return 0; }
    virtual const char* paramName(int32_t /*index*/) const { return ""; }
    virtual float paramMin(int32_t /*index*/) const { return 0.0f; }
    virtual float paramMax(int32_t /*index*/) const { return 1.0f; }
    virtual float getParam(int32_t /*index*/) const { return 0.0f; }
    virtual void setParam(int32_t /*index*/, float /*value*/) {}
    virtual bool isLatencyConfigurationParam(int32_t /*index*/) const { return false; }
    virtual bool isPresetParam(int32_t /*index*/) const { return true; }
    void setLiveParam(int32_t index, float value) {
        if (!isLatencyConfigurationParam(index)) setParam(index, value);
    }

    virtual int32_t pluginParamCount() const { return 0; }
    virtual std::string pluginParamId(int32_t /*index*/) const { return {}; }
    virtual std::string pluginParamName(int32_t /*index*/) const { return {}; }
    virtual float pluginParamGet(int32_t /*index*/) const { return 0.0f; }
    virtual std::string pluginParamText(int32_t /*index*/) const { return {}; }
    virtual float pluginParamDefault(int32_t /*index*/) const { return 0.0f; }
    virtual void pluginParamSet(int32_t /*index*/, float /*normalized*/) {}
    virtual int32_t pluginParamIndexOfId(const std::string& /*id*/) const { return -1; }
    virtual int32_t lastTouchedPluginParam() { return -1; }
    virtual int32_t takePluginGestureBegin() { return -1; }
    virtual int32_t takePluginGestureEnd() { return -1; }

    virtual std::shared_ptr<Device> clone() const { return nullptr; }

    virtual bool hasTail() const { return false; }
    virtual bool isIdle() const { return false; }
    virtual bool requiresActiveProcessing() const { return sleepMode() == PluginSleepMode::Never || editorOpen(); }
    virtual void wake() {}
    virtual void setSleepMode(PluginSleepMode mode);
    virtual PluginSleepMode sleepMode() const { return sleepMode_.load(std::memory_order_relaxed); }

    virtual float gainReductionDb() const { return 0.0f; }
    virtual int32_t scopeRead(float* /*out*/, int32_t /*maxSamples*/) const { return 0; }
    virtual void deviceAction(int32_t /*id*/, int32_t /*iarg*/, float /*farg*/) {}
    virtual void paramsRestored(int32_t /*count*/) {}
    virtual int32_t layerWave(int32_t /*layer*/, float* /*out*/, int32_t /*maxSamples*/) const { return 0; }
    virtual std::string deviceText(int32_t /*id*/) const { return {}; }
    virtual bool loadFile(const std::string& /*path*/) { return false; }

    virtual int32_t sidechainSourceTrackId() const { return -1; }
    virtual void setSidechainSourceTrackId(int32_t /*trackId*/) {}
    virtual void setSidechain(const float* /*interleaved*/, int32_t /*frames*/) {}
    virtual void setSidechainLate(bool /*late*/) {}
    virtual void setPreviewKey(const float* /*interleaved*/, int32_t /*frames*/) {}
    virtual bool acceptsSidechain() const { return false; }

    virtual bool wantsMidiKey() const { return false; }
    virtual bool wantsMidiKeyWhenSilent() const { return false; }
    virtual void setMidiKey(const MidiEv* /*evs*/, int32_t /*n*/) {}
    virtual void setMidiKeyBlock(const MidiKeyBlock& block) {
        if (block.events) setMidiKey(block.events, block.count);
    }
    virtual bool wantsOwnMidiKey() const { return false; }

    float sidechainGainDb() const noexcept { return scGainDb_.load(std::memory_order_relaxed); }
    void setSidechainGainDb(float db) noexcept { scGainDb_.store(db, std::memory_order_relaxed); }
    float sidechainMix() const noexcept { return scMix_.load(std::memory_order_relaxed); }
    void setSidechainMix(float m) noexcept { scMix_.store(std::clamp(m, 0.0f, 1.0f), std::memory_order_relaxed); }
    int32_t sidechainTapPre() const noexcept { return scTapPre_.load(std::memory_order_relaxed); }
    void setSidechainTapPre(int32_t pre) noexcept { scTapPre_.store(pre != 0 ? 1 : 0, std::memory_order_relaxed); }
    uint64_t sidechainPart() const noexcept { return scPart_.load(std::memory_order_relaxed); }
    void setSidechainPart(uint64_t part) noexcept { scPart_.store(part, std::memory_order_relaxed); }

    virtual void setTransport(double /*beatStart*/, double /*samplesPerBeat*/, bool /*playing*/) {}
    virtual void setTransportInfo(const TransportInfo&) {}

    bool bypassed() const noexcept { return bypassed_.load(std::memory_order_relaxed); }
    void setBypassed(bool b) noexcept { bypassed_.store(b, std::memory_order_relaxed); }

    bool folded() const noexcept { return folded_; }
    void setFolded(bool f) noexcept { folded_ = f; }

    int32_t presetTag() const noexcept { return presetTag_; }
    void setPresetTag(int32_t t) noexcept { presetTag_ = t; }

    const EditorPreview& editorPreview() const noexcept { return preview_; }
    void setEditorPreview(EditorPreview p) noexcept { preview_ = std::move(p); }

    TelemetryMode telemetryMode() const noexcept { return telemetry_.mode(); }
    virtual void setTelemetryMode(TelemetryMode mode) { telemetry_.setMode(mode); }
    void observeTelemetry() const noexcept { telemetry_.observe(); }
    float telemetryCpuLoad() const noexcept { return telemetry_.cpuLoad(); }

  protected:
    DeviceTelemetry::ScopedBlockTimer blockTimer(int32_t frames, double sampleRate, double smoothing = 0.1) noexcept {
        return DeviceTelemetry::ScopedBlockTimer(telemetry_, frames, sampleRate, smoothing);
    }
    float readScopeCpuLoad() const noexcept { return telemetry_.observeAndReadCpu(); }
    void resetTelemetry() noexcept { telemetry_.reset(); }

  private:
    mutable DeviceTelemetry telemetry_;
    std::atomic<PluginSleepMode> sleepMode_{PluginSleepMode::Auto};
    std::atomic<bool> countedNeverSleep_{false};
    std::atomic<bool> bypassed_{false};
    bool folded_ = false;
    int32_t presetTag_ = 0;
    EditorPreview preview_;
    std::atomic<float> scGainDb_{0.0f};
    std::atomic<float> scMix_{1.0f};
    std::atomic<int32_t> scTapPre_{0};
    std::atomic<uint64_t> scPart_{0};
    uint64_t lineage_ = nextDeviceLineage();
};

} // namespace nota::dsp
