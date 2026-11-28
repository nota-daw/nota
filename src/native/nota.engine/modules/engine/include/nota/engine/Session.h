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
#include <vector>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Scene {
  public:
    Scene() = default;
    Scene(std::weak_ptr<Engine::Impl> impl, int32_t sceneIndex) noexcept : impl_(std::move(impl)), index_(sceneIndex) {}

    int32_t index() const noexcept { return index_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    void launch(bool fromFollow = false, double quantOverride = -1.0);
    void stop();
    bool cancelLaunch();
    bool isQueued() const;
    int32_t record();

    bool props(engine::SceneProps& out) const;
    bool setProps(const engine::SceneProps& p);
    std::string name() const;
    bool setName(const std::string& name);

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t index_ = -1;
};

class NOTA_ENGINE_CORE_API Engine::Session {
  public:
    Session() = default;
    explicit Session(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    int32_t sceneCount() const;
    Scene scene(int32_t sceneIndex) const;
    Slot slot(int32_t trackId, int32_t sceneIndex) const;

    int32_t addScene();
    int32_t insertScene(int32_t at);
    int32_t duplicateScene(int32_t sceneIndex);
    int32_t captureScene(int32_t at);
    bool removeScene(int32_t sceneIndex);
    bool moveScene(int32_t from, int32_t to);

    void stopAll();
    void setLaunchQuant(double beats);
    double launchQuant() const;
    void setFollow(bool on);
    bool follow() const;
    void setRecordLength(double beats);
    double recordLength() const;
    void stopRecord();
    bool recordTarget(int32_t* trackId, int32_t* scene, double* elapsed) const;
    bool takeSceneTempoChange(double* bpm, int32_t* num, int32_t* den);
    int32_t passSpans(engine::PassSpan* out, int32_t cap) const;
    bool sceneLaunchWaiting() const;

    int32_t arrangementClipsToSession(const std::vector<std::pair<int32_t, int32_t>>& sel, int32_t startScene);
    int32_t pasteClipBlockToSession(int32_t destTrackId, int32_t startScene);
    int32_t slotsToArrangement(
        const std::vector<std::pair<int32_t, int32_t>>& slots, double atBeat, int32_t destTrackId);

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
