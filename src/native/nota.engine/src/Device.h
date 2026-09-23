// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Effect device interface (M3). Like Instrument, a Device is a stable,
// audio-thread-owned object shared across graph snapshots. It processes a
// track's stereo buffer in place, after the instrument/clips and before the
// channel fader (an insert effect). Hosted plugins slot in as PluginEffect
// (see pluginhost/), keeping this interface JUCE-free.

#pragma once

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "TransportInfo.h"

namespace nota {

struct MidiEv;

class Device {
public:
    virtual ~Device() = default;

    // Called on the message thread before the device is used (and on sample-rate
    // changes). maxBlock is the largest frame count a process() call may pass.
    virtual void setSampleRate(double sr, int32_t maxBlock) = 0;

    // Process `frames` of interleaved stereo in place. Audio thread; no locks.
    virtual void process(float* buf, int32_t frames) = 0;

    // Reported processing latency, for future PDC (M3-7).
    virtual int32_t latencySamples() const { return 0; }

    // Native editor GUI (M3-4). Message thread only.
    virtual void openEditor() {}
    virtual void closeEditor() {}

    // Opaque plugin state (M3-5), for project save/restore. Message thread only.
    virtual std::vector<uint8_t> getState() const { return {}; }
    virtual void setState(const uint8_t* data, int32_t size) { (void)data; (void)size; }

    // Display name for the device rack (M4). Built-ins return a literal; hosted
    // plugins return their name.
    virtual const char* displayName() const { return "Device"; }

    // Built-in identity, for project save (M7-6): the `kind` accepted by
    // Engine::addTrackBuiltinDevice (0=EQ,1=Compressor,2=Reverb,3=Delay,
    // 4=Utility, 6=Amp), or -1 for a hosted plugin. Message thread only.
    virtual int32_t builtinKind() const { return -1; }

    // Stable plugin identifier for project save (M7-6c); empty for built-ins.
    // Message thread only.
    virtual std::string pluginIdentifier() const { return {}; }

    // Built-in parameter interface (M4-4/5). Params are plain floats in
    // [min,max]; get/set is atomic and lock-free (UI thread <-> audio thread).
    // Hosted plugins keep the defaults (0 params) — they use their own editor.
    virtual int32_t     paramCount() const { return 0; }
    virtual const char* paramName(int32_t index) const { (void)index; return ""; }
    virtual float       paramMin(int32_t index) const { (void)index; return 0.0f; }
    virtual float       paramMax(int32_t index) const { (void)index; return 1.0f; }
    virtual float       getParam(int32_t index) const { (void)index; return 0.0f; }
    virtual void        setParam(int32_t index, float value) { (void)index; (void)value; }

    // Hosted-plugin parameter automation (M9-B). Distinct from the built-in
    // param interface above: values are normalized 0..1 and identified by a
    // stable string paramID. Built-ins expose none; PluginEffect surfaces its
    // JUCE parameters. get/set run block-rate on the audio thread (ARCHITECTURE.md § Automation).
    virtual int32_t     pluginParamCount() const { return 0; }
    virtual std::string pluginParamId(int32_t index) const { (void)index; return {}; }
    virtual std::string pluginParamName(int32_t index) const { (void)index; return {}; }
    virtual float       pluginParamGet(int32_t index) const { (void)index; return 0.0f; }
    virtual void        pluginParamSet(int32_t index, float normalized) { (void)index; (void)normalized; }
    virtual int32_t     pluginParamIndexOfId(const std::string& id) const { (void)id; return -1; }
    // "Learn" (M9-B3): index of the parameter last moved in the plugin's own GUI
    // since the previous call, or -1. Consume-once (returns then clears).
    virtual int32_t     lastTouchedPluginParam() { return -1; }
    // Automation write (M9-C): GUI gesture begin/end param index, consume-once (-1 none).
    virtual int32_t     takePluginGestureBegin() { return -1; }
    virtual int32_t     takePluginGestureEnd() { return -1; }

    // Deep copy for track duplication. Built-ins are cloned by the engine from
    // builtinKind()+params; hosted plugins re-instantiate here (+copied state).
    // Returns nullptr if the engine should handle it. Message thread.
    virtual std::shared_ptr<Device> clone() const { return nullptr; }

    // Live gain reduction (dB, >=0) for metering/visualization. Non-zero only for
    // dynamics devices (the built-in Compressor); others report 0. Audio thread
    // writes an atomic, UI reads lock-free (like the per-track level meters).
    virtual float gainReductionDb() const { return 0.0f; }

    // Real-time analyzer feed: copy up to maxSamples of a recently-seen mono signal
    // into out (oldest→newest), returning the count written. Non-zero only for
    // devices with a scope (the built-in EQ-8's pre-EQ spectrum). Audio thread
    // writes a ring, UI reads lock-free; torn reads are acceptable for a visualizer.
    virtual int32_t scopeRead(float* /*out*/, int32_t /*maxSamples*/) const { return 0; }

