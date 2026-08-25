// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

#include "MidiConfig.h"

#import <CoreMIDI/CoreMIDI.h>
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

std::string endpointDisplayName(MIDIEndpointRef ep) {
    CFStringRef name = nullptr;
    if (MIDIObjectGetStringProperty(ep, kMIDIPropertyDisplayName, &name) != noErr || !name)
        return {};
    std::string out = cfToString(name);
    CFRelease(name);
    return out;
}

// ~/Library/Application Support/Nota/midi.json
NSString* configPath() {
    NSArray* dirs = NSSearchPathForDirectoriesInDomains(
        NSApplicationSupportDirectory, NSUserDomainMask, YES);
    if (dirs.count == 0) return nil;
    NSString* dir = [dirs[0] stringByAppendingPathComponent:@"Nota"];
    [[NSFileManager defaultManager] createDirectoryAtPath:dir
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    return [dir stringByAppendingPathComponent:@"midi.json"];
}

} // namespace

std::string midiUidForEndpoint(uint32_t endpoint) {
    SInt32 uid = 0;
    if (MIDIObjectGetIntegerProperty(static_cast<MIDIObjectRef>(endpoint),
                                     kMIDIPropertyUniqueID, &uid) != noErr)
        return {};
    return std::to_string(uid);
}

std::vector<MidiDeviceInfo> enumerateMidiInputs() {
    std::vector<MidiDeviceInfo> out;
    const ItemCount n = MIDIGetNumberOfSources();
    for (ItemCount i = 0; i < n; ++i) {
        MIDIEndpointRef src = MIDIGetSource(i);
        if (!src) continue;
        MidiDeviceInfo info;
        info.uid  = midiUidForEndpoint(src);
        info.name = endpointDisplayName(src);
        if (info.uid.empty()) continue; // no stable id -> can't persist a choice
        if (info.name.empty()) info.name = "MIDI input " + info.uid;
        out.push_back(std::move(info));
    }
    return out;
}

MidiConfig loadMidiConfig() {
    MidiConfig cfg;
    NSString* path = configPath();
    if (!path) return cfg;
    NSData* data = [NSData dataWithContentsOfFile:path];
    if (!data) return cfg;
    id json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![json isKindOfClass:[NSDictionary class]]) return cfg;
    NSDictionary* d = (NSDictionary*)json;
    if (NSArray* arr = d[@"disabledInputUids"]; [arr isKindOfClass:[NSArray class]]) {
        for (id item in arr)
            if ([item isKindOfClass:[NSString class]])
                cfg.disabledInputUids.push_back(((NSString*)item).UTF8String);
    }
    return cfg;
}

void saveMidiConfig(const MidiConfig& cfg) {
    NSString* path = configPath();
    if (!path) return;
    NSMutableArray* arr = [NSMutableArray arrayWithCapacity:cfg.disabledInputUids.size()];
    for (const auto& uid : cfg.disabledInputUids)
        [arr addObject:[NSString stringWithUTF8String:uid.c_str()]];
    NSDictionary* d = @{ @"disabledInputUids": arr };
    NSData* data = [NSJSONSerialization dataWithJSONObject:d
                                                  options:NSJSONWritingPrettyPrinted
                                                    error:nil];
    if (!data) return;
    [data writeToFile:path atomically:YES];
}

} // namespace nota
