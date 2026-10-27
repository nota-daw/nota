// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Finding the owner of a nested parameter (specs/044) on a track snapshot: the rack at a
// ParamPath host, then its chain child. Used by automation, CV links, Arp CC routes and
// the sidechain feed. Reads the published graph and the rack's live snapshot only and
// allocates nothing, so the audio thread may call it (AR-6).

#pragma once

#include "engine/ParamPath.h"
#include "engine/Track.h"
#include "devices/racks/MidiRack.h"
#include "devices/racks/RackDevice.h"
#include "devices/racks/RackInstrument.h"

namespace nota {

// The rack at a track-level host (null when the host isn't a rack).
inline const RackCore* rackAtHost(const Track& t, int32_t host) {
    if (isMidiHost(host)) {
        const int32_t mi = midiHostIndex(host);
        if (mi < 0 || mi >= static_cast<int32_t>(t.midiEffects.size()) || !t.midiEffects[mi]) return nullptr;
        return t.midiEffects[mi]->asRack();
    }
    if (host == -1) return t.instrument ? t.instrument->asRack() : nullptr;
    if (host < 0 || host >= static_cast<int32_t>(t.devices.size()) || !t.devices[host]) return nullptr;
    return t.devices[host]->asRack();
}

// The child a nested path names; exactly one pointer is set, or none when the rack, the
// chain or the slot is gone.
struct ParamOwner {
    Device* device = nullptr;
    MidiDevice* midi = nullptr;
    Instrument* instrument = nullptr;
};
inline ParamOwner resolveNested(const Track& t, int32_t host, uint64_t chainUid, int32_t slot) {
    ParamOwner o;
    const RackCore* r = rackAtHost(t, host);
    if (!r) return o;
    if (slot >= 0) o.device = r->childDevice(chainUid, slot);
    else if (RackCore::isMidiTarget(slot)) o.midi = r->childMidi(chainUid, slot);
    else if (slot == -1) o.instrument = r->childInstrument(chainUid);
    return o;
}
inline ParamOwner resolveNested(const Track& t, const ParamPath& p) {
    return resolveNested(t, p.host, p.chainUid, p.slot);
}

} // namespace nota
