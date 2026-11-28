// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#if defined(__APPLE__)

#include <CoreAudio/AudioHardware.h>
#include <CoreAudio/AudioHardwareBase.h>
#include <CoreAudio/AudioHardwareDeprecated.h>
#include <CoreAudio/CoreAudio.h>
#include <CoreAudio/CoreAudioTypes.h>
#include <CoreAudioTypes/CoreAudioBaseTypes.h>
#include <MacTypes.h>

#include <chrono>
#include <thread>
#include <vector>

namespace nota::io::audio::coreaudio {

inline double deviceNominalSampleRate(AudioDeviceID dev) {
    if (dev == kAudioObjectUnknown) return 0.0;
    const AudioObjectPropertyAddress addr{
        kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    Float64 sr = 0.0;
    UInt32 size = sizeof(sr);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &sr) != noErr) return 0.0;
    return sr;
}

inline bool isBluetoothDevice(AudioDeviceID dev) {
    const AudioObjectPropertyAddress addr{
        kAudioDevicePropertyTransportType, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    UInt32 transport = 0;
    UInt32 size = sizeof(transport);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &transport) != noErr) return false;
    return transport == kAudioDeviceTransportTypeBluetooth || transport == kAudioDeviceTransportTypeBluetoothLE;
}

inline bool deviceSupportsSampleRate(AudioDeviceID dev, double sr) {
    const AudioObjectPropertyAddress addr{kAudioDevicePropertyAvailableNominalSampleRates,
        kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(dev, &addr, 0, nullptr, &size) != noErr || size == 0) return false;
    std::vector<AudioValueRange> ranges(size / sizeof(AudioValueRange));
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, ranges.data()) != noErr) return false;
    for (const auto& r : ranges) {
        if (sr >= r.mMinimum - 0.5 && sr <= r.mMaximum + 0.5) return true;
    }
    return false;
}

inline bool setDeviceNominalSampleRate(AudioDeviceID dev, double sr, int timeoutMs = 1000) {
    if (dev == kAudioObjectUnknown || !(sr > 0.0)) return false;
    if (deviceNominalSampleRate(dev) == sr) return true;
    if (isBluetoothDevice(dev) || !deviceSupportsSampleRate(dev, sr)) return false;
    Float64 want = sr;
    const AudioObjectPropertyAddress addr{
        kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    if (AudioObjectSetPropertyData(dev, &addr, 0, nullptr, sizeof(want), &want) != noErr) return false;
    for (int waited = 0; waited < timeoutMs; waited += 10) {
        if (deviceNominalSampleRate(dev) == sr) return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    return deviceNominalSampleRate(dev) == sr;
}

} // namespace nota::io::audio::coreaudio

#endif
