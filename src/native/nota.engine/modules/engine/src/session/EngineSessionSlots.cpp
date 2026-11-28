// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SessionOps — session record finalization, clip<->slot transfers, slot length/gain/notes/props, and landing rules.

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

void SessionOps::finalizeSessionRecord(bool relaunch) {
    if (rt_.recordSessionScene_ < 0 && !rt_.recordSessionAudio_) return; // not a session record
    // Stopped while still counting in (#158): nothing was recorded, so put things back.
    if (rt_.sessionCountIn_) {
        rt_.sessionCountIn_ = false;
        const bool pendingSlot = rt_.countInLaunchScene_.exchange(-1, std::memory_order_acq_rel) >= 0;
        if (pendingSlot || (rt_.recordSessionAudio_ && rt_.uiCountIn_.load(std::memory_order_relaxed) > 0.0)) {
            cancelSessionCountIn();
            return;
        }
    }
    // Stopped before it started (#503): no slot of the MIDI take was ever seen recording — it
    // still waits for its launch boundary, or that launch was cancelled (#480) — so drop the
    // take: its clip and its history steps, the slots back as they were (a slot's next-take
    // length included), and any launch of it still queued. A slot that started and was stopped
    // since (Back to Arrangement) has started: its take is kept.
    if (!rt_.recordSessionAudio_ && rt_.recordSessionScene_ >= 0 && rt_.takeAttempt_.active &&
        rt_.takeAttempt_.session) {
        std::vector<std::pair<int32_t, int32_t>> queued; // (track, scene) of each slot's launch
        bool waiting = true;
        for (const auto& l : rt_.midiLanes_) {
            if (l.trackId < 0 || l.scene < 0 || !l.player) continue;
            if (l.started || l.player->playing.load(std::memory_order_relaxed) == l.scene) waiting = false;
            queued.emplace_back(l.trackId, l.scene);
        }
        if (waiting && !queued.empty()) {
            rt_.recorder().discardTake();
            // Withdraw only the take's own launches still queued (#480): a stop queued since
            // stays, and what the track plays carries on.
            for (auto [tid, sc] : queued) cancelSlotLaunch(tid, sc);
            rt_.sessionCountInFresh_ = false;
            return;
        }
    }
    // Audio take: materialise into the slot (reads recordSession* + clears the audio flags).
    if (rt_.recordSessionAudio_) {
        std::vector<std::pair<int32_t, int32_t>> slots; // every slot the take records (specs/086)
        for (const auto& l : rt_.captureLanes_)
            if (l.scene >= 0) slots.emplace_back(l.trackId, l.scene);
        if (slots.empty() && rt_.recordSessionTrackId_ > 0 && rt_.recordSessionScene_ >= 0)
            slots.emplace_back(rt_.recordSessionTrackId_, rt_.recordSessionScene_);
        rt_.recorder().stopAudioRecording();
        if (relaunch)
            for (auto [tid, sc] : slots) launchSlot(tid, sc);
    }
    // Notes still queued go into the take first (a note-off since the last poll()), so a take with
    // notes is never taken for an empty one when it lands (#481). Then the take's last controller
    // points (held back for thinning) go in before the target does.
    if (rt_.recordSessionScene_ >= 0 && !rt_.recordSessionAudio_) rt_.recorder().foldRecordedTake();
    if (rt_.recordSessionScene_ >= 0 && !rt_.recordSessionAudio_) rt_.recorder().flushCtlTake();
    // A fresh MIDI take that ran open-ended: cut to whole launch quanta, every slot of the take.
    if (rt_.recordSessionScene_ >= 0 && !rt_.recordSessionAudio_)
        for (auto& l : rt_.midiLanes_)
            if (l.scene >= 0) {
                rt_.recorder().cutGrowingLane(l);
                noteMidiLaneLanded(l); // kept as a playable clip: its slot's rule may fire (#481)
            }
    rt_.recordSessionGrowing_ = false;
    rt_.recordSessionLen_ = 0.0;
    rt_.recording_.store(false, std::memory_order_relaxed);
    rt_.recordSlotPlayer_.store(nullptr, std::memory_order_release);
    rt_.recordSessionScene_ = -1;
    rt_.recordSessionTrackId_ = 0;
    rt_.recordSessionAudio_ = false;   // also when the take captured nothing (stopAudioRecording left it)
    rt_.recorder().closeTakeAttempt(); // the take is kept
}

