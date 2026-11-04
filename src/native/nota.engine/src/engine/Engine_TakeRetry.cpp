// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Discard and retry a take (#185, specs/078). A take attempt remembers, before it writes
// anything, the tracks it may write and the undo/redo state; a retry puts those tracks back
// with no history step of its own and records again — the Arrangement from where the
// transport was started, a Session slot from the next launch quantum while the rest of the
// Session plays on. The audio thread learns of the new take through a TakeRestart command
// queued behind the transport moves, so nothing from before the restart reaches it.

#include "Engine.h"

#include <algorithm>
#include <cmath>

namespace nota {

void Engine::openTakeAttempt(bool session) {
    takeAttempt_ = TakeAttempt{};
    takeAttempt_.active = true;
    takeAttempt_.session = session;
    takeAttempt_.undoBase = undoStack_.size();
    takeAttempt_.redoSaved = redoStack_;
}

void Engine::noteTakeTrack(int32_t trackId) {
    if (!takeAttempt_.active || !authoring_) return;
    for (const auto& b : takeAttempt_.baseTracks)
        if (b.first == trackId) return; // first write wins: that is the pre-take state
    if (auto t = findTrackAuthoring(trackId)) takeAttempt_.baseTracks.emplace_back(trackId, std::move(t));
}

void Engine::closeTakeAttempt() {
    takeAttempt_ = TakeAttempt{};
    retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
}

void Engine::discardTake() {
    if (!takeAttempt_.active) return;
    TakeAttempt a = std::move(takeAttempt_);
    takeAttempt_ = TakeAttempt{};

    // Stop capturing: automation writes end (their lanes come back with the tracks below),
    // the audio capture is dropped unmaterialised, and the MIDI fold forgets the take.
    finishAllWrites();
    recording_.store(false, std::memory_order_relaxed);
    abortAudioRecording();
    {
        RecordedNote n;
        while (recorded_.pop(n)) {}
        RecordedExpr x;
        while (recordedExpr_.pop(x)) {}
    }
    resetCtlFold();
    pendingExpr_.clear();
    takeNotes_.clear();
    appliedReplaceUpTo_ = 0;
    replaceUpTo_.store(0, std::memory_order_relaxed);
    ++takeEpoch_; // whatever the audio thread still queues for the old take is dropped by poll()
    recordTrackId_ = 0;
    recordClipIndex_ = -1;
    recordTrackRt_.store(-1, std::memory_order_relaxed);
    recordSlotPlayer_.store(nullptr, std::memory_order_release);
    recordSessionScene_ = -1;
    recordSessionTrackId_ = 0;
    recordSessionAudio_ = false;
    recordSessionGrowing_ = false;
    sessionCountIn_ = false;
    retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();

    // Put the tracks back as they were before the take wrote them. A Session take restores
    // only its slot, so edits to the track's other slots made meanwhile stay.
    if (authoring_ && !a.baseTracks.empty()) {
        auto g = std::make_shared<Graph>();
        inheritGraphLevel(*g, *authoring_);
        g->tracks.reserve(authoring_->tracks.size());
        auto baseOf = [&](int32_t id) -> std::shared_ptr<Track> {
            for (const auto& b : a.baseTracks)
                if (b.first == id) return b.second;
            return nullptr;
        };
        auto restore = [&](const std::shared_ptr<Track>& cur) -> std::shared_ptr<Track> {
            auto base = cur ? baseOf(cur->id()) : nullptr;
            if (!base) return cur;
            if (!a.session || cur->id() != a.sessionTrackId) return base;
            auto nt = cloneTrack(*cur);
            const int32_t sc = a.sessionScene;
            if (sc < 0) return nt;
            if (static_cast<int32_t>(nt->sessionSlots.size()) <= sc) nt->sessionSlots.resize(sc + 1);
            nt->sessionSlots[sc] =
                sc < static_cast<int32_t>(base->sessionSlots.size()) ? base->sessionSlots[sc] : SessionSlot{};
            return nt;
        };
        for (const auto& t : authoring_->tracks) g->tracks.push_back(restore(t));
        g->masterTrack = restore(authoring_->masterTrack);
        publishRaw(std::move(g));
        placeTakesForTempo();
        recomputePdc();
        reapplyRackMacros();
        reapplyCcRoutes();
    }

    // No history of its own: the take's checkpoints go (mix-only value steps made meanwhile
    // stay — they describe the user's hand, not the take), and the redo branch comes back.
    const size_t base = std::min(a.undoBase, undoStack_.size());
    std::vector<UndoEntry> kept;
    for (size_t i = base; i < undoStack_.size(); ++i)
        if (!undoStack_[i].graph) kept.push_back(std::move(undoStack_[i]));
    undoStack_.resize(base);
    for (auto& e : kept) undoStack_.push_back(std::move(e));
    if (kept.empty()) redoStack_ = std::move(a.redoSaved);
}

int32_t Engine::retryTake() {
    if (!takeAttempt_.active) return 0;

    auto pushTakeRestart = [&](bool openGate) {
        Command c;
        c.type = CommandType::TakeRestart;
        c.i0 = static_cast<int32_t>(takeEpoch_);
        c.i1 = openGate ? 1 : 0;
        commands_.push(c);
    };

    if (takeAttempt_.session) {
        const int32_t tid = takeAttempt_.sessionTrackId, sc = takeAttempt_.sessionScene;
        // Still counting in from stop: nothing was recorded — start the count-in over, as Stop
        // and a fresh Record into the slot would.
        if (sessionCountIn_ && uiCountIn_.load(std::memory_order_relaxed) > 0.0) {
            sessionCountIn_ = false;
            countInLaunchScene_.store(-1, std::memory_order_release);
            cancelSessionCountIn();
            recordSessionSlot(tid, sc);
            return isRecording() ? 2 : -1;
        }
        discardTake();
        pushTakeRestart(true);
        // The take records again from the next launch quantum (now when launching is
        // unquantized); until then the slot shows it is waiting (slot state 5).
        const double q = launchQuant();
        const double pos = transport_.uiPositionBeats();
        const double at = q > 0.0 ? std::ceil(pos / q - 1e-9) * q : pos;
        recordSessionSlot(tid, sc);
        if (!isRecording()) return -1;
        if (at > pos) retryStartBeat_ = at;
        return 2;
    }

    // Arrangement: back to where the transport was started for this take — its start when
    // the take rolled the transport, else where Play was pressed (the punch-in's run-up).
    const bool countedIn = takeAttempt_.countedIn;
    const bool startedTransport = recordStartedTransport_;
    const double restart = startedTransport ? recordReturnBeat_ : transport_.uiAnchorBeats();
    discardTake();
    captureGate_.store(false, std::memory_order_relaxed); // nothing until the playhead is back
    if (countedIn) {
        transportStop(); // count in again from stop, as the take did
        seekBeats(restart);
        pushTakeRestart(countInBars() <= 0); // else the count-in's end opens the gate
    } else {
        seekBeats(restart); // keep rolling: the run-up plays again
        pushTakeRestart(true);
    }
    const bool ok = startArrangementTake(!countedIn, restart, countedIn);
    recordStartedTransport_ = startedTransport;
    recordReturnBeat_ = restart;
    if (!ok) {
        captureGate_.store(true, std::memory_order_relaxed);
        return -1;
    }
    return 1;
}

} // namespace nota
