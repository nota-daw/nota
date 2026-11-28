// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// macOS Objective-C++ bridges for PluginEditorWindow: app activation & local NSEvent keyboard monitor.

#if defined(__APPLE__)

#include "hosting/MacBridges.h"

#import <AppKit/AppKit.h>

#include <memory>
#include <utility>

namespace nota::pluginhost::detail::mac {
namespace {

bool takesTyping(NSResponder* responder) {
    if (responder == nil) return false;
    if ([responder isKindOfClass:[NSText class]]) return true;
    if ([responder conformsToProtocol:@protocol(NSTextInputClient)]) {
        return [(id<NSTextInputClient>)responder selectedRange].location != NSNotFound;
    }
    return false;
}

struct MonitorHolder {
    id monitor = nil;
    EditorKeyCallback callback;

    explicit MonitorHolder(EditorKeyCallback cb) : callback(std::move(cb)) {}
    ~MonitorHolder() {
        if (monitor != nil) {
            [NSEvent removeMonitor:monitor];
            [monitor release];
            monitor = nil;
        }
    }
};

} // namespace

void activateApplication() {
    [NSApp activateIgnoringOtherApps:YES];
}

ScopedEditorKeyMonitor installEditorKeyMonitor(void* nsViewHandle, EditorKeyCallback callback) {
    auto holder = std::make_shared<MonitorHolder>(std::move(callback));
    MonitorHolder* raw = holder.get();
    raw->monitor = [[NSEvent
        addLocalMonitorForEventsMatchingMask:(NSEventMaskKeyDown | NSEventMaskKeyUp)
                                     handler:^NSEvent*(NSEvent* ev) {
                                       if (nsViewHandle == nullptr) return ev;
                                       auto* view = static_cast<NSView*>(nsViewHandle);
                                       if (ev.window != view.window) return ev;

                                       NSString* chars = ev.charactersIgnoringModifiers;
                                       const int32_t ch =
                                           chars.length > 0 ? static_cast<int32_t>([chars characterAtIndex:0]) : 0;
                                       const int code = static_cast<int>(ev.keyCode);
                                       const bool isKeyDown = (ev.type == NSEventTypeKeyDown);
                                       const bool hasMods = (ev.modifierFlags & (NSEventModifierFlagCommand |
                                                                                    NSEventModifierFlagControl |
                                                                                    NSEventModifierFlagOption)) != 0;

                                       if (isKeyDown && !hasMods && takesTyping(ev.window.firstResponder)) {
                                           return ev;
                                       }
                                       const bool consumed =
                                           raw->callback && raw->callback(ch, code, isKeyDown, hasMods);
                                       return consumed ? nil : ev;
                                     }] retain];
    return holder;
}

} // namespace nota::pluginhost::detail::mac

#endif // defined(__APPLE__)
