// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/IAudioInputStream.h"
#include "nota/io/audio/IAudioOutputStream.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace nota::io::audio {

/// Platform audio driver plugin contract (CoreAudio, miniaudio, ASIO, NullAudio, etc.).
class NOTA_IO_API IAudioDriver {
  public:
    virtual ~IAudioDriver() = default;

    virtual std::string_view id() const noexcept = 0;
    virtual std::string_view displayName() const noexcept = 0;
    virtual DriverCapabilities capabilities() const noexcept = 0;

    virtual std::vector<AudioDeviceInfo> enumerateDevices(bool inputScope) const = 0;
    virtual uint32_t resolveDeviceId(std::string_view uid, bool inputScope) const = 0;
    virtual std::string effectiveDeviceUid(std::string_view uid, bool inputScope) const = 0;

    [[nodiscard]] virtual std::unique_ptr<IAudioOutputStream> createOutputStream() const = 0;
    [[nodiscard]] virtual std::unique_ptr<IAudioInputStream> createInputStream() const = 0;
};

} // namespace nota::io::audio
