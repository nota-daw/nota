// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if defined(__APPLE__)

#include "gamepad/drivers/GamepadDrivers.h"

#import <Foundation/Foundation.h>
#import <GameController/GameController.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace nota::io::gamepad {
namespace {

enum : int32_t { kBtnA = 1, kBtnB, kBtnX, kBtnY, kL1, kR1, kL2, kR2 };
constexpr float kStickDeadzone = 0.10f;

int32_t quantizeStick(float v) {
    v = std::clamp(v, -1.0f, 1.0f);
    float a = std::fabs(v);
    a = a <= kStickDeadzone ? 0.0f : (a - kStickDeadzone) / (1.0f - kStickDeadzone);
    if (v < 0.0f) a = -a;
    return static_cast<int32_t>(std::lround(a * 63.5f + 63.5f));
}

int32_t quantizeTrigger(float v) {
    return static_cast<int32_t>(std::lround(std::clamp(v, 0.0f, 1.0f) * 127.0f));
}

struct Pad {
    GCController* controller = nil;
    std::string uid;
    std::string name;
    std::array<bool, 9> lastBtn{};
    std::array<bool, 4> lastDpad{};
};

class GameControllerDriver final : public IGamepadDriver {
  public:
    GameControllerDriver() = default;
    ~GameControllerDriver() override { stop(); }

    std::string_view id() const noexcept override { return "gamecontroller"; }
    std::string_view displayName() const noexcept override { return "Apple GameController"; }

    bool start(GamepadSink& sink) override {
        bool expected = false;
        if (!running_.compare_exchange_strong(expected, true)) return true;
        sink_ = &sink;
        handlerQueue_ = dispatch_queue_create("app.nota.gamepad", DISPATCH_QUEUE_SERIAL);

        NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
        connectObs_ = [[nc addObserverForName:GCControllerDidConnectNotification
                                       object:nil
                                        queue:nil
                                   usingBlock:^(NSNotification* n) { addController(n.object); }] retain];
        disconnectObs_ = [[nc addObserverForName:GCControllerDidDisconnectNotification
                                          object:nil
                                           queue:nil
                                      usingBlock:^(NSNotification* n) { removeController(n.object); }] retain];
        for (GCController* c in [GCController controllers]) addController(c);
        [GCController startWirelessControllerDiscoveryWithCompletionHandler:^{}];
        return true;
    }

    void stop() override {
        bool expected = true;
        if (!running_.compare_exchange_strong(expected, false)) return;
        [GCController stopWirelessControllerDiscovery];
        NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
        if (connectObs_) {
            [nc removeObserver:connectObs_];
            [connectObs_ release];
            connectObs_ = nil;
        }
        if (disconnectObs_) {
            [nc removeObserver:disconnectObs_];
            [disconnectObs_ release];
            disconnectObs_ = nil;
        }
        {
            std::lock_guard<std::mutex> lock(padsMutex_);
            for (auto& up : pads_) {
                up->controller.extendedGamepad.valueChangedHandler = nil;
                [up->controller release];
            }
            pads_.clear();
        }
        if (sink_) sink_->onPadsChanged({});
        if (handlerQueue_) {
            dispatch_release(handlerQueue_);
            handlerQueue_ = nullptr;
        }
        sink_ = nullptr;
    }

  private:
    void notifyPadsLocked() {
        if (!sink_) return;
        std::vector<GamepadDeviceInfo> infos;
        infos.reserve(pads_.size());
        for (const auto& p : pads_) infos.push_back(GamepadDeviceInfo{p->uid, p->name});
        sink_->onPadsChanged(infos);
    }

