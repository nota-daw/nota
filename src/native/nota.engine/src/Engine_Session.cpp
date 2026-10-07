// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — Session view (M5): scenes/slots, launch/stop/record, quantization, session<->arrangement transfer, session notes and audio slots.

#include "Engine.h"
#include "AudioFile.h"
#include "Compressor.h"
#include "CoreAudioBackend.h"
#include "Delay.h"
#include "Eq.h"
#include "PluginHostBridge.h"
#include "Reverb.h"
#include "Sampler.h"
#include "Synth.h"
#include "Utility.h"

#include <algorithm>
#include <chrono>
#include <map>
#include <cmath>

namespace nota {

bool Engine::sessionAudioSlotInfo(int32_t trackId, int32_t scene, NotaSessionAudioSlot* out) const {
    if (!out) return false;
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return false;
    const SessionSlot& sl = t->sessionSlots[scene];
    if (!sl.hasClip || !sl.audio.sample) return false;
    out->sample_id = sl.audio.sample->id;
    out->length_beats = sl.lengthBeats;
    out->source_offset_frames = sl.audio.sourceOffsetFrames;
    out->length_frames = sl.audio.lengthFrames;
    out->gain = sl.audio.gain;
    return true;
}

bool Engine::addSessionAudioClip(int32_t trackId, int32_t scene, const std::string& path,
                                 double lengthBeats, double sourceOffsetFrames,
                                 int64_t lengthFrames, float gain) {
    if (scene < 0) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) <= scene) nt->sessionSlots.resize(scene + 1);
    SessionSlot& sl = nt->sessionSlots[scene];
    sl.hasClip = true;
    sl.lengthBeats = lengthBeats;
    sl.audio = AudioClip{};
    sl.audio.sample = sample;
    sl.audio.sourceOffsetFrames = sourceOffsetFrames;
    sl.audio.lengthFrames = lengthFrames;
    sl.audio.gain = gain;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::addSessionAudioFile(int32_t trackId, int32_t scene, const std::string& path) {
    if (scene < 0) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;

    // Loop length = the file's real duration in beats at the current tempo.
    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    const double seconds = sample->sourceSampleRate > 0 ? sample->frames / sample->sourceSampleRate : 0.0;
    double beats = (spb > 0.0 && sr > 0.0) ? seconds * sr / spb : 4.0;
    if (!(beats > 0.0)) beats = 4.0;

    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) <= scene) nt->sessionSlots.resize(scene + 1);
    SessionSlot& sl = nt->sessionSlots[scene];
    sl.hasClip = true;
    sl.lengthBeats = beats;
    sl.audio = AudioClip{};
    sl.audio.sample = sample;
    sl.audio.sourceOffsetFrames = 0.0;
    sl.audio.lengthFrames = sample->frames;
    sl.audio.gain = 1.0f;
    republishWithTrack(trackId, nt);
    return true;
}

// --- Session view (M5) ------------------------------------------------------
int32_t Engine::sceneCount() const { return authoring_->sceneCount; }

bool Engine::clearSessionSlot(int32_t trackId, int32_t scene) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[scene].hasClip) return false;
    // Stop it first if this slot is the one playing/queued (else it keeps looping empty).
    if (old->sessionPlayer) {
        const int32_t p = old->sessionPlayer->playing.load(std::memory_order_relaxed);
        const int32_t q = old->sessionPlayer->pending.load(std::memory_order_relaxed);
        if (p == scene || q == scene) old->sessionPlayer->requestStop();
    }
    auto nt = cloneTrack(*old);
    nt->sessionSlots[scene] = SessionSlot{};   // empty the slot
    republishWithTrack(trackId, nt);
    return true;
}

// Rebuild the graph with a changed scene count, cloning every track so its sessionSlots
// vector is resized off the audio thread's shared copy. `mutate(slots)` adjusts each clone's
// slot vector (grow for add, erase for remove).
std::shared_ptr<Graph> Engine::rebuildScenes(int32_t newCount,
                                              const std::function<void(std::vector<SessionSlot>&)>& mutate) {
    auto g = std::make_shared<Graph>();
    g->sceneCount   = newCount;
    g->scenes       = authoring_->scenes;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack  = authoring_->masterTrack;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        auto nt = cloneTrack(*t);
        mutate(nt->sessionSlots);
        if (static_cast<int32_t>(nt->sessionSlots.size()) != newCount) nt->sessionSlots.resize(newCount);
        g->tracks.push_back(nt);
    }
    return g;
}

int32_t Engine::addScene() {
    const int32_t idx = authoring_->sceneCount;
    publish(rebuildScenes(idx + 1, [](std::vector<SessionSlot>&) {}));
    return idx;
}

bool Engine::removeScene(int32_t scene) {
    if (authoring_->sceneCount <= 1) return false;   // always keep at least one scene
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    // Removing a row shifts every later slot up: a take recording into the row ends first,
    // players on the row stop, players below it follow their slot up.
    if (recordSessionScene_ == scene) finalizeSessionRecord(false);
    auto g = rebuildScenes(authoring_->sceneCount - 1, [scene](std::vector<SessionSlot>& slots) {
        if (scene < static_cast<int32_t>(slots.size())) slots.erase(slots.begin() + scene);
    });
    if (scene < static_cast<int32_t>(g->scenes.size())) g->scenes.erase(g->scenes.begin() + scene);
    publish(std::move(g));
    remapPlayers([scene](int32_t x) { return x == scene ? -1 : (x > scene ? x - 1 : x); });
    return true;
}

void Engine::setLaunchQuant(double beats) { launchQuant_.store(beats < 0 ? 0 : beats, std::memory_order_relaxed); }

// Instrument slots loop their MIDI clip; audio slots loop their captured/dropped take
// (renderSessionAudioSlotRaw). Both are launchable; other track types have no slots.
static inline bool sessionLaunchable(const Track& t) {
    return t.type() == TrackType::Instrument || t.type() == TrackType::Audio;
}

void Engine::launchSlot(int32_t trackId, int32_t scene) { launchSlotVel(trackId, scene, 1.0f); }

