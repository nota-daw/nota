// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Non-Apple device enumeration for AudioConfig.h (Windows + Linux, miniaudio
// WASAPI / PulseAudio / ALSA). Persistence (audio.json) lives in the shared
// ConfigJson.cpp. Devices are keyed by a UID string derived from the miniaudio
// ma_device_id (stable for a given endpoint within a session).

#include "audio_io/AudioConfig.h"
#include "MiniaudioDevices.h"
#include "nota_miniaudio.h"

#include <cstdio>
#include <string>
#include <vector>

namespace nota {
namespace {

// Serialize a ma_device_id to a stable hex string (its raw bytes). miniaudio
// zero-initialises device-info records before filling them, so the same endpoint
// yields the same bytes across enumerations within a session.
std::string idToUid(const ma_device_id& id) {
    const auto* bytes = reinterpret_cast<const unsigned char*>(&id);
    std::string out;
    out.reserve(sizeof(id) * 2);
    char buf[3];
    for (size_t i = 0; i < sizeof(id); ++i) {
        std::snprintf(buf, sizeof(buf), "%02x", bytes[i]);
        out += buf;
    }
    return out;
}

// Enumerate devices on one scope, copying the info records out (miniaudio owns
// the originals only until the context is uninitialised).
bool enumerate(bool inputScope, std::vector<ma_device_info>& out) {
    ma_context ctx;
    if (ma_context_init(nullptr, 0, nullptr, &ctx) != MA_SUCCESS) return false;

    ma_device_info* playback = nullptr;
    ma_uint32 playbackCount = 0;
    ma_device_info* capture = nullptr;
    ma_uint32 captureCount = 0;
    ma_result r = ma_context_get_devices(&ctx, &playback, &playbackCount, &capture, &captureCount);
    if (r == MA_SUCCESS) {
        ma_device_info* src = inputScope ? capture : playback;
        ma_uint32 n = inputScope ? captureCount : playbackCount;
        out.assign(src, src + n);
    }
    ma_context_uninit(&ctx);
    return r == MA_SUCCESS;
}

} // namespace

std::vector<AudioDeviceInfo> enumerateAudioDevices(bool inputScope) {
    std::vector<AudioDeviceInfo> out;
    std::vector<ma_device_info> infos;
    if (!enumerate(inputScope, infos)) return out;
    for (const auto& info : infos) {
        AudioDeviceInfo d;
        d.uid = idToUid(info.id);
        d.name = info.name[0] ? std::string(info.name) : d.uid;
        out.push_back(std::move(d));
    }
    return out;
}

uint32_t resolveAudioDeviceId(const std::string& uid, bool inputScope) {
    if (uid.empty()) return 0; // system default
    std::vector<ma_device_info> infos;
    if (!enumerate(inputScope, infos)) return 0;
    for (size_t i = 0; i < infos.size(); ++i)
        if (idToUid(infos[i].id) == uid) return static_cast<uint32_t>(i + 1); // 1-based; 0 reserved for default
    return 0; // saved device gone -> fall back to default
}

bool notaMiniaudioDeviceIdForIndex(bool inputScope, uint32_t oneBasedIndex, ma_device_id& out) {
    if (oneBasedIndex == 0) return false;
    std::vector<ma_device_info> infos;
    if (!enumerate(inputScope, infos)) return false;
    if (oneBasedIndex > infos.size()) return false;
    out = infos[oneBasedIndex - 1].id;
    return true;
}

} // namespace nota
