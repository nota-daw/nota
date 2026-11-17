// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — parameter automation (M9): read/apply, lane CRUD, hosted-plugin parameter surface, contextual write recording, self-tests.

#include "Engine.h"
#include "TempoMap.h"
#include "ParamResolve.h"
#include "bridge/PluginHostBridge.h"
#include "devices/instruments/Synth.h"
#include "devices/instruments/Twin.h"

#include <algorithm>
#include <limits>
#include <cmath>

namespace nota {

// --- parameter automation (M9) ---------------------------------------------
// One track's lanes at `beat` (block rate, audio thread, allocation-free). Shared by the
// tracks and the master (specs/099): master Pan writes the master pan stage's value, master
// device / plug-in lanes drive the master chain; Tempo plays through the tempo map instead.
// `beatEnd` is the block's end, for the instruments that read a lane at their own steps
// (pluginParamLane, specs/100).
static void applyTrackLanes(Track& t, double beat, double beatEnd) {
    for (const TrackLane& lane : t.automation) {
        if (lane.points.empty() || lane.suppressRead) continue; // M9-C: control leads while writing
        if (lane.target == AutomationTarget::Tempo) continue; // specs/099: plays through the tempo map
        const float v = lane.valueAt(beat);
        if (lane.nested()) { // a device grouped into a rack (specs/044)
            const ParamOwner o = resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot);
            if (lane.target == AutomationTarget::PluginParam) {
                if (lane.paramIndex < 0) continue;
                if (o.instrument) o.instrument->pluginParamSet(lane.paramIndex, v);
                else if (o.device) o.device->pluginParamSet(lane.paramIndex, v);
            } else if (o.device) o.device->setParam(lane.paramIndex, v);
            else if (o.midi) o.midi->setParam(lane.paramIndex, v);
            continue;
        }
        switch (lane.target) {
        case AutomationTarget::Tempo: break;
        case AutomationTarget::Volume: t.setVolume(v); break;
        case AutomationTarget::Pan: t.setPan(v); break;
        case AutomationTarget::Send: break; // specs/069: a send lane does not move the level
        case AutomationTarget::DeviceParam:
            if (lane.deviceIndex >= 0 && lane.deviceIndex < static_cast<int32_t>(t.devices.size()))
                if (auto* d = t.devices[lane.deviceIndex].get()) d->setParam(lane.paramIndex, v);
            break;
        case AutomationTarget::MidiDeviceParam: // deviceIndex = MIDI-FX chain index
            if (lane.deviceIndex >= 0 && lane.deviceIndex < static_cast<int32_t>(t.midiEffects.size()))
                if (auto* m = t.midiEffects[lane.deviceIndex].get()) m->setParam(lane.paramIndex, v);
            break;
        case AutomationTarget::PluginParam:
            // Normalized 0..1 into a hosted plugin (M9-B). deviceIndex < 0
            // targets the instrument; paramIndex is pre-resolved from paramId.
            if (lane.paramIndex < 0) break;
            if (lane.deviceIndex < 0) {
                if (t.instrument) t.instrument->pluginParamSet(lane.paramIndex, v);
            } else if (lane.deviceIndex < static_cast<int32_t>(t.devices.size())) {
                if (auto* d = t.devices[lane.deviceIndex].get()) d->pluginParamSet(lane.paramIndex, v);
            }
            break;
        }
    }
    // Exact-time lanes (specs/100), once every lane above has set its block-start value: an
    // instrument samples a lane on its step grid, and the grid can be another param of the
    // same block (Twin's Rate), so the order the lanes were added in must not matter.
    for (const TrackLane& lane : t.automation) {
        if (lane.points.empty() || lane.suppressRead || lane.target != AutomationTarget::PluginParam || lane.paramIndex < 0)
            continue;
        Instrument* inst = nullptr;
        if (lane.nested()) inst = resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot).instrument;
        else if (lane.deviceIndex < 0) inst = t.instrument.get();
        if (inst) inst->pluginParamLane(lane.paramIndex, lane, beat, beatEnd);
    }
}

void Engine::applyMasterLanes(Track& master, double beat) { applyTrackLanes(master, beat, beat); }

const TrackLane* Engine::masterPanLane(const Track& master) {
    for (const TrackLane& l : master.automation)
        if (l.target == AutomationTarget::Pan && !l.nested() && !l.points.empty() && !l.suppressRead) return &l;
    return nullptr;
}

void Engine::applyAutomation(Graph* g, double beat, double beatEnd) {
    if (beatEnd < beat) beatEnd = beat;
    // Most tracks carry no lanes: skip them before the (out-of-line) per-lane body.
    for (auto& tptr : g->tracks)
        if (!tptr->automation.empty()) applyTrackLanes(*tptr, beat, beatEnd);
    // Master lanes (specs/099): Pan and the master chain's device / plug-in parameters.
    // A master Volume lane is not added there — master volume stays the graph-level lane.
    if (g->masterTrack && !g->masterTrack->automation.empty()) applyTrackLanes(*g->masterTrack, beat, beatEnd);
    // Master-volume lane (graph-level, M9 follow-up): write the master gain atomic.
    if (!g->masterVolume.points.empty() && !g->masterVolume.suppressRead)
        masterVolume_.store(std::clamp(g->masterVolume.valueAt(beat), 0.0f, 2.0f), std::memory_order_relaxed);
}

