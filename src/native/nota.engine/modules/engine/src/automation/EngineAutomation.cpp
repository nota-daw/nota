// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationOps — parameter automation read/apply, plugin param surface, and nested paths.

#include "automation/AutomationOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "model/ParamPath.h"
#include "model/ParamResolve.h"
#include "model/Track.h"
#include "nota/devices/Register.h"
#include "render/RenderRoutingPlan.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

// --- parameter automation (M9) ---------------------------------------------
// One track's lanes at `beat` (block rate, audio thread, allocation-free). Shared by the
// tracks and the master (specs/099): master Pan writes the master pan stage's value, master
// device / plug-in lanes drive the master chain; Tempo plays through the tempo map instead.
// `beatEnd` is the block's end, for the instruments that read a lane at their own steps
// (pluginParamLane, specs/100).
static void applyLaneEndpoint(
    Track& t, const TrackLane& lane, const RenderRoutingPlan::CompiledParamEndpoint& ep, float v) {
    using Kind = RenderRoutingPlan::CompiledParamEndpoint::Kind;
    switch (ep.kind) {
    case Kind::Invalid: return;
    case Kind::Volume: t.setVolume(v); return;
    case Kind::Pan: t.setPan(v); return;
    case Kind::DeviceGeneric:
        if (ep.device) ep.device->setLiveParam(ep.paramIndex, v);
        return;
    case Kind::MidiDeviceGeneric:
        if (ep.midi) ep.midi->setParam(ep.paramIndex, v);
        return;
    case Kind::InstrumentPlugin:
        if (ep.instrument) ep.instrument->pluginParamSet(ep.paramIndex, v);
        return;
    case Kind::DevicePlugin:
        if (ep.device) ep.device->pluginParamSet(ep.paramIndex, v);
        return;
    case Kind::NestedRackDeviceGeneric:
    case Kind::NestedRackMidiGeneric:
    case Kind::NestedRackDevicePlugin:
    case Kind::NestedRackInstrumentPlugin: {
        Device* d = ep.device;
        MidiDevice* m = ep.midi;
        Instrument* inst = ep.instrument;
        if (!ep.rack || ep.rack->liveGeneration() != ep.expectedRackGen) {
            const ParamOwner o = resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot);
            d = o.device;
            m = o.midi;
            inst = o.instrument;
        }
        if (lane.target == AutomationTarget::PluginParam) {
            if (inst) inst->pluginParamSet(lane.paramIndex, v);
            else if (d)
                d->pluginParamSet(lane.paramIndex, v);
        } else if (d) {
            d->setLiveParam(lane.paramIndex, v);
        } else if (m) {
            m->setParam(lane.paramIndex, v);
        }
        return;
    }
    }
}

static void applyCompiledTrackLanes(Track& t, const std::vector<RenderRoutingPlan::CompiledParamEndpoint>& targets,
    bool hasStepLanes, double beat, double beatEnd) {
    const size_t n = std::min(t.automation.size(), targets.size());
    for (size_t i = 0; i < n; ++i) {
        const TrackLane& lane = t.automation[i];
        if (lane.points.empty() || lane.suppressRead || lane.target == AutomationTarget::Tempo) continue;
        applyLaneEndpoint(t, lane, targets[i], lane.valueAt(beat));
    }
    if (!hasStepLanes) return;
    for (size_t i = 0; i < n; ++i) {
        const TrackLane& lane = t.automation[i];
        if (lane.points.empty() || lane.suppressRead || lane.target != AutomationTarget::PluginParam ||
            lane.paramIndex < 0)
            continue;
        const auto& ep = targets[i];
        Instrument* inst = nullptr;
        if (ep.nested) {
            inst = (ep.rack && ep.rack->liveGeneration() == ep.expectedRackGen)
                       ? ep.instrument
                       : resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot).instrument;
        } else if (lane.deviceIndex < 0) {
            inst = ep.instrument ? ep.instrument : t.instrument.get();
        }
        if (inst) inst->pluginParamLane(lane.paramIndex, lane, beat, beatEnd);
    }
}

