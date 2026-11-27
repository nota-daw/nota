// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SessionOps — scenes, quantized launch/stop/cancel, and slot MIDI/audio recording triggers.

#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "core/Transport.h"
#include "model/Graph.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/engine/Types.h"
#include "session/SessionHelpers.h"
#include "session/SessionOps.h"
#include "session/SessionPlayer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

using namespace nota::detail::session_helpers;

bool SessionOps::sessionAudioSlotInfo(int32_t trackId, int32_t scene, SessionAudioSlotInfo* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return false;
    const SessionSlot& sl = t->sessionSlots[static_cast<size_t>(scene)];
    if (!sl.hasClip || !sl.audio.sample) return false;
    out->sampleId = sl.audio.sample->id;
    out->lengthBeats = sl.lengthBeats;
    out->sourceOffsetFrames = sl.audio.sourceOffsetFrames;
    out->lengthFrames = sl.audio.lengthFrames;
    out->gain = sl.audio.gain;
    return true;
}

bool SessionOps::addSessionAudioClip(int32_t trackId, int32_t scene, const std::string& path, double lengthBeats,
    double sourceOffsetFrames, int64_t lengthFrames, float gain) {
    if (scene < 0) return false;
    auto sample = decodeAudioFile(path, rt_.decodeOptions());
    if (!sample || sample->empty()) return false;
    if (sample->source) sample->source->primeAround(static_cast<int64_t>(std::max(0.0, sourceOffsetFrames)), false);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) <= scene) slots.resize(static_cast<size_t>(scene) + 1);
    }
    SessionSlot& sl = nt->sessionSlots.write()[static_cast<size_t>(scene)];
    sl.hasClip = true;
    sl.lengthBeats = lengthBeats;
    sl.audio = AudioClip{};
    sl.audio.sample = sample;
    sl.audio.sourceOffsetFrames = sourceOffsetFrames;
    sl.audio.lengthFrames = lengthFrames;
    sl.audio.gain = gain;
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool SessionOps::addSessionAudioFile(int32_t trackId, int32_t scene, const std::string& path) {
    if (scene < 0) return false;
    auto sample = decodeAudioFile(path, rt_.decodeOptions());
    if (!sample || sample->empty()) return false;
    if (sample->source) sample->source->primeAround(0, false);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;

    // Loop length = the file's real duration in beats at the current tempo.
    const double sr = rt_.transport_.sampleRate();
    const double spb = rt_.transport_.samplesPerBeat();
    const double seconds =
        sample->sourceSampleRate > 0 ? static_cast<double>(sample->frames) / sample->sourceSampleRate : 0.0;
    double beats = (spb > 0.0 && sr > 0.0) ? seconds * sr / spb : 4.0;
    if (!(beats > 0.0)) beats = 4.0;

    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) <= scene) slots.resize(static_cast<size_t>(scene) + 1);
    }
    SessionSlot& sl = nt->sessionSlots.write()[static_cast<size_t>(scene)];
    sl.hasClip = true;
    sl.lengthBeats = beats;
    sl.audio = AudioClip{};
    sl.audio.sample = sample;
    sl.audio.sourceOffsetFrames = 0.0;
    sl.audio.lengthFrames = sample->frames;
    sl.audio.gain = 1.0f;
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// --- Session view (M5) ------------------------------------------------------
int32_t SessionOps::sceneCount() const {
    return rt_.authoring_->sceneCount;
}

