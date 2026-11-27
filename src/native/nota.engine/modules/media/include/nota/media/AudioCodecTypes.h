// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nota::media {

enum class SampleFormat : uint8_t {
    Unknown = 0,
    Float32 = 1,
    Pcm16 = 2,
    Pcm24 = 3,
    Pcm32 = 4,
};

struct AudioStreamInfo {
    int32_t channels = 0;
    double sampleRate = 0.0;
    int64_t totalFrames = 0; // 0 when unknown (e.g. headerless/streamed container)
    SampleFormat nativeFormat = SampleFormat::Unknown;
    bool seekable = false;
    std::string codecId;

    bool valid() const noexcept { return channels > 0 && sampleRate > 0.0; }
};

enum class SampleLoadPolicy : int32_t {
    Auto = 0,       // Stream seekable files larger than thresholdBytes, else decode into RAM
    MemoryOnly = 1, // Always decode full PCM into RAM
    StreamAlways = 2,
};

inline constexpr int64_t kDefaultStreamThresholdBytes = 16LL * 1024 * 1024; // ~24s stereo @ 44.1kHz f32

struct AudioDecodeOptions {
    SampleLoadPolicy policy = SampleLoadPolicy::Auto;
    int64_t streamThresholdBytes = kDefaultStreamThresholdBytes;
};

struct AudioEncodeConfig {
    int32_t channels = 2;
    int32_t sampleRate = 44100;
    int32_t bitsPerSample = 24;   // 16 or 24 for lossless targets (WAV / FLAC)
    int32_t bitrateKbps = 320;    // CBR kbps for lossy codecs (e.g. MP3: 128/192/256/320)
    int32_t compressionLevel = 5; // Codec-specific preset (e.g. FLAC 0..8)
    bool dither = false;          // ±1-LSB TPDF dither when quantizing down to 16-bit
};

struct AudioCodecDescriptor {
    std::string formatId; // e.g. "wav", "flac", "mp3"
    std::string displayName;
    std::vector<std::string> extensions; // lowercase without leading dot
    bool canDecode = false;
    bool canEncode = false;
};

} // namespace nota::media
