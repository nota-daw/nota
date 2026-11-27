// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "render/RenderRoutingPlan.h"
#include "core/EnginePrelude.h"
#include "automation/Modulation.h"
#include "model/Graph.h"
#include "model/ParamResolve.h"
#include "model/Track.h"
#include "nota/devices/racks/RackCore.h" // IWYU pragma: keep

#include <algorithm>
#include <vector>
#include <utility>
#include <memory>
#include <cstdint>
#include <cstddef>

namespace nota::detail {

int32_t RenderRoutingPlan::findTrackIndex(int32_t trackId) const noexcept {
    if (trackId < 0 || tracksById_.empty()) return -1;
    auto it = std::lower_bound(tracksById_.begin(), tracksById_.end(), trackId,
        [](const TrackIdSlot& s, int32_t id) { return s.trackId < id; });
    return (it != tracksById_.end() && it->trackId == trackId) ? it->trackIndex : -1;
}

Track* RenderRoutingPlan::findTrack(const Graph& graph, int32_t trackId) const noexcept {
    const int32_t idx = findTrackIndex(trackId);
    if (idx < 0 || static_cast<size_t>(idx) >= graph.tracks.size()) return nullptr;
    return graph.tracks[static_cast<size_t>(idx)].get();
}

int32_t RenderRoutingPlan::groupSlotOfId(int32_t groupId) const noexcept {
    if (groupId < 0) return -1;
    for (int32_t i = 0; i < groupCount_; ++i)
        if (groups_[static_cast<size_t>(i)].trackId == groupId) return i;
    return -1;
}

RenderRoutingPlan::CompiledParamEndpoint RenderRoutingPlan::compileAutomationTarget(Track& t, const TrackLane& lane) {
    CompiledParamEndpoint ep;
    ep.track = &t;
    ep.host = lane.deviceIndex;
    ep.chainUid = lane.chainUid;
    ep.slot = lane.slot;
    ep.paramIndex = lane.paramIndex;
    ep.nested = lane.nested();

    if (lane.target == AutomationTarget::Tempo) return ep;

    if (ep.nested) {
        ep.rack = rackAtHost(t, lane.deviceIndex);
        if (!ep.rack) return ep;
        ep.expectedRackGen = ep.rack->liveGeneration();
        const ParamOwner o = resolveNested(t, lane.deviceIndex, lane.chainUid, lane.slot);
        ep.device = o.device;
        ep.midi = o.midi;
        ep.instrument = o.instrument;
        if (lane.target == AutomationTarget::PluginParam) {
            if (lane.paramIndex < 0) return ep;
            if (o.instrument) ep.kind = CompiledParamEndpoint::Kind::NestedRackInstrumentPlugin;
            else if (o.device)
                ep.kind = CompiledParamEndpoint::Kind::NestedRackDevicePlugin;
        } else if (o.device) {
            ep.kind = CompiledParamEndpoint::Kind::NestedRackDeviceGeneric;
        } else if (o.midi) {
            ep.kind = CompiledParamEndpoint::Kind::NestedRackMidiGeneric;
        }
        return ep;
    }

    switch (lane.target) {
    case AutomationTarget::Volume: ep.kind = CompiledParamEndpoint::Kind::Volume; break;
    case AutomationTarget::Pan: ep.kind = CompiledParamEndpoint::Kind::Pan; break;
    case AutomationTarget::DeviceParam:
        if (lane.deviceIndex >= 0 && lane.deviceIndex < static_cast<int32_t>(t.devices.size())) {
            ep.device = t.devices[static_cast<size_t>(lane.deviceIndex)].get();
            if (ep.device) ep.kind = CompiledParamEndpoint::Kind::DeviceGeneric;
        }
        break;
    case AutomationTarget::MidiDeviceParam:
        if (lane.deviceIndex >= 0 && lane.deviceIndex < static_cast<int32_t>(t.midiEffects.size())) {
            ep.midi = t.midiEffects[static_cast<size_t>(lane.deviceIndex)].get();
            if (ep.midi) ep.kind = CompiledParamEndpoint::Kind::MidiDeviceGeneric;
        }
        break;
    case AutomationTarget::PluginParam:
        if (lane.paramIndex < 0) break;
        if (lane.deviceIndex < 0) {
            ep.instrument = t.instrument.get();
            if (ep.instrument) ep.kind = CompiledParamEndpoint::Kind::InstrumentPlugin;
        } else if (lane.deviceIndex < static_cast<int32_t>(t.devices.size())) {
            ep.device = t.devices[static_cast<size_t>(lane.deviceIndex)].get();
            if (ep.device) ep.kind = CompiledParamEndpoint::Kind::DevicePlugin;
        }
        break;
    case AutomationTarget::Send:
    case AutomationTarget::Tempo: break;
    }
    return ep;
}

RenderRoutingPlan::CompiledParamEndpoint RenderRoutingPlan::compileCvSourceParam(Track& t, const CvLink& link) {
    CompiledParamEndpoint ep;
    ep.track = &t;
    ep.host = link.sourceDevice;
    ep.chainUid = link.sourceChainUid;
    ep.slot = link.sourceSlot;
    ep.paramIndex = link.sourceParam;
    ep.nested = link.sourceChainUid != 0;
    if (ep.paramIndex < 0) return ep;

    if (ep.nested) {
        ep.rack = rackAtHost(t, link.sourceDevice);
        if (!ep.rack) return ep;
        ep.expectedRackGen = ep.rack->liveGeneration();
        ep.device = resolveNested(t, link.sourceDevice, link.sourceChainUid, link.sourceSlot).device;
        if (ep.device && ep.paramIndex < ep.device->paramCount()) {
            ep.minVal = ep.device->paramMin(ep.paramIndex);
            ep.spanVal = ep.device->paramMax(ep.paramIndex) - ep.minVal;
            ep.kind = CompiledParamEndpoint::Kind::NestedRackDeviceGeneric;
        }
        return ep;
    }

    if (link.sourceDevice >= 0 && link.sourceDevice < static_cast<int32_t>(t.devices.size())) {
        ep.device = t.devices[static_cast<size_t>(link.sourceDevice)].get();
        if (ep.device && ep.paramIndex < ep.device->paramCount()) {
            ep.minVal = ep.device->paramMin(ep.paramIndex);
            ep.spanVal = ep.device->paramMax(ep.paramIndex) - ep.minVal;
            ep.kind = CompiledParamEndpoint::Kind::DeviceGeneric;
        }
    }
    return ep;
}

RenderRoutingPlan::CompiledParamEndpoint RenderRoutingPlan::compileCvTargetParam(Track& tgt, const CvLink& link) {
    CompiledParamEndpoint ep;
    ep.track = &tgt;
    ep.host = link.targetDevice;
    ep.chainUid = link.targetChainUid;
    ep.slot = link.targetSlot;
    ep.paramIndex = link.targetParam;
    ep.nested = link.targetChainUid != 0;
    if (ep.paramIndex < 0) return ep;

    const int32_t tk = link.targetKind;
    if (ep.nested) {
        ep.rack = rackAtHost(tgt, link.targetDevice);
        if (!ep.rack) return ep;
        ep.expectedRackGen = ep.rack->liveGeneration();
        const ParamOwner o = resolveNested(tgt, link.targetDevice, link.targetChainUid, link.targetSlot);
        if (tk == static_cast<int32_t>(CvTargetKind::Instrument)) {
            if (o.instrument && ep.paramIndex < o.instrument->pluginParamCount()) {
                ep.instrument = o.instrument;
                ep.minVal = 0.0f;
                ep.spanVal = 1.0f;
                ep.kind = CompiledParamEndpoint::Kind::NestedRackInstrumentPlugin;
            }
        } else if (tk == static_cast<int32_t>(CvTargetKind::MidiFx)) {
            if (o.midi && ep.paramIndex < o.midi->paramCount()) {
                ep.midi = o.midi;
                ep.minVal = o.midi->paramMin(ep.paramIndex);
                ep.spanVal = o.midi->paramMax(ep.paramIndex) - ep.minVal;
                ep.kind = CompiledParamEndpoint::Kind::NestedRackMidiGeneric;
            }
        } else if (o.device && ep.paramIndex < o.device->paramCount() &&
                   !o.device->isLatencyConfigurationParam(ep.paramIndex)) {
            ep.device = o.device;
            ep.minVal = o.device->paramMin(ep.paramIndex);
            ep.spanVal = o.device->paramMax(ep.paramIndex) - ep.minVal;
            ep.kind = CompiledParamEndpoint::Kind::NestedRackDeviceGeneric;
        }
        return ep;
    }

    if (tk == static_cast<int32_t>(CvTargetKind::Instrument)) {
        if (tgt.instrument && ep.paramIndex < tgt.instrument->pluginParamCount()) {
            ep.instrument = tgt.instrument.get();
            ep.minVal = 0.0f;
            ep.spanVal = 1.0f;
            ep.kind = CompiledParamEndpoint::Kind::InstrumentPlugin;
        }
    } else if (tk == static_cast<int32_t>(CvTargetKind::MidiFx)) {
        if (link.targetDevice >= 0 && link.targetDevice < static_cast<int32_t>(tgt.midiEffects.size())) {
            MidiDevice* m = tgt.midiEffects[static_cast<size_t>(link.targetDevice)].get();
            if (m && ep.paramIndex < m->paramCount()) {
                ep.midi = m;
                ep.minVal = m->paramMin(ep.paramIndex);
                ep.spanVal = m->paramMax(ep.paramIndex) - ep.minVal;
                ep.kind = CompiledParamEndpoint::Kind::MidiDeviceGeneric;
            }
        }
    } else if (link.targetDevice >= 0 && link.targetDevice < static_cast<int32_t>(tgt.devices.size())) {
        Device* d = tgt.devices[static_cast<size_t>(link.targetDevice)].get();
        if (d && ep.paramIndex < d->paramCount() && !d->isLatencyConfigurationParam(ep.paramIndex)) {
            ep.device = d;
            ep.minVal = d->paramMin(ep.paramIndex);
            ep.spanVal = d->paramMax(ep.paramIndex) - ep.minVal;
            ep.kind = CompiledParamEndpoint::Kind::DeviceGeneric;
        }
    }
    return ep;
}

std::shared_ptr<const RenderRoutingPlan> RenderRoutingPlan::build(const Graph& graph) {
    auto plan = std::make_shared<RenderRoutingPlan>();
    const size_t n = graph.tracks.size();
    plan->trackCount_ = static_cast<int32_t>(n);

    // 1. Track ID directory + returns + groups + audio track subset.
    plan->tracksById_.reserve(n);
    plan->leafRoutes_.resize(n);
    plan->trackCcRoutes_.resize(n);

    for (size_t i = 0; i < n; ++i) {
        Track* t = graph.tracks[i].get();
        if (!t) continue;
        const int32_t idx = static_cast<int32_t>(i);
        plan->tracksById_.push_back({t->id(), idx});
        if (t->type() == TrackType::Return) {
            ++plan->returnCount_;
        } else if (t->type() == TrackType::Group) {
            if (plan->groupCount_ < kMaxGroups) {
                CompiledGroupSlot& gs = plan->groups_[static_cast<size_t>(plan->groupCount_++)];
                gs.track = t;
                gs.trackIndex = idx;
                gs.trackId = t->id();
                gs.parentGroupSlot = t->groupId(); // temporarily holds parent ID until step 2 resolves to slot
            }
        } else if (t->type() == TrackType::Audio) {
            plan->audioTrackIndices_.push_back(idx);
        }
    }
    std::sort(plan->tracksById_.begin(), plan->tracksById_.end(),
        [](const TrackIdSlot& a, const TrackIdSlot& b) { return a.trackId < b.trackId; });

    // 2. Resolve group parent slots and ancestor chains (matching legacy loop bounds/order).
    for (int32_t gi = 0; gi < plan->groupCount_; ++gi) {
        CompiledGroupSlot& gs = plan->groups_[static_cast<size_t>(gi)];
        gs.parentGroupSlot = plan->groupSlotOfId(gs.parentGroupSlot);
    }
    for (int32_t gi = 0; gi < plan->groupCount_; ++gi) {
        CompiledGroupSlot& gs = plan->groups_[static_cast<size_t>(gi)];
        int32_t cur = gi;
        int32_t depth = 0;
        while (cur >= 0 && depth < kMaxGroups) {
            gs.ancestors[static_cast<size_t>(depth++)] = static_cast<int8_t>(cur);
            cur = plan->groups_[static_cast<size_t>(cur)].parentGroupSlot;
        }
        gs.ancestorCount = static_cast<int8_t>(depth);
        gs.depth = depth;
        if (depth > plan->maxGroupDepth_) plan->maxGroupDepth_ = depth;
    }

    // 3. Per-track leaf routing, MIDI source cache, potential sidechains, automation, modulators, CC routes.
    for (size_t i = 0; i < n; ++i) {
        Track* t = graph.tracks[i].get();
        if (!t) continue;
        const int32_t idx = static_cast<int32_t>(i);
        CompiledLeafRoute& lr = plan->leafRoutes_[i];
        lr.parentGroupSlot = static_cast<int8_t>(plan->groupSlotOfId(t->groupId()));

        const int32_t midiSrc = t->midiFromTrackId();
        lr.expectedMidiFromId = midiSrc;
        lr.midiFromTrackIndex = plan->findTrackIndex(midiSrc);

        bool mayConsumeSidechain = false;
        if (t->instrument && (t->instrument->acceptsSidechain() || t->instrument->asRack() != nullptr))
            mayConsumeSidechain = true;
        if (!mayConsumeSidechain) {
            for (const auto& d : t->devices)
                if (d && (d->acceptsSidechain() || d->asRack() != nullptr)) {
                    mayConsumeSidechain = true;
                    break;
                }
        }
        lr.hasPotentialSidechainConsumer = mayConsumeSidechain;
        if (mayConsumeSidechain) plan->sidechainConsumerLeaves_.push_back(idx);

        if (!t->automation.empty()) {
            CompiledAutomationTrack cat;
            cat.track = t;
            cat.trackIndex = idx;
            cat.laneTargets.reserve(t->automation.size());
            for (const TrackLane& lane : t->automation) {
                cat.laneTargets.push_back(compileAutomationTarget(*t, lane));
                if (!lane.points.empty() && !lane.suppressRead && lane.target == AutomationTarget::PluginParam &&
                    lane.paramIndex >= 0) {
                    cat.hasAnyPluginStepLane = true;
                }
            }
            plan->automatedTracks_.push_back(std::move(cat));
        }

        if (!t->modulators.empty()) plan->modulatorTrackIndices_.push_back(idx);

        if (t->ccRoutes && !t->ccRoutes->empty()) {
            auto compiled = std::make_unique<std::vector<CompiledCcRouteEntry>>();
            compiled->reserve(t->ccRoutes->size());
            for (const Track::CcRoute& rt : *t->ccRoutes) {
                CompiledCcRouteEntry entry;
                entry.arp = rt.arp;
                TrackLane synthLane;
                synthLane.deviceIndex = rt.dev;
                synthLane.chainUid = rt.uid;
                synthLane.slot = rt.slot;
                synthLane.paramIndex = rt.param;
                synthLane.target = AutomationTarget::DeviceParam;
                entry.target = compileAutomationTarget(*t, synthLane);
                compiled->push_back(entry);
            }
            plan->trackCcRoutes_[i] = std::move(compiled);
        }
    }

    // 4. Master lanes & Pan lane.
    if (graph.masterTrack) {
        Track& master = *graph.masterTrack;
        if (!master.automation.empty()) {
            plan->masterLaneTargets_.reserve(master.automation.size());
            for (const TrackLane& lane : master.automation) {
                plan->masterLaneTargets_.push_back(compileAutomationTarget(master, lane));
                if (!plan->masterPanLane_ && lane.target == AutomationTarget::Pan && !lane.nested() &&
                    !lane.points.empty() && !lane.suppressRead) {
                    plan->masterPanLane_ = &lane;
                }
            }
        }
    }

    // 5. Compile all CV links across tracks into one dense active list.
    for (size_t i = 0; i < n; ++i) {
        Track* owner = graph.tracks[i].get();
        if (!owner || owner->cvLinks.empty()) continue;
        for (size_t li = 0; li < owner->cvLinks.size(); ++li) {
            const CvLink& link = owner->cvLinks[li];
            CompiledCvLinkEntry entry;
            entry.ownerTrack = owner;
            entry.linkIndex = static_cast<int32_t>(li);
            entry.targetKind = link.targetKind;
            entry.targetDevice = link.targetDevice;
            entry.targetParam = link.targetParam;
            entry.targetChainUid = link.targetChainUid;
            entry.targetSlot = link.targetSlot;

            if (link.sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
                entry.sourceIsParam = true;
                entry.sourceParam = compileCvSourceParam(*owner, link);
            } else {
                for (const Modulator& m : owner->modulators) {
                    if (m.id == link.sourceModId) {
                        entry.sourceMod = &m;
                        break;
                    }
                }
            }

            Track* tgt = owner;
            if (link.targetTrack >= 0) tgt = plan->findTrack(graph, link.targetTrack);
            entry.targetTrack = tgt;
            if (tgt) entry.destParam = compileCvTargetParam(*tgt, link);

            plan->cvLinks_.push_back(std::move(entry));
        }
    }

    return plan;
}

} // namespace nota::detail