bool Engine::automationSelfTest() {
    // Volume lane: 1.0 at beat 0, 0.0 at beat 4 -> 0.5 at beat 2; hold at ends.
    TrackLane lane;
    lane.target = AutomationTarget::Volume;
    lane.points = {{0.0, 1.0f}, {4.0, 0.0f}};
    if (std::fabs(lane.valueAt(2.0) - 0.5f) > 1e-4f) return false;
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
    TrackLane lane;
    lane.target = AutomationTarget::Volume;
    lane.points = {{0.0, 0.0f, 0.0f}, {4.0, 1.0f, 0.0f}};
    if (std::fabs(lane.valueAt(2.0) - 0.5f) > 1e-4f) return false; // linear baseline
    lane.points[0].curve = 1.0f;                                   // ease-out
    if (lane.valueAt(2.0) < 0.6f) return false;
    // No jump at the start (#591): the old power curve was ~75 % there by 1 % of the span.
    if (lane.valueAt(0.04) > 0.1f) return false;
    const float easeOutMid = lane.valueAt(2.0);
    lane.points[0].curve = -1.0f; // ease-in
    if (lane.valueAt(2.0) > 0.4f) return false;
    // Ease-in and ease-out mirror each other.
    if (std::fabs(lane.valueAt(2.0) - (1.0f - easeOutMid)) > 1e-4f) return false;
    // A piece of a shaped segment, with subCurve, reads the same values (shift-invariance).
    {
        TrackLane cut;
        cut.target = AutomationTarget::Volume;
        cut.points = {{1.0, lane.valueAt(1.0), AutomationLane::subCurve(-1.0f, 0.5)}, {3.0, lane.valueAt(3.0), 0.0f}};
        for (double b = 1.0; b <= 3.0; b += 0.25)
            if (std::fabs(cut.valueAt(b) - lane.valueAt(b)) > 1e-4f) return false;
    }
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
int32_t Engine::pluginParamIndexFromV1(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex >= 0 || !t->instrument || t->instrument->kind() != 18) return paramIndex;
    return twin::paramFromV1(paramIndex); // Twin's side blocks grew (#531)
}
std::string Engine::pluginParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamName(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamName(paramIndex) : std::string{};
}
std::string Engine::pluginParamText(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamText(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamText(paramIndex) : std::string{};
}
// Stepped params (specs/100): only an instrument declares them; effects' plugin params stay
// continuous for the lane editor.
int32_t Engine::pluginParamSteps(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    return t && deviceIndex < 0 && t->instrument ? t->instrument->pluginParamSteps(paramIndex) : 0;
}
std::string Engine::pluginParamStepText(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, int32_t step) const {
    auto t = findTrackAuthoring(trackId);
    return t && deviceIndex < 0 && t->instrument ? t->instrument->pluginParamStepText(paramIndex, step) : std::string{};
}
float Engine::pluginParamDefault(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    if (deviceIndex < 0) {
        if (!t->instrument) return 0.0f;
        // A built-in instrument has no plugin default of its own (the base returns 0, which
        // sent a graph's "Reset to default" to 0); its default is a fresh instance's value.
        if (t->instrument->kind() >= 0) return instrumentParamDefault(trackId, paramIndex);
        return t->instrument->pluginParamDefault(paramIndex);
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return 0.0f;
    auto& d = t->devices[deviceIndex];
    return d ? d->pluginParamDefault(paramIndex) : 0.0f;
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
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->pluginParamSet(paramIndex, normalized);
        return;
    }
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
        const TrackLane& l = old->automation[i];
        if (!l.nested() && l.target == AutomationTarget::PluginParam && l.deviceIndex == deviceIndex && l.paramId == id)
            return static_cast<int32_t>(i);
    }
    auto nt = cloneTrack(*old);
    TrackLane lane;
    lane.target = AutomationTarget::PluginParam;
    lane.deviceIndex = deviceIndex;
    lane.paramId = id;
    lane.paramIndex = pluginParamIndexOfId(trackId, deviceIndex, id); // resolve now; -1 if absent
    nt->automation.push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    republishWithTrack(trackId, nt);
    return index;
}

// ---- nested addresses for save / load (specs/044) ---------------------------
uint64_t Engine::nestedChainUid(int32_t trackId, int32_t host, int32_t chainIndex) const {
    auto t = findTrackAuthoring(trackId);
    const RackCore* r = t ? rackAtHost(*t, host) : nullptr;
    return r ? r->chainBaseUid(chainIndex) : 0;
}
int32_t Engine::nestedChainIndex(int32_t trackId, int32_t host, uint64_t uid) const {
    auto t = findTrackAuthoring(trackId);
    const RackCore* r = t ? rackAtHost(*t, host) : nullptr;
    return r ? r->chainIndexOfUid(uid) : -1;
}

bool Engine::automationLanePath(int32_t trackId, int32_t laneIndex, int32_t* chainIndex, int32_t* slot) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return false;
    const TrackLane& l = t->automation[laneIndex];
    if (chainIndex) *chainIndex = l.nested() ? nestedChainIndex(trackId, l.deviceIndex, l.chainUid) : -1;
    if (slot) *slot = l.nested() ? l.slot : -1;
    return true;
}

int32_t Engine::addNestedAutomationLane(int32_t trackId, int32_t target, int32_t host, int32_t chainIndex,
    int32_t slot, int32_t paramIndex, const char* paramId) {
    auto old = findTrackAuthoring(trackId);
    const uint64_t uid = nestedChainUid(trackId, host, chainIndex);
    if (!old || uid == 0) return -1;
    const auto tgt = static_cast<AutomationTarget>(target);
    if (tgt != AutomationTarget::DeviceParam && tgt != AutomationTarget::MidiDeviceParam && tgt != AutomationTarget::PluginParam)
        return -1;
    TrackLane lane;
    lane.target = tgt;
    lane.deviceIndex = host;
    lane.chainUid = uid;
    lane.slot = slot;
    lane.paramId = paramId ? paramId : "";
    lane.paramIndex = paramIndex;
    if (tgt == AutomationTarget::PluginParam && !lane.paramId.empty()) {
        const ParamOwner o = resolveNested(*old, host, uid, slot);
        lane.paramIndex = o.instrument ? o.instrument->pluginParamIndexOfId(lane.paramId)
                        : o.device     ? o.device->pluginParamIndexOfId(lane.paramId)
                                       : -1;
    }
    for (size_t i = 0; i < old->automation.size(); ++i) { // one lane per target
        const TrackLane& l = old->automation[i];
        if (l.target == tgt && l.deviceIndex == host && l.chainUid == uid && l.slot == slot
            && (tgt == AutomationTarget::PluginParam ? l.paramId == lane.paramId : l.paramIndex == paramIndex))
            return static_cast<int32_t>(i);
    }
    auto nt = cloneTrack(*old);
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
bool sameTarget(int32_t t0, int32_t d0, int32_t p0, const std::string& id0, int32_t t1, int32_t d1, int32_t p1,
    const std::string& id1) {
    if (t0 != t1) return false;
    switch (static_cast<AutomationTarget>(t0)) {
    case AutomationTarget::DeviceParam:
    case AutomationTarget::MidiDeviceParam: return d0 == d1 && p0 == p1;
    case AutomationTarget::PluginParam: return d0 == d1 && id0 == id1;
    case AutomationTarget::Send: return d0 == d1; // the bus
    default: return true; // Volume / Pan
    }
}
} // namespace

