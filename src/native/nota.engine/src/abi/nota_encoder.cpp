// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Streaming FLAC / MP3 file encoders behind the nota_encoder_* C ABI slice of
// nota_engine.h (export pipeline). The managed exporter owns the render loop
// (render_offline chunks) and streams interleaved stereo floats through one
// handle per output file. Offline-only by construction: nothing here is
// reachable from the audio thread (AR-6), and no handle touches the Engine —
// the functions take only their own opaque encoder.
//
// Codecs: vendored libFLAC 1.5 (BSD) and LAME 3.100 (LGPL, register note 7).
// Conversion rules mirror WavWriter (managed) so a FLAC at a given depth is
// sample-equivalent to the WAV at the same depth: ±1-LSB TPDF dither applies
// to 16-bit targets only, and 24-bit uses the asymmetric 2^23-1 scale.

#include "nota/nota_engine.h"

#include <FLAC/stream_encoder.h>
#include <lame.h>

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <vector>

namespace nota {
namespace {

// Minimal xorshift for the TPDF dither — offline path, distribution quality
// is all that matters (same role as WavWriter's System.Random).
uint32_t NextUniform() {
    static thread_local uint32_t s = 0x6d2b79f5u;
    s ^= s << 13;
    s ^= s >> 17;
    s ^= s << 5;
    return s;
}

float NextUnit() {
    return (float)(NextUniform() >> 8) / 16777216.0f;
}

float Clamp1(float x) {
    return x < -1.0f ? -1.0f : (x > 1.0f ? 1.0f : x);
}

} // namespace
} // namespace nota

// The opaque handle from the header. One per output file.
struct NotaEncoder {
    bool isFlac = true;
    bool failed = false;

    // FLAC
    FLAC__StreamEncoder* flac = nullptr;

    // MP3
    lame_global_flags* gfp = nullptr;
    FILE* file = nullptr;
    std::vector<unsigned char> mp3Buf;

    // shared conversion state (FLAC integer targets)
    int bits = 24;
    bool dither = false;
    float tpdfPrev = 0.0f;
    std::vector<FLAC__int32> pcm;
};

namespace {

using nota::Clamp1;
using nota::NextUnit;

bool EncoderWriteFlac(NotaEncoder* e, const float* samples, int32_t frames) {
    if (e->pcm.size() < (size_t)frames * 2) e->pcm.resize((size_t)frames * 2);

    const float scale = e->bits == 16 ? 32767.0f : 8388607.0f; // 2^15-1 / 2^23-1
    for (size_t i = 0; i < (size_t)frames * 2; ++i) {
        float x = Clamp1(samples[i]);
        if (e->dither) // ±1-LSB TPDF: difference of two uniforms, WavWriter-style
        {
            float cur = NextUnit();
            x += (cur - e->tpdfPrev) / scale;
            e->tpdfPrev = cur;
            x = Clamp1(x);
        }
        e->pcm[i] = (FLAC__int32)std::lround(x * scale);
    }
    return FLAC__stream_encoder_process_interleaved(e->flac, e->pcm.data(), (unsigned)frames);
}

bool EncoderWriteMp3(NotaEncoder* e, const float* samples, int32_t frames) {
    if (e->mp3Buf.empty()) e->mp3Buf.resize(1 << 16); // generous: 1.25x samples + slack at 320 kbps
    int n = lame_encode_buffer_interleaved_ieee_float(e->gfp, samples, frames, e->mp3Buf.data(), (int)e->mp3Buf.size());
    if (n < 0) return false;
    if (n > 0 && std::fwrite(e->mp3Buf.data(), 1, (size_t)n, e->file) != (size_t)n) return false;
    return true;
}

void EncoderFree(NotaEncoder* e) // shared by close and discard
{
    if (e->flac) FLAC__stream_encoder_delete(e->flac);
    if (e->gfp) lame_close(e->gfp);
    if (e->file) std::fclose(e->file);
    delete e;
}

} // namespace

