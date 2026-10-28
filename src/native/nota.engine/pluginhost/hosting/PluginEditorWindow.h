// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEditorWindow — native window hosting a plugin's editor + computer
// keyboard injection . Mirrors JUCE's AudioPluginHost idiom.
// Message thread only. macOS-only NSEvent monitor (native NSViews swallow JUCE
// key events there); on Windows JUCE key events reach the editor normally.

#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <vector>

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
    // Set by the owning adapter (message thread) before/at open; called from the
    // key monitor to inject computer-keyboard MIDI into THIS plugin.
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)> onNoteOff;

    // Called while the window is still on screen, right before its close button hides it,
    // so the owner can capture it (specs/046).
    std::function<void()> onHide;

    explicit PluginEditorWindow(juce::AudioPluginInstance& p);
    ~PluginEditorWindow() override;
    void closeButtonPressed() override {
        if (onHide) onHide();
        setVisible(false);
    }

  private:
    void installKeyMonitor();
    void removeKeyMonitor();

#if JUCE_MAC
    void* keyMonitor_ = nullptr; // NSEvent monitor (opaque here; AppKit in the .cpp)
#endif
};

// Owns (at most one) editor window for a plugin; open() lazily creates it and
// re-shows on subsequent calls. The adapter wires onNoteOn/onNoteOff so the
// window's computer keyboard reaches its plugin, and onPreview to keep the picture
// of the window taken each time it is hidden (specs/046) — not called when the
// capture fails or comes back blank.
struct EditorHost {
    std::unique_ptr<PluginEditorWindow> window;
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)> onNoteOff;
    std::function<void(std::vector<uint8_t>)> onPreview;

    void open(juce::AudioPluginInstance& p);
    void close();
    bool isOpen() const { return window != nullptr && window->isVisible(); }

  private:
    void snapshot();
};

} // namespace hosting
} // namespace nota
