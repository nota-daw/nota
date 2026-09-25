// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AudioImport.h"
#include "AudioFile.h"
#include "TempoDetect.h"

// Declarations only — the implementations live in AudioFile.cpp.
#include "dr_wav.h"
#include "dr_flac.h"
#include "dr_mp3.h"

#include <algorithm>
#include <cctype>

namespace nota {

// One streaming decoder, chosen by extension. Each exposes the header (channels,
// rate, total frames) up front and then reads interleaved f32 frames on demand.
struct AudioImportJob::Decoder {
    enum class Kind { Wav, Flac, Mp3 } kind = Kind::Wav;
    drwav wav{};
    drflac* flac = nullptr;
    drmp3 mp3{};
    bool open = false;

    ~Decoder() { close(); }

    void close() {
        if (!open) return;
        switch (kind) {
            case Kind::Wav:  drwav_uninit(&wav); break;
            case Kind::Flac: drflac_close(flac); flac = nullptr; break;
            case Kind::Mp3:  drmp3_uninit(&mp3); break;
        }
        open = false;
    }

    uint64_t read(uint64_t frames, float* out) {
        switch (kind) {
            case Kind::Wav:  return drwav_read_pcm_frames_f32(&wav, frames, out);
            case Kind::Flac: return drflac_read_pcm_frames_f32(flac, frames, out);
            case Kind::Mp3:  return drmp3_read_pcm_frames_f32(&mp3, frames, out);
        }
        return 0;
    }
};

AudioImportJob::~AudioImportJob() = default;

std::unique_ptr<AudioImportJob> AudioImportJob::open(const std::string& path) {
    std::string ext;
    if (auto dot = path.find_last_of('.'); dot != std::string::npos) ext = path.substr(dot + 1);
    std::transform(ext.begin(), ext.end(), ext.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });

    auto dec = std::make_unique<Decoder>();
    uint32_t channels = 0; double rate = 0.0; uint64_t total = 0;
    if (ext == "wav") {
        dec->kind = Decoder::Kind::Wav;
        if (!drwav_init_file(&dec->wav, path.c_str(), nullptr)) return nullptr;
        dec->open = true;
        channels = dec->wav.channels; rate = dec->wav.sampleRate; total = dec->wav.totalPCMFrameCount;
    } else if (ext == "flac") {
        dec->kind = Decoder::Kind::Flac;
        dec->flac = drflac_open_file(path.c_str(), nullptr);
        if (!dec->flac) return nullptr;
        dec->open = true;
        channels = dec->flac->channels; rate = dec->flac->sampleRate; total = dec->flac->totalPCMFrameCount;
    } else if (ext == "mp3") {
        dec->kind = Decoder::Kind::Mp3;
        if (!drmp3_init_file(&dec->mp3, path.c_str(), nullptr)) return nullptr;
        dec->open = true;
        channels = dec->mp3.channels; rate = dec->mp3.sampleRate;
        total = drmp3_get_pcm_frame_count(&dec->mp3);   // a linear scan, but we're off the UI thread
    } else {
        return nullptr;   // aiff and others: deferred (as decodeAudioFile)
    }

    auto job = std::unique_ptr<AudioImportJob>(new AudioImportJob());
    job->buf_ = std::make_shared<SampleBuffer>();
    job->buf_->sourceSampleRate = rate;
    if (channels == 0 || rate <= 0.0) return nullptr;

    if (total == 0) {
        // Length unknown up front (a streamed FLAC without it in STREAMINFO): no block-wise
        // progress possible, so fall back to decoding it whole here.
        dec->close();
        auto whole = decodeAudioFile(path);
        if (!whole || whole->empty()) return nullptr;
        job->buf_ = std::move(whole);
        job->decoded_ = job->buf_->frames;
        job->done_ = true;
        return job;
    }

    job->buf_->channels = static_cast<int32_t>(channels);
    job->buf_->frames = static_cast<int64_t>(total);
    job->buf_->samples.assign(static_cast<size_t>(total) * channels, 0.0f);
    job->buf_->updatePeakTable(0, 0);   // allocate the overview (all "no data")
    job->dec_ = std::move(dec);
    return job;
}

int32_t AudioImportJob::step(int64_t maxFrames) {
    if (done_) return 0;
    if (!dec_ || !buf_) return -1;
    SampleBuffer& b = *buf_;
    const int64_t want = std::min<int64_t>(std::max<int64_t>(1, maxFrames), b.frames - decoded_);
    const uint64_t got = dec_->read(static_cast<uint64_t>(want), b.samples.data() + decoded_ * b.channels);
    const int64_t before = decoded_;
    decoded_ += static_cast<int64_t>(got);
    if (!seeded_) b.updatePeakTable(before, decoded_);
    if (got < static_cast<uint64_t>(want) || decoded_ >= b.frames) finish();
    return done_ ? 0 : 1;
}

// End of stream: trim to what actually decoded (a header can over-report a truncated
// file), rebuild the overview from the real samples, release the decoder.
void AudioImportJob::finish() {
    SampleBuffer& b = *buf_;
    if (decoded_ < b.frames) {
        b.frames = decoded_;
        b.samples.resize(static_cast<size_t>(decoded_) * b.channels);
        b.peakTable.clear();
        b.buildPeakTable();
    } else if (seeded_) {
        b.buildPeakTable();   // a seeded (cached) overview is replaced by the measured one
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
    // Seeded and still decoding: read the cached blocks only — the samples under a partial
    // edge block may not be decoded yet, and scanning those zeros would skew the bucket.
    const bool fromTable = seeded_ && !done_;
    const int64_t ready = done_ ? b.frames : decoded_;   // else only the decoded prefix
    for (int32_t i = 0; i < buckets; ++i) {
        const int64_t f0 = b.frames * i / buckets, f1 = b.frames * (i + 1) / buckets;
        float mn = 1.0f, mx = -1.0f;   // "not decoded yet"
        if (fromTable) {
            const int64_t b1 = std::max(f0 / SampleBuffer::kPeakBlock + 1,
                                        (f1 + SampleBuffer::kPeakBlock - 1) / SampleBuffer::kPeakBlock);
            for (int64_t k = f0 / SampleBuffer::kPeakBlock; k < b1 && k < b.peakBlocks(); ++k) {
                mn = std::min(mn, b.peakTable[k * 2]); mx = std::max(mx, b.peakTable[k * 2 + 1]);
            }
        } else if (f0 < ready) {
            b.peakRange(f0, std::min(f1, ready), mn, mx);
        }
        out[i * 2] = mn; out[i * 2 + 1] = mx;
    }
    return buckets;
}

bool AudioImportJob::seedPeakTable(const float* table, int64_t count) {
    if (!table || !buf_ || done_) return false;
    SampleBuffer& b = *buf_;
    if (count != b.peakBlocks() * 2) return false;   // cache is for a different file/length
    b.peakTable.assign(table, table + count);
    seeded_ = true;
    return true;
}

double AudioImportJob::detectTempo() const {
    if (!done_ || !buf_ || buf_->empty()) return 0.0;
    return nota::detectTempo(*buf_, 0, buf_->frames, buf_->sourceSampleRate);
}

} // namespace nota
