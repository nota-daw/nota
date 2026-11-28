// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Real-time render graph work items, scratch arenas, live MIDI snap/CC lanes,
// resampling, and pool sizing constants shared inside nota::detail.

#pragma once

#include "devices/DeviceTypes.h"
#include "model/EngineTypes.h"
#include "model/Track.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/dsp/rt/PlanarStereoBuffer.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/io/midi/IMidiInput.h"

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace nota::detail {

struct Graph;

inline constexpr int32_t kPreviewScope = 4096;
inline constexpr int32_t kMasterTrackId = 1000000;
inline constexpr size_t kLayoutHistory = 16;
inline constexpr int32_t kSincZeros = 20;
inline constexpr int32_t kSincTable = 256;
inline constexpr int32_t kMonitorSrcHalfMax = 160;
inline constexpr int32_t kMonitorSrcFrames = 4096 + 2 * kMonitorSrcHalfMax + 8;
inline constexpr int kMaxLive = 256;
inline constexpr int kMaxLiveExpr = 256;
inline constexpr int32_t kLiveReleaseExpr = -2;
inline constexpr int kMaxLiveRelease = 64;
inline constexpr int kMaxHeldSnap = 256;
inline constexpr int kMaxBlockExpr = 1024;
inline constexpr int kCtlInputs = nota::io::midi::IMidiInput::kMaxSources + 1;
inline constexpr int32_t kMaxBlock = 16384;
inline constexpr double kTailQuietSeconds = 3.0;
inline constexpr double kTailMaxQuietSeconds = 30.0;
inline constexpr float kTailFloor = 1.0e-5f;
inline constexpr double kOverrunShare = 0.05;
inline constexpr double kRtSizeLoad = 0.50;
// parWorkNs_ sums per-leaf durations across concurrent cores (~20-30% contention overhead vs solo),
// so values up to ~1.15 * roomNs can still fit solo during the probe window.
inline constexpr double kRtExitHysteresis = 1.15;
inline constexpr double kRtHoldSeconds = 2.0;
inline constexpr double kRtMaxHoldSeconds = 32.0;
inline constexpr double kRtProbeSeconds = 0.5;
inline constexpr int32_t kMaxGroups = 16;
inline constexpr int32_t kMaxRoutes = kSidechainRoutes;
inline constexpr int32_t kTrackTapRingFrames = 2 * kMaxBlock;
inline constexpr int32_t kMaxSegAnchors = 32;
inline constexpr int kMaxKeyLevels = kSidechainRoutes;

struct RtMidiLane {
    int32_t trackId = -1;
    bool pendingActive[128] = {};
    double pendingStart[128] = {};
    float pendingVel[128] = {};
    int8_t pendingSource[128] = {};
    int8_t pendingChannel[128] = {};
    bool recRoutedActive[128] = {};
    double recRoutedStart[128] = {};
    float recRoutedVel[128] = {};

    void clear() noexcept {
        for (int i = 0; i < 128; ++i) {
            pendingActive[i] = false;
            recRoutedActive[i] = false;
        }
    }
};

struct HeldSnapSlot {
    std::atomic<int32_t> pitch{0};
    std::atomic<int32_t> source{0};
    std::atomic<double> startBeat{0.0};
    std::atomic<float> velocity{0.0f};
};

struct LiveCtlIn {
    std::atomic<float> value{nota::dsp::kCtlRest};
    std::atomic<int32_t> channel{0};
    std::atomic<uint32_t> stamp{0};
};

enum class LeafPath : uint8_t { Skip, Frozen, Monitor, SessionInst, SessionAudio, Instrument, AudioClips, Tail };

struct NOTA_CACHE_ALIGNED LeafWork {
    Track* t = nullptr;
    bool audible = false;
    bool capturing = false;
    bool feeding = false;
    bool toMix = false;
    bool monitoring = false;
    bool frozenActive = false;
    bool sessionActive = false;
    bool cue = false;
    bool cueOnly = false;
    bool postTapMuted = false;
    bool eligible = false;
    LeafPath path = LeafPath::Skip;
    int wid = -1;
    int64_t workNs = 0;
    float* dest = nullptr;
    int8_t destSlot = -1;
    float* route = nullptr;
    float sends[kMaxReturns] = {};
    bool anySend = false;
    bool tapRec = false;
    uint32_t keyNeeds = 0;
    int level = 0;
    bool preRendered = false;
    bool tapsDone = false;
    bool copyPost = true;
    bool drySilent = false;
    bool postSilent = false;
    bool hybridEligible = false;
    bool hybridServed = false;
    bool hybridDropTick = false;
    float vol = 1.0f;
    float pan = 0.0f;
};

struct NOTA_CACHE_ALIGNED BusWork {
    Track* track = nullptr;
    int32_t trackIndex = -1;
    int32_t busIndex = -1;
    int32_t parentGroupSlot = -1;
    bool busActive = false;
    bool mute = false;
    bool feedMute = false;
    bool solo = false;
    bool cue = false;
    bool eligible = false;
    bool parallelRan = false;
    uint32_t keyNeeds = 0;
    int32_t level = 0;
};

struct RenderScratch {
    size_t tracks = 0;
    int32_t frameCapacity = 0;
    size_t workerPlanarCount = 0;
    std::vector<LeafWork> leafWork;
    std::vector<float> leafBuf;
    std::vector<float> groupScratch;
    std::vector<float> returnScratch;
    std::vector<nota::dsp::PlanarStereoBuffer> workerPlanar;
    std::array<BusWork, 16> groupWork{};
    std::vector<std::vector<nota::dsp::MidiEv>> blockMidi;
    std::vector<int> blockMidiN;
    std::vector<nota::dsp::MidiKeyNotes> blockMidiStart;
    std::vector<std::vector<ExprEv>> blockExpr;
    std::vector<int> blockExprN;
    std::vector<int> parIdx;
    const Graph* cachedRouteGraph = nullptr;
    uint64_t cachedRoutingEpoch = 0;
    std::vector<int32_t> trackRouteSlot;
};

struct SegAnchor {
    int32_t off;
    double samples;
    bool rolling;
    bool jump;
};

} // namespace nota::detail
