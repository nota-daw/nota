// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — Session view (M5): scenes/slots, launch/stop/record, quantization, session<->arrangement transfer, session notes and audio slots.

#include "Engine.h"
#include "media/AudioFile.h"

#include <algorithm>
#include <map>
#include <cmath>

namespace nota {

namespace {
SceneInfo sceneOr(const Graph& g, int32_t scene); // below, with the scene editing
} // namespace

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

bool Engine::addSessionAudioClip(int32_t trackId, int32_t scene, const std::string& path, double lengthBeats,
    double sourceOffsetFrames, int64_t lengthFrames, float gain) {
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
int32_t Engine::sceneCount() const {
    return authoring_->sceneCount;
}

bool Engine::clearSessionSlot(int32_t trackId, int32_t scene) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[scene].hasClip) return false;
    // Stop it first if this slot is the one playing/queued (else it keeps looping empty).
    if (old->sessionPlayer) {
        const int32_t p = old->sessionPlayer->playing.load(std::memory_order_relaxed);
        const int32_t q = old->sessionPlayer->pendingSlot();
        if (p == scene || q == scene) old->sessionPlayer->requestStop();
    }
    auto nt = cloneTrack(*old);
    replaceSlotContent(nt->sessionSlots[scene], SessionSlot{}); // empty the slot; its next-take length stays (#503)
    republishWithTrack(trackId, nt);
    return true;
}

// Rebuild the graph with a changed scene count, cloning every track so its sessionSlots
// vector is resized off the audio thread's shared copy. `mutate(slots)` adjusts each clone's
// slot vector (grow for add, erase for remove).
std::shared_ptr<Graph> Engine::rebuildScenes(
    int32_t newCount, const std::function<void(std::vector<SessionSlot>&)>& mutate) {
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    g->sceneCount = newCount;
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
    if (authoring_->sceneCount <= 1) return false; // always keep at least one scene
    if (scene < 0 || scene >= authoring_->sceneCount) return false;
    // Removing a row shifts every later slot up: a take recording into the row ends first,
    // players on the row stop, players below it follow their slot up.
    // A slot that joined the take in that row (specs/086) lands what it has and leaves it.
    // (The slot carrying the take ends it below.)
    for (size_t k = 0; k < midiLanes_.size(); ++k)
        if (midiLanes_[k].trackId >= 0 && midiLanes_[k].scene == scene && midiLanes_[k].trackId != recordSessionTrackId_)
            finishSessionMidiLane(static_cast<int32_t>(k));
    if (recordSessionAudio_ && audioRecording_) { // the same for an audio slot recording along
        std::vector<int32_t> along;
        for (const auto& l : captureLanes_)
            if (l.scene == scene && l.trackId != recordSessionTrackId_) along.push_back(l.trackId);
        for (int32_t tid : along) finishCaptureLane(tid);
    }
    if (recordSessionScene_ == scene) finalizeSessionRecord(false);
    auto g = rebuildScenes(authoring_->sceneCount - 1, [scene](std::vector<SessionSlot>& slots) {
        if (scene < static_cast<int32_t>(slots.size())) slots.erase(slots.begin() + scene);
    });
    if (scene < static_cast<int32_t>(g->scenes.size())) g->scenes.erase(g->scenes.begin() + scene);
    publish(std::move(g));
    remapPlayers([scene](int32_t x) { return x == scene ? -1 : (x > scene ? x - 1 : x); });
    return true;
}

void Engine::setLaunchQuant(double beats) {
    launchQuant_.store(beats < 0 ? 0 : beats, std::memory_order_relaxed);
}

// Instrument slots loop their MIDI clip; audio slots loop their captured/dropped take
// (renderSessionAudioSlotRaw). Both are launchable; other track types have no slots.
static inline bool sessionLaunchable(const Track& t) {
    return t.type() == TrackType::Instrument || t.type() == TrackType::Audio;
}

void Engine::launchSlot(int32_t trackId, int32_t scene) {
    launchSlotVel(trackId, scene, 1.0f);
}

void Engine::launchSlotVel(int32_t trackId, int32_t scene, float velocity) {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer) return;
    launchSlotImpl(*t, scene, velocity, false);
    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!transport_.uiIsPlaying()) {
        arrangementActive_.store(false, std::memory_order_relaxed);
        playClock();
    }
}

// One track's launch request. An empty slot stops the track — unless the request comes from
// a scene and the slot's stop button was removed. A clip launches with its own quantum,
// legato and launch-velocity gain; Toggle stops a playing slot and Repeat keeps retriggering
// until releaseSlot (direct launches only — a scene always just starts its clips).
void Engine::launchSlotImpl(Track& t, int32_t scene, float velocity, bool fromScene, int32_t gen) {
    auto& sp = *t.sessionPlayer;
    const bool inRange = scene >= 0 && scene < static_cast<int32_t>(t.sessionSlots.size());
    const bool hasClip = inRange && t.sessionSlots[scene].hasClip;
    if (!hasClip || !sessionLaunchable(t)) {
        if (fromScene && inRange && !t.sessionSlots[scene].stopButton) return;
        sp.requestStop(gen);
        return;
    }
    const SessionSlot& s = t.sessionSlots[scene];
    if (!fromScene && !forceSlotLaunch_ && s.launchMode == LaunchMode::Toggle &&
        sp.playing.load(std::memory_order_relaxed) == scene &&
        sp.pendingSlot() == SessionPlayer::kNone) {
        sp.requestStop();
        return;
    }
    const float v = std::clamp(velocity, 0.0f, 1.0f);
    const float gain = 1.0f - std::clamp(s.velocityAmount, 0.0f, 1.0f) * (1.0f - v);
    sp.repeatSlot.store(!fromScene && s.launchMode == LaunchMode::Repeat ? scene : -1, std::memory_order_relaxed);
    sp.requestLaunch(scene, s.quantBeats, s.legato, gain, gen);
}

void Engine::releaseSlot(int32_t trackId, int32_t scene) {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return;
    const SessionSlot& s = t->sessionSlots[scene];
    if (!s.hasClip || (s.launchMode != LaunchMode::Gate && s.launchMode != LaunchMode::Repeat)) return;
    auto& sp = *t->sessionPlayer;
    if (sp.playing.load(std::memory_order_relaxed) == scene || sp.pendingSlot() == scene)
        sp.requestStop();
}

