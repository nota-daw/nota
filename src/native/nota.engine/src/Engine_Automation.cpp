// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — parameter automation (M9): read/apply, lane CRUD, hosted-plugin parameter surface, contextual write recording, self-tests.

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

// --- parameter automation (M9) ---------------------------------------------
void Engine::applyAutomation(Graph* g, double beat) {
    for (auto& tptr : g->tracks) {
        Track& t = *tptr;
        for (const AutomationLane& lane : t.automation) {
            if (lane.points.empty() || lane.suppressRead) continue; // M9-C: control leads while writing
            const float v = lane.valueAt(beat);
            switch (lane.target) {
                case AutomationTarget::Volume: t.setVolume(v); break;
                case AutomationTarget::Pan:    t.setPan(v);    break;
                case AutomationTarget::DeviceParam:
                    if (lane.deviceIndex >= 0 &&
                        lane.deviceIndex < static_cast<int32_t>(t.devices.size()))
                        if (auto* d = t.devices[lane.deviceIndex].get())
                            d->setParam(lane.paramIndex, v);
                    break;
                case AutomationTarget::MidiDeviceParam:   // deviceIndex = MIDI-FX chain index
                    if (lane.deviceIndex >= 0 &&
                        lane.deviceIndex < static_cast<int32_t>(t.midiEffects.size()))
                        if (auto* m = t.midiEffects[lane.deviceIndex].get())
                            m->setParam(lane.paramIndex, v);
                    break;
                case AutomationTarget::PluginParam:
                    // Normalized 0..1 into a hosted plugin (M9-B). deviceIndex < 0
                    // targets the instrument; paramIndex is pre-resolved from paramId.
                    if (lane.paramIndex < 0) break;
                    if (lane.deviceIndex < 0) {
                        if (t.instrument) t.instrument->pluginParamSet(lane.paramIndex, v);
                    } else if (lane.deviceIndex < static_cast<int32_t>(t.devices.size())) {
                        if (auto* d = t.devices[lane.deviceIndex].get())
                            d->pluginParamSet(lane.paramIndex, v);
                    }
                    break;
            }
        }
    }
    // Master-volume lane (graph-level, M9 follow-up): write the master gain atomic.
    if (!g->masterVolume.points.empty() && !g->masterVolume.suppressRead)
        masterVolume_.store(std::clamp(g->masterVolume.valueAt(beat), 0.0f, 2.0f), std::memory_order_relaxed);
}

bool Engine::automationSelfTest() {
    // Volume lane: 1.0 at beat 0, 0.0 at beat 4 -> 0.5 at beat 2; hold at ends.
    AutomationLane lane;
    lane.target = AutomationTarget::Volume;
    lane.points = {{0.0, 1.0f}, {4.0, 0.0f}};
    if (std::fabs(lane.valueAt(2.0)  - 0.5f) > 1e-4f) return false;
    if (std::fabs(lane.valueAt(-1.0) - 1.0f) > 1e-4f) return false; // hold before first
    if (std::fabs(lane.valueAt(10.0) - 0.0f) > 1e-4f) return false; // hold after last

    // Block-rate apply writes the track's volume atomic (device-free graph).
    auto g = std::make_shared<Graph>();
    auto t = std::make_shared<Track>(9999, TrackType::Instrument);
    t->setVolume(1.0f);
    t->automation.push_back(lane);
    g->tracks.push_back(t);
    applyAutomation(g.get(), 2.0);
    return std::fabs(t->volume() - 0.5f) <= 1e-4f;
}

bool Engine::automationCurveSelfTest() {
    // Segment 0->4, value 0->1. At the midpoint (beat 2) a linear lane reads 0.5;
    // ease-out curve (+1) reads well above, ease-in (-1) well below, and the curve
    // rides on the LEFT point of the segment. Endpoints stay exact.
    AutomationLane lane;
    lane.target = AutomationTarget::Volume;
    lane.points = {{0.0, 0.0f, 0.0f}, {4.0, 1.0f, 0.0f}};
    if (std::fabs(lane.valueAt(2.0) - 0.5f) > 1e-4f) return false;   // linear baseline
    lane.points[0].curve = 1.0f;                                    // ease-out
    if (lane.valueAt(2.0) < 0.6f) return false;
    lane.points[0].curve = -1.0f;                                   // ease-in
    if (lane.valueAt(2.0) > 0.4f) return false;
    // Endpoints unaffected by curvature.
    if (std::fabs(lane.valueAt(0.0) - 0.0f) > 1e-4f) return false;
    if (std::fabs(lane.valueAt(4.0) - 1.0f) > 1e-4f) return false;
    return true;
}

