// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MIDI effect interface. A MidiDevice transforms the note-event stream *before*
// the instrument (unlike Device, which processes the audio buffer after it). Like
// Instrument/Device it is a stable, audio-thread-owned object shared across graph
// snapshots — its live state (an arpeggiator's held chord, step phase, pending
// note-offs) must survive structural edits. The engine builds the block's sorted
// note events, runs them through each track's midiEffects chain, then re-sorts and
// drives the instrument (see Engine::renderInstrumentRaw). JUCE-free.

#pragma once

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {

// A note event with a sample offset within the block. Same layout as the local
// event struct the render paths already sort and feed to the instrument, so a
// MidiDevice's output drives that segment loop with no conversion.
struct MidiEv {
    int32_t off;    // sample offset within the block
    bool    on;     // true = note-on, false = note-off
    int32_t pitch;  // 0..127
    float   vel;    // 0..1 (note-on)
};

class MidiDevice {
public:
    virtual ~MidiDevice() = default;

    virtual void setSampleRate(double sr, int32_t /*maxBlock*/) {}

    // Transform `in` (nIn sorted events) into `out` (writing at most maxOut events,
    // set nOut). beatStart is the block's start position in beats (transport when
    // playing, an internal accumulator when stopped-but-live, or the slot clock in
    // Session); spb = samples per beat; playing distinguishes those clock domains.
    virtual void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                         int32_t frames, double beatStart, double spb, bool playing) = 0;

    // Flush all live state (held notes, pending offs, phase) WITHOUT touching params.
    // Called at transport stop / loop wrap / session slot switch, alongside the
    // instrument's allNotesOff, to avoid ghost/stuck notes across clock changes.
    virtual void reset() {}

    // Built-in identity (0 = Arpeggiator), or -1 for a future hosted MIDI plugin.
    virtual int32_t     midiKind() const { return -1; }
    virtual const char* displayName() const { return "MidiDevice"; }

    // Generic float parameter interface (same shape as Device): plain floats in
    // [min,max], atomic + lock-free get/set (UI thread <-> audio thread).
    virtual int32_t     paramCount() const { return 0; }
    virtual const char* paramName(int32_t index) const { (void)index; return ""; }
    virtual float       paramMin(int32_t index) const { (void)index; return 0.0f; }
    virtual float       paramMax(int32_t index) const { (void)index; return 1.0f; }
    virtual float       getParam(int32_t index) const { (void)index; return 0.0f; }
    virtual void        setParam(int32_t index, float value) { (void)index; (void)value; }

    // Live telemetry for editors: the last note-on this device remapped, input and output
    // value (Nota Scale IN→OUT pitch, Nota Velocity IN→OUT velocity). -1 = none / not
    // exposed. Message-thread read of an audio-thread snapshot.
    virtual int32_t midiLastIn() const { return -1; }
    virtual int32_t midiLastOut() const { return -1; }

    // A float scope buffer for the editor (e.g. Nota Velocity's recent in/out pairs). Writes
    // up to maxN floats into out, returns the count. 0 = no scope. Message-thread read.
    virtual int32_t midiScope(float* /*out*/, int32_t /*maxN*/) const { return 0; }

    // Deep copy for track duplication (rebuilt by midiKind() + copied params by the
    // engine; live state starts fresh). Returns nullptr if the engine should handle it.
    virtual std::shared_ptr<MidiDevice> clone() const { return nullptr; }

    // Opaque state for project save/restore (default: params-only via the engine).
    virtual std::vector<uint8_t> getState() const { return {}; }
    virtual void setState(const uint8_t* data, int32_t size) { (void)data; (void)size; }

    // Bypass: when true, process() passes input through unchanged.
    bool bypassed() const { return bypassed_.load(std::memory_order_relaxed); }
    void setBypassed(bool b) { bypassed_.store(b, std::memory_order_relaxed); }

    // Map/CC routing: a device (the arpeggiator) can drive a target audio-device param
    // on the same track from its per-step CC lane. destDevice = the track's audio-device
    // chain index (-2 = no routing), destParam = that device's param index, depth 0..1
    // scales the modulation; ccValue() is the current normalized CC value (0..1). The
    // engine reads these each block after the MIDI chain and writes the target param.
    virtual int32_t ccDestDevice() const { return -2; }
    virtual int32_t ccDestParam() const { return -1; }
    virtual float   ccDepth() const { return 0.0f; }
    virtual float   ccValue() const { return 0.0f; }
    virtual void    setCcDest(int32_t /*destDevice*/, int32_t /*destParam*/) {}
    virtual void    setCcDepth(float /*depth*/) {}

private:
    std::atomic<bool> bypassed_{false};
};

} // namespace nota
