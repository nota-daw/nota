// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/midi/IMidiInput.h"
#include "nota/io/midi/IMidiOutput.h"
#include "nota/io/midi/MidiTypes.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace nota::io::midi {

/// Platform MIDI driver plugin contract (CoreMIDI, RtMidi, NullMidi, etc.).
class NOTA_IO_API IMidiDriver {
  public:
    virtual ~IMidiDriver() = default;

    virtual std::string_view id() const noexcept = 0;
    virtual std::string_view displayName() const noexcept = 0;

    virtual std::vector<MidiDeviceInfo> enumerateInputs() const = 0;
    virtual std::string endpointUid(uint32_t endpoint) const = 0;
    virtual std::string endpointName(uint32_t endpoint) const = 0;
    virtual uint32_t portsGeneration() const = 0;

    [[nodiscard]] virtual std::unique_ptr<IMidiInput> createInput() const = 0;
    [[nodiscard]] virtual std::unique_ptr<IMidiOutput> createOutput() const = 0;
};

} // namespace nota::io::midi
