// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "codecs/BuiltinCodecs.h"
#include "codecs/PcmDitherConverter.h"
#include "nota/media/AudioCodecRegistry.h"
#include <string>
#include <memory>
#include <cstddef>
#include "nota/media/IAudioEncoder.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/AudioCodecTypes.h"
#ifdef _WIN32
#include "codecs/PathUtils.h"
#endif

#define DR_FLAC_IMPLEMENTATION
#include <dr_flac.h>
#include <FLAC/stream_encoder.h>

#include <algorithm>
#include <cstdint>
#include <vector>

namespace nota::media::detail {

namespace {

class FlacDecoder final : public IAudioDecoder {
  public:
    ~FlacDecoder() override { close(); }

    bool open(const std::string& path) override {
        close();
#ifdef _WIN32
        flac_ = drflac_open_file_w(widePath(path).c_str(), nullptr);
#else
        flac_ = drflac_open_file(path.c_str(), nullptr);
#endif
        if (!flac_) return false;
        info_.channels = static_cast<int32_t>(flac_->channels);
        info_.sampleRate = static_cast<double>(flac_->sampleRate);
        info_.totalFrames = static_cast<int64_t>(flac_->totalPCMFrameCount);
        info_.nativeFormat = (flac_->bitsPerSample <= 16) ? SampleFormat::Pcm16 : SampleFormat::Pcm24;
        info_.seekable = info_.totalFrames > 0;
        info_.codecId = "flac";
        if (!info_.valid()) {
            close();
            return false;
        }
        return true;
    }

    void close() noexcept override {
        if (!flac_) return;
        drflac_close(flac_);
        flac_ = nullptr;
        info_ = {};
    }

    bool isOpen() const noexcept override { return flac_ != nullptr; }
    AudioStreamInfo info() const noexcept override { return info_; }

    bool seekToFrame(int64_t frame) override {
        if (!flac_) return false;
        return drflac_seek_to_pcm_frame(flac_, static_cast<drflac_uint64>(std::max<int64_t>(0, frame))) == DRFLAC_TRUE;
    }

    int64_t readFrames(int64_t maxFrames, float* interleavedOut) override {
        if (!flac_ || !interleavedOut || maxFrames <= 0) return 0;
        return static_cast<int64_t>(
            drflac_read_pcm_frames_f32(flac_, static_cast<drflac_uint64>(maxFrames), interleavedOut));
    }

  private:
    drflac* flac_ = nullptr;
    AudioStreamInfo info_{};
};

class FlacEncoder final : public IAudioEncoder {
  public:
    ~FlacEncoder() override { discard(); }

    bool open(const std::string& path, const AudioEncodeConfig& config) override {
        discard();
        if (path.empty() || config.sampleRate <= 0 || config.channels <= 0 ||
            (config.bitsPerSample != 16 && config.bitsPerSample != 24))
            return false;

        enc_ = FLAC__stream_encoder_new();
        if (!enc_) return false;

        FLAC__stream_encoder_set_channels(enc_, static_cast<unsigned>(config.channels));
        FLAC__stream_encoder_set_bits_per_sample(enc_, static_cast<unsigned>(config.bitsPerSample));
        FLAC__stream_encoder_set_sample_rate(enc_, static_cast<unsigned>(config.sampleRate));
        FLAC__stream_encoder_set_compression_level(
            enc_, static_cast<unsigned>(std::clamp(config.compressionLevel, 0, 8)));

        if (FLAC__stream_encoder_init_file(enc_, path.c_str(), nullptr, nullptr) !=
            FLAC__STREAM_ENCODER_INIT_STATUS_OK) {
            FLAC__stream_encoder_delete(enc_);
            enc_ = nullptr;
            return false;
        }

        channels_ = config.channels;
        converter_.reset(config.bitsPerSample, config.dither);
        failed_ = false;
        return true;
    }

    bool writeFrames(const float* interleavedIn, int32_t frames) override {
        if (!enc_ || !interleavedIn || frames < 0) return false;
        if (failed_) return false;
        if (frames == 0) return true;

        const size_t count = static_cast<size_t>(frames) * static_cast<size_t>(channels_);
        converter_.convertToInt32(interleavedIn, count, i32Buf_);
        static_assert(sizeof(FLAC__int32) == sizeof(int32_t), "FLAC__int32 must match int32_t");
        if (!FLAC__stream_encoder_process_interleaved(
                enc_, reinterpret_cast<const FLAC__int32*>(i32Buf_.data()), static_cast<unsigned>(frames))) {
            failed_ = true;
            return false;
        }
        return true;
    }

    bool close() override {
        if (!enc_) return false;
        bool ok = !failed_;
        if (ok && !FLAC__stream_encoder_finish(enc_)) ok = false;
        FLAC__stream_encoder_delete(enc_);
        enc_ = nullptr;
        return ok;
    }

    void discard() noexcept override {
        if (!enc_) return;
        FLAC__stream_encoder_delete(enc_);
        enc_ = nullptr;
    }

    bool isOpen() const noexcept override { return enc_ != nullptr; }

  private:
    FLAC__StreamEncoder* enc_ = nullptr;
    int32_t channels_ = 2;
    bool failed_ = false;
    PcmDitherConverter converter_;
    std::vector<int32_t> i32Buf_;
};

} // namespace

void registerFlacCodec(AudioCodecRegistry& reg) {
    AudioCodecDescriptor desc;
    desc.formatId = "flac";
    desc.displayName = "FLAC (Free Lossless Audio Codec)";
    desc.extensions = {"flac"};
    desc.canDecode = true;
    desc.canEncode = true;
    reg.registerDecoder(desc, [] { return std::make_unique<FlacDecoder>(); });
    reg.registerEncoder(desc, [] { return std::make_unique<FlacEncoder>(); });
}

} // namespace nota::media::detail