// A Session take stopped during its count-in: stop the clicks (the transport never rolled),
// drop the capture, and take a fresh take's empty clip back out when nothing was edited since
// it went in, so the slot is as it was.
void SessionOps::cancelSessionCountIn() {
    const int32_t tid = rt_.recordSessionTrackId_, sc = rt_.recordSessionScene_;
    if (rt_.recordSessionAudio_) rt_.recorder().stopAudioRecording(); // nothing captured: the tap was gated
    rt_.recorder().resetCtlFold();
    rt_.recording_.store(false, std::memory_order_relaxed);
    rt_.recordSlotPlayer_.store(nullptr, std::memory_order_release);
    rt_.recordSessionScene_ = -1;
    rt_.recordSessionTrackId_ = 0;
    rt_.recordSessionAudio_ = false;
    rt_.recordSessionGrowing_ = false;
    rt_.recordSessionLen_ = 0.0;
    rt_.recorder().closeTakeAttempt();
    rt_.arrangementActive_.store(false, std::memory_order_relaxed);
    rt_.uiCountIn_.store(0.0, std::memory_order_relaxed);
    Command c;
    c.type = CommandType::TransportStop;
    rt_.commands_.push(c); // also reopens the capture gate
    if (rt_.sessionCountInFresh_ && rt_.undoPushCount_ == rt_.sessionCountInUndo_ && !rt_.undoStack_.empty() &&
        rt_.undoStack_.back().graph) {
        // The last checkpoint is the empty clip going in: drop it, and give the slot back
        // what it held before (only the slot — anything else published since stays).
        auto before = std::move(rt_.undoStack_.back().graph);
        rt_.undoStack_.pop_back();
        const SessionSlot* was = nullptr;
        for (auto& bt : before->tracks)
            if (bt->id() == tid && sc >= 0 && sc < static_cast<int32_t>(bt->sessionSlots.size()))
                was = &bt->sessionSlots[static_cast<size_t>(sc)];
        if (auto old = rt_.findTrackAuthoring(tid);
            old && sc >= 0 && sc < static_cast<int32_t>(old->sessionSlots.size())) {
            auto nt = rt_.cloneTrack(*old);
            nt->sessionSlots.write()[static_cast<size_t>(sc)] = was ? *was : SessionSlot{};
            rt_.republishWithTrackRaw(tid, nt);
        }
    }
    rt_.sessionCountInFresh_ = false;
}

// A take's loop length: the recorded beats rounded to whole launch quanta (bars when
// launching is unquantized or finer than a beat), never shorter than one quantum.
double SessionOps::roundedTakeBeats(double beats) const {
    double q = rt_.launchQuant_.load(std::memory_order_relaxed);
    if (q < 1.0) q = 4.0;
    return std::max(q, std::round(beats / q) * q);
}

