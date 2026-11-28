// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// macOS Objective-C++ bridges for PluginHost: NSRunningApplication caching and AU workgroup hand-off.

#if defined(__APPLE__)

#include "hosting/MacBridges.h"

#import <AppKit/AppKit.h>
#include <AudioToolbox/AudioToolbox.h>
#include <Block.h>
#include <objc/runtime.h>
#include <os/workgroup.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

namespace nota::pluginhost::detail::mac {
namespace {

OSType fourCharOsType(const std::string& s) noexcept {
    OSType t = 0;
    for (size_t i = 0; i < 4; ++i) {
        const uint8_t ch = i < s.size() ? static_cast<uint8_t>(s[i]) : static_cast<uint8_t>(' ');
        t = (t << 8) | static_cast<OSType>(ch);
    }
    return t;
}

std::string cfStringToUtf8(CFStringRef str) {
    if (str == nullptr) return {};
    const CFIndex len = CFStringGetLength(str);
    const CFIndex maxSize = CFStringGetMaximumSizeForEncoding(len, kCFStringEncodingUTF8) + 1;
    std::string out(static_cast<size_t>(maxSize), '\0');
    if (CFStringGetCString(str, out.data(), maxSize, kCFStringEncodingUTF8)) {
        out.resize(std::strlen(out.c_str()));
        return out;
    }
    return {};
}

} // namespace

void cacheCurrentApplication() {
    static std::once_flag once;
    std::call_once(once, [] {
        Class cls = [NSRunningApplication class];
        Method current = class_getClassMethod(cls, @selector(currentApplication));
        Method name = class_getInstanceMethod(cls, @selector(localizedName));
        if (current == nullptr || name == nullptr) return;
        using CurrentFn = NSRunningApplication* (*)(id, SEL);
        using NameFn = NSString* (*)(id, SEL);
        const auto originalCurrent = reinterpret_cast<CurrentFn>(method_getImplementation(current));
        static const auto originalName = reinterpret_cast<NameFn>(method_getImplementation(name));
        static NSRunningApplication* own = [originalCurrent(cls, @selector(currentApplication)) retain];
        static NSString* ownName = [originalName(own, @selector(localizedName)) copy];
        method_setImplementation(current, imp_implementationWithBlock(^NSRunningApplication*(id) { return own; }));
        method_setImplementation(name, imp_implementationWithBlock(^NSString*(NSRunningApplication* app) {
          return app == own ? ownName : originalName(app, @selector(localizedName));
        }));
    });
}

void retainWorkgroup(void* osWorkgroup) {
    if (osWorkgroup == nullptr) return;
    static std::mutex mx;
    static std::vector<void*> kept;
    const std::lock_guard<std::mutex> lock(mx);
    if (std::find(kept.begin(), kept.end(), osWorkgroup) == kept.end()) {
        if (@available(macOS 11.0, *)) os_retain(static_cast<os_workgroup_t>(osWorkgroup));
        kept.push_back(osWorkgroup);
    }
}

void* copyAuWorkgroupObserver(void* audioUnitPtr) {
    if (audioUnitPtr == nullptr) return nullptr;
    if (@available(macOS 12.0, *)) {
        AudioUnit au = static_cast<AudioUnit>(audioUnitPtr);
        AURenderContextObserver obs = nullptr;
        UInt32 size = sizeof(obs);
        if (AudioUnitGetProperty(
                au, kAudioUnitProperty_RenderContextObserver, kAudioUnitScope_Global, 0, &obs, &size) == noErr &&
            obs != nullptr) {
            return reinterpret_cast<void*>(Block_copy(obs));
        }
    }
    return nullptr;
}

void releaseWorkgroupObserver(void* observerBlock) {
    if (observerBlock != nullptr) { Block_release(observerBlock); }
}

void invokeWorkgroupObserver(void* observerBlock, void* osWorkgroup) {
    if (observerBlock == nullptr) return;
    if (@available(macOS 12.0, *)) {
        AudioUnitRenderContext ctx{};
        ctx.workgroup = static_cast<os_workgroup_t>(osWorkgroup);
        reinterpret_cast<AURenderContextObserver>(observerBlock)(&ctx);
    }
}

bool preloadVst3Bundle(const std::string& utf8Path, bool isDir, std::string& error) {
    CFURLRef url = CFURLCreateFromFileSystemRepresentation(
        nullptr, reinterpret_cast<const UInt8*>(utf8Path.c_str()), static_cast<CFIndex>(utf8Path.size()), isDir);
    if (url == nullptr) return true;
    CFBundleRef bundle = CFBundleCreate(kCFAllocatorDefault, url);
    CFRelease(url);
    if (bundle == nullptr) return true;
    CFErrorRef err = nullptr;
    if (CFBundleLoadExecutableAndReturnError(bundle, &err)) return true;
    error = "the bundle's executable didn't load";
    if (err != nullptr) {
        if (CFStringRef why = CFErrorCopyFailureReason(err)) {
            error = cfStringToUtf8(why);
            CFRelease(why);
        }
        CFRelease(err);
    }
    return false;
}

bool preloadAudioUnitComponent(const std::string& componentId, std::string& error) {
    const size_t sep1 = componentId.rfind(':');
    const size_t sep2 = componentId.rfind('/');
    const size_t pos = (sep1 == std::string::npos) ? sep2 : (sep2 == std::string::npos ? sep1 : std::max(sep1, sep2));
    const std::string tail = (pos == std::string::npos) ? componentId : componentId.substr(pos + 1);

    std::vector<std::string> tokens;
    size_t start = 0;
    while (start < tail.size()) {
        const size_t comma = tail.find(',', start);
        const std::string part = (comma == std::string::npos) ? tail.substr(start) : tail.substr(start, comma - start);
        if (!part.empty()) tokens.push_back(part);
        if (comma == std::string::npos) break;
        start = comma + 1;
    }
    if (tokens.size() != 3) return true;

    AudioComponentDescription cd{};
    cd.componentType = fourCharOsType(tokens[0]);
    cd.componentSubType = fourCharOsType(tokens[1]);
    cd.componentManufacturer = fourCharOsType(tokens[2]);
    AudioComponent comp = AudioComponentFindNext(nullptr, &cd);
    if (comp == nullptr) return true;

    AudioComponentDescription found{};
    if (AudioComponentGetDescription(comp, &found) == noErr &&
        (found.componentFlags & kAudioComponentFlag_IsV3AudioUnit) != 0) {
        return true;
    }
    AudioComponentInstance inst = nullptr;
    const OSStatus st = AudioComponentInstanceNew(comp, &inst);
    if (st == noErr && inst != nullptr) {
        AudioComponentInstanceDispose(inst);
        return true;
    }
    error = "AudioComponentInstanceNew failed (" + std::to_string(static_cast<int>(st)) + ")";
    return false;
}

} // namespace nota::pluginhost::detail::mac

#endif // defined(__APPLE__)
