// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AsioSampleFormat.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota::asio {
namespace {

// Significant bits of the 32-bit-container integer types.
int32_t int32Bits(SampleType t) {
    switch (t) {
    case SampleType::Int32LSB16: return 16;
    case SampleType::Int32LSB18: return 18;
    case SampleType::Int32LSB20: return 20;
    case SampleType::Int32LSB24: return 24;
    default: return 32;
    }
}

// Round a float to a signed integer of `bits` bits, clamped to [-2^(bits-1), 2^(bits-1) - 1].
inline int32_t quantize(float x, int32_t bits) {
    const double full = std::ldexp(1.0, bits - 1);
    double v = std::nearbyint(static_cast<double>(x) * full);
    v = std::clamp(v, -full, full - 1.0);
    return static_cast<int32_t>(v);
}

} // namespace

bool isSupported(SampleType t) {
    switch (t) {
    case SampleType::Int16LSB:
    case SampleType::Int24LSB:
    case SampleType::Int32LSB:
    case SampleType::Float32LSB:
    case SampleType::Float64LSB:
    case SampleType::Int32LSB16:
    case SampleType::Int32LSB18:
    case SampleType::Int32LSB20:
    case SampleType::Int32LSB24: return true;
    default: return false;
    }
}

int32_t bytesPerSample(SampleType t) {
    switch (t) {
    case SampleType::Int16LSB: return 2;
    case SampleType::Int24LSB: return 3;
    case SampleType::Float64LSB: return 8;
    case SampleType::Int32LSB:
    case SampleType::Float32LSB:
    case SampleType::Int32LSB16:
    case SampleType::Int32LSB18:
    case SampleType::Int32LSB20:
    case SampleType::Int32LSB24: return 4;
    default: return 0;
    }
}

const char* sampleTypeName(SampleType t) {
    switch (t) {
    case SampleType::Int16MSB: return "Int16MSB";
    case SampleType::Int24MSB: return "Int24MSB";
    case SampleType::Int32MSB: return "Int32MSB";
    case SampleType::Float32MSB: return "Float32MSB";
    case SampleType::Float64MSB: return "Float64MSB";
    case SampleType::Int32MSB16: return "Int32MSB16";
    case SampleType::Int32MSB18: return "Int32MSB18";
    case SampleType::Int32MSB20: return "Int32MSB20";
    case SampleType::Int32MSB24: return "Int32MSB24";
    case SampleType::Int16LSB: return "Int16LSB";
    case SampleType::Int24LSB: return "Int24LSB";
    case SampleType::Int32LSB: return "Int32LSB";
    case SampleType::Float32LSB: return "Float32LSB";
    case SampleType::Float64LSB: return "Float64LSB";
    case SampleType::Int32LSB16: return "Int32LSB16";
    case SampleType::Int32LSB18: return "Int32LSB18";
    case SampleType::Int32LSB20: return "Int32LSB20";
    case SampleType::Int32LSB24: return "Int32LSB24";
    case SampleType::DSDInt8LSB1: return "DSDInt8LSB1";
    case SampleType::DSDInt8MSB1: return "DSDInt8MSB1";
    case SampleType::DSDInt8NER8: return "DSDInt8NER8";
    }
    return "unknown";
}

