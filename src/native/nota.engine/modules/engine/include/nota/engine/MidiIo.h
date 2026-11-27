// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"
#include "nota/io/gamepad/GamepadTypes.h"
#include "nota/io/midi/MidiTypes.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::MidiIo {
  public:
    MidiIo() = default;
    explicit MidiIo(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    io::midi::MidiConfig config() const;
    void setInputEnabled(const std::string& uid, bool enabled);
    bool inputEnabled(const std::string& uid) const;
    void setMpe(bool enabled, int32_t bendRange);
    bool applyConfig();

    std::vector<engine::MidiPortRow> ports();
    std::vector<engine::MidiPortRow> portRows() const;
    int32_t sourceOfUid(const std::string& uid) const;
    bool send(const std::string& outputUid, const uint8_t* data, int32_t len);

    int32_t pollControlEvents(engine::ControlEventData* out, int32_t max);
    bool setClaims(const engine::MidiClaimSpec* claims, int32_t count);
    void setLearnListening(bool on);
    int32_t pollSurfaceEvents(engine::SurfaceEventData* out, int32_t max);
    uint32_t surfaceDropped() const;
    int32_t pollSysex(int32_t* outSource, uint8_t* out, int32_t cap);

    int32_t addTestPort(const std::string& name);
    bool injectTestMidi(int32_t source, const uint8_t* data, int32_t len);
    int32_t takeTestMidiSent(int32_t source, uint8_t* out, int32_t cap);
    void pushMidiForTest(int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2);

    void noteOn(int32_t pitch, float velocity, int32_t source = -1, int32_t channel = 0);
    void noteOff(int32_t pitch, float releaseVelocity = 0.0f, int32_t source = -1, int32_t channel = 0);
    void noteExpression(int32_t pitch, int32_t dim, float value, int32_t source = -1, int32_t channel = 0);
    void controller(int32_t ctl, float value, int32_t source = -1, int32_t channel = 0);
    int32_t liveHeldNotes(int32_t* out, int32_t maxCount) const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

class NOTA_ENGINE_CORE_API Engine::GamepadIo {
  public:
    GamepadIo() = default;
    explicit GamepadIo(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    void start();
    void stop();
    int32_t count() const;
    void info(int32_t index, const char** outUid, const char** outName) const;
    int32_t pollEvents(io::gamepad::GamepadButtonEvent* out, int32_t max);
    int32_t axisValues(int32_t pad, int32_t* out, int32_t max) const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
