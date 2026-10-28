// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TypingKeyboard implementation. Plain C++ (no JUCE, no AppKit): the macOS
// keycodes below are just numbers from the ANSI layout, and the unit test
// pins the whole table.

#include "TypingKeyboard.h"

#include "nota/nota_engine.h" // NOTA_API for the C ABI setter

#include <atomic>
#include <cctype>

namespace nota {
namespace hosting {
namespace {

constexpr int32_t kMinOctave = -4;
constexpr int32_t kMaxOctave = 4;
constexpr int32_t kMinVelocity = 1;
constexpr int32_t kMaxVelocity = 127;

std::atomic<int32_t> g_octave{0};
std::atomic<int32_t> g_velocity{100}; // matches the main window's default

int32_t clampInt(int32_t v, int32_t lo, int32_t hi) {
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

} // namespace

int32_t typingKeyboardBasePitchForMacKeyCode(unsigned short keyCode) {
    // ANSI virtual keycodes for A W S E D F T G Y H U J K.
    switch (keyCode) {
    case 0x00: return 60; // A
    case 0x0D: return 61; // W
    case 0x01: return 62; // S
    case 0x0E: return 63; // E
    case 0x02: return 64; // D
    case 0x03: return 65; // F
    case 0x11: return 66; // T
    case 0x05: return 67; // G
    case 0x10: return 68; // Y
    case 0x04: return 69; // H
    case 0x20: return 70; // U
    case 0x26: return 71; // J
    case 0x28: return 72; // K
    default: return -1;
    }
}

int32_t typingKeyboardBasePitchForCharacter(int unicodeChar) {
    switch (std::tolower(unicodeChar)) {
    case 'a': return 60;
    case 'w': return 61;
    case 's': return 62;
    case 'e': return 63;
    case 'd': return 64;
    case 'f': return 65;
    case 't': return 66;
    case 'g': return 67;
    case 'y': return 68;
    case 'h': return 69;
    case 'u': return 70;
    case 'j': return 71;
    case 'k': return 72;
    default: return -1;
    }
}

void setTypingKeyboardState(int32_t octaveDelta, int32_t velocity) {
    g_octave.store(clampInt(octaveDelta, kMinOctave, kMaxOctave), std::memory_order_relaxed);
    g_velocity.store(clampInt(velocity, kMinVelocity, kMaxVelocity), std::memory_order_relaxed);
}

int32_t typingKeyboardOctave() {
    return g_octave.load(std::memory_order_relaxed);
}

int32_t typingKeyboardVelocity() {
    return g_velocity.load(std::memory_order_relaxed);
}

int32_t typingKeyboardResolvePitch(int32_t basePitch) {
    return clampInt(basePitch + typingKeyboardOctave() * 12, 0, 127);
}

float typingKeyboardVelocity01() {
    return typingKeyboardVelocity() / 127.0f;
}

} // namespace hosting
} // namespace nota

extern "C" NOTA_API void nota_pluginhost_set_typing_keyboard(int32_t octave_delta, int32_t velocity) {
    nota::hosting::setTypingKeyboardState(octave_delta, velocity);
}