void Engine::launchScene(int32_t scene, bool fromFollow) {
    // Launch the whole row at once: every track's slot in this scene fires on the
    // next quantize boundary; tracks with an empty slot here are stopped (scene-launch
    // semantics) unless that slot's stop button was removed.
    // #480: settle the launch before this one first (did it start?), so the follow saved below is
    // the one really running. A launch still waiting is replaced: the follow from before both stays.
    const bool replacing = sceneLaunchWaiting();
    if (!replacing) {
        prevFollowScene_ = sceneFollowScene_;
        prevFollowStart_ = sceneFollowStart_;
        waitFromFollow_ = fromFollow;
    } else if (!fromFollow) {
        waitFromFollow_ = false; // the performer chose this one; the follow before it did not
    }
    // This launch's generation travels with every request it queues, so it can be withdrawn whole
    // and its follow / tempo wait until one of them really starts.
    if (++sceneLaunchGen_ <= 0 || sceneLaunchGen_ > static_cast<int32_t>(SessionPlayer::kGenMask)) sceneLaunchGen_ = 1;
    const int32_t gen = sceneLaunchGen_;
    bool queued = false;
    for (auto& t : authoring_->tracks)
        if (auto& sp = t->sessionPlayer) {
            launchSlotImpl(*t, scene, 1.0f, true, gen);
            queued |= SessionPlayer::genOf(sp->request.load()) == gen;
        }
    waitSceneGen_ = queued ? gen : 0; // nothing queued (an empty row): nothing to wait for or cancel
    waitScene_ = queued ? scene : -1;

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
            sceneTempo_ = si.tempo;
            sceneSigNum_ = si.sigNum;
            sceneSigDen_ = si.sigDen;
            sceneTempoAt_ = sceneFollowStart_;
            sceneTempoPending_ = true;
            if (!transport_.uiIsPlaying()) applySceneTempo();
        }
    }

    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!transport_.uiIsPlaying()) {
        arrangementActive_.store(false, std::memory_order_relaxed);
        playClock();
    }
}

void Engine::stopSlot(int32_t trackId) {
    auto t = findTrackAuthoring(trackId);
    if (t && t->sessionPlayer) t->sessionPlayer->requestStop();
}

void Engine::stopScene(int32_t scene) {
    for (auto& t : authoring_->tracks) {
        if (!t->sessionPlayer) continue;
        const int32_t playing = t->sessionPlayer->playing.load(std::memory_order_relaxed);
        const int32_t pending = t->sessionPlayer->pendingSlot();
        if (playing == scene || pending == scene) t->sessionPlayer->requestStop();
    }
}

void Engine::stopAllSession() {
    for (auto& t : authoring_->tracks)
        if (t->sessionPlayer) t->sessionPlayer->requestStop();
    sceneFollowScene_ = -1;
    // A scene still waiting for its boundary never starts now: its tempo change goes too (#480).
    if (waitSceneGen_ != 0) sceneTempoPending_ = false;
    waitSceneGen_ = 0;
    waitScene_ = -1;
    stopSessionRecord();
}

bool Engine::sessionSlotLaunchQueued(int32_t trackId, int32_t scene) const {
    if (scene < 0) return false;
    for (auto& t : authoring_->tracks)
        if (t->sessionPlayer && t->sessionPlayer->pendingSlot() == scene &&
            (t->id() == trackId || isUnderGroup(*authoring_, *t, trackId)))
            return true;
    return false;
}

// A row is queued while a launch of one of its slots waits (a direct one, or a restart of a slot
// already playing), or while its scene launch waits with nothing of it started yet — a row that
// queued only stops counts too. One started track makes the scene launch started (#480).
bool Engine::sessionSceneQueued(int32_t scene) const {
    if (scene < 0) return false;
    bool waiting = false, started = false, rowLaunch = false;
    for (auto& t : authoring_->tracks)
        if (auto& sp = t->sessionPlayer) {
            // A direct launch of one of the row's slots. The rest of a scene launch that has started
            // (a clip with a longer quantum still waiting) belongs to a started scene: not queued.
            const uint64_t r = sp->request.load();
            if (SessionPlayer::slotOf(r) == scene && SessionPlayer::genOf(r) == 0) rowLaunch = true;
            if (waitSceneGen_ != 0 && waitScene_ == scene) {
                const auto v = sp->viewGen(waitSceneGen_);
                started |= v.applied;
                waiting |= v.queued;
            }
        }
    return (waiting && !started) || (rowLaunch && !started);
}

// Aim a cancel at what each matching track has queued now (#480): one command per target, the
// exact request word seen here, pushed as one batch. The live request is never written here, so a
// launch being applied at that moment is never disturbed; the audio thread withdraws a target only
// if it is still that very request. If the batch doesn't fit the queue nothing is sent and the
// cancel reports false.
template <typename Match> bool Engine::aimLaunchCancel(bool wholeScene, Match match) {
    std::vector<Command> batch;
    for (auto& t : authoring_->tracks)
        if (auto& sp = t->sessionPlayer) {
            const uint64_t r = sp->request.load();
            const int32_t p = SessionPlayer::slotOf(r);
            if (p == SessionPlayer::kNone || p == SessionPlayer::kStopNow || !match(*t, r)) continue;
            Command c;
            c.type = CommandType::CancelSessionLaunch;
            c.i0 = static_cast<int32_t>(static_cast<uint32_t>(r));
            c.i1 = static_cast<int32_t>(static_cast<uint32_t>(r >> 32));
            c.d0 = t->id();
            c.d1 = wholeScene ? 1.0 : 0.0; // a whole-scene cancel spares a scene launch that started
            batch.push_back(c);
        }
    return !batch.empty() && commands_.pushBatch(batch.data(), batch.size());
}

bool Engine::cancelSlotLaunch(int32_t trackId, int32_t scene) {
    // trackId may be a group: its slot cancels the launches of every track below it. A clip still
    // waiting in a scene whose other clips have started (its own longer quantum) can be cancelled on
    // its own slot: that is the performer's choice for that one slot.
    if (scene < 0) return false;
    return aimLaunchCancel(false, [&](const Track& t, uint64_t r) {
        return SessionPlayer::slotOf(r) == scene && (t.id() == trackId || isUnderGroup(*authoring_, t, trackId));
    });
}

