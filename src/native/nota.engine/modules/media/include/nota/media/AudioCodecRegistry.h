// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioCodecTypes.h"
#include "nota/media/Export.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/IAudioEncoder.h"

#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace nota::media {

class NOTA_MEDIA_API AudioCodecRegistry {
  public:
    using DecoderFactory = std::function<std::unique_ptr<IAudioDecoder>()>;
    using EncoderFactory = std::function<std::unique_ptr<IAudioEncoder>()>;

    [[nodiscard]] static AudioCodecRegistry& instance();

    void registerDecoder(AudioCodecDescriptor desc, DecoderFactory factory);
    void registerEncoder(AudioCodecDescriptor desc, EncoderFactory factory);

    [[nodiscard]] bool supportsDecodeExtension(const std::string& extOrPath) const;
    [[nodiscard]] bool supportsEncodeFormat(const std::string& formatIdOrPath) const;

    [[nodiscard]] std::unique_ptr<IAudioDecoder> openDecoder(const std::string& path) const;
    [[nodiscard]] std::unique_ptr<IAudioEncoder> openEncoder(
        const std::string& formatIdOrPath, const std::string& path, const AudioEncodeConfig& config) const;

    [[nodiscard]] std::vector<AudioCodecDescriptor> listCodecs() const;

  private:
    AudioCodecRegistry() = default;

    struct DecoderEntry {
        AudioCodecDescriptor desc;
        DecoderFactory factory;
    };
    struct EncoderEntry {
        AudioCodecDescriptor desc;
        EncoderFactory factory;
    };

    mutable std::mutex mutex_;
    std::unordered_map<std::string, DecoderEntry> decodersByExt_;
    std::unordered_map<std::string, EncoderEntry> encodersByFormat_;
};

// Registers built-in codecs (WAV via dr_wav, FLAC via dr_flac + libFLAC, MP3 via dr_mp3 + LAME). Idempotent.
NOTA_MEDIA_API void registerBuiltinAudioCodecs();

} // namespace nota::media
