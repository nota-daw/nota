// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Types.h"

#include <cstdint>
#include <string>
#include <vector>

namespace nota::engine {

struct MixTargetSpec {
    MixTargetKind kind = MixTargetKind::TrackVolume;
    int32_t trackId = 0;
    int32_t deviceIndex = -1;
    int32_t paramIndex = -1;
};

struct SectionInfo {
    double startBeat = 0.0;
    double lengthBeats = 0.0;
    std::string name;
};

struct ClipMoveSpec {
    int32_t srcTrackId = 0;
    int32_t clipIndex = 0;
    int32_t dstTrackId = 0;
    double newStartBeat = 0.0;
};

struct SessionAudioSlotInfo {
    int64_t sampleId = 0;
    double lengthBeats = 0.0;
    double sourceOffsetFrames = 0.0;
    int64_t lengthFrames = 0;
    float gain = 1.0f;
};

struct SessionClipProps {
    int32_t color = -1;
    int32_t launchMode = 0;
    double quantBeats = -1.0;
    int32_t legato = 0;
    int32_t loop = 1;
    float velocityAmount = 0.0f;
    int32_t followA = 0;
    int32_t followB = 0;
    int32_t chanceA = 100;
    int32_t chanceB = 0;
    double followBeats = 0.0;
    int32_t jumpScene = 0;
    int32_t followBypass = 0;
};

struct SceneProps {
    int32_t color = -1;
    double tempo = 0.0;
    int32_t sigNum = 0;
    int32_t sigDen = 0;
    int32_t follow = 0;
    double followBeats = 32.0;
};

struct SlotTakeLength {
    int32_t mode = 0;
    int32_t bars = 0;
    int32_t beats = 0;
};

struct SlotLandingRule {
    int32_t action = 0;
    int32_t scene = 0;
    double quantBeats = -1.0;
};

struct PassSpan {
    int32_t trackId = 0;
    int32_t slot = -1;
    double startBeat = 0.0;
    double endBeat = 0.0;
    double slotPos = 0.0;
    int32_t open = 0;
    int32_t warped = 0;
};

struct RecordHeldNote {
    int32_t pitch = 0;
    int32_t source = -1;
    double startBeat = 0.0;
    float velocity = 0.0f;
};

struct AutomationPointData {
    double beat = 0.0;
    float value = 0.0f;
    float curve = 0.0f;
};

struct ExprPointData {
    int32_t dim = 0;
    float beat = 0.0f;
    float value = 0.0f;
};

struct ArticulationEntryData {
    int32_t id = 0;
    int32_t triggerMode = 0;
    double offsetMs = 0.0;
    double leadMs = 0.0;
    int32_t actionStart = 0;
    int32_t actionCount = 0;
};

struct ArticulationActionData {
    int32_t kind = 0;
    int32_t channel = 0;
    int32_t data1 = 0;
    int32_t data2 = 0;
};

struct MidiClaimSpec {
    int32_t source = 0;
    int32_t wholePort = 0;
    int32_t status = 0;
    int32_t channel = 0;
    int32_t data1 = 0;
};

struct SurfaceEventData {
    int32_t source = 0;
    int32_t status = 0;
    int32_t channel = 0;
    int32_t d1 = 0;
    int32_t d2 = 0;
};

struct ControlEventData {
    int32_t kind = 0;
    int32_t channel = 0;
    int32_t number = 0;
    int32_t value = 0;
};

struct AuditionNoteSpec {
    double startBeat = 0.0;
    double lengthBeats = 0.0;
    int32_t pitch = 60;
    float velocity = 0.8f;
};

struct PresetTagInfo {
    std::string id;
    std::string name;
    std::vector<float> base;
};

struct MidiPortRow {
    int32_t source = -1;
    std::string uid;
    std::string name;
    std::string outputUid;
};

} // namespace nota::engine
