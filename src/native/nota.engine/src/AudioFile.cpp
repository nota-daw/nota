// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AudioFile.h"

#define DR_WAV_IMPLEMENTATION
#define DR_FLAC_IMPLEMENTATION
#define DR_MP3_IMPLEMENTATION
#include "dr_wav.h"
#include "dr_flac.h"
#include "dr_mp3.h"

#include <algorithm>
#include <cctype>

namespace nota {

namespace {

std::string extLower(const std::string& path) {
    auto dot = path.find_last_of('.');
    if (dot == std::string::npos) return {};
    std::string ext = path.substr(dot + 1);
    std::transform(ext.begin(), ext.end(), ext.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return ext;
}

std::shared_ptr<SampleBuffer> decodeWav(const std::string& path) {
    unsigned int channels = 0, sampleRate = 0;
    drwav_uint64 frameCount = 0;
    float* data = drwav_open_file_and_read_pcm_frames_f32(
        path.c_str(), &channels, &sampleRate, &frameCount, nullptr);
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
    unsigned int channels = 0, sampleRate = 0;
    drflac_uint64 frameCount = 0;
    float* data = drflac_open_file_and_read_pcm_frames_f32(
        path.c_str(), &channels, &sampleRate, &frameCount, nullptr);
    if (!data) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(channels);
    buf->frames = static_cast<int64_t>(frameCount);
    buf->sourceSampleRate = sampleRate;
    buf->samples.assign(data, data + frameCount * channels);
    drflac_free(data, nullptr);
    return buf;
}

std::shared_ptr<SampleBuffer> decodeMp3(const std::string& path) {
    drmp3_config cfg{};
    drmp3_uint64 frameCount = 0;
    float* data = drmp3_open_file_and_read_pcm_frames_f32(
        path.c_str(), &cfg, &frameCount, nullptr);
    if (!data) return nullptr;
    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = static_cast<int32_t>(cfg.channels);
    buf->frames = static_cast<int64_t>(frameCount);
    buf->sourceSampleRate = cfg.sampleRate;
    buf->samples.assign(data, data + frameCount * cfg.channels);
    drmp3_free(data, nullptr);
    return buf;
}

} // namespace

std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path) {
    const std::string ext = extLower(path);
    if (ext == "wav")  return decodeWav(path);
    if (ext == "flac") return decodeFlac(path);
    if (ext == "mp3")  return decodeMp3(path);
    return nullptr; // aiff and others: deferred
}

} // namespace nota
