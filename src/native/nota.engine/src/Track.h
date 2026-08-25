// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio track + clip model (M1). A Track is a stable object referenced by id;
// its mix parameters are atomics written by the UI thread and read by the audio
// thread. Its clip list is immutable once published in a graph snapshot
// (structural edits build a new snapshot — see Graph.h / AR-5).

#pragma once

#include "Automation.h"
#include "CompensationDelay.h"
#include "Device.h"
#include "Instrument.h"
#include "MidiClip.h"
#include "MidiDevice.h"
#include "Modulation.h"
#include "SampleBuffer.h"
#include "SessionPlayer.h"
#include "Warp.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {

// A placed audio clip. `startBeat` is the musical anchor on the timeline; the
// audio itself plays at its natural rate (no time-stretch in MVP — AR/Non-Goal).
struct AudioClip {
    std::shared_ptr<SampleBuffer> sample;
    std::string name;             // user-facing clip name (empty = default)
    double  startBeat = 0.0;      // timeline position (beats)
    double  sourceOffsetFrames = 0.0; // start offset into the sample
    int64_t lengthFrames = 0;     // clip length in *source* frames (0 = whole sample)
    bool    active = true;        // clip deactivate (key 0): false = stays but silent
    float   gain = 1.0f;
    float   pitchSemitones = 0.0f; // varispeed transpose (no warp): also scales duration
    // Warp (time-stretch to tempo). When enabled, `warpCache` (below) holds the played
    // window pre-stretched at the current tempo/device rate and the audio thread copies
    // it straight (pitch baked in). Rebuilt on tempo/pitch/mode/marker/window/SR change.
    // Off => varispeed via pitchSemitones (above).
    bool     warpEnabled = false;
    WarpMode warpMode = WarpMode::Complex;
    double   warpBeats = 0.0;      // full warped material length (beats) = last marker's beat
    std::vector<WarpMarker> warpMarkers; // source↔beat anchors (≥2 when warped)
    // Played sub-window of the warp (trim). Beats within [0, warpBeats];
    // the clip plays [warpPlayStart, warpPlayEndEff()] placed at startBeat. 0/0 => whole
    // warp (default, back-compat). Trimming leaves warpBeats/markers untouched.
    double   warpPlayStart = 0.0;
    double   warpPlayEnd = 0.0;    // 0 = to warpBeats
    // Offline stretch cache: the played window pre-rendered to a
    // device-rate buffer on the authoring thread. The audio thread copies straight
    // from it — no realtime stretcher, no per-clip-start priming spike. Immutable +
    // shared across snapshots; rebuilt on tempo/pitch/mode/marker/window/SR change.
    // Null when warp is off (or while a rebuild is pending).
    std::shared_ptr<const WarpCache> warpCache;
    // Clip volume envelope (M9 follow-up): breakpoints in clip-local beats, value
    // 0..1, multiplying the per-sample output. Empty = no envelope (fast path).
    AutomationLane volumeEnvelope;
    // Clip pan envelope (M9 follow-up): value -1..1 (balance law, unity at centre),
    // applied per sample after the volume envelope. Empty = no envelope.
    AutomationLane panEnvelope;

    int64_t effectiveLength() const {
        if (!sample) return 0;
        int64_t maxLen = sample->frames - static_cast<int64_t>(sourceOffsetFrames);
        if (lengthFrames <= 0) return maxLen;
        return lengthFrames < maxLen ? lengthFrames : maxLen;
    }
    // Played warp window (beats). Default (0/0) is the whole warp [0, warpBeats].
    double warpPlayEndEff() const { return warpPlayEnd > 0.0 ? std::min(warpPlayEnd, warpBeats) : warpBeats; }
    double warpPlayLen()    const { return std::max(0.0, warpPlayEndEff() - warpPlayStart); }
    // Playback speed multiplier from the transpose (2^(semitones/12)). >1 plays
    // faster + higher-pitched over fewer beats; <1 slower + lower over more beats.
    double pitchRatio() const { return std::pow(2.0, static_cast<double>(pitchSemitones) / 12.0); }
};

// A Session-view clip slot (M5). One per scene on each track. Holds a MIDI clip
// (instrument tracks) or a captured audio take (audio tracks, M5-4); the type is
// implied by the track type. Immutable in a snapshot.
struct SessionSlot {
    bool     hasClip = false;
    MidiClip midi;              // instrument tracks: notes relative to slot start
    AudioClip audio;            // audio tracks: captured sample, looped over lengthBeats (M5-4)
    double   lengthBeats = 4.0; // loop length
};

enum class TrackType { Audio, Instrument, Return, Group };

