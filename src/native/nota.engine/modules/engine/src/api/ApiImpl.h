// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "core/EngineRuntime.h"
#include "model/Track.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <cstddef>
#include <memory>
#include <vector>
#include <utility>

namespace nota {

struct Engine::Impl {
    detail::EngineRuntime runtime;
};

namespace detail {

struct EngineApiAccess {
    static bool hasTrack(const EngineRuntime& rt, int32_t trackId) noexcept {
        return rt.findTrackAuthoring(trackId) != nullptr;
    }

    static bool hasClip(const EngineRuntime& rt, int32_t trackId, int32_t clipIndex) noexcept {
        auto t = rt.findTrackAuthoring(trackId);
        if (!t) return false;
        if (t->type() == TrackType::Instrument)
            return clipIndex >= 0 && clipIndex < static_cast<int32_t>(t->midiClips.size());
        if (t->type() == TrackType::Audio) {
            if (clipIndex >= 0) return clipIndex < static_cast<int32_t>(t->clips.size());
            const size_t scene = static_cast<size_t>(sessionSceneOfClip(clipIndex));
            return sessionSceneOfClip(clipIndex) >= 0 && scene < t->sessionSlots.size() &&
                   t->sessionSlots[scene].hasClip;
        }
        return false;
    }

    static bool hasDevice(
        const EngineRuntime& rt, int32_t trackId, int32_t deviceIndex, engine::DeviceFamily family) noexcept {
        switch (family) {
        case engine::DeviceFamily::AudioEffect: return rt.devices().deviceAt(trackId, deviceIndex) != nullptr;
        case engine::DeviceFamily::MidiEffect: return rt.devices().midiDeviceAt(trackId, deviceIndex) != nullptr;
        case engine::DeviceFamily::Instrument: {
            auto t = rt.findTrackAuthoring(trackId);
            return t && t->instrument != nullptr;
        }
        }
        return false;
    }

    static bool hasRack(const EngineRuntime& rt, int32_t trackId, int32_t deviceIndex) noexcept {
        return rt.racks().coreAt(trackId, deviceIndex) != nullptr;
    }

    static bool hasRackChain(
        const EngineRuntime& rt, int32_t trackId, int32_t deviceIndex, int32_t chainIndex) noexcept {
        const auto* r = rt.racks().coreAt(trackId, deviceIndex);
        return r && chainIndex >= 0 && chainIndex < r->chainCount();
    }

    static bool hasScene(const EngineRuntime& rt, int32_t scene) noexcept {
        return scene >= 0 && scene < rt.session().sceneCount();
    }

    static bool hasSlot(const EngineRuntime& rt, int32_t trackId, int32_t scene) noexcept {
        auto t = rt.findTrackAuthoring(trackId);
        return t && scene >= 0 && scene < static_cast<int32_t>(t->sessionSlots.size());
    }

    static bool hasAutomationLane(const EngineRuntime& rt, int32_t trackId, int32_t laneIndex) noexcept {
        return laneIndex >= 0 && laneIndex < rt.automation().automationLaneCount(trackId);
    }

    static bool hasModulator(const EngineRuntime& rt, int32_t trackId, int32_t modId) noexcept {
        return rt.automation().modulatorKind(trackId, modId) >= 0;
    }

    static bool hasCvLink(const EngineRuntime& rt, int32_t trackId, int32_t index) noexcept {
        return index >= 0 && index < rt.automation().cvLinkCount(trackId);
    }

    static const std::vector<std::pair<int32_t, int32_t>>& lastPlaced(const EngineRuntime& rt) noexcept {
        return rt.clips().lastPlaced();
    }

    static const PresetTagData* presetTagData(const EngineRuntime& rt, int32_t tag) noexcept {
        return rt.devices().presetTagData(tag);
    }
};

} // namespace detail
} // namespace nota
