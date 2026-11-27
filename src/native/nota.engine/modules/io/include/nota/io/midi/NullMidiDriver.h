// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/midi/IMidiDriver.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/midi/IMidiOutput.h"
#include "nota/io/midi/IMidiInput.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace nota::io::midi {

/// Deterministic device-free MIDI driver for headless runs and unit tests.
class NOTA_IO_API NullMidiDriver final : public IMidiDriver {
  public:
    std::string_view id() const noexcept override { return "null"; }
    std::string_view displayName() const noexcept override { return "Null MIDI"; }

    std::vector<MidiDeviceInfo> enumerateInputs() const override;
    std::string endpointUid(uint32_t endpoint) const override;
    std::string endpointName(uint32_t endpoint) const override;
    uint32_t portsGeneration() const override;

    [[nodiscard]] std::unique_ptr<IMidiInput> createInput() const override;
    [[nodiscard]] std::unique_ptr<IMidiOutput> createOutput() const override;
};

} // namespace nota::io::midi
