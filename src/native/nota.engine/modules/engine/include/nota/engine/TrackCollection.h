// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::TrackCollection {
  public:
    TrackCollection() = default;
    explicit TrackCollection(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    int32_t count() const;
    Track at(int32_t index) const;
    Track byId(int32_t trackId) const;
    Track master() const;
    bool infoAt(int32_t index, engine::TrackInfo& out) const;

    Track addAudio();
    Track addGroup();
    Track addReturn();
    Track addInstrument(std::string_view slug);
    Track addBuiltinInstrument(int32_t kind);
    Track addSampler(const std::string& path = {}, int32_t rootNote = 60, bool loop = false);
    Track addInstrumentRack();
    Track addDrumRack();
    Track addPluginInstrument(int32_t catalogIndex);

    int32_t duplicate(int32_t trackId);
    int32_t duplicateMany(const int32_t* ids, int32_t count, int32_t* outIds, int32_t cap);
    int32_t addLike(int32_t trackId);
    bool remove(int32_t trackId);
    bool removeMany(const int32_t* ids, int32_t count);
    bool move(int32_t trackId, int32_t toIndex);

    int32_t createGroup(const int32_t* ids, int32_t count);
    bool ungroup(int32_t groupId);

    bool copy(int32_t trackId);
    bool copyMany(const int32_t* ids, int32_t count);
    int32_t paste();
    int32_t pasteAfter(int32_t afterTrackId, int32_t* outIds, int32_t cap);
    bool hasClipboard() const;

    bool moveClipBlock(const std::vector<engine::ClipMoveSpec>& moves);
    bool copyClipBlock(const std::vector<std::pair<int32_t, int32_t>>& selection);
    bool cutClipBlock(const std::vector<std::pair<int32_t, int32_t>>& selection);
    int32_t pasteClipBlock(double atBeat, int32_t destTrackId);
    double duplicateClipBlock(const std::vector<std::pair<int32_t, int32_t>>& selection);
    const std::vector<std::pair<int32_t, int32_t>>& lastPlacedClips() const;

    bool deleteClipsInRange(const std::vector<int32_t>& trackIds, double start, double end, bool withAuto = false);
    double duplicateRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool insertTime(double atBeat, double lengthBeats, bool duplicate);
    bool splitClipsAtRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool fadeTouchingSplits(const std::vector<std::pair<int32_t, int32_t>>& selection);
    void setSplitFades(bool on);
    bool splitFades() const;
    bool consolidateRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool copyRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool cutRange(const std::vector<int32_t>& trackIds, double start, double end);
    int32_t clipboardBlockCount() const;
    int32_t clipboardClipKind() const;

    void setAutomationLock(bool locked);
    bool automationLock() const;
    bool lastMoveKeptDeviceAutomation() const;

    bool sampleInfo(int64_t sampleId, engine::SampleInfo& out) const;
    int64_t sampleRead(int64_t sampleId, float* out, int64_t cap) const;
    int32_t createNoteExpr(const engine::ExprPointData* points, int32_t count);
    int32_t noteExprPoints(int32_t id, engine::ExprPointData* out, int32_t maxPoints) const;
    std::optional<engine::PresetTagInfo> presetTagData(int32_t tag) const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