void Engine::launchSlotVel(int32_t trackId, int32_t scene, float velocity) {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer) return;
    launchSlotImpl(*t, scene, velocity, false);
    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!transport_.uiIsPlaying()) { arrangementActive_.store(false, std::memory_order_relaxed); playClock(); }
}

// One track's launch request. An empty slot stops the track — unless the request comes from
// a scene and the slot's stop button was removed. A clip launches with its own quantum,
// legato and launch-velocity gain; Toggle stops a playing slot and Repeat keeps retriggering
// until releaseSlot (direct launches only — a scene always just starts its clips).
void Engine::launchSlotImpl(Track& t, int32_t scene, float velocity, bool fromScene) {
    auto& sp = *t.sessionPlayer;
    const bool inRange = scene >= 0 && scene < static_cast<int32_t>(t.sessionSlots.size());
    const bool hasClip = inRange && t.sessionSlots[scene].hasClip;
    if (!hasClip || !sessionLaunchable(t)) {
        if (fromScene && inRange && !t.sessionSlots[scene].stopButton) return;
        sp.requestStop();
        return;
    }
    const SessionSlot& s = t.sessionSlots[scene];
    if (!fromScene && s.launchMode == LaunchMode::Toggle
        && sp.playing.load(std::memory_order_relaxed) == scene
        && sp.pending.load(std::memory_order_relaxed) == SessionPlayer::kNone) {
        sp.requestStop();
        return;
    }
    const float v = std::clamp(velocity, 0.0f, 1.0f);
    const float gain = 1.0f - std::clamp(s.velocityAmount, 0.0f, 1.0f) * (1.0f - v);
    sp.repeatSlot.store(!fromScene && s.launchMode == LaunchMode::Repeat ? scene : -1, std::memory_order_relaxed);
    sp.requestLaunch(scene, s.quantBeats, s.legato, gain);
}

void Engine::releaseSlot(int32_t trackId, int32_t scene) {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return;
    const SessionSlot& s = t->sessionSlots[scene];
    if (!s.hasClip || (s.launchMode != LaunchMode::Gate && s.launchMode != LaunchMode::Repeat)) return;
    auto& sp = *t->sessionPlayer;
    if (sp.playing.load(std::memory_order_relaxed) == scene || sp.pending.load(std::memory_order_relaxed) == scene)
        sp.requestStop();
}

void Engine::launchScene(int32_t scene) {
    // Launch the whole row at once: every track's slot in this scene fires on the
    // next quantize boundary; tracks with an empty slot here are stopped (scene-launch
    // semantics) unless that slot's stop button was removed.
    for (auto& t : authoring_->tracks)
        if (t->sessionPlayer) launchSlotImpl(*t, scene, 1.0f, true);

    // Scene follow counts from the boundary the row actually starts on.
    const double q = launchQuant_.load(std::memory_order_relaxed);
    const double pos = transport_.uiPositionBeats();
    sceneFollowScene_ = scene;
    sceneFollowStart_ = (q > 0.0 && pos > 1e-9) ? std::ceil(pos / q - 1e-9) * q : pos;

    // Scene tempo / signature apply on that boundary too (at once when the clock is stopped).
    sceneTempoPending_ = false;
    if (scene >= 0 && scene < static_cast<int32_t>(authoring_->scenes.size())) {
        const SceneInfo& si = authoring_->scenes[scene];
        if (si.tempo > 0.0 || (si.sigNum > 0 && si.sigDen > 0)) {
            sceneTempo_ = si.tempo; sceneSigNum_ = si.sigNum; sceneSigDen_ = si.sigDen;
            sceneTempoAt_ = sceneFollowStart_;
            sceneTempoPending_ = true;
            if (!transport_.uiIsPlaying()) applySceneTempo();
        }
    }

    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!transport_.uiIsPlaying()) { arrangementActive_.store(false, std::memory_order_relaxed); playClock(); }
}

void Engine::stopSlot(int32_t trackId) {
    auto t = findTrackAuthoring(trackId);
    if (t && t->sessionPlayer) t->sessionPlayer->requestStop();
}

void Engine::stopScene(int32_t scene) {
    for (auto& t : authoring_->tracks) {
        if (!t->sessionPlayer) continue;
        const int32_t playing = t->sessionPlayer->playing.load(std::memory_order_relaxed);
        const int32_t pending = t->sessionPlayer->pending.load(std::memory_order_relaxed);
        if (playing == scene || pending == scene) t->sessionPlayer->requestStop();
    }
}

void Engine::stopAllSession() {
    for (auto& t : authoring_->tracks)
        if (t->sessionPlayer) t->sessionPlayer->requestStop();
    sceneFollowScene_ = -1;
    stopSessionRecord();
}

void Engine::trackBackToArrangement(int32_t trackId) {
    auto t = findTrackAuthoring(trackId);
    if (t && t->sessionPlayer) t->sessionPlayer->requestStopNow();
}

int32_t Engine::sessionPlayingSlot(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return (t && t->sessionPlayer) ? t->sessionPlayer->playing.load(std::memory_order_relaxed) : -1;
}

double Engine::sessionSlotPosition(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer) return 0.0;
    const int32_t p = t->sessionPlayer->playing.load(std::memory_order_relaxed);
    if (p < 0 || p >= static_cast<int32_t>(t->sessionSlots.size())) return 0.0;
    const double L = t->sessionSlots[p].lengthBeats > 0 ? t->sessionSlots[p].lengthBeats : 4.0;
    double x = std::fmod(t->sessionPlayer->uiLocalBeats.load(std::memory_order_relaxed), L);
    return x < 0.0 ? x + L : x;
}

