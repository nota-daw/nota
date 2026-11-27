// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInstrument — parallel-chain Instrument Rack with key/velocity zones, MIDI FX & mono glide.

#pragma once

#include "nota/devices/Export.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiTypes.h"
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

class NOTA_DEVICES_API RackInstrument : public dsp::Instrument, public RackCore {
  public:
    RackInstrument();
    ~RackInstrument() override;

    int32_t kind() const override { return 3; }
    const RackCore* asRack() const override { return this; }
    const dsp::capabilities::IRack* asRackCapability() const override { return this; }
    const char* displayName() const override { return "Nota Instrument Rack"; }
    bool hostsPlugin() const override { return chainsHostPlugin(); }

    void setSampleRate(double sr) override { setRates(sr, kMaxBlock); }
    void ensureBlockCapacity(int32_t frames) override {
        arena_.ensureCapacity(frames);
        ensureChainBlockCapacity(frames);
    }
    void setTelemetryMode(dsp::TelemetryMode mode) override {
        dsp::Instrument::setTelemetryMode(mode);
        RackCore::setTelemetryMode(mode);
    }

    void setTransportInfo(const dsp::TransportInfo& ti) override { forwardTransport(ti); }
    void setTransport(double beatStart, double spb, bool playing) override { forwardTempo(beatStart, spb, playing); }

    bool takesBlockMidi() const override;
    void blockMidi(const dsp::MidiEv* evs, int n, int32_t frames, double beatStart, double spb, bool playing) override;
    void midiTimeJump(double deltaBeats) override;
    static constexpr int32_t kMaxMidiChains = 16;

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void noteOffVelocity(int32_t pitch, float velocity) override;
    void allNotesOff() override;
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void controller(int32_t ctl, float value) override;
    void programChange(int32_t program) override;
    void keyswitchOn(int32_t pitch, float velocity) override;
    void keyswitchOff(int32_t pitch) override;
    bool takesArticulations() const override;
    bool supportsMpe() const override;

    void render(float* out, int32_t frames) override;

    bool hasTail() const override { return true; }
    bool isIdle() const override;
    bool needsIdleRender() const override { return hasPendingRetire(); }
    bool requiresActiveProcessing() const override {
        return dsp::Instrument::requiresActiveProcessing() || chainsRequireActiveProcessing(true);
    }
    void wake() override { wakeChains(true); }
    void setSleepMode(dsp::PluginSleepMode mode) override {
        dsp::Instrument::setSleepMode(mode);
        wake();
    }

    int32_t latencySamples() const override { return maxChainLatency(); }

    int32_t partCount() const override;
    uint64_t partId(int32_t index) const override { return chainBaseUid(index); }
    int32_t partIndex(uint64_t id) const override;
    std::string partName(int32_t index) const override;
    void setPartTaps(const PartTap* taps, int32_t n) override { taps_.set(taps, n); }

    void shareMonoWith(const RackInstrument& o) { mono_ = o.mono_; }

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
    std::shared_ptr<dsp::Instrument> clone() const override;

  protected:
    dsp::StereoScratchArena<1> arena_;

    static void addTap(float* dst, const float* src, int32_t frames) {
        for (int32_t i = 0; i < frames * 2; ++i) dst[i] += src[i];
    }
    PartTaps taps_;

  private:
    void fanOn(int32_t pitch, float velocity, int32_t glideFrom);
    void fanOff(int32_t pitch);
    float relVel_ = 0.0f;
    void monoRemove(int32_t pitch);
    void syncMono();
    void endGlide();
    void syncChains();
    void deliver(int32_t c, dsp::Instrument& inst, const dsp::MidiEv& ev);

    struct MonoState {
        int8_t held[128] = {};
        int32_t heldN = 0;
        float heldVel[128] = {};
        int32_t sounding = -1;
        float glideBend = 0.0f;
        int64_t glideTotal = 0, glideLeft = 0;
        bool wasMono = false;
        uint16_t midiRoute[128] = {};
        uint8_t outHeld[kMaxMidiChains][128] = {};
        const void* chainId[kMaxMidiChains] = {};
        uint64_t chainSig[kMaxMidiChains] = {};
        uint64_t structure = 0;
    };
    std::shared_ptr<MonoState> mono_ = std::make_shared<MonoState>();

    static constexpr int32_t kChainEvents = 1024;
    dsp::MidiEv* chainEvs_ = nullptr;
    dsp::MidiEv* scratchA_ = nullptr;
    dsp::MidiEv* scratchB_ = nullptr;
    std::unique_ptr<dsp::MidiEv[]> evStore_;
    int32_t chainCount_[kMaxMidiChains] = {}, chainCursor_[kMaxMidiChains] = {};
    int32_t blockPos_ = 0;
    uint16_t evMask_[1024] = {};
    NOTA_NOINLINE void renderChunk(RackState& st, float* out, int32_t frames, int32_t tapAt);
    bool midiChainAt(int32_t c) const;
};

} // namespace nota::devices
