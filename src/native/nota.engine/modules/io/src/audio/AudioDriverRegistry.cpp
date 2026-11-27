// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/audio/AudioDriverRegistry.h"

#include "audio/drivers/AudioDrivers.h"
#include "nota/io/audio/IAudioOutputStream.h"
#include "nota/io/audio/IAudioInputStream.h"
#include "nota/io/audio/IAudioDriver.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/NullAudioDriver.h"

#include <memory>
#include <cstdint>
#include <mutex>
#include <string_view>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::audio {
namespace {

std::once_flag g_builtinAudioOnce;

std::shared_ptr<const IAudioDriver> resolveActiveDriver(AudioSystem system) {
    registerBuiltinAudioDrivers();
    auto& reg = AudioDriverRegistry::instance();
    if (auto d = reg.find(systemDriverId(system))) return d;
    return reg.defaultDriver();
}

} // namespace

AudioDriverRegistry& AudioDriverRegistry::instance() {
    static AudioDriverRegistry* reg = new AudioDriverRegistry();
    return *reg;
}

void AudioDriverRegistry::registerDriver(std::shared_ptr<const IAudioDriver> driver) {
    if (!driver) return;
    std::lock_guard<std::mutex> lock(mutex_);
    for (auto& existing : drivers_) {
        if (existing->id() == driver->id()) {
            existing = std::move(driver);
            return;
        }
    }
    drivers_.push_back(std::move(driver));
}

std::shared_ptr<const IAudioDriver> AudioDriverRegistry::find(std::string_view id) const {
    std::lock_guard<std::mutex> lock(mutex_);
    for (const auto& d : drivers_) {
        if (d->id() == id) return d;
    }
    return nullptr;
}

std::shared_ptr<const IAudioDriver> AudioDriverRegistry::defaultDriver() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return drivers_.empty() ? nullptr : drivers_.front();
}

std::vector<std::shared_ptr<const IAudioDriver>> AudioDriverRegistry::list() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return drivers_;
}

void registerBuiltinAudioDrivers() {
    std::call_once(g_builtinAudioOnce, [] {
        auto& reg = AudioDriverRegistry::instance();
#if defined(__APPLE__)
        reg.registerDriver(detail::createCoreAudioDriver());
#elif defined(_WIN32) || defined(__linux__)
        reg.registerDriver(detail::createMiniaudioDriver());
#endif
#if defined(NOTA_HAS_ASIO)
        reg.registerDriver(detail::createAsioDriver());
#endif
        reg.registerDriver(std::make_shared<NullAudioDriver>());
    });
}

std::string_view systemDriverId(AudioSystem system) noexcept {
#if defined(NOTA_HAS_ASIO)
    if (system == AudioSystem::Asio) return "asio";
#else
    (void)system;
#endif
#if defined(__APPLE__)
    return "coreaudio";
#elif defined(_WIN32) || defined(__linux__)
    return "miniaudio";
#else
    return "null";
#endif
}

bool asioSupported() noexcept {
#if defined(NOTA_HAS_ASIO)
    return true;
#else
    return false;
#endif
}

std::unique_ptr<IAudioOutputStream> createAudioOutputStream(AudioSystem system) {
    const auto d = resolveActiveDriver(system);
    return d ? d->createOutputStream() : nullptr;
}

std::unique_ptr<IAudioInputStream> createAudioInputStream(AudioSystem system) {
    const auto d = resolveActiveDriver(system);
    return d ? d->createInputStream() : nullptr;
}

std::vector<AudioDeviceInfo> enumerateAudioDevices(bool inputScope) {
    const auto d = resolveActiveDriver(AudioSystem::Native);
    return d ? d->enumerateDevices(inputScope) : std::vector<AudioDeviceInfo>{};
}

uint32_t resolveAudioDeviceId(std::string_view uid, bool inputScope) {
    const auto d = resolveActiveDriver(AudioSystem::Native);
    return d ? d->resolveDeviceId(uid, inputScope) : 0u;
}

std::string effectiveAudioDeviceUid(std::string_view uid, bool inputScope) {
    const auto d = resolveActiveDriver(AudioSystem::Native);
    return d ? d->effectiveDeviceUid(uid, inputScope) : std::string{};
}

} // namespace nota::io::audio
