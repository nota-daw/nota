// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/MidiDevice.h"

#include <atomic>
#include <cstdint>

namespace nota::dsp {
namespace {

std::atomic<uint64_t> gNextMidiDeviceLineage{0};

} // namespace

uint64_t nextMidiDeviceLineage() noexcept {
    return gNextMidiDeviceLineage.fetch_add(1u, std::memory_order_relaxed) + 1u;
}

MidiDevice::~MidiDevice() = default;

} // namespace nota::dsp