void Engine::recordSessionSlot(int32_t trackId, int32_t scene) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    if (scene < 0 || scene >= authoring_->sceneCount) return;

    // Audio track: capture input into the slot (M5-4). Reuses the M4-3 input
    // pipeline; stopSessionRecord() materialises the take into slot.audio.
    if (t->type() == TrackType::Audio) {
        recordSessionTrackId_ = trackId;
        recordSessionScene_   = scene;
        recordSessionAudio_   = true;
        if (startAudioRecording(trackId)) {
            recordStartStatus_ = 0;
            recording_.store(true, std::memory_order_relaxed); // drives slot state = recording
        } else {
            recordStartStatus_ = 2;
            recordSessionAudio_ = false;
            recordSessionScene_ = -1;
            recordSessionTrackId_ = 0;
        }
        return;
    }

    if (t->type() != TrackType::Instrument) return;
    // Make sure the slot holds a clip we can overdub into.
    const bool hasClip = scene < static_cast<int32_t>(t->sessionSlots.size())
                         && t->sessionSlots[scene].hasClip;
    // An empty slot records a fresh take: with a fixed length it is that long; otherwise it
    // runs until stopped and is cut to whole quanta then (finalizeSessionRecord).
    recordSessionGrowing_ = false;
    if (!hasClip) {
        recordSessionGrowing_ = sessionRecordLen_ <= 0.0;
        addSessionMidiClip(trackId, scene, recordSessionGrowing_ ? kGrowingTakeBeats : sessionRecordLen_);
        t = findTrackAuthoring(trackId); if (!t) return;
    }

    recordSessionTrackId_ = trackId;
    recordSessionScene_   = scene;
    recordSlotLen_.store(t->sessionSlots[scene].lengthBeats, std::memory_order_relaxed);
    launchSlot(trackId, scene); // play + loop the slot (also auto-starts transport)
    recordSlotPlayer_.store(t->sessionPlayer.get(), std::memory_order_release);
    recording_.store(true, std::memory_order_relaxed);
}

void Engine::stopSessionRecord() { finalizeSessionRecord(true); }

// End an in-progress session recording. relaunch=true loops the fresh take back (the UI's
// "stop recording"); relaunch=false just materialises it and leaves it stopped (a global stop,
// so the take isn't lost). No-op when nothing is recording into a session slot.
void Engine::finalizeSessionRecord(bool relaunch) {
    if (recordSessionScene_ < 0 && !recordSessionAudio_) return;   // not a session record
    // Audio take: materialise into the slot (reads recordSession* + clears the audio flags).
    if (recordSessionAudio_) {
        const int32_t tid = recordSessionTrackId_, sc = recordSessionScene_;
        stopAudioRecording();
        if (relaunch && tid > 0 && sc >= 0) launchSlot(tid, sc);
    }
    // A fresh MIDI take that ran open-ended: cut it to whole launch quanta (a bar when
    // launching is unquantized) — no undo step of its own, so undo drops the whole take.
    if (recordSessionGrowing_ && recordSessionScene_ >= 0) {
        recordSessionGrowing_ = false;
        const int32_t tid = recordSessionTrackId_, sc = recordSessionScene_;
        double el = 0.0;
        if (auto* sp = recordSlotPlayer_.load(std::memory_order_acquire))
            if (sp->playing.load(std::memory_order_relaxed) == sc) el = sp->playedBeats.load(std::memory_order_relaxed);
        if (auto old = findTrackAuthoring(tid); old && sc < static_cast<int32_t>(old->sessionSlots.size())) {
            auto nt = cloneTrack(*old);
            SessionSlot& sl = nt->sessionSlots[sc];
            const double len = roundedTakeBeats(el);
            sl.lengthBeats = len;
            sl.midi.lengthBeats = len;
            republishWithTrackRaw(tid, nt);
        }
    }
    recording_.store(false, std::memory_order_relaxed);
    recordSlotPlayer_.store(nullptr, std::memory_order_release);
    recordSessionScene_ = -1;
    recordSessionTrackId_ = 0;
}

// A take's loop length: the recorded beats rounded to whole launch quanta (bars when
// launching is unquantized or finer than a beat), never shorter than one quantum.
double Engine::roundedTakeBeats(double beats) const {
    double q = launchQuant_.load(std::memory_order_relaxed);
    if (q < 1.0) q = 4.0;
    return std::max(q, std::round(beats / q) * q);
}

int32_t Engine::sessionSlotToArrangement(int32_t trackId, int32_t scene, double startBeat) {
    auto old = findTrackAuthoring(trackId);
    if (!old || (old->type() != TrackType::Instrument && old->type() != TrackType::Audio)) return -1;
    if (scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return -1;
    const SessionSlot& s = old->sessionSlots[scene];
    if (!s.hasClip) return -1;
    auto nt = cloneTrack(*old);
    if (old->type() == TrackType::Audio) {
        // An audio take lands as a plain clip of the slot's sample (one pass of the loop).
        if (!s.audio.sample) return -1;
        AudioClip c = s.audio;
        c.startBeat = startBeat < 0.0 ? 0.0 : startBeat;
        c.name = s.name;
        nt->clips.push_back(c);
        const int32_t idx = static_cast<int32_t>(nt->clips.size()) - 1;
        republishWithTrack(trackId, nt);
        return idx;
    }
    MidiClip c    = s.midi;       // notes + clip envelopes, already relative to clip start
    c.name        = s.name;
    c.startBeat   = startBeat < 0.0 ? 0.0 : startBeat;
    c.lengthBeats = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    c.active      = true;
    nt->midiClips.push_back(c);
    const int32_t idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
    republishWithTrack(trackId, nt);
    return idx;
}

bool Engine::arrangementClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    nt->sessionSlots[scene] = slotFromMidiClip(old->midiClips[clipIndex]);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::arrangementAudioClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    if (!old->clips[clipIndex].sample) return false;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    nt->sessionSlots[scene] = slotFromAudioClip(old->clips[clipIndex]);
    republishWithTrack(trackId, nt);
    return true;
}

// A fresh slot looping one arrangement MIDI clip: its notes and clip envelopes, its name,
// and its length as the loop.
SessionSlot Engine::slotFromMidiClip(const MidiClip& m) const {
    SessionSlot s;
    s.hasClip = true;
    s.midi = m;
    s.midi.startBeat = 0.0;       // slot content is relative to slot start
    s.midi.active = true;         // a slot has no deactivate; it would just stay silent
    s.lengthBeats = m.lengthBeats > 0 ? m.lengthBeats : 4.0;
    s.midi.lengthBeats = s.lengthBeats;
    s.name = m.name;
    return s;
}

