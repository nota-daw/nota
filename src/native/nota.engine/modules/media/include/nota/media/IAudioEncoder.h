// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioCodecTypes.h"
#include "nota/media/Export.h"

#include <cstdint>
#include <string>

namespace nota::media {

class NOTA_MEDIA_API IAudioEncoder {
  public:
    virtual ~IAudioEncoder() = default;

    virtual bool open(const std::string& path, const AudioEncodeConfig& config) = 0;
    virtual bool writeFrames(const float* interleavedIn, int32_t frames) = 0;
    virtual bool close() = 0;
    virtual void discard() noexcept = 0;
    virtual bool isOpen() const noexcept = 0;
};

} // namespace nota::media