bool Engine::cancelSceneLaunch(int32_t scene) {
    if (!sessionSceneQueued(scene)) return false;
    // Its waiting scene launch's requests (launches and stops), and direct launches of the row's
    // slots — not what another scene launch queued for this row.
    const int32_t gen = (waitSceneGen_ != 0 && waitScene_ == scene) ? waitSceneGen_ : 0;
    return aimLaunchCancel(true, [&](const Track&, uint64_t r) {
        const int32_t rg = SessionPlayer::genOf(r);
        return (gen > 0 && rg == gen) || (SessionPlayer::slotOf(r) == scene && rg == 0);
    });
}

bool Engine::sceneLaunchWaiting() {
    if (waitSceneGen_ == 0) return false;
    bool applied = false, queued = false, busy = false;
    for (auto& t : authoring_->tracks)
        if (auto& sp = t->sessionPlayer) {
            const auto v = sp->viewGen(waitSceneGen_);
            applied |= v.applied;
            queued |= v.queued;
            busy |= v.busy;
        }
    if (busy || (!applied && queued)) return true; // a request mid-consume: decide on the next poll
    waitSceneGen_ = 0;
    waitScene_ = -1;
    if (applied) return false; // it started: its follow and tempo go ahead as launched
    // Cancelled or replaced before its boundary (#480): nothing of it starts. Drop its tempo change
    // and bring back the scene follow that ran before it — it carries on, and fires if its time came
    // while this launch waited. Only when that follow launched this scene itself does follow end
    // there (it would just choose the same again).
    sceneTempoPending_ = false;
    sceneFollowScene_ = waitFromFollow_ ? -1 : prevFollowScene_;
    sceneFollowStart_ = prevFollowStart_;
    return false;
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
    // From stop, a take counts in first when a count-in is set (#158), as an Arrangement take
    // does; with the transport already rolling it starts on the launch quantum as before.
    const bool countIn =
        !transport_.uiIsPlaying() && uiCountIn_.load(std::memory_order_relaxed) <= 0.0 && countInBars() > 0;
    // A count-in that has run out no longer holds the take back: joining is open again.
    if (sessionCountIn_ && countInLaunchScene_.load(std::memory_order_acquire) < 0 &&
        uiCountIn_.load(std::memory_order_relaxed) <= 0.0) {
        sessionCountIn_ = false;
        sessionCountInFresh_ = false;
    }
    // Another audio slot while a Session audio take runs: it records along, as a lane of that
    // take (specs/086), from now — the take is already rolling and counted in.
    if (t->type() == TrackType::Audio && recordSessionAudio_ && audioRecording_) {
        if (addSessionAudioLane(trackId, scene)) {
            if (auto& sp = t->sessionPlayer) // the take replaces what this track was playing (#337)
                if (sp->playing.load(std::memory_order_relaxed) >= 0 ||
                    sp->pendingSlot() != SessionPlayer::kNone)
                    sp->requestStopNow();
        }
        return;
    }
    // Another track's MIDI slot while a Session MIDI take runs: it joins the take (specs/086).
    if (t->type() == TrackType::Instrument && trackId != recordSessionTrackId_ && addSessionMidiLane(trackId, scene))
        return;
    // While a Session MIDI take counts in, another track's slot can't join it yet (its launch
    // would start the transport early) and mustn't take the take over: the press waits.
    if (sessionCountIn_ && recording_.load(std::memory_order_relaxed) && !recordSessionAudio_ &&
        trackId != recordSessionTrackId_)
        return;
    // A slot joining the take above keeps its count-in; a new take starts without one.
    sessionCountIn_ = false;
    sessionCountInFresh_ = false;
    // Remember the slot as it was, so the take can be discarded and retried (specs/078).
    openTakeAttempt(true);
    noteTakeTrack(trackId);
    takeAttempt_.sessionTrackId = trackId;
    takeAttempt_.sessionScene = scene;
    takeAttempt_.sessionSlots.assign(1, {trackId, scene});

    // Audio track: capture input into the slot (M5-4). Reuses the M4-3 input
    // pipeline; stopSessionRecord() materialises the take into slot.audio.
    if (t->type() == TrackType::Audio) {
        publishMidiLanes({}); // the take records no MIDI (specs/086)
        recordSessionTrackId_ = trackId;
        recordSessionScene_ = scene;
        recordSessionAudio_ = true;
        // The take's length (#503): an empty slot's own next-take length; a take over a clip
        // follows the Session record length as before.
        {
            const SessionSlot* sl =
                scene < static_cast<int32_t>(t->sessionSlots.size()) ? &t->sessionSlots[scene] : nullptr;
            recordSessionLen_ = resolveTakeBeats(sl && !sl->hasClip ? sl : nullptr);
            recordSessionOwnLen_ = ownTakeLength(sl);
        }
        recordCountIn_ = countIn; // startAudioRecording leaves rolling the transport to us
        const bool ok = startAudioRecording(trackId);
        recordCountIn_ = false;
        if (ok) {
            recordStartStatus_ = 0;
            recording_.store(true, std::memory_order_relaxed); // drives slot state = recording
            // A track plays one slot at a time: the take replaces whatever this track was
            // playing (or about to launch), from the moment capture starts (#337). Idle
            // players are left alone (a stray stop request would flush their MIDI state).
            if (auto& sp = t->sessionPlayer)
                if (sp->playing.load(std::memory_order_relaxed) >= 0 ||
                    sp->pendingSlot() != SessionPlayer::kNone)
                    sp->requestStopNow();
            if (countIn) {
                sessionCountIn_ = true;
                transportPlayCountIn();
            } // the capture tap stays gated meanwhile
        } else {
            recordStartStatus_ = 2;
            recordSessionAudio_ = false;
            recordSessionLen_ = 0.0;
            recordSessionScene_ = -1;
            recordSessionTrackId_ = 0;
            closeTakeAttempt();
        }
        return;
    }

    if (t->type() != TrackType::Instrument) {
        closeTakeAttempt();
        return;
    }
    // Make sure the slot holds a clip we can overdub into.
    const bool hasClip = scene < static_cast<int32_t>(t->sessionSlots.size()) && t->sessionSlots[scene].hasClip;
    // An empty slot records a fresh take: with a fixed length — the slot's own, or the Session
    // record length when the slot follows it (#503) — it is that long; otherwise it runs until
    // stopped and is cut to whole quanta then (finalizeSessionRecord). An overdub keeps the clip
    // and stops after the Session record length, as before.
    recordSessionGrowing_ = false;
    recordSessionLen_ = sessionRecordLen_;
    recordSessionOwnLen_ = false;
    if (!hasClip) {
        const SessionSlot* sl =
            scene < static_cast<int32_t>(t->sessionSlots.size()) ? &t->sessionSlots[scene] : nullptr;
        recordSessionLen_ = resolveTakeBeats(sl);
        recordSessionOwnLen_ = ownTakeLength(sl);
        recordSessionGrowing_ = recordSessionLen_ <= 0.0;
        {
            TakePushScope takePush(*this); // the take's own clip (specs/078)
            addSessionMidiClip(trackId, scene, recordSessionGrowing_ ? kGrowingTakeBeats : recordSessionLen_);
        }
        t = findTrackAuthoring(trackId);
        if (!t) return;
        sessionCountInFresh_ = true;
        sessionCountInUndo_ = undoPushCount_;
    }

    publishMidiLanes({trackId}); // its MIDI input filter decides what reaches the slot (specs/086)
    midiLanes_[0].scene = scene;
    midiLanes_[0].growing = recordSessionGrowing_;
    midiLanes_[0].lenBeats = recordSessionLen_;
    midiLanes_[0].ownLen = recordSessionOwnLen_;
    midiLanes_[0].player = t->sessionPlayer;
    publishSessionLaneSlots();
    takeAttempt_.sessionSlots.assign(1, {trackId, scene});
    resetCtlFold();
    recordSessionTrackId_ = trackId;
    recordSessionScene_ = scene;
    recordSlotLen_.store(t->sessionSlots[scene].lengthBeats, std::memory_order_relaxed);
    recordSlotHist_.publish(t->sessionPlayer); // the previous take's player waits out two renders
    recordSlotPlayer_.store(t->sessionPlayer.get(), std::memory_order_release);
    if (countIn && t->sessionPlayer) {
        // The slot waits for the count-in: the audio thread launches it the moment the
        // clicks run out (Engine_Render). Session-only, like any launch from stop.
        sessionCountIn_ = true;
        if (t->sessionPlayer->pendingSlot() != SessionPlayer::kNone)
            t->sessionPlayer->requestStopNow(); // no earlier request may start it before then
        countInLaunchScene_.store(scene, std::memory_order_release);
        arrangementActive_.store(false, std::memory_order_relaxed);
        startCountIn(countInBars());
    } else {
        launchSlot(trackId, scene); // play + loop the slot (also auto-starts transport)
    }
    recording_.store(true, std::memory_order_relaxed);
}