// --- hosted-plugin parameters (M9-B) ---------------------------------------
// deviceIndex < 0 addresses the track's instrument; otherwise the effect at that
// chain position. All are message-thread introspection over the authoring graph.

int32_t Engine::pluginParamCount(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamCount() : 0;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return 0;
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamCount() : 0;
}
std::string Engine::pluginParamId(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamId(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamId(paramIndex) : std::string{};
}
std::string Engine::pluginParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamName(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamName(paramIndex) : std::string{};
}
float Engine::pluginParamGet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamGet(paramIndex) : 0.0f;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return 0.0f;
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamGet(paramIndex) : 0.0f;
}
void Engine::pluginParamSet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float normalized) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    // Instrument param that's a CV target → edit routes to the link base (CvTargetKind::Instrument).
    if (deviceIndex < 0) routeCvBaseEdit(1, trackId, -1, paramIndex, normalized);
    if (deviceIndex < 0) { if (t->instrument) t->instrument->pluginParamSet(paramIndex, normalized); return; }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return;
    if (auto& d = t->devices[deviceIndex]) d->pluginParamSet(paramIndex, normalized);
}
int32_t Engine::pluginParamIndexOfId(int32_t trackId, int32_t deviceIndex, const std::string& id) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return -1;
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamIndexOfId(id) : -1;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return -1;
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamIndexOfId(id) : -1;
}

int32_t Engine::lastTouchedPluginParam(int32_t trackId, int32_t deviceIndex) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return -1;
    if (deviceIndex < 0) return t->instrument ? t->instrument->lastTouchedPluginParam() : -1;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return -1;
    auto& d = t->devices[deviceIndex];
    return d ? d->lastTouchedPluginParam() : -1;
}

int32_t Engine::addPluginAutomationLane(int32_t trackId, int32_t deviceIndex, const char* paramId) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const std::string id = paramId ? paramId : "";
    // Reuse an existing lane on the same (device, param).
    for (size_t i = 0; i < old->automation.size(); ++i) {
        const AutomationLane& l = old->automation[i];
        if (l.target == AutomationTarget::PluginParam && l.deviceIndex == deviceIndex && l.paramId == id)
            return static_cast<int32_t>(i);
    }
    auto nt = cloneTrack(*old);
    AutomationLane lane;
    lane.target = AutomationTarget::PluginParam;
    lane.deviceIndex = deviceIndex;
    lane.paramId = id;
    lane.paramIndex = pluginParamIndexOfId(trackId, deviceIndex, id); // resolve now; -1 if absent
    nt->automation.push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    republishWithTrack(trackId, nt);
    return index;
}

std::string Engine::automationLaneParamId(int32_t trackId, int32_t laneIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return {};
    return t->automation[laneIndex].paramId;
}

// --- automation write / record (M9-C) --------------------------------------

namespace {
bool sameTarget(int32_t t0, int32_t d0, int32_t p0, const std::string& id0,
                int32_t t1, int32_t d1, int32_t p1, const std::string& id1) {
    if (t0 != t1) return false;
    switch (static_cast<AutomationTarget>(t0)) {
        case AutomationTarget::DeviceParam:
        case AutomationTarget::MidiDeviceParam: return d0 == d1 && p0 == p1;
        case AutomationTarget::PluginParam: return d0 == d1 && id0 == id1;
        default: return true; // Volume / Pan
    }
}
} // namespace

