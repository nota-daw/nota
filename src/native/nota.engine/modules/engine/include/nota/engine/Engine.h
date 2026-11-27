// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Export.h"

#include <memory>

namespace nota {

class NOTA_ENGINE_CORE_API Engine {
  public:
    class Transport;
    class Mixer;
    class Track;
    class TrackCollection;
    class Clip;
    class Device;
    class DeviceChain;
    class Parameter;
    class Rack;
    class RackChain;
    class Macro;
    class Session;
    class Scene;
    class Slot;
    class Recorder;
    class AutomationLane;
    class Modulator;
    class CvLink;
    class Preview;
    class AudioIo;
    class MidiIo;
    class GamepadIo;
    class History;
    class Diagnostics;

    struct Impl;

    Engine();
    ~Engine();

    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;
    Engine(Engine&&) noexcept;
    Engine& operator=(Engine&&) noexcept;

    static const char* version() noexcept;

    void reset();
    [[nodiscard]] bool isValid() const noexcept { return impl_ != nullptr; }
    explicit operator bool() const noexcept { return isValid(); }

    Transport transport();
    const Transport transport() const;

    Mixer mixer();
    const Mixer mixer() const;

    TrackCollection tracks();
    const TrackCollection tracks() const;

    Session session();
    const Session session() const;

    Recorder recorder();
    const Recorder recorder() const;

    Preview preview();
    const Preview preview() const;

    AudioIo audio();
    const AudioIo audio() const;

    MidiIo midi();
    const MidiIo midi() const;

    GamepadIo gamepad();
    const GamepadIo gamepad() const;

    History history();
    const History history() const;

    Diagnostics diagnostics();
    const Diagnostics diagnostics() const;

  private:
    std::shared_ptr<Impl> impl_;
};

} // namespace nota
