// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RecordingOps — MIDI routing/offset, live note/controller/MPE queues, MIDI take lanes, and setRecording.

#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "model/EngineTypes.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "recording/CaptureRing.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

void RecordingOps::setTrackMidiSource(int32_t trackId, int32_t sourceTrackId) {
    if (sourceTrackId == trackId) sourceTrackId = -1; // no self-routing
    if (auto t = rt_.findTrackAuthoring(trackId)) t->setMidiFromTrackId(sourceTrackId);
}
// One undo step (specs/085, T051), like every other edit of a track: a new track with the offset,
// published with the graph. The audio thread reads the new track's atomic; nothing is written to
// the track it may be rendering. An unchanged value is no step at all.
void RecordingOps::setTrackMidiOffsetMs(int32_t trackId, double ms) {
    if (!std::isfinite(ms)) ms = 0.0;
    ms = std::clamp(ms, -2000.0, 2000.0);
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->midiOffsetMs() == ms) return;
    auto nt = rt_.cloneTrack(*old);
    nt->setMidiOffsetMs(ms);
    rt_.republishWithTrack(trackId, nt);
}
double RecordingOps::trackMidiOffsetMs(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->midiOffsetMs() : 0.0;
}
int32_t RecordingOps::trackMidiSource(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->midiFromTrackId() : -1;
}
// The MidiEvent of a live event from `source` (specs/086): hardware sources keep their index.
static MidiEvent liveEvent(bool on, int32_t pitch, float v, int32_t expr, int32_t source, int32_t channel) {
    MidiEvent e{on, pitch, v, -1, expr};
    e.source = static_cast<int8_t>(source >= 0 && source < 127 ? source : -1);
    e.channel = static_cast<int8_t>(std::clamp(channel, 0, 15));
    return e;
}

void RecordingOps::noteOn(int32_t pitch, float velocity, int32_t source, int32_t channel) {
    if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
    {
        std::lock_guard<std::mutex> lk(liveMidiMx_);
        rt_.liveMidi_.push(liveEvent(true, pitch, velocity, -1, source, channel));
    }
    if (pitch >= 0 && pitch < 128)
        liveHeld_[static_cast<size_t>(pitch >> 5)].fetch_or(1u << (pitch & 31), std::memory_order_relaxed);
}
void RecordingOps::noteOff(int32_t pitch, float releaseVelocity, int32_t source, int32_t channel) {
    if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
    {
        std::lock_guard<std::mutex> lk(liveMidiMx_);
        rt_.liveMidi_.push(liveEvent(false, pitch, std::clamp(releaseVelocity, 0.0f, 1.0f), -1, source, channel));
    }
    if (pitch >= 0 && pitch < 128)
        liveHeld_[static_cast<size_t>(pitch >> 5)].fetch_and(~(1u << (pitch & 31)), std::memory_order_relaxed);
}

// The instrument-wide dims (pitch −1) are controllers now: one path plays and records them.
static int32_t ctlOfGlobalDim(int32_t dim) {
    switch (dim) {
    case ExprBend: return kCtlPitchWheel;
    case ExprPressure: return kCtlChannelPressure;
    case ExprSlide: return 74;
    default: return kCtlSustain; // ExprSustain
    }
}

void RecordingOps::noteExpression(int32_t pitch, int32_t dim, float value, int32_t source, int32_t channel) {
    if (pitch < -1 || pitch > 127 || dim < 0 || dim >= kExprWireDims || (dim >= kExprDims && pitch >= 0) ||
        !std::isfinite(value))
        return;
    if (pitch < 0) {
        controller(ctlOfGlobalDim(dim), value, source, channel);
        return;
    }
    std::lock_guard<std::mutex> lk(liveMidiMx_);
    rt_.liveMidi_.push(liveEvent(false, pitch, value, dim, source, channel));
}

void RecordingOps::trackNoteExpression(int32_t trackId, int32_t pitch, int32_t dim, float value) {
    if (pitch < -1 || pitch > 127 || dim < 0 || dim >= kExprWireDims || (dim >= kExprDims && pitch >= 0) ||
        !std::isfinite(value))
        return;
    if (pitch < 0) {
        trackController(trackId, ctlOfGlobalDim(dim), value);
        return;
    }
    std::lock_guard<std::mutex> lk(remoteMidiMx_);
    rt_.remoteMidi_.push(MidiEvent{false, pitch, value, trackId, dim});
}