static void applyTrackLanes(Track& t, double beat, double beatEnd) {
    for (const TrackLane& lane : t.automation) {
        if (lane.points.empty() || lane.suppressRead) continue; // M9-C: control leads while writing
        if (lane.target == AutomationTarget::Tempo) continue;   // specs/099: plays through the tempo map
        const float v = lane.valueAt(beat);
        if (lane.nested()) { // a device grouped into a rack (specs/044)
            const ParamOwner o = resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot);
            if (lane.target == AutomationTarget::PluginParam) {
                if (lane.paramIndex < 0) continue;
                if (o.instrument) o.instrument->pluginParamSet(lane.paramIndex, v);
                else if (o.device)
                    o.device->pluginParamSet(lane.paramIndex, v);
            } else if (o.device)
                o.device->setLiveParam(lane.paramIndex, v);
            else if (o.midi)
                o.midi->setParam(lane.paramIndex, v);
            continue;
        }
        switch (lane.target) {
        case AutomationTarget::Tempo: break;
        case AutomationTarget::Volume: t.setVolume(v); break;
        case AutomationTarget::Pan: t.setPan(v); break;
        case AutomationTarget::Send: break; // specs/069: a send lane does not move the level
        case AutomationTarget::DeviceParam:
            if (lane.deviceIndex >= 0 && lane.deviceIndex < static_cast<int32_t>(t.devices.size()))
                if (auto* d = t.devices[static_cast<size_t>(lane.deviceIndex)].get())
                    d->setLiveParam(lane.paramIndex, v);
            break;
        case AutomationTarget::MidiDeviceParam: // deviceIndex = MIDI-FX chain index
            if (lane.deviceIndex >= 0 && lane.deviceIndex < static_cast<int32_t>(t.midiEffects.size()))
                if (auto* m = t.midiEffects[static_cast<size_t>(lane.deviceIndex)].get())
                    m->setParam(lane.paramIndex, v);
            break;
        case AutomationTarget::PluginParam:
            // Normalized 0..1 into a hosted plugin (M9-B). deviceIndex < 0
            // targets the instrument; paramIndex is pre-resolved from paramId.
            if (lane.paramIndex < 0) break;
            if (lane.deviceIndex < 0) {
                if (t.instrument) t.instrument->pluginParamSet(lane.paramIndex, v);
            } else if (lane.deviceIndex < static_cast<int32_t>(t.devices.size())) {
                if (auto* d = t.devices[static_cast<size_t>(lane.deviceIndex)].get())
                    d->pluginParamSet(lane.paramIndex, v);
            }
            break;
        }
    }
    // Exact-time lanes (specs/100), once every lane above has set its block-start value: an
    // instrument samples a lane on its step grid, and the grid can be another param of the
    // same block (Twin's Rate), so the order the lanes were added in must not matter.
    for (const TrackLane& lane : t.automation) {
        if (lane.points.empty() || lane.suppressRead || lane.target != AutomationTarget::PluginParam ||
            lane.paramIndex < 0)
            continue;
        Instrument* inst = nullptr;
        if (lane.nested()) inst = resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot).instrument;
        else if (lane.deviceIndex < 0)
            inst = t.instrument.get();
        if (inst) inst->pluginParamLane(lane.paramIndex, lane, beat, beatEnd);
    }
}

void AutomationOps::applyMasterLanes(Track& master, double beat) {
    applyTrackLanes(master, beat, beat);
}

const TrackLane* AutomationOps::masterPanLane(const Track& master) {
    for (const TrackLane& l : master.automation)
        if (l.target == AutomationTarget::Pan && !l.nested() && !l.points.empty() && !l.suppressRead) return &l;
    return nullptr;
}