float Engine::currentTargetValue(const Track& t, int32_t target, int32_t deviceIndex, int32_t paramIndex) const {
    switch (static_cast<AutomationTarget>(target)) {
    case AutomationTarget::Volume: return t.volume();
    case AutomationTarget::Pan: return t.pan();
    case AutomationTarget::Send: return t.send(deviceIndex);
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
    republishWithTrackRaw(trackId, nt); // transient flag — never an undo step
}

void Engine::setAutomationRecord(bool on) {
    autoRecord_.store(on, std::memory_order_relaxed);
    if (!on) finishAllWrites(); // leaving record ends any gesture in progress
}

// Find an existing lane for a target without creating one (used by the override
// path, which must not litter the project with empty lanes).
static int32_t findLaneFor(
    const Track& t, int32_t target, int32_t deviceIndex, int32_t paramIndex, const std::string& paramId) {
    for (size_t i = 0; i < t.automation.size(); ++i) {
        const TrackLane& l = t.automation[i];
        if (l.nested() || l.target != static_cast<AutomationTarget>(target)) continue; // specs/044
        switch (l.target) {
        case AutomationTarget::DeviceParam:
        case AutomationTarget::MidiDeviceParam:
            if (l.deviceIndex != deviceIndex || l.paramIndex != paramIndex) continue;
            break;
        case AutomationTarget::PluginParam:
            if (l.deviceIndex != deviceIndex || l.paramId != paramId) continue;
            break;
        case AutomationTarget::Send:
            if (l.deviceIndex != deviceIndex) continue;
            break;
        default: break; // Volume / Pan: one lane per track
        }
        return static_cast<int32_t>(i);
    }
    return -1;
}

// specs/016: an unlatched touch — a mouse press on a parameter control, or a one-off edit
// bracketed by begin/end — is one value-undo gesture on that parameter. Latched touches
// (hardware controls) end by pause on the managed side instead; volume / pan touches are
// gestured by their sources (specs/009).
void Engine::beginParamTouch(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const std::string& id) {
    MixKind kind;
    switch (static_cast<AutomationTarget>(target)) {
    case AutomationTarget::DeviceParam: kind = MixKind::DeviceParam; break;
    case AutomationTarget::MidiDeviceParam: kind = MixKind::MidiDeviceParam; break;
    case AutomationTarget::PluginParam:
        kind = MixKind::PluginParam;
        paramIndex = pluginParamIndexOfId(trackId, deviceIndex, id);
        break;
    default: return;
    }
    for (const auto& tg : touchGestures_)
        if (tg.trackId == trackId &&
            sameTarget(tg.target, tg.deviceIndex, tg.paramIndex, tg.paramId, target, deviceIndex, paramIndex, id))
            return; // already open
    const MixTarget mt{kind, trackId, deviceIndex, paramIndex};
    if (const int32_t h = beginMixGesture(&mt, 1))
        touchGestures_.push_back({trackId, target, deviceIndex, paramIndex, id, h});
}

void Engine::endParamTouch(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const std::string& id) {
    for (size_t i = 0; i < touchGestures_.size(); ++i) {
        const auto& tg = touchGestures_[i];
        // PluginParam touches match by id (sameTarget ignores the index begin resolved).
        if (tg.trackId != trackId ||
            !sameTarget(tg.target, tg.deviceIndex, tg.paramIndex, tg.paramId, target, deviceIndex, paramIndex, id))
            continue;
        const int32_t h = tg.handle;
        touchGestures_.erase(touchGestures_.begin() + static_cast<std::ptrdiff_t>(i));
        endMixGesture(h);
        return;
    }
}

void Engine::beginAutomationWrite(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId, bool latch) {
    const std::string id = paramId ? paramId : "";
    // Open the undo gesture first, so a checkpoint the write pushes below (recording) merges
    // into it and the lane edit and the value undo as one step.
    if (!latch) beginParamTouch(trackId, target, deviceIndex, paramIndex, id);
    for (auto& aw : activeWrites_) // already writing this target?
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
        for (auto& o : overrides_)
            if (o.trackId == trackId && o.laneIndex == lane) return;
        setLaneSuppressRead(trackId, lane, true);
        overrides_.push_back({trackId, lane});
        return;
    }

    // Writing a lane during a take is part of the take (specs/078): remember the track as it was
    // before, and mark the checkpoints below (a new lane, the pre-write state) as the take's.
    noteTakeTrack(trackId);
    TakePushScope takePush(*this);

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
        if (overrides_[i].trackId == trackId && overrides_[i].laneIndex == laneIndex) {
            overrides_.erase(overrides_.begin() + i);
            break;
        }
    // Checkpoint the clean pre-write state once per write session (concurrent
    // targets share one undo step); subsequent grows are raw.
    if (activeWrites_.empty()) pushUndo();
    setLaneSuppressRead(trackId, laneIndex, true);
    const double beat = transport_.uiPositionBeats();
    ActiveWrite aw{trackId, target, deviceIndex, resolvedParam, id, laneIndex, beat, latch};
    if (auto tw = findTrackAuthoring(trackId)) aw.before = currentTargetValue(*tw, target, deviceIndex, resolvedParam);
    activeWrites_.push_back(std::move(aw));
}