bool SessionOps::clearSessionSlot(int32_t trackId, int32_t scene) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[static_cast<size_t>(scene)].hasClip) return false;
    // Stop it first if this slot is the one playing/queued (else it keeps looping empty).
    if (old->sessionPlayer) {
        const int32_t p = old->sessionPlayer->playing.load(std::memory_order_relaxed);
        const int32_t q = old->sessionPlayer->pendingSlot();
        if (p == scene || q == scene) old->sessionPlayer->requestStop();
    }
    auto nt = rt_.cloneTrack(*old);
    replaceSlotContent(nt->sessionSlots.write()[static_cast<size_t>(scene)],
        SessionSlot{}); // empty the slot; its next-take length stays (#503)
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// Rebuild the graph with a changed scene count, cloning every track so its sessionSlots
// vector is resized off the audio thread's shared copy. `mutate(slots)` adjusts each clone's
// slot vector (grow for add, erase for remove).
std::shared_ptr<Graph> SessionOps::rebuildScenes(
    int32_t newCount, const std::function<void(std::vector<SessionSlot>&)>& mutate) {
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->sceneCount = newCount;
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        auto nt = rt_.cloneTrack(*t);
        auto& slots = nt->sessionSlots.write();
        mutate(slots);
        if (static_cast<int32_t>(slots.size()) != newCount) slots.resize(static_cast<size_t>(newCount));
        g->tracks.push_back(nt);
    }
    return g;
}

int32_t SessionOps::addScene() {
    const int32_t idx = rt_.authoring_->sceneCount;
    rt_.publish(rebuildScenes(idx + 1, [](std::vector<SessionSlot>&) {}));
    return idx;
}

bool SessionOps::removeScene(int32_t scene) {
    if (rt_.authoring_->sceneCount <= 1) return false; // always keep at least one scene
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    LandingMute mute(*this); // takes landing in a row being deleted fire nothing (#481)
    // Removing a row shifts every later slot up: a take recording into the row ends first,
    // players on the row stop, players below it follow their slot up.
    // A slot that joined the take in that row (specs/086) lands what it has and leaves it.
    // (The slot carrying the take ends it below.)
    for (size_t k = 0; k < rt_.midiLanes_.size(); ++k)
        if (rt_.midiLanes_[k].trackId >= 0 && rt_.midiLanes_[k].scene == scene &&
            rt_.midiLanes_[k].trackId != rt_.recordSessionTrackId_)
            rt_.recorder().finishSessionMidiLane(static_cast<int32_t>(k));
    if (rt_.recordSessionAudio_ && rt_.audioRecording_) { // the same for an audio slot recording along
        std::vector<int32_t> along;
        for (const auto& l : rt_.captureLanes_)
            if (l.scene == scene && l.trackId != rt_.recordSessionTrackId_) along.push_back(l.trackId);
        for (int32_t tid : along) rt_.recorder().finishCaptureLane(tid);
    }
    if (rt_.recordSessionScene_ == scene) finalizeSessionRecord(false);
    auto g = rebuildScenes(rt_.authoring_->sceneCount - 1, [scene](std::vector<SessionSlot>& slots) {
        if (scene < static_cast<int32_t>(slots.size())) slots.erase(slots.begin() + scene);
    });
    if (scene < static_cast<int32_t>(g->scenes.size())) g->scenes.erase(g->scenes.begin() + scene);
    remapLandingScenes(*g, [scene](int32_t x) { return x == scene ? -1 : (x > scene ? x - 1 : x); });
    rt_.publish(std::move(g));
    remapPlayers([scene](int32_t x) { return x == scene ? -1 : (x > scene ? x - 1 : x); });
    return true;
}

void SessionOps::setLaunchQuant(double beats) {
    rt_.launchQuant_.store(beats < 0 ? 0 : beats, std::memory_order_relaxed);
}

void SessionOps::launchSlot(int32_t trackId, int32_t scene) {
    launchSlotVel(trackId, scene, 1.0f);
}

void SessionOps::launchSlotVel(int32_t trackId, int32_t scene, float velocity) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer) return;
    launchSlotImpl(*t, scene, velocity, false);
    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!rt_.transport_.uiIsPlaying()) {
        rt_.arrangementActive_.store(false, std::memory_order_relaxed);
        rt_.playClock();
    }
}

