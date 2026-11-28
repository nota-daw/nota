// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MidiTypes — block-relative MIDI event & held-note bitmask tracking.

#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <type_traits>

namespace nota::dsp {

// Trivially default-constructible on purpose: the audio render loop keeps a
// 1024-entry array of these on the stack for every instrument track every block,
// and default member initializers would zero ~20 KB per call.
struct MidiEv {
    int32_t off;
    bool on;
    int32_t pitch;
    float vel;
    float dur;
};
static_assert(std::is_trivially_default_constructible_v<MidiEv>);

struct MidiKeyNotes {
    int32_t mask = 0;
    std::array<uint16_t, 128> held{};

    void apply(const MidiEv& event) noexcept {
        if (event.pitch < 0 || static_cast<std::size_t>(event.pitch) >= held.size()) return;
        const std::size_t p = static_cast<std::size_t>(event.pitch);
        auto& count = held[p];
        const int32_t pitchClass = event.pitch % 12;
        if (event.on && event.vel > 0.0f) {
            if (count < UINT16_MAX) ++count;
            mask |= (1 << pitchClass);
        } else if (count != 0u && --count == 0u) {
            for (std::size_t pitch = static_cast<std::size_t>(pitchClass); pitch < held.size(); pitch += 12u) {
                if (held[pitch] != 0u) return;
            }
            mask &= ~(1 << pitchClass);
        }
    }
};

struct MidiKeyBlock {
    int32_t sourceTrack = -1;
    const MidiEv* events = nullptr;
    int32_t count = 0;
    const MidiKeyNotes* start = nullptr;
    int32_t endMask = 0;
};

} // namespace nota::dsp
