// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationOps — master tempo/volume/pan lanes, clip envelopes, and clip CC curves.

#include "automation/AutomationOps.h"
#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"
#include "core/TempoMap.h"
#include "model/MidiClip.h"
#include "model/Track.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota::detail {

// --- master Tempo lane -> tempo map (specs/099) -----------------------------

int32_t AutomationOps::tempoLaneSelfTest() {
    auto storage = std::make_unique<EngineRuntime>();
    EngineRuntime& e = *storage;
    e.transport_.setSampleRate(48000);
    e.transport_.setBpm(120);
    e.authoringBpm_ = 120;
    const int32_t master = kMasterTrackId;
    const int32_t tempoTarget = static_cast<int32_t>(AutomationTarget::Tempo);
    auto lanePoints = [&]() -> std::vector<AutomationPoint> {
        const TrackLane* l = rt_.masterTempoLane(*e.authoring_);
        return l ? l->points : std::vector<AutomationPoint>{};
    };
    auto inSync = [&]() {
        const auto pts = lanePoints();
        const TempoMap* m = e.tempoMap();
        if (pts.empty()) return m == nullptr;
        if (!m || m->source.size() != pts.size()) return false;
        for (size_t i = 0; i < pts.size(); ++i)
            if (m->source[i].beat != pts[i].beat || m->source[i].value != pts[i].value ||
                m->source[i].curve != pts[i].curve)
                return false;
        return true;
    };
    auto set = [&](int32_t lane, std::initializer_list<AutomationPointData> pts, bool live = false) {
        std::vector<AutomationPointData> v(pts);
        return live ? e.automation().setAutomationPointsLive(master, lane, v.data(), static_cast<int32_t>(v.size()))
                    : e.automation().setAutomationPoints(master, lane, v.data(), static_cast<int32_t>(v.size()));
    };

    // 0: Tempo is master-only.
    const int32_t audio = e.tracks().addAudioTrack();
    if (e.automation().addAutomationLane(audio, tempoTarget, -1, -1) != -1) return 1;
    const int32_t lane = e.automation().addAutomationLane(master, tempoTarget, -1, -1);
    if (lane < 0 || e.tempoAutomated()) return 2; // an empty lane is no map

    // 1: the map follows the lane's own curve exactly.
    if (!set(lane, {{0, 120, 0.5f}, {16, 90, 0}})) return 3;
    TrackLane ref;
    ref.target = AutomationTarget::Tempo;
    ref.points = lanePoints();
    for (int i = 0; i <= 64; ++i) {
        const double b = 16.0 * i / 64;
        if (std::fabs(e.tempoMap()->bpmAt(b) - ref.valueAt(b)) > 1e-3) return 4;
    }
    if (!e.tempoAutomated() || !inSync()) return 5;
    if (e.setBpm(100)) return 6; // the transport tempo is refused while automated

    // 2: a same-beat pair is a jump; 3: a first point after 0 holds back to 0.
    if (!set(lane, {{8, 100, 0}, {8, 140, 0}})) return 7;
    const TempoMap* m = e.tempoMap();
    if (!m || std::fabs(m->bpmAt(0) - 100) > 1e-9 || std::fabs(m->bpmAt(7.999) - 100) > 1e-9 ||
        std::fabs(m->bpmAt(8.001) - 140) > 1e-9)
        return 8;
    if (std::fabs(e.beatToSeconds(8) - 4.8) > 1e-9 || std::fabs(e.beatToSeconds(12) - (4.8 + 4 * 60.0 / 140)) > 1e-9)
        return 9;

    // 4: values clamp to 20..300 on set.
    if (!set(lane, {{0, 10, 0}, {4, 400, 0}})) return 10;
    const auto clamped = lanePoints();
    if (clamped.size() != 2 || clamped[0].value != 20.0f || clamped[1].value != 300.0f) return 11;

    // 5: a drag as the editor does it — one checkpointed set, then live sets with no undo
    // step — re-derives on every set; undo/redo restore lane + map together.
    if (!set(lane, {{0, 120, 0}, {8, 60, 0}})) return 12;
    const double before = e.beatToSeconds(8);
    if (!set(lane, {{0, 120, 0}, {8, 180, 0}})) return 13;
    if (!set(lane, {{0, 120, 0}, {8, 200, 0}}, true) || !inSync() ||
        std::fabs(e.beatToSeconds(8) - 6.0 * std::log(200.0 / 120.0)) > 1e-7)
        return 14; // linear BPM: 60·8/80·ln(200/120)
    const double after = e.beatToSeconds(8);
    if (!e.undo() || !inSync() || std::fabs(e.beatToSeconds(8) - before) > 1e-12) return 15;
    if (!e.redo() || !inSync() || std::fabs(e.beatToSeconds(8) - after) > 1e-12) return 16;

    // 6: an unwarped clip keeps its physical time when only the tempo changes.
    {
        auto t = e.findTrackAuthoring(audio);
        auto nt = e.cloneTrack(*t);
        AudioClip c;
        c.startBeat = 16;
        c.warpEnabled = false;
        nt->clips.write().push_back(c);
        e.republishWithTrack(audio, nt);
    }
    const double clipSeconds = e.beatToSeconds(16);
    if (!set(lane, {{0, 120, 0}, {8, 90, -0.4f}})) return 17;
    const auto placed = e.findTrackAuthoring(audio)->clips.back().startBeat;
    if (std::fabs(e.beatToSeconds(placed) - clipSeconds) > 1e-9) return 18;

    // 7: a section move leaves the Tempo lane (the ruler) where it is, and the map with it.
    e.setSections({{0, 8, "A"}, {8, 8, "B"}}, false);
    const auto lanePointsBefore = lanePoints();
    const TempoMap* mapBefore = e.tempoMap();
    if (!e.clips().moveSection(1, 0) || !inSync() || e.tempoMap() != mapBefore ||
        lanePoints().size() != lanePointsBefore.size())
        return 19;

    // 8: removing the lane (or clearing it) removes the map; the transport tempo leads again.
    if (!e.automation().removeAutomationLane(master, lane) || e.tempoMap() || e.tempoAutomated()) return 20;
    if (!e.setBpm(100)) return 21;
    return 0;
}

