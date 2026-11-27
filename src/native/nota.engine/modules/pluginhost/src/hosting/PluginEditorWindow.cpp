// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/PluginEditorWindow.h"

#include "hosting/EditorSnapshot.h"
#include "hosting/MacBridges.h"
#include "nota/core/EditorKeys.h"
#include <utility>
#include <memory>
#include <cstdint>
#include <atomic>

namespace nota::pluginhost::detail {
namespace {

#if !defined(__APPLE__)
int32_t editorKeyCode(const juce::KeyPress& key) {
    const int code = key.getKeyCode();
    static const int fkeys[] = {juce::KeyPress::F1Key, juce::KeyPress::F2Key, juce::KeyPress::F3Key,
        juce::KeyPress::F4Key, juce::KeyPress::F5Key, juce::KeyPress::F6Key, juce::KeyPress::F7Key,
        juce::KeyPress::F8Key, juce::KeyPress::F9Key, juce::KeyPress::F10Key, juce::KeyPress::F11Key,
        juce::KeyPress::F12Key, juce::KeyPress::F13Key, juce::KeyPress::F14Key, juce::KeyPress::F15Key,
        juce::KeyPress::F16Key, juce::KeyPress::F17Key, juce::KeyPress::F18Key, juce::KeyPress::F19Key,
        juce::KeyPress::F20Key, juce::KeyPress::F21Key, juce::KeyPress::F22Key, juce::KeyPress::F23Key,
        juce::KeyPress::F24Key};
    for (int i = 0; i < 24; ++i) {
        if (code == fkeys[i]) return 0xF704 + i;
    }
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
#endif

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
    juce::Process::makeForegroundProcess();
#if defined(__APPLE__)
    mac::activateApplication();
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

bool PluginEditorWindow::keyPressed(const juce::KeyPress& key) {
#if defined(__APPLE__)
    (void)key;
    return false;
#else
    const auto mods = key.getModifiers();
    if (mods.isCommandDown() || mods.isCtrlDown() || mods.isAltDown()) return false;
    const int32_t ch = editorKeyCode(key);
    if (!core::editorkeys::claims(ch)) return false;
    if (held_.count(key.getKeyCode()) == 0) {
        held_[key.getKeyCode()] = core::editorkeys::tokenOf(ch);
        core::editorkeys::push(ch, true);
    }
    return true;
#endif
}

bool PluginEditorWindow::keyStateChanged(bool isKeyDown) {
#if !defined(__APPLE__)
    if (!isKeyDown) {
        for (auto it = held_.begin(); it != held_.end();) {
            if (juce::KeyPress::isKeyCurrentlyDown(it->first)) {
                ++it;
                continue;
            }
            core::editorkeys::push(it->second, false);
            it = held_.erase(it);
        }
    }
#else
    (void)isKeyDown;
#endif
    return false;
}

void PluginEditorWindow::releaseHeldKeys() {
    for (const auto& [code, ch] : held_) core::editorkeys::push(ch, false);
    held_.clear();
}

void PluginEditorWindow::installKeyMonitor() {
#if defined(__APPLE__)
    keyMonitor_ = mac::installEditorKeyMonitor(
        getWindowHandle(), [this](int32_t ch, int code, bool isKeyDown, bool hasMods) -> bool {
            if (!isKeyDown) {
                auto it = held_.find(code);
                if (it == held_.end()) return false;
                core::editorkeys::push(it->second, false);
                held_.erase(it);
                return true;
            }
            if (hasMods) return false;
            if (held_.count(code) != 0) return true;
            if (!core::editorkeys::claims(ch)) return false;
            held_[code] = core::editorkeys::tokenOf(ch);
            core::editorkeys::push(ch, true);
            return true;
        });
#endif
}

void PluginEditorWindow::removeKeyMonitor() {
#if defined(__APPLE__)
    if (keyMonitor_ != nullptr) {
        mac::removeEditorKeyMonitor(keyMonitor_);
        keyMonitor_ = nullptr;
    }
#endif
}

EditorHost::~EditorHost() {
    applyOpenState(false);
}

void EditorHost::applyOpenState(bool nowOpen) {
    const bool prev = open_.exchange(nowOpen, std::memory_order_acq_rel);
    if (prev != nowOpen && onOpenChanged) onOpenChanged(nowOpen);
}

void EditorHost::open(juce::AudioPluginInstance& p) {
    if (juce::MessageManager::getInstanceWithoutCreating() == nullptr) return;
    if (window == nullptr) {
        window = std::make_unique<PluginEditorWindow>(p);
        window->onNoteOn = onNoteOn;
        window->onNoteOff = onNoteOff;
        window->onHide = [this] {
            snapshot();
            applyOpenState(false);
        };
    } else {
        window->setVisible(true);
        window->toFront(true);
    }
    applyOpenState(window != nullptr && window->isVisible());
}

void EditorHost::close() {
    if (window != nullptr && window->isVisible()) {
        snapshot();
        applyOpenState(false);
        window->setVisible(false);
    } else {
        applyOpenState(false);
    }
}

void EditorHost::snapshot() {
    if (!window || !onPreview) return;
    auto jpeg = snapshotEditor(*window);
    if (!jpeg.empty()) onPreview(std::move(jpeg));
}

} // namespace nota::pluginhost::detail
