// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::AutomationLane {
  public:
    AutomationLane() = default;
    AutomationLane(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t laneIndex) noexcept
        : impl_(std::move(impl)), trackId_(trackId), index_(laneIndex) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t index() const noexcept { return index_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    bool info(int32_t* outTarget, int32_t* outDeviceIndex, int32_t* outParamIndex, int32_t* outPointCount) const;
    std::string paramId() const;
    bool path(int32_t* chainIndex, int32_t* slot) const;
    int32_t points(engine::AutomationPointData* out, int32_t cap) const;
    bool setPoints(const engine::AutomationPointData* pts, int32_t count);
    bool setPointsLive(const engine::AutomationPointData* pts, int32_t count);
    bool remove();

    static void setRecordEnabled(Engine& engine, bool on);
    static bool recordEnabled(const Engine& engine);
    static void beginWrite(Engine& engine, int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex,
        const char* paramId, bool latch);
    static void endWrite(
        Engine& engine, int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId);
    static void reenableAll(Engine& engine);
    static bool hasOverrides(const Engine& engine);

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t index_ = -1;
};

} // namespace nota
