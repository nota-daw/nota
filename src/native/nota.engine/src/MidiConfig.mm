// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// macOS device enumeration for MidiConfig.h (CoreMIDI). Persistence
// (midi.json) lives in the shared ConfigJson.cpp.

#include "MidiConfig.h"

#include <algorithm>
#include <string>

#if defined(__APPLE__)

#import <CoreMIDI/CoreMIDI.h>
#import <CoreFoundation/CoreFoundation.h>

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

} // namespace nota

#endif // defined(__APPLE__)