// Maximum number of return (aux/send) buses (M6-1). Each track carries a fixed
// atomic send level per bus, so send routing is RT-safe and allocation-free.
inline constexpr int kMaxReturns = 4;

class Track {
public:
    explicit Track(int32_t id, TrackType type = TrackType::Audio) : id_(id), type_(type) {}

    int32_t   id() const { return id_; }
    TrackType type() const { return type_; }

    // UI-thread setters (atomic) — read on the audio thread, no locks.
    void setVolume(float v) { volume_.store(v, std::memory_order_relaxed); }
    void setPan(float p)    { pan_.store(p, std::memory_order_relaxed); }
    void setMute(bool m)    { mute_.store(m, std::memory_order_relaxed); }
    void setSolo(bool s)    { solo_.store(s, std::memory_order_relaxed); }
    void setArmed(bool a)   { armed_.store(a, std::memory_order_relaxed); }

    float volume() const { return volume_.load(std::memory_order_relaxed); }
    float pan() const    { return pan_.load(std::memory_order_relaxed); }
    bool  mute() const   { return mute_.load(std::memory_order_relaxed); }
    bool  solo() const   { return solo_.load(std::memory_order_relaxed); }
    bool  armed() const  { return armed_.load(std::memory_order_relaxed); }

    // MIDI → CV (modular editor): the track's latest MIDI-derived control values,
    // updated from the post-FX note stream each block (see computeInstrumentMidi) and
    // read by MIDI→CV modulators. Transient (audio-thread state, not snapshot-copied).
    void updateMidiCv(const MidiEv* evs, int n) {
        int held = midiHeld_.load(std::memory_order_relaxed);
        float vel = midiVel_.load(std::memory_order_relaxed);
        float note = midiNote_.load(std::memory_order_relaxed);
        for (int i = 0; i < n; ++i) {
            if (evs[i].on) { vel = evs[i].vel; note = evs[i].pitch / 127.0f; ++held; }
            else if (held > 0) --held;
        }
        midiHeld_.store(held < 0 ? 0 : held, std::memory_order_relaxed);
        midiVel_.store(vel, std::memory_order_relaxed);
        midiNote_.store(note, std::memory_order_relaxed);
    }
    float midiVelocity() const { return midiVel_.load(std::memory_order_relaxed); }
    float midiNoteNorm() const { return midiNote_.load(std::memory_order_relaxed); }
    bool  midiGate()     const { return midiHeld_.load(std::memory_order_relaxed) > 0; }

    // Metering (M6-2): post-fader block peak/RMS per channel. Written by the
    // audio thread each block, read by the UI. Transient — not copied across
    // snapshots (a freshly cloned track just re-populates on the next block).
    void setMeter(float pkL, float pkR, float rmsL, float rmsR) {
        mPeakL_.store(pkL, std::memory_order_relaxed);
        mPeakR_.store(pkR, std::memory_order_relaxed);
        mRmsL_.store(rmsL, std::memory_order_relaxed);
        mRmsR_.store(rmsR, std::memory_order_relaxed);
    }
    float meterPeakL() const { return mPeakL_.load(std::memory_order_relaxed); }
    float meterPeakR() const { return mPeakR_.load(std::memory_order_relaxed); }
    float meterRmsL()  const { return mRmsL_.load(std::memory_order_relaxed); }
    float meterRmsR()  const { return mRmsR_.load(std::memory_order_relaxed); }

    // Send levels to return buses (M6-1), post-fader. `bus` is a return slot
    // 0..kMaxReturns-1. Atomic (continuous UI control, like the fader).
    void  setSend(int32_t bus, float level) {
        if (bus >= 0 && bus < kMaxReturns) send_[bus].store(level, std::memory_order_relaxed);
    }
    float send(int32_t bus) const {
        return (bus >= 0 && bus < kMaxReturns) ? send_[bus].load(std::memory_order_relaxed) : 0.0f;
    }

    // Return-bus identity (M6-1): >=0 for return tracks, giving the bus slot they
    // read; -1 for regular tracks. Set once at creation.
    int32_t returnIndex() const { return returnIndex_; }
    void    setReturnIndex(int32_t i) { returnIndex_ = i; }

    // Group membership: the id of the parent Group track this track belongs to, or -1 for
    // top-level. A track of type Group can itself be nested inside another Group (its own
    // groupId points at the parent). Structural (message-thread) field, like returnIndex_.
    int32_t groupId() const { return groupId_; }
    void    setGroupId(int32_t id) { groupId_ = id; }

