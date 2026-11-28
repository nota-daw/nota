// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationOps — write/record, lane CRUD, device index remapping, and self-tests.

#include "automation/AutomationOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
#include "recording/RecordingOps.h"
#include "model/EngineTypes.h"
#include "model/ParamPath.h"
#include "model/Track.h"
#include "nota/devices/DeviceIds.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <initializer_list>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

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
    default: return true;                         // Volume / Pan
    }
}
} // namespace

float AutomationOps::currentTargetValue(const Track& t, int32_t target, int32_t deviceIndex, int32_t paramIndex) const {
    switch (static_cast<AutomationTarget>(target)) {
    case AutomationTarget::Tempo: return 0.0f;
    case AutomationTarget::Volume: return t.volume();
    case AutomationTarget::Pan: return t.pan();
    case AutomationTarget::Send: return t.send(deviceIndex);
    case AutomationTarget::DeviceParam:
        if (deviceIndex >= 0 && deviceIndex < static_cast<int32_t>(t.devices.size()))
            if (auto* d = t.devices[static_cast<size_t>(deviceIndex)].get()) return d->getParam(paramIndex);
        return 0.0f;
    case AutomationTarget::MidiDeviceParam:
        if (deviceIndex >= 0 && deviceIndex < static_cast<int32_t>(t.midiEffects.size()))
            if (auto* m = t.midiEffects[static_cast<size_t>(deviceIndex)].get()) return m->getParam(paramIndex);
        return 0.0f;
    case AutomationTarget::PluginParam:
        if (deviceIndex < 0) return t.instrument ? t.instrument->pluginParamGet(paramIndex) : 0.0f;
        if (deviceIndex < static_cast<int32_t>(t.devices.size()))
            if (auto* d = t.devices[static_cast<size_t>(deviceIndex)].get()) return d->pluginParamGet(paramIndex);
        return 0.0f;
    }
    return 0.0f;
}

void AutomationOps::setLaneSuppressRead(int32_t trackId, int32_t laneIndex, bool suppress) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return;
    if (old->automation[static_cast<size_t>(laneIndex)].suppressRead == suppress) return;
    auto nt = rt_.cloneTrack(*old);
    nt->automation.write()[static_cast<size_t>(laneIndex)].suppressRead = suppress;
    rt_.republishWithTrackRaw(trackId, nt); // transient flag — never an undo step
}

