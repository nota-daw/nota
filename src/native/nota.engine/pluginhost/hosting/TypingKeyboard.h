// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TypingKeyboard — the computer-keyboard piano shared by every plugin editor
// window. The main window owns the same map (MainWindow.Input.cs KeyToPitch:
// the A-row is C4=60..C5=72, Z/X shift the octave, C/V the velocity); editor
// windows mirror it so auditioning a hosted plugin plays the same pitches.
// Process-wide state, message thread only in practice (written from the
// managed push, read when a key event lands); atomics keep it honest if the
// UI and JUCE message loops ever run on different threads. Never touched by
// the audio thread — pitch resolution happens before the GuiNoteQueue.

#pragma once

#include <cstdint>

namespace nota {
namespace hosting {

// Base pitches at octave 0, matching the main window's KeyToPitch map.
int32_t typingKeyboardBasePitchForMacKeyCode(unsigned short keyCode); // -1 = not a note key
int32_t typingKeyboardBasePitchForCharacter(int unicodeChar);         // case-insensitive, -1 = not a note key

// Mirrors the main window's typing octave (Z/X, -4..+4) and velocity (C/V,
// 1..127). Out-of-range inputs are clamped.
void setTypingKeyboardState(int32_t octaveDelta, int32_t velocity);
int32_t typingKeyboardOctave();
int32_t typingKeyboardVelocity();

// Applies the current octave to a base pitch, clamped to MIDI 0..127.
int32_t typingKeyboardResolvePitch(int32_t basePitch);

// Current velocity as 0..1 for note-on events.
float typingKeyboardVelocity01();

} // namespace hosting
} // namespace nota
