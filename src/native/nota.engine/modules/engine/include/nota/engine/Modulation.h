// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"

#include <cstdint>
#include <memory>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Modulator {
  public:
    Modulator() = default;
    Modulator(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t modId) noexcept
        : impl_(std::move(impl)), trackId_(trackId), modId_(modId) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t id() const noexcept { return modId_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    int32_t kind() const;
    float get(int32_t field) const;
    void set(int32_t field, float value);
    float value() const;
    int32_t scope(float* values, float* dts, int32_t cap) const;
    bool remove();

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t modId_ = 0;
};

class NOTA_ENGINE_CORE_API Engine::CvLink {
  public:
    CvLink() = default;
    CvLink(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t index) noexcept
        : impl_(std::move(impl)), trackId_(trackId), index_(index) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t index() const noexcept { return index_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    int32_t source() const;
    int32_t sourceKind() const;
    int32_t sourceDevice() const;
    int32_t sourceParam() const;
    int32_t targetKind() const;
    int32_t targetTrack() const;
    int32_t device() const;
    int32_t param() const;
    float depth() const;
    int32_t mode() const;
    float base() const;
    void setDepth(float depth);
    void setMode(int32_t mode);
    void setBase(float base);
    bool path(int32_t* srcChain, int32_t* srcSlot, int32_t* tgtChain, int32_t* tgtSlot) const;
    bool remove();

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t index_ = -1;
};

} // namespace nota
