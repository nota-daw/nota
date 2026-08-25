// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio file decoding (message thread only — never called from the audio thread).
// Backed by dr_libs (public-domain). WAV/FLAC/MP3 supported in M1; AIFF is
// deferred (needs libsndfile — see LICENSES/third-party.md).

#pragma once

#include "SampleBuffer.h"
#include <memory>
#include <string>

namespace nota {

// Decodes `path` into a SampleBuffer. Returns nullptr on failure or unsupported
// format. Format is chosen by file extension.
std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path);

} // namespace nota
