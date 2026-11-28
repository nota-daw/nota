// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Instrument — synth/sampler/plugin instrument interface.

#pragma once

#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/Export.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/rt/AutomationTypes.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::devices {
class DrumRack;
class RackCore;
} // namespace nota::devices

namespace nota::dsp {

namespace capabilities {
class IRack;
} // namespace capabilities

NOTA_DSP_API uint64_t nextInstrumentUid() noexcept;

class NOTA_DSP_API Instrument {
  public:
    virtual nota::devices::RackCore* asRack() noexcept { return nullptr; }
    virtual const nota::devices::RackCore* asRack() const { return nullptr; }
    virtual nota::devices::DrumRack* asDrumRack() noexcept { return nullptr; }
    virtual const nota::devices::DrumRack* asDrumRack() const noexcept { return nullptr; }
    virtual const capabilities::IRack* asRackCapability() const { return nullptr; }
    virtual ~Instrument();

    virtual void setSampleRate(double sr) = 0;
    virtual void ensureBlockCapacity(int32_t /*frames*/) {}

    virtual bool takesBlockMidi() const { return false; }
    virtual void blockMidi(const MidiEv* /*evs*/, int32_t /*n*/, int32_t /*frames*/, double /*beatStart*/,
        double /*spb*/, bool /*playing*/) {}
    virtual void midiTimeJump(double /*deltaBeats*/) {}

    virtual bool takesTimedEvents() const { return false; }
    int32_t eventOffset = 0;
    int32_t eventChannel = 0;
    virtual void programChange(int32_t /*program*/) {}
    virtual bool takesArticulations() const { return false; }
    virtual void keyswitchOn(int32_t pitch, float velocity) { noteOn(pitch, velocity); }
    virtual void keyswitchOff(int32_t pitch) { noteOff(pitch); }

    virtual void noteOn(int32_t pitch, float velocity) = 0;
    virtual void noteOff(int32_t pitch) = 0;
    virtual void noteOffVelocity(int32_t pitch, float /*velocity*/) { noteOff(pitch); }
    virtual void allNotesOff() = 0;

    virtual void noteExpression(int32_t /*pitch*/, int32_t /*dim*/, float /*value*/) {}
    virtual bool supportsMpe() const { return false; }

    std::atomic<float> deliveredCtl[kCtlCount];
    bool folded = false;
    int32_t presetTag = 0;
    EditorPreview editorPreview;

    Instrument();
    virtual void controller(int32_t ctl, float value);

    virtual bool handlesSustain() const { return false; }
    virtual bool playsOneShot() const { return false; }
    virtual void noteOffHeldBack(int32_t /*pitch*/) {}

    std::atomic<bool> pedalDown{false};
    std::atomic<uint64_t> pedalHeld[2] = {0, 0};
    std::atomic<uint8_t> pedalRelVel[128] = {};

    virtual void render(float* out, int32_t frames) = 0;

    bool bypassed() const noexcept { return bypassed_.load(std::memory_order_relaxed); }
    void setBypassed(bool b) noexcept { bypassed_.store(b, std::memory_order_relaxed); }
    std::atomic<bool> offSettled{false};

    static constexpr int32_t kTapLen = 4096;

    void tapOutput(const float* stereo, int32_t frames) noexcept {
        if (tapBlocks_.load(std::memory_order_relaxed) <= 0 || !stereo || frames <= 0) return;
        tapBlocks_.fetch_sub(1, std::memory_order_relaxed);
        const uint32_t w = tapW_.load(std::memory_order_relaxed);
        for (int32_t i = 0; i < frames; ++i) {
            tap_[(w + static_cast<uint32_t>(i)) & (static_cast<uint32_t>(kTapLen) - 1u)].store(
                0.5f * (stereo[i * 2] + stereo[i * 2 + 1]), std::memory_order_relaxed);
        }
        tapW_.store(w + static_cast<uint32_t>(frames), std::memory_order_release);
    }

    void tapSilence(int32_t frames) noexcept {
        if (tapBlocks_.load(std::memory_order_relaxed) <= 0 || frames <= 0) return;
        tapBlocks_.fetch_sub(1, std::memory_order_relaxed);
        const uint32_t w = tapW_.load(std::memory_order_relaxed);
        for (int32_t i = 0; i < frames; ++i) {
            tap_[(w + static_cast<uint32_t>(i)) & (static_cast<uint32_t>(kTapLen) - 1u)].store(
                0.0f, std::memory_order_relaxed);
        }
        tapW_.store(w + static_cast<uint32_t>(frames), std::memory_order_release);
    }

    int32_t readOutput(float* out, int32_t maxN) const noexcept {
        if (!out || maxN <= 0) return 0;
        tapBlocks_.store(kTapHoldBlocks, std::memory_order_relaxed);
        const int32_t n = std::min<int32_t>(maxN, kTapLen);
        const uint32_t w = tapW_.load(std::memory_order_acquire);
        for (int32_t i = 0; i < n; ++i) {
            out[i] =
                tap_[(w - static_cast<uint32_t>(n) + static_cast<uint32_t>(i)) & (static_cast<uint32_t>(kTapLen) - 1u)]
                    .load(std::memory_order_relaxed);
        }
        return n;
    }

    virtual void setTransport(double /*beatStart*/, double /*samplesPerBeat*/, bool /*playing*/) {}
    virtual void setTransportInfo(const TransportInfo&) {}