void AutomationOps::applyAutomation(Graph* g, double beat, double beatEnd) {
    if (beatEnd < beat) beatEnd = beat;
    if (const RenderRoutingPlan* plan = g ? g->routingPlan.get() : nullptr) {
        for (const auto& cat : plan->automatedTracks())
            if (cat.track)
                applyCompiledTrackLanes(*cat.track, cat.laneTargets, cat.hasAnyPluginStepLane, beat, beatEnd);
        if (g->masterTrack && !g->masterTrack->automation.empty())
            applyCompiledTrackLanes(*g->masterTrack, plan->masterLaneTargets(), false, beat, beatEnd);
    } else {
        // Fallback when no routing plan is attached (e.g. unit self-tests).
        for (auto& tptr : g->tracks)
            if (!tptr->automation.empty()) applyTrackLanes(*tptr, beat, beatEnd);
        if (g->masterTrack && !g->masterTrack->automation.empty()) applyTrackLanes(*g->masterTrack, beat, beatEnd);
    }
    // Master-volume lane (graph-level, M9 follow-up): write the master gain atomic.
    if (!g->masterVolume.points.empty() && !g->masterVolume.suppressRead)
        rt_.masterVolume_.store(std::clamp(g->masterVolume.valueAt(beat), 0.0f, 2.0f), std::memory_order_relaxed);
}

bool AutomationOps::automationSelfTest() {
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
    t->automation.write().push_back(lane);
    g->tracks.push_back(t);
    applyAutomation(g.get(), 2.0);
    return std::fabs(t->volume() - 0.5f) <= 1e-4f;
}

bool AutomationOps::automationCurveSelfTest() {
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

int32_t AutomationOps::pluginParamCount(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0;
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamCount() : 0;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return 0;
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamCount() : 0;
}
std::string AutomationOps::pluginParamId(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamId(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamId(paramIndex) : std::string{};
}
int32_t AutomationOps::pluginParamIndexFromV1(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || deviceIndex >= 0 || !t->instrument ||
        t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::Twin))
        return paramIndex;
    return devices::twinParamFromV1(paramIndex); // Twin's side blocks grew (#531)
}
std::string AutomationOps::pluginParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamName(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamName(paramIndex) : std::string{};
}
std::string AutomationOps::pluginParamText(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamText(paramIndex) : std::string{};
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamText(paramIndex) : std::string{};
}
// Stepped params (specs/100): only an instrument declares them; effects' plugin params stay
// continuous for the lane editor.
int32_t AutomationOps::pluginParamSteps(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && deviceIndex < 0 && t->instrument ? t->instrument->pluginParamSteps(paramIndex) : 0;
}
std::string AutomationOps::pluginParamStepText(
    int32_t trackId, int32_t deviceIndex, int32_t paramIndex, int32_t step) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && deviceIndex < 0 && t->instrument ? t->instrument->pluginParamStepText(paramIndex, step) : std::string{};
}
float AutomationOps::pluginParamDefault(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    if (deviceIndex < 0) {
        if (!t->instrument) return 0.0f;
        // A built-in instrument has no plugin default of its own (the base returns 0, which
        // sent a graph's "Reset to default" to 0); its default is a fresh instance's value.
        if (t->instrument->kind() >= 0) return rt_.devices().instrumentParamDefault(trackId, paramIndex);
        return t->instrument->pluginParamDefault(paramIndex);
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return 0.0f;
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamDefault(paramIndex) : 0.0f;
}
float AutomationOps::pluginParamGet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamGet(paramIndex) : 0.0f;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return 0.0f;
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamGet(paramIndex) : 0.0f;
}
void AutomationOps::pluginParamSet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float normalized) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    // Instrument param that's a CV target → edit routes to the link base (CvTargetKind::Instrument).
    if (deviceIndex < 0) routeCvBaseEdit(1, trackId, -1, paramIndex, normalized);
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->pluginParamSet(paramIndex, normalized);
        return;
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return;
    if (auto& d = t->devices[static_cast<size_t>(deviceIndex)]) d->pluginParamSet(paramIndex, normalized);
}
int32_t AutomationOps::pluginParamIndexOfId(int32_t trackId, int32_t deviceIndex, const std::string& id) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return -1;
    if (deviceIndex < 0) return t->instrument ? t->instrument->pluginParamIndexOfId(id) : -1;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return -1;
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->pluginParamIndexOfId(id) : -1;
}