void RecordingOps::controller(int32_t ctl, float value, int32_t source, int32_t channel) {
    if (ctlIsReserved(ctl) || !std::isfinite(value)) return;
    value = ctlClamp(ctl, value);
    rt_.noteLiveController(ctl, value, source, channel);
    std::lock_guard<std::mutex> lk(liveMidiMx_);
    rt_.liveMidi_.push(liveEvent(false, -1, value, kCtlDimBase + ctl, source, channel));
}

bool RecordingOps::setTrackMidiInput(int32_t trackId, int32_t source, int32_t channel) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument) return false;
    if (source != Track::kMidiAllInputs && (source < 0 || source >= MidiInput::kMaxSources)) source = -1; // nothing
    channel = std::clamp(channel, 0, 16);
    if (source == Track::kMidiAllInputs) t->midiInputUid.clear();
    t->setMidiInput(source, channel);
    if (const int32_t k = midiLaneOf(trackId); k >= 0) { // recording now: the lane follows
        rt_.midiLaneSrc_[static_cast<size_t>(k)].store(source, std::memory_order_relaxed);
        rt_.midiLaneCh_[static_cast<size_t>(k)].store(channel, std::memory_order_relaxed);
    }
    return true;
}

bool RecordingOps::setTrackMidiInputUid(int32_t trackId, const std::string& uid, int32_t channel) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument) return false;
    t->midiInputUid = uid;
    return setTrackMidiInput(trackId, uid.empty() ? Track::kMidiAllInputs : rt_.io().midiSourceOfUid(uid), channel);
}
std::string RecordingOps::trackMidiInputUid(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->midiInputUid : std::string{};
}

// MIDI take lanes (specs/086): one per recording instrument track, published to the audio
// thread before the take's recording_ flag; a lane closes when its track stops recording.
void RecordingOps::publishMidiLanes(const std::vector<int32_t>& trackIds) {
    const int32_t n = std::min(static_cast<int32_t>(trackIds.size()), kMaxMidiTakeLanes);
    rt_.midiLanes_.assign(static_cast<size_t>(n), MidiTakeLane{});
    for (int32_t k = 0; k < n; ++k) {
        const size_t i = static_cast<size_t>(k);
        rt_.midiLanes_[i].trackId = trackIds[i];
        auto t = rt_.findTrackAuthoring(trackIds[i]);
        rt_.midiLaneSrc_[i].store(t ? t->midiInputSource() : Track::kMidiAllInputs, std::memory_order_relaxed);
        rt_.midiLaneCh_[i].store(t ? t->midiInputChannel() : 0, std::memory_order_relaxed);
        rt_.midiLaneTrack_[i].store(trackIds[i], std::memory_order_relaxed);
    }
    rt_.midiLaneCount_.store(n, std::memory_order_release);
    publishSessionLaneSlots();
}

void RecordingOps::closeMidiLane(int32_t lane) {
    if (lane < 0 || lane >= static_cast<int32_t>(rt_.midiLanes_.size())) return;
    rt_.midiLanes_[static_cast<size_t>(lane)].trackId = -1;
    rt_.midiLaneTrack_[static_cast<size_t>(lane)].store(-1, std::memory_order_relaxed);
    publishSessionLaneSlots();
}

// The Session lanes' slots, for the audio thread's per-lane clocks (specs/086); none for an
// Arrangement take. The table keeps its players alive while a render may read it.
void RecordingOps::publishSessionLaneSlots() {
    std::shared_ptr<SessionLaneSlots> tab;
    for (size_t k = 0; k < rt_.midiLanes_.size() && k < 16; ++k) {
        const MidiTakeLane& l = rt_.midiLanes_[k];
        if (l.trackId < 0 || l.scene < 0 || !l.player) continue;
        if (!tab) tab = std::make_shared<SessionLaneSlots>();
        tab->player[k] = l.player.get();
        tab->scene[k] = l.scene;
        double len = 4.0;
        if (auto t = rt_.findTrackAuthoring(l.trackId); t && l.scene < static_cast<int32_t>(t->sessionSlots.size()))
            len = t->sessionSlots[static_cast<size_t>(l.scene)].lengthBeats;
        tab->len[k] = len;
        tab->keep.push_back(l.player);
    }
    rt_.laneSlotsHist_.publish(std::move(tab));
}