float Engine::currentTargetValue(const Track& t, int32_t target, int32_t deviceIndex, int32_t paramIndex) const {
    switch (static_cast<AutomationTarget>(target)) {
        case AutomationTarget::Volume: return t.volume();
        case AutomationTarget::Pan:    return t.pan();
        case AutomationTarget::DeviceParam:
            if (deviceIndex >= 0 && deviceIndex < static_cast<int32_t>(t.devices.size()))
                if (auto* d = t.devices[deviceIndex].get()) return d->getParam(paramIndex);
            return 0.0f;
        case AutomationTarget::MidiDeviceParam:
            if (deviceIndex >= 0 && deviceIndex < static_cast<int32_t>(t.midiEffects.size()))
                if (auto* m = t.midiEffects[deviceIndex].get()) return m->getParam(paramIndex);
            return 0.0f;
        case AutomationTarget::PluginParam:
            if (deviceIndex < 0) return t.instrument ? t.instrument->pluginParamGet(paramIndex) : 0.0f;
            if (deviceIndex < static_cast<int32_t>(t.devices.size()))
                if (auto* d = t.devices[deviceIndex].get()) return d->pluginParamGet(paramIndex);
            return 0.0f;
    }
    return 0.0f;
}

void Engine::setLaneSuppressRead(int32_t trackId, int32_t laneIndex, bool suppress) {
    auto old = findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return;
    if (old->automation[laneIndex].suppressRead == suppress) return;
    auto nt = cloneTrack(*old);
    nt->automation[laneIndex].suppressRead = suppress;
    republishWithTrackRaw(trackId, nt);   // transient flag — never an undo step
}

void Engine::setAutomationRecord(bool on) {
    autoRecord_.store(on, std::memory_order_relaxed);
    if (!on) finishAllWrites();           // leaving record ends any gesture in progress
}

// Find an existing lane for a target without creating one (used by the override
// path, which must not litter the project with empty lanes).
static int32_t findLaneFor(const Track& t, int32_t target, int32_t deviceIndex,
                           int32_t paramIndex, const std::string& paramId) {
    for (size_t i = 0; i < t.automation.size(); ++i) {
        const AutomationLane& l = t.automation[i];
        if (l.target != static_cast<AutomationTarget>(target)) continue;
        switch (l.target) {
            case AutomationTarget::DeviceParam:
            case AutomationTarget::MidiDeviceParam:
                if (l.deviceIndex != deviceIndex || l.paramIndex != paramIndex) continue;
                break;
            case AutomationTarget::PluginParam:
                if (l.deviceIndex != deviceIndex || l.paramId != paramId) continue;
                break;
            default: break;               // Volume / Pan: one lane per track
        }
        return static_cast<int32_t>(i);
    }
    return -1;
}

void Engine::beginAutomationWrite(int32_t trackId, int32_t target, int32_t deviceIndex,
                                  int32_t paramIndex, const char* paramId, bool latch) {
    const std::string id = paramId ? paramId : "";
    for (auto& aw : activeWrites_)        // already writing this target?
        if (aw.trackId == trackId &&
            sameTarget(aw.target, aw.deviceIndex, aw.paramIndex, aw.paramId, target, deviceIndex, paramIndex, id))
            return;

    if (!autoRecord_.load(std::memory_order_relaxed)) {
        // Not recording: the lane keeps playing back unless it actually has automation,
        // in which case the hand takes over (override) until reenableAutomation().
        auto t = findTrackAuthoring(trackId);
        if (!t) return;
        const int32_t lane = findLaneFor(*t, target, deviceIndex, paramIndex, id);
        if (lane < 0 || t->automation[lane].points.empty()) return;
        for (auto& o : overrides_) if (o.trackId == trackId && o.laneIndex == lane) return;
        setLaneSuppressRead(trackId, lane, true);
        overrides_.push_back({trackId, lane});
        return;
    }

    // Resolve (or create) the target's lane. PluginParam resolves paramIndex by id.
    int32_t laneIndex, resolvedParam = paramIndex;
    if (static_cast<AutomationTarget>(target) == AutomationTarget::PluginParam) {
        laneIndex = addPluginAutomationLane(trackId, deviceIndex, id.c_str());
        resolvedParam = pluginParamIndexOfId(trackId, deviceIndex, id);
    } else {
        laneIndex = addAutomationLane(trackId, target, deviceIndex, paramIndex);
    }
    if (laneIndex < 0) return;
    // Recording this lane supersedes any hand override of it.
    for (size_t i = 0; i < overrides_.size(); ++i)
        if (overrides_[i].trackId == trackId && overrides_[i].laneIndex == laneIndex)
            { overrides_.erase(overrides_.begin() + i); break; }
    // Checkpoint the clean pre-write state once per write session (concurrent
    // targets share one undo step); subsequent grows are raw.
    if (activeWrites_.empty()) pushUndo();
    setLaneSuppressRead(trackId, laneIndex, true);
    const double beat = transport_.uiPositionBeats();
    activeWrites_.push_back({trackId, target, deviceIndex, resolvedParam, id, laneIndex, beat, latch});
}

