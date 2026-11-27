// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/audio/AudioConfigStore.h"

#include "nota/core/ConfigFile.h"
#include "nota/io/audio/AudioIoTypes.h"

#include <cstdint>
#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
#include <string>

namespace nota::io::audio {
namespace {

using nlohmann::json;

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

std::string getStringTolerant(const json& j, const char* key) {
    const auto it = j.find(key);
    if (it == j.end()) return {};
    if (it->is_string()) return it->get<std::string>();
    return {};
}

} // namespace

AudioConfig AudioConfigStore::load() {
    AudioConfig cfg;
    const std::string dir = core::cfgfile::dataDir();
    if (dir.empty()) return cfg;
    const std::string text = core::cfgfile::readFile((std::filesystem::path(dir) / "audio.json").string());
    if (text.empty()) return cfg;
    try {
        const json j = json::parse(text);
        if (!j.is_object()) return cfg;
        cfg.outputDeviceUid = getStringTolerant(j, "outputDeviceUid");
        cfg.inputDeviceUid = getStringTolerant(j, "inputDeviceUid");
        cfg.sampleRate = getNumberTolerant(j, "sampleRate", 0.0);
        cfg.bufferFrames = static_cast<int32_t>(getNumberTolerant(j, "bufferFrames", 0.0));
        cfg.wasapiExclusive = getBoolTolerant(j, "wasapiExclusive", false);
#if defined(_WIN32)
        cfg.audioSystem = getStringTolerant(j, "audioSystem") == "asio" ? AudioSystem::Asio : AudioSystem::Wasapi;
#endif
        cfg.asioDriverClsid = getStringTolerant(j, "asioDriver");
        cfg.asioDriverName = getStringTolerant(j, "asioDriverName");
        cfg.asioOutputPair = std::max(0, static_cast<int32_t>(getNumberTolerant(j, "asioOutputPair", 0.0)));
        cfg.asioInputPair = std::max(0, static_cast<int32_t>(getNumberTolerant(j, "asioInputPair", 0.0)));
    } catch (...) {}
    return cfg;
}

void AudioConfigStore::save(const AudioConfig& cfg) {
    const std::string dir = core::cfgfile::dataDir();
    if (dir.empty()) return;
    json j;
    j["outputDeviceUid"] = cfg.outputDeviceUid;
    j["inputDeviceUid"] = cfg.inputDeviceUid;
    j["sampleRate"] = cfg.sampleRate;
    j["bufferFrames"] = cfg.bufferFrames;
    j["wasapiExclusive"] = cfg.wasapiExclusive;
    j["audioSystem"] = cfg.audioSystem == AudioSystem::Asio ? "asio" : "wasapi";
    j["asioDriver"] = cfg.asioDriverClsid;
    j["asioDriverName"] = cfg.asioDriverName;
    j["asioOutputPair"] = cfg.asioOutputPair;
    j["asioInputPair"] = cfg.asioInputPair;
    core::cfgfile::writeFileAtomic((std::filesystem::path(dir) / "audio.json").string(), j.dump(2) + "\n");
}

AudioConfig loadAudioConfig() {
    return AudioConfigStore::load();
}

void saveAudioConfig(const AudioConfig& cfg) {
    AudioConfigStore::save(cfg);
}

} // namespace nota::io::audio
