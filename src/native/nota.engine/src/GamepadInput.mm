// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// macOS gamepad input on the GameController framework (see GamepadInput.h for
// why not raw IOKit HID). ARC is off in this target, so ObjC objects are
// retained/released by hand.

#include "GamepadInput.h"
#include "CommandQueue.h" // SpscRingBuffer

#import <GameController/GameController.h>
#import <Foundation/Foundation.h>

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace nota {
namespace {

// Logical button ids for the eight playable buttons (mirrors the UI note layout:
// 1..4 = face A/B/X/Y, 5..8 = shoulders/triggers L1/R1/L2/R2).
enum : int32_t { kBtnA = 1, kBtnB, kBtnX, kBtnY, kL1, kR1, kL2, kR2 };

struct Pad {
    GCController* controller = nil; // retained
    std::string   uid;
    std::string   name;
    bool lastBtn[9]  = {false};     // indexed by logical id 1..8
    bool lastDpad[4] = {false};     // up, down, left, right
};

struct State {
    std::atomic<bool> running{false};
    std::mutex padsMutex;
    std::vector<std::unique_ptr<Pad>> pads;  // stable addresses for handler blocks
    SpscRingBuffer<GamepadInput::ButtonEvent, 256>* queue = nullptr;
    id connectObs = nil;
    id disconnectObs = nil;
    dispatch_queue_t handlerQueue = nullptr; // one serial queue = single producer
    mutable std::vector<std::string> snapshotUids;   // C-ABI out-pointer backing
    mutable std::vector<std::string> snapshotNames;
};

void pushEdge(State* s, int32_t padIndex, int32_t buttonId, bool pressed) {
    GamepadInput::ButtonEvent ev{};
    ev.pressed = pressed ? 1 : 0;
    ev.buttonId = buttonId;
    ev.pad = padIndex;
    s->queue->push(ev); // drop silently if full
}

// Diff the current gamepad state against the pad's remembered state and queue an
// edge for every button/d-pad direction that changed. Runs on the serial handler
// queue (single producer into the ring).
void syncState(State* s, Pad* p) {
    GCExtendedGamepad* g = p->controller.extendedGamepad;
    if (!g) return;

    int32_t padIndex;
    {
        std::lock_guard<std::mutex> lock(s->padsMutex);
        padIndex = -1;
        for (size_t i = 0; i < s->pads.size(); ++i)
            if (s->pads[i].get() == p) { padIndex = (int32_t)i; break; }
        if (padIndex < 0) return; // pad was removed
    }

    auto edge = [&](int slot, int32_t buttonId, bool pressed) {
        if (pressed != p->lastBtn[slot]) { p->lastBtn[slot] = pressed; pushEdge(s, padIndex, buttonId, pressed); }
    };
    edge(kBtnA, kBtnA, g.buttonA.isPressed);
    edge(kBtnB, kBtnB, g.buttonB.isPressed);
    edge(kBtnX, kBtnX, g.buttonX.isPressed);
    edge(kBtnY, kBtnY, g.buttonY.isPressed);
    edge(kL1, kL1, g.leftShoulder.isPressed);
    edge(kR1, kR1, g.rightShoulder.isPressed);
    edge(kL2, kL2, g.leftTrigger.isPressed);
    edge(kR2, kR2, g.rightTrigger.isPressed);

    auto dpad = [&](int i, int32_t buttonId, bool pressed) {
        if (pressed != p->lastDpad[i]) { p->lastDpad[i] = pressed; pushEdge(s, padIndex, buttonId, pressed); }
    };
    dpad(0, GamepadButton::DpadUp,    g.dpad.up.isPressed);
    dpad(1, GamepadButton::DpadDown,  g.dpad.down.isPressed);
    dpad(2, GamepadButton::DpadLeft,  g.dpad.left.isPressed);
    dpad(3, GamepadButton::DpadRight, g.dpad.right.isPressed);
}

void addController(State* s, GCController* c) {
    if (!c || !c.extendedGamepad) return; // ignore micro-gamepads / non-extended
    {
        std::lock_guard<std::mutex> lock(s->padsMutex);
        for (auto& up : s->pads) if (up->controller == c) return; // already tracked
        if (s->pads.size() >= 8) return;                          // UI cap
        auto pad = std::make_unique<Pad>();
        pad->controller = [c retain];
        pad->uid  = "gc-" + std::to_string(reinterpret_cast<uintptr_t>(c));
        pad->name = c.vendorName ? std::string(c.vendorName.UTF8String) : "Gamepad";
        s->pads.push_back(std::move(pad));
    }
    // Deliver value changes on our serial queue so the ring has a single producer.
    c.handlerQueue = s->handlerQueue;
    Pad* raw = nullptr;
    {
        std::lock_guard<std::mutex> lock(s->padsMutex);
        raw = s->pads.back().get();
    }
    c.extendedGamepad.valueChangedHandler = ^(GCExtendedGamepad*, GCControllerElement*) {
        if (s->running.load()) syncState(s, raw);
    };
}

void removeController(State* s, GCController* c) {
    std::lock_guard<std::mutex> lock(s->padsMutex);
    for (auto it = s->pads.begin(); it != s->pads.end(); ++it) {
        if ((*it)->controller == c) {
            (*it)->controller.extendedGamepad.valueChangedHandler = nil;
            [(*it)->controller release];
            s->pads.erase(it);
            return;
        }
    }
}

} // namespace

struct GamepadInput::Impl {
    State state;
    SpscRingBuffer<ButtonEvent, 256> queue;
};

GamepadInput::GamepadInput() : impl_(new Impl()) { impl_->state.queue = &impl_->queue; }
GamepadInput::~GamepadInput() { stop(); delete impl_; }

void GamepadInput::start() {
    bool expected = false;
    if (!impl_->state.running.compare_exchange_strong(expected, true)) return;
    State* s = &impl_->state;
    s->handlerQueue = dispatch_queue_create("app.nota.gamepad", DISPATCH_QUEUE_SERIAL);

    // GameController delivers connect/disconnect on the app's run loop (Nota is a
    // GUI app, so that loop is live). Track already-paired pads, then keep up.
    NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
    s->connectObs = [[nc addObserverForName:GCControllerDidConnectNotification object:nil queue:nil
        usingBlock:^(NSNotification* n){ addController(s, n.object); }] retain];
    s->disconnectObs = [[nc addObserverForName:GCControllerDidDisconnectNotification object:nil queue:nil
        usingBlock:^(NSNotification* n){ removeController(s, n.object); }] retain];
    for (GCController* c in [GCController controllers]) addController(s, c);
    [GCController startWirelessControllerDiscoveryWithCompletionHandler:^{}];
}

void GamepadInput::stop() {
    bool expected = true;
    if (!impl_->state.running.compare_exchange_strong(expected, false)) return;
    State* s = &impl_->state;
    [GCController stopWirelessControllerDiscovery];
    NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
    if (s->connectObs)    { [nc removeObserver:s->connectObs];    [s->connectObs release];    s->connectObs = nil; }
    if (s->disconnectObs) { [nc removeObserver:s->disconnectObs]; [s->disconnectObs release]; s->disconnectObs = nil; }
    {
        std::lock_guard<std::mutex> lock(s->padsMutex);
        for (auto& up : s->pads) {
            up->controller.extendedGamepad.valueChangedHandler = nil;
            [up->controller release];
        }
        s->pads.clear();
    }
    if (s->handlerQueue) { dispatch_release(s->handlerQueue); s->handlerQueue = nullptr; }
}

int32_t GamepadInput::padCount() const {
    std::lock_guard<std::mutex> lock(impl_->state.padsMutex);
    return static_cast<int32_t>(impl_->state.pads.size());
}

void GamepadInput::padInfo(int32_t i, const char** outUid, const char** outName) const {
    std::lock_guard<std::mutex> lock(impl_->state.padsMutex);
    impl_->state.snapshotUids.clear();
    impl_->state.snapshotNames.clear();
    for (const auto& up : impl_->state.pads) {
        impl_->state.snapshotUids.push_back(up->uid);
        impl_->state.snapshotNames.push_back(up->name);
    }
    if (i < 0 || i >= static_cast<int32_t>(impl_->state.snapshotUids.size())) { *outUid = *outName = ""; return; }
    *outUid = impl_->state.snapshotUids[i].c_str();
    *outName = impl_->state.snapshotNames[i].c_str();
}

int32_t GamepadInput::pollEvents(ButtonEvent* out, int32_t max) {
    if (!out || max <= 0) return 0;
    int32_t n = 0;
    ButtonEvent ev;
    while (n < max && impl_->queue.pop(ev)) out[n++] = ev;
    return n;
}

} // namespace nota
