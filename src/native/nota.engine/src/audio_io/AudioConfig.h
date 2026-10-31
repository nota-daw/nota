// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio device configuration (M7-1): the user's chosen output/input device,
// sample rate and buffer size, persisted across launches, plus device
// enumeration (CoreAudio on macOS, miniaudio on Windows/Linux). Devices are keyed
// by a stable UID string — numeric device ids are not stable across
// reboots/reconnects, so we resolve UID -> id at start() time. On Windows the
// config also picks the audio system (WASAPI or ASIO) and the ASIO driver and
// channel pairs (specs/049).

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nota {

// Windows audio system (specs/049). Elsewhere always Wasapi, meaning "the native backend".
enum class AudioSystem : int32_t { Wasapi = 0, Asio = 1 };

struct AudioConfig {
    std::string outputDeviceUid;  // "" = system default output
    std::string inputDeviceUid;   // "" = system default input
    double sampleRate = 0.0;      // 0 = device default
    int32_t bufferFrames = 0;     // 0 = device default (ignored under ASIO: the driver decides)
    bool wasapiExclusive = false; // Windows/WASAPI only; ignored elsewhere
    AudioSystem audioSystem = AudioSystem::Wasapi;
    std::string asioDriverClsid;  // the chosen ASIO driver's CLSID (primary key)
    std::string asioDriverName;   // its registry name (fallback key if the CLSID changed)
    int32_t asioOutputPair = 0;   // first channel of the output pair
    int32_t asioInputPair = 0;    // first channel of the input pair
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

// Persistence: audio.json in the data dir the app sets (nota_set_data_dir), atomic write.
AudioConfig loadAudioConfig();
void saveAudioConfig(const AudioConfig& cfg);

} // namespace nota