// One track's launch request. An empty slot stops the track — unless the request comes from
// a scene and the slot's stop button was removed. A clip launches with its own quantum,
// legato and launch-velocity gain; Toggle stops a playing slot and Repeat keeps retriggering
// until releaseSlot (direct launches only — a scene always just starts its clips).
void SessionOps::launchSlotImpl(
    Track& t, int32_t scene, float velocity, bool fromScene, int32_t gen, double quantOverride) {
    auto& sp = *t.sessionPlayer;
    const bool inRange = scene >= 0 && scene < static_cast<int32_t>(t.sessionSlots.size());
    const bool hasClip = inRange && t.sessionSlots[static_cast<size_t>(scene)].hasClip;
    if (!hasClip || !sessionLaunchable(t)) {
        if (fromScene && inRange && !t.sessionSlots[static_cast<size_t>(scene)].stopButton) return;
        sp.requestStop(gen, quantOverride);
        return;
    }
    const SessionSlot& s = t.sessionSlots[static_cast<size_t>(scene)];
    if (!fromScene && !rt_.forceSlotLaunch_ && s.launchMode == LaunchMode::Toggle &&
        sp.playing.load(std::memory_order_relaxed) == scene && sp.pendingSlot() == SessionPlayer::kNone) {
        sp.requestStop();
        return;
    }
    const float v = std::clamp(velocity, 0.0f, 1.0f);
    const float gain = 1.0f - std::clamp(s.velocityAmount, 0.0f, 1.0f) * (1.0f - v);
    sp.repeatSlot.store(!fromScene && s.launchMode == LaunchMode::Repeat ? scene : -1, std::memory_order_relaxed);
    sp.requestLaunch(scene, quantOverride >= 0.0 ? quantOverride : s.quantBeats, s.legato, gain, gen);
}

void SessionOps::releaseSlot(int32_t trackId, int32_t scene) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return;
    const SessionSlot& s = t->sessionSlots[static_cast<size_t>(scene)];
    if (!s.hasClip || (s.launchMode != LaunchMode::Gate && s.launchMode != LaunchMode::Repeat)) return;
    auto& sp = *t->sessionPlayer;
    if (sp.playing.load(std::memory_order_relaxed) == scene || sp.pendingSlot() == scene) sp.requestStop();
}

void SessionOps::launchScene(int32_t scene, bool fromFollow, double quantOverride) {
    // Launch the whole row at once: every track's slot in this scene fires on the
    // next quantize boundary; tracks with an empty slot here are stopped (scene-launch
    // semantics) unless that slot's stop button was removed.
    // #480: settle the launch before this one first (did it start?), so the follow saved below is
    // the one really running. A launch still waiting is replaced: the follow from before both stays.
    const bool replacing = sceneLaunchWaiting();
    if (!replacing) {
        prevFollowScene_ = rt_.sceneFollowScene_;
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
    for (auto& t : rt_.authoring_->tracks)
        if (auto& sp = t->sessionPlayer) {
            launchSlotImpl(*t, scene, 1.0f, true, gen, quantOverride);
            queued |= SessionPlayer::genOf(sp->request.load()) == gen;
        }
    waitSceneGen_ = queued ? gen : 0; // nothing queued (an empty row): nothing to wait for or cancel
    waitScene_ = queued ? scene : -1;

    // Scene follow counts from the boundary the row actually starts on.
    const double q = quantOverride >= 0.0 ? quantOverride : rt_.launchQuant_.load(std::memory_order_relaxed);
    const double pos = rt_.transport_.uiPositionBeats();
    rt_.sceneFollowScene_ = scene;
    sceneFollowStart_ = (q > 0.0 && pos > 1e-9) ? std::ceil(pos / q - 1e-9) * q : pos;

    // Scene tempo / signature apply on that boundary too (at once when the clock is stopped).
    sceneTempoPending_ = false;
    if (scene >= 0 && scene < static_cast<int32_t>(rt_.authoring_->scenes.size())) {
        const SceneInfo& si = rt_.authoring_->scenes[static_cast<size_t>(scene)];
        if (si.tempo > 0.0 || (si.sigNum > 0 && si.sigDen > 0)) {
            sceneTempo_ = si.tempo;
            sceneSigNum_ = si.sigNum;
            sceneSigDen_ = si.sigDen;
            sceneTempoAt_ = sceneFollowStart_;
            sceneTempoPending_ = true;
            if (!rt_.transport_.uiIsPlaying()) applySceneTempo();
        }
    }

    // Start the clock for quantize — but session-only: launching a clip must NOT roll the
    // whole Arrangement. If the Arrangement is already playing, leave it (clips layer over it).
    if (!rt_.transport_.uiIsPlaying()) {
        rt_.arrangementActive_.store(false, std::memory_order_relaxed);
        rt_.playClock();
    }
}

void SessionOps::stopSlot(int32_t trackId) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (t && t->sessionPlayer) t->sessionPlayer->requestStop();
}

