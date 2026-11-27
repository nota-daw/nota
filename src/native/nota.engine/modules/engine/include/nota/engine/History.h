// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"

#include <cstdint>
#include <memory>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::History {
  public:
    History() = default;
    explicit History(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    bool undo();
    bool redo();
    bool canUndo() const;
    bool canRedo() const;

    void beginGroup();
    void endGroup();
    void clear();

    int32_t beginMixGesture(const engine::MixTargetSpec* targets, int32_t count);
    void endMixGesture(int32_t handle);

    bool mixUndoSelfTest();
    bool presetTagSelfTest();

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
