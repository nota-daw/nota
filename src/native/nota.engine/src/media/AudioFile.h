// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio file decoding (message thread only — never called from the audio thread).
// Backed by dr_libs (public-domain). WAV/FLAC/MP3 supported in M1; AIFF is
// deferred (needs libsndfile — see LICENSES/third-party.md).

#pragma once

#include "AudioSource.h"
#include "SampleBuffer.h"
#include <memory>
#include <string>

namespace nota {

// Decodes `path` into a SampleBuffer. Returns nullptr on failure or unsupported
// format. Format is chosen by file extension.
std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path);
std::shared_ptr<SampleBuffer> decodeAudioFile(const std::string& path, const AudioDecodeOptions& opts);

#ifdef _WIN32
// A UTF-8 path as Windows wide characters, for the dr_libs *_w opens: their char* opens read
// the path in the ANSI code page, so a non-ASCII folder or file name failed (#974).
std::wstring widePath(const std::string& utf8);
#endif

} // namespace nota
