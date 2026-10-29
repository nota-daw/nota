// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A custom wavetable for Nota Aurora (specs/053): the frames an audio file was turned
// into, conditioned (DC removed, peak-normalised, loop seams blended) and band-limited
// into ten mip levels so a bright table played high does not fold back. It is built
// once on any thread — the builders touch no engine state — and never changes after,
// so the audio thread can read it through a plain pointer for as long as the
// snapshot that holds it is live.
//
// How a file becomes frames (spec FR-009, research R3):
//   (a) a standard wavetable file — a `clm ` chunk names the frame size, or the length
//       is an exact multiple of 2048 (at least two frames) — keeps its frames, ≤ 256;
//   (b) otherwise up to 4096 samples is one cycle, resampled to 2048 exactly;
//   (c) otherwise a recording: 16 evenly spaced 2048-sample windows, each snapped to
//       an upward zero crossing, with no pitch detection.
// Only the first 60 s are read.

#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {

struct AuroraTable {
    static constexpr int kSize = 2048, kLevels = 10, kMaxFrames = 256, kRecordingFrames = 16;
    static constexpr int kMinLen = 256;
    static constexpr double kMaxSeconds = 60.0;

    enum Origin : int32_t { WavetableFile = 0, SingleCycle = 1, Recording = 2 };
    enum Result : int32_t { Ok = 0, ErrDecode = -1, ErrFormat = -2, ErrSilent = -3, ErrEmpty = -4, ErrArgs = -5 };

    int32_t frames = 0;
    int32_t origin = WavetableFile;
    bool truncatedLength = false, truncatedFrames = false;
    std::string name;
    uint8_t hash[16] = {};
    uint32_t serial = 0;
    // level[k] holds `frames` frames of len(k) samples with the harmonics below
    // harmonics(k) = 1024 >> k (level 0 is the conditioned frames themselves). From level 1
    // on a level keeps at least four samples per cycle of its top harmonic, and no level is
    // shorter than 256 samples, so the linear read stays clean (research R4).
    std::vector<float> level[kLevels];

    static constexpr int len(int k) {
        const int l = k == 0 ? kSize : (2 * kSize) >> k;
        return l > kSize ? kSize : (l < kMinLen ? kMinLen : l);
    }
    static constexpr int harmonics(int k) { return (kSize / 2) >> k; }
    const float* frame(int k, int f) const { return level[k].data() + (size_t)f * (size_t)len(k); }
    const float* base() const { return level[0].data(); }

    // Decode and build (WAV / FLAC / MP3). Returns Ok and sets `out`, or an error.
    static int32_t buildFromFile(const std::string& path, std::shared_ptr<const AuroraTable>& out);
    // The same rules from mono samples already in memory (`declaredFrame` > 0 when the
    // source names its frame size). Used by buildFromFile and by tests.
    static int32_t buildFromMono(const float* mono, int64_t n, int32_t declaredFrame, std::string name,
        bool truncatedLength, std::shared_ptr<const AuroraTable>& out);
    // Already-conditioned frames (a project or preset reload): only the mips are
    // rebuilt, so a reload plays exactly what was saved.
    static int32_t buildFromFrames(const float* frames, int32_t count, std::string name, int32_t origin,
        std::shared_ptr<const AuroraTable>& out);
};

// What one Aurora holds: a table per oscillator (null = none) and, for a table a project
// could not restore, the name it had.
struct AuroraTableSet {
    std::shared_ptr<const AuroraTable> slot[2];
    std::string missing[2];
};

} // namespace nota
