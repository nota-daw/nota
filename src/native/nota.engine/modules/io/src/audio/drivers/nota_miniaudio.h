// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#if defined(_WIN32) || defined(__linux__)

#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#define MA_NO_ENGINE
#define MA_NO_NODE_GRAPH
#define MA_NO_RESOURCE_MANAGER
#define MA_ENABLE_ONLY_SPECIFIC_BACKENDS
#if defined(_WIN32)
#define MA_ENABLE_WASAPI
#elif defined(__linux__)
#define MA_ENABLE_PULSEAUDIO
#define MA_ENABLE_ALSA
#endif
#define MA_ENABLE_NULL

#include <miniaudio.h>

#endif