void AutomationOps::setAutomationRecord(bool on) {
    rt_.autoRecord_.store(on, std::memory_order_relaxed);
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
void AutomationOps::beginParamTouch(
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
    for (const auto& tg : rt_.touchGestures_)
        if (tg.trackId == trackId &&
            sameTarget(tg.target, tg.deviceIndex, tg.paramIndex, tg.paramId, target, deviceIndex, paramIndex, id))
            return; // already open
    const MixTarget mt{kind, trackId, deviceIndex, paramIndex};
    if (const int32_t h = rt_.beginMixGesture(&mt, 1))
        rt_.touchGestures_.push_back({trackId, target, deviceIndex, paramIndex, id, h});
}

void AutomationOps::endParamTouch(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const std::string& id) {
    for (size_t i = 0; i < rt_.touchGestures_.size(); ++i) {
        const auto& tg = rt_.touchGestures_[i];
        // PluginParam touches match by id (sameTarget ignores the index begin resolved).
        if (tg.trackId != trackId ||
            !sameTarget(tg.target, tg.deviceIndex, tg.paramIndex, tg.paramId, target, deviceIndex, paramIndex, id))
            continue;
        const int32_t h = tg.handle;
        rt_.touchGestures_.erase(rt_.touchGestures_.begin() + static_cast<std::ptrdiff_t>(i));
        rt_.endMixGesture(h);
        return;
    }
}

void AutomationOps::beginAutomationWrite(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId, bool latch) {
    const std::string id = paramId ? paramId : "";
    // Open the undo gesture first, so a checkpoint the write pushes below (recording) merges
    // into it and the lane edit and the value undo as one step.
    if (!latch) beginParamTouch(trackId, target, deviceIndex, paramIndex, id);
    for (auto& aw : rt_.activeWrites_) // already writing this target?
        if (aw.trackId == trackId &&
            sameTarget(aw.target, aw.deviceIndex, aw.paramIndex, aw.paramId, target, deviceIndex, paramIndex, id))
            return;

    if (!rt_.autoRecord_.load(std::memory_order_relaxed)) {
        // Not recording: the lane keeps playing back unless it actually has automation,
        // in which case the hand takes over (override) until reenableAutomation().
        auto t = rt_.findTrackAuthoring(trackId);
        if (!t) return;
        const int32_t lane = findLaneFor(*t, target, deviceIndex, paramIndex, id);
        if (lane < 0 || t->automation[static_cast<size_t>(lane)].points.empty()) return;
        for (auto& o : rt_.overrides_)
            if (o.trackId == trackId && o.laneIndex == lane) return;
        setLaneSuppressRead(trackId, lane, true);
        rt_.overrides_.push_back({trackId, lane});
        return;
    }

    // Writing a lane during a take is part of the take (specs/078): remember the track as it was
    // before, and mark the checkpoints below (a new lane, the pre-write state) as the take's.
    rt_.recorder().noteTakeTrack(trackId);
    EngineRuntime::TakePushScope takePush(rt_);

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
    for (size_t i = 0; i < rt_.overrides_.size(); ++i)
        if (rt_.overrides_[i].trackId == trackId && rt_.overrides_[i].laneIndex == laneIndex) {
            rt_.overrides_.erase(rt_.overrides_.begin() + static_cast<std::ptrdiff_t>(i));
            break;
        }
    // Checkpoint the clean pre-write state once per write session (concurrent
    // targets share one undo step); subsequent grows are raw.
    if (rt_.activeWrites_.empty()) rt_.pushUndo();
    setLaneSuppressRead(trackId, laneIndex, true);
    const double beat = rt_.transport_.uiPositionBeats();
    ActiveWrite aw{trackId, target, deviceIndex, resolvedParam, id, laneIndex, beat, latch};
    if (auto tw = rt_.findTrackAuthoring(trackId))
        aw.before = currentTargetValue(*tw, target, deviceIndex, resolvedParam);
    rt_.activeWrites_.push_back(std::move(aw));
}

void AutomationOps::endAutomationWrite(
    int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId) {
    // A released mouse gesture stops writing here; a latched one (hardware control)
    // keeps writing its last value until the transport stops (finishAllWrites).
    const std::string id = paramId ? paramId : "";
    endParamTouch(trackId, target, deviceIndex, paramIndex, id); // the value-undo step (specs/016)
    for (size_t i = 0; i < rt_.activeWrites_.size(); ++i) {
        auto& aw = rt_.activeWrites_[i];
        if (aw.trackId == trackId &&
            sameTarget(aw.target, aw.deviceIndex, aw.paramIndex, aw.paramId, target, deviceIndex, paramIndex, id)) {
            if (aw.latch) return;
            // A discrete edit (a click: a bank button, a mode chip) begins and ends its write before
            // poll() samples it, so it would leave the lane as it was. Record it at the playhead.
            if (!aw.sampled && rt_.autoRecord_.load(std::memory_order_relaxed) && rt_.transport_.uiIsPlaying())
                recordAutomationClick(aw, rt_.transport_.uiPositionBeats());
            setLaneSuppressRead(aw.trackId, aw.laneIndex, false);
            rt_.activeWrites_.erase(rt_.activeWrites_.begin() + static_cast<std::ptrdiff_t>(i));
            return;
        }
    }
}

void AutomationOps::finishAllWrites() {
    for (auto& aw : rt_.activeWrites_) setLaneSuppressRead(aw.trackId, aw.laneIndex, false);
    rt_.activeWrites_.clear();
}

void AutomationOps::reenableAutomation() {
    for (auto& o : rt_.overrides_) setLaneSuppressRead(o.trackId, o.laneIndex, false);
    rt_.overrides_.clear();
}

void AutomationOps::sampleAutomationWritesAt(double beat) {
    for (auto& aw : rt_.activeWrites_) {
        auto old = rt_.findTrackAuthoring(aw.trackId);
        if (!old || aw.laneIndex < 0 || aw.laneIndex >= static_cast<int32_t>(old->automation.size())) continue;
        const float v = currentTargetValue(*old, aw.target, aw.deviceIndex, aw.paramIndex);
        auto nt = rt_.cloneTrack(*old);
        auto& pts = nt->automation.write()[static_cast<size_t>(aw.laneIndex)].points;
        // Overwrite-sweep: drop points the playhead just passed over, then add the
        // fresh one. Backward motion (loop wrap / seek) resets the sweep origin.
        const double lo = std::min(aw.lastBeat, beat), hi = std::max(aw.lastBeat, beat);
        pts.erase(std::remove_if(
                      pts.begin(), pts.end(), [&](const AutomationPoint& p) { return p.beat > lo && p.beat <= hi; }),
            pts.end());
        pts.push_back({beat, v});
        std::sort(
            pts.begin(), pts.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
        rt_.republishWithTrackRaw(aw.trackId, nt); // grow the take, not an undo step
        aw.lastBeat = beat;
        aw.sampled = true;
    }
}

// A click while recording (specs/100): the lane's value just before the playhead, then the new
// value, both at the playhead — a step, so a stepped target (a Rhythm bank) switches right there
// instead of ramping in from the point before. Part of the write's undo step (begin pushed it).
void AutomationOps::recordAutomationClick(const ActiveWrite& aw, double beat) {
    auto old = rt_.findTrackAuthoring(aw.trackId);
    if (!old || aw.laneIndex < 0 || aw.laneIndex >= static_cast<int32_t>(old->automation.size())) return;
    const float v = currentTargetValue(*old, aw.target, aw.deviceIndex, aw.paramIndex);
    const auto& was = old->automation[static_cast<size_t>(aw.laneIndex)].points;
    auto nt = rt_.cloneTrack(*old);
    auto& pts = nt->automation.write()[static_cast<size_t>(aw.laneIndex)].points;
    // The step starts from what the lane plays there, or — a fresh lane — what the target was.
    // The segment the click cuts keeps its shape on both sides: the piece before it ends at the
    // step with the curve of that piece (AutomationLane::pieceCurve), so nothing before the
    // click plays differently, and the step runs on to the next point with the rest of it.
    float after = 0.0f;
    if (!was.empty()) {
        const auto next = std::upper_bound(
            was.begin(), was.end(), beat, [](double b, const AutomationPoint& q) { return b < q.beat; });
        if (next != was.begin() && next != was.end()) {
            const size_t prev = static_cast<size_t>(next - was.begin()) - 1;
            pts[prev].curve = AutomationLane::pieceCurve(was, was[prev].beat, beat);
            after = AutomationLane::pieceCurve(was, beat, next->beat);
        }
    }
    pts.push_back(
        {beat, was.empty() ? aw.before : old->automation[static_cast<size_t>(aw.laneIndex)].valueAt(beat), 0.0f});
    pts.push_back({beat, v, after});
    std::stable_sort(
        pts.begin(), pts.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    rt_.republishWithTrackRaw(aw.trackId, nt);
}

bool AutomationOps::automationWriteSelfTest() {
    // Device-free. Record on: a mouse gesture (latch=false) writes at the playhead and
    // stops on release; a latched gesture ignores the release and only a stop ends it.
    // Record off: touching an automated param overrides its lane until re-enable.
    rt_.reset();
    auto t = std::make_shared<Track>(1, TrackType::Instrument);
    t->instrument = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::Synth));
    auto g = std::make_shared<Graph>();
    g->tracks.push_back(t);
    rt_.publishRaw(g);

    // Mouse gesture on Volume while recording.
    setAutomationRecord(true);
    beginAutomationWrite(1, 0, -1, -1, "", false);
    if (rt_.findTrackAuthoring(1)->automation.empty()) return false;
    if (!rt_.findTrackAuthoring(1)->automation[0].suppressRead) return false;
    rt_.tracks().setTrackVolume(1, 1.5f); // by id — targets the live authoring track (as the UI does)
    sampleAutomationWritesAt(3.0);
    endAutomationWrite(1, 0, -1, -1, "");
    {
        auto tt = rt_.findTrackAuthoring(1);
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
    endAutomationWrite(1, 0, -1, -1, "");                                     // release — ignored when latched
    if (!rt_.findTrackAuthoring(1)->automation[0].suppressRead) return false; // still writing
    finishAllWrites();                                                        // transport stop
    if (rt_.findTrackAuthoring(1)->automation[0].suppressRead) return false;  // now cleared

    // Record off: a touch on a target with no automation must not create a lane...
    setAutomationRecord(false);
    const size_t lanes = rt_.findTrackAuthoring(1)->automation.size();
    beginAutomationWrite(1, 1, -1, -1, "", false); // Pan — no lane yet
    if (rt_.findTrackAuthoring(1)->automation.size() != lanes) return false;
    if (automationOverridden()) return false;
    // ...but on the automated Volume lane it overrides playback until re-enable.
    beginAutomationWrite(1, 0, -1, -1, "", false);
    if (!automationOverridden()) return false;
    if (!rt_.findTrackAuthoring(1)->automation[0].suppressRead) return false;
    endAutomationWrite(1, 0, -1, -1, ""); // release keeps the override
    if (!rt_.findTrackAuthoring(1)->automation[0].suppressRead) return false;
    reenableAutomation();
    if (automationOverridden()) return false;
    if (rt_.findTrackAuthoring(1)->automation[0].suppressRead) return false;

    rt_.reset();
    return true;
}

bool AutomationOps::pluginAutomationSelfTest() {
    // Device-free: a PluginParam lane on a non-plugin instrument (built-in Synth,
    // which exposes no plugin params) must store its fields and dispatch through
    // applyAutomation without crashing (the set is a harmless base no-op).
    auto g = std::make_shared<Graph>();
    auto t = std::make_shared<Track>(9998, TrackType::Instrument);
    t->instrument = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::Synth));
    TrackLane lane;
    lane.target = AutomationTarget::PluginParam;
    lane.deviceIndex = -1; // instrument
    lane.paramIndex = 0;
    lane.paramId = "test-param";
    lane.points = {{0.0, 0.0f}, {4.0, 1.0f}};
    t->automation.write().push_back(lane);
    g->tracks.push_back(t);
    applyAutomation(g.get(), 2.0); // must not crash
    return t->automation.size() == 1 && t->automation[0].target == AutomationTarget::PluginParam &&
           t->automation[0].paramId == "test-param" && std::fabs(lane.valueAt(2.0) - 0.5f) <= 1e-4f;
}