void Engine::stopSessionRecord() {
    finalizeSessionRecord(true);
}

void Engine::stopSessionRecordSlot(int32_t trackId) {
    // Several slots record (specs/086): this one lands and loops back, the rest keep going.
    if (recordSessionAudio_ && audioRecording_ && captureLanes_.size() > 1) {
        if (const CaptureLane* l = captureLaneOf(trackId)) {
            const int32_t scene = l->scene;
            finishCaptureLane(trackId);
            if (takeAttempt_.active) keepTakeTrack(trackId); // landed: a retry leaves it be
            if (scene >= 0) launchSlot(trackId, scene);
            return;
        }
    }
    // Several MIDI slots record: this one lands and plays on, the rest keep going.
    if (recording_.load(std::memory_order_relaxed) && !recordSessionAudio_ && !sessionCountIn_ &&
        openMidiLaneCount() > 1)
        if (const int32_t k = midiLaneOf(trackId); k >= 0 && midiLanes_[static_cast<size_t>(k)].scene >= 0) {
            finishSessionMidiLane(k);
            return;
        }
    finalizeSessionRecord(true);
}

bool Engine::sessionSlotRecording(int32_t trackId, int32_t scene) const {
    if (recording_.load(std::memory_order_relaxed) && !recordSessionAudio_) { // a MIDI slot of the take (specs/086)
        const int32_t k = midiLaneOf(trackId);
        return k >= 0 && midiLanes_[static_cast<size_t>(k)].scene == scene;
    }
    if (!audioRecording_ || !recordSessionAudio_) return false;
    const CaptureLane* l = captureLaneOf(trackId);
    return l && l->scene == scene;
}

