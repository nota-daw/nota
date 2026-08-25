// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Platform-selected construction of the audio output backend and input capture.
// macOS -> CoreAudio (HAL); Windows -> miniaudio (WASAPI). The engine never
// names a concrete backend; it asks the factory, so the device layer stays
// swappable per platform (ARCHITECTURE.md § Audio engine).

#pragma once

#include "AudioBackend.h"
#include "AudioInput.h"

#include <memory>

namespace nota {

std::unique_ptr<AudioBackend> createAudioBackend();
std::unique_ptr<AudioInput>   createAudioInput();

} // namespace nota
