// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/gamepad/GamepadTypes.h"
#include "nota/io/gamepad/IGamepadDriver.h"

#include <cstdint>
#include <memory>

namespace nota::io::gamepad {

/// Gamepad input frontend decoupling OS controller discovery from SPSC button/axis polling.
class NOTA_IO_API GamepadInput {
  public:
    using ButtonEvent = GamepadButtonEvent;

    GamepadInput();
    explicit GamepadInput(std::unique_ptr<IGamepadDriver> driver);
    ~GamepadInput();

    GamepadInput(const GamepadInput&) = delete;
    GamepadInput& operator=(const GamepadInput&) = delete;

    void start();
    void stop();

    int32_t padCount() const;
    void padInfo(int32_t i, const char** outUid, const char** outName) const;

    int32_t pollEvents(ButtonEvent* out, int32_t max);
    int32_t axisValues(int32_t i, int32_t* out, int32_t max) const;

  private:
    struct State;
    std::unique_ptr<State> state_;
};

[[nodiscard]] NOTA_IO_API std::unique_ptr<IGamepadDriver> createBuiltinGamepadDriver();

} // namespace nota::io::gamepad
