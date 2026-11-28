// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioCodecTypes.h"
#include "nota/media/Export.h"

#include <cstdint>
#include <string>

namespace nota::media {

class NOTA_MEDIA_API IAudioDecoder {
  public:
    virtual ~IAudioDecoder() = default;

    virtual bool open(const std::string& path) = 0;
    virtual void close() noexcept = 0;
    virtual bool isOpen() const noexcept = 0;

    virtual AudioStreamInfo info() const noexcept = 0;
    virtual bool seekToFrame(int64_t frame) = 0;
    virtual int64_t readFrames(int64_t maxFrames, float* interleavedOut) = 0;
};

} // namespace nota::media