void Engine::endAutomationWrite(int32_t trackId, int32_t target, int32_t deviceIndex,
                                int32_t paramIndex, const char* paramId) {
    // A released mouse gesture stops writing here; a latched one (hardware control)
    // keeps writing its last value until the transport stops (finishAllWrites).
    const std::string id = paramId ? paramId : "";
    for (size_t i = 0; i < activeWrites_.size(); ++i) {
        auto& aw = activeWrites_[i];
        if (aw.trackId == trackId &&
            sameTarget(aw.target, aw.deviceIndex, aw.paramIndex, aw.paramId, target, deviceIndex, paramIndex, id)) {
            if (aw.latch) return;
            setLaneSuppressRead(aw.trackId, aw.laneIndex, false);
            activeWrites_.erase(activeWrites_.begin() + i);
            return;
        }
    }
}

void Engine::finishAllWrites() {
    for (auto& aw : activeWrites_) setLaneSuppressRead(aw.trackId, aw.laneIndex, false);
    activeWrites_.clear();
}

void Engine::reenableAutomation() {
    for (auto& o : overrides_) setLaneSuppressRead(o.trackId, o.laneIndex, false);
    overrides_.clear();
}

void Engine::sampleAutomationWritesAt(double beat) {
    for (auto& aw : activeWrites_) {
        auto old = findTrackAuthoring(aw.trackId);
        if (!old || aw.laneIndex < 0 || aw.laneIndex >= static_cast<int32_t>(old->automation.size())) continue;
        const float v = currentTargetValue(*old, aw.target, aw.deviceIndex, aw.paramIndex);
        auto nt = cloneTrack(*old);
        auto& pts = nt->automation[aw.laneIndex].points;
        // Overwrite-sweep: drop points the playhead just passed over, then add the
        // fresh one. Backward motion (loop wrap / seek) resets the sweep origin.
        const double lo = std::min(aw.lastBeat, beat), hi = std::max(aw.lastBeat, beat);
        pts.erase(std::remove_if(pts.begin(), pts.end(),
                    [&](const AutomationPoint& p){ return p.beat > lo && p.beat <= hi; }), pts.end());
        pts.push_back({beat, v});
        std::sort(pts.begin(), pts.end(),
                  [](const AutomationPoint& a, const AutomationPoint& b){ return a.beat < b.beat; });
        republishWithTrackRaw(aw.trackId, nt);   // grow the take, not an undo step
        aw.lastBeat = beat;
    }
}

bool Engine::automationWriteSelfTest() {
    // Device-free. Record on: a mouse gesture (latch=false) writes at the playhead and
    // stops on release; a latched gesture ignores the release and only a stop ends it.
    // Record off: touching an automated param overrides its lane until re-enable.
    reset();
    auto t = std::make_shared<Track>(1, TrackType::Instrument);
    t->instrument = std::make_shared<Synth>();
    auto g = std::make_shared<Graph>();
    g->tracks.push_back(t);
    publishRaw(g);

    // Mouse gesture on Volume while recording.
    setAutomationRecord(true);
    beginAutomationWrite(1, 0, -1, -1, "", false);
    if (findTrackAuthoring(1)->automation.empty()) return false;
    if (!findTrackAuthoring(1)->automation[0].suppressRead) return false;
    setTrackVolume(1, 1.5f);   // by id — targets the live authoring track (as the UI does)
    sampleAutomationWritesAt(3.0);
    endAutomationWrite(1, 0, -1, -1, "");
    {
        auto tt = findTrackAuthoring(1);
        if (tt->automation[0].suppressRead) return false;         // cleared on release
        const auto& pts = tt->automation[0].points;
        bool ok = false;
        for (auto& p : pts) if (std::fabs(p.beat - 3.0) < 1e-6 && std::fabs(p.value - 1.5f) < 1e-4f) ok = true;
        if (!ok) return false;
    }

    // Latched gesture (hardware control): releasing must NOT end the write — only a
    // transport stop does, via finishAllWrites.
    beginAutomationWrite(1, 0, -1, -1, "", true);
    endAutomationWrite(1, 0, -1, -1, "");                          // release — ignored when latched
    if (!findTrackAuthoring(1)->automation[0].suppressRead) return false; // still writing
    finishAllWrites();                                            // transport stop
    if (findTrackAuthoring(1)->automation[0].suppressRead) return false;  // now cleared

    // Record off: a touch on a target with no automation must not create a lane...
    setAutomationRecord(false);
    const size_t lanes = findTrackAuthoring(1)->automation.size();
    beginAutomationWrite(1, 1, -1, -1, "", false);                 // Pan — no lane yet
    if (findTrackAuthoring(1)->automation.size() != lanes) return false;
    if (automationOverridden()) return false;
    // ...but on the automated Volume lane it overrides playback until re-enable.
    beginAutomationWrite(1, 0, -1, -1, "", false);
    if (!automationOverridden()) return false;
    if (!findTrackAuthoring(1)->automation[0].suppressRead) return false;
    endAutomationWrite(1, 0, -1, -1, "");                          // release keeps the override
    if (!findTrackAuthoring(1)->automation[0].suppressRead) return false;
    reenableAutomation();
    if (automationOverridden()) return false;
    if (findTrackAuthoring(1)->automation[0].suppressRead) return false;

    reset();
    return true;
}

