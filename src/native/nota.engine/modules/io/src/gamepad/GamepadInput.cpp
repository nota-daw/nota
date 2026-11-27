// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/gamepad/GamepadInput.h"

#include "gamepad/drivers/GamepadDrivers.h"
#include "nota/dsp/rt/SpscRingBuffer.h"
#include "nota/io/gamepad/IGamepadDriver.h"
#include "nota/io/gamepad/GamepadTypes.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstddef>
#include <memory>
#include <cstdint>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::gamepad {

namespace {
constexpr int32_t kMaxPads = 8;
} // namespace

struct GamepadInput::State final : public GamepadSink {
    State() { resetAxes(); }

    void resetAxes() {
        for (auto& padAxes : axes) {
            padAxes[static_cast<std::size_t>(GamepadAxis::LeftX)].store(64, std::memory_order_relaxed);
            padAxes[static_cast<std::size_t>(GamepadAxis::LeftY)].store(64, std::memory_order_relaxed);
            padAxes[static_cast<std::size_t>(GamepadAxis::RightX)].store(64, std::memory_order_relaxed);
            padAxes[static_cast<std::size_t>(GamepadAxis::RightY)].store(64, std::memory_order_relaxed);
            padAxes[static_cast<std::size_t>(GamepadAxis::LeftTrigger)].store(0, std::memory_order_relaxed);
            padAxes[static_cast<std::size_t>(GamepadAxis::RightTrigger)].store(0, std::memory_order_relaxed);
        }
    }

    void onPadsChanged(const std::vector<GamepadDeviceInfo>& nextPads) override {
        std::lock_guard<std::mutex> lock(padsMutex);
        pads.clear();
        const std::size_t count = std::min(nextPads.size(), static_cast<std::size_t>(kMaxPads));
        pads.assign(nextPads.begin(), nextPads.begin() + static_cast<std::ptrdiff_t>(count));
        if (pads.empty()) resetAxes();
    }

    void onButtonEdge(int32_t padIndex, int32_t buttonId, bool pressed) override {
        ButtonEvent ev{};
        ev.pressed = pressed ? 1 : 0;
        ev.buttonId = buttonId;
        ev.pad = padIndex;
        (void)queue.push(ev);
    }

    void onAxisValue(int32_t padIndex, int32_t axisIndex, int32_t value) override {
        if (padIndex < 0 || padIndex >= kMaxPads || axisIndex < 0 || axisIndex >= GamepadAxis::Count) return;
        axes[static_cast<std::size_t>(padIndex)][static_cast<std::size_t>(axisIndex)].store(
            value, std::memory_order_relaxed);
    }

    std::unique_ptr<IGamepadDriver> driver;
    mutable std::mutex padsMutex;
    std::vector<GamepadDeviceInfo> pads;
    mutable std::vector<std::string> snapshotUids;
    mutable std::vector<std::string> snapshotNames;
    std::array<std::array<std::atomic<int32_t>, GamepadAxis::Count>, kMaxPads> axes{};
    dsp::SpscRingBuffer<ButtonEvent, 256> queue;
    std::atomic<bool> started{false};
};

GamepadInput::GamepadInput() : GamepadInput(createBuiltinGamepadDriver()) {}

GamepadInput::GamepadInput(std::unique_ptr<IGamepadDriver> driver) : state_(std::make_unique<State>()) {
    state_->driver = std::move(driver);
}

GamepadInput::~GamepadInput() {
    stop();
}

void GamepadInput::start() {
    bool expected = false;
    if (!state_->started.compare_exchange_strong(expected, true)) return;
    if (state_->driver) { (void)state_->driver->start(*state_); }
}

void GamepadInput::stop() {
    bool expected = true;
    if (!state_->started.compare_exchange_strong(expected, false)) return;
    if (state_->driver) { state_->driver->stop(); }
    std::lock_guard<std::mutex> lock(state_->padsMutex);
    state_->pads.clear();
    state_->resetAxes();
}

int32_t GamepadInput::padCount() const {
    std::lock_guard<std::mutex> lock(state_->padsMutex);
    return static_cast<int32_t>(state_->pads.size());
}

void GamepadInput::padInfo(int32_t i, const char** outUid, const char** outName) const {
    if (!outUid || !outName) return;
    std::lock_guard<std::mutex> lock(state_->padsMutex);
    state_->snapshotUids.clear();
    state_->snapshotNames.clear();
    for (const auto& p : state_->pads) {
        state_->snapshotUids.push_back(p.uid);
        state_->snapshotNames.push_back(p.name);
    }
    if (i < 0 || i >= static_cast<int32_t>(state_->snapshotUids.size())) {
        *outUid = *outName = "";
        return;
    }
    *outUid = state_->snapshotUids[static_cast<std::size_t>(i)].c_str();
    *outName = state_->snapshotNames[static_cast<std::size_t>(i)].c_str();
}

int32_t GamepadInput::axisValues(int32_t i, int32_t* out, int32_t max) const {
    if (!out || max <= 0) return 0;
    std::lock_guard<std::mutex> lock(state_->padsMutex);
    if (i < 0 || i >= static_cast<int32_t>(state_->pads.size())) return 0;
    const int32_t n = std::min(max, GamepadAxis::Count);
    for (int32_t a = 0; a < n; ++a) {
        out[a] = state_->axes[static_cast<std::size_t>(i)][static_cast<std::size_t>(a)].load(std::memory_order_relaxed);
    }
    return n;
}

int32_t GamepadInput::pollEvents(ButtonEvent* out, int32_t max) {
    if (!out || max <= 0) return 0;
    int32_t n = 0;
    ButtonEvent ev{};
    while (n < max && state_->queue.pop(ev)) { out[n++] = ev; }
    return n;
}

std::unique_ptr<IGamepadDriver> createBuiltinGamepadDriver() {
    return detail::createPlatformGamepadDriver();
}

} // namespace nota::io::gamepad