void Engine::endAutomationWrite(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId) {
    // A released mouse gesture stops writing here; a latched one (hardware control)
    // keeps writing its last value until the transport stops (finishAllWrites).
    const std::string id = paramId ? paramId : "";
    endParamTouch(trackId, target, deviceIndex, paramIndex, id); // the value-undo step (specs/016)
    for (size_t i = 0; i < activeWrites_.size(); ++i) {
        auto& aw = activeWrites_[i];
        if (aw.trackId == trackId &&
            sameTarget(aw.target, aw.deviceIndex, aw.paramIndex, aw.paramId, target, deviceIndex, paramIndex, id)) {
            if (aw.latch) return;
            // A discrete edit (a click: a bank button, a mode chip) begins and ends its write before
            // poll() samples it, so it would leave the lane as it was. Record it at the playhead.
            if (!aw.sampled && autoRecord_.load(std::memory_order_relaxed) && transport_.uiIsPlaying())
                recordAutomationClick(aw, transport_.uiPositionBeats());
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
        pts.erase(std::remove_if(
                      pts.begin(), pts.end(), [&](const AutomationPoint& p) { return p.beat > lo && p.beat <= hi; }),
            pts.end());
        pts.push_back({beat, v});
        std::sort(
            pts.begin(), pts.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
        republishWithTrackRaw(aw.trackId, nt); // grow the take, not an undo step
        aw.lastBeat = beat;
        aw.sampled = true;
    }
}

// A click while recording (specs/100): the lane's value just before the playhead, then the new
// value, both at the playhead — a step, so a stepped target (a Rhythm bank) switches right there
// instead of ramping in from the point before. Part of the write's undo step (begin pushed it).
void Engine::recordAutomationClick(const ActiveWrite& aw, double beat) {
    auto old = findTrackAuthoring(aw.trackId);
    if (!old || aw.laneIndex < 0 || aw.laneIndex >= static_cast<int32_t>(old->automation.size())) return;
    const float v = currentTargetValue(*old, aw.target, aw.deviceIndex, aw.paramIndex);
    const auto& was = old->automation[aw.laneIndex].points;
    auto nt = cloneTrack(*old);
    auto& pts = nt->automation[aw.laneIndex].points;
    // The step starts from what the lane plays there, or — a fresh lane — what the target was.
    // The segment the click cuts keeps its shape on both sides: the piece before it ends at the
    // step with the curve of that piece (AutomationLane::pieceCurve), so nothing before the
    // click plays differently, and the step runs on to the next point with the rest of it.
    float after = 0.0f;
    if (!was.empty()) {
        const auto next = std::upper_bound(was.begin(), was.end(), beat,
            [](double b, const AutomationPoint& q) { return b < q.beat; });
        if (next != was.begin() && next != was.end()) {
            const size_t prev = static_cast<size_t>(next - was.begin()) - 1;
            pts[prev].curve = AutomationLane::pieceCurve(was, was[prev].beat, beat);
            after = AutomationLane::pieceCurve(was, beat, next->beat);
        }
    }
    pts.push_back({beat, was.empty() ? aw.before : old->automation[aw.laneIndex].valueAt(beat), 0.0f});
    pts.push_back({beat, v, after});
    std::stable_sort(
        pts.begin(), pts.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    republishWithTrackRaw(aw.trackId, nt);
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
    setTrackVolume(1, 1.5f); // by id — targets the live authoring track (as the UI does)
    sampleAutomationWritesAt(3.0);
    endAutomationWrite(1, 0, -1, -1, "");
    {
        auto tt = findTrackAuthoring(1);
        if (tt->automation[0].suppressRead) return false; // cleared on release
        const auto& pts = tt->automation[0].points;
        bool ok = false;
        for (auto& p : pts)
            if (std::fabs(p.beat - 3.0) < 1e-6 && std::fabs(p.value - 1.5f) < 1e-4f) ok = true;
        if (!ok) return false;
    }

    // Latched gesture (hardware control): releasing must NOT end the write — only a
    // transport stop does, via finishAllWrites.
    beginAutomationWrite(1, 0, -1, -1, "", true);
    endAutomationWrite(1, 0, -1, -1, "");                                 // release — ignored when latched
    if (!findTrackAuthoring(1)->automation[0].suppressRead) return false; // still writing
    finishAllWrites();                                                    // transport stop
    if (findTrackAuthoring(1)->automation[0].suppressRead) return false;  // now cleared

    // Record off: a touch on a target with no automation must not create a lane...
    setAutomationRecord(false);
    const size_t lanes = findTrackAuthoring(1)->automation.size();
    beginAutomationWrite(1, 1, -1, -1, "", false); // Pan — no lane yet
    if (findTrackAuthoring(1)->automation.size() != lanes) return false;
    if (automationOverridden()) return false;
    // ...but on the automated Volume lane it overrides playback until re-enable.
    beginAutomationWrite(1, 0, -1, -1, "", false);
    if (!automationOverridden()) return false;
    if (!findTrackAuthoring(1)->automation[0].suppressRead) return false;
    endAutomationWrite(1, 0, -1, -1, ""); // release keeps the override
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
    TrackLane lane;
    lane.target = AutomationTarget::PluginParam;
    lane.deviceIndex = -1; // instrument
    lane.paramIndex = 0;
    lane.paramId = "test-param";
    lane.points = {{0.0, 0.0f}, {4.0, 1.0f}};
    t->automation.push_back(lane);
    g->tracks.push_back(t);
    applyAutomation(g.get(), 2.0); // must not crash
    return t->automation.size() == 1 && t->automation[0].target == AutomationTarget::PluginParam &&
           t->automation[0].paramId == "test-param" && std::fabs(lane.valueAt(2.0) - 0.5f) <= 1e-4f;
}

int32_t Engine::addAutomationLane(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const auto tgt = static_cast<AutomationTarget>(target);
    if (target < 0 || target > static_cast<int32_t>(AutomationTarget::Tempo)) return -1;
    if (tgt == AutomationTarget::Send) return -1; // specs/069: sends are not an automation target
    if (tgt == AutomationTarget::Tempo && trackId != kMasterTrackId) return -1; // specs/099: master only
    // specs/099: the master's Volume is the graph-level masterVolume lane; it has no MIDI chain.
    if (trackId == kMasterTrackId && (tgt == AutomationTarget::Volume || tgt == AutomationTarget::MidiDeviceParam))
        return -1;
    const bool usesDev = tgt == AutomationTarget::DeviceParam || tgt == AutomationTarget::MidiDeviceParam;
    // Reuse an existing lane on the same target (no snapshot) — one lane per target.
    for (size_t i = 0; i < old->automation.size(); ++i) {
        const TrackLane& l = old->automation[i];
        if (!l.nested() && l.target == tgt && (!usesDev || (l.deviceIndex == deviceIndex && l.paramIndex == paramIndex)))
            return static_cast<int32_t>(i);
    }
    auto nt = cloneTrack(*old);
    TrackLane lane;
    lane.target = tgt;
    lane.deviceIndex = usesDev ? deviceIndex : -1;
    lane.paramIndex = usesDev ? paramIndex : -1;
    nt->automation.push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    republishWithTrack(trackId, nt);
    return index;
}

// --- device edit → lane index fix-up (message thread) -----------------------

void Engine::remapAutomationAfterDeviceChange(Track& nt, bool midi, int32_t removedIndex, int32_t from, int32_t to) {
    // Lanes on the effect chain (DeviceParam / PluginParam >= 0) or the MIDI-FX chain.
    auto onChain = [midi](const TrackLane& l) {
        // A nested lane (specs/044) follows its rack: deviceIndex is the rack's address.
        if (l.nested()) return midi ? isMidiHost(l.deviceIndex) : l.deviceIndex >= 0;
        if (midi) return l.target == AutomationTarget::MidiDeviceParam;
        return l.target == AutomationTarget::DeviceParam ||
               (l.target == AutomationTarget::PluginParam && l.deviceIndex >= 0);
    };
    // New chain position of `idx` (-1 = its device was removed).
    auto remap = [&](int32_t idx) -> int32_t {
        if (removedIndex >= 0) return idx == removedIndex ? -1 : idx > removedIndex ? idx - 1 : idx;
        if (idx == from) return to;
        if (from < to) {
            if (idx > from && idx <= to) return idx - 1;
        } else {
            if (idx >= to && idx < from) return idx + 1;
        }
        return idx;
    };

    // laneMap: old lane index → new lane index (-1 = dropped with its device).
    std::vector<int32_t> laneMap(nt.automation.size());
    std::vector<TrackLane> kept;
    kept.reserve(nt.automation.size());
    for (size_t i = 0; i < nt.automation.size(); ++i) {
        TrackLane& l = nt.automation[i];
        if (onChain(l)) {
            const bool midiRack = l.nested() && midi;
            const int32_t ni = remap(midiRack ? midiHostIndex(l.deviceIndex) : l.deviceIndex);
            if (ni < 0) {
                laneMap[i] = -1;
                continue;
            }
            l.deviceIndex = midiRack ? midiHost(ni) : ni;
        }
        laneMap[i] = static_cast<int32_t>(kept.size());
        kept.push_back(std::move(l));
    }
    nt.automation = std::move(kept);

    // In-flight gestures on this track address lanes by index and targets by device.
    const int32_t trackId = nt.id();
    auto laneGone = [&](int32_t& laneIndex) {
        if (laneIndex < 0 || laneIndex >= static_cast<int32_t>(laneMap.size())) return true;
        laneIndex = laneMap[laneIndex];
        return laneIndex < 0;
    };
    std::erase_if(activeWrites_, [&](ActiveWrite& aw) {
        if (aw.trackId != trackId) return false;
        if (laneGone(aw.laneIndex)) return true;
        const auto tgt = static_cast<AutomationTarget>(aw.target);
        const bool chain = midi ? tgt == AutomationTarget::MidiDeviceParam
                                : tgt == AutomationTarget::DeviceParam ||
                                      (tgt == AutomationTarget::PluginParam && aw.deviceIndex >= 0);
        if (chain) aw.deviceIndex = remap(aw.deviceIndex);
        return false;
    });
    std::erase_if(overrides_, [&](OverrideTarget& o) { return o.trackId == trackId && laneGone(o.laneIndex); });
}

std::vector<TrackLane> Engine::deviceLanes(const Track& t, int32_t family, int32_t index) const {
    std::vector<TrackLane> out;
    for (const auto& l : t.automation) {
        bool on = false;
        if (l.nested()) // lanes into a rack go where the rack goes (specs/044)
            on = family == 0 ? l.deviceIndex == index && index >= 0
               : family == 1 ? l.deviceIndex == midiHost(index)
               : family == 2 && l.deviceIndex == -1;
        else switch (family) {
        case 0:
            on = (l.target == AutomationTarget::DeviceParam || l.target == AutomationTarget::PluginParam) && l.deviceIndex == index;
            break;
        case 1: on = l.target == AutomationTarget::MidiDeviceParam && l.deviceIndex == index; break;
        case 2: on = l.target == AutomationTarget::PluginParam && l.deviceIndex < 0; break;
        default: break;
        }
        if (on) {
            out.push_back(l);
            out.back().suppressRead = false; // a gesture on the old track doesn't follow it
        }
    }
    return out;
}

bool Engine::automationDeviceRemapSelfTest() {
    // Two effects, a lane on each: reordering the chain keeps every lane on its own
    // device; removing a device drops its lanes and shifts the rest.
    const int32_t tid = addInstrumentTrack();
    const int32_t d0 = addTrackBuiltinDevice(tid, 3); // Delay   @0
    const int32_t d1 = addTrackBuiltinDevice(tid, 4); // Utility @1
    const int32_t d2 = addTrackBuiltinDevice(tid, 2); // Reverb  @2
    if (d0 != 0 || d1 != 1 || d2 != 2) {
        removeTrack(tid);
        return false;
    }
    const int32_t dp = static_cast<int32_t>(AutomationTarget::DeviceParam);
    addAutomationLane(tid, dp, 0, 1);                                               // lane 0 → Delay
    addAutomationLane(tid, dp, 1, 2);                                               // lane 1 → Utility
    addAutomationLane(tid, dp, 2, 3);                                               // lane 2 → Reverb
    addAutomationLane(tid, static_cast<int32_t>(AutomationTarget::Volume), -1, -1); // lane 3
    auto devOf = [&](int32_t lane) {
        int32_t dev = -9;
        automationLaneInfo(tid, lane, nullptr, &dev, nullptr, nullptr);
        return dev;
    };
    auto kindOf = [&](int32_t lane) { return trackDeviceBuiltinKind(tid, devOf(lane)); };

    moveDevice(tid, 0, 2); // Utility, Reverb, Delay
    bool ok = kindOf(0) == 3 && kindOf(1) == 4 && kindOf(2) == 2 && devOf(3) == -1;
    moveDevice(tid, 2, 0); // Delay, Utility, Reverb
    ok = ok && devOf(0) == 0 && devOf(1) == 1 && devOf(2) == 2;
    removeDevice(tid, 1); // Delay, Reverb — Utility's lane goes
    ok = ok && automationLaneCount(tid) == 3 && kindOf(0) == 3 && kindOf(1) == 2 && devOf(2) == -1;
    removeTrack(tid);
    return ok;
}

int32_t Engine::automationLaneCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->automation.size()) : 0;
}

bool Engine::automationLaneInfo(int32_t trackId, int32_t laneIndex, int32_t* outTarget, int32_t* outDeviceIndex,
    int32_t* outParamIndex, int32_t* outPointCount) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return false;
    const TrackLane& l = t->automation[laneIndex];
    if (outTarget) *outTarget = static_cast<int32_t>(l.target);
    if (outDeviceIndex) *outDeviceIndex = l.deviceIndex;
    if (outParamIndex) *outParamIndex = l.paramIndex;
    if (outPointCount) *outPointCount = static_cast<int32_t>(l.points.size());
    return true;
}