    // Interactive-device command channel (the looper's Record/Overdub/Play/Stop/
    // Undo/Clear and per-layer mute/gain). Message thread pokes lock-free state; the
    // audio thread applies it at the next quantize boundary. Most devices ignore it.
    virtual void deviceAction(int32_t /*id*/, int32_t /*iarg*/, float /*farg*/) {}

    // A loader (a rack's saved state) has just restored the first `count` params from a save.
    // A device whose layout grew by appended params can put them back to what an older save
    // meant (Nota EQ-3: Range → Classic). Message thread.
    virtual void paramsRestored(int32_t /*count*/) {}

    // Per-layer waveform envelope for multi-layer devices (the looper): fill up to
    // maxSamples peak bins of layer's buffer, oldest→newest, returning the count.
    virtual int32_t layerWave(int32_t /*layer*/, float* /*out*/, int32_t /*maxSamples*/) const { return 0; }

    // Devices that carry a named resource (the Chamber's impulse response): an id-specific
    // text (resource name, category, a catalogue listing …); empty by default. Message thread.
    virtual std::string deviceText(int32_t /*id*/) const { return {}; }

    // Load an auxiliary audio file into the device (the Chamber's user impulse response).
    // Returns false when the device takes no file or it can't be decoded. Message thread.
    virtual bool loadFile(const std::string& /*path*/) { return false; }

    // Sidechain / signal routing (Phase B). A device may key its processing off
    // another track's signal. sidechainSourceTrackId() reports the chosen source
    // track (-1 = none); the engine assigns a routing slot and, right before each
    // process() call, hands the device that source's stereo buffer (previous block,
    // interleaved) via setSidechain — or nullptr when there's no source. Message
    // thread sets the source; audio thread reads the buffer. Non-dynamics devices
    // ignore all of this.
    virtual int32_t sidechainSourceTrackId() const { return -1; }
    virtual void    setSidechainSourceTrackId(int32_t /*trackId*/) {}
    virtual void    setSidechain(const float* /*interleaved*/, int32_t /*frames*/) {}

    // Whether this device can key off a sidechain source at all: true for the
    // built-in Compressor and for hosted plugins that expose a sidechain input
    // bus (Phase C). The UI only offers a source picker when this is true.
    virtual bool acceptsSidechain() const { return false; }

    // MIDI key: a device that follows another track's notes (Nota Auto Shift's MIDI target)
    // returns true; the engine then hands it the sidechain source track's post-FX note
    // events for the block (offsets within the block) right before process(). Audio thread.
    virtual bool wantsMidiKey() const { return false; }
    virtual void setMidiKey(const MidiEv* /*evs*/, int32_t /*n*/) {}

    // Sidechain shaping (Phase D). Stored on the base so they
    // persist/query generically; each dynamics device reads them in process():
    //  - gain: dB applied to the sidechain detector signal (raises how hard the
    //    source drives the effect — the usual "make it audible" knob);
    //  - mix: dry/wet of this device's effect (0 = bypass-ish, 1 = full);
    //  - tapPre: which point on the source track the engine taps (0 = post-FX
    //    post-fader, 1 = pre-FX pre-fader). Read by the engine, not the device.
    float   sidechainGainDb() const { return scGainDb_.load(std::memory_order_relaxed); }
    void    setSidechainGainDb(float db) { scGainDb_.store(db, std::memory_order_relaxed); }
    float   sidechainMix() const { return scMix_.load(std::memory_order_relaxed); }
    void    setSidechainMix(float m) { scMix_.store(std::clamp(m, 0.0f, 1.0f), std::memory_order_relaxed); }
    int32_t sidechainTapPre() const { return scTapPre_.load(std::memory_order_relaxed); }
    void    setSidechainTapPre(int32_t pre) { scTapPre_.store(pre ? 1 : 0, std::memory_order_relaxed); }

    // Musical transport for tempo-synced devices (e.g. Beat Repeat). The engine calls
    // this on the audio thread just before process() with the block's start position in
    // beats, samples-per-beat, and whether the transport is rolling. Non-synced devices
    // ignore it (default no-op).
    virtual void setTransport(double /*beatStart*/, double /*samplesPerBeat*/, bool /*playing*/) {}

    // Block-rate transport snapshot, pushed right before process so hosted-plugin
    // effects (synced delays/LFOs) follow the DAW. Built-ins ignore it; a rack device
    // forwards it to its chain instruments/devices. Audio thread.
    virtual void setTransportInfo(const TransportInfo&) {}

    // Bypass (M3-6): when true, process() must leave the buffer untouched.
    bool bypassed() const { return bypassed_.load(std::memory_order_relaxed); }
    void setBypassed(bool b) { bypassed_.store(b, std::memory_order_relaxed); }

private:
    std::atomic<bool> bypassed_{false};
    std::atomic<float> scGainDb_{0.0f};
    std::atomic<float> scMix_{1.0f};
    std::atomic<int32_t> scTapPre_{0};
};

} // namespace nota
