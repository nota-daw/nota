// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hosted plug-ins that load after they are inserted (#979, specs/113). The plug-in host hands
// the engine its adapters unloaded; the app's UI timer calls servicePluginLoads(), which attaches
// the ones whose binary has loaded. What an attach changes outside the device itself is settled
// here: the plug-in's latency (PDC) and plug-in-param automation lanes that were made while it
// loaded, when the parameter ids couldn't be resolved yet.

#include "Engine.h"
#include "ParamResolve.h"
#include "bridge/PluginHostBridge.h"

#include <algorithm>
#include <chrono>
#include <thread>

namespace nota {

namespace {

// A plug-in-param lane still waiting for its index: the parameter it names, now that the device
// may have loaded. -1 while it still can't be resolved.
int32_t pendingLaneIndex(const Track& t, const TrackLane& l) {
    if (l.target != AutomationTarget::PluginParam || l.paramIndex >= 0 || l.paramId.empty()) return -1;
    if (l.nested()) {
        const ParamOwner o = resolveNested(t, l.deviceIndex, l.chainUid, l.slot);
        return o.instrument ? o.instrument->pluginParamIndexOfId(l.paramId)
             : o.device     ? o.device->pluginParamIndexOfId(l.paramId)
                            : -1;
    }
    if (l.deviceIndex < 0) return t.instrument ? t.instrument->pluginParamIndexOfId(l.paramId) : -1;
    if (l.deviceIndex >= static_cast<int32_t>(t.devices.size())) return -1;
    const auto& d = t.devices[static_cast<size_t>(l.deviceIndex)];
    return d ? d->pluginParamIndexOfId(l.paramId) : -1;
}

// Every instrument and audio device a rack's chains hold, at any nesting depth.
template <class Fn>
void forEachRackChild(const RackCore* r, Fn& fn) {
    if (!r) return;
    const RackCore::RackState* st = r->live();
    if (!st) return;
    for (const auto& c : st->chains) {
        if (c.instrument) {
            fn(c.instrument.get(), nullptr);
            forEachRackChild(c.instrument->asRack(), fn);
        }
        for (const auto& d : c.devices)
            if (d) {
                fn(nullptr, d.get());
                forEachRackChild(d->asRack(), fn);
            }
        for (const auto& m : c.midiDevices)
            if (m) forEachRackChild(m->asRack(), fn);
    }
}

// Every instrument and audio device a track holds, rack children included: grouping a
// plug-in into a rack (or ungrouping it) moves the same instance, so a plug-in that only
// changed place is still found after the edit.
template <class Fn>
void forEachTrackDevice(const Track& t, Fn fn) {
    if (t.instrument) {
        fn(t.instrument.get(), nullptr);
        forEachRackChild(t.instrument->asRack(), fn);
    }
    for (const auto& d : t.devices)
        if (d) {
            fn(nullptr, d.get());
            forEachRackChild(d->asRack(), fn);
        }
    for (const auto& m : t.midiEffects)
        if (m) forEachRackChild(m->asRack(), fn);
}

} // namespace

int32_t Engine::servicePluginLoads() {
    const PluginLoadsServiced s = nota::servicePluginLoads();
    if (s.attached > 0) recomputePdc(); // the plug-in's latency arrived with it
    // Every tick, not only on an attach: an undo can bring back a track whose lanes were made
    // before its plug-in loaded. Only a lane that now resolves is republished.
    resolveLoadedPluginLanes();
    return s.changed;
}

int32_t Engine::waitPluginLoads(int32_t timeoutMs) {
    const auto until = std::chrono::steady_clock::now() + std::chrono::milliseconds(std::max(0, timeoutMs));
    for (;;) {
        servicePluginLoads();
        const int32_t left = pendingPluginLoads();
        if (left == 0 || std::chrono::steady_clock::now() >= until) return left;
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
}

void Engine::resolveLoadedPluginLanes() {
    if (!authoring_) return;
    std::vector<int32_t> ids;
    for (const auto& t : authoring_->tracks)
        if (t) ids.push_back(t->id());
    if (authoring_->masterTrack) ids.push_back(kMasterTrackId);
    for (const int32_t id : ids) {
        auto t = findTrackAuthoring(id);
        if (!t) continue;
        bool any = false;
        for (const TrackLane& l : t->automation)
            if (pendingLaneIndex(*t, l) >= 0) any = true;
        if (!any) continue;
        auto nt = cloneTrack(*t);
        for (TrackLane& l : nt->automation)
            if (const int32_t i = pendingLaneIndex(*nt, l); i >= 0) l.paramIndex = i;
        republishWithTrackRaw(id, nt); // not an edit of the user's: no undo step
    }
}

void Engine::giveUpVanishedPluginLoads(const Graph& before, const Graph& after) {
    // Cheap first: usually nothing is loading, and then nothing is compared.
    auto loading = [](int32_t s) { return s > 0 && s < 4; }; // waiting, loading, not responding
    std::vector<Instrument*> insts;
    std::vector<Device*> devs;
    auto collect = [&](const std::shared_ptr<Track>& t) {
        if (!t) return;
        forEachTrackDevice(*t, [&](Instrument* i, Device* d) {
            if (i && loading(i->pluginLoadState())) insts.push_back(i);
            if (d && loading(d->pluginLoadState())) devs.push_back(d);
        });
    };
    for (const auto& t : before.tracks) collect(t);
    collect(before.masterTrack);
    if (insts.empty() && devs.empty()) return;
    // Still in the project after the edit: at the top level, or moved into or out of a rack.
    auto drop = [&](const std::shared_ptr<Track>& t) {
        if (!t) return;
        forEachTrackDevice(*t, [&](Instrument* i, Device* d) {
            if (i) insts.erase(std::remove(insts.begin(), insts.end(), i), insts.end());
            if (d) devs.erase(std::remove(devs.begin(), devs.end(), d), devs.end());
        });
    };
    for (const auto& t : after.tracks) drop(t);
    drop(after.masterTrack);
    for (Instrument* i : insts) i->pluginLoadAction(4); // abandon: it left the project
    for (Device* d : devs) d->pluginLoadAction(4);
}

int32_t Engine::devicePluginLoadState(int32_t trackId, int32_t deviceIndex, int32_t* reasonCode, std::string* reasonArg) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    if (deviceIndex < 0) {
        if (!t->instrument) return 0;
        if (reasonCode) *reasonCode = t->instrument->pluginLoadReasonCode();
        if (reasonArg) *reasonArg = t->instrument->pluginLoadReasonArg();
        return t->instrument->pluginLoadState();
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size()) || !t->devices[static_cast<size_t>(deviceIndex)]) return 0;
    const auto& d = t->devices[static_cast<size_t>(deviceIndex)];
    if (reasonCode) *reasonCode = d->pluginLoadReasonCode();
    if (reasonArg) *reasonArg = d->pluginLoadReasonArg();
    return d->pluginLoadState();
}

bool Engine::devicePluginLoadAction(int32_t trackId, int32_t deviceIndex, int32_t action) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) {
        if (!t->instrument) return false;
        t->instrument->pluginLoadAction(action);
        return true;
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size()) || !t->devices[static_cast<size_t>(deviceIndex)]) return false;
    t->devices[static_cast<size_t>(deviceIndex)]->pluginLoadAction(action);
    return true;
}

} // namespace nota