int32_t SessionOps::sessionSlotToArrangement(int32_t trackId, int32_t scene, double startBeat) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || (old->type() != TrackType::Instrument && old->type() != TrackType::Audio)) return -1;
    if (scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return -1;
    const SessionSlot& s = old->sessionSlots[static_cast<size_t>(scene)];
    if (!s.hasClip) return -1;
    auto nt = rt_.cloneTrack(*old);
    if (old->type() == TrackType::Audio) {
        // An audio take lands as a plain clip of the slot's sample (one pass of the loop).
        if (!s.audio.sample) return -1;
        AudioClip c = s.audio;
        c.startBeat = startBeat < 0.0 ? 0.0 : startBeat;
        c.name = s.name;
        nt->clips.write().push_back(c);
        const int32_t idx = static_cast<int32_t>(nt->clips.size()) - 1;
        rt_.republishWithTrack(trackId, nt);
        return idx;
    }
    MidiClip c = s.midi; // notes + clip envelopes, already relative to clip start
    c.name = s.name;
    c.startBeat = startBeat < 0.0 ? 0.0 : startBeat;
    c.lengthBeats = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    c.active = true;
    nt->midiClips.write().push_back(c);
    const int32_t idx = static_cast<int32_t>(nt->midiClips.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return idx;
}

bool SessionOps::arrangementClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        replaceSlotContent(
            slots[static_cast<size_t>(scene)], slotFromMidiClip(old->midiClips[static_cast<size_t>(clipIndex)]));
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool SessionOps::arrangementAudioClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    if (!old->clips[static_cast<size_t>(clipIndex)].sample) return false;
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        replaceSlotContent(
            slots[static_cast<size_t>(scene)], slotFromAudioClip(old->clips[static_cast<size_t>(clipIndex)]));
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// A fresh slot looping one arrangement MIDI clip: its notes and clip envelopes, its name,
// and its length as the loop.
SessionSlot SessionOps::slotFromMidiClip(const MidiClip& m) const {
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
SessionSlot SessionOps::slotFromAudioClip(const AudioClip& a) {
    SessionSlot s;
    s.hasClip = true;
    s.audio = a;
    s.audio.startBeat = 0.0;
    s.audio.active = true;
    if (rt_.clips().warpNeedsBuild(s.audio, false)) // a stale cache, or a copied clip narrowed to the slot
        rt_.clips().configureClipWarp(s.audio, rt_.transport_.samplesPerBeat(), rt_.transport_.sampleRate());
    s.name = a.name;
    const double beats = audioClipBeats(s.audio);
    s.lengthBeats = beats > 0.0 ? beats : 4.0;
    return s;
}

bool SessionOps::addSessionMidiClip(int32_t trackId, int32_t scene, double lengthBeats) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        SessionSlot& s = slots[static_cast<size_t>(scene)];
        s.hasClip = true;
        s.lengthBeats = lengthBeats > 0 ? lengthBeats : 4.0;
        s.midi.lengthBeats = s.lengthBeats;
        s.midi.notes.clear();
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

double SessionOps::sessionSlotLength(int32_t trackId, int32_t scene) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0.0;
    const SessionSlot& s = t->sessionSlots[static_cast<size_t>(scene)];
    return s.hasClip ? s.lengthBeats : 0.0;
}

bool SessionOps::setSessionSlotLength(int32_t trackId, int32_t scene, double lengthBeats) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[static_cast<size_t>(scene)].hasClip || lengthBeats <= 0.0) return false;
    auto nt = rt_.cloneTrack(*old);
    SessionSlot& s = nt->sessionSlots.write()[static_cast<size_t>(scene)];
    s.lengthBeats = lengthBeats;
    s.midi.lengthBeats = lengthBeats;
    rt_.republishWithTrack(trackId, nt);
    // Keep the audio thread's loop length in sync if we're recording into this slot.
    if (rt_.recordSessionTrackId_ == trackId && rt_.recordSessionScene_ == scene)
        rt_.recordSlotLen_.store(lengthBeats, std::memory_order_relaxed);
    if (const int32_t k = rt_.recorder().midiLaneOf(trackId);
        k >= 0 && rt_.midiLanes_[static_cast<size_t>(k)].scene == scene)
        rt_.recorder().publishSessionLaneSlots(); // a joined slot's clock too (specs/086)
    return true;
}

float SessionOps::sessionSlotGain(int32_t trackId, int32_t scene) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 1.0f;
    const SessionSlot& s = t->sessionSlots[static_cast<size_t>(scene)];
    return (s.hasClip && s.audio.sample) ? s.audio.gain : 1.0f;
}

