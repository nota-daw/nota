// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SessionOps — block<->session transfers, scene metadata/rows, scene record, follow engine, and RT pre/post hooks.

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

// --- Arrangement clips -> session slots (a selection or the clip clipboard) -------------

int32_t SessionOps::arrangementClipsToSession(const std::vector<std::pair<int32_t, int32_t>>& sel, int32_t startScene) {
    double len = 0.0;
    return landBlockInSession(rt_.clips().captureBlock(sel, len), startScene);
}

int32_t SessionOps::pasteClipBlockToSession(int32_t destTrackId, int32_t startScene) {
    if (rt_.clips().clipboardBlock().empty() || !rt_.authoring_) return -1;
    return landBlockInSession(destTrackId >= 0 ? rt_.clips().remapBlock(rt_.clips().clipboardBlock(), destTrackId,
                                                     rt_.clips().clipboardBlockAnchor())
                                               : rt_.clips().clipboardBlock(),
        startScene);
}

int32_t SessionOps::sessionSlotsToArrangement(
    const std::vector<std::pair<int32_t, int32_t>>& slots, double atBeat, int32_t destTrackId) {
    rt_.clips().clearLastPlaced();
    if (!rt_.authoring_) return 0;
    std::map<int32_t, std::vector<int32_t>> byTrack; // track -> scenes, in order
    for (const auto& [tid, scene] : slots) byTrack[tid].push_back(scene);
    std::vector<BlockClip> items;
    for (auto& [tid, scenes] : byTrack) {
        auto t = rt_.findTrackAuthoring(tid);
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
    if (destTrackId >= 0) items = rt_.clips().remapBlock(items, destTrackId);
    if (items.empty()) return 0;
    rt_.clips().placeBlock(items, atBeat);
    return static_cast<int32_t>(rt_.clips().lastPlaced().size());
}

// Each track's clips, in time order, fill consecutive slots from one shared scene row, so
// clips that played together in the arrangement share a scene and a sequence reads down
// the column. One undo step, scene rows appended included.
int32_t SessionOps::landBlockInSession(const std::vector<BlockClip>& items, int32_t startScene) {
    if (!rt_.authoring_) return -1;
    std::map<int32_t, std::vector<const BlockClip*>> byTrack;
    for (const auto& b : items) {
        auto t = rt_.findTrackAuthoring(b.trackId);
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
                auto t = rt_.findTrackAuthoring(tid);
                for (int32_t i = 0; i < static_cast<int32_t>(v.size()); ++i)
                    if (const SessionSlot* sl = slotAt(*t, row + i); sl && sl->hasClip) return false;
            }
            return true;
        };
        first = 0;
        while (first < rt_.authoring_->sceneCount && !runFree(first)) ++first;
    }

    rt_.beginUndoGroup();
    while (rt_.authoring_->sceneCount < first + rows) insertScene(rt_.authoring_->sceneCount);
    for (const auto& [tid, v] : byTrack) {
        auto old = rt_.findTrackAuthoring(tid);
        if (!old) continue;
        auto nt = rt_.cloneTrack(*old);
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < rt_.authoring_->sceneCount)
            slots.resize(static_cast<size_t>(rt_.authoring_->sceneCount));
        for (int32_t i = 0; i < static_cast<int32_t>(v.size()); ++i) // the slots keep their next-take length (#503)
            replaceSlotContent(slots[static_cast<size_t>(first + i)],
                v[static_cast<size_t>(i)]->kind == 1 ? slotFromMidiClip(v[static_cast<size_t>(i)]->midi)
                                                     : slotFromAudioClip(v[static_cast<size_t>(i)]->audio));
        rt_.republishWithTrack(tid, nt);
    }
    rt_.endUndoGroup();
    return first;
}