// End an in-progress session recording. relaunch=true loops the fresh take back (the UI's
// "stop recording"); relaunch=false just materialises it and leaves it stopped (a global stop,
// so the take isn't lost). No-op when nothing is recording into a session slot.
void Engine::finalizeSessionRecord(bool relaunch) {
    if (recordSessionScene_ < 0 && !recordSessionAudio_) return; // not a session record
    // Stopped while still counting in (#158): nothing was recorded, so put things back.
    if (sessionCountIn_) {
        sessionCountIn_ = false;
        const bool pendingSlot = countInLaunchScene_.exchange(-1, std::memory_order_acq_rel) >= 0;
        if (pendingSlot || (recordSessionAudio_ && uiCountIn_.load(std::memory_order_relaxed) > 0.0)) {
            cancelSessionCountIn();
            return;
        }
    }
    // Stopped before it started (#503): no slot of the MIDI take was ever seen recording — it
    // still waits for its launch boundary, or that launch was cancelled (#480) — so drop the
    // take: its clip and its history steps, the slots back as they were (a slot's next-take
    // length included), and any launch of it still queued. A slot that started and was stopped
    // since (Back to Arrangement) has started: its take is kept.
    if (!recordSessionAudio_ && recordSessionScene_ >= 0 && takeAttempt_.active && takeAttempt_.session) {
        std::vector<std::pair<int32_t, int32_t>> queued; // (track, scene) of each slot's launch
        bool waiting = true;
        for (const auto& l : midiLanes_) {
            if (l.trackId < 0 || l.scene < 0 || !l.player) continue;
            if (l.started || l.player->playing.load(std::memory_order_relaxed) == l.scene) waiting = false;
            queued.emplace_back(l.trackId, l.scene);
        }
        if (waiting && !queued.empty()) {
            discardTake();
            // Withdraw only the take's own launches still queued (#480): a stop queued since
            // stays, and what the track plays carries on.
            for (auto [tid, sc] : queued) cancelSlotLaunch(tid, sc);
            sessionCountInFresh_ = false;
            return;
        }
    }
    // Audio take: materialise into the slot (reads recordSession* + clears the audio flags).
    if (recordSessionAudio_) {
        std::vector<std::pair<int32_t, int32_t>> slots; // every slot the take records (specs/086)
        for (const auto& l : captureLanes_)
            if (l.scene >= 0) slots.emplace_back(l.trackId, l.scene);
        if (slots.empty() && recordSessionTrackId_ > 0 && recordSessionScene_ >= 0)
            slots.emplace_back(recordSessionTrackId_, recordSessionScene_);
        stopAudioRecording();
        if (relaunch)
            for (auto [tid, sc] : slots) launchSlot(tid, sc);
    }
    // The take's last controller points (held back for thinning) go in before the target does.
    if (recordSessionScene_ >= 0 && !recordSessionAudio_) flushCtlTake();
    // A fresh MIDI take that ran open-ended: cut to whole launch quanta, every slot of the take.
    if (recordSessionScene_ >= 0 && !recordSessionAudio_)
        for (auto& l : midiLanes_)
            if (l.scene >= 0) cutGrowingLane(l);
    recordSessionGrowing_ = false;
    recordSessionLen_ = 0.0;
    recording_.store(false, std::memory_order_relaxed);
    recordSlotPlayer_.store(nullptr, std::memory_order_release);
    recordSessionScene_ = -1;
    recordSessionTrackId_ = 0;
    recordSessionAudio_ = false; // also when the take captured nothing (stopAudioRecording left it)
    closeTakeAttempt();          // the take is kept
}

// A Session take stopped during its count-in: stop the clicks (the transport never rolled),
// drop the capture, and take a fresh take's empty clip back out when nothing was edited since
// it went in, so the slot is as it was.
void Engine::cancelSessionCountIn() {
    const int32_t tid = recordSessionTrackId_, sc = recordSessionScene_;
    if (recordSessionAudio_) stopAudioRecording(); // nothing captured: the tap was gated
    resetCtlFold();
    recording_.store(false, std::memory_order_relaxed);
    recordSlotPlayer_.store(nullptr, std::memory_order_release);
    recordSessionScene_ = -1;
    recordSessionTrackId_ = 0;
    recordSessionAudio_ = false;
    recordSessionGrowing_ = false;
    recordSessionLen_ = 0.0;
    closeTakeAttempt();
    arrangementActive_.store(false, std::memory_order_relaxed);
    uiCountIn_.store(0.0, std::memory_order_relaxed);
    Command c;
    c.type = CommandType::TransportStop;
    commands_.push(c); // also reopens the capture gate
    if (sessionCountInFresh_ && undoPushCount_ == sessionCountInUndo_ && !undoStack_.empty() &&
        undoStack_.back().graph) {
        // The last checkpoint is the empty clip going in: drop it, and give the slot back
        // what it held before (only the slot — anything else published since stays).
        auto before = std::move(undoStack_.back().graph);
        undoStack_.pop_back();
        const SessionSlot* was = nullptr;
        for (auto& bt : before->tracks)
            if (bt->id() == tid && sc >= 0 && sc < static_cast<int32_t>(bt->sessionSlots.size()))
                was = &bt->sessionSlots[sc];
        if (auto old = findTrackAuthoring(tid); old && sc >= 0 && sc < static_cast<int32_t>(old->sessionSlots.size())) {
            auto nt = cloneTrack(*old);
            nt->sessionSlots[sc] = was ? *was : SessionSlot{};
            republishWithTrackRaw(tid, nt);
        }
    }
    sessionCountInFresh_ = false;
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
    MidiClip c = s.midi; // notes + clip envelopes, already relative to clip start
    c.name = s.name;
    c.startBeat = startBeat < 0.0 ? 0.0 : startBeat;
    c.lengthBeats = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    c.active = true;
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
    replaceSlotContent(nt->sessionSlots[scene], slotFromMidiClip(old->midiClips[clipIndex]));
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
    replaceSlotContent(nt->sessionSlots[scene], slotFromAudioClip(old->clips[clipIndex]));
    republishWithTrack(trackId, nt);
    return true;
}