int32_t AutomationOps::addAutomationLane(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    const auto tgt = static_cast<AutomationTarget>(target);
    if (target < 0 || target > static_cast<int32_t>(AutomationTarget::Tempo)) return -1;
    if (tgt == AutomationTarget::Send) return -1; // specs/069: sends are not an automation target
    if (tgt == AutomationTarget::Tempo && trackId != kMasterTrackId) return -1; // specs/099: master only
    // specs/099: the master's Volume is the graph-level masterVolume lane; it has no MIDI chain.
    if (trackId == kMasterTrackId && (tgt == AutomationTarget::Volume || tgt == AutomationTarget::MidiDeviceParam))
        return -1;
    if (tgt == AutomationTarget::DeviceParam) {
        const auto* device = rt_.devices().deviceAt(trackId, deviceIndex);
        if (device && device->isLatencyConfigurationParam(paramIndex)) return -1;
    }
    const bool usesDev = tgt == AutomationTarget::DeviceParam || tgt == AutomationTarget::MidiDeviceParam;
    // Reuse an existing lane on the same target (no snapshot) — one lane per target.
    for (size_t i = 0; i < old->automation.size(); ++i) {
        const TrackLane& l = old->automation[i];
        if (!l.nested() && l.target == tgt &&
            (!usesDev || (l.deviceIndex == deviceIndex && l.paramIndex == paramIndex)))
            return static_cast<int32_t>(i);
    }
    auto nt = rt_.cloneTrack(*old);
    TrackLane lane;
    lane.target = tgt;
    lane.deviceIndex = usesDev ? deviceIndex : -1;
    lane.paramIndex = usesDev ? paramIndex : -1;
    nt->automation.write().push_back(std::move(lane));
    const int32_t index = static_cast<int32_t>(nt->automation.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return index;
}

// --- device edit → lane index fix-up (message thread) -----------------------

void AutomationOps::remapAutomationAfterDeviceChange(
    Track& nt, bool midi, int32_t removedIndex, int32_t from, int32_t to) {
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
    {
        auto& autos = nt.automation.write();
        for (size_t i = 0; i < autos.size(); ++i) {
            TrackLane& l = autos[i];
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
    }
    nt.automation.replace(std::move(kept));

    // In-flight gestures on this track address lanes by index and targets by device.
    const int32_t trackId = nt.id();
    auto laneGone = [&](int32_t& laneIndex) {
        if (laneIndex < 0 || laneIndex >= static_cast<int32_t>(laneMap.size())) return true;
        laneIndex = laneMap[static_cast<size_t>(laneIndex)];
        return laneIndex < 0;
    };
    std::erase_if(rt_.activeWrites_, [&](ActiveWrite& aw) {
        if (aw.trackId != trackId) return false;
        if (laneGone(aw.laneIndex)) return true;
        const auto tgt = static_cast<AutomationTarget>(aw.target);
        const bool chain = midi ? tgt == AutomationTarget::MidiDeviceParam
                                : tgt == AutomationTarget::DeviceParam ||
                                      (tgt == AutomationTarget::PluginParam && aw.deviceIndex >= 0);
        if (chain) aw.deviceIndex = remap(aw.deviceIndex);
        return false;
    });
    std::erase_if(rt_.overrides_, [&](OverrideTarget& o) { return o.trackId == trackId && laneGone(o.laneIndex); });
}

std::vector<TrackLane> AutomationOps::deviceLanes(const Track& t, int32_t family, int32_t index) const {
    std::vector<TrackLane> out;
    for (const auto& l : t.automation) {
        bool on = false;
        if (l.nested()) // lanes into a rack go where the rack goes (specs/044)
            on = family == 0   ? l.deviceIndex == index && index >= 0
                 : family == 1 ? l.deviceIndex == midiHost(index)
                               : family == 2 && l.deviceIndex == -1;
        else
            switch (family) {
            case 0:
                on = (l.target == AutomationTarget::DeviceParam || l.target == AutomationTarget::PluginParam) &&
                     l.deviceIndex == index;
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

bool AutomationOps::automationDeviceRemapSelfTest() {
    // Two effects, a lane on each: reordering the chain keeps every lane on its own
    // device; removing a device drops its lanes and shifts the rest.
    const int32_t tid = rt_.devices().addInstrumentTrack();
    const int32_t d0 = rt_.devices().addTrackBuiltinDevice(tid, 3); // Delay   @0
    const int32_t d1 = rt_.devices().addTrackBuiltinDevice(tid, 4); // Utility @1
    const int32_t d2 = rt_.devices().addTrackBuiltinDevice(tid, 2); // Reverb  @2
    if (d0 != 0 || d1 != 1 || d2 != 2) {
        rt_.tracks().removeTrack(tid);
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
    auto kindOf = [&](int32_t lane) { return rt_.devices().trackDeviceBuiltinKind(tid, devOf(lane)); };

    rt_.devices().moveDevice(tid, 0, 2); // Utility, Reverb, Delay
    bool ok = kindOf(0) == 3 && kindOf(1) == 4 && kindOf(2) == 2 && devOf(3) == -1;
    rt_.devices().moveDevice(tid, 2, 0); // Delay, Utility, Reverb
    ok = ok && devOf(0) == 0 && devOf(1) == 1 && devOf(2) == 2;
    rt_.devices().removeDevice(tid, 1); // Delay, Reverb — Utility's lane goes
    ok = ok && automationLaneCount(tid) == 3 && kindOf(0) == 3 && kindOf(1) == 2 && devOf(2) == -1;
    rt_.tracks().removeTrack(tid);
    return ok;
}

int32_t AutomationOps::automationLaneCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->automation.size()) : 0;
}

bool AutomationOps::automationLaneInfo(int32_t trackId, int32_t laneIndex, int32_t* outTarget, int32_t* outDeviceIndex,
    int32_t* outParamIndex, int32_t* outPointCount) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return false;
    const TrackLane& l = t->automation[static_cast<size_t>(laneIndex)];
    if (outTarget) *outTarget = static_cast<int32_t>(l.target);
    if (outDeviceIndex) *outDeviceIndex = l.deviceIndex;
    if (outParamIndex) *outParamIndex = l.paramIndex;
    if (outPointCount) *outPointCount = static_cast<int32_t>(l.points.size());
    return true;
}

int32_t AutomationOps::getAutomationPoints(
    int32_t trackId, int32_t laneIndex, AutomationPointData* out, int32_t cap) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || laneIndex < 0 || laneIndex >= static_cast<int32_t>(t->automation.size())) return 0;
    const auto& pts = t->automation[static_cast<size_t>(laneIndex)].points;
    if (!out) return static_cast<int32_t>(pts.size());
    const int32_t n = std::min<int32_t>(cap, static_cast<int32_t>(pts.size()));
    for (int32_t i = 0; i < n; ++i) {
        out[i].beat = pts[static_cast<size_t>(i)].beat;
        out[i].value = pts[static_cast<size_t>(i)].value;
        out[i].curve = pts[static_cast<size_t>(i)].curve;
    }
    return n;
}

bool AutomationOps::setAutomationPoints(
    int32_t trackId, int32_t laneIndex, const AutomationPointData* pts, int32_t count) {
    return setAutomationPointsImpl(trackId, laneIndex, pts, count, /*undoCheckpoint=*/true);
}
// Live variant: no undo checkpoint. Used while dragging a point so the lane (and thus
// applyAutomation → the device) follows in real time; the caller checkpoints once at the
// drag start and does the final commit through the undo path.
bool AutomationOps::setAutomationPointsLive(
    int32_t trackId, int32_t laneIndex, const AutomationPointData* pts, int32_t count) {
    return setAutomationPointsImpl(trackId, laneIndex, pts, count, /*undoCheckpoint=*/false);
}
bool AutomationOps::setAutomationPointsImpl(
    int32_t trackId, int32_t laneIndex, const AutomationPointData* pts, int32_t count, bool undoCheckpoint) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return false;
    const bool tempo = old->automation[static_cast<size_t>(laneIndex)].target == AutomationTarget::Tempo;
    // The limit is on the compiled map, not the lane: a jump is two lane points on one beat,
    // so a migrated #568 map of kMaxPoints steps becomes about twice as many lane points.
    if (tempo && count > 2 * static_cast<int32_t>(TempoMap::kMaxPoints)) return false;
    for (int32_t i = 0; tempo && i < count; ++i)
        if (!std::isfinite(pts[i].beat) || !std::isfinite(pts[i].value)) return false;
    if (tempo && count > static_cast<int32_t>(TempoMap::kMaxPoints) - 1) {
        std::vector<double> beats(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) beats[static_cast<size_t>(i)] = std::max(0.0, pts[i].beat);
        std::sort(beats.begin(), beats.end());
        size_t distinct = beats.front() > 0.0 ? 1 : 0; // a first point after 0 holds back to 0
        for (size_t i = 0; i < beats.size(); ++i)
            if (i == 0 || beats[i] - beats[i - 1] >= 1e-6) ++distinct;
        if (distinct > TempoMap::kMaxPoints) return false;
    }
    auto nt = rt_.cloneTrack(*old);
    auto& lane = nt->automation.write()[static_cast<size_t>(laneIndex)];
    lane.points.clear();
    lane.points.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) {
        // specs/099: tempo is 20..300 BPM everywhere; a lane never holds a value the map can't play.
        const float v = tempo ? static_cast<float>(TempoMap::clampBpm(pts[i].value)) : pts[i].value;
        lane.points.push_back({tempo ? std::max(0.0, pts[i].beat) : pts[i].beat, v, pts[i].curve});
    }
    // Stable: two points on one beat are a jump, and their order is its direction.
    std::stable_sort(lane.points.begin(), lane.points.end(),
        [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    if (undoCheckpoint) rt_.republishWithTrack(trackId, nt);
    else
        rt_.republishWithTrackRaw(trackId, nt);
    return true;
}

bool AutomationOps::removeAutomationLane(int32_t trackId, int32_t laneIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || laneIndex < 0 || laneIndex >= static_cast<int32_t>(old->automation.size())) return false;
    auto nt = rt_.cloneTrack(*old);
    auto& autos = nt->automation.write();
    autos.erase(autos.begin() + laneIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool AutomationOps::automationRecord() const {
    return rt_.autoRecord_.load(std::memory_order_relaxed);
}

bool AutomationOps::automationOverridden() const {
    return !rt_.overrides_.empty();
}

} // namespace nota::detail