bool SessionOps::sceneProps(int32_t scene, SceneProps* out) const {
    if (!out || scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    const SceneInfo si = sceneOr(*rt_.authoring_, scene);
    out->color = si.color;
    out->tempo = si.tempo;
    out->sigNum = si.sigNum;
    out->sigDen = si.sigDen;
    out->follow = si.follow ? 1 : 0;
    out->followBeats = si.followBeats;
    return true;
}

bool SessionOps::setSceneProps(int32_t scene, const SceneProps& p) {
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    auto g = std::make_shared<Graph>(*rt_.authoring_); // tracks shared; only scene metadata changes
    if (static_cast<int32_t>(g->scenes.size()) < g->sceneCount) g->scenes.resize(static_cast<size_t>(g->sceneCount));
    SceneInfo& si = g->scenes[static_cast<size_t>(scene)];
    si.color = p.color < 0 ? -1 : p.color;
    si.tempo = p.tempo > 0.0 ? std::clamp(p.tempo, 20.0, 999.0) : 0.0;
    const bool sig = p.sigNum > 0 && p.sigDen > 0;
    si.sigNum = sig ? std::clamp(p.sigNum, 1, 32) : 0;
    si.sigDen = sig ? std::clamp(p.sigDen, 1, 32) : 0;
    si.follow = p.follow != 0;
    si.followBeats = p.followBeats > 0.0 ? p.followBeats : 32.0;
    rt_.publish(std::move(g));
    return true;
}

bool SessionOps::sceneName(int32_t scene, std::string& out) const {
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    out = sceneOr(*rt_.authoring_, scene).name;
    return true;
}

bool SessionOps::setSceneName(int32_t scene, const std::string& name) {
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return false;
    if (sceneOr(*rt_.authoring_, scene).name == name) return true;
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    if (static_cast<int32_t>(g->scenes.size()) < g->sceneCount) g->scenes.resize(static_cast<size_t>(g->sceneCount));
    g->scenes[static_cast<size_t>(scene)].name = name;
    rt_.publish(std::move(g));
    return true;
}

// --- Session P0: scene rows ------------------------------------------------------

// Shift every player (and the recording / follow targets) after a row insert or removal.
// map(x) returns the row's new index, or -1 when it is gone. Players are atomics shared with
// the audio thread, so each is moved with a CAS: a launch racing the edit wins.
void SessionOps::remapPlayers(const std::function<int32_t(int32_t)>& map) {
    for (auto& t : rt_.authoring_->tracks) {
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
            sp.request.compare_exchange_strong(req,
                n < 0 ? SessionPlayer::kIdle : (req & ~0xFFFFFFFFull) | static_cast<uint32_t>(n), // keep gen + ticket
                std::memory_order_relaxed);
        }
        int32_t r = sp.repeatSlot.load(std::memory_order_relaxed);
        if (r >= 0) sp.repeatSlot.compare_exchange_strong(r, map(r), std::memory_order_relaxed);
    }
    if (rt_.recordSessionScene_ >= 0) {
        const int32_t n = map(rt_.recordSessionScene_);
        if (n >= 0) rt_.recordSessionScene_ = n;
    }
    for (auto& l : rt_.captureLanes_) // every slot a Session audio take records (specs/086)
        if (l.scene >= 0)
            if (const int32_t n = map(l.scene); n >= 0) l.scene = n;
    // Every slot a Session MIDI take records follows its row too, or its notes would land in
    // the slot that moved into the old one (specs/086); the audio thread's lane clocks with it.
    bool lanesMoved = false;
    for (auto& l : rt_.midiLanes_)
        if (l.scene >= 0)
            if (const int32_t n = map(l.scene); n >= 0 && n != l.scene) {
                l.scene = n;
                lanesMoved = true;
            }
    if (lanesMoved) rt_.recorder().publishSessionLaneSlots();
    if (rt_.takeAttempt_.sessionScene >= 0)
        if (const int32_t n = map(rt_.takeAttempt_.sessionScene); n >= 0) rt_.takeAttempt_.sessionScene = n;
    for (auto& [tid, sc] : rt_.takeAttempt_.sessionSlots) // a retry relaunches the slots where they are now
        if (sc >= 0)
            if (const int32_t n = map(sc); n >= 0) sc = n;
    if (rt_.sceneFollowScene_ >= 0) rt_.sceneFollowScene_ = map(rt_.sceneFollowScene_);
    if (prevFollowScene_ >= 0) prevFollowScene_ = map(prevFollowScene_);
    if (waitScene_ >= 0) waitScene_ = map(waitScene_);
    // Landings not served yet and the rule's launch still waiting follow their take's row (#481);
    // a take whose row was deleted is gone: it fires nothing, and its waiting launch is withdrawn.
    std::erase_if(rt_.landedTakes_, [&](auto& lt) {
        const int32_t n = map(lt.second);
        lt.second = n;
        return n < 0;
    });
    if (ruleWait_.gen != 0 && ruleWait_.scene >= 0) {
        ruleWait_.scene = map(ruleWait_.scene);
        if (ruleWait_.scene < 0) {
            if (waitSceneGen_ == ruleWait_.gen) cancelSceneLaunch(waitScene_);
            ruleWait_ = RuleWait{};
        }
    }
}