// A fresh slot looping one arrangement audio clip. The whole clip comes along — its region,
// warp, pitch, reverse, gain and envelopes — and the slot loops exactly what the clip plays.
SessionSlot Engine::slotFromAudioClip(const AudioClip& a) {
    SessionSlot s;
    s.hasClip = true;
    s.audio = a;
    s.audio.startBeat = 0.0;
    s.audio.active = true;
    if (s.audio.warpEnabled && !s.audio.warpCache)   // a clipboard copy dropped its cache
        configureClipWarp(s.audio, transport_.samplesPerBeat(), transport_.sampleRate());
    s.name = a.name;
    const double beats = audioClipBeats(s.audio);
    s.lengthBeats = beats > 0.0 ? beats : 4.0;
    return s;
}

bool Engine::addSessionMidiClip(int32_t trackId, int32_t scene, double lengthBeats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    SessionSlot& s = nt->sessionSlots[scene];
    s.hasClip = true;
    s.lengthBeats = lengthBeats > 0 ? lengthBeats : 4.0;
    s.midi.lengthBeats = s.lengthBeats;
    s.midi.notes.clear();
    republishWithTrack(trackId, nt);
    return true;
}

double Engine::sessionSlotLength(int32_t trackId, int32_t scene) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0.0;
    const SessionSlot& s = t->sessionSlots[scene];
    return s.hasClip ? s.lengthBeats : 0.0;
}

bool Engine::setSessionSlotLength(int32_t trackId, int32_t scene, double lengthBeats) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[scene].hasClip || lengthBeats <= 0.0) return false;
    auto nt = cloneTrack(*old);
    SessionSlot& s = nt->sessionSlots[scene];
    s.lengthBeats = lengthBeats;
    s.midi.lengthBeats = lengthBeats;
    republishWithTrack(trackId, nt);
    // Keep the audio thread's loop length in sync if we're recording into this slot.
    if (recordSessionTrackId_ == trackId && recordSessionScene_ == scene)
        recordSlotLen_.store(lengthBeats, std::memory_order_relaxed);
    return true;
}

float Engine::sessionSlotGain(int32_t trackId, int32_t scene) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 1.0f;
    const SessionSlot& s = t->sessionSlots[scene];
    return (s.hasClip && s.audio.sample) ? s.audio.gain : 1.0f;
}

bool Engine::setSessionSlotGain(int32_t trackId, int32_t scene, float gain) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[scene].hasClip || !old->sessionSlots[scene].audio.sample) return false;
    auto nt = cloneTrack(*old);
    nt->sessionSlots[scene].audio.gain = gain < 0.0f ? 0.0f : gain;
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::sessionSlotState(int32_t trackId, int32_t scene) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    if (recording_.load(std::memory_order_relaxed) &&
        recordSessionTrackId_ == trackId && recordSessionScene_ == scene) return 4; // recording
    if (auto& sp = t->sessionPlayer) {
        if (sp->playing.load(std::memory_order_relaxed) == scene) return 3; // playing
        if (sp->pending.load(std::memory_order_relaxed) == scene) return 2; // queued
    }
    return t->sessionSlots[scene].hasClip ? 1 : 0;
}

bool Engine::setSessionNotes(int32_t trackId, int32_t scene, const NotaNoteData* notes, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[scene].hasClip) return false;
    auto nt = cloneTrack(*old);
    MidiClip& clip = nt->sessionSlots[scene].midi;
    clip.notes.clear();
    clip.notes.reserve(count);
    for (int32_t i = 0; i < count; ++i) {
        Note n; n.pitch = notes[i].pitch; n.startBeat = notes[i].start_beat;
        n.lengthBeats = notes[i].length_beats; n.velocity = notes[i].velocity;
        clip.notes.push_back(n);
    }
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::getSessionNotes(int32_t trackId, int32_t scene, NotaNoteData* out, int32_t maxNotes) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    const MidiClip& clip = t->sessionSlots[scene].midi;
    const int32_t n = std::min<int32_t>(maxNotes, static_cast<int32_t>(clip.notes.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].pitch = clip.notes[i].pitch;
        out[i].start_beat = clip.notes[i].startBeat;
        out[i].length_beats = clip.notes[i].lengthBeats;
        out[i].velocity = clip.notes[i].velocity;
    }
    return n;
}

int32_t Engine::sessionNoteCount(int32_t trackId, int32_t scene) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    return static_cast<int32_t>(t->sessionSlots[scene].midi.notes.size());
}

// --- Session audio slots as clips (S-06) ------------------------------------------

// A clip's played length in beats: the warp window when warped, else its source region at
// the current tempo (varispeed included).
double Engine::audioClipBeats(const AudioClip& c) const {
    if (c.warpEnabled && c.warpBeats > 0.0) return c.warpPlayLen();
    const double spb = transport_.samplesPerBeat();
    const double devSR = transport_.sampleRate();
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;
    return static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / c.pitchRatio() / spb;
}

// After a clip edit on a slot's audio (clipIndex <= kSessionClipBase): the slot loops the
// clip, so its loop length follows the clip's played length.
void Engine::syncSessionAudioSlot(Track& t, int32_t clipIndex) const {
    const int32_t scene = sessionSceneOfClip(clipIndex);
    if (scene < 0 || scene >= static_cast<int32_t>(t.sessionSlots.size())) return;
    SessionSlot& s = t.sessionSlots[scene];
    if (!s.hasClip || !s.audio.sample) return;
    s.audio.startBeat = 0.0;
    const double beats = audioClipBeats(s.audio);
    if (beats > 0.0) s.lengthBeats = beats;
}

// --- Session P0: clip + scene properties --------------------------------------

namespace {
SessionSlot* slotAt(Track& t, int32_t scene) {
    return (scene >= 0 && scene < static_cast<int32_t>(t.sessionSlots.size())) ? &t.sessionSlots[scene] : nullptr;
}
const SessionSlot* slotAt(const Track& t, int32_t scene) {
    return (scene >= 0 && scene < static_cast<int32_t>(t.sessionSlots.size())) ? &t.sessionSlots[scene] : nullptr;
}
} // namespace

