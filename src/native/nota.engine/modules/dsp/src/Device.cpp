// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/Device.h"
#include "nota/dsp/rt/SilenceGate.h"

#include <atomic>
#include <cstdint>

namespace nota::dsp {
namespace {

std::atomic<int32_t> gHeldMidiKeyReaders{0};
std::atomic<uint64_t> gNextDeviceLineage{0};

} // namespace

HeldMidiKeyReader::HeldMidiKeyReader() noexcept {
    gHeldMidiKeyReaders.fetch_add(1, std::memory_order_relaxed);
}

HeldMidiKeyReader::HeldMidiKeyReader(const HeldMidiKeyReader&) noexcept {
    gHeldMidiKeyReaders.fetch_add(1, std::memory_order_relaxed);
}

HeldMidiKeyReader::~HeldMidiKeyReader() {
    gHeldMidiKeyReaders.fetch_sub(1, std::memory_order_relaxed);
}

bool heldMidiKeysRead() noexcept {
    return gHeldMidiKeyReaders.load(std::memory_order_relaxed) > 0;
}

uint64_t nextDeviceLineage() noexcept {
    return gNextDeviceLineage.fetch_add(1u, std::memory_order_relaxed) + 1u;
}

Device::~Device() {
    if (countedNeverSleep_.exchange(false, std::memory_order_acq_rel)) { adjustNeverSleepDevices(-1); }
}

void Device::setSleepMode(PluginSleepMode mode) {
    sleepMode_.store(mode, std::memory_order_release);
    const bool never = (mode == PluginSleepMode::Never);
    const bool prev = countedNeverSleep_.exchange(never, std::memory_order_acq_rel);
    if (prev != never) adjustNeverSleepDevices(never ? 1 : -1);
    wake();
}

} // namespace nota::dsp
