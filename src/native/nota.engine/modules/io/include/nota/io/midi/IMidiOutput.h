// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"

#include <cstdint>
#include <string>

namespace nota::io::midi {

/// MIDI destination stream for channel messages and SysEx output (message thread only).
class NOTA_IO_API IMidiOutput {
  public:
    virtual ~IMidiOutput() = default;

    virtual bool open(const std::string& uid) = 0;
    virtual bool send(const uint8_t* data, int32_t len) = 0;
    virtual void close() = 0;
};

} // namespace nota::io::midi
