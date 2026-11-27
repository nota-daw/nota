// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/media/AudioCodecRegistry.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/media/AudioFile.h"
#include "nota/media/AudioImport.h"
#include "nota/media/SampleAnalysis.h"
#include "nota/media/StreamingAudioSource.h"
#include "nota/media/Warp.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <memory>
#include <numbers>
#include <string>
#include <vector>

namespace {

#define CHECK(cond)                                                                                                    \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);                                       \
            return 1;                                                                                                  \
        }                                                                                                              \
    } while (0)

class MockDecoder final : public nota::media::IAudioDecoder {
  public:
    bool open(const std::string& path) override {
        open_ = !path.empty();
        pos_ = 0;
        info_ = {2, 48000.0, 4800, nota::media::SampleFormat::Float32, true, "mockfmt"};
        return open_;
    }
    void close() noexcept override { open_ = false; }
    bool isOpen() const noexcept override { return open_; }
    nota::media::AudioStreamInfo info() const noexcept override { return info_; }
    bool seekToFrame(int64_t frame) override {
        pos_ = std::clamp<int64_t>(frame, 0, info_.totalFrames);
        return true;
    }
    int64_t readFrames(int64_t maxFrames, float* out) override {
        if (!open_ || !out || maxFrames <= 0) return 0;
        const int64_t n = std::min(maxFrames, info_.totalFrames - pos_);
        for (int64_t i = 0; i < n; ++i) {
            out[i * 2] = 0.25f;
            out[i * 2 + 1] = -0.25f;
        }
        pos_ += n;
        return n;
    }

  private:
    bool open_ = false;
    int64_t pos_ = 0;
    nota::media::AudioStreamInfo info_{};
};

int testRegistryAndMockDecoder() {
    auto& reg = nota::media::AudioCodecRegistry::instance();
    CHECK(reg.supportsDecodeExtension(".WAV"));
    CHECK(reg.supportsDecodeExtension("song.flac"));
    CHECK(reg.supportsDecodeExtension("mp3"));
    CHECK(reg.supportsEncodeFormat("wav"));
    CHECK(reg.supportsEncodeFormat("flac"));
    CHECK(reg.supportsEncodeFormat("mp3"));
    CHECK(!reg.supportsDecodeExtension("ogg"));

    nota::media::AudioCodecDescriptor mockDesc;
    mockDesc.formatId = "mockfmt";
    mockDesc.displayName = "Mock Test Format";
    mockDesc.extensions = {"mck"};
    mockDesc.canDecode = true;
    reg.registerDecoder(mockDesc, [] { return std::make_unique<MockDecoder>(); });
    CHECK(reg.supportsDecodeExtension("path/to/AUDIO.MCK"));

    auto buf = nota::media::decodeAudioFile("/virtual/sample.MCK");
    CHECK(buf != nullptr);
    CHECK(buf->frames == 4800);
    CHECK(buf->channels == 2);
    CHECK(std::fabs(buf->at(10, 0) - 0.25f) < 1e-6f);
    CHECK(std::fabs(buf->at(10, 1) + 0.25f) < 1e-6f);
    return 0;
}

std::vector<float> makeMusicalTestSignal(double sr, double seconds, double bpm) {
    const int64_t frames = static_cast<int64_t>(sr * seconds);
    const int64_t beatSamples = static_cast<int64_t>(sr * 60.0 / bpm);
    std::vector<float> pcm(static_cast<size_t>(frames) * 2, 0.0f);
    const float twoPi = 2.0f * std::numbers::pi_v<float>;
    for (int64_t i = 0; i < frames; ++i) {
        const float t = static_cast<float>(static_cast<double>(i) / sr);
        const int64_t inBeat = i % beatSamples;
        const float beatPhase = static_cast<float>(inBeat) / static_cast<float>(sr);
        const float chordEnv = std::exp(-beatPhase * 8.0f);
        const float hitEnv = inBeat < static_cast<int64_t>(sr * 0.015)
                                 ? (1.0f - static_cast<float>(inBeat) / static_cast<float>(sr * 0.015))
                                 : 0.0f;
        const float chord = chordEnv * (0.25f * std::sin(twoPi * 261.63f * t) + 0.20f * std::sin(twoPi * 329.63f * t) +
                                           0.18f * std::sin(twoPi * 392.00f * t));
        const float hit = 0.35f * hitEnv * std::sin(twoPi * 110.0f * t);
        const float sig = chord + hit;
        pcm[static_cast<size_t>(i) * 2] = sig * 0.85f;
        pcm[static_cast<size_t>(i) * 2 + 1] = sig * 0.65f;
    }
    return pcm;
}