// Insert a row at `at`: empty, a copy of row `copyFrom` (old index), or each track's
// currently playing clip (capture). One undo step.
int32_t SessionOps::insertSceneRow(int32_t at, int32_t copyFrom, bool capturePlaying) {
    const int32_t count = rt_.authoring_->sceneCount;
    if (at < 0 || at > count || copyFrom >= count) return -1;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->sceneCount = count + 1;
    if (static_cast<int32_t>(g->scenes.size()) < count) g->scenes.resize(static_cast<size_t>(count));
    g->scenes.insert(g->scenes.begin() + at, copyFrom >= 0 ? g->scenes[static_cast<size_t>(copyFrom)] : SceneInfo{});
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        auto nt = rt_.cloneTrack(*t);
        auto& slots = nt->sessionSlots.write();
        if (static_cast<int32_t>(slots.size()) < count) slots.resize(static_cast<size_t>(count));
        SessionSlot row;
        if (copyFrom >= 0) row = slots[static_cast<size_t>(copyFrom)];
        else if (capturePlaying && t->sessionPlayer) {
            const int32_t p = t->sessionPlayer->playing.load(std::memory_order_relaxed);
            if (p >= 0 && p < count) row = slots[static_cast<size_t>(p)];
        }
        slots.insert(slots.begin() + at, row);
        g->tracks.push_back(nt);
    }
    remapLandingScenes(*g, [at](int32_t x) { return x >= at ? x + 1 : x; });
    rt_.publish(std::move(g));
    remapPlayers([at](int32_t x) { return x >= at ? x + 1 : x; });
    return at;
}

int32_t SessionOps::insertScene(int32_t at) {
    return insertSceneRow(at, -1, false);
}
int32_t SessionOps::duplicateScene(int32_t scene) {
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return -1;
    return insertSceneRow(scene + 1, scene, false);
}
int32_t SessionOps::captureScene(int32_t at) {
    return insertSceneRow(at, -1, true);
}

// Reorder a row: every track's slot and the scene's metadata move together; players follow.
bool SessionOps::moveScene(int32_t from, int32_t to) {
    const int32_t count = rt_.authoring_->sceneCount;
    if (from < 0 || from >= count || to < 0 || to >= count) return false;
    if (from == to) return true;
    auto move = [from, to](auto& v) {
        auto item = v[static_cast<size_t>(from)];
        v.erase(v.begin() + from);
        v.insert(v.begin() + to, item);
    };
    auto g = rebuildScenes(count, [&](std::vector<SessionSlot>& slots) {
        if (static_cast<int32_t>(slots.size()) < count) slots.resize(static_cast<size_t>(count));
        move(slots);
    });
    if (static_cast<int32_t>(g->scenes.size()) < count) g->scenes.resize(static_cast<size_t>(count));
    move(g->scenes);
    auto rowMap = [from, to](int32_t x) {
        if (x == from) return to;
        if (from < to && x > from && x <= to) return x - 1;
        if (to < from && x >= to && x < from) return x + 1;
        return x;
    };
    remapLandingScenes(*g, rowMap);
    rt_.publish(std::move(g));
    remapPlayers(rowMap);
    return true;
}

