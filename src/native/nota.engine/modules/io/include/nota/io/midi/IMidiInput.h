// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/midi/MidiParser.h"
#include "nota/io/midi/MidiTypes.h"

#include <cstdint>
#include <functional>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::midi {

/// Hardware or virtual MIDI input multiplexer connecting active MIDI sources.
class NOTA_IO_API IMidiInput {
  public:
    using MessageCallback =
        std::function<void(int32_t source, int32_t status, int32_t channel, int32_t data1, int32_t data2)>;
    using SysexCallback = std::function<void(int32_t source, const uint8_t* data, int32_t len)>;
    using PortInfo = MidiPortInfo;
    static constexpr int32_t kMaxSources = kMaxMidiSources;

    virtual ~IMidiInput() = default;

    bool open(MessageCallback cb, const std::vector<std::string>& disabledUids = {}) {
        return open(std::move(cb), nullptr, disabledUids);
    }
    virtual bool open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>& disabledUids) = 0;
    virtual void close() = 0;

    virtual const std::vector<std::string>& sourceUids() const = 0;
    virtual const std::vector<PortInfo>& ports() const = 0;

    static constexpr int dataBytes(int status) noexcept { return MidiParser::dataBytes(status); }
};

} // namespace nota::io::midi
