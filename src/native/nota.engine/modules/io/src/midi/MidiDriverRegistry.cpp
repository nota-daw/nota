// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/midi/MidiDriverRegistry.h"

#include "midi/drivers/MidiDrivers.h"
#include "nota/core/ConfigFile.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/midi/IMidiOutput.h"
#include "nota/io/midi/IMidiInput.h"
#include "nota/io/midi/IMidiDriver.h"
#include "nota/io/midi/NullMidiDriver.h"

#include <memory>
#include <cstdint>
#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
#include <mutex>
#include <string_view>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::midi {
namespace {

using nlohmann::json;

std::once_flag g_builtinMidiOnce;

std::shared_ptr<const IMidiDriver> activeMidiDriver() {
    registerBuiltinMidiDrivers();
    return MidiDriverRegistry::instance().defaultDriver();
}

bool getBoolTolerant(const json& j, const char* key, bool fallback) {
    const auto it = j.find(key);
    if (it == j.end()) return fallback;
    if (it->is_boolean()) return it->get<bool>();
    if (it->is_number()) return *it != 0;
    return fallback;
}

double getNumberTolerant(const json& j, const char* key, double fallback) {
    const auto it = j.find(key);
    if (it == j.end()) return fallback;
    if (it->is_number()) return it->get<double>();
    if (it->is_boolean()) return it->get<bool>() ? 1.0 : 0.0;
    return fallback;
}

} // namespace

MidiDriverRegistry& MidiDriverRegistry::instance() {
    static MidiDriverRegistry* reg = new MidiDriverRegistry();
    return *reg;
}

void MidiDriverRegistry::registerDriver(std::shared_ptr<const IMidiDriver> driver) {
    if (!driver) return;
    std::lock_guard<std::mutex> lock(mutex_);
    for (auto& existing : drivers_) {
        if (existing->id() == driver->id()) {
            existing = std::move(driver);
            return;
        }
    }
    drivers_.push_back(std::move(driver));
}

std::shared_ptr<const IMidiDriver> MidiDriverRegistry::find(std::string_view id) const {
    std::lock_guard<std::mutex> lock(mutex_);
    for (const auto& d : drivers_) {
        if (d->id() == id) return d;
    }
    return nullptr;
}

std::shared_ptr<const IMidiDriver> MidiDriverRegistry::defaultDriver() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return drivers_.empty() ? nullptr : drivers_.front();
}

std::vector<std::shared_ptr<const IMidiDriver>> MidiDriverRegistry::list() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return drivers_;
}

void registerBuiltinMidiDrivers() {
    std::call_once(g_builtinMidiOnce, [] {
        auto& reg = MidiDriverRegistry::instance();
#if defined(__APPLE__)
        reg.registerDriver(detail::createCoreMidiDriver());
#elif defined(_WIN32) || defined(__linux__)
        reg.registerDriver(detail::createRtMidiDriver());
#endif
        reg.registerDriver(std::make_shared<NullMidiDriver>());
    });
}

std::unique_ptr<IMidiInput> createMidiInput() {
    const auto d = activeMidiDriver();
    return d ? d->createInput() : nullptr;
}

std::unique_ptr<IMidiOutput> createMidiOutput() {
    const auto d = activeMidiDriver();
    return d ? d->createOutput() : nullptr;
}

std::vector<MidiDeviceInfo> enumerateMidiInputs() {
    const auto d = activeMidiDriver();
    return d ? d->enumerateInputs() : std::vector<MidiDeviceInfo>{};
}

std::string midiUidForEndpoint(uint32_t endpoint) {
    const auto d = activeMidiDriver();
    return d ? d->endpointUid(endpoint) : std::string{};
}

std::string midiEndpointName(uint32_t endpoint) {
    const auto d = activeMidiDriver();
    return d ? d->endpointName(endpoint) : std::string{};
}

uint32_t midiPortsGeneration() {
    const auto d = activeMidiDriver();
    return d ? d->portsGeneration() : 0u;
}

MidiConfig MidiConfigStore::load() {
    MidiConfig cfg;
    const std::string dir = core::cfgfile::dataDir();
    if (dir.empty()) return cfg;
    const std::string text = core::cfgfile::readFile((std::filesystem::path(dir) / "midi.json").string());
    if (text.empty()) return cfg;
    try {
        const json j = json::parse(text);
        if (!j.is_object()) return cfg;
        const auto it = j.find("disabledInputUids");
        if (it != j.end() && it->is_array()) {
            for (const auto& el : *it) {
                if (el.is_string()) cfg.disabledInputUids.push_back(el.get<std::string>());
            }
        }
        cfg.mpe = getBoolTolerant(j, "mpe", true);
        const double bend = getNumberTolerant(j, "mpeBendRange", 48.0);
        cfg.mpeBendRange = std::clamp(static_cast<int32_t>(bend), 1, 96);
    } catch (...) {}
    return cfg;
}

void MidiConfigStore::save(const MidiConfig& cfg) {
    const std::string dir = core::cfgfile::dataDir();
    if (dir.empty()) return;
    json j;
    j["disabledInputUids"] = cfg.disabledInputUids;
    j["mpe"] = cfg.mpe;
    j["mpeBendRange"] = cfg.mpeBendRange;
    core::cfgfile::writeFileAtomic((std::filesystem::path(dir) / "midi.json").string(), j.dump(2) + "\n");
}

MidiConfig loadMidiConfig() {
    return MidiConfigStore::load();
}

void saveMidiConfig(const MidiConfig& cfg) {
    MidiConfigStore::save(cfg);
}

} // namespace nota::io::midi
