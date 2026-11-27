// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/media/AudioImport.h"
#include "nota/media/AudioCodecRegistry.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/media/AudioFile.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/SampleBuffer.h"
#include "nota/media/StreamingAudioSource.h"
#include "nota/media/TempoDetect.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota::media {

AudioImportJob::AudioImportJob(PrivateTag) {}
AudioImportJob::~AudioImportJob() = default;

std::unique_ptr<AudioImportJob> AudioImportJob::open(
    const std::string& path, double maxSeconds, const AudioDecodeOptions& opts) {
    auto dec = AudioCodecRegistry::instance().openDecoder(path);
    if (!dec) return nullptr;
    const AudioStreamInfo info = dec->info();
    if (!info.valid()) return nullptr;

    auto job = std::make_unique<AudioImportJob>(PrivateTag{});
    job->buf_ = std::make_shared<SampleBuffer>();
    job->buf_->sourceSampleRate = info.sampleRate;
    job->path_ = path;

    if (info.totalFrames <= 0) {
        dec->close();
        auto whole = decodeAudioFile(path, opts);
        if (!whole || whole->empty()) return nullptr;
        job->buf_ = std::move(whole);
        job->decoded_ = job->buf_->frames;
        job->sourceFrames_ = job->buf_->frames;
        job->done_ = true;
        return job;
    }

    int64_t total = info.totalFrames;
    job->sourceFrames_ = total;
    if (maxSeconds > 0.0) {
        total = std::min<int64_t>(total, static_cast<int64_t>(std::max(1.0, maxSeconds * info.sampleRate)));
    }
    job->buf_->channels = info.channels;
    job->buf_->frames = total;

    const int64_t threshold = std::max<int64_t>(0, opts.streamThresholdBytes);
    const int64_t estimatedBytes = total * static_cast<int64_t>(info.channels) * static_cast<int64_t>(sizeof(float));
    const bool useStreaming =
        info.seekable && (maxSeconds <= 0.0) &&
        ((opts.policy == SampleLoadPolicy::StreamAlways) ||
            (opts.policy == SampleLoadPolicy::Auto && threshold > 0 && estimatedBytes > threshold));
    job->streaming_ = useStreaming;
    if (useStreaming) {
        const int64_t nBlocks = job->buf_->peakBlocks();
        job->buf_->peakTable.assign(static_cast<size_t>(nBlocks * 2), 0.0f);
        job->buf_->rmsTable.assign(static_cast<size_t>(nBlocks), 0.0f);
        job->buf_->fillSentinel(0, nBlocks);
        job->streamCarry_.assign(
            static_cast<size_t>(SampleBuffer::kPeakBlock) * static_cast<size_t>(info.channels), 0.0f);
    } else {
        job->buf_->samples.assign(static_cast<size_t>(total) * static_cast<size_t>(info.channels), 0.0f);
        job->buf_->updatePeakTable(0, 0);
    }
    job->dec_ = std::move(dec);
    return job;
}

int32_t AudioImportJob::step(int64_t maxFrames) {
    if (done_) return 0;
    if (!dec_ || !buf_) return -1;
    SampleBuffer& b = *buf_;
    const int64_t want = std::min<int64_t>(std::max<int64_t>(1, maxFrames), b.frames - decoded_);
    const int64_t before = decoded_;
    int64_t got = 0;

    if (streaming_) {
        const size_t ch = static_cast<size_t>(b.channels);
        const size_t needSize = static_cast<size_t>(want + streamCarryFrames_) * ch;
        if (streamScratch_.size() < needSize) streamScratch_.resize(needSize);
        if (streamCarryFrames_ > 0) {
            std::copy_n(streamCarry_.data(), static_cast<size_t>(streamCarryFrames_) * ch, streamScratch_.data());
        }
        got = dec_->readFrames(want, streamScratch_.data() + static_cast<size_t>(streamCarryFrames_) * ch);
        decoded_ += got;

        const int64_t blockStartFrame = before - streamCarryFrames_;
        const int64_t totalChunkFrames = streamCarryFrames_ + got;
        const int64_t fullBlocks = totalChunkFrames / SampleBuffer::kPeakBlock;
        if (!seeded_) {
            for (int64_t bi = 0; bi < fullBlocks; ++bi) {
                const int64_t blkIdx = blockStartFrame / SampleBuffer::kPeakBlock + bi;
                if (blkIdx < 0 || blkIdx >= b.peakBlocks()) continue;
                const float* ptr = streamScratch_.data() + static_cast<size_t>(bi * SampleBuffer::kPeakBlock) * ch;
                float mn = 0.0f;
                float mx = 0.0f;
                float meanSq = 0.0f;
                SampleBuffer::computeBlockPeakAndMeanSq(ptr, b.channels, SampleBuffer::kPeakBlock, mn, mx, meanSq);
                b.peakTable[static_cast<size_t>(blkIdx) * 2] = mn;
                b.peakTable[static_cast<size_t>(blkIdx) * 2 + 1] = mx;
                b.rmsTable[static_cast<size_t>(blkIdx)] = meanSq;
            }
        }
        const int64_t rem = totalChunkFrames - fullBlocks * SampleBuffer::kPeakBlock;
        if (rem > 0) {
            std::copy_n(streamScratch_.data() + static_cast<size_t>(fullBlocks * SampleBuffer::kPeakBlock) * ch,
                static_cast<size_t>(rem) * ch, streamCarry_.data());
        }
        streamCarryFrames_ = rem;
    } else {
        got = dec_->readFrames(want, b.samples.data() + decoded_ * b.channels);
        decoded_ += got;
        if (!seeded_) b.updatePeakTable(before, decoded_);
    }

    if (got < want || decoded_ >= b.frames) finish();
    return done_ ? 0 : 1;
}