bool SessionOps::setSessionSlotGain(int32_t trackId, int32_t scene, float gain) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[static_cast<size_t>(scene)].hasClip ||
        !old->sessionSlots[static_cast<size_t>(scene)].audio.sample)
        return false;
    auto nt = rt_.cloneTrack(*old);
    nt->sessionSlots.write()[static_cast<size_t>(scene)].audio.gain = gain < 0.0f ? 0.0f : gain;
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t SessionOps::sessionSlotState(int32_t trackId, int32_t scene) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    if (sessionSlotRecording(trackId, scene) &&
        !(rt_.recordSessionTrackId_ == trackId && rt_.recordSessionScene_ == scene)) {
        // Another slot of the Session take (specs/086); a MIDI one waits for its launch first.
        if (!rt_.recordSessionAudio_ && t->sessionPlayer &&
            t->sessionPlayer->playing.load(std::memory_order_relaxed) != scene)
            return 5;
        return 4;
    }
    if (rt_.recording_.load(std::memory_order_relaxed) && rt_.recordSessionTrackId_ == trackId &&
        rt_.recordSessionScene_ == scene) {
        // Not capturing yet (specs/078): a retried take before the boundary it records from, or a
        // MIDI take whose slot waits for a launch quantum (unquantized, it starts next block).
        if (!std::isnan(rt_.retryStartBeat_) && rt_.transport_.uiPositionBeats() < rt_.retryStartBeat_) return 5;
        if (auto* sp = rt_.recordSlotPlayer_.load(std::memory_order_acquire);
            !rt_.recordSessionAudio_ && launchQuant() > 0.0 && sp &&
            sp->playing.load(std::memory_order_relaxed) != scene && sp->pendingSlot() == scene) {
            return 5;
        }
        return 4; // recording
    }
    if (auto& sp = t->sessionPlayer) {
        if (sp->playing.load(std::memory_order_relaxed) == scene) return 3; // playing
        if (sp->pendingSlot() == scene) return 2;                           // queued
    }
    return t->sessionSlots[static_cast<size_t>(scene)].hasClip ? 1 : 0;
}

bool SessionOps::setSessionNotes(int32_t trackId, int32_t scene, const NoteData* notes, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= static_cast<int32_t>(old->sessionSlots.size())) return false;
    if (!old->sessionSlots[static_cast<size_t>(scene)].hasClip) return false;
    auto nt = rt_.cloneTrack(*old);
    MidiClip& clip = nt->sessionSlots.write()[static_cast<size_t>(scene)].midi;
    clip.notes.clear();
    clip.notes.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) { clip.notes.push_back(rt_.clips().noteFromData(notes[i])); }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t SessionOps::getSessionNotes(int32_t trackId, int32_t scene, NoteData* out, int32_t maxNotes) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    const MidiClip& clip = t->sessionSlots[static_cast<size_t>(scene)].midi;
    const int32_t n = std::min<int32_t>(maxNotes, static_cast<int32_t>(clip.notes.size()));
    for (int32_t i = 0; i < n; ++i) rt_.clips().noteToData(clip.notes[static_cast<size_t>(i)], out[i]);
    return n;
}

int32_t SessionOps::sessionNoteCount(int32_t trackId, int32_t scene) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= static_cast<int32_t>(t->sessionSlots.size())) return 0;
    return static_cast<int32_t>(t->sessionSlots[static_cast<size_t>(scene)].midi.notes.size());
}

// --- Session audio slots as clips (S-06) ------------------------------------------

// A clip's played length in beats: the warp window when warped, else its source region at
// the current tempo (varispeed included).
double SessionOps::audioClipBeats(const AudioClip& c) const {
    if (c.warpEnabled && c.warpBeats > 0.0) return c.warpPlayLen();
    const double spb = rt_.transport_.samplesPerBeat();
    const double devSR = rt_.transport_.sampleRate();
    if (!c.sample || spb <= 0.0 || devSR <= 0.0 || c.sample->sourceSampleRate <= 0.0) return 0.0;
    return static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / c.pitchRatio() / spb;
}

