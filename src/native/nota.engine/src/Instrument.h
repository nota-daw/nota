// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Instrument interface (M2). A stable, audio-thread-owned object shared across
// graph snapshots (its voice state must survive structural edits). The engine
// drives it with sample-accurate note events by rendering in segments between
// events (see Engine::processBlock).

#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "TransportInfo.h"

namespace nota {

class Instrument {
public:
    virtual ~Instrument() = default;

    virtual void setSampleRate(double sr) = 0;

    // Note events (applied at segment boundaries by the engine for sample accuracy).
    virtual void noteOn(int32_t pitch, float velocity) = 0;
    virtual void noteOff(int32_t pitch) = 0;
    virtual void allNotesOff() = 0;

    // Add `frames` of interleaved stereo into `out`. Continues current voices.
    virtual void render(float* out, int32_t frames) = 0;

    // Musical transport for generative instruments (e.g. Nota Pendulum): the engine
    // calls this once per block before render() with the block's start beat,
    // samples-per-beat and whether the transport is rolling. Others ignore it.
    virtual void setTransport(double /*beatStart*/, double /*samplesPerBeat*/, bool /*playing*/) {}

    // Block-rate transport snapshot (tempo/position/play state/loop), pushed by the
    // engine right before render so hosted plugins can sync (AudioPlayHead). Built-ins
    // ignore it; a rack forwards it to its chain instruments/devices. Audio thread.
    virtual void setTransportInfo(const TransportInfo&) {}

    // --- Sidechain input ("React"): an instrument may listen to another track's
    // signal as a modulation source (Nota Flux). Mirrors the Device sidechain API.
    // The engine hands the source track's previous-block, post-fader interleaved
    // stereo just before render; a nullptr means "no source this block". Audio thread.
    virtual bool    acceptsSidechain() const { return false; }
    virtual int32_t sidechainSourceTrackId() const { return -1; }
    virtual void    setSidechainSourceTrackId(int32_t /*trackId*/) {}
    virtual void    setSidechain(const float* /*interleaved*/, int32_t /*frames*/) {}

    // Reported processing latency, for PDC (M3-7). 0 for built-ins.
    virtual int32_t latencySamples() const { return 0; }

    // Currently sounding voice count, for a synth editor's voice meter. -1 = the
    // instrument doesn't expose one. Message-thread read of an audio-thread snapshot.
    virtual int32_t activeVoiceCount() const { return -1; }

    // Currently held notes (MIDI pitches), for generative editors that visualise the
    // input chord (e.g. Nota Pendulum). Writes up to maxN into out and returns the count
    // written; 0 = the instrument doesn't expose one. Message-thread read of a snapshot.
    virtual int32_t heldNotes(int32_t* /*out*/, int32_t /*maxN*/) const { return 0; }

    // Live visual telemetry (e.g. a harmonic spectrum for an FM editor). Writes up to
    // maxN floats into out and returns the count written; 0 = no scope. Message-thread
    // read of an audio-thread snapshot (the instrument keeps its own analysis buffer).
    virtual int32_t scopeRead(float* /*out*/, int32_t /*maxN*/) const { return 0; }

    // Instrument identity, for project save (M7-6): 0 = built-in Synth,
    // 1 = built-in Sampler, 2 = built-in Physical (Nota Physical),
    // -1 = hosted plugin / unknown. Message thread only.
    virtual int32_t kind() const { return -1; }

    // Human-readable name (built-in "Synth"/"Sampler", or the hosted plugin's name).
    // Used to label the track. Message thread only.
    virtual const char* displayName() const { return "Instrument"; }

    // Stable plugin identifier for project save (M7-6c); empty for built-ins.
    // Hosted plugins return juce PluginDescription::createIdentifierString().
    // Message thread only.
    virtual std::string pluginIdentifier() const { return {}; }

    // Native editor GUI (M3-4). No-op for built-in instruments; hosted plugins
    // open/hide their editor window. Must be called on the message thread.
    virtual void openEditor() {}
    virtual void closeEditor() {}

    // Opaque plugin state (M3-5), for project save/restore. Empty for built-ins.
    // Message thread only.
    virtual std::vector<uint8_t> getState() const { return {}; }
    virtual void setState(const uint8_t* data, int32_t size) { (void)data; (void)size; }

    // UI command channel for editors that manage state beyond the plugin params —
    // e.g. a drum machine's step patterns / voice selection. id/iarg/farg are
    // instrument-specific. Default no-op. Message thread.
    virtual void action(int32_t /*id*/, int32_t /*iarg*/, float /*farg*/) {}

    // Hosted-plugin parameter automation (M9-B). Values are normalized 0..1 and
    // identified by a stable string paramID (the index is version-unstable).
    // Built-ins expose none; plugin adapters surface their JUCE parameters.
    // count/id/name/indexOf are message-thread; get/set are called block-rate on
    // the audio thread by Engine::applyAutomation (see ARCHITECTURE.md § Automation).
    virtual int32_t     pluginParamCount() const { return 0; }
    virtual std::string pluginParamId(int32_t index) const { (void)index; return {}; }
    virtual std::string pluginParamName(int32_t index) const { (void)index; return {}; }
    virtual float       pluginParamGet(int32_t index) const { (void)index; return 0.0f; }
    virtual void        pluginParamSet(int32_t index, float normalized) { (void)index; (void)normalized; }
    virtual int32_t     pluginParamIndexOfId(const std::string& id) const { (void)id; return -1; }
    // "Learn" (M9-B3): index of the parameter last moved in the plugin's own GUI
    // since the previous call, or -1. Consume-once (returns then clears).
    virtual int32_t     lastTouchedPluginParam() { return -1; }
    // Automation write (M9-C): index of a parameter whose GUI gesture just began /
    // ended in the plugin's own editor, or -1. Consume-once. Lets Touch/Latch record
    // when the user grabs a knob in the plugin window.
    virtual int32_t     takePluginGestureBegin() { return -1; }
    virtual int32_t     takePluginGestureEnd() { return -1; }

    // Deep copy for track duplication (M9-follow-up). Built-ins are cloned by the
    // engine from `kind()`; hosted plugins re-instantiate here (fresh DSP instance +
    // copied state). Returns nullptr if the engine should handle it. Message thread.
    virtual std::shared_ptr<Instrument> clone() const { return nullptr; }
};

} // namespace nota