bool Engine::sessionClipProps(int32_t trackId, int32_t scene, NotaSessionClipProps* out) const {
    auto t = findTrackAuthoring(trackId);
    const SessionSlot* s = t ? slotAt(*t, scene) : nullptr;
    if (!s || !s->hasClip || !out) return false;
    out->color = s->color;
    out->launch_mode = static_cast<int32_t>(s->launchMode);
    out->quant_beats = s->quantBeats;
    out->legato = s->legato ? 1 : 0;
    out->loop = s->loop ? 1 : 0;
    out->velocity_amount = s->velocityAmount;
    out->follow_a = static_cast<int32_t>(s->followA);
    out->follow_b = static_cast<int32_t>(s->followB);
    out->chance_a = s->chanceA;
    out->chance_b = s->chanceB;
    out->follow_beats = s->followBeats;
    out->jump_scene = s->jumpScene;
    return true;
}

bool Engine::setSessionClipProps(int32_t trackId, int32_t scene, const NotaSessionClipProps& p) {
    auto old = findTrackAuthoring(trackId);
    const SessionSlot* cur = old ? slotAt(*old, scene) : nullptr;
    if (!cur || !cur->hasClip) return false;
    auto nt = cloneTrack(*old);
    SessionSlot& s = nt->sessionSlots[scene];
    s.color = p.color < 0 ? -1 : p.color;
    s.launchMode = static_cast<LaunchMode>(std::clamp(p.launch_mode, 0, 3));
    s.quantBeats = p.quant_beats < 0.0 ? -1.0 : p.quant_beats;
    s.legato = p.legato != 0;
    s.loop = p.loop != 0;
    s.velocityAmount = std::clamp(p.velocity_amount, 0.0f, 1.0f);
    s.followA = static_cast<FollowAction>(std::clamp(p.follow_a, 0, 9));
    s.followB = static_cast<FollowAction>(std::clamp(p.follow_b, 0, 9));
    s.chanceA = std::clamp(p.chance_a, 0, 100);
    s.chanceB = std::clamp(p.chance_b, 0, 100);
    s.followBeats = p.follow_beats > 0.0 ? p.follow_beats : 0.0;
    s.jumpScene = std::max(0, p.jump_scene);
    republishWithTrack(trackId, nt);
    // A slot leaving Repeat mode must not keep retriggering.
    if (s.launchMode != LaunchMode::Repeat && old->sessionPlayer
        && old->sessionPlayer->repeatSlot.load(std::memory_order_relaxed) == scene)
        old->sessionPlayer->repeatSlot.store(-1, std::memory_order_relaxed);
    return true;
}

bool Engine::sessionClipName(int32_t trackId, int32_t scene, std::string& out) const {
    auto t = findTrackAuthoring(trackId);
    const SessionSlot* s = t ? slotAt(*t, scene) : nullptr;
    if (!s || !s->hasClip) return false;
    out = s->name;
    return true;
}

bool Engine::setSessionClipName(int32_t trackId, int32_t scene, const std::string& name) {
    auto old = findTrackAuthoring(trackId);
    const SessionSlot* cur = old ? slotAt(*old, scene) : nullptr;
    if (!cur || !cur->hasClip) return false;
    if (cur->name == name) return true;
    auto nt = cloneTrack(*old);
    nt->sessionSlots[scene].name = name;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::sessionSlotStopButton(int32_t trackId, int32_t scene) const {
    auto t = findTrackAuthoring(trackId);
    const SessionSlot* s = t ? slotAt(*t, scene) : nullptr;
    return !s || s->stopButton;
}

bool Engine::setSessionSlotStopButton(int32_t trackId, int32_t scene, bool on) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= authoring_->sceneCount) return false;
    if (sessionSlotStopButton(trackId, scene) == on) return true;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    nt->sessionSlots[scene].stopButton = on;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::copySessionSlot(int32_t srcTrack, int32_t srcScene, int32_t dstTrack, int32_t dstScene) {
    auto src = findTrackAuthoring(srcTrack);
    auto dst = findTrackAuthoring(dstTrack);
    if (!src || !dst || src->type() != dst->type() || !sessionLaunchable(*dst)) return false;
    const SessionSlot* from = slotAt(*src, srcScene);
    if (!from || !from->hasClip || dstScene < 0 || dstScene >= authoring_->sceneCount) return false;
    if (srcTrack == dstTrack && srcScene == dstScene) return true;
    const SessionSlot copy = *from;   // before cloning: src may be dst
    auto nt = cloneTrack(*dst);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    nt->sessionSlots[dstScene] = copy;
    republishWithTrack(dstTrack, nt);
    return true;
}

// --- Arrangement clips -> session slots (a selection or the clip clipboard) -------------

int32_t Engine::arrangementClipsToSession(const std::vector<std::pair<int32_t,int32_t>>& sel, int32_t startScene) {
    double len = 0.0;
    return landBlockInSession(captureBlock(sel, len), startScene);
}

int32_t Engine::pasteClipBlockToSession(int32_t destTrackId, int32_t startScene) {
    if (clipboardBlock_.empty() || !authoring_) return -1;
    return landBlockInSession(destTrackId >= 0 ? remapBlock(clipboardBlock_, destTrackId) : clipboardBlock_, startScene);
}

int32_t Engine::sessionSlotsToArrangement(const std::vector<std::pair<int32_t,int32_t>>& slots, double atBeat, int32_t destTrackId) {
    lastPlaced_.clear();
    if (!authoring_) return 0;
    std::map<int32_t, std::vector<int32_t>> byTrack;   // track -> scenes, in order
    for (const auto& [tid, scene] : slots) byTrack[tid].push_back(scene);
    std::vector<BlockClip> items;
    for (auto& [tid, scenes] : byTrack) {
        auto t = findTrackAuthoring(tid);
        if (!t || !sessionLaunchable(*t)) continue;
        std::sort(scenes.begin(), scenes.end());
        scenes.erase(std::unique(scenes.begin(), scenes.end()), scenes.end());
        double at = 0.0;
        for (int32_t scene : scenes) {
            const SessionSlot* sl = slotAt(*t, scene);
            if (!sl || !sl->hasClip) continue;
            BlockClip b;
            b.trackId = tid;
            b.relStart = at;
            if (t->type() == TrackType::Instrument) {
                b.kind = 1;
                b.midi = sl->midi;          // notes + clip envelopes, relative to the slot start
                b.midi.name = sl->name;
                b.midi.active = true;
                b.midi.lengthBeats = sl->lengthBeats > 0 ? sl->lengthBeats : 4.0;
                b.len = b.midi.lengthBeats;
            } else {
                if (!sl->audio.sample) continue;
                b.kind = 0;                 // an audio take lands as one pass of its loop
                b.audio = sl->audio;
                b.audio.name = sl->name;
                b.audio.active = true;
                b.audio.warpCache = nullptr; // placeBlock rebuilds it
                b.len = audioClipBeats(b.audio);
            }
            at += b.len;
            items.push_back(std::move(b));
        }
    }
    if (destTrackId >= 0) items = remapBlock(items, destTrackId);
    if (items.empty()) return 0;
    placeBlock(items, atBeat);
    return static_cast<int32_t>(lastPlaced_.size());
}