// After a clip edit on a slot's audio (clipIndex <= kSessionClipBase): the slot loops the
// clip, so its loop length follows the clip's played length.
void SessionOps::syncSessionAudioSlot(Track& t, int32_t clipIndex) const {
    const int32_t scene = sessionSceneOfClip(clipIndex);
    if (scene < 0 || scene >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& cs = t.sessionSlots[static_cast<size_t>(scene)];
    if (!cs.hasClip || !cs.audio.sample) return;
    SessionSlot& s = t.sessionSlots.write()[static_cast<size_t>(scene)];
    s.audio.startBeat = 0.0;
    const double beats = audioClipBeats(s.audio);
    if (beats > 0.0) s.lengthBeats = beats;
}

// --- Session P0: clip + scene properties --------------------------------------

bool SessionOps::sessionClipProps(int32_t trackId, int32_t scene, SessionClipProps* out) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const SessionSlot* s = t ? slotAt(*t, scene) : nullptr;
    if (!s || !s->hasClip || !out) return false;
    out->color = s->color;
    out->launchMode = static_cast<int32_t>(s->launchMode);
    out->quantBeats = s->quantBeats;
    out->legato = s->legato ? 1 : 0;
    out->loop = s->loop ? 1 : 0;
    out->velocityAmount = s->velocityAmount;
    out->followA = static_cast<int32_t>(s->followA);
    out->followB = static_cast<int32_t>(s->followB);
    out->chanceA = s->chanceA;
    out->chanceB = s->chanceB;
    out->followBeats = s->followBeats;
    out->jumpScene = s->jumpScene;
    out->followBypass = s->followBypass ? 1 : 0;
    return true;
}

bool SessionOps::setSessionClipProps(int32_t trackId, int32_t scene, const SessionClipProps& p) {
    auto old = rt_.findTrackAuthoring(trackId);
    const SessionSlot* cur = old ? slotAt(*old, scene) : nullptr;
    if (!cur || !cur->hasClip) return false;
    auto nt = rt_.cloneTrack(*old);
    SessionSlot& s = nt->sessionSlots.write()[static_cast<size_t>(scene)];
    s.color = p.color < 0 ? -1 : p.color;
    s.launchMode = static_cast<LaunchMode>(std::clamp(p.launchMode, 0, 3));
    s.quantBeats = p.quantBeats < 0.0 ? -1.0 : p.quantBeats;
    s.legato = p.legato != 0;
    s.loop = p.loop != 0;
    s.velocityAmount = std::clamp(p.velocityAmount, 0.0f, 1.0f);
    s.followA = static_cast<FollowAction>(std::clamp(p.followA, 0, 9));
    s.followB = static_cast<FollowAction>(std::clamp(p.followB, 0, 9));
    s.chanceA = std::clamp(p.chanceA, 0, 100);
    s.chanceB = std::clamp(p.chanceB, 0, 100);
    s.followBeats = p.followBeats > 0.0 ? p.followBeats : 0.0;
    s.jumpScene = std::max(0, p.jumpScene);
    s.followBypass = p.followBypass != 0;
    rt_.republishWithTrack(trackId, nt);
    // A slot leaving Repeat mode must not keep retriggering.
    if (s.launchMode != LaunchMode::Repeat && old->sessionPlayer &&
        old->sessionPlayer->repeatSlot.load(std::memory_order_relaxed) == scene)
        old->sessionPlayer->repeatSlot.store(-1, std::memory_order_relaxed);
    return true;
}

bool SessionOps::sessionClipName(int32_t trackId, int32_t scene, std::string& out) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const SessionSlot* s = t ? slotAt(*t, scene) : nullptr;
    if (!s || !s->hasClip) return false;
    out = s->name;
    return true;
}

bool SessionOps::setSessionClipName(int32_t trackId, int32_t scene, const std::string& name) {
    auto old = rt_.findTrackAuthoring(trackId);
    const SessionSlot* cur = old ? slotAt(*old, scene) : nullptr;
    if (!cur || !cur->hasClip) return false;
    if (cur->name == name) return true;
    auto nt = rt_.cloneTrack(*old);
    nt->sessionSlots.write()[static_cast<size_t>(scene)].name = name;
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool SessionOps::sessionSlotStopButton(int32_t trackId, int32_t scene) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const SessionSlot* s = t ? slotAt(*t, scene) : nullptr;
    return !s || s->stopButton;
}