int32_t Engine::getAutomationPoints(int32_t trackId, int32_t laneIndex, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return 0;
    const auto& pts = t->automation[laneIndex].points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[i].beat;
        out[i].value = pts[i].value;
        out[i].curve = pts[i].curve;
    }
    return n;
}

bool Engine::setAutomationPoints(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count) {
    return setAutomationPointsImpl(trackId, laneIndex, pts, count, /*undoCheckpoint=*/true);
}
// Live variant: no undo checkpoint. Used while dragging a point so the lane (and thus
// applyAutomation → the device) follows in real time; the caller checkpoints once at the
// drag start and does the final commit through the undo path.
bool Engine::setAutomationPointsLive(
    int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count) {
    return setAutomationPointsImpl(trackId, laneIndex, pts, count, /*undoCheckpoint=*/false);
}
bool Engine::setAutomationPointsImpl(
    int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count, bool undoCheckpoint) {
    auto old = findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return false;
    const bool tempo = old->automation[laneIndex].target == AutomationTarget::Tempo;
    // The limit is on the compiled map, not the lane: a jump is two lane points on one beat,
    // so a migrated #568 map of kMaxPoints steps becomes about twice as many lane points.
    if (tempo && count > 2 * static_cast<int32_t>(TempoMap::kMaxPoints)) return false;
    for (int32_t i = 0; tempo && i < count; ++i)
        if (!std::isfinite(pts[i].beat) || !std::isfinite(pts[i].value)) return false;
    if (tempo && count > static_cast<int32_t>(TempoMap::kMaxPoints) - 1) {
        std::vector<double> beats(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) beats[i] = std::max(0.0, pts[i].beat);
        std::sort(beats.begin(), beats.end());
        size_t distinct = beats.front() > 0.0 ? 1 : 0; // a first point after 0 holds back to 0
        for (size_t i = 0; i < beats.size(); ++i)
            if (i == 0 || beats[i] - beats[i - 1] >= 1e-6) ++distinct;
        if (distinct > TempoMap::kMaxPoints) return false;
    }
    auto nt = cloneTrack(*old);
    auto& lane = nt->automation[laneIndex];
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) {
        // specs/099: tempo is 20..300 BPM everywhere; a lane never holds a value the map can't play.
        const float v = tempo ? static_cast<float>(TempoMap::clampBpm(pts[i].value)) : pts[i].value;
        lane.points.push_back({tempo ? std::max(0.0, pts[i].beat) : pts[i].beat, v, pts[i].curve});
    }
    // Stable: two points on one beat are a jump, and their order is its direction.
    std::stable_sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    if (undoCheckpoint) republishWithTrack(trackId, nt);
    else
        republishWithTrackRaw(trackId, nt);
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

