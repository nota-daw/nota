// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEditorWindow — native window hosting a plugin's editor + computer
// keyboard audition. Mirrors JUCE's AudioPluginHost idiom: the typing keys
// play THIS visible plugin, everything else (Space/Return included) belongs
// to the plugin and passes through untouched.
// Message thread only. Two complementary key paths: on macOS an NSEvent
// monitor (native NSViews swallow JUCE key events there), everywhere a JUCE
// key listener (covers the generic editor and JUCE-based editors; the monitor
// consumes note keys first, so the two never double-trigger).

#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <unordered_map>

#include "engine/CommandQueue.h" // SpscRingBuffer: message-thread -> audio-thread notes

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

namespace nota {
namespace hosting {

// Message-thread producer -> audio-thread consumer. The editor window's computer
// keyboard pushes note events here; the plugin adapter drains them in its audio
// callback (keeps the plugin's own MidiBuffer audio-thread-only — no data race).
struct GuiNote {
    bool on;
    int32_t pitch;
    float velocity;
};
using GuiNoteQueue = nota::SpscRingBuffer<GuiNote, 256>;

// A separate native window hosting a plugin's editor. Falls back to a generic
// editor when the plugin provides none.
class PluginEditorWindow : public juce::DocumentWindow {
  public:
    using NoteOnFn = std::function<void(int32_t, float)>;
    using NoteOffFn = std::function<void(int32_t)>;

    // The sink is taken up front so the key monitor (installed in the
    // constructor) never observes a half-wired window. Effects pass empty
    // sinks: note keys then fall through to the plugin like any other key.
    explicit PluginEditorWindow(juce::AudioPluginInstance& p, NoteOnFn noteOn, NoteOffFn noteOff);
    ~PluginEditorWindow() override;

    // Re-points the sink (used when re-showing an existing window).
    void setNoteSink(NoteOnFn noteOn, NoteOffFn noteOff);

    // Releases every still-held audition note at its original pitch. Called on
    // hide/close so a key-up lost to the close can never hang a voice. Only
    // releases editor-injected notes — live notes from the main window belong
    // to the engine and keep ringing.
    void releaseHeldNotes();

    void closeButtonPressed() override;
    bool keyPressed(const juce::KeyPress& key) override;
    bool keyStateChanged(bool isKeyDown) override;

  private:
    void installKeyMonitor();
    void removeKeyMonitor();

    bool hasNoteSink() const { return static_cast<bool>(onNoteOn_) || static_cast<bool>(onNoteOff_); }
    void noteOn(std::unordered_map<int, int32_t>& held, int keyId, int32_t basePitch);
    void noteOff(std::unordered_map<int, int32_t>& held, int keyId);

    NoteOnFn onNoteOn_;
    NoteOffFn onNoteOff_;
    std::unordered_map<int, int32_t> heldMac_;  // macOS keycode -> resolved pitch at note-on time
    std::unordered_map<int, int32_t> heldJuce_; // JUCE key code -> resolved pitch at note-on time

#if JUCE_MAC
    void* keyMonitor_ = nullptr; // NSEvent monitor (opaque here; AppKit in the .cpp)
#endif
};

// Owns (at most one) editor window for a plugin; open() lazily creates it and
// re-shows on subsequent calls. The adapter wires onNoteOn/onNoteOff so the
// window's computer keyboard reaches its plugin.
struct EditorHost {
    std::unique_ptr<PluginEditorWindow> window;
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)> onNoteOff;

    void open(juce::AudioPluginInstance& p);
    void close();
    bool isOpen() const { return window != nullptr && window->isVisible(); }
};

} // namespace hosting
} // namespace nota
