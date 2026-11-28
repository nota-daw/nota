// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "codecs/BuiltinCodecs.h"
#include "nota/media/AudioCodecRegistry.h"
#include <string>
#include <memory>
#include "nota/media/IAudioEncoder.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/AudioCodecTypes.h"
#ifdef _WIN32
#include "codecs/PathUtils.h"
#endif

#define DR_MP3_IMPLEMENTATION
#include <dr_mp3.h>
#include <lame.h>

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <vector>

namespace nota::media::detail {

namespace {

class Mp3Decoder final : public IAudioDecoder {
  public:
    ~Mp3Decoder() override { close(); }

    bool open(const std::string& path) override {
        close();
#ifdef _WIN32
        if (!drmp3_init_file_w(&mp3_, widePath(path).c_str(), nullptr)) return false;
#else
        if (!drmp3_init_file(&mp3_, path.c_str(), nullptr)) return false;
#endif
        open_ = true;
        info_.channels = static_cast<int32_t>(mp3_.channels);
        info_.sampleRate = static_cast<double>(mp3_.sampleRate);
        info_.totalFrames = static_cast<int64_t>(drmp3_get_pcm_frame_count(&mp3_));
        drmp3_seek_to_pcm_frame(&mp3_, 0);
        info_.nativeFormat = SampleFormat::Float32;
        info_.seekable = info_.totalFrames > 0;
        info_.codecId = "mp3";
        return info_.valid();
    }

    void close() noexcept override {
        if (!open_) return;
        drmp3_uninit(&mp3_);
        open_ = false;
        info_ = {};
    }

    bool isOpen() const noexcept override { return open_; }
    AudioStreamInfo info() const noexcept override { return info_; }

    bool seekToFrame(int64_t frame) override {
        if (!open_) return false;
        return drmp3_seek_to_pcm_frame(&mp3_, static_cast<drmp3_uint64>(std::max<int64_t>(0, frame))) == DRMP3_TRUE;
    }

    int64_t readFrames(int64_t maxFrames, float* interleavedOut) override {
        if (!open_ || !interleavedOut || maxFrames <= 0) return 0;
        return static_cast<int64_t>(
            drmp3_read_pcm_frames_f32(&mp3_, static_cast<drmp3_uint64>(maxFrames), interleavedOut));
    }

  private:
    drmp3 mp3_{};
    bool open_ = false;
    AudioStreamInfo info_{};
};

class Mp3Encoder final : public IAudioEncoder {
  public:
    ~Mp3Encoder() override { discard(); }

    bool open(const std::string& path, const AudioEncodeConfig& config) override {
        discard();
        if (path.empty() || config.channels != 2 || (config.sampleRate != 44100 && config.sampleRate != 48000) ||
            (config.bitrateKbps != 128 && config.bitrateKbps != 192 && config.bitrateKbps != 256 &&
                config.bitrateKbps != 320))
            return false;

        file_ = std::fopen(path.c_str(), "wb");
        if (!file_) return false;

        gfp_ = lame_init();
        if (!gfp_) {
            std::fclose(file_);
            file_ = nullptr;
            return false;
        }
        lame_set_num_channels(gfp_, config.channels);
        lame_set_in_samplerate(gfp_, config.sampleRate);
        lame_set_out_samplerate(gfp_, config.sampleRate);
        lame_set_brate(gfp_, config.bitrateKbps);
        if (lame_init_params(gfp_) < 0) {
            discard();
            return false;
        }

        mp3Buf_.resize(1 << 16);
        failed_ = false;
        return true;
    }

    bool writeFrames(const float* interleavedIn, int32_t frames) override {
        if (!gfp_ || !file_ || !interleavedIn || frames < 0) return false;
        if (failed_) return false;
        if (frames == 0) return true;

        const size_t minCap = static_cast<size_t>(frames) * 5 / 4 + 7200;
        if (mp3Buf_.size() < minCap) mp3Buf_.resize(minCap);

        const int n = lame_encode_buffer_interleaved_ieee_float(
            gfp_, interleavedIn, frames, mp3Buf_.data(), static_cast<int>(mp3Buf_.size()));
        if (n < 0 ||
            (n > 0 && std::fwrite(mp3Buf_.data(), 1, static_cast<size_t>(n), file_) != static_cast<size_t>(n))) {
            failed_ = true;
            return false;
        }
        return true;
    }

    bool close() override {
        if (!gfp_ && !file_) return false;
        bool ok = !failed_;
        if (ok && gfp_ && file_) {
            const int n = lame_encode_flush(gfp_, mp3Buf_.data(), static_cast<int>(mp3Buf_.size()));
            if (n < 0 ||
                (n > 0 && std::fwrite(mp3Buf_.data(), 1, static_cast<size_t>(n), file_) != static_cast<size_t>(n)))
                ok = false;
        }
        if (file_) {
            if (std::fclose(file_) != 0 && ok) ok = false;
            file_ = nullptr;
        }
        if (gfp_) {
            lame_close(gfp_);
            gfp_ = nullptr;
        }
        return ok;
    }

    void discard() noexcept override {
        if (gfp_) {
            lame_close(gfp_);
            gfp_ = nullptr;
        }
        if (file_) {
            std::fclose(file_);
            file_ = nullptr;
        }
    }

    bool isOpen() const noexcept override { return gfp_ != nullptr && file_ != nullptr; }

  private:
    lame_global_flags* gfp_ = nullptr;
    FILE* file_ = nullptr;
    std::vector<unsigned char> mp3Buf_;
    bool failed_ = false;
};

} // namespace

void registerMp3Codec(AudioCodecRegistry& reg) {
    AudioCodecDescriptor desc;
    desc.formatId = "mp3";
    desc.displayName = "MP3 (MPEG Layer-3)";
    desc.extensions = {"mp3"};
    desc.canDecode = true;
    desc.canEncode = true;
    reg.registerDecoder(desc, [] { return std::make_unique<Mp3Decoder>(); });
    reg.registerEncoder(desc, [] { return std::make_unique<Mp3Encoder>(); });
}

} // namespace nota::media::detail
