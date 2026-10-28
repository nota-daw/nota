// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEditorWindow implementation. The NSEvent monitor handle is void* in the header
// so adapter TUs can stay plain C++ (this TU compiles as ObjC++ on macOS).

#include "PluginEditorWindow.h"

#include "TypingKeyboard.h"

#if JUCE_MAC
#import <AppKit/AppKit.h> // NSEvent monitor for computer-keyboard MIDI
#endif

namespace nota {
namespace hosting {

PluginEditorWindow::PluginEditorWindow(juce::AudioPluginInstance& p, NoteOnFn noteOn, NoteOffFn noteOff)
    : juce::DocumentWindow(p.getName().isNotEmpty() ? p.getName() : juce::String("Plugin"), juce::Colours::black,
          juce::DocumentWindow::minimiseButton | juce::DocumentWindow::closeButton),
      onNoteOn_(std::move(noteOn)), onNoteOff_(std::move(noteOff)) {
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
    releaseHeldNotes();
    clearContentComponent();
}

void PluginEditorWindow::setNoteSink(NoteOnFn noteOn, NoteOffFn noteOff) {
    onNoteOn_ = std::move(noteOn);
    onNoteOff_ = std::move(noteOff);
}

void PluginEditorWindow::releaseHeldNotes() {
    for (const auto& [keyId, pitch] : heldMac_) {
        (void)keyId;
        if (onNoteOff_) onNoteOff_(pitch);
    }
    heldMac_.clear();
    for (const auto& [keyId, pitch] : heldJuce_) {
        (void)keyId;
        if (onNoteOff_) onNoteOff_(pitch);
    }
    heldJuce_.clear();
}

void PluginEditorWindow::closeButtonPressed() {
    releaseHeldNotes();
    setVisible(false);
}

void PluginEditorWindow::noteOn(std::unordered_map<int, int32_t>& held, int keyId, int32_t basePitch) {
    // Resolve at press time and remember the exact pitch: the octave may shift
    // (Z/X in the main window) before the key comes back up.
    const int32_t pitch = typingKeyboardResolvePitch(basePitch);
    held[keyId] = pitch;
    if (onNoteOn_) onNoteOn_(pitch, typingKeyboardVelocity01());
}

void PluginEditorWindow::noteOff(std::unordered_map<int, int32_t>& held, int keyId) {
    const auto it = held.find(keyId);
    if (it == held.end()) return;
    if (onNoteOff_) onNoteOff_(it->second);
    held.erase(it);
}

// JUCE fallback path (all platforms): catches the generic editor and JUCE-based
// editors. Native plugin views swallow keys before JUCE sees them — that is what
// the macOS monitor above is for; it consumes note keys first (return nil), so
// the two paths never fire for the same press.
bool PluginEditorWindow::keyPressed(const juce::KeyPress& key) {
    if (!hasNoteSink()) return false;
    if (key.getModifiers().isCommandDown() || key.getModifiers().isCtrlDown() || key.getModifiers().isAltDown())
        return false; // shortcut chord, not a note
    const int32_t base = typingKeyboardBasePitchForCharacter(key.getTextCharacter());
    if (base < 0) return false; // Space/Return and the rest belong to the plugin
    const int keyId = key.getKeyCode();
    if (heldJuce_.find(keyId) != heldJuce_.end()) return true; // auto-repeat: single voice
    noteOn(heldJuce_, keyId, base);
    return true;
}

bool PluginEditorWindow::keyStateChanged(bool /*isKeyDown*/) {
    // JUCE reports no per-key release here, so retire every held key the peer no
    // longer sees down. (Text editors consume their keys before they reach the
    // window, so typing in a preset search never starts a note this way.)
    bool releasedAny = false;
    for (auto it = heldJuce_.begin(); it != heldJuce_.end();) {
        if (juce::KeyPress::isKeyCurrentlyDown(it->first)) {
            ++it;
            continue;
        }
        if (onNoteOff_) onNoteOff_(it->second);
        it = heldJuce_.erase(it);
        releasedAny = true;
    }
    return releasedAny;
}

#if JUCE_MAC
namespace {

// True while the user is typing into a text field inside the editor window
// (preset search, value entry): those keystrokes belong to the plugin.
bool isTextEditingFirstResponder(NSWindow* window) {
    if (window == nullptr) return false;
    const id firstResponder = [window firstResponder];
    if (firstResponder == nil) return false;
    return [firstResponder isKindOfClass:[NSTextView class]] || [firstResponder isKindOfClass:[NSTextField class]];
}

} // namespace
#endif

void PluginEditorWindow::installKeyMonitor() {
#if JUCE_MAC
    // Captured raw: the block outlives nothing — it is removed in the destructor,
    // which runs on the message thread like every event it sees.
    PluginEditorWindow* self = this;
    keyMonitor_ = [[NSEvent
        addLocalMonitorForEventsMatchingMask:(NSEventMaskKeyDown | NSEventMaskKeyUp)
                                     handler:^NSEvent*(NSEvent* ev) {
                                       void* handle = self->getWindowHandle();
                                       if (handle == nullptr || ev.window != ((NSView*)handle).window)
                                           return ev; // not our window — leave the event alone
                                       if (ev.modifierFlags & (NSEventModifierFlagCommand | NSEventModifierFlagControl |
                                                                  NSEventModifierFlagOption))
                                           return ev;                       // shortcut chord, not a note
                                       if (!self->hasNoteSink()) return ev; // no note sink (e.g. effect plugin)
                                       if (isTextEditingFirstResponder(ev.window)) return ev; // typing text, not notes
                                       const int32_t base = typingKeyboardBasePitchForMacKeyCode(ev.keyCode);
                                       if (base < 0) return ev; // Space/Return and the rest belong to the plugin
                                       const int keyId = static_cast<int>(ev.keyCode);
                                       if (ev.type == NSEventTypeKeyDown) {
                                           if (!ev.isARepeat) self->noteOn(self->heldMac_, keyId, base);
                                       } else {
                                           self->noteOff(self->heldMac_, keyId);
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
        window = std::make_unique<PluginEditorWindow>(p, onNoteOn, onNoteOff);
    } else {
        // Re-point the sink every time: the adapter may have been re-created
        // (clone) while the window survived, and a stale sink would play dead.
        window->setNoteSink(onNoteOn, onNoteOff);
        window->setVisible(true);
        window->toFront(true);
    }
}

void EditorHost::close() {
    if (window) {
        window->releaseHeldNotes();
        window->setVisible(false);
    }
}

} // namespace hosting
} // namespace nota
