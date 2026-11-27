// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"

#include <cstdint>

namespace nota::io::audio::asio {

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
    Int24LSB = 17,
    Int32LSB = 18,
    Float32LSB = 19,
    Float64LSB = 20,
    Int32LSB16 = 24,
    Int32LSB18 = 25,
    Int32LSB20 = 26,
    Int32LSB24 = 27,
    DSDInt8LSB1 = 32,
    DSDInt8MSB1 = 33,
    DSDInt8NER8 = 40,
};

NOTA_IO_API bool isSupported(SampleType type);
NOTA_IO_API int32_t bytesPerSample(SampleType type);
NOTA_IO_API const char* sampleTypeName(SampleType type);
NOTA_IO_API void toFloat(const void* src, SampleType type, float* dst, int32_t frames, int32_t dstStride);
NOTA_IO_API void fromFloat(const float* src, int32_t srcStride, SampleType type, void* dst, int32_t frames);
NOTA_IO_API void clear(SampleType type, void* dst, int32_t frames);

} // namespace nota::io::audio::asio
