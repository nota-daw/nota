// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/AudioIoTypes.h"

#include <cstdint>
#include <string>

namespace nota::io::audio {

/// Audio input capture stream abstraction.
class NOTA_IO_API IAudioInputStream {
  public:
    virtual ~IAudioInputStream() = default;

    virtual bool start(InputCallback cb, uint32_t device = 0, double preferredRate = 0.0) = 0;
    virtual void stop() = 0;
    virtual bool isRunning() const = 0;
    virtual double sampleRate() const = 0;
    virtual int32_t channelCount() const = 0;
    virtual std::string channelName(int32_t index) const = 0;
};

} // namespace nota::io::audio
