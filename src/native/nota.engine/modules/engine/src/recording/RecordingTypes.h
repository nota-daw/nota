// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio/MIDI recording, loop pass capture, fold state, and test MIDI port types.

#pragma once

#include "io/MidiSurface.h"
#include "model/EngineTypes.h"
#include "nota/dsp/rt/AutomationTypes.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/io/midi/MidiParser.h"
#include "session/SessionPlayer.h"

#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

inline constexpr double kGrowingTakeBeats = 4096.0;
inline constexpr int32_t kMaxTakeBars = 64;
inline constexpr int32_t kMaxMeterBeats = 32;
inline constexpr int32_t kMaxMidiTakeLanes = 16;
inline constexpr int32_t kSkipInputUnavailable = 1;
inline constexpr int32_t kSkipTooMany = 2;
inline constexpr int32_t kSkipSwitchedOff = 3;
inline constexpr int32_t kSkipMixedSources = 4;

inline bool isCaptureMarker(const InputFrame& f) noexcept {
    return std::isnan(f.l) && std::isnan(f.r);
}

struct TakeGeometry;

struct TestMidiPort {
    std::string name;
    int32_t source = 0;
    nota::io::midi::MidiParser parser;
    MidiLoopback sent;
};

struct TakeNote {
    int32_t pitch;
    double absStart;
    int32_t pass;
};

struct CtlFold {
    std::vector<RecordedCtl> run;
    bool open = false;
    int32_t pass = 0;
    double startAbs = 0.0;
    double lastAbs = 0.0;
    bool hasCommitted = false;
    nota::dsp::AutomationPoint committed{};
    std::shared_ptr<const nota::dsp::ControllerCurve> before;
};

struct TakeCtl {
    int32_t ctl;
    double absBeat;
    int32_t pass;
};

struct MidiTakeLane {
    int32_t trackId = -1;
    int32_t clipIndex = -1;
    int32_t scene = -1;
    bool growing = false;
    double lenBeats = 0.0;
    bool ownLen = false;
    bool fresh = false;
    bool started = false;
    std::shared_ptr<SessionPlayer> player;
    std::vector<TakeNote> takeNotes;
    std::array<CtlFold, nota::dsp::kCtlCount> ctlFold{};
    std::vector<TakeCtl> takeCtls;
};

struct TakeAttempt {
    bool active = false;
    bool session = false;
    bool countedIn = false;
    std::vector<std::pair<int32_t, std::shared_ptr<Track>>> baseTracks;
    size_t undoBase = 0;
    std::vector<UndoEntry> redoSaved;
    int32_t sessionTrackId = 0;
    int32_t sessionScene = -1;
    std::vector<std::pair<int32_t, int32_t>> sessionSlots;
};

struct LoopPass {
    std::vector<float> buf;
    int32_t channels = 2;
    size_t from = 0;
    size_t to = 0;
};

struct CaptureLane {
    int32_t trackId = 0;
    int32_t first = 0;
    int32_t width = 2;
    int32_t channels = 2;
    int32_t scene = -1;
    size_t offset = 0;
    double lenBeats = 0.0;
    bool ownLen = false;
    bool fresh = false;
    double startBeat = std::numeric_limits<double>::quiet_NaN();
    std::vector<float> buf;
    std::vector<float> scratch;
    std::vector<LoopPass> passes;
};

} // namespace nota::detail