extern "C" {

NOTA_API NotaEncoder* nota_encoder_open_flac(
    const char* path, int32_t sampleRate, int32_t bitsPerSample, int32_t dither) {
    if (!path || !*path || (bitsPerSample != 16 && bitsPerSample != 24) || sampleRate <= 0) return nullptr;

    NotaEncoder* e = new NotaEncoder();
    e->isFlac = true;
    e->bits = bitsPerSample;
    e->dither = dither != 0 && bitsPerSample == 16;

    e->flac = FLAC__stream_encoder_new();
    if (!e->flac) {
        delete e;
        return nullptr;
    }
    FLAC__stream_encoder_set_channels(e->flac, 2);
    FLAC__stream_encoder_set_bits_per_sample(e->flac, bitsPerSample);
    FLAC__stream_encoder_set_sample_rate(e->flac, (unsigned)sampleRate);
    FLAC__stream_encoder_set_compression_level(e->flac, 5); // library default
    if (FLAC__stream_encoder_init_file(e->flac, path, nullptr, nullptr) != FLAC__STREAM_ENCODER_INIT_STATUS_OK) {
        FLAC__stream_encoder_delete(e->flac);
        delete e;
        return nullptr;
    }
    return e;
}

NOTA_API NotaEncoder* nota_encoder_open_mp3(const char* path, int32_t sampleRate, int32_t bitrateKbps) {
    if (!path || !*path || (sampleRate != 44100 && sampleRate != 48000) || // MPEG-1 layer III only
        (bitrateKbps != 128 && bitrateKbps != 192 && bitrateKbps != 256 && bitrateKbps != 320))
        return nullptr;

    FILE* f = std::fopen(path, "wb");
    if (!f) return nullptr;

    NotaEncoder* e = new NotaEncoder();
    e->isFlac = false;
    e->file = f;

    e->gfp = lame_init();
    if (!e->gfp) {
        std::fclose(f);
        delete e;
        return nullptr;
    }
    lame_set_num_channels(e->gfp, 2);
    lame_set_in_samplerate(e->gfp, sampleRate);
    lame_set_out_samplerate(e->gfp, sampleRate);
    lame_set_brate(e->gfp, bitrateKbps); // CBR: vbr is off by default
    if (lame_init_params(e->gfp) < 0) {
        lame_close(e->gfp);
        std::fclose(f);
        delete e;
        return nullptr;
    }
    return e;
}

NOTA_API NotaResult nota_encoder_write(NotaEncoder* enc, const float* samples, int32_t frames) {
    if (!enc || !samples || frames < 0) return NOTA_ERR_INVALID_ARG;
    if (enc->failed || frames == 0) return enc->failed ? NOTA_ERR_UNKNOWN : NOTA_OK;

    bool ok = enc->isFlac ? EncoderWriteFlac(enc, samples, frames) : EncoderWriteMp3(enc, samples, frames);
    if (!ok) enc->failed = true; // sticky: further writes no-op, close reports it
    return ok ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

NOTA_API NotaResult nota_encoder_close(NotaEncoder* enc) {
    if (!enc) return NOTA_ERR_INVALID_ARG;
    NotaResult result = NOTA_OK;

    if (!enc->failed) {
        if (enc->isFlac) {
            // finish() flushes the last block and finalizes MD5/seektable.
            if (!FLAC__stream_encoder_finish(enc->flac)) result = NOTA_ERR_UNKNOWN;
        } else {
            if (enc->mp3Buf.empty()) enc->mp3Buf.resize(1 << 16);
            int n = lame_encode_flush(enc->gfp, enc->mp3Buf.data(), (int)enc->mp3Buf.size());
            if (n > 0 && std::fwrite(enc->mp3Buf.data(), 1, (size_t)n, enc->file) != (size_t)n) n = -1;
            if (n < 0) result = NOTA_ERR_UNKNOWN;
        }
        if (enc->file && std::fclose(enc->file) != 0 && result == NOTA_OK) result = NOTA_ERR_UNKNOWN;
        enc->file = nullptr;
    } else if (enc->file) {
        std::fclose(enc->file); // failed stream: don't care about its error
        enc->file = nullptr;
    }

    EncoderFree(enc);
    return result;
}

NOTA_API void nota_encoder_discard(NotaEncoder* enc) {
    if (!enc) return;
    EncoderFree(enc);
}

} // extern "C"
