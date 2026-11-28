// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/dsp/rt/SpscRingBuffer.h"

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#include <atomic>
#include <cstdint>
#include <functional>
#include <map>
#include <memory>
#include <vector>

namespace nota::pluginhost::detail {

struct GuiNote {
    bool on = false;
    int32_t pitch = 0;
    float velocity = 0.0f;
};
using GuiNoteQueue = dsp::SpscRingBuffer<GuiNote, 256>;

class PluginEditorWindow : public juce::DocumentWindow {
  public:
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)> onNoteOff;
    std::function<void()> onHide;

    explicit PluginEditorWindow(juce::AudioPluginInstance& p);
    ~PluginEditorWindow() override;

    void closeButtonPressed() override {
        if (onHide) onHide();
        releaseHeldKeys();
        setVisible(false);
    }

    bool keyPressed(const juce::KeyPress& key) override;
    bool keyStateChanged(bool isKeyDown) override;

    void activeWindowStatusChanged() override {
        juce::DocumentWindow::activeWindowStatusChanged();
        if (!isActiveWindow()) releaseHeldKeys();
    }

  private:
    void installKeyMonitor();
    void removeKeyMonitor();
    void releaseHeldKeys();

    std::map<int, int32_t> held_;
    std::shared_ptr<void> keyMonitor_;
    std::unique_ptr<juce::GenericAudioProcessorEditor> genericEditor_;
};

struct EditorHost {
    ~EditorHost();

    std::unique_ptr<PluginEditorWindow> window;
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)> onNoteOff;
    std::function<void(std::vector<uint8_t>)> onPreview;
    std::function<void(bool)> onOpenChanged;

    void open(juce::AudioPluginInstance& p);
    void close();
    bool isOpen() const noexcept { return open_.load(std::memory_order_acquire); }
    void setSimulatedOpenForTest(bool open) { applyOpenState(open); }

  private:
    void applyOpenState(bool nowOpen);
    void snapshot();
    std::atomic<bool> open_{false};
};

} // namespace nota::pluginhost::detail
