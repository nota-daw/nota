// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

// Windows / Linux gamepad input goes here in a follow-up (XInput / evdev).
// For now the public C ABI reports zero pads so the managed layer treats the
// feature as "unavailable on this platform" without conditional compilation.

#include "GamepadInput.h"

namespace nota {

struct GamepadInput::Impl {};

GamepadInput::GamepadInput() : impl_(new Impl()) {}
GamepadInput::~GamepadInput() { delete impl_; }

void GamepadInput::start() {}
void GamepadInput::stop() {}

int32_t GamepadInput::padCount() const { return 0; }
void GamepadInput::padInfo(int32_t, const char** outUid, const char** outName) const {
    *outUid = "";
    *outName = "";
}
int32_t GamepadInput::pollEvents(ButtonEvent*, int32_t) { return 0; }

} // namespace nota
