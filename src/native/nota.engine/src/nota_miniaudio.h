// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared miniaudio configuration for Nota's non-Apple audio backend (Windows and
// Linux). EVERY translation unit that includes miniaudio MUST go through this
// header so the MA_NO_* / MA_ENABLE_* macros are identical across TUs — they
// change public struct layouts (ma_device etc.), so mismatched macros are an ODR
// hazard that corrupts memory at runtime. The single implementation TU
// (miniaudio_impl.cpp) defines MINIAUDIO_IMPLEMENTATION before including this.
//
// We use only the device-I/O + enumeration layer: decoding stays in dr_libs
// (AudioFile.cpp), so the high-level engine/decoder/resource-manager pieces are
// trimmed to cut build time and dependencies. Backends per platform:
//   • Windows — WASAPI (ASIO is a later addition).
//   • Linux   — PulseAudio (preferred; also drives PipeWire) with an ALSA
//               fallback. Both are runtime-linked by miniaudio (dlopen), so the
//               build needs no libpulse/libasound at link time — only -ldl.

#pragma once

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
    // Order sets miniaudio's default preference: PulseAudio first, then ALSA.
    #define MA_ENABLE_PULSEAUDIO
    #define MA_ENABLE_ALSA
#endif

#include "miniaudio/miniaudio.h"
