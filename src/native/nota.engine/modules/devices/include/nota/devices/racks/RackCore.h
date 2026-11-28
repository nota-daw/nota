// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackCore — shared parallel-chain, macro, snapshot & persistence foundation for Nota's racks.

#pragma once

#include "nota/devices/Export.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiDevice.h"
#include "nota/dsp/capabilities/IRack.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/dsp/rt/ScratchArena.h"
#include "nota/dsp/rt/SnapshotHistory.h"
#include "nota/dsp/rt/TransportInfo.h"
#include "nota/media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <functional>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::devices {

class NOTA_DEVICES_API RackCore : public dsp::capabilities::IRack {
  public:
    static constexpr int32_t kNumMacros = 8;
    static constexpr int32_t kMaxBlock = 16384;

    struct ChainControls {
        std::atomic<float> gain{1.0f};
        std::atomic<float> pan{0.0f};
        std::atomic<bool> mute{false};
        std::atomic<bool> solo{false};
        std::atomic<int32_t> triggerNote{-1};
        std::atomic<int32_t> keyLo{0}, keyHi{127};
        std::atomic<int32_t> velLo{0}, velHi{127};
        std::atomic<float> meter{0.0f};
        std::atomic<int32_t> chokeGroup{0};
        std::atomic<int32_t> tune{0};
        std::atomic<float> decay{1.0f};
        std::atomic<float> ampEnv{0.0f};
        bool retrig = false;
        uint32_t hitSerial = 0;
        bool silentNow = false;
        std::atomic<int32_t> selLo{0}, selHi{127};
        std::atomic<int32_t> transpose{0};
        std::atomic<float> velScale{1.0f};
    };

    static uint64_t nextChainUid() noexcept;

    struct RackChain {
        uint64_t uid = nextChainUid();
        uint32_t instGen = 0;
        std::shared_ptr<dsp::Instrument> instrument;
        std::vector<std::shared_ptr<dsp::Device>> devices;
        std::vector<std::shared_ptr<dsp::MidiDevice>> midiDevices;
        std::shared_ptr<ChainControls> ctl = std::make_shared<ChainControls>();
        std::string name;
    };

    struct MacroMapping {
        int32_t macro = 0;
        int32_t chainIndex = 0;
        int32_t deviceIndex = -1;
        int32_t paramIndex = 0;
        float rangeMin = 0.0f;
        float rangeMax = 1.0f;
        int32_t curve = 0;
    };

    static constexpr int32_t kPadControls = -2;
    static constexpr int32_t kRackControl = -3;
    enum RackParam : int32_t { RackSelector = 0, kRackParams };
    static constexpr int32_t kMidiDeviceBase = -1000;
    static bool isMidiTarget(int32_t deviceIndex) { return deviceIndex <= kMidiDeviceBase; }
    static int32_t midiTargetIndex(int32_t deviceIndex) { return kMidiDeviceBase - deviceIndex; }
    static int32_t midiTarget(int32_t midiIndex) { return kMidiDeviceBase - midiIndex; }
    enum PadParam : int32_t { PadVolume = 0, PadPan, PadTune, PadDecay, kPadParams };
    static const char* padParamName(int32_t p) {
        static const char* n[kPadParams] = {"Volume", "Pan", "Tune", "Decay"};
        return (p >= 0 && p < kPadParams) ? n[p] : "";
    }
    static float padParamMin(int32_t p) { return p == PadPan ? -1.0f : p == PadTune ? -48.0f : 0.0f; }
    static float padParamMax(int32_t p) { return p == PadVolume ? 2.0f : p == PadTune ? 48.0f : 1.0f; }

    struct RackState {
        uint64_t generation = 0;
        bool hasChildren = false;
        bool hasSidechainChild = false;
        std::vector<RackChain> chains;
        std::vector<MacroMapping> mappings;
    };