// --- Session P0: recording ---------------------------------------------------------

int32_t SessionOps::recordSessionScene(int32_t scene) {
    if (scene < 0 || scene >= rt_.authoring_->sceneCount) return 0;
    for (auto& t : rt_.authoring_->tracks) {
        if (!t->armed() || !sessionLaunchable(*t)) continue;
        const SessionSlot* s = slotAt(*t, scene);
        if (s && s->hasClip) continue;
        recordSessionSlot(t->id(), scene);
        return t->id();
    }
    return 0;
}

bool SessionOps::sessionRecordTarget(int32_t* trackId, int32_t* scene, double* elapsed) const {
    if (!rt_.recording_.load(std::memory_order_relaxed) || rt_.recordSessionScene_ < 0 ||
        rt_.recordSessionTrackId_ <= 0)
        return false;
    double el = 0.0;
    if (rt_.recordSessionAudio_) // a retried take counts from the boundary it records from (specs/078)
        el = std::max(0.0,
            rt_.transport_.uiPositionBeats() - (std::isnan(rt_.retryStartBeat_)
                                                       ? rt_.audioRecordStartBeat_
                                                       : std::max(rt_.audioRecordStartBeat_, rt_.retryStartBeat_)));
    else if (auto* sp = rt_.recordSlotPlayer_.load(std::memory_order_acquire))
        if (sp->playing.load(std::memory_order_relaxed) == rt_.recordSessionScene_)
            el = sp->playedBeats.load(std::memory_order_relaxed);
    if (trackId) *trackId = rt_.recordSessionTrackId_;
    if (scene) *scene = rt_.recordSessionScene_;
    if (elapsed) *elapsed = el;
    return true;
}

// poll(): fixed-length capture ends a take by itself, and a following scene queues the next
// row one launch quantum ahead so it lands on the boundary where this one's time runs out.
bool SessionOps::takeSceneTempoChange(double* bpm, int32_t* num, int32_t* den) {
    if (!sceneTempoApplied_) return false;
    sceneTempoApplied_ = false;
    if (bpm) *bpm = sceneTempo_;
    if (num) *num = sceneSigNum_;
    if (den) *den = sceneSigDen_;
    return true;
}

void SessionOps::applySceneTempo() {
    sceneTempoPending_ = false;
    sceneTempoApplied_ = true; // the UI's transport picks the new values up (takeSceneTempoChange)
    // #971: a scene tempo heard in a pass goes to the Tempo lane when the pass ends — only while
    // no Tempo lane leads (then setBpm below is refused and nothing changed).
    if (rt_.sessionPass_.active && sceneTempo_ > 0.0 && !rt_.tempoMap())
        rt_.sessionPass_.tempoSteps.emplace_back(sceneTempoAt_, sceneTempo_);
    if (sceneTempo_ > 0.0) rt_.setBpm(sceneTempo_);
    if (sceneSigNum_ > 0 && sceneSigDen_ > 0) rt_.setTimeSignature(sceneSigNum_, sceneSigDen_);
}

