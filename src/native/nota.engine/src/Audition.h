// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preset audition: a standalone chain — MIDI effects → instrument → audio effects — built
// fresh from built-in kinds, that renders a short phrase OFFLINE into a buffer. It is not a
// track and never enters the graph, so it can be built, rendered and thrown away on any
// worker thread while the audio thread plays the project; the finished buffer is handed to
// the engine's preview voice (see Engine::auditionStore / previewCached), which gives the
// browser player its waveform, seek, loop and level for free.
//
// An effect preset has no instrument: a source buffer is fed through its devices instead —
// a file, or a demo track the managed side composes from parts (each part a rig of its own,
// mixed in with addSourceFrom and kept by key with cacheSource, so a track is rendered once
// per session). A kit (Drum Rack / Nota Rhythm) plays on useKit's pad instrument: one-shot
// pads with gain, pan, choke groups and their own effect chains. Rendering runs in 256-frame
// blocks with a cancel flag checked between them, so a stale audition (the selection moved
// on) stops within a block. After the phrase the rig keeps rendering until the tail
// (release, reverb, delay) falls below −80 dB or the tail budget runs out (then it fades).

#pragma once

#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "SampleBuffer.h"

namespace nota {

class Device;
class Instrument;
class MidiDevice;
class AuditionKit;

struct AuditionNote {
    double  startBeat;
    double  lengthBeats;
    int32_t pitch;
    float   velocity;   // 0..1
};

class AuditionRig {
public:
    // Render flags.
    static constexpr int32_t kRolling = 1;   // instruments see a rolling transport (generative synths)

    explicit AuditionRig(double sampleRate);
    ~AuditionRig();

    bool    setInstrument(int32_t kind);
    bool    instrumentParam(const std::string& id, float value);           // normalized, by plugin-param id
    int32_t addDevice(int32_t kind);
    bool    deviceParam(int32_t index, const std::string& name, float value);
    int32_t addMidiEffect(int32_t kind);
    bool    midiParam(int32_t index, const std::string& name, float value);
    // The Sampler's or Nota Grain's sample, rooted at rootNote: a file, or (empty path, the
    // Sampler only) a procedural keys tone rooted at C4.
    bool    setSamplerSample(const std::string& path, int32_t rootNote = 60);
    // Nota Mosaic's program; waits (up to timeoutMs) until its samples are in, so the
    // offline render that follows hears them. False when the instrument isn't a Mosaic.
    bool    setMosaicProgram(const std::string& text, int32_t timeoutMs = 20000);
    bool    setSourceFile(const std::string& path, double maxSeconds);
    // Mixes another rig's rendered result into this rig's source (a demo track's part).
    bool    addSourceFrom(const AuditionRig& part, float gain);
    // Demo tracks, rendered once: cacheSource normalizes the mixed source to targetPeak and
    // keeps it (process-wide, any thread) under key; useCachedSource takes it back.
    bool    useCachedSource(const std::string& key);
    bool    cacheSource(const std::string& key, float targetPeak);

    // Kits: a pad instrument in place of a built-in one. Pan −1..1, choke 0 = none.
    bool    useKit();
    int32_t kitAddPad(int32_t note, const std::string& path, float gain, float pan, int32_t choke);
    int32_t kitPadAddDevice(int32_t pad, int32_t kind);
    bool    kitPadDeviceParam(int32_t pad, int32_t device, const std::string& name, float value);

    // Renders the phrase (plus its tail) into the result buffer. Returns the frame count,
    // or -1 when cancelled / nothing to render. Call once per rig.
    int64_t render(const AuditionNote* notes, int32_t count, double bpm, double phraseBeats,
                   double maxTailSeconds, int32_t flags);
    void    cancel() { cancelled_.store(true, std::memory_order_relaxed); }

    // (min, max) pairs over both channels of the result; returns the bucket count.
    int32_t peaks(float* outMinMax, int32_t maxPoints) const;
    double  seconds() const { return result_ && sr_ > 0 ? result_->frames / sr_ : 0.0; }
    std::shared_ptr<SampleBuffer> result() const { return result_; }

private:
    double sr_;
    std::shared_ptr<Instrument> inst_;
    std::vector<std::shared_ptr<Device>> devices_;
    std::vector<std::shared_ptr<MidiDevice>> midi_;
    std::shared_ptr<const SampleBuffer> source_;   // stereo at sr_
    std::shared_ptr<SampleBuffer> mixing_;         // source_ while parts are mixed in
    std::shared_ptr<SampleBuffer> result_;
    AuditionKit* kit_ = nullptr;                   // inst_ when useKit()
    std::atomic<bool> cancelled_{false};
};

} // namespace nota
