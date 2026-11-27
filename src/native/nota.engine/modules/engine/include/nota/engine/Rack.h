// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Macro {
  public:
    Macro() = default;
    Macro(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t macroIndex) noexcept
        : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), index_(macroIndex) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t deviceIndex() const noexcept { return deviceIndex_; }
    int32_t index() const noexcept { return index_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    float get() const;
    void set(float value);
    std::string name() const;
    void setName(const std::string& name);

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t deviceIndex_ = -1;
    int32_t index_ = -1;
};

class NOTA_ENGINE_CORE_API Engine::Rack {
  public:
    Rack() = default;
    Rack(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex) noexcept
        : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t deviceIndex() const noexcept { return deviceIndex_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    bool kindAllowed(int32_t kind) const;
    int32_t chainCount() const;
    RackChain chain(int32_t chainIndex) const;
    int32_t addChain(int32_t instrumentKind);
    int32_t addSamplerChain(const std::string& path, int32_t rootNote, bool loop);
    int32_t addPluginInstrumentChain(int32_t catalogIndex);
    bool removeChain(int32_t chainIndex);
    int32_t duplicateChain(int32_t chainIndex);
    bool checkpoint();
    int32_t nestedChainIndex(uint64_t uid) const;

    Macro macro(int32_t macroIndex) const;
    int32_t addMacroMapping(
        int32_t macro, int32_t chain, int32_t targetDevice, int32_t paramIndex, float rangeMin, float rangeMax);
    int32_t mappingCount() const;
    bool mappingInfo(int32_t index, int32_t& macro, int32_t& chain, int32_t& targetDevice, int32_t& paramIndex,
        float& rangeMin, float& rangeMax) const;
    bool removeMapping(int32_t index);
    bool setMappingRange(int32_t index, float rangeMin, float rangeMax);
    int32_t mappingCurve(int32_t index) const;
    bool setMappingCurve(int32_t index, int32_t curve);

    bool mono() const;
    void setMono(bool on);
    bool merge() const;
    void setMerge(bool on);
    bool thru() const;
    void setThru(bool on);
    float volume() const;
    void setVolume(float v);
    float glide() const;
    void setGlide(float v);

    void setMode(int32_t mode);
    int32_t mode() const;
    void setDryWet(float v);
    float dryWet() const;
    void setPdc(bool on);
    bool pdc() const;
    void setChainSelect(float v);
    float chainSelect() const;
    void setSelFollow(bool on);
    bool selFollow() const;
    float liveSelector() const;

    void setSwing(float v);
    float swing() const;
    void setHumanize(float v);
    float humanize() const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t deviceIndex_ = -1;
};

} // namespace nota
