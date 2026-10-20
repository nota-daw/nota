// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEditorWindow implementation. The NSEvent monitor handle is void* in the header
// so adapter TUs can stay plain C++ (this TU compiles as ObjC++ on macOS).

#include "PluginEditorWindow.h"

#if JUCE_MAC
#import <AppKit/AppKit.h> // NSEvent monitor for computer-keyboard MIDI
#endif

namespace nota {
namespace hosting {
namespace {

// Typing-keyboard row -> MIDI pitch, matching the app's own
// KeyToPitch map (W = C#4 = 61 … K = C5 = 72). Keyed by macOS ANSI virtual
// keycodes so it works regardless of which control has JUCE key focus. -1 = not
// a note key.
int keycodeToPitch(unsigned short kc) {
    switch (kc) {
    case 0x0D: return 61; // W
    case 0x01: return 62; // S
    case 0x0E: return 63; // E
    case 0x02: return 64; // D
    case 0x03: return 65; // F
    case 0x11: return 66; // T
    case 0x05: return 67; // G
    case 0x10: return 68; // Y
    case 0x04: return 69; // H
    case 0x20: return 70; // U
    case 0x26: return 71; // J
    case 0x28: return 72; // K
    default: return -1;
    }
}

} // namespace

PluginEditorWindow::PluginEditorWindow(juce::AudioPluginInstance& p)
    : juce::DocumentWindow(p.getName().isNotEmpty() ? p.getName() : juce::String("Plugin"), juce::Colours::black,
          juce::DocumentWindow::minimiseButton | juce::DocumentWindow::closeButton) {
    setUsingNativeTitleBar(true);
    setSize(400, 300);
    juce::AudioProcessorEditor* ui = nullptr;
    if (p.hasEditor()) ui = p.createEditorAndMakeActive();
    if (ui == nullptr) ui = new juce::GenericAudioProcessorEditor(p);
    setContentOwned(ui, true);
    setResizable(ui->isResizable(), false);
    setTopLeftPosition(80, 80);
    // Float above the Nota main window and bring it to the foreground so the
    // user doesn't have to hunt for it behind the app (req: always on top).
    // Under `dotnet run` (no .app bundle) the process starts as a non-foreground
    // app, so a fresh native window opens *behind* Avalonia and never takes key
    // focus — promote the process and activate the app first, then order front.
    juce::Process::makeForegroundProcess();
#if JUCE_MAC
    [NSApp activateIgnoringOtherApps:YES];
#endif
    setAlwaysOnTop(true);
    setVisible(true);
    toFront(true);
    installKeyMonitor();
}

PluginEditorWindow::~PluginEditorWindow() {
    removeKeyMonitor();
    clearContentComponent();
}

void PluginEditorWindow::installKeyMonitor() {
#if JUCE_MAC
    keyMonitor_ = [[NSEvent
        addLocalMonitorForEventsMatchingMask:(NSEventMaskKeyDown | NSEventMaskKeyUp)
                                     handler:^NSEvent*(NSEvent* ev) {
                                       void* handle = this->getWindowHandle();
                                       if (handle == nullptr || ev.window != ((NSView*)handle).window)
                                           return ev; // not our window — leave the event alone
                                       if (ev.modifierFlags & (NSEventModifierFlagCommand | NSEventModifierFlagControl |
                                                                  NSEventModifierFlagOption))
                                           return ev;                          // shortcut chord, not a note
                                       if (!onNoteOn && !onNoteOff) return ev; // no note sink (e.g. effect plugin)
                                       const int pitch = keycodeToPitch(ev.keyCode);
                                       if (pitch < 0) return ev;
                                       if (ev.type == NSEventTypeKeyDown) {
                                           if (!ev.isARepeat && onNoteOn) onNoteOn(pitch, 0.85f);
                                       } else if (onNoteOff) {
                                           onNoteOff(pitch);
                                       }
                                       return nil; // consume the note key (no system beep, no double-handle)
                                     }] retain];
#endif
}

void PluginEditorWindow::removeKeyMonitor() {
#if JUCE_MAC
    if (keyMonitor_ != nullptr) {
        id monitor = static_cast<id>(keyMonitor_);
        [NSEvent removeMonitor:monitor];
        [monitor release];
        keyMonitor_ = nullptr;
    }
#endif
}

void EditorHost::open(juce::AudioPluginInstance& p) {
    if (juce::MessageManager::getInstanceWithoutCreating() == nullptr) return;
    if (window == nullptr) {
        window = std::make_unique<PluginEditorWindow>(p);
        window->onNoteOn = onNoteOn;
        window->onNoteOff = onNoteOff;
    } else {
        window->setVisible(true);
        window->toFront(true);
    }
}

void EditorHost::close() {
    if (window) window->setVisible(false);
}

} // namespace hosting
} // namespace nota
