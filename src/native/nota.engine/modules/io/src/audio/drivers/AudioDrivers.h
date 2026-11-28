// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/audio/IAudioDriver.h"

#include <memory>

namespace nota::io::audio::detail {

#if defined(__APPLE__)
std::shared_ptr<const IAudioDriver> createCoreAudioDriver();
#elif defined(_WIN32) || defined(__linux__)
std::shared_ptr<const IAudioDriver> createMiniaudioDriver();
#endif

#if defined(NOTA_HAS_ASIO)
std::shared_ptr<const IAudioDriver> createAsioDriver();
#endif

} // namespace nota::io::audio::detail
