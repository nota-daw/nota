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
#include <mutex>
#include <string>
#include <string_view>
#include <vector>

namespace nota::io::audio {

/// Thread-safe registry of audio drivers (built-in platform drivers + headless/test drivers).
class NOTA_IO_API AudioDriverRegistry {
  public:
    static AudioDriverRegistry& instance();

    void registerDriver(std::shared_ptr<const IAudioDriver> driver);
    std::shared_ptr<const IAudioDriver> find(std::string_view id) const;
    std::shared_ptr<const IAudioDriver> defaultDriver() const;
    std::vector<std::shared_ptr<const IAudioDriver>> list() const;

  private:
    AudioDriverRegistry() = default;
    mutable std::mutex mutex_;
    std::vector<std::shared_ptr<const IAudioDriver>> drivers_;
};

/// Idempotently registers the platform's built-in audio drivers and the NullAudio fallback.
NOTA_IO_API void registerBuiltinAudioDrivers();

NOTA_IO_API std::string_view systemDriverId(AudioSystem system) noexcept;
NOTA_IO_API bool asioSupported() noexcept;

[[nodiscard]] NOTA_IO_API std::unique_ptr<IAudioOutputStream> createAudioOutputStream(
    AudioSystem system = AudioSystem::Native);
[[nodiscard]] NOTA_IO_API std::unique_ptr<IAudioInputStream> createAudioInputStream(
    AudioSystem system = AudioSystem::Native);

NOTA_IO_API std::vector<AudioDeviceInfo> enumerateAudioDevices(bool inputScope);
NOTA_IO_API uint32_t resolveAudioDeviceId(std::string_view uid, bool inputScope);
NOTA_IO_API std::string effectiveAudioDeviceUid(std::string_view uid, bool inputScope);

} // namespace nota::io::audio
