// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Platform-selected construction of the audio output backend and input capture.
// macOS -> CoreAudio (HAL); Windows -> miniaudio (WASAPI) or ASIO (specs/049); Linux ->
// miniaudio. The engine never names a concrete backend; it asks the factory, so the device
// layer stays swappable per platform (ARCHITECTURE.md § Audio engine). The ASIO backend and
// input share one process-wide AsioSession; only this factory knows.

#pragma once

#include "AudioBackend.h"
#include "AudioConfig.h"
#include "AudioInput.h"

#include <memory>

namespace nota {

// `system` is honoured on Windows builds with ASIO; elsewhere it is ignored.
std::unique_ptr<AudioBackend> createAudioBackend(AudioSystem system = AudioSystem::Wasapi);
// Pass the system that is actually running (after any fallback), so ASIO capture binds to
// the open driver instead of opening a second device.
std::unique_ptr<AudioInput> createAudioInput(AudioSystem system = AudioSystem::Wasapi);

// True if this build can drive ASIO drivers (Windows).
bool asioSupported();

} // namespace nota