    // Record input source for an audio track (internal resampling): 0 = hardware input
    // (default), -1 = master bus, >0 = another track's post-fader output (by id). Atomic
    // (audio thread reads it while capturing).
    int32_t recordInputSource() const { return recordInputSource_.load(std::memory_order_relaxed); }
    void    setRecordInputSource(int32_t s) { recordInputSource_.store(s, std::memory_order_relaxed); }

    // MIDI routing: send this instrument track's note events (clips + live input) to
    // another instrument track's instrument as well. -1 = off. The destination reads
    // this while rendering, so it's atomic. One hop only (not transitive).
    int32_t midiFromTrackId() const { return midiFromTrackId_.load(std::memory_order_relaxed); }
    void    setMidiFromTrackId(int32_t id) { midiFromTrackId_.store(id, std::memory_order_relaxed); }

    // Freeze (M7): when frozen, the render substitutes `frozenBuf` (this track's
    // post-device, PRE-fader audio, captured offline over the whole arrangement) for
    // the live instrument + device chain — the fader/pan/sends/mute/solo still run
    // live. The buffer is indexed by beat via `frozenSpb` (frame = beat·frozenSpb) so
    // it stays aligned when the render sample rate changes (e.g. an export bounce).
    bool frozen() const { return frozen_.load(std::memory_order_relaxed); }
    void setFrozen(bool f) { frozen_.store(f, std::memory_order_relaxed); }

    // Immutable after publish (only touched while building a snapshot).
    std::vector<AudioClip> clips;       // audio tracks
    std::vector<MidiClip>  midiClips;   // instrument tracks
    std::vector<SessionSlot> sessionSlots; // Session view, one per scene (M5)
    std::vector<AutomationLane> automation; // parameter automation lanes (M9)

    // CV modulation (Phase 3, Modular editor). Sources + edges to device params.
    // Config lives in atomics (live-tweakable); add/remove is structural (clone).
    std::vector<Modulator> modulators;
    std::vector<CvLink>    cvLinks;
    int32_t nextModId = 1;              // monotonic modulator-id source

    // UI metadata (message thread only; never read by the audio thread). `name` is the
    // user-facing track name (empty = fall back to a default); `colorIndex` is a palette
    // slot (-1 = auto: assign by position).
    std::string name;
    int32_t     colorIndex = -1;

    // Stable across snapshots (voice/DSP state must survive structural edits).
    std::shared_ptr<Instrument> instrument;          // instrument tracks
    std::vector<std::shared_ptr<MidiDevice>> midiEffects; // MIDI FX chain, before the instrument
    std::vector<std::shared_ptr<Device>> devices;    // insert effect chain (M3)

    // Plugin-delay compensation (M3-7). Shared across snapshots (state persists).
    std::shared_ptr<CompensationDelay> pdc = std::make_shared<CompensationDelay>();

    // Session playback (M5-2). Shared across snapshots (position/state persists).
    std::shared_ptr<SessionPlayer> sessionPlayer = std::make_shared<SessionPlayer>();

    // Freeze buffer (M7): interleaved stereo PCM (null = not frozen), captured at
    // `frozenSpb` samples-per-beat. Shared across snapshots; only replaced on a
    // (un)freeze structural edit, so the audio thread reads it lock-free like `instrument`.
    std::shared_ptr<const std::vector<float>> frozenBuf;
    double frozenSpb = 0.0;

private:
    int32_t   id_;
    TrackType type_;
    std::atomic<float> volume_{1.0f};
    std::atomic<float> pan_{0.0f};   // -1 (L) .. +1 (R)
    std::atomic<bool>  mute_{false};
    std::atomic<bool>  solo_{false};
    std::atomic<bool>  armed_{false};
    std::atomic<float> midiVel_{0.0f};    // MIDI→CV: last note-on velocity 0..1
    std::atomic<float> midiNote_{0.0f};   // last note pitch, normalized 0..1
    std::atomic<int32_t> midiHeld_{0};    // held-note count (gate = >0)
    std::atomic<float> mPeakL_{0.0f};
    std::atomic<float> mPeakR_{0.0f};
    std::atomic<float> mRmsL_{0.0f};
    std::atomic<float> mRmsR_{0.0f};
    std::atomic<float> send_[kMaxReturns] = {};  // per-return send levels (0 default)
    int32_t            returnIndex_ = -1;         // >=0 for return tracks
    int32_t            groupId_ = -1;             // parent Group track id, or -1 (top-level)
    std::atomic<int32_t> recordInputSource_{0};   // 0 hardware, -1 master, >0 source track id
    std::atomic<int32_t> midiFromTrackId_{-1};      // -1 off, >0 forward MIDI to this track id
    std::atomic<bool>    frozen_{false};            // M7: play frozenBuf instead of the live chain
};

} // namespace nota
