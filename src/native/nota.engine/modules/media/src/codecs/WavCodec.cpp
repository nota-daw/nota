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

#define DR_WAV_IMPLEMENTATION
#include <dr_wav.h>

#include <algorithm>
#include <cstdint>
#include <vector>

namespace nota::media::detail {

namespace {

SampleFormat wavFormatFromMeta(uint16_t formatTag, uint16_t bitsPerSample) noexcept {
    if (formatTag == DR_WAVE_FORMAT_IEEE_FLOAT && bitsPerSample == 32) return SampleFormat::Float32;
    if (bitsPerSample == 16) return SampleFormat::Pcm16;
    if (bitsPerSample == 24) return SampleFormat::Pcm24;
    if (bitsPerSample == 32) return SampleFormat::Pcm32;
    return SampleFormat::Unknown;
}

class WavDecoder final : public IAudioDecoder {
  public:
    ~WavDecoder() override { close(); }

    bool open(const std::string& path) override {
        close();
#ifdef _WIN32
        if (!drwav_init_file_w(&wav_, widePath(path).c_str(), nullptr)) return false;
#else
        if (!drwav_init_file(&wav_, path.c_str(), nullptr)) return false;
#endif
        open_ = true;
        info_.channels = static_cast<int32_t>(wav_.channels);
        info_.sampleRate = static_cast<double>(wav_.sampleRate);
        info_.totalFrames = static_cast<int64_t>(wav_.totalPCMFrameCount);
        info_.nativeFormat = wavFormatFromMeta(wav_.translatedFormatTag, wav_.bitsPerSample);
        info_.seekable = info_.totalFrames > 0;
        info_.codecId = "wav";
        return info_.valid();
    }

    void close() noexcept override {
        if (!open_) return;
        drwav_uninit(&wav_);
        open_ = false;
        info_ = {};
    }

    bool isOpen() const noexcept override { return open_; }
    AudioStreamInfo info() const noexcept override { return info_; }

    bool seekToFrame(int64_t frame) override {
        if (!open_) return false;
        return drwav_seek_to_pcm_frame(&wav_, static_cast<drwav_uint64>(std::max<int64_t>(0, frame))) == DRWAV_TRUE;
    }

    int64_t readFrames(int64_t maxFrames, float* interleavedOut) override {
        if (!open_ || !interleavedOut || maxFrames <= 0) return 0;
        return static_cast<int64_t>(
            drwav_read_pcm_frames_f32(&wav_, static_cast<drwav_uint64>(maxFrames), interleavedOut));
    }

  private:
    drwav wav_{};
    bool open_ = false;
    AudioStreamInfo info_{};
};

class WavEncoder final : public IAudioEncoder {
  public:
    ~WavEncoder() override { discard(); }

    bool open(const std::string& path, const AudioEncodeConfig& config) override {
        discard();
        if (path.empty() || config.sampleRate <= 0 || config.channels <= 0 ||
            (config.bitsPerSample != 16 && config.bitsPerSample != 24 && config.bitsPerSample != 32))
            return false;

        drwav_data_format fmt{};
        fmt.container = drwav_container_riff;
        fmt.format = (config.bitsPerSample == 32) ? DR_WAVE_FORMAT_IEEE_FLOAT : DR_WAVE_FORMAT_PCM;
        fmt.channels = static_cast<drwav_uint32>(config.channels);
        fmt.sampleRate = static_cast<drwav_uint32>(config.sampleRate);
        fmt.bitsPerSample = static_cast<drwav_uint32>(config.bitsPerSample);

#ifdef _WIN32
        if (!drwav_init_file_write_w(&wav_, widePath(path).c_str(), &fmt, nullptr)) return false;
#else
        if (!drwav_init_file_write(&wav_, path.c_str(), &fmt, nullptr)) return false;
#endif
        channels_ = config.channels;
        bits_ = config.bitsPerSample;
        converter_.reset(config.bitsPerSample, config.dither);
        open_ = true;
        failed_ = false;
        return true;
    }

    bool writeFrames(const float* interleavedIn, int32_t frames) override {
        if (!open_ || !interleavedIn || frames < 0) return false;
        if (failed_) return false;
        if (frames == 0) return true;

        const size_t count = static_cast<size_t>(frames) * static_cast<size_t>(channels_);
        drwav_uint64 written = 0;
        if (bits_ == 32) {
            written = drwav_write_pcm_frames(&wav_, static_cast<drwav_uint64>(frames), interleavedIn);
        } else if (bits_ == 16) {
            converter_.convertToInt16(interleavedIn, count, i16Buf_);
            written = drwav_write_pcm_frames(&wav_, static_cast<drwav_uint64>(frames), i16Buf_.data());
        } else {
            converter_.convertToPacked24Le(interleavedIn, count, packed24_);
            written = drwav_write_pcm_frames(&wav_, static_cast<drwav_uint64>(frames), packed24_.data());
        }
        if (written != static_cast<drwav_uint64>(frames)) {
            failed_ = true;
            return false;
        }
        return true;
    }

    bool close() override {
        if (!open_) return false;
        const bool ok = !failed_;
        drwav_uninit(&wav_);
        open_ = false;
        return ok;
    }

    void discard() noexcept override {
        if (!open_) return;
        drwav_uninit(&wav_);
        open_ = false;
    }

    bool isOpen() const noexcept override { return open_; }

  private:
    drwav wav_{};
    bool open_ = false;
    bool failed_ = false;
    int32_t channels_ = 2;
    int32_t bits_ = 24;
    PcmDitherConverter converter_;
    std::vector<int16_t> i16Buf_;
    std::vector<uint8_t> packed24_;
};

} // namespace

void registerWavCodec(AudioCodecRegistry& reg) {
    AudioCodecDescriptor desc;
    desc.formatId = "wav";
    desc.displayName = "WAV (PCM / IEEE Float)";
    desc.extensions = {"wav"};
    desc.canDecode = true;
    desc.canEncode = true;
    reg.registerDecoder(desc, [] { return std::make_unique<WavDecoder>(); });
    reg.registerEncoder(desc, [] { return std::make_unique<WavEncoder>(); });
}

} // namespace nota::media::detail