void SessionOps::stopScene(int32_t scene) {
    for (auto& t : rt_.authoring_->tracks) {
        if (!t->sessionPlayer) continue;
        const int32_t playing = t->sessionPlayer->playing.load(std::memory_order_relaxed);
        const int32_t pending = t->sessionPlayer->pendingSlot();
        if (playing == scene || pending == scene) t->sessionPlayer->requestStop();
    }
}

void SessionOps::stopAllSession() {
    LandingMute mute(*this); // stopping everything lands takes without firing their rules (#481)
    for (auto& t : rt_.authoring_->tracks)
        if (t->sessionPlayer) t->sessionPlayer->requestStop();
    rt_.sceneFollowScene_ = -1;
    // A scene still waiting for its boundary never starts now: its tempo change goes too (#480).
    if (waitSceneGen_ != 0) sceneTempoPending_ = false;
    waitSceneGen_ = 0;
    waitScene_ = -1;
    stopSessionRecord();
    rt_.landedTakes_.clear(); // takes that landed just before: everything stops, nothing launches (#481)
    ruleWait_ = RuleWait{};
}

bool SessionOps::sessionSlotLaunchQueued(int32_t trackId, int32_t scene) const {
    if (scene < 0) return false;
    for (auto& t : rt_.authoring_->tracks)
        if (t->sessionPlayer && t->sessionPlayer->pendingSlot() == scene &&
            (t->id() == trackId || rt_.isUnderGroup(*rt_.authoring_, *t, trackId)))
            return true;
    return false;
}

// A row is queued while a launch of one of its slots waits (a direct one, or a restart of a slot
// already playing), or while its scene launch waits with nothing of it started yet — a row that
// queued only stops counts too. One started track makes the scene launch started (#480).
bool SessionOps::sessionSceneQueued(int32_t scene) const {
    if (scene < 0) return false;
    bool waiting = false, started = false, rowLaunch = false;
    for (auto& t : rt_.authoring_->tracks)
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
template <typename Match> bool SessionOps::aimLaunchCancel(bool wholeScene, Match match) {
    std::vector<Command> batch;
    for (auto& t : rt_.authoring_->tracks)
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
    return !batch.empty() && rt_.commands_.pushBatch(batch.data(), batch.size());
}

bool SessionOps::cancelSlotLaunch(int32_t trackId, int32_t scene) {
    // trackId may be a group: its slot cancels the launches of every track below it. A clip still
    // waiting in a scene whose other clips have started (its own longer quantum) can be cancelled on
    // its own slot: that is the performer's choice for that one slot.
    if (scene < 0) return false;
    return aimLaunchCancel(false, [&](const Track& t, uint64_t r) {
        return SessionPlayer::slotOf(r) == scene &&
               (t.id() == trackId || rt_.isUnderGroup(*rt_.authoring_, t, trackId));
    });
}

bool SessionOps::cancelSceneLaunch(int32_t scene) {
    if (!sessionSceneQueued(scene)) return false;
    // Its waiting scene launch's requests (launches and stops), and direct launches of the row's
    // slots — not what another scene launch queued for this row.
    const int32_t gen = (waitSceneGen_ != 0 && waitScene_ == scene) ? waitSceneGen_ : 0;
    return aimLaunchCancel(true, [&](const Track&, uint64_t r) {
        const int32_t rg = SessionPlayer::genOf(r);
        return (gen > 0 && rg == gen) || (SessionPlayer::slotOf(r) == scene && rg == 0);
    });
}

bool SessionOps::sceneLaunchWaiting() {
    if (waitSceneGen_ == 0) return false;
    bool applied = false, queued = false, busy = false;
    for (auto& t : rt_.authoring_->tracks)
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
    rt_.sceneFollowScene_ = waitFromFollow_ ? -1 : prevFollowScene_;
    sceneFollowStart_ = prevFollowStart_;
    return false;
}

void SessionOps::trackBackToArrangement(int32_t trackId) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (t && t->sessionPlayer) t->sessionPlayer->requestStopNow();
}

