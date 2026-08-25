// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Offline tempo estimation for a decoded audio region. Used by auto-warp to snap
// a clip's musical length to the beat grid so it conforms to the project tempo.
// Method: log-energy onset novelty → autocorrelation with a tempo prior (Ellis-
// style). Message-thread only; no RT constraints.

#pragma once

#include "SampleBuffer.h"
#include <cstdint>

namespace nota {

// Estimate the tempo (BPM) of `len` frames starting at `offset` in `src`.
// Returns 0.0 when the material is too short or too flat to estimate.
// Result is folded into a musical range (~70–180 BPM) via the tempo prior.
double detectTempo(const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate);

// Detect transient (onset) positions in `len` frames from `offset`, writing their
// absolute source frames into `outSrcFrames` (ascending). Returns the count (capped
// at `maxCount`), 0 when the material is too short/flat. Used by Beats-mode warp to
// lock hits to the grid. Shares the onset-novelty envelope with detectTempo.
int32_t detectTransients(const SampleBuffer& src, int64_t offset, int64_t len,
                         double sampleRate, double* outSrcFrames, int32_t maxCount);

} // namespace nota