// Each track's clips, in time order, fill consecutive slots from one shared scene row, so
// clips that played together in the arrangement share a scene and a sequence reads down
// the column. One undo step, scene rows appended included.
int32_t Engine::landBlockInSession(const std::vector<BlockClip>& items, int32_t startScene) {
    if (!authoring_) return -1;
    std::map<int32_t, std::vector<const BlockClip*>> byTrack;
    for (const auto& b : items) {
        auto t = findTrackAuthoring(b.trackId);
        if (!t || !sessionLaunchable(*t)) continue;
        if ((b.kind == 1) != (t->type() == TrackType::Instrument)) continue;
        if (b.kind == 0 && !b.audio.sample) continue;
        byTrack[b.trackId].push_back(&b);
    }
    if (byTrack.empty()) return -1;
    int32_t rows = 0;
    for (auto& [tid, v] : byTrack) {
        std::stable_sort(v.begin(), v.end(), [](const BlockClip* x, const BlockClip* y) { return x->relStart < y->relStart; });
        rows = std::max(rows, static_cast<int32_t>(v.size()));
    }

    int32_t first = startScene;
    if (first < 0) {
        // The first row from which every track's run of slots is empty (rows past the end are).
        auto runFree = [&](int32_t row) {
            for (const auto& [tid, v] : byTrack) {
                auto t = findTrackAuthoring(tid);
                for (int32_t i = 0; i < static_cast<int32_t>(v.size()); ++i)
                    if (const SessionSlot* sl = slotAt(*t, row + i); sl && sl->hasClip) return false;
            }
            return true;
        };
        first = 0;
        while (first < authoring_->sceneCount && !runFree(first)) ++first;
    }

    beginUndoGroup();
    while (authoring_->sceneCount < first + rows) insertScene(authoring_->sceneCount);
    for (const auto& [tid, v] : byTrack) {
        auto old = findTrackAuthoring(tid);
        if (!old) continue;
        auto nt = cloneTrack(*old);
        if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
            nt->sessionSlots.resize(authoring_->sceneCount);
        for (int32_t i = 0; i < static_cast<int32_t>(v.size()); ++i)
            nt->sessionSlots[first + i] = v[i]->kind == 1 ? slotFromMidiClip(v[i]->midi) : slotFromAudioClip(v[i]->audio);
        republishWithTrack(tid, nt);
    }
    endUndoGroup();
    return first;
}

namespace {
SceneInfo sceneOr(const Graph& g, int32_t scene) {
    return (scene >= 0 && scene < static_cast<int32_t>(g.scenes.size())) ? g.scenes[scene] : SceneInfo{};
}
} // namespace

bool Engine::sceneProps(int32_t scene, NotaSceneProps* out) const {
    if (!out || scene < 0 || scene >= authoring_->sceneCount) return false;
    const SceneInfo si = sceneOr(*authoring_, scene);
    out->color = si.color;
    out->tempo = si.tempo;
    out->sig_num = si.sigNum;
    out->sig_den = si.sigDen;
    out->follow = si.follow ? 1 : 0;
    out->follow_beats = si.followBeats;
    return true;
}

bool Engine::setSceneProps(int32_t scene, const NotaSceneProps& p) {
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    auto g = std::make_shared<Graph>(*authoring_);   // tracks shared; only scene metadata changes
    if (static_cast<int32_t>(g->scenes.size()) < g->sceneCount) g->scenes.resize(g->sceneCount);
    SceneInfo& si = g->scenes[scene];
    si.color = p.color < 0 ? -1 : p.color;
    si.tempo = p.tempo > 0.0 ? std::clamp(p.tempo, 20.0, 999.0) : 0.0;
    const bool sig = p.sig_num > 0 && p.sig_den > 0;
    si.sigNum = sig ? std::clamp(p.sig_num, 1, 32) : 0;
    si.sigDen = sig ? std::clamp(p.sig_den, 1, 32) : 0;
    si.follow = p.follow != 0;
    si.followBeats = p.follow_beats > 0.0 ? p.follow_beats : 32.0;
    publish(std::move(g));
    return true;
}

bool Engine::sceneName(int32_t scene, std::string& out) const {
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    out = sceneOr(*authoring_, scene).name;
    return true;
}

bool Engine::setSceneName(int32_t scene, const std::string& name) {
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    if (sceneOr(*authoring_, scene).name == name) return true;
    auto g = std::make_shared<Graph>(*authoring_);
    if (static_cast<int32_t>(g->scenes.size()) < g->sceneCount) g->scenes.resize(g->sceneCount);
    g->scenes[scene].name = name;
    publish(std::move(g));
    return true;
}

// --- Session P0: scene rows ------------------------------------------------------

// Shift every player (and the recording / follow targets) after a row insert or removal.
// map(x) returns the row's new index, or -1 when it is gone. Players are atomics shared with
// the audio thread, so each is moved with a CAS: a launch racing the edit wins.
void Engine::remapPlayers(const std::function<int32_t(int32_t)>& map) {
    for (auto& t : authoring_->tracks) {
        if (!t->sessionPlayer) continue;
        auto& sp = *t->sessionPlayer;
        int32_t p = sp.playing.load(std::memory_order_relaxed);
        if (p >= 0) { const int32_t n = map(p); sp.playing.compare_exchange_strong(p, n < 0 ? -1 : n, std::memory_order_relaxed); }
        int32_t q = sp.pending.load(std::memory_order_relaxed);
        if (q >= 0) { const int32_t n = map(q); sp.pending.compare_exchange_strong(q, n < 0 ? SessionPlayer::kNone : n, std::memory_order_relaxed); }
        int32_t r = sp.repeatSlot.load(std::memory_order_relaxed);
        if (r >= 0) sp.repeatSlot.compare_exchange_strong(r, map(r), std::memory_order_relaxed);
    }
    if (recordSessionScene_ >= 0) { const int32_t n = map(recordSessionScene_); if (n >= 0) recordSessionScene_ = n; }
    if (sceneFollowScene_ >= 0) sceneFollowScene_ = map(sceneFollowScene_);
}

