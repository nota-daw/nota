// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/gamepad/GamepadTypes.h"

#include <cstdint>
#include <string_view>
#include <vector>

namespace nota::io::gamepad {

/// Sink receiving lock-free controller button edges and axis updates from a gamepad driver.
class NOTA_IO_API GamepadSink {
  public:
    virtual ~GamepadSink() = default;

    virtual void onPadsChanged(const std::vector<GamepadDeviceInfo>& pads) = 0;
    virtual void onButtonEdge(int32_t padIndex, int32_t buttonId, bool pressed) = 0;
    virtual void onAxisValue(int32_t padIndex, int32_t axisIndex, int32_t value) = 0;
};

/// Platform gamepad driver backend (GameController on macOS, stub/evdev/XInput elsewhere).
class NOTA_IO_API IGamepadDriver {
  public:
    virtual ~IGamepadDriver() = default;

    virtual std::string_view id() const noexcept = 0;
    virtual std::string_view displayName() const noexcept = 0;

    virtual bool start(GamepadSink& sink) = 0;
    virtual void stop() = 0;
};

} // namespace nota::io::gamepad
