// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/IAudioDriver.h"
#include "nota/io/audio/IAudioOutputStream.h"
#include "nota/io/audio/IAudioInputStream.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace nota::io::audio {

/// Deterministic device-free audio driver for headless runs and unit tests.
class NOTA_IO_API NullAudioDriver final : public IAudioDriver {
  public:
    std::string_view id() const noexcept override { return "null"; }
    std::string_view displayName() const noexcept override { return "Null Audio"; }
    DriverCapabilities capabilities() const noexcept override;

    std::vector<AudioDeviceInfo> enumerateDevices(bool inputScope) const override;
    uint32_t resolveDeviceId(std::string_view uid, bool inputScope) const override;
    std::string effectiveDeviceUid(std::string_view uid, bool inputScope) const override;

    [[nodiscard]] std::unique_ptr<IAudioOutputStream> createOutputStream() const override;
    [[nodiscard]] std::unique_ptr<IAudioInputStream> createInputStream() const override;
};

} // namespace nota::io::audio
