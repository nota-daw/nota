// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Device addressing, clipboard, transfer flags, sidechain constants, and
// preset tag records shared across EngineRuntime and devices/*.

#pragma once

#include "nota/dsp/Device.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiDevice.h"

#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

inline constexpr int32_t kTransferSidechainCleared = 1;
inline constexpr int32_t kTransferLinksDropped = 2;
inline constexpr int32_t kTransferSourceRemoved = 4;
inline constexpr int32_t kSidechainRoutes = 16;
inline constexpr int32_t kMidiRackBase = -1000;
inline constexpr int32_t kNestedRackBase = -100000;

inline constexpr bool isNestedRack(int32_t deviceIndex) noexcept {
    return deviceIndex <= kNestedRackBase;
}

struct DeviceRef {
    int32_t fam = -1;
    int32_t idx = -1;
    uint64_t uid = 0;
    int32_t slot = -1;
};

using DeviceRefMap = std::function<DeviceRef(const DeviceRef&)>;
using NestedMap = std::function<std::pair<uint64_t, int32_t>(uint64_t, int32_t)>;

struct ClipEntry {
    std::shared_ptr<nota::dsp::MidiDevice> midi;
    std::shared_ptr<nota::dsp::Instrument> inst;
    std::shared_ptr<nota::dsp::Device> dev;
};

struct PresetTagData {
    std::string id;
    std::string name;
    std::vector<float> base;
};

} // namespace nota::detail
