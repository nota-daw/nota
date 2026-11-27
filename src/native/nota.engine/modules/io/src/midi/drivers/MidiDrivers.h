// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/midi/IMidiDriver.h"

#include <memory>

namespace nota::io::midi::detail {

#if defined(__APPLE__)
std::shared_ptr<const IMidiDriver> createCoreMidiDriver();
#elif defined(_WIN32) || defined(__linux__)
std::shared_ptr<const IMidiDriver> createRtMidiDriver();
#endif

} // namespace nota::io::midi::detail
