// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <cstdint>

namespace nota::engine {

inline constexpr int32_t kMasterTrackId = 1000000;
inline constexpr int32_t kMidiRackBase = -1000;
inline constexpr int32_t kNestedRackBase = -100000;
inline constexpr int32_t kSidechainRoutes = 16;

constexpr bool isNestedRackAddress(int32_t deviceIndex) noexcept {
    return deviceIndex <= kNestedRackBase;
}

inline constexpr int32_t kTransferSidechainCleared = 1;
inline constexpr int32_t kTransferLinksDropped = 2;
inline constexpr int32_t kTransferSourceRemoved = 4;

enum class TrackKind : int32_t { Audio = 0, Instrument = 1, Return = 2, Group = 3 };
enum class ClipKind : int32_t { Audio = 0, Midi = 1 };
enum class PanLawMode : int32_t { Balance = 0, Stereo = 1 };
enum class ClipPeakMode : int32_t { Played = 0, Source = 1, WarpFull = 2, WarpRange = 3, SourceRange = 4 };
enum class DeviceFamily : int32_t { AudioEffect = 0, MidiEffect = 1, Instrument = 2 };
enum class HybridMode : int32_t { Off = 0, Auto = 1 };

enum class MixTargetKind : int32_t {
    TrackVolume = 0,
    TrackPan = 1,
    MasterVolume = 2,
    DeviceParam = 3,
    MidiDeviceParam = 4,
    PluginParam = 5,
    PresetTag = 6,
    DeviceNotes = 7,
    TrackSend = 8,
    InstrumentEdit = 9,
    InstrumentState = 10,
};

struct TrackInfo {
    int32_t id = 0;
    int32_t type = 0;
    int32_t clipCount = 0;
    int32_t muted = 0;
    int32_t soloed = 0;
    int32_t armed = 0;
    float volume = 1.0f;
    float pan = 0.0f;
    int32_t groupId = -1;
};

struct ClipInfo {
    double startBeat = 0.0;
    double lengthBeats = 0.0;
    int32_t kind = 0;
    int32_t active = 1;
};

struct AudioClipInfo {
    double startBeat = 0.0;
    double sourceOffsetFrames = 0.0;
    int64_t lengthFrames = 0;
    float gain = 1.0f;
    int64_t sampleId = 0;
    float pitchSemitones = 0.0f;
    int32_t warpEnabled = 0;
    int32_t warpMode = 0;
    double warpBeats = 0.0;
    double warpPlayStart = 0.0;
    double warpPlayEnd = 0.0;
    int32_t reversed = 0;
};

struct NoteData {
    int32_t pitch = 60;
    double startBeat = 0.0;
    double lengthBeats = 1.0;
    float velocity = 0.8f;
    int32_t exprId = 0;
    int32_t articulationId = 0;
};

struct ClipAdsr {
    double attackBeats = 0.0;
    double decayBeats = 0.0;
    double releaseBeats = 0.0;
    float sustain = 1.0f;
};

struct TakeInfo {
    int32_t id = 0;
    int32_t number = 0;
    int32_t group = 0;
    double originBeat = 0.0;
    double startBeat = 0.0;
    double lengthBeats = 0.0;
    double sourceOffsetFrames = 0.0;
    int64_t lengthFrames = 0;
    int64_t sampleId = 0;
};

struct SampleInfo {
    int32_t channels = 0;
    int64_t frames = 0;
    double sampleRate = 0.0;
};

struct SamplerInfo {
    int64_t sampleId = 0;
    int32_t rootNote = 60;
    int32_t loop = 0;
};

struct MosaicStatus {
    int32_t state = 0;
    int32_t filesDone = 0;
    int32_t filesTotal = 0;
    int32_t missing = 0;
    int32_t zones = 0;
    int32_t serial = 0;
    int64_t diskDone = 0;
    int64_t diskTotal = 0;
    int64_t ramBytes = 0;
};

struct MeterLevels {
    float peakL = 0.0f;
    float peakR = 0.0f;
    float rmsL = 0.0f;
    float rmsR = 0.0f;
};

struct TrackMeterEntry {
    int32_t trackId = 0;
    MeterLevels meter;
};

struct StereoMeasure {
    int32_t trackId = 0;
    float levelDb = -200.0f;
    float balance = 0.0f;
    float width = 0.0f;
    float correlation = 0.0f;
};

} // namespace nota::engine
