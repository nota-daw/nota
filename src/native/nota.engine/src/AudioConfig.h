// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio device configuration (M7-1): the user's chosen output/input device,
// sample rate and buffer size, persisted across launches, plus CoreAudio
// device enumeration. Devices are keyed by their stable UID string
// (kAudioDevicePropertyDeviceUID) — the numeric AudioDeviceID is not stable
// across reboots/reconnects, so we resolve UID -> id at start() time.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nota {

struct AudioConfig {
    std::string outputDeviceUid;  // "" = system default output
    std::string inputDeviceUid;   // "" = system default input
    double      sampleRate   = 0.0; // 0 = device default
    int32_t     bufferFrames = 0;   // 0 = device default
    bool        wasapiExclusive = false; // Windows/WASAPI only; ignored elsewhere
};

struct AudioDeviceInfo {
    std::string uid;
    std::string name;
};

// Enumerate devices that expose at least one stream on the requested scope
// (inputScope=true -> capture-capable devices, false -> playback-capable).
std::vector<AudioDeviceInfo> enumerateAudioDevices(bool inputScope);

// Resolve a persisted UID to a live AudioDeviceID (returned as uint32_t so the
// AudioBackend interface stays free of CoreAudio headers). An empty or unknown
// UID falls back to the current system default device; 0 if none exists.
uint32_t resolveAudioDeviceId(const std::string& uid, bool inputScope);

// Persistence: ~/Library/Application Support/Nota/audio.json (atomic write).
AudioConfig loadAudioConfig();
void        saveAudioConfig(const AudioConfig& cfg);

} // namespace nota