bool SessionOps::setSessionSlotStopButton(int32_t trackId, int32_t scene, bool on) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    if (sessionSlotStopButton(trackId, scene) == on) return true;
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        slots[static_cast<size_t>(scene)].stopButton = on;
    }
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// --- Next-take length per slot (#503, specs/106) -----------------------------------------------
// The value belongs to the slot (track × scene) and lives in the graph, so Undo/Redo, scene
// edits, a cancelled count-in and a retried take carry it like any slot property. It is kept,
// hidden, while the slot holds a clip: a deleted clip or an undone take shows it again.

bool SessionOps::sessionSlotTakeLength(int32_t trackId, int32_t scene, NextTakeLength& out) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    const SessionSlot* s = slotAt(*t, scene);
    out = s ? s->nextTake : NextTakeLength{};
    return true;
}

bool SessionOps::setSessionSlotTakeLength(int32_t trackId, int32_t scene, const NextTakeLength& v) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
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
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        slots[static_cast<size_t>(scene)].nextTake = n;
    }
    rt_.republishWithTrack(trackId, nt); // one undo step, like the Stop button
    return true;
}

double SessionOps::resolveTakeBeats(const SessionSlot* s) const {
    if (!s || s->nextTake.mode == SlotTakeMode::Global) return sessionRecordLen_;
    if (s->nextTake.mode == SlotTakeMode::Open) return 0.0;
    // Bars of the meter in effect now — a meter change since preparing keeps the bar count.
    return static_cast<double>(s->nextTake.bars) * std::max(1, rt_.transport_.beatsPerBar()) + s->nextTake.beats;
}

double SessionOps::sessionSlotTakeBeats(int32_t trackId, int32_t scene) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= rt_.authoring_->sceneCount) return 0.0;
    return resolveTakeBeats(slotAt(*t, scene));
}

void SessionOps::replaceSlotContent(SessionSlot& dst, SessionSlot src) {
    src.nextTake = dst.nextTake;
    src.landing = dst.landing; // the landing rule belongs to the slot too (#481)
    dst = std::move(src);
}

// --- Landing rule per slot (#481, specs/111) -----------------------------------------------------
// Like the next-take length, the rule is a slot property in the graph: Undo/Redo, scene edits and
// a cancelled or retried take carry it, and it stays (hidden) under a clip. It holds no record of
// past firings — a landing is an event of the running engine only — so nothing can replay.

static constexpr double kMaxLandingQuant = 32.0; // the largest launch quantum offered (8 bars)

bool SessionOps::sessionSlotLandingRule(int32_t trackId, int32_t scene, LandingRule& out) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    const SessionSlot* s = slotAt(*t, scene);
    out = s ? s->landing : LandingRule{};
    return true;
}

bool SessionOps::setSessionSlotLandingRule(int32_t trackId, int32_t scene, const LandingRule& v) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    LandingRule n{v.action, 0, -1.0};
    switch (v.action) {
    case LandingAction::Off:
    case LandingAction::NextScene: break;
    case LandingAction::Scene:
        if (v.scene < 0 || v.scene >= rt_.authoring_->sceneCount) return false;
        n.scene = v.scene;
        break;
    default: return false;
    }
    if (!std::isfinite(v.quantBeats) || v.quantBeats > kMaxLandingQuant) return false;
    if (v.quantBeats >= 0.0) n.quantBeats = v.quantBeats;
    LandingRule cur;
    sessionSlotLandingRule(trackId, scene, cur);
    if (cur == n) return true;
    auto nt = rt_.cloneTrack(*old);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        slots[static_cast<size_t>(scene)].landing = n;
    }
    rt_.republishWithTrack(trackId, nt); // one undo step, like the take length
    return true;
}

// A rule names its target scene by index: a row insert / move / delete moves the index with the
// scene, and a deleted target turns the rule Off (FR-012). `g` holds freshly cloned tracks.
void SessionOps::remapLandingScenes(Graph& g, const std::function<int32_t(int32_t)>& map) {
    for (auto& t : g.tracks) {
        bool hasSceneRule = false;
        for (const auto& s : t->sessionSlots)
            if (s.landing.action == LandingAction::Scene) {
                hasSceneRule = true;
                break;
            }
        if (!hasSceneRule) continue;
        for (auto& s : t->sessionSlots.write())
            if (s.landing.action == LandingAction::Scene) {
                const int32_t n = map(s.landing.scene);
                if (n < 0) s.landing = LandingRule{};
                else
                    s.landing.scene = n;
            }
    }
}

