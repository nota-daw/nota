// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"

#include <cstdint>
#include <memory>
#include <utility>

namespace nota::engine {

struct TransportSnapshot {
    double startBeat = 0.0;
    double positionBeats = 0.0;
    double bpm = 120.0;
    double loopStart = 0.0;
    double loopEnd = 0.0;
    double durationSeconds = 0.0;
    double elapsedSeconds = 0.0;
    int32_t playing = 0;
    int32_t looping = 0;
};

} // namespace nota::engine

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Transport {
  public:
    Transport() = default;
    explicit Transport(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    void play();
    void playCountIn();
    void stop();
    void seekBeats(double beat);

    bool setBpm(double bpm);
    double bpm() const;
    void setTimeSignature(int32_t num, int32_t denom);
    void setLoop(bool enabled, double startBeat, double endBeat);
    bool loopEnabled() const;
    double loopStart() const;
    double loopEnd() const;
    void setMetronome(bool on);

    double positionBeats() const;
    bool isPlaying() const;
    bool snapshot(engine::TransportSnapshot& out) const;

    void setCountInBars(int32_t bars);
    int32_t countInBars() const;
    double countInBeatsRemaining() const;

    void setLoopRecordReplace(bool on);
    bool loopRecordReplace() const;

    bool arrangementActive() const;
    void backToArrangement();

    bool tempoAutomated() const;
    double bpmAt(double beat) const;
    double beatToSeconds(double beat) const;
    double secondsToBeat(double seconds) const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