// A fresh slot looping one arrangement MIDI clip: its notes and clip envelopes, its name,
// and its length as the loop.
SessionSlot Engine::slotFromMidiClip(const MidiClip& m) const {
    SessionSlot s;
    s.hasClip = true;
    s.midi = m;
    s.midi.startBeat = 0.0; // slot content is relative to slot start
    s.midi.active = true;   // a slot has no deactivate; it would just stay silent
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
    if (warpNeedsBuild(s.audio, false)) // a stale cache, or a copied clip narrowed to the slot
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
    if (const int32_t k = midiLaneOf(trackId); k >= 0 && midiLanes_[static_cast<size_t>(k)].scene == scene)
        publishSessionLaneSlots(); // a joined slot's clock too (specs/086)
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
    if (sessionSlotRecording(trackId, scene) && !(recordSessionTrackId_ == trackId && recordSessionScene_ == scene)) {
        // Another slot of the Session take (specs/086); a MIDI one waits for its launch first.
        if (!recordSessionAudio_ && t->sessionPlayer && t->sessionPlayer->playing.load(std::memory_order_relaxed) != scene)
            return 5;
        return 4;
    }
    if (recording_.load(std::memory_order_relaxed) && recordSessionTrackId_ == trackId && recordSessionScene_ == scene) {
        // Not capturing yet (specs/078): a retried take before the boundary it records from, or a
        // MIDI take whose slot waits for a launch quantum (unquantized, it starts next block).
        if (!std::isnan(retryStartBeat_) && transport_.uiPositionBeats() < retryStartBeat_) return 5;
        if (auto* sp = recordSlotPlayer_.load(std::memory_order_acquire);
            !recordSessionAudio_ && launchQuant() > 0.0 && sp && sp->playing.load(std::memory_order_relaxed) != scene &&
                   sp->pendingSlot() == scene) {
            return 5;
        }
        return 4; // recording
    }
    if (auto& sp = t->sessionPlayer) {
        if (sp->playing.load(std::memory_order_relaxed) == scene) return 3; // playing
        if (sp->pendingSlot() == scene) return 2; // queued
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
    for (int32_t i = 0; i < count; ++i) { clip.notes.push_back(noteFromData(notes[i])); }
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::getSessionNotes(int32_t trackId, int32_t scene, NotaNoteData* out, int32_t maxNotes) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    const MidiClip& clip = t->sessionSlots[scene].midi;
    const int32_t n = std::min<int32_t>(maxNotes, static_cast<int32_t>(clip.notes.size()));
    for (int32_t i = 0; i < n; ++i) noteToData(clip.notes[i], out[i]);
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
    out->follow_bypass = s->followBypass ? 1 : 0;
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
    s.followBypass = p.follow_bypass != 0;
    republishWithTrack(trackId, nt);
    // A slot leaving Repeat mode must not keep retriggering.
    if (s.launchMode != LaunchMode::Repeat && old->sessionPlayer &&
        old->sessionPlayer->repeatSlot.load(std::memory_order_relaxed) == scene)
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

// --- Next-take length per slot (#503, specs/106) -----------------------------------------------
// The value belongs to the slot (track × scene) and lives in the graph, so Undo/Redo, scene
// edits, a cancelled count-in and a retried take carry it like any slot property. It is kept,
// hidden, while the slot holds a clip: a deleted clip or an undone take shows it again.

bool Engine::sessionSlotTakeLength(int32_t trackId, int32_t scene, NextTakeLength& out) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= authoring_->sceneCount) return false;
    const SessionSlot* s = slotAt(*t, scene);
    out = s ? s->nextTake : NextTakeLength{};
    return true;
}

bool Engine::setSessionSlotTakeLength(int32_t trackId, int32_t scene, const NextTakeLength& v) {
    auto old = findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= authoring_->sceneCount) return false;
    NextTakeLength n{v.mode, 0, 0};
    switch (v.mode) {
    case SlotTakeMode::Global:
    case SlotTakeMode::Open: break;
    case SlotTakeMode::Fixed:
        // 1 beat .. 64 bars. Beats are checked against the largest meter, not the current one:
        // a meter change reaches the transport only at the next render, and a project loads its
        // slots before that (the UI offers beats of the meter it shows).
        if (v.bars < 0 || v.bars > kMaxTakeBars || v.beats < 0 || v.beats >= kMaxMeterBeats) return false;
        if ((v.bars == 0 && v.beats == 0) || (v.bars == kMaxTakeBars && v.beats > 0)) return false;
        n.bars = v.bars;
        n.beats = v.beats;
        break;
    default: return false;
    }
    NextTakeLength cur;
    sessionSlotTakeLength(trackId, scene, cur);
    if (cur == n) return true;
    auto nt = cloneTrack(*old);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    nt->sessionSlots[scene].nextTake = n;
    republishWithTrack(trackId, nt); // one undo step, like the Stop button
    return true;
}

double Engine::resolveTakeBeats(const SessionSlot* s) const {
    if (!s || s->nextTake.mode == SlotTakeMode::Global) return sessionRecordLen_;
    if (s->nextTake.mode == SlotTakeMode::Open) return 0.0;
    // Bars of the meter in effect now — a meter change since preparing keeps the bar count.
    return static_cast<double>(s->nextTake.bars) * std::max(1, transport_.beatsPerBar()) + s->nextTake.beats;
}

double Engine::sessionSlotTakeBeats(int32_t trackId, int32_t scene) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= authoring_->sceneCount) return 0.0;
    return resolveTakeBeats(slotAt(*t, scene));
}

void Engine::replaceSlotContent(SessionSlot& dst, SessionSlot src) {
    src.nextTake = dst.nextTake;
    dst = std::move(src);
}

bool Engine::copySessionSlot(int32_t srcTrack, int32_t srcScene, int32_t dstTrack, int32_t dstScene) {
    auto src = findTrackAuthoring(srcTrack);
    auto dst = findTrackAuthoring(dstTrack);
    if (!src || !dst || src->type() != dst->type() || !sessionLaunchable(*dst)) return false;
    const SessionSlot* from = slotAt(*src, srcScene);
    if (!from || !from->hasClip || dstScene < 0 || dstScene >= authoring_->sceneCount) return false;
    if (srcTrack == dstTrack && srcScene == dstScene) return true;
    const SessionSlot copy = *from; // before cloning: src may be dst
    auto nt = cloneTrack(*dst);
    if (static_cast<int32_t>(nt->sessionSlots.size()) < authoring_->sceneCount)
        nt->sessionSlots.resize(authoring_->sceneCount);
    replaceSlotContent(nt->sessionSlots[dstScene], copy); // the destination keeps its next-take length (#503)
    republishWithTrack(dstTrack, nt);
    return true;
}

// --- Arrangement clips -> session slots (a selection or the clip clipboard) -------------

int32_t Engine::arrangementClipsToSession(const std::vector<std::pair<int32_t, int32_t>>& sel, int32_t startScene) {
    double len = 0.0;
    return landBlockInSession(captureBlock(sel, len), startScene);
}

int32_t Engine::pasteClipBlockToSession(int32_t destTrackId, int32_t startScene) {
    if (clipboardBlock_.empty() || !authoring_) return -1;
    return landBlockInSession(
        destTrackId >= 0 ? remapBlock(clipboardBlock_, destTrackId, clipboardBlockAnchor_) : clipboardBlock_, startScene);
}

