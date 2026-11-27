// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// NotaPlayHead — JUCE AudioPlayHead over nota::dsp::TransportInfo.

#pragma once

#include "nota/dsp/rt/TransportInfo.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota::pluginhost::detail {

class NotaPlayHead : public juce::AudioPlayHead {
  public:
    dsp::TransportInfo info{};

    juce::Optional<PositionInfo> getPosition() const override;
};

} // namespace nota::pluginhost::detail