// --- master Tempo lane -> tempo map (specs/099) -----------------------------

int32_t Engine::tempoLaneSelfTest() {
    auto storage = std::make_unique<Engine>();
    Engine& e = *storage;
    e.transport_.setSampleRate(48000);
    e.transport_.setBpm(120);
    e.authoringBpm_ = 120;
    const int32_t master = kMasterTrackId;
    const int32_t tempoTarget = static_cast<int32_t>(AutomationTarget::Tempo);
    auto lanePoints = [&]() -> std::vector<AutomationPoint> {
        const TrackLane* l = masterTempoLane(*e.authoring_);
        return l ? l->points : std::vector<AutomationPoint>{};
    };
    auto inSync = [&]() {
        const auto pts = lanePoints();
        const TempoMap* m = e.tempoMap();
        if (pts.empty()) return m == nullptr;
        if (!m || m->source.size() != pts.size()) return false;
        for (size_t i = 0; i < pts.size(); ++i)
            if (m->source[i].beat != pts[i].beat || m->source[i].value != pts[i].value || m->source[i].curve != pts[i].curve) return false;
        return true;
    };
    auto set = [&](int32_t lane, std::initializer_list<NotaAutomationPoint> pts, bool live = false) {
        std::vector<NotaAutomationPoint> v(pts);
        return live ? e.setAutomationPointsLive(master, lane, v.data(), static_cast<int32_t>(v.size()))
                    : e.setAutomationPoints(master, lane, v.data(), static_cast<int32_t>(v.size()));
    };

    // 0: Tempo is master-only.
    const int32_t audio = e.addAudioTrack();
    if (e.addAutomationLane(audio, tempoTarget, -1, -1) != -1) return 1;
    const int32_t lane = e.addAutomationLane(master, tempoTarget, -1, -1);
    if (lane < 0 || e.tempoAutomated()) return 2; // an empty lane is no map

    // 1: the map follows the lane's own curve exactly.
    if (!set(lane, {{0, 120, 0.5f}, {16, 90, 0}})) return 3;
    TrackLane ref; ref.target = AutomationTarget::Tempo; ref.points = lanePoints();
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
        std::fabs(m->bpmAt(8.001) - 140) > 1e-9) return 8;
    if (std::fabs(e.beatToSeconds(8) - 4.8) > 1e-9 || std::fabs(e.beatToSeconds(12) - (4.8 + 4 * 60.0 / 140)) > 1e-9) return 9;

    // 4: values clamp to 20..300 on set.
    if (!set(lane, {{0, 10, 0}, {4, 400, 0}})) return 10;
    const auto clamped = lanePoints();
    if (clamped.size() != 2 || clamped[0].value != 20.0f || clamped[1].value != 300.0f) return 11;

    // 5: a drag as the editor does it — one checkpointed set, then live sets with no undo
    // step — re-derives on every set; undo/redo restore lane + map together.
    if (!set(lane, {{0, 120, 0}, {8, 60, 0}})) return 12;
    const double before = e.beatToSeconds(8);
    if (!set(lane, {{0, 120, 0}, {8, 180, 0}})) return 13;
    if (!set(lane, {{0, 120, 0}, {8, 200, 0}}, true) || !inSync() || std::fabs(e.beatToSeconds(8) - 6.0 * std::log(200.0 / 120.0)) > 1e-7) return 14; // linear BPM: 60·8/80·ln(200/120)
    const double after = e.beatToSeconds(8);
    if (!e.undo() || !inSync() || std::fabs(e.beatToSeconds(8) - before) > 1e-12) return 15;
    if (!e.redo() || !inSync() || std::fabs(e.beatToSeconds(8) - after) > 1e-12) return 16;

    // 6: an unwarped clip keeps its physical time when only the tempo changes.
    {
        auto t = e.findTrackAuthoring(audio);
        auto nt = e.cloneTrack(*t);
        AudioClip c; c.startBeat = 16; c.warpEnabled = false;
        nt->clips.push_back(c);
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
    if (!e.moveSection(1, 0) || !inSync() || e.tempoMap() != mapBefore || lanePoints().size() != lanePointsBefore.size()) return 19;

    // 8: removing the lane (or clearing it) removes the map; the transport tempo leads again.
    if (!e.removeAutomationLane(master, lane) || e.tempoMap() || e.tempoAutomated()) return 20;
    if (!e.setBpm(100)) return 21;
    return 0;
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
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[i].beat;
        out[i].value = pts[i].value;
        out[i].curve = pts[i].curve;
    }
    return n;
}

