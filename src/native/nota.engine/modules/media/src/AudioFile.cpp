// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/media/AudioFile.h"
#include "nota/media/AudioCodecRegistry.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/SampleBuffer.h"
#include "nota/media/StreamingAudioSource.h"

#include <algorithm>
#include <utility>
#include <string>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <vector>

namespace nota::media {

bool probeAudioFile(const std::string& path, AudioStreamInfo& outInfo) {
    auto dec = AudioCodecRegistry::instance().openDecoder(path);
    if (!dec) return false;
    outInfo = dec->info();
    return outInfo.valid();
}

std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path, const AudioDecodeOptions& opts) {
    if (opts.policy != SampleLoadPolicy::MemoryOnly) {
        int32_t ch = 0;
        int64_t frames = 0;
        double sr = 0.0;
        if (StreamingAudioSource::probeHeader(path, ch, frames, sr)) {
            const int64_t threshold = std::max<int64_t>(0, opts.streamThresholdBytes);
            const int64_t estimatedBytes = frames * static_cast<int64_t>(ch) * static_cast<int64_t>(sizeof(float));
            const bool wantStream =
                (opts.policy == SampleLoadPolicy::StreamAlways) ||
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

    auto dec = AudioCodecRegistry::instance().openDecoder(path);
    if (!dec) return nullptr;
    const AudioStreamInfo info = dec->info();
    if (!info.valid()) return nullptr;

    auto buf = std::make_shared<SampleBuffer>();
    buf->channels = info.channels;
    buf->sourceSampleRate = info.sampleRate;

    if (info.totalFrames > 0) {
        buf->frames = info.totalFrames;
        buf->samples.resize(static_cast<size_t>(info.totalFrames) * static_cast<size_t>(info.channels));
        int64_t readTotal = 0;
        while (readTotal < info.totalFrames) {
            const int64_t got = dec->readFrames(
                info.totalFrames - readTotal, buf->samples.data() + readTotal * static_cast<int64_t>(info.channels));
            if (got <= 0) break;
            readTotal += got;
        }
        if (readTotal <= 0) return nullptr;
        if (readTotal < info.totalFrames) {
            buf->frames = readTotal;
            buf->samples.resize(static_cast<size_t>(readTotal) * static_cast<size_t>(info.channels));
        }
    } else {
        constexpr int64_t kBlock = 16384;
        std::vector<float> block(static_cast<size_t>(kBlock) * static_cast<size_t>(info.channels));
        for (;;) {
            const int64_t got = dec->readFrames(kBlock, block.data());
            if (got <= 0) break;
            buf->samples.insert(buf->samples.end(), block.begin(),
                block.begin() + static_cast<ptrdiff_t>(got * static_cast<int64_t>(info.channels)));
            if (got < kBlock) break;
        }
        if (buf->samples.empty()) return nullptr;
        buf->frames = static_cast<int64_t>(buf->samples.size() / static_cast<size_t>(info.channels));
    }

    buf->buildPeakTable();
    return buf;
}

std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path) {
    return decodeAudioFile(path, AudioDecodeOptions{SampleLoadPolicy::MemoryOnly, kDefaultStreamThresholdBytes});
}

} // namespace nota::media