void SessionOps::serviceSessionFollow() {
    // A scene launch still waiting for its boundary holds back its tempo and follow (#480).
    bool waiting = sceneLaunchWaiting();
    serviceLandingRules();                  // takes landed since the last poll (#481)
    if (ruleWait_.gen != 0) waiting = true; // a rule's launch was just queued: it waits for its boundary
    if (!waiting && sceneTempoPending_ && rt_.transport_.uiPositionBeats() + 1e-6 >= sceneTempoAt_) applySceneTempo();
    // Fixed-length capture: each slot of the take (several with specs/086) stops when its own
    // length — resolved from its slot when it started (#503) — is recorded. Landing the last one
    // ends the take (stopSessionRecordSlot), so one slot never cuts another short.
    double el = 0.0;
    if (sessionRecordTarget(nullptr, nullptr, &el)) {
        std::vector<int32_t> done;
        if (!rt_.recordSessionAudio_) {
            for (auto& l : rt_.midiLanes_) // a slot seen recording has started: a stop keeps its take
                if (l.scene >= 0 && l.player && l.player->playing.load(std::memory_order_relaxed) == l.scene)
                    l.started = true;
            for (const auto& l : rt_.midiLanes_)
                if (l.trackId >= 0 && l.scene >= 0 && l.lenBeats > 0.0 && l.player &&
                    l.player->playing.load(std::memory_order_relaxed) == l.scene &&
                    l.player->playedBeats.load(std::memory_order_relaxed) >= l.lenBeats - 1e-6)
                    done.push_back(l.trackId);
        } else {
            const double now = rt_.transport_.uiPositionBeats();
            for (const auto& l : rt_.captureLanes_) {
                if (l.scene < 0 || l.lenBeats <= 0.0) continue;
                // A slot that joined later counts from its own start; the others from the take's.
                const double lane = std::isnan(l.startBeat) ? el : now - l.startBeat;
                if (lane >= l.lenBeats - 1e-6) done.push_back(l.trackId);
            }
        }
        rt_.takeLengthReached_ = true; // they land at their length: nothing to cut (#503)
        for (int32_t tid : done) stopSessionRecordSlot(tid);
        rt_.takeLengthReached_ = false;
    }
    if (!rt_.transport_.uiIsPlaying()) {
        rt_.sceneFollowScene_ = -1;
        return;
    }
    const int32_t sc = rt_.sceneFollowScene_;
    if (waiting || sc < 0 || sc >= rt_.authoring_->sceneCount || !sessionFollow()) return;
    const SceneInfo si = sceneOr(*rt_.authoring_, sc);
    if (!si.follow || si.followBeats <= 0.0) return;
    const double q = rt_.launchQuant_.load(std::memory_order_relaxed);
    const double lead = q > 0.0 ? std::min(q, si.followBeats) : 0.0;
    const double due = sceneFollowStart_ + si.followBeats;
    if (rt_.transport_.uiPositionBeats() + lead + 1e-6 < due) return;
    launchScene((sc + 1) % rt_.authoring_->sceneCount, true);
    sceneFollowStart_ = due; // exactly where the outgoing scene's time ran out
}

// Peaks of an audio slot's take (whole sample) for the grid / inspector preview.
int32_t SessionOps::sessionSlotPeaks(int32_t trackId, int32_t scene, float* outMinMax, int32_t maxPoints) const {
    return rt_.clips().getClipPeaks(trackId, sessionClipIndex(scene), outMinMax, maxPoints);
}

// --- Session P0: audio thread ---------------------------------------------------

int32_t SessionOps::followTarget(const Track& t, int32_t cur, FollowAction a) {
    const int32_t n = static_cast<int32_t>(t.sessionSlots.size());
    auto filled = [&](int32_t i) { return i >= 0 && i < n && t.sessionSlots[static_cast<size_t>(i)].hasClip; };
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
        const int32_t j = t.sessionSlots[static_cast<size_t>(cur)].jumpScene;
        return filled(j) ? j : -1;
    }
    default: return cur;
    }
}

void SessionOps::sessionPreApply(Track& t, double blockBeats) {
    auto& sp = *t.sessionPlayer;
    const int32_t cur = sp.playing.load(std::memory_order_relaxed);
    if (cur < 0 || cur >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[static_cast<size_t>(cur)];

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
        double q = s.quantBeats >= 0.0 ? s.quantBeats : rt_.launchQuant_.load(std::memory_order_relaxed);
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

void SessionOps::sessionPostRender(Track& t, double blockBeats) {
    auto& sp = *t.sessionPlayer;
    sp.publish(blockBeats);
    const int32_t cur = sp.playing.load(std::memory_order_relaxed);
    if (cur < 0 || cur >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[static_cast<size_t>(cur)];
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

} // namespace nota::detail
