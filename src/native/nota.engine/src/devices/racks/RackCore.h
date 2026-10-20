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

#include "devices/Instrument.h"
#include "engine/SnapshotHistory.h"
#include "devices/Device.h"

#include "media/SampleBuffer.h"
#include "devices/BuiltinDevices.h"

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
    static constexpr int32_t kMaxBlock = 16384; // matches Engine::kMaxBlock

    struct ChainControls {
        std::atomic<float> gain{1.0f};
        std::atomic<float> pan{0.0f}; // -1..+1
        std::atomic<bool> mute{false};
        std::atomic<bool> solo{false};
        std::atomic<int32_t> triggerNote{-1}; // Drum Rack: the pad's MIDI note; -1 = all notes (instrument/effect rack)
        std::atomic<int32_t> keyLo{0}, keyHi{127}; // Instrument Rack: key zone (MIDI note range)
        std::atomic<int32_t> velLo{0}, velHi{127}; // velocity range (MIDI 0..127)
        std::atomic<float> meter{0.0f};            // post-render peak (UI meter)
        // Drum Rack per-pad shaping (v6). tune is passed to the pad instrument on
        // trigger; decay drives an amp VCA on the pad's summed output; chokeGroup
        // makes pads in the same group monophonic (a new hit cuts the others).
        std::atomic<int32_t> chokeGroup{0}; // 0 = none, 1..8 choke group
        std::atomic<int32_t> tune{0};       // pad transpose, semitones (-48..+48)
        std::atomic<float> decay{1.0f};     // pad decay 0..1 (1 = sustain / no shaping)
        float ampEnv = 0.0f;                // audio-thread only: pad decay VCA state
        bool retrig = false;                // audio-thread only: noteOn asks for an env kick
    };

    struct RackChain {
        std::shared_ptr<Instrument> instrument;       // null for effect-rack chains
        std::vector<std::shared_ptr<Device>> devices; // insert chain
        std::shared_ptr<ChainControls> ctl = std::make_shared<ChainControls>();
        // Display name (v8). Message thread only — the audio thread never reads it.
        // Empty means "fall back to the chain instrument's name": a Drum Rack pad that
        // holds a Sampler would otherwise read "Nota Sampler" on all 16 pads.
        std::string name;
    };

    // A macro (0..1) drives one target param over [rangeMin, rangeMax] in the
    // target's own units (instrument targets use the normalized 0..1 plugin-param
    // surface; device targets use the native min..max generic-param surface).
    struct MacroMapping {
        int32_t macro = 0;
        int32_t chainIndex = 0;
        int32_t deviceIndex = -1; // -1 = the chain's instrument, kPadControls = the pad itself, else a device
        int32_t paramIndex = 0;
        float rangeMin = 0.0f;
        float rangeMax = 1.0f;
        int32_t curve = 0; // 0 Linear, 1 Exp, 2 Log, 3 S-curve
    };

    // A mapping with this deviceIndex targets the chain's own controls (a Drum Rack pad's
    // strip) rather than a child: paramIndex is a PadParam, in the control's own units.
    static constexpr int32_t kPadControls = -2;
    enum PadParam : int32_t { PadVolume = 0, PadPan, PadTune, PadDecay, kPadParams };
    static const char* padParamName(int32_t p) {
        static const char* n[kPadParams] = {"Volume", "Pan", "Tune", "Decay"};
        return (p >= 0 && p < kPadParams) ? n[p] : "";
    }
    static float padParamMin(int32_t p) { return p == PadPan ? -1.0f : p == PadTune ? -48.0f : 0.0f; }
    static float padParamMax(int32_t p) { return p == PadVolume ? 2.0f : p == PadTune ? 48.0f : 1.0f; }

    struct RackState {
        std::vector<RackChain> chains;
        std::vector<MacroMapping> mappings;
    };

    // Hosted plugins can't be created in the JUCE-free core, so the engine installs
    // factories that instantiate a plugin child by its stable identifier (used when
    // a saved rack blob is reloaded). Empty id → nullptr (an effect-rack chain with
    // no instrument). Live drops inject instances directly via addChain*Instance.
    struct PluginFactory {
        std::function<std::shared_ptr<Instrument>(const std::string& id)> instrumentById;
        std::function<std::shared_ptr<Device>(const std::string& id)> effectById;
    };
    static PluginFactory& pluginFactory() {
        static PluginFactory f;
        return f;
    }

    RackCore() {
        for (auto& m : macros_) m.store(0.0f, std::memory_order_relaxed);
        commit(std::make_shared<RackState>());
    }
    virtual ~RackCore() = default;

    // Audio-thread view of the current snapshot (raw pointer, retained by states_
    // until the renders that could still hold it have finished).
    RackState* live() const { return states_.live(); }

    // Fan the transport snapshot out to every chain child so hosted plugins nested
    // in a rack sync to the DAW clock too (the Instrument/Device overrides call this).
    void forwardTransport(const TransportInfo& ti);

    // The DAW tempo for tempo-synced chain children (a synced Delay, Auto Filter, Auto Pan …):
    // without it they ran on their free times inside a rack. Audio thread, once per block.
    void forwardTempo(double beatStart, double spb, bool playing);

    // Sample rate + max block; forwarded to every child. Message thread.
    void setRates(double sr, int32_t maxBlock);

    // ---- chain name (message thread; UI metadata) -------------------------
    std::string chainName(int32_t c) const {
        RackState* st = live();
        return (st && c >= 0 && c < static_cast<int32_t>(st->chains.size())) ? st->chains[c].name : std::string{};
    }
    bool setChainName(int32_t c, const std::string& name);

    // ---- structural editing (message thread) ------------------------------
    int32_t chainCount() const {
        RackState* st = live();
        return st ? static_cast<int32_t>(st->chains.size()) : 0;
    }
    // Adds a chain; instKind 0=Synth, 2=Physical, <0/other = no instrument
    // (effect-rack chains). Returns the new chain index.
    int32_t addChain(int32_t instKind);
    // Adds a chain hosting a pre-built instrument (e.g. a Sampler the engine loaded
    // from a file — RackCore can't decode audio). Returns the new chain index.
    int32_t addChainInstrument(std::shared_ptr<Instrument> inst);
    // Load a sample into a chain's Sampler (or turn the chain into one), keeping its params
    // + devices. Publishes a fresh Sampler via a state swap (thread-safe, no in-place mutate).
    bool setChainInstrumentSample(int32_t chain, std::shared_ptr<SampleBuffer> buf, int32_t root, bool loop);
    bool removeChain(int32_t chain);
    bool setChainInstrument(int32_t chain, int32_t instKind);
    int32_t chainInstrumentKind(int32_t chain) const;
    Instrument* chainInstrument(int32_t chain) const;
    int32_t chainDeviceCount(int32_t chain) const;
    int32_t addChainDevice(int32_t chain, int32_t deviceKind);
    // Adds a pre-built device (e.g. a hosted plugin the engine instantiated) to a chain.
    int32_t addChainDeviceInstance(int32_t chain, std::shared_ptr<Device> dev);
    bool removeChainDevice(int32_t chain, int32_t dev);
    bool moveChainDevice(int32_t chain, int32_t from, int32_t to);
    Device* chainDeviceAt(int32_t chain, int32_t dev) const;

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

    // Drum Rack per-pad shaping (v6).
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

    // Key/velocity zone (Instrument Rack): only notes inside the range reach this chain.
    void setChainZone(int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi);
    void chainZone(int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const;
    float chainMeter(int32_t c) const {
        auto* x = chainControls(c);
        return x ? x->meter.load(std::memory_order_relaxed) : 0.0f;
    }

    // Rack output: gain + glide (message thread), volume applied at render.
    float rackVolume() const { return rackVolume_.load(std::memory_order_relaxed); }
    void setRackVolume(float v) { rackVolume_.store(std::clamp(v, 0.0f, 4.0f), std::memory_order_relaxed); }
    float rackGlide() const { return glide_.load(std::memory_order_relaxed); }
    void setRackGlide(float v) { glide_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Drum Rack kit-level timing (v6): swing pushes off-beat 1/16 hits late; humanize
    // adds a small random offset. Applied by the engine's event scheduler (Engine_Render).
    float swing() const { return swing_.load(std::memory_order_relaxed); }
    void setSwing(float v) { swing_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    float humanize() const { return humanize_.load(std::memory_order_relaxed); }
    void setHumanize(float v) { humanize_.store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Audio Effect Rack routing + output stage (v7). mode: 0 Parallel, 1 Series,
    // 2 Select (chains whose select-zone [velLo..velHi] covers the selector are active).
    // dryWet mixes the rack's wet output against the dry input; pdc reports chain
    // latency to the host; chainSelect is the manual selector 0..1 and selFollow drives
    // it from the input level instead. liveSelector is the runtime position (UI marker).
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

    // ---- macros -----------------------------------------------------------
    float macroGet(int32_t i) const {
        return (i >= 0 && i < kNumMacros) ? macros_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void macroSet(int32_t i, float v);
    // Macro plugin-param surface, shared by the Instrument/Device overrides.
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
    // Custom macro name (falls back to "Macro N" when unset). Message thread.
    std::string macroName(int32_t i) const {
        if (i < 0 || i >= kNumMacros) return {};
        return macroNames_[i].empty() ? macroParamName(i) : macroNames_[i];
    }
    void setMacroName(int32_t i, const std::string& name) {
        if (i >= 0 && i < kNumMacros) macroNames_[i] = name;
    }

    // ---- mappings ---------------------------------------------------------
    int32_t addMacroMapping(
        int32_t macro, int32_t chain, int32_t deviceIndex, int32_t paramIndex, float rangeMin, float rangeMax);
    int32_t mappingCount() const {
        RackState* st = live();
        return st ? static_cast<int32_t>(st->mappings.size()) : 0;
    }
    bool mappingInfo(int32_t i, MacroMapping& out) const;
    bool removeMapping(int32_t i);
    // Update an existing mapping's range / response curve (Macro-map editing).
    bool setMappingRange(int32_t i, float rangeMin, float rangeMax);
    bool setMappingCurve(int32_t i, int32_t curve);
    int32_t mappingCurve(int32_t i) const {
        RackState* st = live();
        return (st && i >= 0 && i < static_cast<int32_t>(st->mappings.size())) ? st->mappings[i].curve : 0;
    }

    // ---- persistence blob (shared format; instKind -1 = no instrument) ----
    std::vector<uint8_t> serialize() const;
    void deserialize(const uint8_t* data, int32_t size);

  protected:
    // Max chain latency (per-chain internal alignment is a follow-up).
    int32_t maxChainLatency() const;

  public:
    // Built-in instrument factory (public so the Engine can read a fresh one's param
    // defaults for a chain instrument, e.g. double-click reset on Sampler knobs).
    static std::shared_ptr<Instrument> makeInstrument(int32_t kind);

  protected:
    static std::shared_ptr<Device> makeDevice(int32_t kind) { return makeChainDevice(kind); }

    // Audio thread: hold one for the whole render/process call (it ends the render
    // epoch that frees retired snapshots).
    SnapshotHistory<RackState>::RenderScope renderScope() { return SnapshotHistory<RackState>::RenderScope(states_); }

    // Macro response curve applied to the 0..1 value before mapping to [min,max].
    static float applyCurve(float v, int32_t curve);

    void applyMacro(int32_t macro, float value);

    std::atomic<float> macros_[kNumMacros];
    std::string macroNames_[kNumMacros];   // custom macro names (message thread; "" = default M#)
    std::atomic<float> rackVolume_{1.0f};  // Instrument Rack output gain (linear)
    std::atomic<float> glide_{0.0f};       // rack glide 0..1 (→ ms; stored/reserved)
    std::atomic<float> swing_{0.0f};       // Drum Rack swing 0..1 (v6)
    std::atomic<float> humanize_{0.0f};    // Drum Rack humanize 0..1 (v6)
    std::atomic<int32_t> mode_{0};         // Audio Effect Rack: 0 Parallel, 1 Series, 2 Select (v7)
    std::atomic<float> dryWet_{1.0f};      // effect-rack rack-out dry/wet (1 = full wet)
    std::atomic<int32_t> pdc_{1};          // PDC compensation on(1)/off(0)
    std::atomic<float> chainSelect_{0.5f}; // Select-mode manual selector 0..1
    std::atomic<int32_t> selFollow_{0};    // Select follows input level
    std::atomic<float> liveSel_{0.5f};     // runtime selector position (audio writes, UI reads)
    double sampleRate_ = 44100.0;
    int32_t maxBlock_ = kMaxBlock;

  private:
    std::shared_ptr<RackState> copyState() const {
        return authoring_ ? std::make_shared<RackState>(*authoring_) : std::make_shared<RackState>();
    }
    void commit(std::shared_ptr<RackState> ns) {
        authoring_ = ns;
        states_.publish(std::move(ns));
    }
    static void pruneMappings(std::shared_ptr<RackState>& ns, int32_t removedChain);

    static void putU8(std::vector<uint8_t>& b, uint8_t v) { b.push_back(v); }
    static void putU32(std::vector<uint8_t>& b, uint32_t v) {
        for (int i = 0; i < 4; ++i) b.push_back(uint8_t(v >> (i * 8)));
    }
    static void putI32(std::vector<uint8_t>& b, int32_t v) {
        uint32_t u;
        std::memcpy(&u, &v, 4);
        putU32(b, u);
    }
    static void putF32(std::vector<uint8_t>& b, float v) {
        uint32_t u;
        std::memcpy(&u, &v, 4);
        putU32(b, u);
    }
    static void putU64(std::vector<uint8_t>& b, uint64_t v) {
        for (int i = 0; i < 8; ++i) b.push_back(uint8_t(v >> (i * 8)));
    }
    static void putI64(std::vector<uint8_t>& b, int64_t v) {
        uint64_t u;
        std::memcpy(&u, &v, 8);
        putU64(b, u);
    }
    static void putF64(std::vector<uint8_t>& b, double v) {
        uint64_t u;
        std::memcpy(&u, &v, 8);
        putU64(b, u);
    }
    static void putString(std::vector<uint8_t>& b, const std::string& s) {
        putU32(b, static_cast<uint32_t>(s.size()));
        b.insert(b.end(), s.begin(), s.end());
    }
    struct Cursor {
        const uint8_t* d;
        int32_t n;
        int32_t p;
        bool ok() const { return p <= n; }
        const uint8_t* take(uint32_t len) {
            if (p + static_cast<int64_t>(len) > n) {
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
        for (int i = 0; i < 4; ++i) v |= uint32_t(c.d[c.p++]) << (i * 8);
        return v;
    }
    static int32_t getI32(Cursor& c) {
        uint32_t u = getU32(c);
        int32_t v;
        std::memcpy(&v, &u, 4);
        return v;
    }
    static float getF32(Cursor& c) {
        uint32_t u = getU32(c);
        float v;
        std::memcpy(&v, &u, 4);
        return v;
    }
    static uint64_t getU64(Cursor& c) {
        if (c.p + 8 > c.n) {
            c.p = c.n + 1;
            return 0;
        }
        uint64_t v = 0;
        for (int i = 0; i < 8; ++i) v |= uint64_t(c.d[c.p++]) << (i * 8);
        return v;
    }
    static int64_t getI64(Cursor& c) {
        uint64_t u = getU64(c);
        int64_t v;
        std::memcpy(&v, &u, 8);
        return v;
    }
    static double getF64(Cursor& c) {
        uint64_t u = getU64(c);
        double v;
        std::memcpy(&v, &u, 8);
        return v;
    }
    static std::string getString(Cursor& c);
    static float clamp01(float v) { return std::isfinite(v) ? std::clamp(v, 0.0f, 1.0f) : 0.0f; }

    static constexpr uint32_t kMagic = 0x314B524E; // 'NRK1'
    static constexpr uint32_t kBlobVersion =
        8; // v2 triggerNote; v3 Sampler; v4 plugins; v5 zones/names/rack-out/curve; v6 drum pad shaping + swing/humanize; v7 effect-rack mode/dry-wet/pdc/select; v8 chain name

    std::shared_ptr<RackState> authoring_;
    SnapshotHistory<RackState> states_;
};

} // namespace nota