bool Engine::setMasterVolumeAutomation(const NotaAutomationPoint* pts, int32_t count, bool live) {
    if (!authoring_) return false;
    auto g = std::make_shared<Graph>(*authoring_); // copy tracks (shared) + sceneCount + masterVolume
    auto& lane = g->masterVolume;
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    if (live) publishRaw(std::move(g)); // a drag's later moves: heard at once, no undo step
    else publish(std::move(g));         // structural edit → undo checkpoint
    return true;
}

// --- per-clip volume envelope (M9 follow-up) -------------------------------

int32_t Engine::getAudioClipVolumeEnvelope(
    int32_t trackId, int32_t clipIndex, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (!audioClipRef(*t, clipIndex)) return 0;
    const auto& pts = (*audioClipRef(*t, clipIndex)).volumeEnvelope.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[i].beat;
        out[i].value = pts[i].value;
        out[i].curve = pts[i].curve;
    }
    return n;
}

bool Engine::setAudioClipVolumeEnvelope(
    int32_t trackId, int32_t clipIndex, const NotaAutomationPoint* pts, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = cloneTrack(*old);
    auto& lane = (*audioClipRef(*nt, clipIndex)).volumeEnvelope;
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt); // structural edit → undo checkpoint
    return true;
}

int32_t Engine::getAudioClipPanEnvelope(
    int32_t trackId, int32_t clipIndex, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Audio) return 0;
    if (!audioClipRef(*t, clipIndex)) return 0;
    const auto& pts = (*audioClipRef(*t, clipIndex)).panEnvelope.points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[i].beat;
        out[i].value = pts[i].value;
        out[i].curve = pts[i].curve;
    }
    return n;
}

bool Engine::setAudioClipPanEnvelope(
    int32_t trackId, int32_t clipIndex, const NotaAutomationPoint* pts, int32_t count) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Audio) return false;
    if (!audioClipRef(*old, clipIndex)) return false;
    auto nt = cloneTrack(*old);
    auto& lane = (*audioClipRef(*nt, clipIndex)).panEnvelope;
    lane.points.clear();
    lane.points.reserve(count);
    for (int32_t i = 0; i < count; ++i) lane.points.push_back({pts[i].beat, pts[i].value, pts[i].curve});
    std::sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    syncSessionAudioSlot(*nt, clipIndex);
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::getMidiClipEnvelope(
    int32_t trackId, int32_t clipIndex, int32_t kind, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->midiClips.size())) return 0;
    const MidiClip& c = t->midiClips[clipIndex];
    const auto& pts = (kind == 1 ? c.volumeEnvelope : c.velocityEnvelope).points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[i].beat;
        out[i].value = pts[i].value;
        out[i].curve = pts[i].curve;
    }
    return n;
}

bool Engine::setMidiClipEnvelope(
    int32_t trackId, int32_t clipIndex, int32_t kind, const NotaAutomationPoint* pts, int32_t count) {
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

// --- clip controller curves (specs/020) ---------------------------------------
static const MidiClip* ctlClipOf(const Track& t, bool slot, int32_t index) {
    if (slot) {
        if (index < 0 || index >= static_cast<int32_t>(t.sessionSlots.size()) || !t.sessionSlots[index].hasClip)
            return nullptr;
        return &t.sessionSlots[index].midi;
    }
    if (index < 0 || index >= static_cast<int32_t>(t.midiClips.size())) return nullptr;
    return &t.midiClips[index];
}

int32_t Engine::ctlList(int32_t trackId, bool slot, int32_t index, int32_t* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
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

int32_t Engine::ctlGet(
    int32_t trackId, bool slot, int32_t index, int32_t ctl, NotaAutomationPoint* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    const MidiClip* c = t ? ctlClipOf(*t, slot, index) : nullptr;
    const ControllerCurve* cv = c ? c->controller(ctl) : nullptr;
    if (!cv) return 0;
    const auto& pts = cv->points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[i].beat;
        out[i].value = pts[i].value;
        out[i].curve = pts[i].curve;
    }
    return n;
}

bool Engine::ctlSet(
    int32_t trackId, bool slot, int32_t index, int32_t ctl, const NotaAutomationPoint* pts, int32_t count) {
    if (ctlIsReserved(ctl) || count < 0 || (count > 0 && !pts)) return false;
    auto old = findTrackAuthoring(trackId);
    if (!old || !ctlClipOf(*old, slot, index)) return false;
    auto nt = cloneTrack(*old);
    MidiClip& c = slot ? nt->sessionSlots[index].midi : nt->midiClips[index];
    std::vector<AutomationPoint> p;
    p.reserve(count);
    for (int32_t i = 0; i < count; ++i)
        p.push_back({pts[i].beat, ctlClamp(ctl, pts[i].value), std::clamp(pts[i].curve, -1.0f, 1.0f)});
    c.setController(ctl, std::move(p));
    republishWithTrack(trackId, nt);
    return true;
}

float Engine::deliveredController(int32_t trackId, int32_t ctl) const {
    if (ctl < 0 || ctl >= kCtlCount) return std::numeric_limits<float>::quiet_NaN();
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->instrument) return std::numeric_limits<float>::quiet_NaN();
    return t->instrument->deliveredCtl[ctl].load(std::memory_order_relaxed);
}

