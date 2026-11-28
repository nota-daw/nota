// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioCodecTypes.h"
#include "nota/media/Export.h"
#include "nota/media/SampleBuffer.h"

#include <memory>
#include <string>

namespace nota::media {

[[nodiscard]] NOTA_MEDIA_API std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path);
[[nodiscard]] NOTA_MEDIA_API std::shared_ptr<SampleBuffer> decodeAudioFile(
    const std::string& path, const AudioDecodeOptions& opts);
[[nodiscard]] NOTA_MEDIA_API bool probeAudioFile(const std::string& path, AudioStreamInfo& outInfo);

} // namespace nota::media