// --- master-volume automation (graph-level, M9 follow-up) ------------------

int32_t AutomationOps::masterVolumeAutomationCount() const {
    return rt_.authoring_ ? static_cast<int32_t>(rt_.authoring_->masterVolume.points.size()) : 0;
}

int32_t AutomationOps::getMasterVolumeAutomation(AutomationPointData* out, int32_t cap) const {
    if (!rt_.authoring_) return 0;
    const auto& pts = rt_.authoring_->masterVolume.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[static_cast<size_t>(i)].beat;
        out[i].value = pts[static_cast<size_t>(i)].value;
        out[i].curve = pts[static_cast<size_t>(i)].curve;
    }
    return n;
}

bool AutomationOps::setMasterVolumeAutomation(const AutomationPointData* pts, int32_t count, bool live) {
    if (!rt_.authoring_) return false;
    auto g = std::make_shared<Graph>(*rt_.authoring_); // copy tracks (shared) + sceneCount + masterVolume
    auto& lane = g->masterVolume;
    lane.points.clear();
    lane.points.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    if (live) rt_.publishRaw(std::move(g)); // a drag's later moves: heard at once, no undo step
    else
        rt_.publish(std::move(g)); // structural edit → undo checkpoint
    return true;
}

// --- per-clip volume envelope (M9 follow-up) -------------------------------

int32_t AutomationOps::getAudioClipVolumeEnvelope(
    int32_t trackId, int32_t clipIndex, AutomationPointData* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (!audioClipRef(*t, clipIndex)) return 0;
    const auto& pts = (*audioClipRef(*t, clipIndex)).volumeEnvelope.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[static_cast<size_t>(i)].beat;
        out[i].value = pts[static_cast<size_t>(i)].value;
        out[i].curve = pts[static_cast<size_t>(i)].curve;
    }
    return n;
}

bool AutomationOps::setAudioClipVolumeEnvelope(
    int32_t trackId, int32_t clipIndex, const AutomationPointData* pts, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = rt_.cloneTrack(*old);
    auto& lane = (*audioClipWrite(*nt, clipIndex)).volumeEnvelope;
    lane.points.clear();
    lane.points.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt); // structural edit → undo checkpoint
    return true;
}

