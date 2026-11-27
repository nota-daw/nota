// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice — parallel-chain Audio Effect Rack (Parallel / Series / Select).

#pragma once

#include "nota/devices/Export.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/capabilities/IRack.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/dsp/rt/ScratchArena.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/TransportInfo.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::devices {

class NOTA_DEVICES_API RackDevice final : public dsp::Device, public RackCore {
  public:
    ~RackDevice() override;

    int32_t builtinKind() const override { return 5; }
    const RackCore* asRack() const override { return this; }
    const dsp::capabilities::IRack* asRackCapability() const override { return this; }
    const char* displayName() const override { return "Nota Audio Effect Rack"; }
    bool hostsPlugin() const override { return chainsHostPlugin(); }

    void setSampleRate(double sr, int32_t maxBlock) override { setRates(sr, maxBlock); }
    void ensureBlockCapacity(int32_t frames) override {
        arena_.ensureCapacity(frames);
        ensureChainBlockCapacity(frames);
    }
    void setTelemetryMode(dsp::TelemetryMode mode) override {
        dsp::Device::setTelemetryMode(mode);
        RackCore::setTelemetryMode(mode);
    }

    void setTransportInfo(const dsp::TransportInfo& ti) override { forwardTransport(ti); }
    void setTransport(double beatStart, double spb, bool playing) override { forwardTempo(beatStart, spb, playing); }

    void process(float* buf, int32_t frames) override;

    bool hasTail() const override { return chainsHaveTail(); }
    bool requiresActiveProcessing() const override {
        return dsp::Device::requiresActiveProcessing() || chainsRequireActiveProcessing(false);
    }
    bool isIdle() const override {
        if (requiresActiveProcessing() || hasPendingRetire()) return false;
        return chainsIdle(false);
    }
    void wake() override { wakeChains(false); }
    void setSleepMode(dsp::PluginSleepMode mode) override {
        dsp::Device::setSleepMode(mode);
        wake();
    }

    int32_t latencySamples() const override;

    int32_t pluginParamCount() const override { return kNumMacros; }
    std::string pluginParamId(int32_t i) const override { return macroParamId(i); }
    std::string pluginParamName(int32_t i) const override { return macroParamName(i); }
    float pluginParamGet(int32_t i) const override { return macroGet(i); }
    void pluginParamSet(int32_t i, float v) override { macroSet(i, v); }
    int32_t pluginParamIndexOfId(const std::string& id) const override { return macroParamIndexOfId(id); }

    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override {
        deserialize(data, size);
        ensureChainBlockCapacity(arena_.capacityFrames());
        wake();
    }
    std::shared_ptr<dsp::Device> clone() const override;

  private:
    NOTA_NOINLINE void processActive(RackState& st, float* buf, int32_t frames);
    NOTA_NOINLINE void processEmptySingleChain(RackState& st, float* buf, int32_t frames);
    NOTA_NOINLINE void processChunk(RackState& st, float* buf, int32_t frames);
    dsp::StereoScratchArena<3> arena_;
};

} // namespace nota::devices
