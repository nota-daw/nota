// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EditorSnapshot — the picture of a plug-in's editor window that its device card shows
// (specs/046). Captured from the window itself while it is still on screen: macOS through
// CGWindowListCreateImage on our own window (it sees Metal/OpenGL content and needs no
// Screen Recording permission), Windows through PrintWindow; Linux has no capture yet.
// Message thread only.

#pragma once

#include <cstdint>
#include <vector>

#include <juce_gui_basics/juce_gui_basics.h>

namespace nota {
namespace hosting {

// The editor's content (title bar cropped off), scaled to card size and JPEG-encoded.
// Empty when the platform can't capture, the window isn't on screen, or the capture comes
// back blank (one colour) — the caller then keeps the preview it had.
std::vector<uint8_t> snapshotEditor(juce::DocumentWindow& window);

} // namespace hosting
} // namespace nota