bool Engine::pluginAutomationSelfTest() {
    // Device-free: a PluginParam lane on a non-plugin instrument (built-in Synth,
    // which exposes no plugin params) must store its fields and dispatch through
    // applyAutomation without crashing (the set is a harmless base no-op).
    auto g = std::make_shared<Graph>();
    auto t = std::make_shared<Track>(9998, TrackType::Instrument);
    t->instrument = std::make_shared<Synth>();
    AutomationLane lane;
    lane.target = AutomationTarget::PluginParam;
    lane.deviceIndex = -1;      // instrument
    lane.paramIndex = 0;
    lane.paramId = "test-param";
    lane.points = {{0.0, 0.0f}, {4.0, 1.0f}};
    t->automation.push_back(lane);
    g->tracks.push_back(t);
    applyAutomation(g.get(), 2.0);   // must not crash
    return t->automation.size() == 1
        && t->automation[0].target == AutomationTarget::PluginParam
        && t->automation[0].paramId == "test-param"
        && std::fabs(lane.valueAt(2.0) - 0.5f) <= 1e-4f;
}

int32_t Engine::addAutomationLane(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const auto tgt = static_cast<AutomationTarget>(target);
    const bool usesDev = tgt == AutomationTarget::DeviceParam || tgt == AutomationTarget::MidiDeviceParam;
    // Reuse an existing lane on the same target (no snapshot) — one lane per target.
    for (size_t i = 0; i < old->automation.size(); ++i) {
        const AutomationLane& l = old->automation[i];
        if (l.target == tgt &&
            (!usesDev || (l.deviceIndex == deviceIndex && l.paramIndex == paramIndex)))
            return static_cast<int32_t>(i);
    }
    auto nt = cloneTrack(*old);
    AutomationLane lane;
    lane.target = tgt;
    lane.deviceIndex = usesDev ? deviceIndex : -1;
    lane.paramIndex  = usesDev ? paramIndex  : -1;
    nt->automation.push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    republishWithTrack(trackId, nt);
    return index;
}

int32_t Engine::automationLaneCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->automation.size()) : 0;
}

bool Engine::automationLaneInfo(int32_t trackId, int32_t laneIndex, int32_t* outTarget,
                                int32_t* outDeviceIndex, int32_t* outParamIndex, int32_t* outPointCount) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return false;
    const AutomationLane& l = t->automation[laneIndex];
    if (outTarget)      *outTarget      = static_cast<int32_t>(l.target);
    if (outDeviceIndex) *outDeviceIndex = l.deviceIndex;
    if (outParamIndex)  *outParamIndex  = l.paramIndex;
    if (outPointCount)  *outPointCount  = static_cast<int32_t>(l.points.size());
    return true;
}

int32_t Engine::getAutomationPoints(int32_t trackId, int32_t laneIndex, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return 0;
    const auto& pts = t->automation[laneIndex].points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) { out[i].beat = pts[i].beat; out[i].value = pts[i].value; out[i].curve = pts[i].curve; }
    return n;
}