int32_t Engine::sessionSlotsToArrangement(
    const std::vector<std::pair<int32_t, int32_t>>& slots, double atBeat, int32_t destTrackId) {
    lastPlaced_.clear();
    if (!authoring_) return 0;
    std::map<int32_t, std::vector<int32_t>> byTrack; // track -> scenes, in order
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
                b.midi = sl->midi; // notes + clip envelopes, relative to the slot start
                b.midi.name = sl->name;
                b.midi.active = true;
                b.midi.lengthBeats = sl->lengthBeats > 0 ? sl->lengthBeats : 4.0;
                b.len = b.midi.lengthBeats;
            } else {
                if (!sl->audio.sample) continue;
                b.kind = 0; // an audio take lands as one pass of its loop
                b.audio = sl->audio;
                b.audio.name = sl->name;
                b.audio.active = true;
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
        std::stable_sort(
            v.begin(), v.end(), [](const BlockClip* x, const BlockClip* y) { return x->relStart < y->relStart; });
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
        for (int32_t i = 0; i < static_cast<int32_t>(v.size()); ++i) // the slots keep their next-take length (#503)
            replaceSlotContent(nt->sessionSlots[first + i],
                v[i]->kind == 1 ? slotFromMidiClip(v[i]->midi) : slotFromAudioClip(v[i]->audio));
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
    auto g = std::make_shared<Graph>(*authoring_); // tracks shared; only scene metadata changes
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
        if (p >= 0) {
            const int32_t n = map(p);
            sp.playing.compare_exchange_strong(p, n < 0 ? -1 : n, std::memory_order_relaxed);
        }
        uint64_t req = sp.request.load(std::memory_order_relaxed);
        if (const int32_t q = SessionPlayer::slotOf(req); q >= 0) {
            const int32_t n = map(q);
            sp.request.compare_exchange_strong(
                req, n < 0 ? SessionPlayer::kIdle : (req & ~0xFFFFFFFFull) | static_cast<uint32_t>(n), // keep gen + ticket
                std::memory_order_relaxed);
        }
        int32_t r = sp.repeatSlot.load(std::memory_order_relaxed);
        if (r >= 0) sp.repeatSlot.compare_exchange_strong(r, map(r), std::memory_order_relaxed);
    }
    if (recordSessionScene_ >= 0) {
        const int32_t n = map(recordSessionScene_);
        if (n >= 0) recordSessionScene_ = n;
    }
    for (auto& l : captureLanes_) // every slot a Session audio take records (specs/086)
        if (l.scene >= 0)
            if (const int32_t n = map(l.scene); n >= 0) l.scene = n;
    // Every slot a Session MIDI take records follows its row too, or its notes would land in
    // the slot that moved into the old one (specs/086); the audio thread's lane clocks with it.
    bool lanesMoved = false;
    for (auto& l : midiLanes_)
        if (l.scene >= 0)
            if (const int32_t n = map(l.scene); n >= 0 && n != l.scene) {
                l.scene = n;
                lanesMoved = true;
            }
    if (lanesMoved) publishSessionLaneSlots();
    if (takeAttempt_.sessionScene >= 0)
        if (const int32_t n = map(takeAttempt_.sessionScene); n >= 0) takeAttempt_.sessionScene = n;
    for (auto& [tid, sc] : takeAttempt_.sessionSlots) // a retry relaunches the slots where they are now
        if (sc >= 0)
            if (const int32_t n = map(sc); n >= 0) sc = n;
    if (sceneFollowScene_ >= 0) sceneFollowScene_ = map(sceneFollowScene_);
    if (prevFollowScene_ >= 0) prevFollowScene_ = map(prevFollowScene_);
    if (waitScene_ >= 0) waitScene_ = map(waitScene_);
}

// Insert a row at `at`: empty, a copy of row `copyFrom` (old index), or each track's
// currently playing clip (capture). One undo step.
int32_t Engine::insertSceneRow(int32_t at, int32_t copyFrom, bool capturePlaying) {
    const int32_t count = authoring_->sceneCount;
    if (at < 0 || at > count || copyFrom >= count) return -1;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    g->sceneCount = count + 1;
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

int32_t Engine::insertScene(int32_t at) {
    return insertSceneRow(at, -1, false);
}
int32_t Engine::duplicateScene(int32_t scene) {
    if (scene < 0 || scene >= authoring_->sceneCount) return -1;
    return insertSceneRow(scene + 1, scene, false);
}
int32_t Engine::captureScene(int32_t at) {
    return insertSceneRow(at, -1, true);
}

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
    if (!recording_.load(std::memory_order_relaxed) || recordSessionScene_ < 0 || recordSessionTrackId_ <= 0)
        return false;
    double el = 0.0;
    if (recordSessionAudio_) // a retried take counts from the boundary it records from (specs/078)
        el = std::max(0.0, transport_.uiPositionBeats() -
                               (std::isnan(retryStartBeat_) ? audioRecordStartBeat_
                                                            : std::max(audioRecordStartBeat_, retryStartBeat_)));
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
    sceneTempoApplied_ = true; // the UI's transport picks the new values up (takeSceneTempoChange)
    // #971: a scene tempo heard in a pass goes to the Tempo lane when the pass ends — only while
    // no Tempo lane leads (then setBpm below is refused and nothing changed).
    if (sessionPass_.active && sceneTempo_ > 0.0 && !tempoMap())
        sessionPass_.tempoSteps.emplace_back(sceneTempoAt_, sceneTempo_);
    if (sceneTempo_ > 0.0) setBpm(sceneTempo_);
    if (sceneSigNum_ > 0 && sceneSigDen_ > 0) setTimeSignature(sceneSigNum_, sceneSigDen_);
}