// Byte-wise little-endian reads/writes: the buffers are the driver's, alignment unknown.
void toFloat(const void* src, SampleType t, float* dst, int32_t frames, int32_t stride) {
    const auto* b = static_cast<const uint8_t*>(src);
    switch (t) {
    case SampleType::Int16LSB:
        for (int32_t i = 0; i < frames; ++i) {
            const auto v = static_cast<int16_t>(static_cast<uint16_t>(b[i * 2] | (b[i * 2 + 1] << 8)));
            dst[i * stride] = static_cast<float>(v) * (1.0f / 32768.0f);
        }
        return;
    case SampleType::Int24LSB:
        for (int32_t i = 0; i < frames; ++i) {
            const uint8_t* p = b + i * 3;
            int32_t v = p[0] | (p[1] << 8) | (p[2] << 16);
            if (v & 0x800000) v -= 0x1000000; // sign-extend
            dst[i * stride] = static_cast<float>(v) * (1.0f / 8388608.0f);
        }
        return;
    case SampleType::Int32LSB:
    case SampleType::Int32LSB16:
    case SampleType::Int32LSB18:
    case SampleType::Int32LSB20:
    case SampleType::Int32LSB24: {
        const double scale = 1.0 / std::ldexp(1.0, int32Bits(t) - 1);
        for (int32_t i = 0; i < frames; ++i) {
            int32_t v;
            std::memcpy(&v, b + i * 4, 4);
            dst[i * stride] = static_cast<float>(v * scale);
        }
        return;
    }
    case SampleType::Float32LSB:
        for (int32_t i = 0; i < frames; ++i) std::memcpy(&dst[i * stride], b + i * 4, 4);
        return;
    case SampleType::Float64LSB:
        for (int32_t i = 0; i < frames; ++i) {
            double v;
            std::memcpy(&v, b + i * 8, 8);
            dst[i * stride] = static_cast<float>(v);
        }
        return;
    default:
        for (int32_t i = 0; i < frames; ++i) dst[i * stride] = 0.0f;
        return;
    }
}

void fromFloat(const float* src, int32_t stride, SampleType t, void* dst, int32_t frames) {
    auto* b = static_cast<uint8_t*>(dst);
    switch (t) {
    case SampleType::Int16LSB:
        for (int32_t i = 0; i < frames; ++i) {
            const auto v = static_cast<uint16_t>(quantize(src[i * stride], 16));
            b[i * 2] = static_cast<uint8_t>(v);
            b[i * 2 + 1] = static_cast<uint8_t>(v >> 8);
        }
        return;
    case SampleType::Int24LSB:
        for (int32_t i = 0; i < frames; ++i) {
            const auto v = static_cast<uint32_t>(quantize(src[i * stride], 24));
            b[i * 3] = static_cast<uint8_t>(v);
            b[i * 3 + 1] = static_cast<uint8_t>(v >> 8);
            b[i * 3 + 2] = static_cast<uint8_t>(v >> 16);
        }
        return;
    case SampleType::Int32LSB:
    case SampleType::Int32LSB16:
    case SampleType::Int32LSB18:
    case SampleType::Int32LSB20:
    case SampleType::Int32LSB24: {
        const int32_t bits = int32Bits(t);
        for (int32_t i = 0; i < frames; ++i) {
            const int32_t v = quantize(src[i * stride], bits);
            std::memcpy(b + i * 4, &v, 4);
        }
        return;
    }
    case SampleType::Float32LSB:
        for (int32_t i = 0; i < frames; ++i) std::memcpy(b + i * 4, &src[i * stride], 4);
        return;
    case SampleType::Float64LSB:
        for (int32_t i = 0; i < frames; ++i) {
            const double v = src[i * stride];
            std::memcpy(b + i * 8, &v, 8);
        }
        return;
    default: return;
    }
}

void clear(SampleType t, void* dst, int32_t frames) {
    // Zero is all-zero bytes in every PCM / float layout, so an unused channel of an MSB
    // type can be silenced too (DSD is not: it has no PCM zero, and is left alone).
    int32_t bytes = bytesPerSample(t);
    if (bytes == 0) {
        switch (t) {
        case SampleType::Int16MSB: bytes = 2; break;
        case SampleType::Int24MSB: bytes = 3; break;
        case SampleType::Float64MSB: bytes = 8; break;
        case SampleType::Int32MSB:
        case SampleType::Float32MSB:
        case SampleType::Int32MSB16:
        case SampleType::Int32MSB18:
        case SampleType::Int32MSB20:
        case SampleType::Int32MSB24: bytes = 4; break;
        default: break;
        }
    }
    if (bytes > 0 && dst) std::memset(dst, 0, static_cast<size_t>(bytes) * static_cast<size_t>(frames));
}

} // namespace nota::asio
