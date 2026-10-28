// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ParamPath — where a parameter's owner sits once devices can be grouped into a rack
// (specs/044). Automation lanes, CV links and Arp CC routes used to name a device by its
// index in the track's chain; a device grouped into a rack lives one level down, in a
// chain of the rack at that index. The nested part is the chain's uid (stable while
// chains move, duplicate or swap instruments) and a slot in it. Racks nest one level
// (specs/041), so one step is all a path needs.

#pragma once

#include <cstdint>

namespace nota {

// A track-level device in the engine's rack addressing: >= 0 an audio effect, -1 the
// instrument, kMidiHostBase - i the track's i-th MIDI effect (Engine::kMidiRackBase).
constexpr int32_t kMidiHostBase = -1000;
inline bool isMidiHost(int32_t host) { return host <= kMidiHostBase; }
inline int32_t midiHost(int32_t midiIndex) { return kMidiHostBase - midiIndex; }
inline int32_t midiHostIndex(int32_t host) { return kMidiHostBase - host; }

struct ParamPath {
    int32_t host = -1;     // the track-level device (see above)
    uint64_t chainUid = 0; // 0 = the host itself; else a chain of the rack at host
    int32_t slot = -1;     // in that chain: >= 0 a device, -1 the instrument,
                           // RackCore::midiTarget(i) its i-th MIDI effect
    bool nested() const { return chainUid != 0; }
    bool operator==(const ParamPath&) const = default;
};

} // namespace nota
