// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "core/EngineRuntime.h"
#include "model/Track.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiDevice.h"
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

    static uint64_t deviceIdentity(
        const EngineRuntime& rt, int32_t trackId, int32_t deviceIndex, engine::DeviceFamily family) noexcept {
        auto t = rt.findTrackAuthoring(trackId);
        if (!t) return 0;
        switch (family) {
        case engine::DeviceFamily::AudioEffect:
            return (deviceIndex >= 0 && deviceIndex < static_cast<int32_t>(t->devices.size()) &&
                       t->devices[static_cast<size_t>(deviceIndex)])
                       ? t->devices[static_cast<size_t>(deviceIndex)]->lineage()
                       : 0;
        case engine::DeviceFamily::MidiEffect:
            return (deviceIndex >= 0 && deviceIndex < static_cast<int32_t>(t->midiEffects.size()) &&
                       t->midiEffects[static_cast<size_t>(deviceIndex)])
                       ? t->midiEffects[static_cast<size_t>(deviceIndex)]->lineage()
                       : 0;
        case engine::DeviceFamily::Instrument: return t->instrument ? t->instrument->uid() : 0;
        }
        return 0;
    }

    static bool resolveDevice(const EngineRuntime& rt, int32_t trackId, engine::DeviceFamily family, uint64_t deviceId,
        int32_t& outIndex) noexcept {
        outIndex = -1;
        if (deviceId == 0) return false;
        auto t = rt.findTrackAuthoring(trackId);
        if (!t) return false;
        switch (family) {
        case engine::DeviceFamily::AudioEffect:
            for (size_t i = 0; i < t->devices.size(); ++i) {
                if (t->devices[i] && t->devices[i]->lineage() == deviceId) {
                    outIndex = static_cast<int32_t>(i);
                    return true;
                }
            }
            return false;
        case engine::DeviceFamily::MidiEffect:
            for (size_t i = 0; i < t->midiEffects.size(); ++i) {
                if (t->midiEffects[i] && t->midiEffects[i]->lineage() == deviceId) {
                    outIndex = static_cast<int32_t>(i);
                    return true;
                }
            }
            return false;
        case engine::DeviceFamily::Instrument: return t->instrument && t->instrument->uid() == deviceId;
        }
        return false;
    }

    static uint64_t rackIdentity(const EngineRuntime& rt, int32_t trackId, int32_t deviceIndex) noexcept {
        if (!rt.racks().coreAt(trackId, deviceIndex)) return 0;
        if (deviceIndex <= engine::kNestedRackBase) {
            auto* host = rt.racks().coreAt(trackId, -1);
            auto* inst = host ? host->chainInstrument(engine::kNestedRackBase - deviceIndex) : nullptr;
            return inst ? inst->uid() : 0;
        }
        if (deviceIndex <= engine::kMidiRackBase) {
            return deviceIdentity(rt, trackId, engine::kMidiRackBase - deviceIndex, engine::DeviceFamily::MidiEffect);
        }
        if (deviceIndex < 0) return deviceIdentity(rt, trackId, -1, engine::DeviceFamily::Instrument);
        return deviceIdentity(rt, trackId, deviceIndex, engine::DeviceFamily::AudioEffect);
    }

    static bool resolveRack(const EngineRuntime& rt, int32_t trackId, int32_t origDeviceIndex, uint64_t deviceId,
        int32_t& outDeviceIndex) noexcept {
        outDeviceIndex = -1;
        if (deviceId == 0) return false;
        if (origDeviceIndex <= engine::kNestedRackBase) {
            auto* host = rt.racks().coreAt(trackId, -1);
            if (!host) return false;
            const int32_t chains = host->chainCount();
            for (int32_t c = 0; c < chains; ++c) {
                auto* inst = host->chainInstrument(c);
                if (inst && inst->uid() == deviceId && inst->asRack() != nullptr) {
                    outDeviceIndex = engine::kNestedRackBase - c;
                    return true;
                }
            }
            return false;
        }
        if (origDeviceIndex <= engine::kMidiRackBase) {
            int32_t idx = -1;
            if (!resolveDevice(rt, trackId, engine::DeviceFamily::MidiEffect, deviceId, idx)) return false;
            const int32_t di = engine::kMidiRackBase - idx;
            if (!rt.racks().coreAt(trackId, di)) return false;
            outDeviceIndex = di;
            return true;
        }
        if (origDeviceIndex < 0) {
            int32_t ignored = -1;
            if (!resolveDevice(rt, trackId, engine::DeviceFamily::Instrument, deviceId, ignored)) return false;
            if (!rt.racks().coreAt(trackId, -1)) return false;
            outDeviceIndex = -1;
            return true;
        }
        int32_t idx = -1;
        if (!resolveDevice(rt, trackId, engine::DeviceFamily::AudioEffect, deviceId, idx)) return false;
        if (!rt.racks().coreAt(trackId, idx)) return false;
        outDeviceIndex = idx;
        return true;
    }

    static uint64_t rackChainIdentity(
        const EngineRuntime& rt, int32_t trackId, int32_t deviceIndex, int32_t chainIndex) noexcept {
        const auto* r = rt.racks().coreAt(trackId, deviceIndex);
        return r ? r->chainBaseUid(chainIndex) : 0;
    }

    static bool resolveRackChain(const EngineRuntime& rt, int32_t trackId, int32_t origDeviceIndex,
        uint64_t rackDeviceId, uint64_t chainUid, int32_t& outDeviceIndex, int32_t& outChainIndex) noexcept {
        outChainIndex = -1;
        if (chainUid == 0 || !resolveRack(rt, trackId, origDeviceIndex, rackDeviceId, outDeviceIndex)) return false;
        const auto* r = rt.racks().coreAt(trackId, outDeviceIndex);
        if (!r) return false;
        const int32_t idx = r->chainIndexOfUid(chainUid);
        if (idx < 0 || idx >= r->chainCount()) return false;
        outChainIndex = idx;
        return true;
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