void AudioImportJob::finish() {
    SampleBuffer& b = *buf_;
    if (streaming_) {
        if (decoded_ < b.frames) b.frames = decoded_;
        if (!seeded_ && streamCarryFrames_ > 0 && b.peakBlocks() > 0) {
            const int64_t lastBlk = b.peakBlocks() - 1;
            float mn = 0.0f;
            float mx = 0.0f;
            float meanSq = 0.0f;
            SampleBuffer::computeBlockPeakAndMeanSq(
                streamCarry_.data(), b.channels, streamCarryFrames_, mn, mx, meanSq);
            b.peakTable[static_cast<size_t>(lastBlk) * 2] = mn;
            b.peakTable[static_cast<size_t>(lastBlk) * 2 + 1] = mx;
            b.rmsTable[static_cast<size_t>(lastBlk)] = meanSq;
        }
        streamCarry_.clear();
        streamCarry_.shrink_to_fit();
        streamScratch_.clear();
        streamScratch_.shrink_to_fit();
        if (dec_) dec_->close();
        dec_.reset();
        b.source = StreamingAudioSource::open(path_, seeded_ ? &b.peakTable : nullptr, seeded_ ? &b.rmsTable : nullptr);
        done_ = true;
        return;
    }
    if (decoded_ < b.frames) {
        b.frames = decoded_;
        b.samples.resize(static_cast<size_t>(decoded_) * static_cast<size_t>(b.channels));
        b.peakTable.clear();
        b.buildPeakTable();
    } else if (seeded_) {
        b.buildPeakTable();
    }
    if (dec_) dec_->close();
    dec_.reset();
    done_ = true;
}

int32_t AudioImportJob::peaks(float* out, int32_t maxPoints) const {
    if (!out || maxPoints <= 0 || !buf_) return 0;
    const SampleBuffer& b = *buf_;
    if (b.frames <= 0) return 0;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, b.frames));
    const bool fromTable = (seeded_ || streaming_) && !done_;
    const int64_t ready = done_ ? b.frames : decoded_;
    for (int32_t i = 0; i < buckets; ++i) {
        const int64_t f0 = b.frames * i / buckets;
        const int64_t f1 = b.frames * (i + 1) / buckets;
        float mn = 1.0f;
        float mx = -1.0f;
        if (fromTable) {
            if (f0 < ready || seeded_) {
                const int64_t b1 = std::max(
                    f0 / SampleBuffer::kPeakBlock + 1, (f1 + SampleBuffer::kPeakBlock - 1) / SampleBuffer::kPeakBlock);
                for (int64_t k = f0 / SampleBuffer::kPeakBlock; k < b1 && k < b.peakBlocks(); ++k) {
                    mn = std::min(mn, b.peakTable[static_cast<size_t>(k) * 2]);
                    mx = std::max(mx, b.peakTable[static_cast<size_t>(k) * 2 + 1]);
                }
            }
        } else if (f0 < ready) {
            b.peakRange(f0, std::min(f1, ready), mn, mx);
        }
        out[i * 2] = mn;
        out[i * 2 + 1] = mx;
    }
    return buckets;
}

bool AudioImportJob::seedPeakTable(const float* table, int64_t count) {
    if (!table || !buf_ || done_) return false;
    SampleBuffer& b = *buf_;
    if (count != b.peakBlocks() * 2) return false;
    b.peakTable.assign(table, table + count);
    seeded_ = true;
    return true;
}

double AudioImportJob::detectTempo() const {
    if (!done_ || !buf_ || buf_->empty()) return 0.0;
    return nota::media::detectTempo(*buf_, 0, buf_->frames, buf_->sourceSampleRate);
}

} // namespace nota::media