// Insert a row at `at`: empty, a copy of row `copyFrom` (old index), or each track's
// currently playing clip (capture). One undo step.
int32_t Engine::insertSceneRow(int32_t at, int32_t copyFrom, bool capturePlaying) {
    const int32_t count = authoring_->sceneCount;
    if (at < 0 || at > count || copyFrom >= count) return -1;
    auto g = std::make_shared<Graph>();
    g->sceneCount   = count + 1;
    g->scenes       = authoring_->scenes;
    g->masterVolume = authoring_->masterVolume;
    g->masterTrack  = authoring_->masterTrack;
    if (static_cast<int32_t>(g->scenes.size()) < count) g->scenes.resize(count);
    g->scenes.insert(g->scenes.begin() + at, copyFrom >= 0 ? g->scenes[copyFrom] : SceneInfo{});
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        auto nt = cloneTrack(*t);
        auto& slots = nt->sessionSlots;
        if (static_cast<int32_t>(slots.size()) < count) slots.resize(count);
        SessionSlot row;
        if (copyFrom >= 0) row = slots[copyFrom];
        else if (capturePlaying && t->sessionPlayer) {
            const int32_t p = t->sessionPlayer->playing.load(std::memory_order_relaxed);
            if (p >= 0 && p < count) row = slots[p];
        }
        slots.insert(slots.begin() + at, row);
        g->tracks.push_back(nt);
    }
    publish(std::move(g));
    remapPlayers([at](int32_t x) { return x >= at ? x + 1 : x; });
    return at;
}

int32_t Engine::insertScene(int32_t at) { return insertSceneRow(at, -1, false); }
int32_t Engine::duplicateScene(int32_t scene) {
    if (scene < 0 || scene >= authoring_->sceneCount) return -1;
    return insertSceneRow(scene + 1, scene, false);
}
int32_t Engine::captureScene(int32_t at) { return insertSceneRow(at, -1, true); }

// Reorder a row: every track's slot and the scene's metadata move together; players follow.
bool Engine::moveScene(int32_t from, int32_t to) {
    const int32_t count = authoring_->sceneCount;
    if (from < 0 || from >= count || to < 0 || to >= count) return false;
    if (from == to) return true;
    auto move = [from, to](auto& v) {
        auto item = v[from];
        v.erase(v.begin() + from);
        v.insert(v.begin() + to, item);
    };
    auto g = rebuildScenes(count, [&](std::vector<SessionSlot>& slots) {
        if (static_cast<int32_t>(slots.size()) < count) slots.resize(count);
        move(slots);
    });
    if (static_cast<int32_t>(g->scenes.size()) < count) g->scenes.resize(count);
    move(g->scenes);
    publish(std::move(g));
    remapPlayers([from, to](int32_t x) {
        if (x == from) return to;
        if (from < to && x > from && x <= to) return x - 1;
        if (to < from && x >= to && x < from) return x + 1;
        return x;
    });
    return true;
}

// --- Session P0: recording ---------------------------------------------------------

int32_t Engine::recordSessionScene(int32_t scene) {
    if (scene < 0 || scene >= authoring_->sceneCount) return 0;
    for (auto& t : authoring_->tracks) {
        if (!t->armed() || !sessionLaunchable(*t)) continue;
        const SessionSlot* s = slotAt(*t, scene);
        if (s && s->hasClip) continue;
        recordSessionSlot(t->id(), scene);
        return t->id();
    }
    return 0;
}

bool Engine::sessionRecordTarget(int32_t* trackId, int32_t* scene, double* elapsed) const {
    if (!recording_.load(std::memory_order_relaxed) || recordSessionScene_ < 0 || recordSessionTrackId_ <= 0) return false;
    double el = 0.0;
    if (recordSessionAudio_) el = std::max(0.0, transport_.uiPositionBeats() - audioRecordStartBeat_);
    else if (auto* sp = recordSlotPlayer_.load(std::memory_order_acquire))
        if (sp->playing.load(std::memory_order_relaxed) == recordSessionScene_)
            el = sp->playedBeats.load(std::memory_order_relaxed);
    if (trackId) *trackId = recordSessionTrackId_;
    if (scene) *scene = recordSessionScene_;
    if (elapsed) *elapsed = el;
    return true;
}

// poll(): fixed-length capture ends a take by itself, and a following scene queues the next
// row one launch quantum ahead so it lands on the boundary where this one's time runs out.
bool Engine::takeSceneTempoChange(double* bpm, int32_t* num, int32_t* den) {
    if (!sceneTempoApplied_) return false;
    sceneTempoApplied_ = false;
    if (bpm) *bpm = sceneTempo_;
    if (num) *num = sceneSigNum_;
    if (den) *den = sceneSigDen_;
    return true;
}

void Engine::applySceneTempo() {
    sceneTempoPending_ = false;
    sceneTempoApplied_ = true;   // the UI's transport picks the new values up (takeSceneTempoChange)
    if (sceneTempo_ > 0.0) setBpm(sceneTempo_);
    if (sceneSigNum_ > 0 && sceneSigDen_ > 0) setTimeSignature(sceneSigNum_, sceneSigDen_);
}