// A fresh take into (trackId, scene) has become a playable clip. Only noted here — the rule is
// served on the next poll(), after the take's own relaunch, so its scene launch is the later
// request (and wins on that track, as any later launch does).
void SessionOps::noteTakeLanded(int32_t trackId, int32_t scene) {
    if (landingMuted_ > 0 || scene < 0) return; // a global stop / a deleted row: never fires
    rt_.landedTakes_.emplace_back(trackId, scene);
}

// A MIDI lane lands when its take ends with the lane kept: only a fresh take whose slot was seen
// recording counts (one still waiting for its boundary is dropped before this, specs/078).
void SessionOps::noteMidiLaneLanded(const MidiTakeLane& l) {
    if (!l.fresh || l.trackId < 0 || l.scene < 0) return;
    const bool started = l.started || (l.player && l.player->playing.load(std::memory_order_relaxed) == l.scene);
    if (started) noteTakeLanded(l.trackId, l.scene);
}

void SessionOps::serviceLandingRules() {
    // The rule's launch still waiting: gone once it started, was replaced or cancelled; withdrawn
    // when its take no longer holds the slot (undone, clip deleted) before its boundary (FR-006).
    if (ruleWait_.gen != 0) {
        if (waitSceneGen_ != ruleWait_.gen) {
            ruleWait_ = RuleWait{};
        } else {
            auto t = rt_.findTrackAuthoring(ruleWait_.trackId);
            const SessionSlot* s = t ? slotAt(*t, ruleWait_.scene) : nullptr;
            if (!s || !s->hasClip) {
                cancelSceneLaunch(waitScene_);
                ruleWait_ = RuleWait{};
            }
        }
    }
    if (rt_.landedTakes_.empty()) return;
    auto landed = std::move(rt_.landedTakes_);
    rt_.landedTakes_.clear();
    if (!rt_.transport_.uiIsPlaying() || !sessionFollow()) return; // stopped since, or Follow off globally
    const int32_t count = rt_.authoring_->sceneCount;
    for (auto [tid, sc] : landed) {
        auto t = rt_.findTrackAuthoring(tid);
        const SessionSlot* s = t ? slotAt(*t, sc) : nullptr;
        if (!s || !s->hasClip || s->landing.action == LandingAction::Off || count <= 0) continue;
        // A MIDI take with no notes is not a take worth moving on for (clarified 2026-11-23).
        if (t->type() == TrackType::Instrument && s->midi.notes.empty()) continue;
        const int32_t target = s->landing.action == LandingAction::NextScene ? (sc + 1) % count : s->landing.scene;
        if (target < 0 || target >= count) continue;
        launchScene(target, false, s->landing.quantBeats);
        ruleWait_ = waitSceneGen_ != 0 ? RuleWait{waitSceneGen_, tid, sc} : RuleWait{};
    }
}

bool SessionOps::copySessionSlot(int32_t srcTrack, int32_t srcScene, int32_t dstTrack, int32_t dstScene) {
    auto src = rt_.findTrackAuthoring(srcTrack);
    auto dst = rt_.findTrackAuthoring(dstTrack);
    if (!src || !dst || src->type() != dst->type() || !sessionLaunchable(*dst)) return false;
    const SessionSlot* from = slotAt(*src, srcScene);
    if (!from || !from->hasClip || dstScene < 0 || dstScene >= rt_.authoring_->sceneCount) return false;
    if (srcTrack == dstTrack && srcScene == dstScene) return true;
    const SessionSlot copy = *from; // before cloning: src may be dst
    auto nt = rt_.cloneTrack(*dst);
    {
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        replaceSlotContent(
            slots[static_cast<size_t>(dstScene)], copy); // the destination keeps its next-take length (#503)
    }
    rt_.republishWithTrack(dstTrack, nt);
    return true;
}

} // namespace nota::detail