// Device-free checks of the controller curves (specs/020): numbered, 0 = pass. Runs on the
// message thread with private state (the fold scratch is saved and restored).
int32_t Engine::midiCtlSelfTest() {
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
        clipCtlEvents(c, 0.0, 400.0, 0.0, 256, 100.0, false, ev, n, 256); // spb 100: beat 1 = sample 100
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
        clipCtlEvents(c, 0.0, 400.0, 250.0, 64, 100.0, true, ev, n, 256);
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
        clipCtlEndResets(d, 0.0, 200.0, 150.0, 100, ev, n, 256);
        if (n != 1 || ev[0].off != 50 || ev[0].dim != kCtlDimBase + kCtlSustain || ev[0].value != kCtlRest) return 6;
    }
    // 7-8: the recording fold.
    auto savedFold = ctlFold_;
    auto savedBatch = ctlBatch_;
    auto savedTake = takeCtls_;
    const bool savedStop = ctlStopPending_;
    const double savedStopAt = ctlStopAt_;
    int32_t fail = 0;
    {
        // 7: overdub — pass 1 moves the mod wheel from beat 2: it replaces from there to the
        // pass end; pass 0's earlier point stays.
        for (auto& f : ctlFold_) f = CtlFold{};
        ctlBatch_.clear();
        takeCtls_.clear();
        ctlStopPending_ = false;
        MidiClip c;
        c.lengthBeats = 4.0;
        c.setController(kCtlModWheel, {{0.2, 0.3f, 0.0f}, {2.5, 0.3f, 0.0f}, {3.5, 0.1f, 0.0f}});
        ctlBatch_ = {{kCtlModWheel, 2.0, 0.9f, 1}, {kCtlModWheel, 3.0, 0.9f, 1}, {-1, 4.0, 0.0f, 1}};
        foldRecordedCtl(c, 0.0);
        const ControllerCurve* m = c.controller(kCtlModWheel);
        if (!m || !near(m->valueAt(1.0), 0.3f) || !near(m->valueAt(3.0), 0.9f) || !near(m->valueAt(3.8), 0.9f))
            fail = 7;
        // 8: a take that stops at beat 4 hands back to what the clip held after it.
        if (!fail) {
            for (auto& f : ctlFold_) f = CtlFold{};
            MidiClip e;
            e.lengthBeats = 8.0;
            e.setController(kCtlModWheel, {{0.0, 0.2f, 0.0f}, {6.0, 0.2f, 0.0f}});
            ctlBatch_ = {{kCtlModWheel, 2.0, 0.8f, 0}, {kCtlModWheel, 3.0, 0.8f, 0}};
            ctlStopPending_ = true;
            ctlStopAt_ = 4.0;
            foldRecordedCtl(e, 0.0);
            const ControllerCurve* w = e.controller(kCtlModWheel);
            if (!w || !near(w->valueAt(1.0), 0.2f) || !near(w->valueAt(2.5), 0.8f) || !near(w->valueAt(4.0), 0.2f) ||
                !near(w->valueAt(7.0), 0.2f))
                fail = 8;
        }
        // 9: the same take over a curved segment (#591): the old curve keeps its shape before
        // the take and from the hand-back at beat 4 on, not a straight line.
        if (!fail) {
            for (auto& f : ctlFold_) f = CtlFold{};
            ControllerCurve was;
            was.ctl = kCtlModWheel;
            was.points = {{0.0, 0.0f, -0.8f}, {8.0, 1.0f, 0.0f}};
            MidiClip e;
            e.lengthBeats = 8.0;
            e.setController(kCtlModWheel, was.points);
            ctlBatch_ = {{kCtlModWheel, 2.0, 0.8f, 0}, {kCtlModWheel, 3.0, 0.8f, 0}};
            ctlStopPending_ = true;
            ctlStopAt_ = 4.0;
            foldRecordedCtl(e, 0.0);
            const ControllerCurve* w = e.controller(kCtlModWheel);
            if (!w) fail = 9;
            else
                for (double b : {0.5, 1.0, 1.9, 4.5, 5.0, 6.0, 7.0, 7.9})
                    if (!near(w->valueAt(b), was.valueAt(b))) fail = 9;
        }
        // 10: a slice of a curved segment keeps its shape at both edges.
        if (!fail) {
            ControllerCurve c;
            c.ctl = kCtlModWheel;
            c.points = {{0.0, 0.0f, 0.7f}, {4.0, 1.0f, -0.5f}, {8.0, 0.2f, 0.0f}};
            const double from = 1.0, to = 6.0;
            ControllerCurve s;
            s.ctl = kCtlModWheel;
            s.points = sliceCtlPoints(c, from, to);
            for (double b = from; b <= to; b += 0.25)
                if (!near(s.valueAt(b - from), c.valueAt(b))) fail = 10;
        }
    }
    ctlFold_ = savedFold;
    ctlBatch_ = savedBatch;
    takeCtls_ = savedTake;
    ctlStopPending_ = savedStop;
    ctlStopAt_ = savedStopAt;
    return fail;
}

float Engine::masterPan() const {
    auto m = findTrackAuthoring(kMasterTrackId);
    return m ? m->pan() : 0.0f;
}

// specs/099 PR 2: master Pan and master device lanes play back; the master's Volume stays
// the graph-level lane; removing a master device drops its lanes and undo brings both back.
int32_t Engine::masterAutomationSelfTest() {
    auto storage = std::make_unique<Engine>();
    Engine& e = *storage;
    const int32_t m = kMasterTrackId;
    const int32_t panT = static_cast<int32_t>(AutomationTarget::Pan);
    const int32_t devT = static_cast<int32_t>(AutomationTarget::DeviceParam);
    auto set = [&](int32_t lane, float a, float b) {
        NotaAutomationPoint pts[2] = {{0.0, a, 0.0f}, {4.0, b, 0.0f}};
        return e.setAutomationPoints(m, lane, pts, 2);
    };
    auto near = [](float a, float b) { return std::fabs(a - b) <= 1e-4f; };

    // 1: the master has no Volume / MIDI-effect lane (Volume is the graph-level lane).
    if (e.addAutomationLane(m, static_cast<int32_t>(AutomationTarget::Volume), -1, -1) != -1) return 1;
    if (e.addAutomationLane(m, static_cast<int32_t>(AutomationTarget::MidiDeviceParam), 0, 0) != -1) return 1;
    // 2: setter / getter clamp to -1..1.
    e.setMasterPan(-3.0f);
    if (!near(e.masterPan(), -1.0f)) return 2;
    e.setMasterPan(0.25f);
    if (!near(e.masterPan(), 0.25f)) return 2;
    // 3: a master Pan lane drives the master pan.
    const int32_t pan = e.addAutomationLane(m, panT, -1, -1);
    if (pan < 0 || !set(pan, -1.0f, 1.0f)) return 3;
    e.applyAutomation(e.authoring_.get(), 2.0);
    if (!near(e.masterPan(), 0.0f)) return 3;
    e.applyAutomation(e.authoring_.get(), 0.0);
    if (!near(e.masterPan(), -1.0f)) return 3;
    // 4: a master device parameter lane drives the master device.
    const int32_t dev = e.addTrackBuiltinDevice(m, 0); // EQ
    if (dev < 0) return 4;
    const int32_t dl = e.addAutomationLane(m, devT, dev, 1);
    if (dl < 0 || !set(dl, 0.1f, 0.9f)) return 4;
    e.applyAutomation(e.authoring_.get(), 0.0);
    const float p0 = e.deviceGetParam(m, dev, 1);
    e.applyAutomation(e.authoring_.get(), 4.0);
    const float p4 = e.deviceGetParam(m, dev, 1);
    if (p0 == p4) return 4;
    // 5: removing the master device drops its lane; undo restores device and lane.
    if (e.automationLaneCount(m) != 2) return 5;
    if (!e.removeDevice(m, dev) || e.automationLaneCount(m) != 1 || e.trackDeviceCount(m) != 0) return 5;
    if (!e.undo() || e.automationLaneCount(m) != 2 || e.trackDeviceCount(m) != 1) return 6;
    return 0;
}

} // namespace nota