void Engine::serviceSessionFollow() {
    if (sceneTempoPending_ && transport_.uiPositionBeats() + 1e-6 >= sceneTempoAt_) applySceneTempo();
    if (sessionRecordLen_ > 0.0) {
        double el = 0.0;
        if (sessionRecordTarget(nullptr, nullptr, &el) && el >= sessionRecordLen_ - 1e-6) stopSessionRecord();
    }
    if (!transport_.uiIsPlaying()) { sceneFollowScene_ = -1; return; }
    const int32_t sc = sceneFollowScene_;
    if (sc < 0 || sc >= authoring_->sceneCount || !sessionFollow()) return;
    const SceneInfo si = sceneOr(*authoring_, sc);
    if (!si.follow || si.followBeats <= 0.0) return;
    const double q = launchQuant_.load(std::memory_order_relaxed);
    const double lead = q > 0.0 ? std::min(q, si.followBeats) : 0.0;
    const double due = sceneFollowStart_ + si.followBeats;
    if (transport_.uiPositionBeats() + lead + 1e-6 < due) return;
    launchScene((sc + 1) % authoring_->sceneCount);
    sceneFollowStart_ = due;   // exactly where the outgoing scene's time ran out
}

// Peaks of an audio slot's take (whole sample) for the grid / inspector preview.
int32_t Engine::sessionSlotPeaks(int32_t trackId, int32_t scene, float* outMinMax, int32_t maxPoints) const {
    return getClipPeaks(trackId, sessionClipIndex(scene), outMinMax, maxPoints);
}

// --- Session P0: audio thread ---------------------------------------------------

int32_t Engine::followTarget(const Track& t, int32_t cur, FollowAction a) {
    const int32_t n = static_cast<int32_t>(t.sessionSlots.size());
    auto filled = [&](int32_t i) { return i >= 0 && i < n && t.sessionSlots[i].hasClip; };
    auto rnd = [&]() { uint32_t x = followRng_; x ^= x << 13; x ^= x >> 17; x ^= x << 5; followRng_ = x; return x; };
    switch (a) {
    case FollowAction::Stop:  return -1;
    case FollowAction::Again: return cur;
    case FollowAction::Next:
        for (int32_t k = 1; k <= n; ++k) if (filled((cur + k) % n)) return (cur + k) % n;
        return cur;
    case FollowAction::Previous:
        for (int32_t k = 1; k <= n; ++k) if (filled(((cur - k) % n + n) % n)) return ((cur - k) % n + n) % n;
        return cur;
    case FollowAction::First:
        for (int32_t i = 0; i < n; ++i) if (filled(i)) return i;
        return cur;
    case FollowAction::Last:
        for (int32_t i = n - 1; i >= 0; --i) if (filled(i)) return i;
        return cur;
    case FollowAction::Any:
    case FollowAction::Other: {
        int32_t pool[256]; int32_t m = 0;
        for (int32_t i = 0; i < n && m < 256; ++i)
            if (filled(i) && (a == FollowAction::Any || i != cur)) pool[m++] = i;
        return m ? pool[rnd() % static_cast<uint32_t>(m)] : cur;
    }
    case FollowAction::Jump: {
        const int32_t j = t.sessionSlots[cur].jumpScene;
        return filled(j) ? j : -1;
    }
    default: return cur;
    }
}

void Engine::sessionPreApply(Track& t, double blockBeats) {
    auto& sp = *t.sessionPlayer;
    const int32_t cur = sp.playing.load(std::memory_order_relaxed);
    if (cur < 0 || cur >= static_cast<int32_t>(t.sessionSlots.size())) return;
    if (sp.pending.load(std::memory_order_relaxed) != SessionPlayer::kNone) return;
    const SessionSlot& s = t.sessionSlots[cur];

    // Repeat: while held, queue a relaunch of the same slot at every quantum.
    if (sp.repeatSlot.load(std::memory_order_relaxed) == cur) {
        double q = s.quantBeats >= 0.0 ? s.quantBeats : launchQuant_.load(std::memory_order_relaxed);
        if (q <= 0.0) q = 0.25;
        sp.pendingQuant.store(q, std::memory_order_relaxed);
        sp.pendingLegato.store(false, std::memory_order_relaxed);
        sp.pendingGain.store(sp.gain, std::memory_order_relaxed);
        int32_t expect = SessionPlayer::kNone;
        sp.pending.compare_exchange_strong(expect, cur, std::memory_order_relaxed);
        return;
    }

    // Follow action: once the slot has played its follow time, pick A or B by weight.
    if (!sessionFollow_.load(std::memory_order_relaxed)) return;
    if (s.followA == FollowAction::None && s.followB == FollowAction::None) return;
    const double due = s.followBeats > 0.0 ? s.followBeats : (s.lengthBeats > 0.0 ? s.lengthBeats : 4.0);
    if (sp.playedBeats.load(std::memory_order_relaxed) + blockBeats * 0.5 < due) return;
    const int32_t total = s.chanceA + s.chanceB;
    uint32_t x = followRng_; x ^= x << 13; x ^= x >> 17; x ^= x << 5; followRng_ = x;
    const FollowAction a = (total <= 0 || static_cast<int32_t>(x % 100u) * total < s.chanceA * 100) ? s.followA : s.followB;
    if (a == FollowAction::None) { sp.playedBeats.store(0.0, std::memory_order_relaxed); return; }
    const int32_t target = followTarget(t, cur, a);
    int32_t expect = SessionPlayer::kNone;
    if (target < 0) { sp.pending.compare_exchange_strong(expect, SessionPlayer::kStopNow, std::memory_order_relaxed); return; }
    sp.pendingQuant.store(0.0, std::memory_order_relaxed);   // follow time is exact, not quantized
    sp.pendingLegato.store(false, std::memory_order_relaxed);
    sp.pendingGain.store(sp.gain, std::memory_order_relaxed);
    sp.pending.compare_exchange_strong(expect, target, std::memory_order_relaxed);
}

void Engine::sessionPostRender(Track& t, double blockBeats) {
    auto& sp = *t.sessionPlayer;
    sp.publish(blockBeats);
    const int32_t cur = sp.playing.load(std::memory_order_relaxed);
    if (cur < 0 || cur >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[cur];
    const double L = s.lengthBeats > 0.0 ? s.lengthBeats : 4.0;
    if (s.loop || sp.localBeats < L) return;
    // One-shot reached its end: stop the track (a queued launch still applies next block).
    int32_t expect = cur;
    if (sp.playing.compare_exchange_strong(expect, -1, std::memory_order_relaxed)) {
        if (t.instrument) t.instrument->allNotesOff();
        for (auto& md : t.midiEffects) if (md) md->reset();
    }
}

} // namespace nota