    void syncState(Pad* p) {
        if (!running_.load(std::memory_order_acquire) || !sink_) return;
        std::lock_guard<std::mutex> lock(padsMutex_);
        int32_t padIndex = -1;
        for (std::size_t i = 0; i < pads_.size(); ++i) {
            if (pads_[i].get() == p) {
                padIndex = static_cast<int32_t>(i);
                break;
            }
        }
        if (padIndex < 0) return;

        GCExtendedGamepad* g = p->controller.extendedGamepad;
        if (!g) return;

        auto edge = [&](std::size_t slot, int32_t buttonId, bool pressed) {
            if (pressed != p->lastBtn[slot]) {
                p->lastBtn[slot] = pressed;
                sink_->onButtonEdge(padIndex, buttonId, pressed);
            }
        };
        edge(kBtnA, kBtnA, g.buttonA.isPressed);
        edge(kBtnB, kBtnB, g.buttonB.isPressed);
        edge(kBtnX, kBtnX, g.buttonX.isPressed);
        edge(kBtnY, kBtnY, g.buttonY.isPressed);
        edge(kL1, kL1, g.leftShoulder.isPressed);
        edge(kR1, kR1, g.rightShoulder.isPressed);
        edge(kL2, kL2, g.leftTrigger.isPressed);
        edge(kR2, kR2, g.rightTrigger.isPressed);

        auto dpad = [&](std::size_t i, int32_t buttonId, bool pressed) {
            if (pressed != p->lastDpad[i]) {
                p->lastDpad[i] = pressed;
                sink_->onButtonEdge(padIndex, buttonId, pressed);
            }
        };
        dpad(0, GamepadButton::DpadUp, g.dpad.up.isPressed);
        dpad(1, GamepadButton::DpadDown, g.dpad.down.isPressed);
        dpad(2, GamepadButton::DpadLeft, g.dpad.left.isPressed);
        dpad(3, GamepadButton::DpadRight, g.dpad.right.isPressed);

        sink_->onAxisValue(padIndex, GamepadAxis::LeftX, quantizeStick(g.leftThumbstick.xAxis.value));
        sink_->onAxisValue(padIndex, GamepadAxis::LeftY, quantizeStick(g.leftThumbstick.yAxis.value));
        sink_->onAxisValue(padIndex, GamepadAxis::RightX, quantizeStick(g.rightThumbstick.xAxis.value));
        sink_->onAxisValue(padIndex, GamepadAxis::RightY, quantizeStick(g.rightThumbstick.yAxis.value));
        sink_->onAxisValue(padIndex, GamepadAxis::LeftTrigger, quantizeTrigger(g.leftTrigger.value));
        sink_->onAxisValue(padIndex, GamepadAxis::RightTrigger, quantizeTrigger(g.rightTrigger.value));
    }

    void addController(GCController* c) {
        if (!c || !c.extendedGamepad) return;
        Pad* raw = nullptr;
        int32_t newIdx = -1;
        {
            std::lock_guard<std::mutex> lock(padsMutex_);
            for (const auto& up : pads_) {
                if (up->controller == c) return;
            }
            if (pads_.size() >= 8) return;
            auto pad = std::make_unique<Pad>();
            pad->controller = [c retain];
            pad->uid = "gc-" + std::to_string(reinterpret_cast<uintptr_t>(c));
            pad->name = c.vendorName ? std::string(c.vendorName.UTF8String) : "Gamepad";
            newIdx = static_cast<int32_t>(pads_.size());
            pads_.push_back(std::move(pad));
            raw = pads_.back().get();
            notifyPadsLocked();
        }
        if (sink_ && newIdx >= 0) {
            sink_->onAxisValue(newIdx, GamepadAxis::LeftX, 64);
            sink_->onAxisValue(newIdx, GamepadAxis::LeftY, 64);
            sink_->onAxisValue(newIdx, GamepadAxis::RightX, 64);
            sink_->onAxisValue(newIdx, GamepadAxis::RightY, 64);
            sink_->onAxisValue(newIdx, GamepadAxis::LeftTrigger, 0);
            sink_->onAxisValue(newIdx, GamepadAxis::RightTrigger, 0);
        }
        c.handlerQueue = handlerQueue_;
        c.extendedGamepad.valueChangedHandler = ^(GCExtendedGamepad*, GCControllerElement*) {
          if (running_.load(std::memory_order_acquire)) syncState(raw);
        };
    }

    void removeController(GCController* c) {
        std::lock_guard<std::mutex> lock(padsMutex_);
        for (auto it = pads_.begin(); it != pads_.end(); ++it) {
            if ((*it)->controller == c) {
                (*it)->controller.extendedGamepad.valueChangedHandler = nil;
                [(*it)->controller release];
                pads_.erase(it);
                notifyPadsLocked();
                return;
            }
        }
    }

    std::atomic<bool> running_{false};
    GamepadSink* sink_ = nullptr;
    std::mutex padsMutex_;
    std::vector<std::unique_ptr<Pad>> pads_;
    ::id connectObs_ = nil;
    ::id disconnectObs_ = nil;
    dispatch_queue_t handlerQueue_ = nullptr;
};

} // namespace

namespace detail {

std::unique_ptr<IGamepadDriver> createPlatformGamepadDriver() {
    return std::make_unique<GameControllerDriver>();
}

} // namespace detail
} // namespace nota::io::gamepad

#endif