    struct PluginFactory {
        std::function<std::shared_ptr<dsp::Instrument>(const std::string& id)> instrumentById;
        std::function<std::shared_ptr<dsp::Device>(const std::string& id)> effectById;
    };
    static PluginFactory& pluginFactory();

    RackCore();
    ~RackCore() override;

    RackState* live() const { return states_.live(); }
    bool hasChains() const noexcept {
        const RackState* st = live();
        return st && !st->chains.empty();
    }
    bool hasChildren() const noexcept {
        const RackState* st = live();
        return st && st->hasChildren;
    }
    bool hasSidechainChild() const noexcept override {
        const RackState* st = live();
        return st && st->hasSidechainChild;
    }
    uint64_t liveGeneration() const {
        const RackState* st = live();
        return st ? st->generation : 0;
    }

    void forwardTransport(const dsp::TransportInfo& ti) {
        RackState* st = live();
        if (!st || !st->hasChildren) return;
        forwardTransportSlow(*st, ti);
    }

    void forwardTempo(double beatStart, double spb, bool playing) {
        RackState* st = live();
        if (!st || !st->hasChildren) return;
        forwardTempoSlow(*st, beatStart, spb, playing);
    }

    bool chainsHostPlugin() const;
    void setRates(double sr, int32_t maxBlock);
    void ensureChainBlockCapacity(int32_t frames);
    void setTelemetryMode(dsp::TelemetryMode mode);

    std::string chainName(int32_t c) const {
        RackState* st = live();
        return (st && c >= 0 && c < static_cast<int32_t>(st->chains.size())) ? st->chains[static_cast<size_t>(c)].name
                                                                             : std::string{};
    }
    bool setChainName(int32_t c, const std::string& name);

    int32_t chainCount() const override {
        RackState* st = live();
        return st ? static_cast<int32_t>(st->chains.size()) : 0;
    }
    int32_t addChain(int32_t instKind);
    int32_t addChainInstrument(std::shared_ptr<dsp::Instrument> inst);
    bool setChainInstrumentSample(int32_t chain, std::shared_ptr<media::SampleBuffer> buf, int32_t root, bool loop);
    bool removeChain(int32_t chain);
    int32_t duplicateChain(int32_t chain);
    bool setChainInstrument(int32_t chain, int32_t instKind);
    bool replaceChainInstrument(int32_t chain, std::shared_ptr<dsp::Instrument> inst, bool prepare = true);
    uint64_t chainUid(int32_t chain) const;

