// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/Export.h"
#include "nota/media/SampleBuffer.h"

#include <cstdint>

namespace nota::media {

NOTA_MEDIA_API double detectTempo(const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate);

NOTA_MEDIA_API int32_t detectTransients(
    const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate, double* outSrcFrames, int32_t maxCount);

} // namespace nota::media
