// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ASIO sample formats (specs/049, research R6). Our own mirror of the SDK's ASIOSampleType
// values, so the platform-independent ASIO core (and its self-test) builds without the SDK.
// The conversions are pure and real-time safe: no allocation, no locks.

#pragma once

#include <cstdint>

namespace nota::asio {

// Values match ASIOSampleType in the SDK's asio.h.
enum class SampleType : int32_t {
    Int16MSB = 0,
    Int24MSB = 1,
    Int32MSB = 2,
    Float32MSB = 3,
    Float64MSB = 4,
    Int32MSB16 = 8,
    Int32MSB18 = 9,
    Int32MSB20 = 10,
    Int32MSB24 = 11,
    Int16LSB = 16,
    Int24LSB = 17, // packed 3 bytes
    Int32LSB = 18,
    Float32LSB = 19,
    Float64LSB = 20,
    Int32LSB16 = 24, // 32-bit container, value in the low 16 bits
    Int32LSB18 = 25,
    Int32LSB20 = 26,
    Int32LSB24 = 27,
    DSDInt8LSB1 = 32,
    DSDInt8MSB1 = 33,
    DSDInt8NER8 = 40,
};

// True for the little-endian PCM / float types Nota converts; false for every MSB and DSD type.
bool isSupported(SampleType type);

// Bytes per sample in the driver buffer (0 for an unsupported type).
int32_t bytesPerSample(SampleType type);

// Short name for messages ("Int32LSB", "Float32MSB", …).
const char* sampleTypeName(SampleType type);

// One channel of driver samples -> float. `dst` is written every `dstStride` floats (2 for
// interleaved stereo). Integers scale by 1 / 2^(bits-1). Unsupported types write zeros.
void toFloat(const void* src, SampleType type, float* dst, int32_t frames, int32_t dstStride);

// Float, read every `srcStride` floats -> one channel of driver samples. Clamped to [-1, 1)
// and rounded for the integer types (no dither). Unsupported types write nothing.
void fromFloat(const float* src, int32_t srcStride, SampleType type, void* dst, int32_t frames);

// Silence in the driver's format: zero bytes for every PCM / float type, MSB included.
void clear(SampleType type, void* dst, int32_t frames);

} // namespace nota::asio
