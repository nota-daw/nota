// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/gamepad/IGamepadDriver.h"

#include <memory>

namespace nota::io::gamepad::detail {

std::unique_ptr<IGamepadDriver> createPlatformGamepadDriver();

} // namespace nota::io::gamepad::detail
