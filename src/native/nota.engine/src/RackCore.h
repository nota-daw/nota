// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackCore — the shared guts of Nota's parallel-chain racks. Holds N parallel
// chains (each an optional built-in instrument + an insert chain of built-in
// devices, plus gain/pan/mute/solo) and 8 macros with parameter mappings, over an
// immutable snapshot that swaps atomically on the message thread (the same
// pattern Engine uses for Graph, so structural edits are audio-thread-safe and
// re-use unchanged children's shared_ptrs — DSP/voice state survives).
//
// RackInstrument (an Instrument, chains have instruments) and RackDevice (a
// Device, chains are effects-only) both derive from this and add only their
// interface-specific bits (note fan-out + render vs. in-place process). The
// serialized blob format is shared: a chain with no instrument writes instKind
// -1. Children are BUILT-IN only in v1 (Synth/Physical + EQ/Comp/Reverb/Delay/
// Utility); hosted plugins inside a rack and key/velocity zones are follow-ups.

#pragma once

#include "Instrument.h"
#include "Device.h"

#include "Synth.h"
#include "PhysicalSynth.h"
#include "WavetableSynth.h"
#include "VoltSynth.h"
#include "BassSynth.h"
#include "PendulumSynth.h"
#include "OperatorSynth.h"
#include "FluxSynth.h"
#include "RhythmMachine.h"
#include "Monolith.h"
#include "Pentad.h"
#include "GrainSynth.h"
#include "Sampler.h"
#include "SampleBuffer.h"
#include "Eq.h"
#include "Compressor.h"
#include "Reverb.h"
#include "Delay.h"
#include "Utility.h"
#include "Amp.h"
#include "AutoFilter.h"
#include "Vintage.h"
#include "AutoPan.h"
#include "AutoShift.h"
#include "BeatRepeat.h"
#include "Crush.h"
#include "DynamicEq.h"
#include "Ceiling.h"
#include "Strata.h"
#include "Eq3.h"
#include "Forge.h"
#include "AutoGain.h"
#include "Shutter.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class RackCore {
public:
    static constexpr int32_t kNumMacros = 8;
    static constexpr int32_t kMaxBlock  = 16384;   // matches Engine::kMaxBlock

    struct ChainControls {
        std::atomic<float>   gain{1.0f};
        std::atomic<float>   pan{0.0f};       // -1..+1
        std::atomic<bool>    mute{false};
        std::atomic<bool>    solo{false};
        std::atomic<int32_t> triggerNote{-1}; // Drum Rack: the pad's MIDI note; -1 = all notes (instrument/effect rack)
        std::atomic<int32_t> keyLo{0}, keyHi{127};   // Instrument Rack: key zone (MIDI note range)
        std::atomic<int32_t> velLo{0}, velHi{127};   // velocity range (MIDI 0..127)
        std::atomic<float>   meter{0.0f};            // post-render peak (UI meter)
        // Drum Rack per-pad shaping (v6). tune is passed to the pad instrument on
        // trigger; decay drives an amp VCA on the pad's summed output; chokeGroup
        // makes pads in the same group monophonic (a new hit cuts the others).
        std::atomic<int32_t> chokeGroup{0};          // 0 = none, 1..8 choke group
        std::atomic<int32_t> tune{0};                // pad transpose, semitones (-48..+48)
        std::atomic<float>   decay{1.0f};            // pad decay 0..1 (1 = sustain / no shaping)
        float                ampEnv = 0.0f;          // audio-thread only: pad decay VCA state
        bool                 retrig = false;         // audio-thread only: noteOn asks for an env kick
    };

    struct RackChain {
        std::shared_ptr<Instrument>          instrument;   // null for effect-rack chains
        std::vector<std::shared_ptr<Device>> devices;      // insert chain
        std::shared_ptr<ChainControls>       ctl = std::make_shared<ChainControls>();
    };

    // A macro (0..1) drives one target param over [rangeMin, rangeMax] in the
    // target's own units (instrument targets use the normalized 0..1 plugin-param
    // surface; device targets use the native min..max generic-param surface).
    struct MacroMapping {
        int32_t macro       = 0;
        int32_t chainIndex  = 0;
        int32_t deviceIndex = -1;   // -1 = the chain's instrument, else a device
        int32_t paramIndex  = 0;
        float   rangeMin    = 0.0f;
        float   rangeMax    = 1.0f;
        int32_t curve       = 0;    // 0 Linear, 1 Exp, 2 Log, 3 S-curve
    };

    struct RackState {
        std::vector<RackChain>    chains;
        std::vector<MacroMapping> mappings;
    };

    // Hosted plugins can't be created in the JUCE-free core, so the engine installs
    // factories that instantiate a plugin child by its stable identifier (used when
    // a saved rack blob is reloaded). Empty id → nullptr (an effect-rack chain with
    // no instrument). Live drops inject instances directly via addChain*Instance.
    struct PluginFactory {
        std::function<std::shared_ptr<Instrument>(const std::string& id)> instrumentById;
        std::function<std::shared_ptr<Device>(const std::string& id)>     effectById;
    };
    static PluginFactory& pluginFactory() { static PluginFactory f; return f; }

    RackCore() {
        for (auto& m : macros_) m.store(0.0f, std::memory_order_relaxed);
        commit(std::make_shared<RackState>());
    }
    virtual ~RackCore() = default;

    // Audio-thread view of the current snapshot (raw pointer, retained by states_).
    RackState* live() const { return live_.load(std::memory_order_acquire); }

    // Fan the transport snapshot out to every chain child so hosted plugins nested
    // in a rack sync to the DAW clock too (the Instrument/Device overrides call this).
    void forwardTransport(const TransportInfo& ti) {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains) {
            if (c.instrument) c.instrument->setTransportInfo(ti);
            for (auto& d : c.devices) if (d) d->setTransportInfo(ti);
        }
    }

    // Sample rate + max block; forwarded to every child. Message thread.
    void setRates(double sr, int32_t maxBlock) {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        maxBlock_ = maxBlock > 0 ? maxBlock : kMaxBlock;
        auto st = authoring_;
        if (!st) return;
        for (auto& c : st->chains) {
            if (c.instrument) c.instrument->setSampleRate(sampleRate_);
            for (auto& d : c.devices) if (d) d->setSampleRate(sampleRate_, maxBlock_);
        }
    }

    // ---- structural editing (message thread) ------------------------------
    int32_t chainCount() const {
        RackState* st = live(); return st ? static_cast<int32_t>(st->chains.size()) : 0;
    }
    // Adds a chain; instKind 0=Synth, 2=Physical, <0/other = no instrument
    // (effect-rack chains). Returns the new chain index.
    int32_t addChain(int32_t instKind) {
        auto inst = makeInstrument(instKind);
        if (inst) inst->setSampleRate(sampleRate_);
        auto ns = copyState();
        RackChain c; c.instrument = inst;
        ns->chains.push_back(std::move(c));
        const int32_t idx = static_cast<int32_t>(ns->chains.size()) - 1;
        commit(ns);
        return idx;
    }
    // Adds a chain hosting a pre-built instrument (e.g. a Sampler the engine loaded
    // from a file — RackCore can't decode audio). Returns the new chain index.
    int32_t addChainInstrument(std::shared_ptr<Instrument> inst) {
        if (inst) inst->setSampleRate(sampleRate_);
        auto ns = copyState();
        RackChain c; c.instrument = std::move(inst);
        ns->chains.push_back(std::move(c));
        const int32_t idx = static_cast<int32_t>(ns->chains.size()) - 1;
        commit(ns);
        return idx;
    }
    // Load a sample into a chain's Sampler (or turn the chain into one), keeping its params
    // + devices. Publishes a fresh Sampler via a state swap (thread-safe, no in-place mutate).
    bool setChainInstrumentSample(int32_t chain, std::shared_ptr<SampleBuffer> buf, int32_t root, bool loop) {
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
        auto* oldSm = dynamic_cast<Sampler*>(ns->chains[chain].instrument.get());
        std::shared_ptr<Instrument> fresh = oldSm ? oldSm->clone() : std::make_shared<Sampler>();
        auto* sm = dynamic_cast<Sampler*>(fresh.get());
        if (!sm) return false;
        sm->setSampleRate(sampleRate_);
        sm->setSample(std::move(buf), root, loop);
        ns->chains[chain].instrument = std::move(fresh);
        commit(ns);
        return true;
    }
    bool removeChain(int32_t chain) {
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
        ns->chains.erase(ns->chains.begin() + chain);
        pruneMappings(ns, chain);
        commit(ns);
        return true;
    }
    bool setChainInstrument(int32_t chain, int32_t instKind) {
        auto inst = makeInstrument(instKind);
        if (!inst) return false;
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
        inst->setSampleRate(sampleRate_);
        ns->chains[chain].instrument = inst;
        commit(ns);
        return true;
    }
    int32_t chainInstrumentKind(int32_t chain) const {
        RackState* st = live();
        if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return -2;
        return st->chains[chain].instrument ? st->chains[chain].instrument->kind() : -2;
    }
    Instrument* chainInstrument(int32_t chain) const {
        RackState* st = live();
        if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
        return st->chains[chain].instrument.get();
    }
    int32_t chainDeviceCount(int32_t chain) const {
        RackState* st = live();
        if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return 0;
        return static_cast<int32_t>(st->chains[chain].devices.size());
    }
    int32_t addChainDevice(int32_t chain, int32_t deviceKind) {
        auto dev = makeDevice(deviceKind);
        if (!dev) return -1;
        return addChainDeviceInstance(chain, dev);
    }
    // Adds a pre-built device (e.g. a hosted plugin the engine instantiated) to a chain.
    int32_t addChainDeviceInstance(int32_t chain, std::shared_ptr<Device> dev) {
        if (!dev) return -1;
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
        dev->setSampleRate(sampleRate_, maxBlock_);
        ns->chains[chain].devices.push_back(std::move(dev));
        const int32_t idx = static_cast<int32_t>(ns->chains[chain].devices.size()) - 1;
        commit(ns);
        return idx;
    }
    bool removeChainDevice(int32_t chain, int32_t dev) {
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
        auto& d = ns->chains[chain].devices;
        if (dev < 0 || dev >= static_cast<int32_t>(d.size())) return false;
        d.erase(d.begin() + dev);
        commit(ns);
        return true;
    }
    bool moveChainDevice(int32_t chain, int32_t from, int32_t to) {
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
        auto& d = ns->chains[chain].devices;
        const int32_t n = static_cast<int32_t>(d.size());
        if (from < 0 || from >= n) return false;
        if (to < 0) to = 0; if (to >= n) to = n - 1;
        if (to == from) return true;
        auto x = d[from];
        d.erase(d.begin() + from);
        d.insert(d.begin() + to, x);
        commit(ns);
        return true;
    }
    Device* chainDeviceAt(int32_t chain, int32_t dev) const {
        RackState* st = live();
        if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
        auto& d = st->chains[chain].devices;
        if (dev < 0 || dev >= static_cast<int32_t>(d.size())) return nullptr;
        return d[dev].get();
    }

    ChainControls* chainControls(int32_t chain) const {
        RackState* st = live();
        if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
        return st->chains[chain].ctl.get();
    }
    void    setChainTriggerNote(int32_t c, int32_t note) { if (auto* x = chainControls(c)) x->triggerNote.store(note, std::memory_order_relaxed); }
    int32_t chainTriggerNote(int32_t c) const { auto* x = chainControls(c); return x ? x->triggerNote.load(std::memory_order_relaxed) : -1; }
    void  setChainGain(int32_t c, float v) { if (auto* x = chainControls(c)) x->gain.store(v, std::memory_order_relaxed); }
    void  setChainPan (int32_t c, float v) { if (auto* x = chainControls(c)) x->pan.store(std::clamp(v, -1.0f, 1.0f), std::memory_order_relaxed); }
    void  setChainMute(int32_t c, bool  b) { if (auto* x = chainControls(c)) x->mute.store(b, std::memory_order_relaxed); }
    void  setChainSolo(int32_t c, bool  b) { if (auto* x = chainControls(c)) x->solo.store(b, std::memory_order_relaxed); }
    float chainGain(int32_t c) const { auto* x = chainControls(c); return x ? x->gain.load(std::memory_order_relaxed) : 1.0f; }
    float chainPan (int32_t c) const { auto* x = chainControls(c); return x ? x->pan.load(std::memory_order_relaxed)  : 0.0f; }
    bool  chainMute(int32_t c) const { auto* x = chainControls(c); return x && x->mute.load(std::memory_order_relaxed); }
    bool  chainSolo(int32_t c) const { auto* x = chainControls(c); return x && x->solo.load(std::memory_order_relaxed); }

    // Drum Rack per-pad shaping (v6).
    void    setChainChoke(int32_t c, int32_t g) { if (auto* x = chainControls(c)) x->chokeGroup.store(std::clamp(g, 0, 8), std::memory_order_relaxed); }
    int32_t chainChoke(int32_t c) const { auto* x = chainControls(c); return x ? x->chokeGroup.load(std::memory_order_relaxed) : 0; }
    void    setChainTune(int32_t c, int32_t st) { if (auto* x = chainControls(c)) x->tune.store(std::clamp(st, -48, 48), std::memory_order_relaxed); }
    int32_t chainTune(int32_t c) const { auto* x = chainControls(c); return x ? x->tune.load(std::memory_order_relaxed) : 0; }
    void    setChainDecay(int32_t c, float v) { if (auto* x = chainControls(c)) x->decay.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    float   chainDecay(int32_t c) const { auto* x = chainControls(c); return x ? x->decay.load(std::memory_order_relaxed) : 1.0f; }

    // Key/velocity zone (Instrument Rack): only notes inside the range reach this chain.
    void setChainZone(int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
        if (auto* x = chainControls(c)) {
            x->keyLo.store(std::clamp(keyLo, 0, 127), std::memory_order_relaxed);
            x->keyHi.store(std::clamp(keyHi, 0, 127), std::memory_order_relaxed);
            x->velLo.store(std::clamp(velLo, 0, 127), std::memory_order_relaxed);
            x->velHi.store(std::clamp(velHi, 0, 127), std::memory_order_relaxed);
        }
    }
    void chainZone(int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
        auto* x = chainControls(c);
        keyLo = x ? x->keyLo.load(std::memory_order_relaxed) : 0;
        keyHi = x ? x->keyHi.load(std::memory_order_relaxed) : 127;
        velLo = x ? x->velLo.load(std::memory_order_relaxed) : 0;
        velHi = x ? x->velHi.load(std::memory_order_relaxed) : 127;
    }
    float chainMeter(int32_t c) const { auto* x = chainControls(c); return x ? x->meter.load(std::memory_order_relaxed) : 0.0f; }

    // Rack output: gain + glide (message thread), volume applied at render.
    float rackVolume() const { return rackVolume_.load(std::memory_order_relaxed); }
    void  setRackVolume(float v) { rackVolume_.store(std::clamp(v, 0.0f, 4.0f), std::memory_order_relaxed); }
    float rackGlide() const { return glide_.load(std::memory_order_relaxed); }
    void  setRackGlide(float v) { glide_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Drum Rack kit-level timing (v6): swing pushes off-beat 1/16 hits late; humanize
    // adds a small random offset. Applied by the engine's event scheduler (Engine_Render).
    float swing() const { return swing_.load(std::memory_order_relaxed); }
    void  setSwing(float v) { swing_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    float humanize() const { return humanize_.load(std::memory_order_relaxed); }
    void  setHumanize(float v) { humanize_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Audio Effect Rack routing + output stage (v7). mode: 0 Parallel, 1 Series,
    // 2 Select (chains whose select-zone [velLo..velHi] covers the selector are active).
    // dryWet mixes the rack's wet output against the dry input; pdc reports chain
    // latency to the host; chainSelect is the manual selector 0..1 and selFollow drives
    // it from the input level instead. liveSelector is the runtime position (UI marker).
    int32_t rackMode() const { return mode_.load(std::memory_order_relaxed); }
    void    setRackMode(int32_t m) { mode_.store(std::clamp(m, 0, 2), std::memory_order_relaxed); }
    float   rackDryWet() const { return dryWet_.load(std::memory_order_relaxed); }
    void    setRackDryWet(float v) { dryWet_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    bool    rackPdc() const { return pdc_.load(std::memory_order_relaxed) != 0; }
    void    setRackPdc(bool b) { pdc_.store(b ? 1 : 0, std::memory_order_relaxed); }
    float   chainSelect() const { return chainSelect_.load(std::memory_order_relaxed); }
    void    setChainSelect(float v) { chainSelect_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    bool    selFollow() const { return selFollow_.load(std::memory_order_relaxed) != 0; }
    void    setSelFollow(bool b) { selFollow_.store(b ? 1 : 0, std::memory_order_relaxed); }
    float   liveSelector() const { return liveSel_.load(std::memory_order_relaxed); }

    // ---- macros -----------------------------------------------------------
    float macroGet(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? macros_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void macroSet(int32_t i, float v) {
        if (i < 0 || i >= kNumMacros) return;
        v = std::clamp(v, 0.0f, 1.0f);
        macros_[i].store(v, std::memory_order_relaxed);
        applyMacro(i, v);
    }
    // Macro plugin-param surface, shared by the Instrument/Device overrides.
    std::string macroParamId(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? ("macro" + std::to_string(i + 1)) : std::string{};
    }
    std::string macroParamName(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? ("Macro " + std::to_string(i + 1)) : std::string{};
    }
    int32_t macroParamIndexOfId(const std::string& id) const {
        for (int32_t i = 0; i < kNumMacros; ++i) if (macroParamId(i) == id) return i;
        return -1;
    }
    // Custom macro name (falls back to "Macro N" when unset). Message thread.
    std::string macroName(int32_t i) const {
        if (i < 0 || i >= kNumMacros) return {};
        return macroNames_[i].empty() ? macroParamName(i) : macroNames_[i];
    }
    void setMacroName(int32_t i, const std::string& name) { if (i >= 0 && i < kNumMacros) macroNames_[i] = name; }

    // ---- mappings ---------------------------------------------------------
    int32_t addMacroMapping(int32_t macro, int32_t chain, int32_t deviceIndex,
                            int32_t paramIndex, float rangeMin, float rangeMax) {
        if (macro < 0 || macro >= kNumMacros) return -1;
        auto ns = copyState();
        if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
        ns->mappings.push_back({macro, chain, deviceIndex, paramIndex, rangeMin, rangeMax});
        const int32_t idx = static_cast<int32_t>(ns->mappings.size()) - 1;
        commit(ns);
        applyMacro(macro, macros_[macro].load(std::memory_order_relaxed));
        return idx;
    }
    int32_t mappingCount() const { RackState* st = live(); return st ? static_cast<int32_t>(st->mappings.size()) : 0; }
    bool mappingInfo(int32_t i, MacroMapping& out) const {
        RackState* st = live();
        if (!st || i < 0 || i >= static_cast<int32_t>(st->mappings.size())) return false;
        out = st->mappings[i];
        return true;
    }
    bool removeMapping(int32_t i) {
        auto ns = copyState();
        if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
        ns->mappings.erase(ns->mappings.begin() + i);
        commit(ns);
        return true;
    }
    // Update an existing mapping's range / response curve (Macro-map editing).
    bool setMappingRange(int32_t i, float rangeMin, float rangeMax) {
        auto ns = copyState();
        if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
        ns->mappings[i].rangeMin = rangeMin; ns->mappings[i].rangeMax = rangeMax;
        const int32_t mo = ns->mappings[i].macro;
        commit(ns);
        applyMacro(mo, macros_[mo].load(std::memory_order_relaxed));
        return true;
    }
    bool setMappingCurve(int32_t i, int32_t curve) {
        auto ns = copyState();
        if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
        ns->mappings[i].curve = std::clamp(curve, 0, 3);
        const int32_t mo = ns->mappings[i].macro;
        commit(ns);
        applyMacro(mo, macros_[mo].load(std::memory_order_relaxed));
        return true;
    }
    int32_t mappingCurve(int32_t i) const {
        RackState* st = live();
        return (st && i >= 0 && i < static_cast<int32_t>(st->mappings.size())) ? st->mappings[i].curve : 0;
    }

    // ---- persistence blob (shared format; instKind -1 = no instrument) ----
    std::vector<uint8_t> serialize() const {
        RackState* st = live();
        std::vector<uint8_t> b;
        putU32(b, kMagic);
        putU32(b, kBlobVersion);
        for (int i = 0; i < kNumMacros; ++i) putF32(b, macros_[i].load(std::memory_order_relaxed));
        // v5: rack output + custom macro names.
        putF32(b, rackVolume_.load(std::memory_order_relaxed));
        putF32(b, glide_.load(std::memory_order_relaxed));
        for (int i = 0; i < kNumMacros; ++i) putString(b, macroNames_[i]);
        // v6: drum-rack kit-level timing.
        putF32(b, swing_.load(std::memory_order_relaxed));
        putF32(b, humanize_.load(std::memory_order_relaxed));
        // v7: audio-effect-rack routing + output stage.
        putI32(b, mode_.load(std::memory_order_relaxed));
        putF32(b, dryWet_.load(std::memory_order_relaxed));
        putU8 (b, static_cast<uint8_t>(pdc_.load(std::memory_order_relaxed)));
        putF32(b, chainSelect_.load(std::memory_order_relaxed));
        putU8 (b, static_cast<uint8_t>(selFollow_.load(std::memory_order_relaxed)));
        if (!st) { putU32(b, 0); putU32(b, 0); return b; }
        putU32(b, static_cast<uint32_t>(st->mappings.size()));
        for (auto& m : st->mappings) {
            putI32(b, m.macro); putI32(b, m.chainIndex); putI32(b, m.deviceIndex);
            putI32(b, m.paramIndex); putF32(b, m.rangeMin); putF32(b, m.rangeMax);
            putI32(b, m.curve);   // v5
        }
        putU32(b, static_cast<uint32_t>(st->chains.size()));
        for (auto& c : st->chains) {
            putF32(b, c.ctl->gain.load(std::memory_order_relaxed));
            putF32(b, c.ctl->pan.load(std::memory_order_relaxed));
            putU8 (b, c.ctl->mute.load(std::memory_order_relaxed) ? 1 : 0);
            putU8 (b, c.ctl->solo.load(std::memory_order_relaxed) ? 1 : 0);
            putI32(b, c.ctl->triggerNote.load(std::memory_order_relaxed));   // blob v2
            // v5: key/velocity zone.
            putI32(b, c.ctl->keyLo.load(std::memory_order_relaxed));
            putI32(b, c.ctl->keyHi.load(std::memory_order_relaxed));
            putI32(b, c.ctl->velLo.load(std::memory_order_relaxed));
            putI32(b, c.ctl->velHi.load(std::memory_order_relaxed));
            // v6: drum-rack pad shaping.
            putI32(b, c.ctl->chokeGroup.load(std::memory_order_relaxed));
            putI32(b, c.ctl->tune.load(std::memory_order_relaxed));
            putF32(b, c.ctl->decay.load(std::memory_order_relaxed));
            const int32_t ik = c.instrument ? c.instrument->kind() : -1;
            putI32(b, ik);
            // v4: kind -1 = hosted plugin instrument OR no instrument; the id (empty
            // for none) lets the factory rebuild it. Built-ins (>=0) need no id.
            if (ik == -1) putString(b, c.instrument ? c.instrument->pluginIdentifier() : std::string{});
            std::vector<uint8_t> is = c.instrument ? c.instrument->getState() : std::vector<uint8_t>{};
            putU32(b, static_cast<uint32_t>(is.size()));
            b.insert(b.end(), is.begin(), is.end());
            if (ik == 1) {   // Sampler payload (blob v3): self-contained sample
                auto* sm = dynamic_cast<Sampler*>(c.instrument.get());
                auto buf = sm ? sm->sample() : nullptr;
                putI32(b, sm ? sm->rootNote() : 60);
                putU8 (b, (sm && sm->loopEnabled()) ? 1 : 0);
                putI32(b, buf ? buf->channels : 0);
                putI64(b, buf ? buf->frames : 0);
                putF64(b, buf ? buf->sourceSampleRate : 44100.0);
                const uint32_t n = buf ? static_cast<uint32_t>(buf->samples.size()) : 0;
                putU32(b, n);
                for (uint32_t s = 0; s < n; ++s) putF32(b, buf->samples[s]);
            }
            putU32(b, static_cast<uint32_t>(c.devices.size()));
            for (auto& d : c.devices) {
                const int32_t bk = d ? d->builtinKind() : -1;
                putI32(b, bk);
                putU8 (b, (d && d->bypassed()) ? 1 : 0);
                if (bk == -1) {   // v4: hosted plugin effect — id + opaque state
                    putString(b, d ? d->pluginIdentifier() : std::string{});
                    std::vector<uint8_t> ds = d ? d->getState() : std::vector<uint8_t>{};
                    putU32(b, static_cast<uint32_t>(ds.size()));
                    b.insert(b.end(), ds.begin(), ds.end());
                }
                const int32_t pc = d ? d->paramCount() : 0;
                putU32(b, static_cast<uint32_t>(pc));
                for (int32_t p = 0; p < pc; ++p) putF32(b, d->getParam(p));
            }
        }
        return b;
    }
    void deserialize(const uint8_t* data, int32_t size) {
        if (!data || size < 8) return;
        Cursor cur{data, size, 0};
        if (getU32(cur) != kMagic) return;
        const uint32_t ver = getU32(cur);   // v1 = no triggerNote; v2 adds it per chain
        for (int i = 0; i < kNumMacros; ++i) macros_[i].store(clamp01(getF32(cur)), std::memory_order_relaxed);
        if (ver >= 5) {
            rackVolume_.store(getF32(cur), std::memory_order_relaxed);
            glide_.store(getF32(cur), std::memory_order_relaxed);
            for (int i = 0; i < kNumMacros; ++i) macroNames_[i] = getString(cur);
        }
        if (ver >= 6) {
            swing_.store(getF32(cur), std::memory_order_relaxed);
            humanize_.store(getF32(cur), std::memory_order_relaxed);
        }
        if (ver >= 7) {
            mode_.store(getI32(cur), std::memory_order_relaxed);
            dryWet_.store(getF32(cur), std::memory_order_relaxed);
            pdc_.store(getU8(cur), std::memory_order_relaxed);
            chainSelect_.store(getF32(cur), std::memory_order_relaxed);
            selFollow_.store(getU8(cur), std::memory_order_relaxed);
        }
        auto ns = std::make_shared<RackState>();
        const uint32_t mapN = getU32(cur);
        for (uint32_t i = 0; i < mapN && cur.ok(); ++i) {
            MacroMapping m;
            m.macro = getI32(cur); m.chainIndex = getI32(cur); m.deviceIndex = getI32(cur);
            m.paramIndex = getI32(cur); m.rangeMin = getF32(cur); m.rangeMax = getF32(cur);
            if (ver >= 5) m.curve = getI32(cur);
            ns->mappings.push_back(m);
        }
        const uint32_t chainN = getU32(cur);
        for (uint32_t i = 0; i < chainN && cur.ok(); ++i) {
            RackChain c;
            c.ctl->gain.store(getF32(cur), std::memory_order_relaxed);
            c.ctl->pan.store(getF32(cur), std::memory_order_relaxed);
            c.ctl->mute.store(getU8(cur) != 0, std::memory_order_relaxed);
            c.ctl->solo.store(getU8(cur) != 0, std::memory_order_relaxed);
            c.ctl->triggerNote.store(ver >= 2 ? getI32(cur) : -1, std::memory_order_relaxed);
            if (ver >= 5) {
                c.ctl->keyLo.store(getI32(cur), std::memory_order_relaxed);
                c.ctl->keyHi.store(getI32(cur), std::memory_order_relaxed);
                c.ctl->velLo.store(getI32(cur), std::memory_order_relaxed);
                c.ctl->velHi.store(getI32(cur), std::memory_order_relaxed);
            }
            if (ver >= 6) {
                c.ctl->chokeGroup.store(getI32(cur), std::memory_order_relaxed);
                c.ctl->tune.store(getI32(cur), std::memory_order_relaxed);
                c.ctl->decay.store(getF32(cur), std::memory_order_relaxed);
            }
            const int32_t ik = getI32(cur);
            std::string instPluginId;
            if (ik == -1 && ver >= 4) instPluginId = getString(cur);   // v4 plugin/none id
            const uint32_t isLen = getU32(cur);
            const uint8_t* isPtr = cur.take(isLen);
            if (ik == -1)   // hosted plugin (via factory) or no instrument (empty id)
                c.instrument = instPluginId.empty() ? nullptr : pluginFactory().instrumentById(instPluginId);
            else
                c.instrument = makeInstrument(ik);
            if (c.instrument) {
                c.instrument->setSampleRate(sampleRate_);
                if (isPtr && isLen > 0) c.instrument->setState(isPtr, static_cast<int32_t>(isLen));
            }
            if (ik == 1 && ver >= 3) {   // Sampler payload (blob v3)
                const int32_t root = getI32(cur);
                const bool loop = getU8(cur) != 0;
                const int32_t ch = getI32(cur);
                const int64_t fr = getI64(cur);
                const double sr = getF64(cur);
                const uint32_t n = getU32(cur);
                auto buf = std::make_shared<SampleBuffer>();
                buf->channels = ch; buf->frames = fr; buf->sourceSampleRate = sr;
                buf->samples.resize(n);
                for (uint32_t s = 0; s < n && cur.ok(); ++s) buf->samples[s] = getF32(cur);
                if (auto* sm = dynamic_cast<Sampler*>(c.instrument.get())) sm->setSample(buf, root, loop);
            }
            const uint32_t devN = getU32(cur);
            for (uint32_t j = 0; j < devN && cur.ok(); ++j) {
                const int32_t dk = getI32(cur);
                const bool byp = getU8(cur) != 0;
                std::string devPluginId; const uint8_t* dsPtr = nullptr; uint32_t dsLen = 0;
                if (dk == -1 && ver >= 4) {   // v4 hosted plugin effect
                    devPluginId = getString(cur);
                    dsLen = getU32(cur);
                    dsPtr = cur.take(dsLen);
                }
                const uint32_t pc = getU32(cur);
                auto dev = (dk == -1) ? (devPluginId.empty() ? nullptr : pluginFactory().effectById(devPluginId))
                                      : makeDevice(dk);
                if (dev) dev->setSampleRate(sampleRate_, maxBlock_);
                for (uint32_t p = 0; p < pc && cur.ok(); ++p) {
                    const float pv = getF32(cur);
                    if (dev) dev->setParam(static_cast<int32_t>(p), pv);
                }
                if (dev) {
                    if (dsPtr && dsLen > 0) dev->setState(dsPtr, static_cast<int32_t>(dsLen));
                    dev->setBypassed(byp);
                    c.devices.push_back(dev);
                }
            }
            ns->chains.push_back(std::move(c));
        }
        commit(ns);
        for (int i = 0; i < kNumMacros; ++i) applyMacro(i, macros_[i].load(std::memory_order_relaxed));
    }

protected:
    // Max chain latency (per-chain internal alignment is a follow-up).
    int32_t maxChainLatency() const {
        RackState* st = live();
        if (!st) return 0;
        int32_t maxLat = 0;
        for (auto& c : st->chains) {
            int32_t lat = c.instrument ? c.instrument->latencySamples() : 0;
            for (auto& d : c.devices) if (d) lat += d->latencySamples();
            if (lat > maxLat) maxLat = lat;
        }
        return maxLat;
    }

public:
    // Built-in instrument factory (public so the Engine can read a fresh one's param
    // defaults for a chain instrument, e.g. double-click reset on Sampler knobs).
    static std::shared_ptr<Instrument> makeInstrument(int32_t kind) {
        switch (kind) {
            case 0: return std::make_shared<Synth>();
            case 1: return std::make_shared<Sampler>();   // bare; the sample rides the blob (deserialize) or engine (addChainInstrument)
            case 2: return std::make_shared<PhysicalSynth>();
            case 5: return std::make_shared<WavetableSynth>();
            case 6: return std::make_shared<VoltSynth>();
            case 7: return std::make_shared<BassSynth>();
            case 8: return std::make_shared<PendulumSynth>();
            case 9: return std::make_shared<OperatorSynth>();
            case 10: return std::make_shared<GrainSynth>();
            case 11: return std::make_shared<FluxSynth>();
            case 12: return std::make_shared<RhythmMachine>();
            case 13: return std::make_shared<Monolith>();
            case 14: return std::make_shared<Pentad>();
            default: return nullptr;   // -1 = no instrument (effect rack); plugins are a follow-up
        }
    }
protected:
    static std::shared_ptr<Device> makeDevice(int32_t kind) {
        switch (kind) {
            case 0:  return std::make_shared<Eq>();
            case 1:  return std::make_shared<Compressor>();
            case 2:  return std::make_shared<Reverb>();
            case 3:  return std::make_shared<Delay>();
            case 4:  return std::make_shared<Utility>();
            case 6:  return std::make_shared<Amp>();
            case 7:  return std::make_shared<AutoFilter>();
            case 8:  return std::make_shared<Vintage>();
            case 9:  return std::make_shared<AutoPan>();
            case 10: return std::make_shared<AutoShift>();
            case 11: return std::make_shared<BeatRepeat>();
            case 12: return std::make_shared<Crush>();
            case 13: return std::make_shared<DynamicEq>();
            case 14: return std::make_shared<Ceiling>();
            case 15: return std::make_shared<Strata>();
            case 16: return std::make_shared<Eq3>();
            case 17: return std::make_shared<Forge>();
            case 18: return std::make_shared<AutoGain>();
            case 19: return std::make_shared<Shutter>();
            default: return nullptr;   // 5 = Effect Rack (nesting) / plugin: not via this factory
        }
    }

    // Macro response curve applied to the 0..1 value before mapping to [min,max].
    static float applyCurve(float v, int32_t curve) {
        v = std::clamp(v, 0.0f, 1.0f);
        switch (curve) {
            case 1:  return v * v;                              // Exp (slow start)
            case 2:  return std::sqrt(v);                       // Log (fast start)
            case 3:  return v * v * (3.0f - 2.0f * v);          // S-curve (smoothstep)
            default: return v;                                  // Linear
        }
    }

    void applyMacro(int32_t macro, float value) {
        RackState* st = live();
        if (!st) return;
        for (auto& m : st->mappings) {
            if (m.macro != macro) continue;
            const float target = m.rangeMin + applyCurve(value, m.curve) * (m.rangeMax - m.rangeMin);
            if (m.chainIndex < 0 || m.chainIndex >= static_cast<int32_t>(st->chains.size())) continue;
            auto& c = st->chains[m.chainIndex];
            if (m.deviceIndex < 0) {
                if (c.instrument) c.instrument->pluginParamSet(m.paramIndex, target);
            } else if (m.deviceIndex < static_cast<int32_t>(c.devices.size())) {
                if (c.devices[m.deviceIndex]) c.devices[m.deviceIndex]->setParam(m.paramIndex, target);
            }
        }
    }

    std::atomic<float> macros_[kNumMacros];
    std::string        macroNames_[kNumMacros];       // custom macro names (message thread; "" = default M#)
    std::atomic<float> rackVolume_{1.0f};             // Instrument Rack output gain (linear)
    std::atomic<float> glide_{0.0f};                  // rack glide 0..1 (→ ms; stored/reserved)
    std::atomic<float> swing_{0.0f};                  // Drum Rack swing 0..1 (v6)
    std::atomic<float> humanize_{0.0f};               // Drum Rack humanize 0..1 (v6)
    std::atomic<int32_t> mode_{0};                    // Audio Effect Rack: 0 Parallel, 1 Series, 2 Select (v7)
    std::atomic<float> dryWet_{1.0f};                 // effect-rack rack-out dry/wet (1 = full wet)
    std::atomic<int32_t> pdc_{1};                     // PDC compensation on(1)/off(0)
    std::atomic<float> chainSelect_{0.5f};            // Select-mode manual selector 0..1
    std::atomic<int32_t> selFollow_{0};               // Select follows input level
    std::atomic<float> liveSel_{0.5f};                // runtime selector position (audio writes, UI reads)
    double  sampleRate_ = 44100.0;
    int32_t maxBlock_   = kMaxBlock;

private:
    std::shared_ptr<RackState> copyState() const {
        return authoring_ ? std::make_shared<RackState>(*authoring_) : std::make_shared<RackState>();
    }
    void commit(std::shared_ptr<RackState> ns) {
        authoring_ = ns;
        states_.push_back(ns);
        if (states_.size() > 16) states_.erase(states_.begin());   // keep short history alive for the audio thread
        live_.store(ns.get(), std::memory_order_release);
    }
    static void pruneMappings(std::shared_ptr<RackState>& ns, int32_t removedChain) {
        auto& v = ns->mappings;
        for (auto it = v.begin(); it != v.end();) {
            if (it->chainIndex == removedChain) it = v.erase(it);
            else { if (it->chainIndex > removedChain) --it->chainIndex; ++it; }
        }
    }

    static void putU8 (std::vector<uint8_t>& b, uint8_t v) { b.push_back(v); }
    static void putU32(std::vector<uint8_t>& b, uint32_t v) { for (int i = 0; i < 4; ++i) b.push_back(uint8_t(v >> (i * 8))); }
    static void putI32(std::vector<uint8_t>& b, int32_t v) { uint32_t u; std::memcpy(&u, &v, 4); putU32(b, u); }
    static void putF32(std::vector<uint8_t>& b, float v)   { uint32_t u; std::memcpy(&u, &v, 4); putU32(b, u); }
    static void putU64(std::vector<uint8_t>& b, uint64_t v) { for (int i = 0; i < 8; ++i) b.push_back(uint8_t(v >> (i * 8))); }
    static void putI64(std::vector<uint8_t>& b, int64_t v) { uint64_t u; std::memcpy(&u, &v, 8); putU64(b, u); }
    static void putF64(std::vector<uint8_t>& b, double v)  { uint64_t u; std::memcpy(&u, &v, 8); putU64(b, u); }
    static void putString(std::vector<uint8_t>& b, const std::string& s) {
        putU32(b, static_cast<uint32_t>(s.size()));
        b.insert(b.end(), s.begin(), s.end());
    }
    struct Cursor {
        const uint8_t* d; int32_t n; int32_t p;
        bool ok() const { return p <= n; }
        const uint8_t* take(uint32_t len) {
            if (p + static_cast<int64_t>(len) > n) { p = n + 1; return nullptr; }
            const uint8_t* r = d + p; p += static_cast<int32_t>(len); return r;
        }
    };
    static uint8_t  getU8 (Cursor& c) { if (c.p + 1 > c.n) { c.p = c.n + 1; return 0; } return c.d[c.p++]; }
    static uint32_t getU32(Cursor& c) {
        if (c.p + 4 > c.n) { c.p = c.n + 1; return 0; }
        uint32_t v = 0; for (int i = 0; i < 4; ++i) v |= uint32_t(c.d[c.p++]) << (i * 8); return v;
    }
    static int32_t getI32(Cursor& c) { uint32_t u = getU32(c); int32_t v; std::memcpy(&v, &u, 4); return v; }
    static float   getF32(Cursor& c) { uint32_t u = getU32(c); float v; std::memcpy(&v, &u, 4); return v; }
    static uint64_t getU64(Cursor& c) {
        if (c.p + 8 > c.n) { c.p = c.n + 1; return 0; }
        uint64_t v = 0; for (int i = 0; i < 8; ++i) v |= uint64_t(c.d[c.p++]) << (i * 8); return v;
    }
    static int64_t getI64(Cursor& c) { uint64_t u = getU64(c); int64_t v; std::memcpy(&v, &u, 8); return v; }
    static double  getF64(Cursor& c) { uint64_t u = getU64(c); double v; std::memcpy(&v, &u, 8); return v; }
    static std::string getString(Cursor& c) {
        const uint32_t n = getU32(c);
        const uint8_t* p = c.take(n);
        return (p && n > 0) ? std::string(reinterpret_cast<const char*>(p), n) : std::string{};
    }
    static float   clamp01(float v) { return std::isfinite(v) ? std::clamp(v, 0.0f, 1.0f) : 0.0f; }

    static constexpr uint32_t kMagic       = 0x314B524E;  // 'NRK1'
    static constexpr uint32_t kBlobVersion = 7;           // v2 triggerNote; v3 Sampler; v4 plugins; v5 zones/names/rack-out/curve; v6 drum pad shaping + swing/humanize; v7 effect-rack mode/dry-wet/pdc/select

    std::shared_ptr<RackState>              authoring_;
    std::vector<std::shared_ptr<RackState>> states_;
    std::atomic<RackState*>                 live_{nullptr};
};

} // namespace nota
