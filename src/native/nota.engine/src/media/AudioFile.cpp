// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AudioFile.h"
#include "StreamingAudioSource.h"

#define DR_WAV_IMPLEMENTATION
#define DR_FLAC_IMPLEMENTATION
#define DR_MP3_IMPLEMENTATION
#include <dr_wav.h>
#include <dr_flac.h>
#include <dr_mp3.h>

#include <algorithm>
#include <cctype>
#include <vector>
#ifdef _WIN32
#include <filesystem>
#endif

namespace nota {

namespace {

std::string extLower(const std::string& path) {
    auto dot = path.find_last_of('.');
    if (dot == std::string::npos) return {};
    std::string ext = path.substr(dot + 1);
    std::transform(
        ext.begin(), ext.end(), ext.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return ext;
}

#ifdef _WIN32
// Paths arrive as UTF-8; on Windows every open goes through widePath (#974).
// Read a decoder to its end in blocks (a FLAC stream may not know its length up front).
template <typename ReadFn>
std::vector<float> readAll(ReadFn read, int32_t channels) {
    std::vector<float> out;
    constexpr uint64_t kBlock = 16384;
    std::vector<float> block(static_cast<size_t>(kBlock) * static_cast<size_t>(channels));
    for (;;) {
        const uint64_t got = read(kBlock, block.data());
        if (got == 0) break;
        out.insert(out.end(), block.begin(), block.begin() + static_cast<ptrdiff_t>(got * static_cast<uint64_t>(channels)));
        if (got < kBlock) break;
    }
    return out;
}
#endif

std::shared_ptr<SampleBuffer> decodeWav(const std::string& path) {
    unsigned int channels = 0, sampleRate = 0;
    drwav_uint64 frameCount = 0;
#ifdef _WIN32
    float* data = drwav_open_file_and_read_pcm_frames_f32_w(widePath(path).c_str(), &channels, &sampleRate, &frameCount, nullptr);
#else
    float* data = drwav_open_file_and_read_pcm_frames_f32(path.c_str(), &channels, &sampleRate, &frameCount, nullptr);
#endif
    if (!data) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(channels);
    buf->frames = static_cast<int64_t>(frameCount);
    buf->sourceSampleRate = sampleRate;
    buf->samples.assign(data, data + frameCount * channels);
    drwav_free(data, nullptr);
    return buf;
}

std::shared_ptr<SampleBuffer> decodeFlac(const std::string& path) {
#ifdef _WIN32
    drflac* flac = drflac_open_file_w(widePath(path).c_str(), nullptr);
    if (!flac) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(flac->channels);
    buf->sourceSampleRate = flac->sampleRate;
    buf->samples = readAll(
        [flac](uint64_t n, float* dst) { return static_cast<uint64_t>(drflac_read_pcm_frames_f32(flac, n, dst)); },
        buf->channels);
    drflac_close(flac);
    if (buf->channels <= 0 || buf->samples.empty()) return nullptr;
    buf->frames = static_cast<int64_t>(buf->samples.size() / static_cast<size_t>(buf->channels));
    return buf;
#else
    unsigned int channels = 0, sampleRate = 0;
    drflac_uint64 frameCount = 0;
    float* data = drflac_open_file_and_read_pcm_frames_f32(path.c_str(), &channels, &sampleRate, &frameCount, nullptr);
    if (!data) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(channels);
    buf->frames = static_cast<int64_t>(frameCount);
    buf->sourceSampleRate = sampleRate;
    buf->samples.assign(data, data + frameCount * channels);
    drflac_free(data, nullptr);
    return buf;
#endif
}

std::shared_ptr<SampleBuffer> decodeMp3(const std::string& path) {
#ifdef _WIN32
    drmp3 mp3;
    if (!drmp3_init_file_w(&mp3, widePath(path).c_str(), nullptr)) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(mp3.channels);
    buf->sourceSampleRate = mp3.sampleRate;
    buf->samples = readAll(
        [&mp3](uint64_t n, float* dst) { return static_cast<uint64_t>(drmp3_read_pcm_frames_f32(&mp3, n, dst)); },
        buf->channels);
    drmp3_uninit(&mp3);
    if (buf->channels <= 0 || buf->samples.empty()) return nullptr;
    buf->frames = static_cast<int64_t>(buf->samples.size() / static_cast<size_t>(buf->channels));
    return buf;
#else
    drmp3_config cfg{};
    drmp3_uint64 frameCount = 0;
    float* data = drmp3_open_file_and_read_pcm_frames_f32(path.c_str(), &cfg, &frameCount, nullptr);
    if (!data) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(cfg.channels);
    buf->frames = static_cast<int64_t>(frameCount);
    buf->sourceSampleRate = cfg.sampleRate;
    buf->samples.assign(data, data + frameCount * cfg.channels);
    drmp3_free(data, nullptr);
    return buf;
#endif
}

} // namespace

#ifdef _WIN32
// dr_libs open a char* path with fopen_s, which Windows reads in the ANSI code page, so any
// file under a folder or name with an accent (Música, Canción, ñ…) failed to open (#974).
std::wstring widePath(const std::string& utf8) {
    return std::filesystem::path(reinterpret_cast<const char8_t*>(utf8.c_str())).wstring();
}
#endif

std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path, const AudioDecodeOptions& opts) {
    if (opts.policy != SampleLoadPolicy::MemoryOnly) {
        int32_t ch = 0;
        int64_t frames = 0;
        double sr = 0.0;
        if (StreamingAudioSource::probeHeader(path, ch, frames, sr)) {
            const int64_t threshold = std::max<int64_t>(0, opts.streamThresholdBytes);
            const int64_t estimatedBytes = frames * static_cast<int64_t>(ch) * static_cast<int64_t>(sizeof(float));
            const bool wantStream = (opts.policy == SampleLoadPolicy::StreamAlways) ||
                                    (opts.policy == SampleLoadPolicy::Auto && threshold > 0 && estimatedBytes > threshold);
            if (wantStream) {
                auto buf = std::make_shared<SampleBuffer>();
                auto src = StreamingAudioSource::open(path, &buf->peakTable, &buf->rmsTable);
                if (src) {
                    buf->channels = src->channels();
                    buf->frames = src->frames();
                    buf->sourceSampleRate = src->sampleRate();
                    buf->source = std::move(src);
                    return buf;
                }
            }
        }
    }

    const std::string ext = extLower(path);
    std::shared_ptr<SampleBuffer> buf;
    if (ext == "wav") buf = decodeWav(path);
    else if (ext == "flac")
        buf = decodeFlac(path);
    else if (ext == "mp3")
        buf = decodeMp3(path);
    // aiff and others: deferred
    if (buf) buf->buildPeakTable(); // one pass now; every later waveform query reads blocks
    return buf;
}

std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path) {
    return decodeAudioFile(path, AudioDecodeOptions{SampleLoadPolicy::MemoryOnly, kDefaultStreamThresholdBytes});
}

} // namespace nota
