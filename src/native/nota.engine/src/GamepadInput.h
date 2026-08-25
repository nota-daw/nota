// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Gamepad input (macOS, GameController framework). GCController decodes the
// pad (Xbox / PlayStation / Switch Pro / 8BitDo …) into named inputs and calls
// our value handler on a serial queue; each button edge is pushed into a
// lock-free ring buffer. The UI polls it via nota_gamepad_poll_events() on its
// own tick, maps a pressed button to a pitch, and calls nota_engine_note_on/off
// — the exact same entry points used by the computer keyboard, so live play,
// the piano-roll highlight, and armed-track recording all reuse the pipeline.
//
// GameController is used rather than raw IOKit HID on purpose: a Switch Pro
// Controller (which the 8BitDo pads emulate) exposes a free-running packet
// counter as phantom "buttons" over raw HID, so idle pads spammed notes.
// GCController decodes the report properly and is silent when idle.
//
// The face buttons (A/B/X/Y) map to logical buttons 1..4, the shoulders and
// triggers (L1/R1/L2/R2) to 5..8, and the d-pad to U/D/L/R events; sticks are
// ignored (this is note input, not CC). A pad is identified by a session uid.
//
// v1 is macOS-only; Win (XInput/RawInput) and Linux (evdev) come next.

#pragma once

#include <cstdint>

namespace nota {

class GamepadInput {
public:
    // One edge: a button went down (pressed=1) or came up (pressed=0).
    // `uid` is the stable pad id (IORegistryEntryID); the buffer always
    // carries a NUL and truncates longer uids (they're only ~10 chars).
    struct ButtonEvent {
        int32_t pressed;      // 1 = down, 0 = up
        int32_t buttonId;     // stable per pad model; see GamepadButton::*
        int32_t pad;          // slot index into the connected-pads table (0..3)
        // uid lives in the pads table; kept off the event so it stays POD and
        // the ring is trivially copyable. UI looks up uid by pad index.
    };

    GamepadInput();
    ~GamepadInput();

    void start();
    void stop(); // also stops at destruction

    // Current number of connected pads (the slots in the pads table).
    int32_t padCount() const;
    // Stable ui/name for pad slot `i` (empty strings if out of range).
    void padInfo(int32_t i, const char** outUid, const char** outName) const;

    // Drain a bounded batch of edges (single consumer: the UI thread). Silent
    // drop on overflow — losing a few edges on a saturated tick is fine.
    int32_t pollEvents(ButtonEvent* out, int32_t max);

    struct Impl;
private:
    Impl* impl_ = nullptr;
};

// Stable button ids for the events. The d-pad resolves to its four cardinal
// buttons; the face/shoulder/trigger buttons carry a logical id 1..8 (A B X Y
// L1 R1 L2 R2). The label shown in the UI is cosmetic and can be remapped later.
namespace GamepadButton {
    inline constexpr int32_t DpadUp    = -1000;
    inline constexpr int32_t DpadDown  = -1001;
    inline constexpr int32_t DpadLeft  = -1002;
    inline constexpr int32_t DpadRight = -1003;
    inline constexpr bool    isDpad(int32_t id) { return id >= DpadRight && id <= DpadUp; }
}

} // namespace nota