int32_t AutomationOps::getAudioClipPanEnvelope(
    int32_t trackId, int32_t clipIndex, AutomationPointData* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (!audioClipRef(*t, clipIndex)) return 0;
    const auto& pts = (*audioClipRef(*t, clipIndex)).panEnvelope.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[static_cast<size_t>(i)].beat;
        out[i].value = pts[static_cast<size_t>(i)].value;
        out[i].curve = pts[static_cast<size_t>(i)].curve;
    }
    return n;
}

bool AutomationOps::setAudioClipPanEnvelope(
    int32_t trackId, int32_t clipIndex, const AutomationPointData* pts, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = rt_.cloneTrack(*old);
    auto& lane = (*audioClipWrite(*nt, clipIndex)).panEnvelope;
    lane.points.clear();
    lane.points.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    rt_.session().syncSessionAudioSlot(*nt, clipIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t AutomationOps::getMidiClipEnvelope(
    int32_t trackId, int32_t clipIndex, int32_t kind, AutomationPointData* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    const MidiClip& c = t->midiClips[static_cast<size_t>(clipIndex)];
    const auto& pts = (kind == 1 ? c.volumeEnvelope : c.velocityEnvelope).points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[static_cast<size_t>(i)].beat;
        out[i].value = pts[static_cast<size_t>(i)].value;
        out[i].curve = pts[static_cast<size_t>(i)].curve;
    }
    return n;
}

bool AutomationOps::setMidiClipEnvelope(
    int32_t trackId, int32_t clipIndex, int32_t kind, const AutomationPointData* pts, int32_t count) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    auto nt = rt_.cloneTrack(*old);
    auto& clip = nt->midiClips.write()[static_cast<size_t>(clipIndex)];
    auto& lane = (kind == 1 ? clip.volumeEnvelope : clip.velocityEnvelope);
    lane.points.clear();
    lane.points.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// --- clip controller curves (specs/020) ---------------------------------------
static const MidiClip* ctlClipOf(const Track& t, bool slot, int32_t index) {
    if (slot) {
        if (index < 0 || index >= static_cast<int32_t>(t.sessionSlots.size()) ||
            !t.sessionSlots[static_cast<size_t>(index)].hasClip)
            return nullptr;
        return &t.sessionSlots[static_cast<size_t>(index)].midi;
    }
    if (index < 0 || index >= static_cast<int32_t>(t.midiClips.size())) return nullptr;
    return &t.midiClips[static_cast<size_t>(index)];
}

int32_t AutomationOps::ctlList(int32_t trackId, bool slot, int32_t index, int32_t* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const MidiClip* c = t ? ctlClipOf(*t, slot, index) : nullptr;
    if (!c) return 0;
    int32_t n = 0;
    for (const auto& cv : c->controllers) {
        if (!cv) continue;
        if (out && n < cap) out[n] = cv->ctl;
        ++n;
    }
    return out ? std::min(n, cap) : n;
}

int32_t AutomationOps::ctlGet(
    int32_t trackId, bool slot, int32_t index, int32_t ctl, AutomationPointData* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const MidiClip* c = t ? ctlClipOf(*t, slot, index) : nullptr;
    const ControllerCurve* cv = c ? c->controller(ctl) : nullptr;
    if (!cv) return 0;
    const auto& pts = cv->points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[static_cast<size_t>(i)].beat;
        out[i].value = pts[static_cast<size_t>(i)].value;
        out[i].curve = pts[static_cast<size_t>(i)].curve;
    }
    return n;
}

bool AutomationOps::ctlSet(
    int32_t trackId, bool slot, int32_t index, int32_t ctl, const AutomationPointData* pts, int32_t count) {
    if (ctlIsReserved(ctl) || count < 0 || (count > 0 && !pts)) return false;
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !ctlClipOf(*old, slot, index)) return false;
    auto nt = rt_.cloneTrack(*old);
    MidiClip& c = slot ? nt->sessionSlots.write()[static_cast<size_t>(index)].midi
                       : nt->midiClips.write()[static_cast<size_t>(index)];
    std::vector<AutomationPoint> p;
    p.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i)
        p.push_back({pts[i].beat, ctlClamp(ctl, pts[i].value), std::clamp(pts[i].curve, -1.0f, 1.0f)});
    c.setController(ctl, std::move(p));
    rt_.republishWithTrack(trackId, nt);
    return true;
}

