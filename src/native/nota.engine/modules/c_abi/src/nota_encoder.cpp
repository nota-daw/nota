// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Thin C ABI bridge for streaming audio export (FLAC / MP3) delegating to nota::media::AudioCodecRegistry.

#include "nota/nota_engine.h"

#include <nota/media/AudioCodecRegistry.h>
#include <nota/media/AudioCodecTypes.h>
#include <nota/media/IAudioEncoder.h>

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

struct NotaEncoder {
    std::unique_ptr<nota::media::IAudioEncoder> codec;
    bool failed = false;
};

namespace {

NotaEncoder* OpenRegisteredEncoder(const char* formatId, const char* path, const nota::media::AudioEncodeConfig& cfg) {
    if (!path || !*path) return nullptr;
    auto codec = nota::media::AudioCodecRegistry::instance().openEncoder(formatId, path, cfg);
    if (!codec) return nullptr;
    auto handle = std::make_unique<NotaEncoder>();
    handle->codec = std::move(codec);
    return handle.release();
}

} // namespace

extern "C" {

NOTA_API NotaEncoder* nota_encoder_open_flac(
    const char* path, int32_t sampleRate, int32_t bitsPerSample, int32_t dither) {
    if ((bitsPerSample != 16 && bitsPerSample != 24) || sampleRate <= 0) return nullptr;
    nota::media::AudioEncodeConfig cfg;
    cfg.channels = 2;
    cfg.sampleRate = sampleRate;
    cfg.bitsPerSample = bitsPerSample;
    cfg.compressionLevel = 5;
    cfg.dither = (dither != 0 && bitsPerSample == 16);
    return OpenRegisteredEncoder("flac", path, cfg);
}

NOTA_API NotaEncoder* nota_encoder_open_mp3(const char* path, int32_t sampleRate, int32_t bitrateKbps) {
    if ((sampleRate != 44100 && sampleRate != 48000) ||
        (bitrateKbps != 128 && bitrateKbps != 192 && bitrateKbps != 256 && bitrateKbps != 320))
        return nullptr;
    nota::media::AudioEncodeConfig cfg;
    cfg.channels = 2;
    cfg.sampleRate = sampleRate;
    cfg.bitrateKbps = bitrateKbps;
    return OpenRegisteredEncoder("mp3", path, cfg);
}

NOTA_API NotaResult nota_encoder_write(NotaEncoder* enc, const float* samples, int32_t frames) {
    if (!enc || !samples || frames < 0) return NOTA_ERR_INVALID_ARG;
    if (enc->failed || frames == 0) return enc->failed ? NOTA_ERR_UNKNOWN : NOTA_OK;
    if (!enc->codec || !enc->codec->writeFrames(samples, frames)) {
        enc->failed = true;
        return NOTA_ERR_UNKNOWN;
    }
    return NOTA_OK;
}

NOTA_API NotaResult nota_encoder_close(NotaEncoder* enc) {
    if (!enc) return NOTA_ERR_INVALID_ARG;
    std::unique_ptr<NotaEncoder> guard(enc);
    if (guard->failed) {
        if (guard->codec) guard->codec->discard();
        return NOTA_ERR_UNKNOWN;
    }
    if (!guard->codec) return NOTA_ERR_UNKNOWN;
    return guard->codec->close() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

NOTA_API void nota_encoder_discard(NotaEncoder* enc) {
    if (!enc) return;
    std::unique_ptr<NotaEncoder> guard(enc);
    if (guard->codec) guard->codec->discard();
}

} // extern "C"