    virtual bool acceptsSidechain() const { return false; }
    virtual int32_t sidechainSourceTrackId() const { return -1; }
    virtual void setSidechainSourceTrackId(int32_t /*trackId*/) {}
    virtual void setSidechain(const float* /*interleaved*/, int32_t /*frames*/) {}
    uint64_t sidechainPart() const noexcept { return scPart_.load(std::memory_order_relaxed); }
    void setSidechainPart(uint64_t part) noexcept { scPart_.store(part, std::memory_order_relaxed); }

    virtual int32_t partCount() const { return 0; }
    virtual uint64_t partId(int32_t /*index*/) const { return 0; }
    virtual int32_t partIndex(uint64_t /*id*/) const { return -1; }
    virtual std::string partName(int32_t /*index*/) const { return {}; }

    struct PartTap {
        uint64_t part = 0;
        float* pre = nullptr;
        float* post = nullptr;
    };
    virtual void setPartTaps(const PartTap* /*taps*/, int32_t /*n*/) {}

    class PartTaps {
      public:
        static constexpr int32_t kMax = 16;
        void set(const PartTap* taps, int32_t n) noexcept {
            n_ = taps ? std::clamp<int32_t>(n, 0, kMax) : 0;
            for (int32_t i = 0; i < n_; ++i) taps_[i] = taps[i];
            pos_ = 0;
        }
        bool any() const noexcept { return n_ > 0; }
        const PartTap* find(uint64_t part) const noexcept {
            for (int32_t i = 0; i < n_; ++i) {
                if (taps_[i].part == part) return &taps_[i];
            }
            return nullptr;
        }
        int32_t count() const noexcept { return n_; }
        const PartTap& at(int32_t i) const noexcept { return taps_[i]; }
        int32_t take(int32_t frames) noexcept {
            const int32_t p = pos_;
            pos_ += frames;
            return p;
        }

      private:
        PartTap taps_[kMax] = {};
        int32_t n_ = 0;
        int32_t pos_ = 0;
    };

    virtual int32_t latencySamples() const { return 0; }

    virtual bool hasTail() const { return false; }
    virtual bool isIdle() const { return false; }
    virtual bool needsIdleRender() const { return false; }
    virtual bool outputAlwaysSilent() const { return isIdle(); }
    virtual bool requiresActiveProcessing() const { return sleepMode() == PluginSleepMode::Never || editorOpen(); }
    virtual void wake() {}
    virtual void setSleepMode(PluginSleepMode mode);
    virtual PluginSleepMode sleepMode() const { return sleepMode_.load(std::memory_order_relaxed); }

    virtual int32_t activeVoiceCount() const { return -1; }
    virtual int32_t heldNotes(int32_t* /*out*/, int32_t /*maxN*/) const { return 0; }
    virtual int32_t scopeRead(float* /*out*/, int32_t /*maxN*/) const { return 0; }

    virtual int32_t kind() const { return -1; }
    virtual bool hostsPlugin() const { return kind() < 0; }
    virtual bool supportsParallelRender() const { return !hostsPlugin(); }
    virtual const char* displayName() const { return "Instrument"; }
    virtual std::string pluginIdentifier() const { return {}; }

    virtual int32_t pluginLoadState() const { return 0; }
    virtual int32_t pluginLoadReasonCode() const { return 0; }
    virtual std::string pluginLoadReasonArg() const { return {}; }
    virtual void pluginLoadAction(int32_t /*action*/) {}

    virtual void openEditor() {}
    virtual void closeEditor() {}
    virtual bool editorOpen() const { return false; }
    virtual void setEditorOpenForTest(bool /*open*/) {}

    virtual std::vector<uint8_t> getState() const { return {}; }
    virtual void setState(const uint8_t* /*data*/, int32_t /*size*/) {}

    virtual void action(int32_t /*id*/, int32_t /*iarg*/, float /*farg*/) {}
    virtual std::vector<uint8_t> editState() const { return {}; }
    virtual void setEditState(const uint8_t* /*data*/, int32_t /*size*/) {}

    virtual int32_t pluginParamCount() const { return 0; }
    virtual std::string pluginParamId(int32_t /*index*/) const { return {}; }
    virtual std::string pluginParamName(int32_t /*index*/) const { return {}; }
    virtual float pluginParamGet(int32_t /*index*/) const { return 0.0f; }
    virtual std::string pluginParamText(int32_t /*index*/) const { return {}; }
    virtual float pluginParamDefault(int32_t /*index*/) const { return 0.0f; }
    virtual void pluginParamSet(int32_t /*index*/, float /*normalized*/) {}
    virtual void pluginParamLane(
        int32_t /*index*/, const AutomationLane& /*lane*/, double /*beatFrom*/, double /*beatTo*/) {}
    virtual int32_t pluginParamIndexOfId(const std::string& /*id*/) const { return -1; }
    virtual int32_t pluginParamSteps(int32_t /*index*/) const { return 0; }
    virtual std::string pluginParamStepText(int32_t /*index*/, int32_t /*step*/) const { return {}; }
    virtual int32_t lastTouchedPluginParam() { return -1; }
    virtual int32_t takePluginGestureBegin() { return -1; }
    virtual int32_t takePluginGestureEnd() { return -1; }

    virtual std::shared_ptr<Instrument> clone() const { return nullptr; }

    uint64_t uid() const noexcept { return uid_; }
    void adoptUid(const Instrument& from) noexcept { uid_ = from.uid_; }

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
    uint64_t uid_ = nextInstrumentUid();
    std::atomic<uint64_t> scPart_{0};

    static constexpr int32_t kTapHoldBlocks = 400;
    std::atomic<float> tap_[kTapLen] = {};
    std::atomic<uint32_t> tapW_{0};
    mutable std::atomic<int32_t> tapBlocks_{0};
};

} // namespace nota::dsp