int32_t SessionOps::sessionPlayingSlot(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return (t && t->sessionPlayer) ? t->sessionPlayer->playing.load(std::memory_order_relaxed) : -1;
}

double SessionOps::sessionSlotPosition(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->sessionPlayer) return 0.0;
    const int32_t p = t->sessionPlayer->playing.load(std::memory_order_relaxed);
    if (p < 0 || p >= static_cast<int32_t>(t->sessionSlots.size())) return 0.0;
    const double L = t->sessionSlots[static_cast<size_t>(p)].lengthBeats > 0
                         ? t->sessionSlots[static_cast<size_t>(p)].lengthBeats
                         : 4.0;
    double x = std::fmod(t->sessionPlayer->uiLocalBeats.load(std::memory_order_relaxed), L);
    return x < 0.0 ? x + L : x;
}

void SessionOps::recordSessionSlot(int32_t trackId, int32_t scene) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return;
    // From stop, a take counts in first when a count-in is set (#158), as an Arrangement take
    // does; with the transport already rolling it starts on the launch quantum as before.
    const bool countIn =
        !rt_.transport_.uiIsPlaying() && rt_.uiCountIn_.load(std::memory_order_relaxed) <= 0.0 && rt_.countInBars() > 0;
    // A count-in that has run out no longer holds the take back: joining is open again.
    if (rt_.sessionCountIn_ && rt_.countInLaunchScene_.load(std::memory_order_acquire) < 0 &&
        rt_.uiCountIn_.load(std::memory_order_relaxed) <= 0.0) {
        rt_.sessionCountIn_ = false;
        rt_.sessionCountInFresh_ = false;
    }
    // Another audio slot while a Session audio take runs: it records along, as a lane of that
    // take (specs/086), from now — the take is already rolling and counted in.
    if (t->type() == TrackType::Audio && rt_.recordSessionAudio_ && rt_.audioRecording_) {
        if (rt_.recorder().addSessionAudioLane(trackId, scene)) {
            if (auto& sp = t->sessionPlayer) // the take replaces what this track was playing (#337)
                if (sp->playing.load(std::memory_order_relaxed) >= 0 || sp->pendingSlot() != SessionPlayer::kNone)
                    sp->requestStopNow();
        }
        return;
    }
    // Another track's MIDI slot while a Session MIDI take runs: it joins the take (specs/086).
    if (t->type() == TrackType::Instrument && trackId != rt_.recordSessionTrackId_ &&
        rt_.recorder().addSessionMidiLane(trackId, scene))
        return;
    // While a Session MIDI take counts in, another track's slot can't join it yet (its launch
    // would start the transport early) and mustn't take the take over: the press waits.
    if (rt_.sessionCountIn_ && rt_.recording_.load(std::memory_order_relaxed) && !rt_.recordSessionAudio_ &&
        trackId != rt_.recordSessionTrackId_)
        return;
    // A slot joining the take above keeps its count-in; a new take starts without one.
    rt_.sessionCountIn_ = false;
    rt_.sessionCountInFresh_ = false;
    // Remember the slot as it was, so the take can be discarded and retried (specs/078).
    rt_.recorder().openTakeAttempt(true);
    rt_.recorder().noteTakeTrack(trackId);
    rt_.takeAttempt_.sessionTrackId = trackId;
    rt_.takeAttempt_.sessionScene = scene;
    rt_.takeAttempt_.sessionSlots.assign(1, {trackId, scene});

    // Audio track: capture input into the slot (M5-4). Reuses the M4-3 input
    // pipeline; stopSessionRecord() materialises the take into slot.audio.
    if (t->type() == TrackType::Audio) {
        rt_.recorder().publishMidiLanes({}); // the take records no MIDI (specs/086)
        rt_.recordSessionTrackId_ = trackId;
        rt_.recordSessionScene_ = scene;
        rt_.recordSessionAudio_ = true;
        // The take's length (#503): an empty slot's own next-take length; a take over a clip
        // follows the Session record length as before.
        bool freshSlot = false;
        {
            const SessionSlot* sl = scene < static_cast<int32_t>(t->sessionSlots.size())
                                        ? &t->sessionSlots[static_cast<size_t>(scene)]
                                        : nullptr;
            rt_.recordSessionLen_ = resolveTakeBeats(sl && !sl->hasClip ? sl : nullptr);
            rt_.recordSessionOwnLen_ = ownTakeLength(sl);
            freshSlot = !sl || !sl->hasClip;
        }
        rt_.recordCountIn_ = countIn; // startAudioRecording leaves rolling the transport to us
        const bool ok = rt_.recorder().startAudioRecording(trackId);
        rt_.recordCountIn_ = false;
        if (ok) {
            if (!rt_.captureLanes_.empty()) rt_.captureLanes_[0].fresh = freshSlot; // its landing fires the rule (#481)
            rt_.recordStartStatus_ = 0;
            rt_.recording_.store(true, std::memory_order_relaxed); // drives slot state = recording
            // A track plays one slot at a time: the take replaces whatever this track was
            // playing (or about to launch), from the moment capture starts (#337). Idle
            // players are left alone (a stray stop request would flush their MIDI state).
            if (auto& sp = t->sessionPlayer)
                if (sp->playing.load(std::memory_order_relaxed) >= 0 || sp->pendingSlot() != SessionPlayer::kNone)
                    sp->requestStopNow();
            if (countIn) {
                rt_.sessionCountIn_ = true;
                rt_.transportPlayCountIn();
            } // the capture tap stays gated meanwhile
        } else {
            rt_.recordStartStatus_ = 2;
            rt_.recordSessionAudio_ = false;
            rt_.recordSessionLen_ = 0.0;
            rt_.recordSessionScene_ = -1;
            rt_.recordSessionTrackId_ = 0;
            rt_.recorder().closeTakeAttempt();
        }
        return;
    }

    if (t->type() != TrackType::Instrument) {
        rt_.recorder().closeTakeAttempt();
        return;
    }
    // Make sure the slot holds a clip we can overdub into.
    const bool hasClip =
        scene < static_cast<int32_t>(t->sessionSlots.size()) && t->sessionSlots[static_cast<size_t>(scene)].hasClip;
    // An empty slot records a fresh take: with a fixed length — the slot's own, or the Session
    // record length when the slot follows it (#503) — it is that long; otherwise it runs until
    // stopped and is cut to whole quanta then (finalizeSessionRecord). An overdub keeps the clip
    // and stops after the Session record length, as before.
    rt_.recordSessionGrowing_ = false;
    rt_.recordSessionLen_ = sessionRecordLen_;
    rt_.recordSessionOwnLen_ = false;
    if (!hasClip) {
        const SessionSlot* sl = scene < static_cast<int32_t>(t->sessionSlots.size())
                                    ? &t->sessionSlots[static_cast<size_t>(scene)]
                                    : nullptr;
        rt_.recordSessionLen_ = resolveTakeBeats(sl);
        rt_.recordSessionOwnLen_ = ownTakeLength(sl);
        rt_.recordSessionGrowing_ = rt_.recordSessionLen_ <= 0.0;
        {
            EngineRuntime::TakePushScope takePush(rt_); // the take's own clip (specs/078)
            addSessionMidiClip(trackId, scene, rt_.recordSessionGrowing_ ? kGrowingTakeBeats : rt_.recordSessionLen_);
        }
        t = rt_.findTrackAuthoring(trackId);
        if (!t) return;
        rt_.sessionCountInFresh_ = true;
        rt_.sessionCountInUndo_ = rt_.undoPushCount_;
    }

    rt_.recorder().publishMidiLanes({trackId}); // its MIDI input filter decides what reaches the slot (specs/086)
    rt_.midiLanes_[0].scene = scene;
    rt_.midiLanes_[0].growing = rt_.recordSessionGrowing_;
    rt_.midiLanes_[0].lenBeats = rt_.recordSessionLen_;
    rt_.midiLanes_[0].ownLen = rt_.recordSessionOwnLen_;
    rt_.midiLanes_[0].fresh = !hasClip; // a fresh take: its landing fires the slot's rule (#481)
    rt_.midiLanes_[0].player = t->sessionPlayer;
    rt_.recorder().publishSessionLaneSlots();
    rt_.takeAttempt_.sessionSlots.assign(1, {trackId, scene});
    rt_.recorder().resetCtlFold();
    rt_.recordSessionTrackId_ = trackId;
    rt_.recordSessionScene_ = scene;
    rt_.recordSlotLen_.store(t->sessionSlots[static_cast<size_t>(scene)].lengthBeats, std::memory_order_relaxed);
    rt_.recordSlotHist_.publish(t->sessionPlayer); // the previous take's player waits out two renders
    rt_.recordSlotPlayer_.store(t->sessionPlayer.get(), std::memory_order_release);
    if (countIn && t->sessionPlayer) {
        // The slot waits for the count-in: the audio thread launches it the moment the
        // clicks run out (Engine_Render). Session-only, like any launch from stop.
        rt_.sessionCountIn_ = true;
        if (t->sessionPlayer->pendingSlot() != SessionPlayer::kNone)
            t->sessionPlayer->requestStopNow(); // no earlier request may start it before then
        rt_.countInLaunchScene_.store(scene, std::memory_order_release);
        rt_.arrangementActive_.store(false, std::memory_order_relaxed);
        rt_.startCountIn(rt_.countInBars());
    } else {
        launchSlot(trackId, scene); // play + loop the slot (also auto-starts transport)
    }
    rt_.recording_.store(true, std::memory_order_relaxed);
}

