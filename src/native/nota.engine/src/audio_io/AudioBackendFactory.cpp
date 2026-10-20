// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AudioBackendFactory.h"

// macOS uses CoreAudio; every other supported platform (Windows, Linux) shares the
// miniaudio backend — only the enabled miniaudio backend differs (nota_miniaudio.h).
#if defined(__APPLE__)
#include "audio_io/macos/CoreAudioBackend.h"
#include "audio_io/macos/CoreAudioInput.h"
#elif defined(_WIN32) || defined(__linux__)
#include "audio_io/desktop/MiniaudioBackend.h"
#include "audio_io/desktop/MiniaudioInput.h"
#else
#error "No audio backend for this platform yet (see AudioBackendFactory.cpp)."
#endif

namespace nota {

std::unique_ptr<AudioBackend> createAudioBackend() {
#if defined(__APPLE__)
    return std::make_unique<CoreAudioBackend>();
#else
    return std::make_unique<MiniaudioBackend>();
#endif
}

std::unique_ptr<AudioInput> createAudioInput() {
#if defined(__APPLE__)
    return std::make_unique<CoreAudioInput>();
#else
    return std::make_unique<MiniaudioInput>();
#endif
}

} // namespace nota
