// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioCodecTypes.h"
#include "nota/media/Export.h"
#include "nota/media/SampleBuffer.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::media {

class IAudioDecoder;

class NOTA_MEDIA_API AudioImportJob {
  private:
    struct PrivateTag {
        explicit PrivateTag() = default;
    };

  public:
    explicit AudioImportJob(PrivateTag);
    ~AudioImportJob();
    AudioImportJob(const AudioImportJob&) = delete;
    AudioImportJob& operator=(const AudioImportJob&) = delete;

    [[nodiscard]] static std::unique_ptr<AudioImportJob> open(
        const std::string& path, double maxSeconds = 0.0, const AudioDecodeOptions& opts = {});

    [[nodiscard]] int32_t step(int64_t maxFrames);

    [[nodiscard]] bool done() const noexcept { return done_; }
    [[nodiscard]] int64_t decodedFrames() const noexcept { return decoded_; }
    [[nodiscard]] int64_t sourceFrames() const noexcept { return sourceFrames_; }
    [[nodiscard]] const std::shared_ptr<SampleBuffer>& buffer() const noexcept { return buf_; }

    int32_t peaks(float* outMinMax, int32_t maxPoints) const;
    [[nodiscard]] bool seedPeakTable(const float* table, int64_t count);
    [[nodiscard]] double detectTempo() const;

  private:
    void finish();

    std::unique_ptr<IAudioDecoder> dec_;
    std::shared_ptr<SampleBuffer> buf_;
    int64_t decoded_ = 0;
    int64_t sourceFrames_ = 0;
    bool done_ = false;
    bool seeded_ = false;
    bool streaming_ = false;
    std::string path_;
    std::vector<float> streamCarry_;
    int64_t streamCarryFrames_ = 0;
    std::vector<float> streamScratch_;
};

} // namespace nota::media