// A MIDI slot launched on another instrument track while a Session MIDI take runs (specs/086):
// it joins the take as a lane of its own — its own clip, input filter and loop clock — and
// records from when its slot starts playing (the launch quantum).
bool RecordingOps::addSessionMidiLane(int32_t trackId, int32_t scene) {
    if (!rt_.recording_.load(std::memory_order_relaxed) || rt_.recordSessionAudio_ || rt_.recordSessionScene_ < 0 ||
        rt_.sessionCountIn_ || rt_.midiLanes_.empty() || rt_.midiLanes_[0].scene < 0)
        return false;
    if (midiLaneOf(trackId) >= 0 || static_cast<int32_t>(rt_.midiLanes_.size()) >= kMaxMidiTakeLanes) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    noteTakeTrack(trackId);
    const bool hasClip =
        scene < static_cast<int32_t>(t->sessionSlots.size()) && t->sessionSlots[static_cast<size_t>(scene)].hasClip;
    // An empty slot's own next-take length (#503); an overdub follows the Session record length.
    const SessionSlot* slot = hasClip || scene >= static_cast<int32_t>(t->sessionSlots.size())
                                  ? nullptr
                                  : &t->sessionSlots[static_cast<size_t>(scene)];
    const double len = rt_.session().resolveTakeBeats(slot);
    const bool growing = !hasClip && len <= 0.0;
    if (!hasClip) {
        EngineRuntime::TakePushScope takePush(rt_); // the take's own clip (specs/078)
        rt_.session().addSessionMidiClip(trackId, scene, growing ? kGrowingTakeBeats : len);
        t = rt_.findTrackAuthoring(trackId);
        if (!t) return false;
    }
    MidiTakeLane l;
    l.trackId = trackId;
    l.scene = scene;
    l.growing = growing;
    l.lenBeats = len;
    l.ownLen = rt_.session().ownTakeLength(slot);
    l.fresh = !hasClip; // its landing fires the slot's rule (#481)
    l.player = t->sessionPlayer;
    const size_t k = rt_.midiLanes_.size();
    rt_.midiLanes_.push_back(std::move(l));
    rt_.midiLaneSrc_[k].store(t->midiInputSource(), std::memory_order_relaxed);
    rt_.midiLaneCh_[k].store(t->midiInputChannel(), std::memory_order_relaxed);
    rt_.midiLaneTrack_[k].store(trackId, std::memory_order_relaxed);
    publishSessionLaneSlots(); // its clock is there before the lane is
    rt_.midiLaneCount_.store(static_cast<int32_t>(k) + 1, std::memory_order_release);
    if (rt_.takeAttempt_.active) rt_.takeAttempt_.sessionSlots.emplace_back(trackId, scene); // a retry relaunches it
    rt_.session().launchSlot(trackId, scene); // plays + loops the slot from the launch quantum
    return true;
}

// A fresh MIDI take that ran open-ended — or one of its slot's own length stopped by hand before
// that length (#503) — is cut to whole launch quanta (a bar when launching is unquantized), never
// past its length. No undo step of its own, so undo drops the whole take.
void RecordingOps::cutGrowingLane(MidiTakeLane& l) {
    const bool cutShort = !l.growing && l.ownLen && !rt_.takeLengthReached_;
    if ((!l.growing && !cutShort) || l.trackId < 0 || l.scene < 0) return;
    l.growing = false;
    l.ownLen = false;
    double el = 0.0;
    if (l.player && l.player->playing.load(std::memory_order_relaxed) == l.scene)
        el = l.player->playedBeats.load(std::memory_order_relaxed);
    if (auto old = rt_.findTrackAuthoring(l.trackId); old && l.scene < static_cast<int32_t>(old->sessionSlots.size())) {
        auto nt = rt_.cloneTrack(*old);
        SessionSlot& sl = nt->sessionSlots.write()[static_cast<size_t>(l.scene)];
        double len = rt_.session().roundedTakeBeats(el);
        if (cutShort) len = std::min(len, l.lenBeats);
        sl.lengthBeats = len;
        sl.midi.lengthBeats = len;
        rt_.republishWithTrackRaw(l.trackId, nt);
    }
}

