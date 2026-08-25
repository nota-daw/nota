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
    // Removing a row shifts every later slot up, so stop all session playback to avoid a
    // player pointing at the wrong (or gone) slot.
    for (auto& t : authoring_->tracks)
        if (t->sessionPlayer) t->sessionPlayer->requestStop();
    publish(rebuildScenes(authoring_->sceneCount - 1, [scene](std::vector<SessionSlot>& slots) {
        if (scene < static_cast<int32_t>(slots.size())) slots.erase(slots.begin() + scene);
    }));
    return true;
}

void Engine::setLaunchQuant(double beats) { launchQuant_.store(beats < 0 ? 0 : beats, std::memory_order_relaxed); }

// Instrument slots loop their MIDI clip; audio slots loop their captured/dropped take
// (renderSessionAudioSlotRaw). Both are launchable; other track types have no slots.
static inline bool sessionLaunchable(const Track& t) {
    return t.type() == TrackType::Instrument || t.type() == TrackType::Audio;
}

void Engine::launchSlot(int32_t trackId, int32_t scene) {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer) return;
    const bool hasClip = scene >= 0 && scene < static_cast<int32_t>(t->sessionSlots.size())
                         && t->sessionSlots[scene].hasClip;
    if (hasClip && sessionLaunchable(*t)) t->sessionPlayer->requestLaunch(scene);
    else t->sessionPlayer->requestStop();
    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!transport_.uiIsPlaying()) { arrangementActive_.store(false, std::memory_order_relaxed); playClock(); }
}

void Engine::launchScene(int32_t scene) {
    // Launch the whole row at once: every track's slot in this scene fires on the
    // next quantize boundary; tracks with an empty slot here are stopped (scene-launch semantics).
    for (auto& t : authoring_->tracks) {
        if (!t->sessionPlayer) continue;
        const bool hasClip = scene >= 0 && scene < static_cast<int32_t>(t->sessionSlots.size())
                             && t->sessionSlots[scene].hasClip;
        if (hasClip && sessionLaunchable(*t)) t->sessionPlayer->requestLaunch(scene);
        else t->sessionPlayer->requestStop();
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
    stopSessionRecord();
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
    if (!hasClip) { addSessionMidiClip(trackId, scene, 4.0); t = findTrackAuthoring(trackId); if (!t) return; }

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
    recording_.store(false, std::memory_order_relaxed);
    recordSlotPlayer_.store(nullptr, std::memory_order_release);
    recordSessionScene_ = -1;
    recordSessionTrackId_ = 0;
}

int32_t Engine::sessionSlotToArrangement(int32_t trackId, int32_t scene, double startBeat) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return -1;
    if (scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return -1;
    const SessionSlot& s = old->sessionSlots[scene];
    if (!s.hasClip) return -1;
    auto nt = cloneTrack(*old);
    MidiClip c;
    c.startBeat   = startBeat < 0.0 ? 0.0 : startBeat;
    c.lengthBeats = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    c.notes       = s.midi.notes; // slot notes are already relative to clip start
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
    const MidiClip src = nt->midiClips[clipIndex]; // copy before mutating the track
    SessionSlot& s = nt->sessionSlots[scene];
    s.hasClip     = true;
    s.lengthBeats = src.lengthBeats > 0 ? src.lengthBeats : 4.0;
    s.midi        = src;
    s.midi.startBeat = 0.0; // slot content is relative to slot start
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::arrangementAudioClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    const auto sample = old->clips[clipIndex].sample;
    if (!sample) return false;
    const float gain = old->clips[clipIndex].gain;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    SessionSlot& s = nt->sessionSlots[scene];
    s.hasClip = true;
    s.audio = AudioClip{};
    s.audio.sample = sample;   // session audio loops the whole sample over lengthBeats
    s.audio.sourceOffsetFrames = 0.0;
    s.audio.lengthFrames = sample->frames;
    s.audio.gain = gain;
    // Loop length = the sample's real duration in beats at the current tempo (like a file drop).
    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    const double seconds = sample->sourceSampleRate > 0 ? sample->frames / sample->sourceSampleRate : 0.0;
    s.lengthBeats = (spb > 0.0 && sr > 0.0) ? seconds * sr / spb : 4.0;
    if (!(s.lengthBeats > 0.0)) s.lengthBeats = 4.0;
    republishWithTrack(trackId, nt);
    return true;
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

} // namespace nota
