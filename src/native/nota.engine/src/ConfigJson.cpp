// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Single JSON implementation for the tiny engine config files (audio.json /
// midi.json) on every platform. Previously macOS used NSJSONSerialization while
// Windows/Linux hand-rolled string-concat + substring parsing, which diverged
// (missing keys, bool-vs-0/1). Now both go through the vendored nlohmann/json.
// Runs on the message thread only (file I/O); never on the audio thread (AR-6).

#include "AudioConfig.h"
#include "MidiConfig.h"
#include "NotaConfigFile.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>

namespace nota {
namespace {

using nlohmann::json;

bool getBoolTolerant(const json& j, const char* key, bool fallback) {
    auto it = j.find(key);
    if (it == j.end()) return fallback;
    if (it->is_boolean()) return it->get<bool>();
    if (it->is_number()) return *it != 0;
    return fallback;
}

double getNumberTolerant(const json& j, const char* key, double fallback) {
    auto it = j.find(key);
    if (it == j.end()) return fallback;
    if (it->is_number()) return it->get<double>();
    if (it->is_boolean()) return it->get<bool>() ? 1.0 : 0.0;
    return fallback;
}

std::string getStringTolerant(const json& j, const char* key) {
    auto it = j.find(key);
    if (it == j.end()) return {};
    if (it->is_string()) return it->get<std::string>();
    return {};
}

} // namespace

AudioConfig loadAudioConfig() {
    AudioConfig cfg;
    std::string dir = cfgfile::dataDir();
    if (dir.empty()) return cfg;
    std::string text = cfgfile::readFile((std::filesystem::path(dir) / "audio.json").string());
    if (text.empty()) return cfg;
    try {
        json j = json::parse(text);
        if (!j.is_object()) return cfg;
        cfg.outputDeviceUid = getStringTolerant(j, "outputDeviceUid");
        cfg.inputDeviceUid = getStringTolerant(j, "inputDeviceUid");
        cfg.sampleRate = getNumberTolerant(j, "sampleRate", 0.0);
        cfg.bufferFrames = static_cast<int32_t>(getNumberTolerant(j, "bufferFrames", 0.0));
        cfg.wasapiExclusive = getBoolTolerant(j, "wasapiExclusive", false);
    } catch (...) {
        // Corrupt file -> defaults, like before.
    }
    return cfg;
}

void saveAudioConfig(const AudioConfig& cfg) {
    std::string dir = cfgfile::dataDir();
    if (dir.empty()) return;
    json j;
    j["outputDeviceUid"] = cfg.outputDeviceUid;
    j["inputDeviceUid"] = cfg.inputDeviceUid;
    j["sampleRate"] = cfg.sampleRate;
    j["bufferFrames"] = cfg.bufferFrames;
    j["wasapiExclusive"] = cfg.wasapiExclusive;
    cfgfile::writeFileAtomic((std::filesystem::path(dir) / "audio.json").string(), j.dump(2) + "\n");
}

MidiConfig loadMidiConfig() {
    MidiConfig cfg;
    std::string dir = cfgfile::dataDir();
    if (dir.empty()) return cfg;
    std::string text = cfgfile::readFile((std::filesystem::path(dir) / "midi.json").string());
    if (text.empty()) return cfg;
    try {
        json j = json::parse(text);
        if (!j.is_object()) return cfg;
        auto it = j.find("disabledInputUids");
        if (it != j.end() && it->is_array()) {
            for (const auto& el : *it)
                if (el.is_string())
                    cfg.disabledInputUids.push_back(el.get<std::string>());
        }
        cfg.mpe = getBoolTolerant(j, "mpe", true);
        double bend = getNumberTolerant(j, "mpeBendRange", 48.0);
        cfg.mpeBendRange = std::clamp(static_cast<int32_t>(bend), 1, 96);
    } catch (...) {
    }
    return cfg;
}

void saveMidiConfig(const MidiConfig& cfg) {
    std::string dir = cfgfile::dataDir();
    if (dir.empty()) return;
    json j;
    j["disabledInputUids"] = cfg.disabledInputUids;
    j["mpe"] = cfg.mpe;
    j["mpeBendRange"] = cfg.mpeBendRange;
    cfgfile::writeFileAtomic((std::filesystem::path(dir) / "midi.json").string(), j.dump(2) + "\n");
}

} // namespace nota
