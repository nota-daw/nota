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
#include "ParamPath.h"

#include <algorithm>
#include <cmath>

namespace nota {

void Engine::openTakeAttempt(bool session) {
    if (!retryingTake_) syncTakeEpoch();
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

// A track left the take with its part kept (disarmed while recording). Its pre-take state no
// longer applies, and the checkpoints the take made so far now hold a kept recording, so they
// become ordinary history: a later retry restores only what is still recording.
void Engine::keepTakeTrack(int32_t trackId) {
    auto& b = takeAttempt_.baseTracks;
    b.erase(std::remove_if(b.begin(), b.end(), [&](const auto& p) { return p.first == trackId; }), b.end());
    auto& sl = takeAttempt_.sessionSlots; // a landed Session slot is no longer the take's (specs/086)
    sl.erase(std::remove_if(sl.begin(), sl.end(), [&](const auto& p) { return p.first == trackId; }), sl.end());
    for (size_t i = std::min(takeAttempt_.undoBase, undoStack_.size()); i < undoStack_.size(); ++i)
        if (undoStack_[i].take) {
            undoStack_[i].take = false;
            undoStack_[i].wasTake = true; // a retry drops it again if it ends up changing nothing
        }
    takeAttempt_.redoSaved.clear(); // a kept recording is an edit: the old redo branch is gone
}

// Every take that starts without a retry puts the audio thread on the current take epoch and
// cancels a Session retry still waiting for its slot — one stopped before it relaunched would
// otherwise leave the next take's events on an epoch poll() drops.
void Engine::syncTakeEpoch() {
    Command c;
    c.type = CommandType::TakeRestart;
    c.i0 = static_cast<int32_t>(takeEpoch_);
    c.i1 = 4; // epoch only: no restart, the gate as it is
    commands_.push(c);
}

void Engine::closeTakeAttempt() {
    takeAttempt_ = TakeAttempt{};
    retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
}

// Where each device of `from` sits in `to` (-1: removed), by lineage: a moved device is the
// same object in a new slot, and a copy an edit swapped in (a rack fork, a MIDI effect's loaded
// state) carries its source's lineage. A fresh device of the same kind is a different device.
template <class Chain>
static std::vector<int32_t> deviceMap(const Chain& from, const Chain& to) {
    std::vector<int32_t> map(from.size(), -1);
    for (size_t i = 0; i < from.size(); ++i)
        for (size_t j = 0; j < to.size(); ++j)
            if (from[i] && to[j] && from[i]->lineage() == to[j]->lineage()) {
                map[i] = static_cast<int32_t>(j);
                break;
            }
    return map;
}

// The pre-take lanes of `base`, aimed at the devices where `cur` has them (deviceMap); a lane
// whose device was removed goes with it, as remapAutomationAfterDeviceChange does.
static std::vector<TrackLane> lanesOnto(const Track& base, const Track& cur) {
    const auto fx = deviceMap(base.devices, cur.devices);
    const auto mfx = deviceMap(base.midiEffects, cur.midiEffects);
    auto at = [](const std::vector<int32_t>& m, int32_t idx) {
        return idx >= 0 && idx < static_cast<int32_t>(m.size()) ? m[static_cast<size_t>(idx)] : -1;
    };
    std::vector<TrackLane> out;
    out.reserve(base.automation.size());
    for (TrackLane l : base.automation) {
        const bool midiRack = l.nested() && isMidiHost(l.deviceIndex);
        const bool midi = midiRack || (!l.nested() && l.target == AutomationTarget::MidiDeviceParam);
        const bool effect = !midi && (l.nested() ? l.deviceIndex >= 0
                                                 : l.target == AutomationTarget::DeviceParam ||
                                                       (l.target == AutomationTarget::PluginParam && l.deviceIndex >= 0));
        if (midi) {
            const int32_t ni = at(mfx, midiRack ? midiHostIndex(l.deviceIndex) : l.deviceIndex);
            if (ni < 0) continue;
            l.deviceIndex = midiRack ? midiHost(ni) : ni;
        } else if (effect) {
            const int32_t ni = at(fx, l.deviceIndex);
            if (ni < 0) continue;
            l.deviceIndex = ni;
        }
        out.push_back(std::move(l));
    }
    return out;
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

    // Put the tracks back as they were before the take wrote them — in the graph that plays and
    // in every checkpoint the user's own edits pushed meanwhile, so undoing one of those later
    // doesn't bring the take back. A Session take restores only its slot, so edits to the
    // track's other slots stay.
    auto baseOf = [&](int32_t id) -> std::shared_ptr<Track> {
        for (const auto& b : a.baseTracks)
            if (b.first == id) return b.second;
        return nullptr;
    };
    // An Arrangement take restores only what recording writes — the clips, the loop passes and
    // the automation lanes — so a rename, a fader move or a device added meanwhile stays.
    auto restoreTrack = [&](const std::shared_ptr<Track>& cur) -> std::shared_ptr<Track> {
        auto base = cur ? baseOf(cur->id()) : nullptr;
        if (!base || cur == base) return cur;
        auto nt = cloneTrack(*cur);
        int32_t sc = -1; // the slot this track's lane of a Session take records (specs/086)
        if (a.session)
            for (const auto& [tid, scene] : a.sessionSlots)
                if (tid == cur->id()) sc = scene;
        if (a.session && a.sessionSlots.empty() && cur->id() == a.sessionTrackId) sc = a.sessionScene;
        if (sc < 0) {
            nt->clips = base->clips;
            nt->takes = base->takes;
            nt->midiClips = base->midiClips;
            nt->automation = lanesOnto(*base, *cur);
            return nt;
        }
        if (static_cast<int32_t>(nt->sessionSlots.size()) <= sc) nt->sessionSlots.resize(sc + 1);
        nt->sessionSlots[sc] =
            sc < static_cast<int32_t>(base->sessionSlots.size()) ? base->sessionSlots[sc] : SessionSlot{};
        return nt;
    };
    auto restoreGraph = [&](const Graph& src) {
        auto g = std::make_shared<Graph>();
        inheritGraphLevel(*g, src);
        g->tracks.reserve(src.tracks.size());
        for (const auto& t : src.tracks) g->tracks.push_back(restoreTrack(t));
        g->masterTrack = restoreTrack(src.masterTrack);
        return g;
    };
    if (authoring_ && !a.baseTracks.empty()) {
        publishRaw(restoreGraph(*authoring_), true);
        placeTakesForTempo();
        recomputePdc();
        reapplyRackMacros();
        reapplyCcRoutes();
    }

    // No history of its own: the take's checkpoints go (a value change merged into one stays,
    // without the graph — that is the user's hand, not the take). The user's checkpoints made
    // meanwhile stay, without the take in them; with none, the redo branch comes back.
    const size_t base = std::min(a.undoBase, undoStack_.size());
    std::vector<UndoEntry> kept;
    bool userEdits = false;
    for (size_t i = base; i < undoStack_.size(); ++i) {
        UndoEntry e = std::move(undoStack_[i]);
        if (e.take) {
            if (e.mix.empty()) continue;
            e.graph = nullptr;
            e.take = false;
        } else if (e.graph && !a.baseTracks.empty()) {
            e.graph = restoreGraph(*e.graph);
        }
        userEdits = true;
        kept.push_back(std::move(e));
    }
    // A checkpoint the take made before part of it was kept (wasTake) may now step between two
    // identical states — its content was the part just discarded: an Undo that changes nothing.
    // Drop those (track-only edits, so comparing the tracks is enough). Such a step holds only
    // the take's writes, so on a track the retry restored both sides count as the same.
    auto sameTrack = [&](const std::shared_ptr<Track>& x, const std::shared_ptr<Track>& y) {
        return x == y || (x && y && x->id() == y->id() && baseOf(x->id()));
    };
    auto sameTracks = [&](const Graph& x, const Graph& y) {
        if (!sameTrack(x.masterTrack, y.masterTrack) || x.tracks.size() != y.tracks.size()) return false;
        for (size_t k = 0; k < x.tracks.size(); ++k)
            if (!sameTrack(x.tracks[k], y.tracks[k])) return false;
        return true;
    };
    std::shared_ptr<Graph> next = authoring_;
    for (size_t k = kept.size(); k-- > 0;) {
        if (!kept[k].graph) continue;
        if (kept[k].wasTake && kept[k].mix.empty() && next && sameTracks(*kept[k].graph, *next)) {
            kept.erase(kept.begin() + static_cast<std::ptrdiff_t>(k));
            continue;
        }
        next = kept[k].graph;
    }
    userEdits = !kept.empty();
    undoStack_.resize(base);
    for (auto& e : kept) undoStack_.push_back(std::move(e));
    if (!userEdits) redoStack_ = std::move(a.redoSaved);
}

int32_t Engine::retryTake() {
    if (!takeAttempt_.active) return 0;
    // The take already ended (its track was disarmed, say): it was kept — nothing to retry.
    if (!isRecording()) {
        closeTakeAttempt();
        return 0;
    }

    // waitScene >= 0: a Session MIDI take — the audio thread adopts the new epoch only when
    // that slot has relaunched, so nothing played while it waits enters the take.
    auto pushTakeRestart = [&](bool openGate, int32_t waitScene) {
        Command c;
        c.type = CommandType::TakeRestart;
        c.i0 = static_cast<int32_t>(takeEpoch_);
        c.i1 = (openGate ? 1 : 0) | (waitScene >= 0 ? 2 : 0);
        c.d0 = waitScene;
        commands_.push(c);
    };
    // Shut the capture, then move the capture epoch: a producer that sees the new epoch also
    // sees the gate shut, until the restart command opens it behind the transport moves.
    auto fenceCapture = [&]() {
        captureGate_.store(false, std::memory_order_relaxed);
        captureEpoch_.store(takeEpoch_, std::memory_order_release);
    };
    struct Retrying {
        Engine& e;
        explicit Retrying(Engine& en) : e(en) { e.retryingTake_ = true; }
        ~Retrying() { e.retryingTake_ = false; }
    };

    if (takeAttempt_.session) {
        const int32_t tid = takeAttempt_.sessionTrackId, sc = takeAttempt_.sessionScene;
        // The other slots the take still records start again with it (FR-007, specs/086).
        std::vector<std::pair<int32_t, int32_t>> others;
        for (const auto& p : takeAttempt_.sessionSlots)
            if (p.first != tid) others.push_back(p);
        // Still counting in from stop: nothing was recorded — start the count-in over, as Stop
        // and a fresh Record into the slot would.
        if (sessionCountIn_ && uiCountIn_.load(std::memory_order_relaxed) > 0.0) {
            sessionCountIn_ = false;
            countInLaunchScene_.store(-1, std::memory_order_release);
            cancelSessionCountIn();
            recordSessionSlot(tid, sc);
            return isRecording() ? 2 : -1;
        }
        const bool audio = recordSessionAudio_;
        discardTake();
        fenceCapture();
        // The take records again from the next launch quantum (now when launching is
        // unquantized); until then the slot shows it is waiting (slot state 5). The slot
        // relaunches whatever its launch mode (a Toggle slot would stop instead).
        const double q = launchQuant();
        const double pos = transport_.uiPositionBeats();
        const double at = q > 0.0 ? std::ceil(pos / q - 1e-9) * q : pos;
        {
            Retrying r(*this);
            forceSlotLaunch_ = true;
            recordSessionSlot(tid, sc);
            if (audio && isRecording())
                for (auto [otid, osc] : others) recordSessionSlot(otid, osc); // joins as a lane
            forceSlotLaunch_ = false;
        }
        pushTakeRestart(true, audio ? -1 : sc);
        if (!isRecording()) {
            captureGate_.store(true, std::memory_order_relaxed);
            return -1;
        }
        if (at > pos) retryStartBeat_ = at;
        return 2;
    }

    // Arrangement: back to where the transport was started for this take — its start when
    // the take rolled the transport, else where Play was pressed (the punch-in's run-up).
    const bool countedIn = takeAttempt_.countedIn;
    const bool startedTransport = recordStartedTransport_;
    const double restart = startedTransport ? recordReturnBeat_ : transport_.uiAnchorBeats();
    discardTake();
    fenceCapture(); // nothing until the playhead is back
    if (countedIn) {
        transportStop(); // count in again from stop, as the take did
        seekBeats(restart);
        pushTakeRestart(countInBars() <= 0, -1); // else the count-in's end opens the gate
    } else {
        seekBeats(restart); // keep rolling: the run-up plays again
        pushTakeRestart(true, -1);
    }
    bool ok;
    {
        Retrying r(*this);
        ok = startArrangementTake(!countedIn, restart, countedIn);
    }
    recordStartedTransport_ = startedTransport;
    recordReturnBeat_ = restart;
    if (!ok) {
        captureGate_.store(true, std::memory_order_relaxed);
        return -1;
    }
    return 1;
}

} // namespace nota
