// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

#include "AudioBackendFactory.h"

// macOS uses CoreAudio; every other supported platform (Windows, Linux) shares the
// miniaudio backend — only the enabled miniaudio backend differs (nota_miniaudio.h).
#if defined(__APPLE__)
    #include "CoreAudioBackend.h"
    #include "CoreAudioInput.h"
#elif defined(_WIN32) || defined(__linux__)
    #include "MiniaudioBackend.h"
    #include "MiniaudioInput.h"
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