float AutomationOps::deliveredController(int32_t trackId, int32_t ctl) const {
    if (ctl < 0 || ctl >= kCtlCount) return std::numeric_limits<float>::quiet_NaN();
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument) return std::numeric_limits<float>::quiet_NaN();
    return t->instrument->deliveredCtl[ctl].load(std::memory_order_relaxed);
}

// Device-free checks of the controller curves (specs/020): numbered, 0 = pass. Runs on the
// message thread with private state (the fold scratch is saved and restored).
int32_t AutomationOps::midiCtlSelfTest() {
    auto near = [](float a, float b) { return std::fabs(a - b) < 1e-3f; };
    ControllerCurve sus;
    sus.ctl = kCtlSustain;
    sus.points = {{1.0, 1.0f, 0.0f}, {3.0, 0.0f, 0.0f}};
    ControllerCurve mod;
    mod.ctl = kCtlModWheel;
    mod.points = {{0.0, 0.0f, 0.0f}, {2.0, 1.0f, 0.0f}};
    // 1-2: value at a beat — a switch steps, the rest interpolate.
    if (!near(sus.valueAt(2.0), 1.0f) || !near(sus.valueAt(2.999), 1.0f) || !near(sus.valueAt(3.5), 0.0f)) return 1;
    if (!near(mod.valueAt(1.0), 0.5f) || !near(mod.valueAt(5.0), 1.0f)) return 2;
    // 3: a slice pins the value at its start, keeps its points, drops the rest.
    {
        auto p = sliceCtlPoints(sus, 2.0, 4.0);
        if (p.size() != 2 || !near(p[0].value, 1.0f) || p[0].beat != 0.0 || p[1].beat != 1.0 || !near(p[1].value, 0.0f))
            return 3;
        if (!sliceCtlPoints(sus, 0.0, 0.5).empty()) return 3; // before its first point: nothing
    }
    // 4: the playback wire — a clip starting in the block sends its values from there; a switch
    // whose first point lies later sends nothing until it; points land on their sample.
    {
        MidiClip c;
        c.lengthBeats = 4.0;
        c.setController(kCtlSustain, sus.points);
        c.setController(kCtlModWheel, mod.points);
        ExprEv ev[256];
        int n = 0;
        rt_.clipCtlEvents(c, 0.0, 400.0, 0.0, 256, 100.0, false, ev, n, 256); // spb 100: beat 1 = sample 100
        bool modStart = false, susAt100 = false, susAtStart = false, modMoves = false;
        for (int i = 0; i < n; ++i) {
            if (ev[i].dim == kCtlDimBase + kCtlModWheel && ev[i].off == 0 && near(ev[i].value, 0.0f)) modStart = true;
            if (ev[i].dim == kCtlDimBase + kCtlModWheel && ev[i].off == 128 && near(ev[i].value, 0.64f))
                modMoves = true;
            if (ev[i].dim == kCtlDimBase + kCtlSustain && ev[i].off == 100 && near(ev[i].value, 1.0f)) susAt100 = true;
            if (ev[i].dim == kCtlDimBase + kCtlSustain && ev[i].off == 0) susAtStart = true;
        }
        if (!modStart || !susAt100 || susAtStart || !modMoves) return 4;
        // 5: chase — a jump to beat 2.5 sends the pedal down and the wheel up there.
        n = 0;
        rt_.clipCtlEvents(c, 0.0, 400.0, 250.0, 64, 100.0, true, ev, n, 256);
        bool chSus = false, chMod = false;
        for (int i = 0; i < n; ++i) {
            if (ev[i].off == 0 && ev[i].dim == kCtlDimBase + kCtlSustain && near(ev[i].value, 1.0f)) chSus = true;
            if (ev[i].off == 0 && ev[i].dim == kCtlDimBase + kCtlModWheel && near(ev[i].value, 1.0f)) chMod = true;
        }
        if (!chSus || !chMod) return 5;
        // 6: a clip that ends with the pedal down releases it there; the mod wheel stays.
        MidiClip d;
        d.lengthBeats = 2.0;
        d.setController(kCtlSustain, {{0.0, 1.0f, 0.0f}});
        d.setController(kCtlModWheel, {{0.0, 0.7f, 0.0f}});
        n = 0;
        rt_.clipCtlEndResets(d, 0.0, 200.0, 150.0, 100, ev, n, 256);
        if (n != 1 || ev[0].off != 50 || ev[0].dim != kCtlDimBase + kCtlSustain || ev[0].value != kCtlRest) return 6;
    }
    // 7-8: the recording fold.
    auto savedFold = rt_.ctlFold_;
    auto savedBatch = rt_.ctlBatch_;
    auto savedTake = rt_.takeCtls_;
    const bool savedStop = rt_.ctlStopPending_;
    const double savedStopAt = rt_.ctlStopAt_;
    int32_t fail = 0;
    {
        // 7: overdub — pass 1 moves the mod wheel from beat 2: it replaces from there to the
        // pass end; pass 0's earlier point stays.
        for (auto& f : rt_.ctlFold_) f = CtlFold{};
        rt_.ctlBatch_.clear();
        rt_.takeCtls_.clear();
        rt_.ctlStopPending_ = false;
        MidiClip c;
        c.lengthBeats = 4.0;
        c.setController(kCtlModWheel, {{0.2, 0.3f, 0.0f}, {2.5, 0.3f, 0.0f}, {3.5, 0.1f, 0.0f}});
        rt_.ctlBatch_ = {{kCtlModWheel, 2.0, 0.9f, 1}, {kCtlModWheel, 3.0, 0.9f, 1}, {-1, 4.0, 0.0f, 1}};
        rt_.recorder().foldRecordedCtl(c, 0.0);
        const ControllerCurve* m = c.controller(kCtlModWheel);
        if (!m || !near(m->valueAt(1.0), 0.3f) || !near(m->valueAt(3.0), 0.9f) || !near(m->valueAt(3.8), 0.9f))
            fail = 7;
        // 8: a take that stops at beat 4 hands back to what the clip held after it.
        if (!fail) {
            for (auto& f : rt_.ctlFold_) f = CtlFold{};
            MidiClip e;
            e.lengthBeats = 8.0;
            e.setController(kCtlModWheel, {{0.0, 0.2f, 0.0f}, {6.0, 0.2f, 0.0f}});
            rt_.ctlBatch_ = {{kCtlModWheel, 2.0, 0.8f, 0}, {kCtlModWheel, 3.0, 0.8f, 0}};
            rt_.ctlStopPending_ = true;
            rt_.ctlStopAt_ = 4.0;
            rt_.recorder().foldRecordedCtl(e, 0.0);
            const ControllerCurve* w = e.controller(kCtlModWheel);
            if (!w || !near(w->valueAt(1.0), 0.2f) || !near(w->valueAt(2.5), 0.8f) || !near(w->valueAt(4.0), 0.2f) ||
                !near(w->valueAt(7.0), 0.2f))
                fail = 8;
        }
        // 9: the same take over a curved segment (#591): the old curve keeps its shape before
        // the take and from the hand-back at beat 4 on, not a straight line.
        if (!fail) {
            for (auto& f : rt_.ctlFold_) f = CtlFold{};
            ControllerCurve was;
            was.ctl = kCtlModWheel;
            was.points = {{0.0, 0.0f, -0.8f}, {8.0, 1.0f, 0.0f}};
            MidiClip e;
            e.lengthBeats = 8.0;
            e.setController(kCtlModWheel, was.points);
            rt_.ctlBatch_ = {{kCtlModWheel, 2.0, 0.8f, 0}, {kCtlModWheel, 3.0, 0.8f, 0}};
            rt_.ctlStopPending_ = true;
            rt_.ctlStopAt_ = 4.0;
            rt_.recorder().foldRecordedCtl(e, 0.0);
            const ControllerCurve* w = e.controller(kCtlModWheel);
            if (!w) fail = 9;
            else
                for (double b : {0.5, 1.0, 1.9, 4.5, 5.0, 6.0, 7.0, 7.9})
                    if (!near(w->valueAt(b), was.valueAt(b))) fail = 9;
        }
        // 10: a slice of a curved segment keeps its shape at both edges.
        if (!fail) {
            ControllerCurve curve;
            curve.ctl = kCtlModWheel;
            curve.points = {{0.0, 0.0f, 0.7f}, {4.0, 1.0f, -0.5f}, {8.0, 0.2f, 0.0f}};
            const double from = 1.0, to = 6.0;
            ControllerCurve s;
            s.ctl = kCtlModWheel;
            s.points = sliceCtlPoints(curve, from, to);
            for (double b = from; b <= to; b += 0.25)
                if (!near(s.valueAt(b - from), curve.valueAt(b))) fail = 10;
        }
    }
    rt_.ctlFold_ = savedFold;
    rt_.ctlBatch_ = savedBatch;
    rt_.takeCtls_ = savedTake;
    rt_.ctlStopPending_ = savedStop;
    rt_.ctlStopAt_ = savedStopAt;
    return fail;
}