void SessionOps::stopSessionRecord() {
    finalizeSessionRecord(true);
}

void SessionOps::stopSessionRecordSlot(int32_t trackId) {
    // Several slots record (specs/086): this one lands and loops back, the rest keep going.
    if (rt_.recordSessionAudio_ && rt_.audioRecording_ && rt_.captureLanes_.size() > 1) {
        if (const CaptureLane* l = rt_.recorder().captureLaneOf(trackId)) {
            const int32_t scene = l->scene;
            rt_.recorder().finishCaptureLane(trackId);
            if (rt_.takeAttempt_.active) rt_.recorder().keepTakeTrack(trackId); // landed: a retry leaves it be
            if (scene >= 0) launchSlot(trackId, scene);
            return;
        }
    }
    // Several MIDI slots record: this one lands and plays on, the rest keep going.
    if (rt_.recording_.load(std::memory_order_relaxed) && !rt_.recordSessionAudio_ && !rt_.sessionCountIn_ &&
        rt_.recorder().openMidiLaneCount() > 1)
        if (const int32_t k = rt_.recorder().midiLaneOf(trackId);
            k >= 0 && rt_.midiLanes_[static_cast<size_t>(k)].scene >= 0) {
            rt_.recorder().finishSessionMidiLane(k);
            return;
        }
    finalizeSessionRecord(true);
}

bool SessionOps::sessionSlotRecording(int32_t trackId, int32_t scene) const {
    if (rt_.recording_.load(std::memory_order_relaxed) &&
        !rt_.recordSessionAudio_) { // a MIDI slot of the take (specs/086)
        const int32_t k = rt_.recorder().midiLaneOf(trackId);
        return k >= 0 && rt_.midiLanes_[static_cast<size_t>(k)].scene == scene;
    }
    if (!rt_.audioRecording_ || !rt_.recordSessionAudio_) return false;
    const CaptureLane* l = rt_.recorder().captureLaneOf(trackId);
    return l && l->scene == scene;
}

// End an in-progress session recording. relaunch=true loops the fresh take back (the UI's
// "stop recording"); relaunch=false just materialises it and leaves it stopped (a global stop,
// so the take isn't lost). No-op when nothing is recording into a session slot.

} // namespace nota::detail