int32_t AutomationOps::lastTouchedPluginParam(int32_t trackId, int32_t deviceIndex) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return -1;
    if (deviceIndex < 0) return t->instrument ? t->instrument->lastTouchedPluginParam() : -1;
    if (deviceIndex >= static_cast<int32_t>(t->devices.size())) return -1;
    auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    return d ? d->lastTouchedPluginParam() : -1;
}

int32_t AutomationOps::addPluginAutomationLane(int32_t trackId, int32_t deviceIndex, const char* paramId) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    const std::string id = paramId ? paramId : "";
    // Reuse an existing lane on the same (device, param).
    for (size_t i = 0; i < old->automation.size(); ++i) {
        const TrackLane& l = old->automation[i];
        if (!l.nested() && l.target == AutomationTarget::PluginParam && l.deviceIndex == deviceIndex && l.paramId == id)
            return static_cast<int32_t>(i);
    }
    auto nt = rt_.cloneTrack(*old);
    TrackLane lane;
    lane.target = AutomationTarget::PluginParam;
    lane.deviceIndex = deviceIndex;
    lane.paramId = id;
    lane.paramIndex = pluginParamIndexOfId(trackId, deviceIndex, id); // resolve now; -1 if absent
    nt->automation.write().push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return index;
}

// ---- nested addresses for save / load (specs/044) ---------------------------
uint64_t AutomationOps::nestedChainUid(int32_t trackId, int32_t host, int32_t chainIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const RackCore* r = t ? rackAtHost(*t, host) : nullptr;
    return r ? r->chainBaseUid(chainIndex) : 0;
}
int32_t AutomationOps::nestedChainIndex(int32_t trackId, int32_t host, uint64_t uid) const {
    auto t = rt_.findTrackAuthoring(trackId);
    const RackCore* r = t ? rackAtHost(*t, host) : nullptr;
    return r ? r->chainIndexOfUid(uid) : -1;
}

bool AutomationOps::automationLanePath(int32_t trackId, int32_t laneIndex, int32_t* chainIndex, int32_t* slot) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return false;
    const TrackLane& l = t->automation[static_cast<size_t>(laneIndex)];
    if (chainIndex) *chainIndex = l.nested() ? nestedChainIndex(trackId, l.deviceIndex, l.chainUid) : -1;
    if (slot) *slot = l.nested() ? l.slot : -1;
    return true;
}

int32_t AutomationOps::addNestedAutomationLane(int32_t trackId, int32_t target, int32_t host, int32_t chainIndex,
    int32_t slot, int32_t paramIndex, const char* paramId) {
    auto old = rt_.findTrackAuthoring(trackId);
    const uint64_t uid = nestedChainUid(trackId, host, chainIndex);
    if (!old || uid == 0) return -1;
    const auto tgt = static_cast<AutomationTarget>(target);
    if (tgt != AutomationTarget::DeviceParam && tgt != AutomationTarget::MidiDeviceParam &&
        tgt != AutomationTarget::PluginParam)
        return -1;
    if (tgt == AutomationTarget::DeviceParam) {
        const auto owner = resolveNested(*old, host, uid, slot);
        if (owner.device && owner.device->isLatencyConfigurationParam(paramIndex)) return -1;
    }
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
                          : o.device   ? o.device->pluginParamIndexOfId(lane.paramId)
                                       : -1;
    }
    for (size_t i = 0; i < old->automation.size(); ++i) { // one lane per target
        const TrackLane& l = old->automation[i];
        if (l.target == tgt && l.deviceIndex == host && l.chainUid == uid && l.slot == slot &&
            (tgt == AutomationTarget::PluginParam ? l.paramId == lane.paramId : l.paramIndex == paramIndex))
            return static_cast<int32_t>(i);
    }
    auto nt = rt_.cloneTrack(*old);
    nt->automation.write().push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return index;
}

std::string AutomationOps::automationLaneParamId(int32_t trackId, int32_t laneIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return {};
    return t->automation[static_cast<size_t>(laneIndex)].paramId;
}

} // namespace nota::detail
