// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Mixer {
  public:
    Mixer() = default;
    explicit Mixer(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    static constexpr int32_t masterTrackId() noexcept { return engine::kMasterTrackId; }

    void setMasterVolume(float volume);
    float masterVolume() const;
    void setMasterPan(float pan);
    float masterPan() const;
    void masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const;
    void masterMeter(engine::MeterLevels& out) const;

    void setCueVolume(float gain);
    float cueVolume() const;
    void cueMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const;
    void cueMeter(engine::MeterLevels& out) const;

    void setClickDestination(int32_t destination);
    int32_t clickDestination() const;
    void setClickVolume(float gain);

    int32_t returnCount() const;
    int32_t addReturn();

    int32_t readTrackMeters(engine::TrackMeterEntry* out, int32_t capacity) const;
    int32_t stereoMeasures(engine::StereoMeasure* out, int32_t capacity) const;
    bool setPanLaw(engine::PanLawMode law, bool checkpoint);
    engine::PanLawMode panLaw() const;

    std::vector<engine::SectionInfo> sections() const;
    void setSections(std::vector<engine::SectionInfo> sections, bool checkpoint);
    bool moveSection(int32_t index, int32_t toBoundary);
    bool duplicateSectionWithContent(int32_t index);
    bool deleteSectionWithContent(int32_t index);
    bool sectionOverlaps(int32_t index) const;

    int32_t masterVolumeAutomationCount() const;
    int32_t getMasterVolumeAutomation(engine::AutomationPointData* out, int32_t cap) const;
    bool setMasterVolumeAutomation(const engine::AutomationPointData* pts, int32_t count, bool live = false);

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