    const RackChain* chainByUid(uint64_t uid) const {
        RackState* st = live();
        if (!st || uid == 0) return nullptr;
        for (const auto& c : st->chains)
            if (c.uid == uid) return &c;
        return nullptr;
    }
    uint64_t chainBaseUid(int32_t chain) const {
        RackState* st = live();
        return (st && chain >= 0 && chain < static_cast<int32_t>(st->chains.size()))
                   ? st->chains[static_cast<size_t>(chain)].uid
                   : 0;
    }
    int32_t chainIndexOfUid(uint64_t uid) const {
        RackState* st = live();
        if (!st || uid == 0) return -1;
        for (size_t i = 0; i < st->chains.size(); ++i)
            if (st->chains[i].uid == uid) return static_cast<int32_t>(i);
        return -1;
    }
    dsp::Device* childDevice(uint64_t uid, int32_t slot) const {
        const RackChain* c = chainByUid(uid);
        return c && slot >= 0 && slot < static_cast<int32_t>(c->devices.size())
                   ? c->devices[static_cast<size_t>(slot)].get()
                   : nullptr;
    }
    dsp::MidiDevice* childMidi(uint64_t uid, int32_t slot) const {
        const RackChain* c = chainByUid(uid);
        if (!c || !isMidiTarget(slot)) return nullptr;
        const int32_t i = midiTargetIndex(slot);
        return i >= 0 && i < static_cast<int32_t>(c->midiDevices.size()) ? c->midiDevices[static_cast<size_t>(i)].get()
                                                                         : nullptr;
    }
    dsp::Instrument* childInstrument(uint64_t uid) const {
        const RackChain* c = chainByUid(uid);
        return c ? c->instrument.get() : nullptr;
    }
    template <class Fn> void forEachChildDevice(Fn&& fn) const {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains)
            for (auto& d : c.devices)
                if (d) fn(*d);
    }
    template <class Fn> void forEachChildMidi(Fn&& fn) const {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains)
            for (auto& m : c.midiDevices)
                if (m) fn(*m);
    }
    template <class Fn> void forEachChildInstrument(Fn&& fn) const {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains)
            if (c.instrument) fn(*c.instrument);
    }
    bool chainsHaveOpenEditor(bool includeInstruments) const override {
        RackState* st = live();
        if (!st) return false;
        for (const auto& c : st->chains) {
            if (includeInstruments && c.instrument && !c.instrument->bypassed()) {
                if (c.instrument->editorOpen()) return true;
                if (const auto* r = c.instrument->asRackCapability(); r && r->chainsHaveOpenEditor(true)) return true;
            }
            for (const auto& d : c.devices) {
                if (!d || d->bypassed()) continue;
                if (d->editorOpen()) return true;
                if (const auto* r = d->asRackCapability(); r && r->chainsHaveOpenEditor(false)) return true;
            }
        }
        return false;
    }
    int32_t chainInstrumentKind(int32_t chain) const;
    dsp::Instrument* chainInstrument(int32_t chain) const;
    int32_t chainDeviceCount(int32_t chain) const;
    int32_t addChainDevice(int32_t chain, int32_t deviceKind);
    int32_t addChainDeviceInstance(int32_t chain, std::shared_ptr<dsp::Device> dev, bool prepare = true);
    bool removeChainDevice(int32_t chain, int32_t dev);
    bool moveChainDevice(int32_t chain, int32_t from, int32_t to);
    int32_t insertChainDeviceInstance(int32_t chain, std::shared_ptr<dsp::Device> dev, int32_t at, bool prepare = true);
    int32_t moveChainDeviceToChain(int32_t chain, int32_t idx, int32_t toChain);
    dsp::Device* chainDeviceAt(int32_t chain, int32_t dev) const;

    int32_t chainMidiDeviceCount(int32_t chain) const;
    int32_t addChainMidiDevice(int32_t chain, int32_t midiKind);
    int32_t addChainMidiDeviceInstance(int32_t chain, std::shared_ptr<dsp::MidiDevice> dev);
    bool removeChainMidiDevice(int32_t chain, int32_t idx);
    bool moveChainMidiDevice(int32_t chain, int32_t from, int32_t to);
    int32_t insertChainMidiDeviceInstance(int32_t chain, std::shared_ptr<dsp::MidiDevice> dev, int32_t at);
    int32_t moveChainMidiDeviceToChain(int32_t chain, int32_t idx, int32_t toChain);
    dsp::MidiDevice* chainMidiDeviceAt(int32_t chain, int32_t idx) const;

    ChainControls* chainControls(int32_t chain) const;
    void setChainTriggerNote(int32_t c, int32_t note) {
        if (auto* x = chainControls(c)) x->triggerNote.store(note, std::memory_order_relaxed);
    }
    int32_t chainTriggerNote(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->triggerNote.load(std::memory_order_relaxed) : -1;
    }
    void setChainGain(int32_t c, float v) {
        if (auto* x = chainControls(c)) x->gain.store(v, std::memory_order_relaxed);
    }
    void setChainPan(int32_t c, float v) {
        if (auto* x = chainControls(c)) x->pan.store(std::clamp(v, -1.0f, 1.0f), std::memory_order_relaxed);
    }
    void setChainMute(int32_t c, bool b) {
        if (auto* x = chainControls(c)) x->mute.store(b, std::memory_order_relaxed);
    }
    void setChainSolo(int32_t c, bool b) {
        if (auto* x = chainControls(c)) x->solo.store(b, std::memory_order_relaxed);
    }
    float chainGain(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->gain.load(std::memory_order_relaxed) : 1.0f;
    }
    float chainPan(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->pan.load(std::memory_order_relaxed) : 0.0f;
    }
    bool chainMute(int32_t c) const {
        auto* x = chainControls(c);
        return x && x->mute.load(std::memory_order_relaxed);
    }
    bool chainSolo(int32_t c) const {
        auto* x = chainControls(c);
        return x && x->solo.load(std::memory_order_relaxed);
    }

    void setChainChoke(int32_t c, int32_t g) {
        if (auto* x = chainControls(c)) x->chokeGroup.store(std::clamp(g, 0, 8), std::memory_order_relaxed);
    }
    int32_t chainChoke(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->chokeGroup.load(std::memory_order_relaxed) : 0;
    }
    void setChainTune(int32_t c, int32_t st) {
        if (auto* x = chainControls(c)) x->tune.store(std::clamp(st, -48, 48), std::memory_order_relaxed);
    }
    int32_t chainTune(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->tune.load(std::memory_order_relaxed) : 0;
    }
    void setChainDecay(int32_t c, float v) {
        if (auto* x = chainControls(c)) x->decay.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    float chainDecay(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->decay.load(std::memory_order_relaxed) : 1.0f;
    }

    void setChainZone(int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi);
    void chainZone(int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const;
    void setChainSelectRange(int32_t c, int32_t lo, int32_t hi);
    void chainSelectRange(int32_t c, int32_t& lo, int32_t& hi) const;
    void setChainTranspose(int32_t c, int32_t st) {
        if (auto* x = chainControls(c)) x->transpose.store(std::clamp(st, -48, 48), std::memory_order_relaxed);
    }
    int32_t chainTranspose(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->transpose.load(std::memory_order_relaxed) : 0;
    }
    void setChainVelScale(int32_t c, float v) {
        if (auto* x = chainControls(c))
            x->velScale.store(std::isfinite(v) ? std::clamp(v, 0.0f, 2.0f) : 1.0f, std::memory_order_relaxed);
    }
    float chainVelScale(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->velScale.load(std::memory_order_relaxed) : 1.0f;
    }
    float chainMeter(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->meter.load(std::memory_order_relaxed) : 0.0f;
    }

    float rackVolume() const { return rackVolume_.load(std::memory_order_relaxed); }
    void setRackVolume(float v) { rackVolume_.store(std::clamp(v, 0.0f, 4.0f), std::memory_order_relaxed); }
    float rackGlide() const { return glide_.load(std::memory_order_relaxed); }
    void setRackGlide(float v) { glide_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    float swing() const { return swing_.load(std::memory_order_relaxed); }
    void setSwing(float v) { swing_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    float humanize() const { return humanize_.load(std::memory_order_relaxed); }
    void setHumanize(float v) { humanize_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    int32_t rackMode() const { return mode_.load(std::memory_order_relaxed); }
    void setRackMode(int32_t m) { mode_.store(std::clamp(m, 0, 2), std::memory_order_relaxed); }
    float rackDryWet() const { return dryWet_.load(std::memory_order_relaxed); }
    void setRackDryWet(float v) { dryWet_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    bool rackPdc() const { return pdc_.load(std::memory_order_relaxed) != 0; }
    void setRackPdc(bool b) { pdc_.store(b ? 1 : 0, std::memory_order_relaxed); }
    float chainSelect() const { return chainSelect_.load(std::memory_order_relaxed); }
    void setChainSelect(float v) { chainSelect_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    bool selFollow() const { return selFollow_.load(std::memory_order_relaxed) != 0; }
    void setSelFollow(bool b) { selFollow_.store(b ? 1 : 0, std::memory_order_relaxed); }
    float liveSelector() const { return liveSel_.load(std::memory_order_relaxed); }

    bool rackMono() const { return mono_.load(std::memory_order_relaxed) != 0; }
    void setRackMono(bool b) { mono_.store(b ? 1 : 0, std::memory_order_relaxed); }
    bool rackMerge() const { return merge_.load(std::memory_order_relaxed) != 0; }
    void setRackMerge(bool b) { merge_.store(b ? 1 : 0, std::memory_order_relaxed); }
    bool rackThru() const { return thru_.load(std::memory_order_relaxed) != 0; }
    void setRackThru(bool b) { thru_.store(b ? 1 : 0, std::memory_order_relaxed); }

    float macroGet(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? macros_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void macroSet(int32_t i, float v);
    std::string macroParamId(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? ("macro" + std::to_string(i + 1)) : std::string{};
    }
    std::string macroParamName(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? ("Macro " + std::to_string(i + 1)) : std::string{};
    }
    int32_t macroParamIndexOfId(const std::string& id) const {
        for (int32_t i = 0; i < kNumMacros; ++i)
            if (macroParamId(i) == id) return i;
        return -1;
    }
    std::string macroName(int32_t i) const {
        if (i < 0 || i >= kNumMacros) return {};
        return macroNames_[i].empty() ? macroParamName(i) : macroNames_[i];
    }
    void setMacroName(int32_t i, const std::string& name) {
        if (i >= 0 && i < kNumMacros) macroNames_[i] = name;
    }

    int32_t addMacroMapping(
        int32_t macro, int32_t chain, int32_t deviceIndex, int32_t paramIndex, float rangeMin, float rangeMax);
    int32_t mappingCount() const {
        RackState* st = live();
        return st ? static_cast<int32_t>(st->mappings.size()) : 0;
    }
    bool mappingInfo(int32_t i, MacroMapping& out) const;
    bool removeMapping(int32_t i);
    bool setMappingRange(int32_t i, float rangeMin, float rangeMax);
    bool setMappingCurve(int32_t i, int32_t curve);
    int32_t mappingCurve(int32_t i) const {
        RackState* st = live();
        return (st && i >= 0 && i < static_cast<int32_t>(st->mappings.size()))
                   ? st->mappings[static_cast<size_t>(i)].curve
                   : 0;
    }

    void reapplyMacros();
    void forkFrom(const RackCore& o);

    std::vector<uint8_t> serialize() const;
    void deserialize(const uint8_t* data, int32_t size);

    static std::shared_ptr<dsp::Instrument> makeInstrument(int32_t kind);
    static bool isRackKind(int32_t kind) { return kind == 3 || kind == 4; }

  protected:
    int32_t maxChainLatency() const;
    static std::shared_ptr<dsp::Device> makeDevice(int32_t kind) {
        return dsp::DeviceRegistry::createChainEffect(kind);
    }

    dsp::SnapshotHistory<RackState>::RenderScope renderScope() {
        return dsp::SnapshotHistory<RackState>::RenderScope(states_);
    }
    bool hasPendingRetire() const noexcept { return states_.hasPendingRetire(); }

    bool chainsHaveTail() const {
        RackState* st = live();
        if (!st || !st->hasChildren) return false;
        for (const auto& c : st->chains) {
            if (c.instrument && !c.instrument->bypassed() && c.instrument->hasTail()) return true;
            for (const auto& d : c.devices)
                if (d && !d->bypassed() && d->hasTail()) return true;
        }
        return false;
    }

    static bool chainIdle(const RackChain& c, bool includeInstrument) {
        if (includeInstrument && c.instrument && !c.instrument->bypassed() &&
            (!c.instrument->isIdle() || c.instrument->requiresActiveProcessing()))
            return false;
        for (const auto& d : c.devices)
            if (d && !d->bypassed() && (!d->isIdle() || d->requiresActiveProcessing())) return false;
        return true;
    }

    bool chainsIdle(bool includeInstruments) const {
        RackState* st = live();
        if (!st || !st->hasChildren) return true;
        for (const auto& c : st->chains)
            if (!chainIdle(c, includeInstruments)) return false;
        return true;
    }

    bool chainsRequireActiveProcessing(bool includeInstruments) const {
        RackState* st = live();
        if (!st || !st->hasChildren) return false;
        for (const auto& c : st->chains) {
            if (includeInstruments && c.instrument && !c.instrument->bypassed() &&
                c.instrument->requiresActiveProcessing())
                return true;
            for (const auto& d : c.devices)
                if (d && !d->bypassed() && d->requiresActiveProcessing()) return true;
        }
        return false;
    }

    static bool chainMidiDevicesIdle(const RackChain& c) {
        for (const auto& d : c.midiDevices)
            if (d && !d->isIdle()) return false;
        return true;
    }

    bool allChainMidiDevicesIdle() const {
        RackState* st = live();
        if (!st || !st->hasChildren) return true;
        for (const auto& c : st->chains)
            if (!chainMidiDevicesIdle(c)) return false;
        return true;
    }

    void wakeChains(bool includeInstruments) {
        RackState* st = live();
        if (!st || !st->hasChildren) return;
        for (auto& c : st->chains) {
            if (includeInstruments && c.instrument) c.instrument->wake();
            for (auto& d : c.devices)
                if (d) d->wake();
        }
    }

    static float applyCurve(float v, int32_t curve);
    void applyMacro(int32_t macro, float value);

    std::atomic<float> macros_[kNumMacros];
    std::string macroNames_[kNumMacros];
    std::atomic<float> rackVolume_{1.0f};
    std::atomic<float> glide_{0.0f};
    std::atomic<float> swing_{0.0f};
    std::atomic<float> humanize_{0.0f};
    std::atomic<int32_t> mode_{0};
    std::atomic<float> dryWet_{1.0f};
    std::atomic<int32_t> pdc_{1};
    std::atomic<float> chainSelect_{0.5f};
    std::atomic<int32_t> selFollow_{0};
    std::atomic<int32_t> mono_{0};
    std::atomic<int32_t> merge_{1};
    std::atomic<int32_t> thru_{1};
    std::atomic<float> liveSel_{0.5f};
    double sampleRate_ = 44100.0;
    int32_t maxBlock_ = kMaxBlock;
    dsp::TelemetryMode telemetryMode_ = dsp::TelemetryMode::OnDemand;

    static bool chainSupportsParallel(const RackChain& c);
    void prepareParallelScratch(const RackState* st, int32_t minFrames);
    dsp::ChainScratchBank parallelArena_;

  private:
    NOTA_NOINLINE void forwardTransportSlow(RackState& st, const dsp::TransportInfo& ti);
    NOTA_NOINLINE void forwardTempoSlow(RackState& st, double beatStart, double spb, bool playing);
    void propagateTelemetry(RackState& st) const;
    static void copyExternalLinks(const RackChain& src, RackChain& dst);
    std::shared_ptr<RackState> copyState() const {
        return authoring_ ? std::make_shared<RackState>(*authoring_) : std::make_shared<RackState>();
    }
    void commit(std::shared_ptr<RackState> ns) {
        if (ns) {
            ns->generation = ++nextStateGen_;
            bool hasChild = false;
            bool hasSidechain = false;
            for (const auto& c : ns->chains) {
                if (c.instrument) {
                    hasChild = true;
                    if (c.instrument->acceptsSidechain() || c.instrument->asRackCapability() != nullptr)
                        hasSidechain = true;
                }
                if (!c.devices.empty()) {
                    hasChild = true;
                    for (const auto& d : c.devices) {
                        if (d && (d->acceptsSidechain() || d->asRackCapability() != nullptr)) hasSidechain = true;
                    }
                }
                if (!c.midiDevices.empty()) hasChild = true;
            }
            ns->hasChildren = hasChild;
            ns->hasSidechainChild = hasSidechain;
            propagateTelemetry(*ns);
            prepareParallelScratch(ns.get(), maxBlock_);
        }
        authoring_ = ns;
        states_.publish(std::move(ns));
    }
    uint64_t nextStateGen_ = 0;
    static void pruneMappings(std::shared_ptr<RackState>& ns, int32_t removedChain);
    static std::shared_ptr<dsp::MidiDevice> makeChainMidiDevice(int32_t kind);

    static void putU8(std::vector<uint8_t>& b, uint8_t v) { b.push_back(v); }
    static void putU32(std::vector<uint8_t>& b, uint32_t v) {
        for (int i = 0; i < 4; ++i) b.push_back(static_cast<uint8_t>((v >> (i * 8)) & 0xFFu));
    }
    static void putI32(std::vector<uint8_t>& b, int32_t v) {
        uint32_t u = 0;
        std::memcpy(&u, &v, 4);
        putU32(b, u);
    }
    static void putF32(std::vector<uint8_t>& b, float v) {
        uint32_t u = 0;
        std::memcpy(&u, &v, 4);
        putU32(b, u);
    }
    static void putU64(std::vector<uint8_t>& b, uint64_t v) {
        for (int i = 0; i < 8; ++i) b.push_back(static_cast<uint8_t>((v >> (i * 8)) & 0xFFu));
    }
    static void putI64(std::vector<uint8_t>& b, int64_t v) {
        uint64_t u = 0;
        std::memcpy(&u, &v, 8);
        putU64(b, u);
    }
    static void putF64(std::vector<uint8_t>& b, double v) {
        uint64_t u = 0;
        std::memcpy(&u, &v, 8);
        putU64(b, u);
    }
    static void putString(std::vector<uint8_t>& b, const std::string& s) {
        putU32(b, static_cast<uint32_t>(s.size()));
        b.insert(b.end(), s.begin(), s.end());
    }
    struct Cursor {
        const uint8_t* d = nullptr;
        int32_t n = 0;
        int32_t p = 0;
        bool ok() const { return p <= n; }
        const uint8_t* take(uint32_t len) {
            if (static_cast<int64_t>(p) + static_cast<int64_t>(len) > n) {
                p = n + 1;
                return nullptr;
            }
            const uint8_t* r = d + p;
            p += static_cast<int32_t>(len);
            return r;
        }
    };
    static uint8_t getU8(Cursor& c) {
        if (c.p + 1 > c.n) {
            c.p = c.n + 1;
            return 0;
        }
        return c.d[c.p++];
    }
    static uint32_t getU32(Cursor& c) {
        if (c.p + 4 > c.n) {
            c.p = c.n + 1;
            return 0;
        }
        uint32_t v = 0;
        for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(c.d[c.p++]) << (i * 8);
        return v;
    }
    static int32_t getI32(Cursor& c) {
        const uint32_t u = getU32(c);
        int32_t v = 0;
        std::memcpy(&v, &u, 4);
        return v;
    }
    static float getF32(Cursor& c) {
        const uint32_t u = getU32(c);
        float v = 0.0f;
        std::memcpy(&v, &u, 4);
        return v;
    }
    static uint64_t getU64(Cursor& c) {
        if (c.p + 8 > c.n) {
            c.p = c.n + 1;
            return 0;
        }
        uint64_t v = 0;
        for (int i = 0; i < 8; ++i) v |= static_cast<uint64_t>(c.d[c.p++]) << (i * 8);
        return v;
    }
    static int64_t getI64(Cursor& c) {
        const uint64_t u = getU64(c);
        int64_t v = 0;
        std::memcpy(&v, &u, 8);
        return v;
    }
    static double getF64(Cursor& c) {
        const uint64_t u = getU64(c);
        double v = 0.0;
        std::memcpy(&v, &u, 8);
        return v;
    }
    static std::string getString(Cursor& c);
    static float clamp01(float v) { return std::isfinite(v) ? std::clamp(v, 0.0f, 1.0f) : 0.0f; }

    static constexpr uint32_t kMagic = 0x314B524E;
    static constexpr uint32_t kBlobVersion = 11;

    std::shared_ptr<RackState> authoring_;
    dsp::SnapshotHistory<RackState> states_;
};

} // namespace nota::devices
