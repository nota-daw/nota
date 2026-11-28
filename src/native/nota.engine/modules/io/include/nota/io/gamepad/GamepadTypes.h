// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <cstdint>
#include <string>
#include <type_traits>

namespace nota::io::gamepad {

struct GamepadButtonEvent {
    int32_t pressed = 0;
    int32_t buttonId = 0;
    int32_t pad = 0;
};
static_assert(std::is_trivially_copyable_v<GamepadButtonEvent>,
    "GamepadButtonEvent must stay trivially copyable for lock-free rings");

struct GamepadDeviceInfo {
    std::string uid;
    std::string name;
};

namespace GamepadButton {
inline constexpr int32_t DpadUp = -1000;
inline constexpr int32_t DpadDown = -1001;
inline constexpr int32_t DpadLeft = -1002;
inline constexpr int32_t DpadRight = -1003;

inline constexpr bool isDpad(int32_t id) noexcept {
    return id >= DpadRight && id <= DpadUp;
}
} // namespace GamepadButton

namespace GamepadAxis {
inline constexpr int32_t LeftX = 0;
inline constexpr int32_t LeftY = 1;
inline constexpr int32_t RightX = 2;
inline constexpr int32_t RightY = 3;
inline constexpr int32_t LeftTrigger = 4;
inline constexpr int32_t RightTrigger = 5;
inline constexpr int32_t Count = 6;
} // namespace GamepadAxis

} // namespace nota::io::gamepad
