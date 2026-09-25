// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Background audio import: decodes a file block by block into a SampleBuffer,
// building its waveform overview as it goes, so a long file can be loaded on a
// worker thread while the UI draws the waveform filling in. Touches no engine state;
// the finished buffer is handed to Engine::addAudioClipBuffer on the authoring thread.
// A job is single-threaded — the caller serialises every call on one instance.

#pragma once

#include "SampleBuffer.h"
#include <cstdint>
#include <memory>
#include <string>

namespace nota {

class AudioImportJob {
public:
    ~AudioImportJob();

    // Opens `path` and reads its header. Null when the file can't be opened/decoded.
    static std::unique_ptr<AudioImportJob> open(const std::string& path);

    // Decodes up to maxFrames more. Returns 1 while frames remain, 0 once done, -1 on error.
    int32_t step(int64_t maxFrames);

    bool    done() const { return done_; }
    int64_t decodedFrames() const { return decoded_; }
    const std::shared_ptr<SampleBuffer>& buffer() const { return buf_; }

    // Display peaks over the WHOLE file (maxPoints buckets, min/max pairs). Buckets not
    // decoded yet hold (1,-1) so the UI can draw what's ready — unless a cached overview
    // was seeded, in which case the full waveform is available from the start.
    int32_t peaks(float* outMinMax, int32_t maxPoints) const;

    // Seed the overview from a cached table (same length as the buffer's peak table).
    bool seedPeakTable(const float* table, int64_t count);

    // Tempo of the whole decoded file (0 = not detectable). Call once done.
    double detectTempo() const;

private:
    AudioImportJob() = default;
    void finish();

    struct Decoder;
    std::unique_ptr<Decoder> dec_;
    std::shared_ptr<SampleBuffer> buf_;
    int64_t decoded_ = 0;
    bool done_ = false;
    bool seeded_ = false;
};

} // namespace nota