float AutomationOps::masterPan() const {
    auto m = rt_.findTrackAuthoring(kMasterTrackId);
    return m ? m->pan() : 0.0f;
}

// specs/099 PR 2: master Pan and master device lanes play back; the master's Volume stays
// the graph-level lane; removing a master device drops its lanes and undo brings both back.
int32_t AutomationOps::masterAutomationSelfTest() {
    auto storage = std::make_unique<EngineRuntime>();
    EngineRuntime& e = *storage;
    const int32_t m = kMasterTrackId;
    const int32_t panT = static_cast<int32_t>(AutomationTarget::Pan);
    const int32_t devT = static_cast<int32_t>(AutomationTarget::DeviceParam);
    auto set = [&](int32_t lane, float a, float b) {
        AutomationPointData pts[2] = {{0.0, a, 0.0f}, {4.0, b, 0.0f}};
        return e.automation().setAutomationPoints(m, lane, pts, 2);
    };
    auto near = [](float a, float b) { return std::fabs(a - b) <= 1e-4f; };

    // 1: the master has no Volume / MIDI-effect lane (Volume is the graph-level lane).
    if (e.automation().addAutomationLane(m, static_cast<int32_t>(AutomationTarget::Volume), -1, -1) != -1) return 1;
    if (e.automation().addAutomationLane(m, static_cast<int32_t>(AutomationTarget::MidiDeviceParam), 0, 0) != -1)
        return 1;
    // 2: setter / getter clamp to -1..1.
    e.setMasterPan(-3.0f);
    if (!near(e.automation().masterPan(), -1.0f)) return 2;
    e.setMasterPan(0.25f);
    if (!near(e.automation().masterPan(), 0.25f)) return 2;
    // 3: a master Pan lane drives the master pan.
    const int32_t pan = e.automation().addAutomationLane(m, panT, -1, -1);
    if (pan < 0 || !set(pan, -1.0f, 1.0f)) return 3;
    e.automation().applyAutomation(e.authoring_.get(), 2.0);
    if (!near(e.automation().masterPan(), 0.0f)) return 3;
    e.automation().applyAutomation(e.authoring_.get(), 0.0);
    if (!near(e.automation().masterPan(), -1.0f)) return 3;
    // 4: a master device parameter lane drives the master device.
    const int32_t dev = e.devices().addTrackBuiltinDevice(m, 0); // EQ
    if (dev < 0) return 4;
    const int32_t dl = e.automation().addAutomationLane(m, devT, dev, 1);
    if (dl < 0 || !set(dl, 0.1f, 0.9f)) return 4;
    e.automation().applyAutomation(e.authoring_.get(), 0.0);
    const float p0 = e.devices().deviceGetParam(m, dev, 1);
    e.automation().applyAutomation(e.authoring_.get(), 4.0);
    const float p4 = e.devices().deviceGetParam(m, dev, 1);
    if (p0 == p4) return 4;
    // 5: removing the master device drops its lane; undo restores device and lane.
    if (e.automation().automationLaneCount(m) != 2) return 5;
    if (!e.devices().removeDevice(m, dev) || e.automation().automationLaneCount(m) != 1 ||
        e.devices().trackDeviceCount(m) != 0)
        return 5;
    if (!e.undo() || e.automation().automationLaneCount(m) != 2 || e.devices().trackDeviceCount(m) != 1) return 6;
    return 0;
}

} // namespace nota::detail
