// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// NotaPlayHead — JUCE AudioPlayHead over Nota's JUCE-free TransportInfo
// . Lets hosted plugins (synced delays/LFOs, arps, loopers)
// follow the DAW clock. `info` is written by the audio thread in
// setTransportInfo() and read by the same thread inside the plugin's
// processBlock() — no cross-thread sharing.

#pragma once

#include "engine/TransportInfo.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace hosting {

class NotaPlayHead : public juce::AudioPlayHead {
  public:
    nota::TransportInfo info;

    juce::Optional<PositionInfo> getPosition() const override;
};

} // namespace hosting
} // namespace nota
