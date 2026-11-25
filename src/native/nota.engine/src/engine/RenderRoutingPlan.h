// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RenderRoutingPlan — precompiled per-Graph routing, bus ancestry, and compiled param targets.
// Built once on graph publish (publishRaw) so audio callbacks replace O(T^2) track searches
// and repeated nested-rack tree walks with O(1)/O(log T) lock-free reads (AR-6).

#pragma once

#include "Automation.h"
#include "Modulation.h"

#include <array>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

struct Graph;
struct Track;
class Device;
class MidiDevice;
class Instrument;
class RackCore;

class RenderRoutingPlan {
  public:
    static constexpr int32_t kMaxGroups = 16;

    struct TrackIdSlot {
        int32_t trackId = -1;
        int32_t trackIndex = -1;
    };

    struct CompiledGroupSlot {
        Track* track = nullptr;
        int32_t trackIndex = -1;
        int32_t trackId = -1;
        int32_t parentGroupSlot = -1;
        int32_t depth = 1;
        // Ancestors from self up to root (up to kMaxGroups) for O(depth) mute/solo folding.
        std::array<int8_t, kMaxGroups> ancestors{};
        int8_t ancestorCount = 0;
    };

    struct CompiledLeafRoute {
        int8_t parentGroupSlot = -1;
        int32_t expectedMidiFromId = -1;
        int32_t midiFromTrackIndex = -1;
        bool hasPotentialSidechainConsumer = false;
    };

    struct CompiledParamEndpoint {
        enum class Kind : uint8_t {
            Invalid = 0,
            Volume,
            Pan,
            DeviceGeneric,
            MidiDeviceGeneric,
            InstrumentPlugin,
            DevicePlugin,
            NestedRackDeviceGeneric,
            NestedRackMidiGeneric,
            NestedRackDevicePlugin,
            NestedRackInstrumentPlugin
        };

        Kind kind = Kind::Invalid;
        Track* track = nullptr;
        Device* device = nullptr;
        MidiDevice* midi = nullptr;
        Instrument* instrument = nullptr;
        const RackCore* rack = nullptr;
        uint64_t expectedRackGen = 0;
        uint64_t chainUid = 0;
        int32_t host = -1;
        int32_t slot = -1;
        int32_t paramIndex = -1;
        float minVal = 0.0f;
        float spanVal = 1.0f;
        bool nested = false;
    };

    struct CompiledAutomationTrack {
        Track* track = nullptr;
        int32_t trackIndex = -1;
        std::vector<CompiledParamEndpoint> laneTargets;
        bool hasAnyPluginStepLane = false;
    };

    struct CompiledCvLinkEntry {
        Track* ownerTrack = nullptr;
        int32_t linkIndex = -1;
        bool sourceIsParam = false;
        const Modulator* sourceMod = nullptr;
        CompiledParamEndpoint sourceParam;
        CompiledParamEndpoint destParam;
        int32_t targetKind = 0;
        int32_t targetDevice = -1;
        int32_t targetParam = -1;
        uint64_t targetChainUid = 0;
        int32_t targetSlot = -1;
        Track* targetTrack = nullptr;
    };

    struct CompiledCcRouteEntry {
        const MidiDevice* arp = nullptr;
        CompiledParamEndpoint target;
    };

    static std::shared_ptr<const RenderRoutingPlan> build(const Graph& graph);

    int32_t trackCount() const noexcept { return trackCount_; }
    int32_t returnCount() const noexcept { return returnCount_; }
    int32_t groupCount() const noexcept { return groupCount_; }
    int32_t maxGroupDepth() const noexcept { return maxGroupDepth_; }

    const CompiledGroupSlot& groupSlot(int32_t gi) const noexcept { return groups_[static_cast<size_t>(gi)]; }

    int32_t findTrackIndex(int32_t trackId) const noexcept;
    Track* findTrack(const Graph& graph, int32_t trackId) const noexcept;

    int32_t groupSlotOfId(int32_t groupId) const noexcept;
    int8_t leafParentGroupSlot(size_t trackIndex) const noexcept {
        return trackIndex < leafRoutes_.size() ? leafRoutes_[trackIndex].parentGroupSlot : -1;
    }

    int32_t resolveMidiSourceIndex(size_t trackIndex, int32_t liveMidiFromId) const noexcept {
        if (trackIndex < leafRoutes_.size()) {
            const CompiledLeafRoute& r = leafRoutes_[trackIndex];
            if (liveMidiFromId == r.expectedMidiFromId) return r.midiFromTrackIndex;
        }
        return findTrackIndex(liveMidiFromId);
    }

    const std::vector<int32_t>& audioTrackIndices() const noexcept { return audioTrackIndices_; }
    const std::vector<int32_t>& sidechainConsumerLeaves() const noexcept { return sidechainConsumerLeaves_; }
    const std::vector<CompiledAutomationTrack>& automatedTracks() const noexcept { return automatedTracks_; }
    const std::vector<CompiledParamEndpoint>& masterLaneTargets() const noexcept { return masterLaneTargets_; }
    const TrackLane* masterPanLane() const noexcept { return masterPanLane_; }
    const std::vector<int32_t>& modulatorTrackIndices() const noexcept { return modulatorTrackIndices_; }
    const std::vector<CompiledCvLinkEntry>& cvLinks() const noexcept { return cvLinks_; }

    const std::vector<CompiledCcRouteEntry>* ccRoutesForTrack(size_t trackIndex) const noexcept {
        return trackIndex < trackCcRoutes_.size() ? trackCcRoutes_[trackIndex].get() : nullptr;
    }

  private:
    static CompiledParamEndpoint compileAutomationTarget(Track& t, const TrackLane& lane);
    static CompiledParamEndpoint compileCvSourceParam(Track& t, const CvLink& link);
    static CompiledParamEndpoint compileCvTargetParam(Track& tgt, const CvLink& link);

    int32_t trackCount_ = 0;
    int32_t returnCount_ = 0;
    int32_t groupCount_ = 0;
    int32_t maxGroupDepth_ = 0;

    std::vector<TrackIdSlot> tracksById_;
    std::array<CompiledGroupSlot, kMaxGroups> groups_{};
    std::vector<CompiledLeafRoute> leafRoutes_;
    std::vector<int32_t> audioTrackIndices_;
    std::vector<int32_t> sidechainConsumerLeaves_;
    std::vector<CompiledAutomationTrack> automatedTracks_;
    std::vector<CompiledParamEndpoint> masterLaneTargets_;
    const TrackLane* masterPanLane_ = nullptr;
    std::vector<int32_t> modulatorTrackIndices_;
    std::vector<CompiledCvLinkEntry> cvLinks_;
    std::vector<std::unique_ptr<std::vector<CompiledCcRouteEntry>>> trackCcRoutes_;
};

} // namespace nota