int testCodecsRoundTripImportStreamAnalysisAndWarp() {
    namespace fs = std::filesystem;
    const fs::path tmpDir = fs::temp_directory_path() / "nota_test_media_suite";
    fs::create_directories(tmpDir);

    constexpr int32_t kSr = 44100;
    const auto stereo = makeMusicalTestSignal(kSr, 6.0, 120.0);
    const int32_t totalFrames = static_cast<int32_t>(stereo.size() / 2);

    auto& reg = nota::media::AudioCodecRegistry::instance();

    const fs::path wavPath = tmpDir / "test_24.wav";
    const fs::path flacPath = tmpDir / "test_16_dither.flac";
    const fs::path mp3Path = tmpDir / "test_320.mp3";

    {
        nota::media::AudioEncodeConfig cfg;
        cfg.channels = 2;
        cfg.sampleRate = kSr;
        cfg.bitsPerSample = 24;
        auto enc = reg.openEncoder("wav", wavPath.string(), cfg);
        CHECK(enc != nullptr);
        CHECK(enc->writeFrames(stereo.data(), totalFrames));
        CHECK(enc->close());
    }
    {
        nota::media::AudioEncodeConfig cfg;
        cfg.channels = 2;
        cfg.sampleRate = kSr;
        cfg.bitsPerSample = 16;
        cfg.dither = true;
        auto enc = reg.openEncoder("flac", flacPath.string(), cfg);
        CHECK(enc != nullptr);
        CHECK(enc->writeFrames(stereo.data(), totalFrames));
        CHECK(enc->close());
    }
    {
        nota::media::AudioEncodeConfig cfg;
        cfg.channels = 2;
        cfg.sampleRate = kSr;
        cfg.bitrateKbps = 320;
        auto enc = reg.openEncoder("mp3", mp3Path.string(), cfg);
        CHECK(enc != nullptr);
        CHECK(enc->writeFrames(stereo.data(), totalFrames));
        CHECK(enc->close());
    }

    auto wavBuf = nota::media::decodeAudioFile(wavPath.string());
    CHECK(wavBuf && wavBuf->frames == totalFrames && wavBuf->channels == 2);
    CHECK(wavBuf->hasPeakTable() && wavBuf->hasRmsTable());

    auto flacBuf = nota::media::decodeAudioFile(flacPath.string());
    CHECK(flacBuf && flacBuf->frames == totalFrames && flacBuf->channels == 2);

    auto mp3Buf = nota::media::decodeAudioFile(mp3Path.string());
    CHECK(mp3Buf && mp3Buf->frames > totalFrames - 4096 && mp3Buf->channels == 2);

    // Streaming round-trip
    nota::media::AudioDecodeOptions streamOpts;
    streamOpts.policy = nota::media::SampleLoadPolicy::StreamAlways;
    auto streamBuf = nota::media::decodeAudioFile(wavPath.string(), streamOpts);
    CHECK(streamBuf && streamBuf->isStreaming());
    float sl = 0.0f, sr = 0.0f;
    streamBuf->source->primeAround(kSr * 2);
    streamBuf->readStereo(kSr * 2, sl, sr);
    CHECK(std::fabs(sl - wavBuf->at(kSr * 2, 0)) < 1e-4f);

    // Incremental import job
    auto job = nota::media::AudioImportJob::open(flacPath.string());
    CHECK(job != nullptr);
    while (!job->done()) {
        const int32_t rc = job->step(16384);
        CHECK(rc >= 0);
    }
    CHECK(job->buffer() && job->decodedFrames() == totalFrames);

    // Tempo + analysis
    const double bpm = job->detectTempo();
    CHECK(bpm >= 118.0 && bpm <= 122.0);
    const auto features = nota::media::analyzeSample(*wavBuf, wavBuf->frames);
    CHECK(features.bpm >= 118.0 && features.bpm <= 122.0);
    CHECK(features.keyTonic == 0); // C

    // ClipWarpStream offline/RT render check (Complex + RePitch)
    nota::media::ClipWarpStream warp;
    warp.configure(2, nota::media::WarpMode::Complex, 0.0f, kSr, kSr, false);
    CHECK(warp.ready());
    const std::vector<nota::media::WarpMarker> markers{{0.0, 0.0}, {static_cast<double>(totalFrames), 8.0}};
    std::vector<float> warped(512 * 2, 0.0f);
    warp.render(warped.data(), 512, 0.0, markers, 8.0, *wavBuf, kSr * 0.5, kSr, 1.0f);
    float maxOut = 0.0f;
    for (float x : warped) maxOut = std::max(maxOut, std::fabs(x));
    CHECK(maxOut > 0.01f);

    // Drop shared streaming buffer before removing temp files
    streamBuf.reset();
    fs::remove_all(tmpDir);
    return 0;
}

} // namespace

int main() {
    if (testRegistryAndMockDecoder() != 0) return 1;
    if (testCodecsRoundTripImportStreamAnalysisAndWarp() != 0) return 2;
    std::printf("test_media: all checks passed\n");
    return 0;
}
