// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — lifecycle, audio/MIDI device config, preview, tone/transport setters, immutable-graph publish/clone/undo/reset, post-fader meters. Definitions of the rest of the class live in the sibling Engine_*.cpp slices.

#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"
#include "session/SessionPlayer.h"

#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota::detail {

// --- debug tone ------------------------------------------------------------
void EngineRuntime::setToneEnabled(bool enabled) {
    Command c;
    c.type = CommandType::SetToneEnabled;
    c.i0 = enabled ? 1 : 0;
    commands_.push(c);
}
void EngineRuntime::setFrequency(float hz) {
    Command c;
    c.type = CommandType::SetFrequency;
    c.f0 = hz;
    commands_.push(c);
}

// --- transport -------------------------------------------------------------

bool EngineRuntime::enqueueTransportStart(int32_t countInBars) {
    Command c;
    c.type = CommandType::TransportPlay;
    c.i0 = countInBars;
    return commands_.push(c);
}
void EngineRuntime::playClock() {
    enqueueTransportStart(0);
}
// Main Play (and arrangement record) roll the Arrangement; session launches use playClock().
void EngineRuntime::transportPlay() {
    arrangementActive_.store(true, std::memory_order_relaxed);
    const double hint = pendingSeekBeat_ >= 0.0 ? pendingSeekBeat_ : transport_.uiPositionBeats();
    pendingSeekBeat_ = -1.0;
    syncStreamingPlaybackState(true, hint);
    if (hybrid_) {
        hybrid_->invalidate(true);
        hybrid_->onPlayRequested();
    }
    playClock();
}
// Play preceded by the count-in (when one is set and the transport is stopped). The audio
// thread clicks it out and rolls the transport sample-accurately at its end.
void EngineRuntime::transportPlayCountIn() {
    if (uiCountIn_.load(std::memory_order_relaxed) > 0.0) return; // already counting in
    const int32_t bars = countInBars();
    if (bars <= 0 || transport_.uiIsPlaying()) {
        transportPlay();
        return;
    }
    const double hint = pendingSeekBeat_ >= 0.0 ? pendingSeekBeat_ : transport_.uiPositionBeats();
    pendingSeekBeat_ = -1.0;
    syncStreamingPlaybackState(true, hint);
    if (hybrid_) {
        hybrid_->invalidate(true);
        hybrid_->onPlayRequested();
    }
    arrangementActive_.store(true, std::memory_order_relaxed);
    startCountIn(bars);
}
void EngineRuntime::startCountIn(int32_t bars) {
    captureGate_.store(false, std::memory_order_relaxed);
    // Mirror it now so a poll before the audio thread picks the command up already sees
    // the count-in (Play pressed again = stop, not a second start).
    uiCountIn_.store(static_cast<double>(bars) * transport_.beatsPerBar(), std::memory_order_relaxed);
    if (!enqueueTransportStart(bars)) {
        uiCountIn_.store(0.0, std::memory_order_relaxed);
        captureGate_.store(true, std::memory_order_release);
    }
}
void EngineRuntime::transportStop() {
    if (hybrid_) hybrid_->setStopped();
    syncStreamingPlaybackState(false);
    arrangementActive_.store(false, std::memory_order_relaxed);
    {
        SessionOps::LandingMute mute(session()); // a global stop never fires a landing rule (#481)
        session().finalizeSessionRecord(false);  // don't lose an in-progress session take on a global stop
    }
    landedTakes_.clear(); // takes that landed just before: the music stops, nothing launches
    // Stop means everything stops, Session clips included (#139). Immediately, not on the
    // next launch boundary: the clock halts with this command, so a quantized stop would
    // never land and the clips would stay launched (and resume on the next Play).
    // Only tracks with a clip launched or queued: applying a stop flushes the track's notes
    // and MIDI effects, which must not touch a track that plays the Arrangement.
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (auto& sp = t->sessionPlayer;
                sp && (sp->playing.load(std::memory_order_relaxed) >= 0 || sp->pendingSlot() != SessionPlayer::kNone))
                sp->requestStopNow();
    sceneFollowScene_ = -1;
    Command c;
    c.type = CommandType::TransportStop;
    commands_.push(c);
}
// Hand every track back to the Arrangement: stop all session clips + re-activate the timeline.
void EngineRuntime::backToArrangement() {
    if (authoring_)
        for (auto& t : authoring_->tracks)
            if (t->sessionPlayer) t->sessionPlayer->requestStop();
    sceneFollowScene_ = -1;
    arrangementActive_.store(true, std::memory_order_relaxed);
}
bool EngineRuntime::setBpm(double bpm) {
    if (!std::isfinite(bpm) || bpm < 1 || bpm > 999) return false;
    if (tempoMap()) return false; // the master Tempo lane leads (specs/099)
    Command c;
    c.type = CommandType::SetBpm;
    c.d0 = bpm;
    commands_.push(c);
    // Warped clips play from an offline cache stretched to the tempo,
    // so a tempo change must rebuild them. Do it on this (authoring) thread with the new
    // spb computed directly — the SetBpm command above only reaches the transport on the
    // audio thread later, so reading transport_.samplesPerBeat() here would be stale.
    const double devSR = transport_.sampleRate();
    if (bpm > 0.0 && devSR > 0.0) clips().reconfigureAllWarpStreams(devSR * 60.0 / bpm, devSR);
    if (bpm > 0.0) {
        authoringBpm_ = bpm;
        recorder().placeTakesForTempo(); // take lanes + their comp keep one mapping (specs/067)
    }
    if (hybrid_) hybrid_->invalidate(true);
    return true;
}
double EngineRuntime::bpm() const {
    return transport_.uiBpm();
}
void EngineRuntime::setTimeSignature(int num, int denom) {
    Command c;
    c.type = CommandType::SetTimeSignature;
    c.i0 = num;
    c.i1 = denom;
    commands_.push(c);
}
void EngineRuntime::setLoop(bool enabled, double s, double e) {
    loopEnabled_ = enabled && e > s; // message-thread mirror for the UI
    loopStart_ = s;
    loopEnd_ = e;
    if (hybrid_) hybrid_->invalidate(true);
    Command c;
    c.type = CommandType::SetLoop;
    c.i0 = enabled ? 1 : 0;
    c.d0 = s;
    c.d1 = e;
    commands_.push(c);
}
void EngineRuntime::setMetronome(bool on) {
    Command c;
    c.type = CommandType::SetMetronome;
    c.i0 = on ? 1 : 0;
    commands_.push(c);
}
void EngineRuntime::seekBeats(double beat) {
    if (hybrid_) hybrid_->invalidate(true);
    pendingSeekBeat_ = beat;
    syncStreamingPlaybackState(transport_.uiIsPlaying(), beat);
    Command c;
    c.type = CommandType::Seek;
    c.d0 = beat;
    commands_.push(c);
}

} // namespace nota::detail