// One slot of a several-slot Session MIDI take stops (specs/086): what it recorded goes in, its
// open controller ranges close, a fresh open-ended take is cut to quanta, and the slot plays
// on. The first slot leaving hands the take's clock and count to the next.
void RecordingOps::finishSessionMidiLane(int32_t lane) {
    if (lane < 0 || lane >= static_cast<int32_t>(rt_.midiLanes_.size())) return;
    foldRecordedTake(); // everything queued so far, every lane
    MidiTakeLane& l = rt_.midiLanes_[static_cast<size_t>(lane)];
    foldSessionLane(l, lane, {}, {}, true);
    cutGrowingLane(l);
    rt_.session().noteMidiLaneLanded(l); // kept as a playable clip: its slot's rule may fire (#481)
    const int32_t tid = l.trackId;
    closeMidiLane(lane);
    if (rt_.takeAttempt_.active) keepTakeTrack(tid); // landed: a retry leaves it be
    if (tid == rt_.recordSessionTrackId_) {          // the take's first slot: the next one carries it
        for (auto& n : rt_.midiLanes_) {
            if (n.trackId < 0 || n.scene < 0) continue;
            rt_.recordSessionTrackId_ = n.trackId;
            rt_.recordSessionScene_ = n.scene;
            rt_.recordSessionGrowing_ = n.growing;
            rt_.recordSessionLen_ = n.lenBeats;
            if (auto t = rt_.findTrackAuthoring(n.trackId); t && n.scene < static_cast<int32_t>(t->sessionSlots.size()))
                rt_.recordSlotLen_.store(
                    t->sessionSlots[static_cast<size_t>(n.scene)].lengthBeats, std::memory_order_relaxed);
            rt_.recordSlotHist_.publish(n.player);
            rt_.recordSlotPlayer_.store(n.player.get(), std::memory_order_release);
            rt_.takeAttempt_.sessionTrackId = n.trackId;
            rt_.takeAttempt_.sessionScene = n.scene;
            break;
        }
    }
}

int32_t RecordingOps::midiLaneOf(int32_t trackId) const {
    for (size_t k = 0; k < rt_.midiLanes_.size(); ++k)
        if (rt_.midiLanes_[k].trackId == trackId) return static_cast<int32_t>(k);
    return -1;
}

int32_t RecordingOps::openMidiLaneCount() const {
    int32_t n = 0;
    for (const auto& l : rt_.midiLanes_) n += l.trackId >= 0 ? 1 : 0;
    return n;
}
int32_t RecordingOps::trackMidiInputSource(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->midiInputSource() : Track::kMidiAllInputs;
}
int32_t RecordingOps::trackMidiInputChannel(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->midiInputChannel() : 0;
}

void RecordingOps::trackController(int32_t trackId, int32_t ctl, float value) {
    if (ctlIsReserved(ctl) || !std::isfinite(value)) return;
    value = ctlClamp(ctl, value);
    rt_.noteLiveController(ctl, value, -1, 0, trackId);
    std::lock_guard<std::mutex> lk(remoteMidiMx_);
    rt_.remoteMidi_.push(MidiEvent{false, -1, value, trackId, kCtlDimBase + ctl});
}

void RecordingOps::trackNoteOn(int32_t trackId, int32_t pitch, float velocity) {
    {
        std::lock_guard<std::mutex> lk(remoteMidiMx_);
        rt_.remoteMidi_.push(MidiEvent{true, pitch, velocity, trackId});
    }
    if (pitch >= 0 && pitch < 128)
        liveHeld_[static_cast<size_t>(pitch >> 5)].fetch_or(1u << (pitch & 31), std::memory_order_relaxed);
}
void RecordingOps::trackNoteOff(int32_t trackId, int32_t pitch, float releaseVelocity) {
    {
        std::lock_guard<std::mutex> lk(remoteMidiMx_);
        rt_.remoteMidi_.push(MidiEvent{false, pitch, std::clamp(releaseVelocity, 0.0f, 1.0f), trackId});
    }
    if (pitch >= 0 && pitch < 128)
        liveHeld_[static_cast<size_t>(pitch >> 5)].fetch_and(~(1u << (pitch & 31)), std::memory_order_relaxed);
}

// Currently-pressed live-input pitches (keyboard + MIDI), oldest bit order. Lock-free.
int32_t RecordingOps::liveHeldNotes(int32_t* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    int32_t n = 0;
    for (size_t w = 0; w < 4 && n < maxN; ++w) {
        uint32_t bits = liveHeld_[w].load(std::memory_order_relaxed);
        while (bits && n < maxN) {
            int b = std::countr_zero(bits); // portable count-trailing-zeros (C++20)
            out[n++] = static_cast<int32_t>(w) * 32 + b;
            bits &= bits - 1;
        }
    }
    return n;
}