void Engine::serviceSessionFollow() {
    // A scene launch still waiting for its boundary holds back its tempo and follow (#480).
    const bool waiting = sceneLaunchWaiting();
    if (!waiting && sceneTempoPending_ && transport_.uiPositionBeats() + 1e-6 >= sceneTempoAt_) applySceneTempo();
    // Fixed-length capture: each slot of the take (several with specs/086) stops when its own
    // length — resolved from its slot when it started (#503) — is recorded. Landing the last one
    // ends the take (stopSessionRecordSlot), so one slot never cuts another short.
    double el = 0.0;
    if (sessionRecordTarget(nullptr, nullptr, &el)) {
        std::vector<int32_t> done;
        if (!recordSessionAudio_) {
            for (auto& l : midiLanes_) // a slot seen recording has started: a stop keeps its take
                if (l.scene >= 0 && l.player && l.player->playing.load(std::memory_order_relaxed) == l.scene)
                    l.started = true;
            for (const auto& l : midiLanes_)
                if (l.trackId >= 0 && l.scene >= 0 && l.lenBeats > 0.0 && l.player &&
                    l.player->playing.load(std::memory_order_relaxed) == l.scene &&
                    l.player->playedBeats.load(std::memory_order_relaxed) >= l.lenBeats - 1e-6)
                    done.push_back(l.trackId);
        } else {
            const double now = transport_.uiPositionBeats();
            for (const auto& l : captureLanes_) {
                if (l.scene < 0 || l.lenBeats <= 0.0) continue;
                // A slot that joined later counts from its own start; the others from the take's.
                const double lane = std::isnan(l.startBeat) ? el : now - l.startBeat;
                if (lane >= l.lenBeats - 1e-6) done.push_back(l.trackId);
            }
        }
        takeLengthReached_ = true; // they land at their length: nothing to cut (#503)
        for (int32_t tid : done) stopSessionRecordSlot(tid);
        takeLengthReached_ = false;
    }
    if (!transport_.uiIsPlaying()) {
        sceneFollowScene_ = -1;
        return;
    }
    const int32_t sc = sceneFollowScene_;
    if (waiting || sc < 0 || sc >= authoring_->sceneCount || !sessionFollow()) return;
    const SceneInfo si = sceneOr(*authoring_, sc);
    if (!si.follow || si.followBeats <= 0.0) return;
    const double q = launchQuant_.load(std::memory_order_relaxed);
    const double lead = q > 0.0 ? std::min(q, si.followBeats) : 0.0;
    const double due = sceneFollowStart_ + si.followBeats;
    if (transport_.uiPositionBeats() + lead + 1e-6 < due) return;
    launchScene((sc + 1) % authoring_->sceneCount, true);
    sceneFollowStart_ = due; // exactly where the outgoing scene's time ran out
}

// Peaks of an audio slot's take (whole sample) for the grid / inspector preview.
int32_t Engine::sessionSlotPeaks(int32_t trackId, int32_t scene, float* outMinMax, int32_t maxPoints) const {
    return getClipPeaks(trackId, sessionClipIndex(scene), outMinMax, maxPoints);
}

// --- Session P0: audio thread ---------------------------------------------------

int32_t Engine::followTarget(const Track& t, int32_t cur, FollowAction a) {
    const int32_t n = static_cast<int32_t>(t.sessionSlots.size());
    auto filled = [&](int32_t i) { return i >= 0 && i < n && t.sessionSlots[i].hasClip; };
    auto rnd = [&]() {
        uint32_t x = followRng_;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        followRng_ = x;
        return x;
    };
    switch (a) {
    case FollowAction::Stop: return -1;
    case FollowAction::Again: return cur;
    case FollowAction::Next:
        for (int32_t k = 1; k <= n; ++k)
            if (filled((cur + k) % n)) return (cur + k) % n;
        return cur;
    case FollowAction::Previous:
        for (int32_t k = 1; k <= n; ++k)
            if (filled(((cur - k) % n + n) % n)) return ((cur - k) % n + n) % n;
        return cur;
    case FollowAction::First:
        for (int32_t i = 0; i < n; ++i)
            if (filled(i)) return i;
        return cur;
    case FollowAction::Last:
        for (int32_t i = n - 1; i >= 0; --i)
            if (filled(i)) return i;
        return cur;
    case FollowAction::Any:
    case FollowAction::Other: {
        int32_t pool[256];
        int32_t m = 0;
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
    const SessionSlot& s = t.sessionSlots[cur];

    // Follow bypass (#499): a bypassed slot holds. When the bypass ends, count on from the last
    // multiple of the follow time already played: the action lands where it would have without
    // the bypass, and a deadline that passed meanwhile never fires as a catch-up. Tracked before
    // the queued-launch guard, so a launch queued (and later cancelled) while the bypass changed
    // still leaves the right base; and before the global switch, so the base is taken when the
    // bypass ends even while follow is off globally.
    const double due = s.followBeats > 0.0 ? s.followBeats : (s.lengthBeats > 0.0 ? s.lengthBeats : 4.0);
    const double played = sp.playedBeats.load(std::memory_order_relaxed);
    if (s.followBypass) {
        sp.followHeld = true;
    } else if (sp.followHeld) {
        sp.followHeld = false;
        sp.followBase = std::floor(played / due) * due;
    }
    if (sp.pendingSlot() != SessionPlayer::kNone) return;

    // Repeat: while held, queue a relaunch of the same slot at every quantum.
    if (sp.repeatSlot.load(std::memory_order_relaxed) == cur) {
        double q = s.quantBeats >= 0.0 ? s.quantBeats : launchQuant_.load(std::memory_order_relaxed);
        if (q <= 0.0) q = 0.25;
        sp.pendingQuant.store(q, std::memory_order_relaxed);
        sp.pendingLegato.store(false, std::memory_order_relaxed);
        sp.pendingGain.store(sp.gain, std::memory_order_relaxed);
        sp.queueIfIdle(cur);
        return;
    }

    // Follow action: once the slot has played its follow time, pick A or B by weight.
    if (s.followBypass) return;
    if (!sessionFollow_.load(std::memory_order_relaxed)) return;
    if (s.followA == FollowAction::None && s.followB == FollowAction::None) return;
    if (played - sp.followBase + blockBeats * 0.5 < due) return;
    const int32_t total = s.chanceA + s.chanceB;
    uint32_t x = followRng_;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    followRng_ = x;
    const FollowAction a =
        (total <= 0 || static_cast<int32_t>(x % 100u) * total < s.chanceA * 100) ? s.followA : s.followB;
    if (a == FollowAction::None) {
        sp.resetFollowCount();
        return;
    }
    const int32_t target = followTarget(t, cur, a);
    if (target < 0) {
        sp.queueIfIdle(SessionPlayer::kStopNow);
        return;
    }
    sp.pendingQuant.store(0.0, std::memory_order_relaxed); // follow time is exact, not quantized
    sp.pendingLegato.store(false, std::memory_order_relaxed);
    sp.pendingGain.store(sp.gain, std::memory_order_relaxed);
    sp.queueIfIdle(target);
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
        for (auto& md : t.midiEffects)
            if (md) md->reset();
    }
}

} // namespace nota
