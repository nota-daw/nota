// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CoreAudio device queries the output backend and the input capture share: a device's
// nominal rate, whether it is Bluetooth, and whether it lists a rate. Objective-C++ only
// (included from the .mm files). Control thread.

#pragma once

#if defined(__APPLE__)

#import <CoreAudio/CoreAudio.h>

#include <chrono>
#include <thread>
#include <vector>

namespace nota::coreaudio {

inline double deviceNominalSampleRate(AudioDeviceID dev) {
    if (dev == kAudioObjectUnknown) return 0.0;
    AudioObjectPropertyAddress addr{
        kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    Float64 sr = 0.0;
    UInt32 size = sizeof(sr);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &sr) != noErr) return 0.0;
    return sr;
}

// Bluetooth devices (AirPods etc.) get their rate from the link codec. Forcing a
// nominal rate on them (e.g. 44.1 kHz on AirPods that run AAC at 48 kHz) leaves
// the A2DP stream losing sync — a second of crackle, then silence — for every
// app until Bluetooth restarts. Never touch their rate.
inline bool isBluetoothDevice(AudioDeviceID dev) {
    AudioObjectPropertyAddress addr{
        kAudioDevicePropertyTransportType, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    UInt32 transport = 0;
    UInt32 size = sizeof(transport);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &transport) != noErr) return false;
    return transport == kAudioDeviceTransportTypeBluetooth || transport == kAudioDeviceTransportTypeBluetoothLE;
}

inline bool deviceSupportsSampleRate(AudioDeviceID dev, double sr) {
    AudioObjectPropertyAddress addr{kAudioDevicePropertyAvailableNominalSampleRates, kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain};
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(dev, &addr, 0, nullptr, &size) != noErr || size == 0) return false;
    std::vector<AudioValueRange> ranges(size / sizeof(AudioValueRange));
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, ranges.data()) != noErr) return false;
    for (const auto& r : ranges)
        if (sr >= r.mMinimum - 0.5 && sr <= r.mMaximum + 0.5) return true;
    return false;
}

// Ask the device to run at `sr` when it isn't Bluetooth and lists that rate (best effort).
// Returns true when the device runs at `sr` afterwards. The change is asynchronous: wait for
// the device to report it, up to `timeoutMs`.
inline bool setDeviceNominalSampleRate(AudioDeviceID dev, double sr, int timeoutMs = 1000) {
    if (dev == kAudioObjectUnknown || !(sr > 0.0)) return false;
    if (deviceNominalSampleRate(dev) == sr) return true;
    if (isBluetoothDevice(dev) || !deviceSupportsSampleRate(dev, sr)) return false;
    Float64 want = sr;
    AudioObjectPropertyAddress a{
        kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    if (AudioObjectSetPropertyData(dev, &a, 0, nullptr, sizeof(want), &want) != noErr) return false;
    for (int waited = 0; waited < timeoutMs; waited += 10) {
        if (deviceNominalSampleRate(dev) == sr) return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    return deviceNominalSampleRate(dev) == sr;
}

} // namespace nota::coreaudio

#endif
