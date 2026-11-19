// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEditorWindow implementation. The NSEvent monitor handle is void* in the header
// so adapter TUs can stay plain C++ (this TU compiles as ObjC++ on macOS).

#include "PluginEditorWindow.h"

#include "EditorSnapshot.h"
#include "engine/EditorKeys.h"

#if JUCE_MAC
#import <AppKit/AppKit.h> // NSEvent monitor for computer-keyboard MIDI
#endif

namespace nota {
namespace hosting {

#if !JUCE_MAC
namespace {
// A JUCE key as the code the app names it by (Nota.Application.Input.EditorKeyCodes): its
// character, or for a named key the number AppKit gives it, as the macOS window reads them.
int32_t editorKeyCode(const juce::KeyPress& key) {
    const int code = key.getKeyCode();
    static const int fkeys[] = {juce::KeyPress::F1Key, juce::KeyPress::F2Key, juce::KeyPress::F3Key,
        juce::KeyPress::F4Key, juce::KeyPress::F5Key, juce::KeyPress::F6Key, juce::KeyPress::F7Key,
        juce::KeyPress::F8Key, juce::KeyPress::F9Key, juce::KeyPress::F10Key, juce::KeyPress::F11Key,
        juce::KeyPress::F12Key, juce::KeyPress::F13Key, juce::KeyPress::F14Key, juce::KeyPress::F15Key,
        juce::KeyPress::F16Key, juce::KeyPress::F17Key, juce::KeyPress::F18Key, juce::KeyPress::F19Key,
        juce::KeyPress::F20Key, juce::KeyPress::F21Key, juce::KeyPress::F22Key, juce::KeyPress::F23Key,
        juce::KeyPress::F24Key};
    for (int i = 0; i < 24; ++i)
        if (code == fkeys[i]) return 0xF704 + i;
    if (code == juce::KeyPress::upKey) return 0xF700;
    if (code == juce::KeyPress::downKey) return 0xF701;
    if (code == juce::KeyPress::leftKey) return 0xF702;
    if (code == juce::KeyPress::rightKey) return 0xF703;
    if (code == juce::KeyPress::homeKey) return 0xF729;
    if (code == juce::KeyPress::endKey) return 0xF72B;
    if (code == juce::KeyPress::pageUpKey) return 0xF72C;
    if (code == juce::KeyPress::pageDownKey) return 0xF72D;
    if (code == juce::KeyPress::deleteKey) return 0xF728;
    if (code == juce::KeyPress::backspaceKey) return 0x7F;
    if (code == juce::KeyPress::returnKey) return '\r';
    if (code == juce::KeyPress::escapeKey) return 0x1B;
    if (code == juce::KeyPress::tabKey) return '\t';
    if (code == juce::KeyPress::spaceKey) return ' ';
    const auto ch = key.getTextCharacter();
    return ch > 0x20 && ch != 0x7F ? static_cast<int32_t>(ch) : 0;
}
} // namespace
#endif

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
    releaseHeldKeys();
    clearContentComponent();
}

// Windows / Linux: a key the editor left unhandled bubbles up to the window. One the app
// plays with goes to the app (once per press: auto-repeat stays here); anything else, and
// every chord, stays with the plug-in.
bool PluginEditorWindow::keyPressed(const juce::KeyPress& key) {
#if JUCE_MAC
    (void)key;
    return false; // the NSEvent monitor has the keys on macOS
#else
    const auto mods = key.getModifiers();
    if (mods.isCommandDown() || mods.isCtrlDown() || mods.isAltDown()) return false;
    const int32_t ch = editorKeyCode(key);
    if (!editorkeys::claims(ch)) return false;
    if (held_.count(key.getKeyCode()) == 0) {
        held_[key.getKeyCode()] = editorkeys::tokenOf(ch);
        editorkeys::push(ch, true);
    }
    return true;
#endif
}

// JUCE reports releases only as a change of key state: whichever held key is no longer down
// was released.
bool PluginEditorWindow::keyStateChanged(bool isKeyDown) {
#if !JUCE_MAC
    if (!isKeyDown)
        for (auto it = held_.begin(); it != held_.end();) {
            if (juce::KeyPress::isKeyCurrentlyDown(it->first)) {
                ++it;
                continue;
            }
            editorkeys::push(it->second, false);
            it = held_.erase(it);
        }
#else
    (void)isKeyDown;
#endif
    return false;
}

void PluginEditorWindow::releaseHeldKeys() {
    for (const auto& [code, ch] : held_) editorkeys::push(ch, false);
    held_.clear();
}

void PluginEditorWindow::installKeyMonitor() {
#if JUCE_MAC
    // The keys the app plays with (its note map, octave and velocity keys, as bound) go to
    // the app, which plays them as it does over its own window (#221). A chord, a key the app
    // doesn't claim, and anything typed into one of the plug-in's text fields stay with the
    // plug-in. A held key is remembered, so its release reaches the app even if the
    // plug-in took the keyboard in between.
    keyMonitor_ = [[NSEvent
        addLocalMonitorForEventsMatchingMask:(NSEventMaskKeyDown | NSEventMaskKeyUp)
                                     handler:^NSEvent*(NSEvent* ev) {
                                       void* handle = this->getWindowHandle();
                                       if (handle == nullptr || ev.window != ((NSView*)handle).window)
                                           return ev; // not our window — leave the event alone
                                       NSString* chars = ev.charactersIgnoringModifiers;
                                       const int32_t ch = chars.length > 0 ? (int32_t)[chars characterAtIndex:0] : 0;
                                       const int code = (int)ev.keyCode;
                                       if (ev.type == NSEventTypeKeyUp) {
                                           auto it = held_.find(code);
                                           if (it == held_.end()) return ev;
                                           editorkeys::push(it->second, false);
                                           held_.erase(it);
                                           return nil;
                                       }
                                       if (ev.modifierFlags & (NSEventModifierFlagCommand | NSEventModifierFlagControl |
                                                                  NSEventModifierFlagOption))
                                           return ev; // a shortcut chord, not a note
                                       if ([ev.window.firstResponder isKindOfClass:[NSText class]])
                                           return ev; // typing into the plug-in's own field
                                       if (held_.count(code) != 0) return nil; // auto-repeat of a key the app has
                                       if (!editorkeys::claims(ch)) return ev;
                                       held_[code] = editorkeys::tokenOf(ch);
                                       editorkeys::push(ch, true);
                                       return nil; // consumed: no system beep, no double handling
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
        window->onHide = [this] { snapshot(); };
    } else {
        window->setVisible(true);
        window->toFront(true);
    }
}

void EditorHost::close() {
    if (!isOpen()) return;
    snapshot(); // while it is still on screen
    window->setVisible(false);
}

void EditorHost::snapshot() {
    if (!window || !onPreview) return;
    auto jpeg = snapshotEditor(*window);
    if (!jpeg.empty()) onPreview(std::move(jpeg));
}

} // namespace hosting
} // namespace nota
