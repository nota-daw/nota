// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Bridge between the string-UID device config (AudioConfig_miniaudio.cpp) and the
// miniaudio backends, shared by Windows + Linux. Because BackendConfig carries the
// device as a plain uint32_t (a CoreAudio AudioDeviceID on macOS), the miniaudio
// ports reinterpret that integer as a 1-based index into the enumerated device list
// for the given scope (0 = system default). This helper turns that index back into a
// concrete ma_device_id at start() time. Implemented in AudioConfig_miniaudio.cpp so
// it shares the one enumeration path.

#pragma once

#include "nota_miniaudio.h"

#include <cstdint>

namespace nota {

// Fills `out` with the ma_device_id of the 1-based enumerated device on the given
// scope. Returns false for index 0 or out of range — the caller then passes NULL
// to miniaudio (system default device).
bool notaMiniaudioDeviceIdForIndex(bool inputScope, uint32_t oneBasedIndex, ma_device_id& out);

} // namespace nota