bool Engine::setAutomationPoints(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count) {
    return setAutomationPointsImpl(trackId, laneIndex, pts, count, /*undoCheckpoint=*/true);
}
// Live variant: no undo checkpoint. Used while dragging a point so the lane (and thus
// applyAutomation → the device) follows in real time; the caller checkpoints once at the
// drag start and does the final commit through the undo path.
bool Engine::setAutomationPointsLive(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count) {
    return setAutomationPointsImpl(trackId, laneIndex, pts, count, /*undoCheckpoint=*/false);
}
bool Engine::setAutomationPointsImpl(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count, bool undoCheckpoint) {
    auto old = findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return false;
    auto nt = cloneTrack(*old);
    auto& lane = nt->automation[laneIndex];
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
              [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    if (undoCheckpoint) republishWithTrack(trackId, nt);
    else                republishWithTrackRaw(trackId, nt);
    return true;
}

bool Engine::removeAutomationLane(int32_t trackId, int32_t laneIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return false;
    auto nt = cloneTrack(*old);
    nt->automation.erase(nt->automation.begin() + laneIndex);
    republishWithTrack(trackId, nt);
    return true;
}

// --- master-volume automation (graph-level, M9 follow-up) ------------------

int32_t Engine::masterVolumeAutomationCount() const {
    return authoring_ ? static_cast<int32_t>(authoring_->masterVolume.points.size()) : 0;
}

int32_t Engine::getMasterVolumeAutomation(NotaAutomationPoint* out, int32_t cap) const {
    if (!authoring_) return 0;
    const auto& pts = authoring_->masterVolume.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) { out[i].beat = pts[i].beat; out[i].value = pts[i].value; out[i].curve = pts[i].curve; }
    return n;
}

bool Engine::setMasterVolumeAutomation(const NotaAutomationPoint* pts, int32_t count) {
    if (!authoring_) return false;
    auto g = std::make_shared<Graph>(*authoring_);   // copy tracks (shared) + sceneCount + masterVolume
    auto& lane = g->masterVolume;
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
              [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    publish(std::move(g));   // structural edit → undo checkpoint
    return true;
}

// --- per-clip volume envelope (M9 follow-up) -------------------------------

int32_t Engine::getAudioClipVolumeEnvelope(int32_t trackId, int32_t clipIndex, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const auto& pts = t->clips[clipIndex].volumeEnvelope.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) { out[i].beat = pts[i].beat; out[i].value = pts[i].value; out[i].curve = pts[i].curve; }
    return n;
}

bool Engine::setAudioClipVolumeEnvelope(int32_t trackId, int32_t clipIndex, const NotaAutomationPoint* pts, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    auto nt = cloneTrack(*old);
    auto& lane = nt->clips[clipIndex].volumeEnvelope;
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
              [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    republishWithTrack(trackId, nt);   // structural edit → undo checkpoint
    return true;
}

int32_t Engine::getAudioClipPanEnvelope(int32_t trackId, int32_t clipIndex, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const auto& pts = t->clips[clipIndex].panEnvelope.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) { out[i].beat = pts[i].beat; out[i].value = pts[i].value; out[i].curve = pts[i].curve; }
    return n;
}

bool Engine::setAudioClipPanEnvelope(int32_t trackId, int32_t clipIndex, const NotaAutomationPoint* pts, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->clips.size())) return false;
    auto nt = cloneTrack(*old);
    auto& lane = nt->clips[clipIndex].panEnvelope;
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
              [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::getMidiClipEnvelope(int32_t trackId, int32_t clipIndex, int32_t kind, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    const MidiClip& c = t->midiClips[clipIndex];
    const auto& pts = (kind == 1 ? c.volumeEnvelope : c.velocityEnvelope).points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) { out[i].beat = pts[i].beat; out[i].value = pts[i].value; out[i].curve = pts[i].curve; }
    return n;
}

bool Engine::setMidiClipEnvelope(int32_t trackId, int32_t clipIndex, int32_t kind, const NotaAutomationPoint* pts, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || clipIndex < 0 || clipIndex >= static_cast<int32_t>(old->midiClips.size())) return false;
    auto nt = cloneTrack(*old);
    auto& lane = (kind == 1 ? nt->midiClips[clipIndex].volumeEnvelope : nt->midiClips[clipIndex].velocityEnvelope);
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
              [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    republishWithTrack(trackId, nt);
    return true;
}

} // namespace nota
