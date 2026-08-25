// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Non-Apple implementation of MidiConfig.h for Windows + Linux, mirroring the macOS
// MidiConfig.mm but backed by RtMidi (WinMM on Windows; ALSA sequencer on Linux).
// RtMidi exposes no numeric unique id, so the port's display name serves as the
// stable UID (a documented v1 limitation: two inputs with identical names collide).
// Config persists to <config-dir>/midi.json (NotaConfigFile::dataDir).

#include "MidiConfig.h"
#include "NotaConfigFile.h"

#include "rtmidi/RtMidi.h"

#include <filesystem>
#include <memory>
#include <string>

namespace nota {

// portIndex is an RtMidi input port number; the UID is that port's name.
std::string midiUidForEndpoint(uint32_t endpoint) {
    try {
        RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
        if (endpoint < in.getPortCount())
            return in.getPortName(endpoint);
    } catch (...) {
        // fall through
    }
    return {};
}

std::vector<MidiDeviceInfo> enumerateMidiInputs() {
    std::vector<MidiDeviceInfo> out;
    try {
        RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
        const unsigned int n = in.getPortCount();
        for (unsigned int i = 0; i < n; ++i) {
            MidiDeviceInfo info;
            info.name = in.getPortName(i);
            info.uid  = info.name;               // RtMidi has no numeric id
            if (info.uid.empty()) continue;
            out.push_back(std::move(info));
        }
    } catch (...) {
        // no MIDI subsystem / no ports
    }
    return out;
}

MidiConfig loadMidiConfig() {
    MidiConfig cfg;
    std::string dir = cfgfile::dataDir();
    if (dir.empty()) return cfg;
    std::string path = (std::filesystem::path(dir) / "midi.json").string();
    std::string json = cfgfile::readFile(path);
    if (json.empty()) return cfg;
    cfgfile::jsonGetStringArray(json, "disabledInputUids", cfg.disabledInputUids);
    return cfg;
}

void saveMidiConfig(const MidiConfig& cfg) {
    std::string dir = cfgfile::dataDir();
    if (dir.empty()) return;
    std::string json = "{\n  \"disabledInputUids\": [";
    for (size_t i = 0; i < cfg.disabledInputUids.size(); ++i) {
        if (i) json += ",";
        json += "\n    \"" + cfgfile::jsonEscape(cfg.disabledInputUids[i]) + "\"";
    }
    json += cfg.disabledInputUids.empty() ? "]\n}\n" : "\n  ]\n}\n";
    std::string path = (std::filesystem::path(dir) / "midi.json").string();
    cfgfile::writeFileAtomic(path, json);
}

} // namespace nota
