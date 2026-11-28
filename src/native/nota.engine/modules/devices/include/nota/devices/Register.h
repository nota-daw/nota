// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Register — idempotent built-in device registration into nota::dsp::DeviceRegistry.

#pragma once

#include "nota/devices/Export.h"

#include <cstdint>
#include <string_view>

namespace nota::devices {

NOTA_DEVICES_API void registerBuiltinDevices();
NOTA_DEVICES_API bool builtinDevicesRegistered() noexcept;

NOTA_DEVICES_API int32_t twinParamFromV1(int32_t paramIndex) noexcept;
NOTA_DEVICES_API void setMosaicRoot(std::string_view name, std::string_view path);

NOTA_DEVICES_API bool reverbCombWrapSelfTest();
NOTA_DEVICES_API bool runBuiltinFxSelfTest();

} // namespace nota::devices