// The live-recording preview (#56): the audio thread's held-note snapshot, retried while a
// block is writing it. Take geometry comes from the message-thread record state (this
// runs on the UI thread, which owns it). A read that keeps racing returns 0 — one
// frame without preview, never a wait.
int32_t RecordingOps::recordHeldNotes(
    RecordHeldNote* out, int32_t maxN, int32_t* trackId, int32_t* sessionScene, double* nowBeat) const {
    // An audio take into a Session slot records no MIDI: recordTrackId_ may still name the
    // last MIDI take's track (it isn't reset on stop), so it must not light up here.
    const bool rec = rt_.recording_.load(std::memory_order_relaxed) && !rt_.recordSessionAudio_;
    const bool slot = rec && rt_.recordSessionScene_ >= 0 && rt_.recordSessionTrackId_ > 0;
    const int32_t tid = !rec ? 0 : slot ? rt_.recordSessionTrackId_ : (recordTrackId_ > 0 ? recordTrackId_ : 0);
    if (trackId) *trackId = tid;
    if (sessionScene) *sessionScene = slot ? rt_.recordSessionScene_ : -1;
    if (nowBeat) *nowBeat = 0.0;
    if (tid <= 0) return 0;
    for (int attempt = 0; attempt < 4; ++attempt) {
        const uint32_t s0 = rt_.heldSeq_.load(std::memory_order_acquire);
        if (s0 & 1u) continue;
        const int32_t n = std::clamp(rt_.heldCount_.load(std::memory_order_relaxed), 0, kMaxHeldSnap);
        const double now = rt_.heldNow_.load(std::memory_order_relaxed);
        const int32_t m = (out && maxN > 0) ? std::min(n, maxN) : 0;
        for (int32_t i = 0; i < m; ++i) {
            out[i].pitch = rt_.heldSnap_[i].pitch.load(std::memory_order_relaxed);
            out[i].source = rt_.heldSnap_[i].source.load(std::memory_order_relaxed);
            out[i].startBeat = rt_.heldSnap_[i].startBeat.load(std::memory_order_relaxed);
            out[i].velocity = rt_.heldSnap_[i].velocity.load(std::memory_order_relaxed);
        }
        std::atomic_thread_fence(std::memory_order_acquire);
        if (rt_.heldSeq_.load(std::memory_order_relaxed) != s0) continue;
        if (nowBeat) *nowBeat = now;
        return n;
    }
    return 0;
}

void RecordingOps::setRecording(bool on) {
    // The record button is also the automation-record switch (M9-C): while it is
    // engaged, moving any control writes that parameter's lane. This is set even
    // when no track is armed for a take — if the UI then pops the button back up
    // it calls us again with false, which clears it.
    rt_.automation().setAutomationRecord(on);
    if (on) {
        collectRecordedExpr();
        pendingExpr_.clear();
        resetCtlFold();
    } // a new take starts with no stale curves
    if (!on) {
        // Controllers still being replaced over a range end where the take stops (poll folds it).
        if (rt_.recording_.load(std::memory_order_relaxed)) {
            rt_.ctlStopPending_ = true;
            rt_.ctlStopAt_ = rt_.transport_.uiPositionBeats();
        }
        rt_.recording_.store(false, std::memory_order_relaxed);
        stopAudioRecording(); // materialise if an audio take was rolling
                              // The Session performance last (#971): after the takes are in place, so an armed
                              // instrument track's played notes can join the clip its slot wrote.
        if (rt_.sessionPass_.active) {
            foldRecordedTake();
            rt_.session().finishSessionPass(rt_.transport_.uiPositionBeats());
        }
        if (!rt_.takeAttempt_.session) closeTakeAttempt(); // the take is kept: nothing to put back
        // If the take started the transport, stop it and rewind to where the
        // take began so re-recording lands in the same place (no drift).
        if (recordStartedTransport_) {
            recordStartedTransport_ = false;
            rt_.transportStop();
            rt_.seekBeats(recordReturnBeat_);
        }
        return;
    }
    startArrangementTake(rt_.transport_.uiIsPlaying(), rt_.transport_.uiPositionBeats(), false);
}

// The take start behind setRecording(true). A retry (specs/078) passes where the take starts
// and whether the clock rolls — the transport mirror still shows the take it just dropped —

} // namespace nota::detail
