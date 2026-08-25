// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

#include "AudioConfig.h"

#import <CoreAudio/CoreAudio.h>
#import <Foundation/Foundation.h>

namespace nota {
namespace {

std::string cfToString(CFStringRef s) {
    if (!s) return {};
    if (const char* fast = CFStringGetCStringPtr(s, kCFStringEncodingUTF8))
        return std::string(fast);
    CFIndex len = CFStringGetLength(s);
    CFIndex max = CFStringGetMaximumSizeForEncoding(len, kCFStringEncodingUTF8) + 1;
    std::string out(static_cast<size_t>(max), '\0');
    if (CFStringGetCString(s, out.data(), max, kCFStringEncodingUTF8))
        out.resize(std::char_traits<char>::length(out.c_str()));
    else
        out.clear();
    return out;
}

// Number of channels the device exposes on the given scope (0 = none).
UInt32 deviceChannels(AudioDeviceID dev, bool inputScope) {
    AudioObjectPropertyAddress addr{
        kAudioDevicePropertyStreamConfiguration,
        inputScope ? kAudioDevicePropertyScopeInput : kAudioDevicePropertyScopeOutput,
        kAudioObjectPropertyElementMain };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(dev, &addr, 0, nullptr, &size) != noErr || size == 0)
        return 0;
    std::vector<uint8_t> storage(size);
    auto* abl = reinterpret_cast<AudioBufferList*>(storage.data());
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, abl) != noErr)
        return 0;
    UInt32 channels = 0;
    for (UInt32 i = 0; i < abl->mNumberBuffers; ++i)
        channels += abl->mBuffers[i].mNumberChannels;
    return channels;
}

std::string deviceUidString(AudioDeviceID dev) {
    AudioObjectPropertyAddress addr{ kAudioDevicePropertyDeviceUID,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain };
    CFStringRef uid = nullptr;
    UInt32 size = sizeof(uid);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &uid) != noErr || !uid)
        return {};
    std::string out = cfToString(uid);
    CFRelease(uid);
    return out;
}

std::string deviceNameString(AudioDeviceID dev) {
    AudioObjectPropertyAddress addr{ kAudioObjectPropertyName,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain };
    CFStringRef name = nullptr;
    UInt32 size = sizeof(name);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &name) != noErr || !name)
        return {};
    std::string out = cfToString(name);
    CFRelease(name);
    return out;
}

AudioDeviceID defaultDeviceId(bool inputScope) {
    AudioObjectPropertyAddress addr{ inputScope ? kAudioHardwarePropertyDefaultInputDevice
                                                : kAudioHardwarePropertyDefaultOutputDevice,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain };
    AudioDeviceID dev = kAudioObjectUnknown;
    UInt32 size = sizeof(dev);
    AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &size, &dev);
    return dev;
}

// ~/Library/Application Support/Nota/audio.json
NSString* configPath() {
    NSArray* dirs = NSSearchPathForDirectoriesInDomains(
        NSApplicationSupportDirectory, NSUserDomainMask, YES);
    if (dirs.count == 0) return nil;
    NSString* dir = [dirs[0] stringByAppendingPathComponent:@"Nota"];
    [[NSFileManager defaultManager] createDirectoryAtPath:dir
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    return [dir stringByAppendingPathComponent:@"audio.json"];
}

} // namespace

std::vector<AudioDeviceInfo> enumerateAudioDevices(bool inputScope) {
    std::vector<AudioDeviceInfo> out;
    AudioObjectPropertyAddress addr{ kAudioHardwarePropertyDevices,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain };
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(kAudioObjectSystemObject, &addr, 0, nullptr, &size) != noErr
        || size == 0)
        return out;
    const UInt32 count = size / sizeof(AudioDeviceID);
    std::vector<AudioDeviceID> ids(count);
    if (AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &size, ids.data()) != noErr)
        return out;

    for (AudioDeviceID dev : ids) {
        if (deviceChannels(dev, inputScope) == 0) continue; // wrong scope
        AudioDeviceInfo info;
        info.uid  = deviceUidString(dev);
        info.name = deviceNameString(dev);
        if (info.uid.empty()) continue;
        if (info.name.empty()) info.name = info.uid;
        out.push_back(std::move(info));
    }
    return out;
}

uint32_t resolveAudioDeviceId(const std::string& uid, bool inputScope) {
    if (!uid.empty()) {
        CFStringRef cf = CFStringCreateWithCString(kCFAllocatorDefault, uid.c_str(),
                                                   kCFStringEncodingUTF8);
        if (cf) {
            AudioObjectPropertyAddress addr{ kAudioHardwarePropertyTranslateUIDToDevice,
                                             kAudioObjectPropertyScopeGlobal,
                                             kAudioObjectPropertyElementMain };
            AudioDeviceID dev = kAudioObjectUnknown;
            UInt32 size = sizeof(dev);
            OSStatus st = AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr,
                                                     sizeof(cf), &cf, &size, &dev);
            CFRelease(cf);
            if (st == noErr && dev != kAudioObjectUnknown)
                return dev; // matched the saved device
        }
    }
    return defaultDeviceId(inputScope); // empty or unknown UID -> current default
}

AudioConfig loadAudioConfig() {
    AudioConfig cfg;
    NSString* path = configPath();
    if (!path) return cfg;
    NSData* data = [NSData dataWithContentsOfFile:path];
    if (!data) return cfg;
    id json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![json isKindOfClass:[NSDictionary class]]) return cfg;
    NSDictionary* d = (NSDictionary*)json;
    if (NSString* s = d[@"outputDeviceUid"]; [s isKindOfClass:[NSString class]])
        cfg.outputDeviceUid = s.UTF8String;
    if (NSString* s = d[@"inputDeviceUid"]; [s isKindOfClass:[NSString class]])
        cfg.inputDeviceUid = s.UTF8String;
    if (NSNumber* n = d[@"sampleRate"]; [n isKindOfClass:[NSNumber class]])
        cfg.sampleRate = n.doubleValue;
    if (NSNumber* n = d[@"bufferFrames"]; [n isKindOfClass:[NSNumber class]])
        cfg.bufferFrames = n.intValue;
    return cfg;
}

void saveAudioConfig(const AudioConfig& cfg) {
    NSString* path = configPath();
    if (!path) return;
    NSDictionary* d = @{
        @"outputDeviceUid": [NSString stringWithUTF8String:cfg.outputDeviceUid.c_str()],
        @"inputDeviceUid":  [NSString stringWithUTF8String:cfg.inputDeviceUid.c_str()],
        @"sampleRate":      @(cfg.sampleRate),
        @"bufferFrames":    @(cfg.bufferFrames),
    };
    NSData* data = [NSJSONSerialization dataWithJSONObject:d
                                                  options:NSJSONWritingPrettyPrinted
                                                    error:nil];
    if (!data) return;
    // Atomic write (temp + rename), like ProjectService.Save.
    [data writeToFile:path atomically:YES];
}

} // namespace nota
