// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

#include "MidiInput.h"
#include "MidiConfig.h"

#import <CoreMIDI/CoreMIDI.h>

#include <algorithm>

// Legacy MIDIReadProc byte-list API (simple, adequate for M2). The newer
// MIDIReceiveBlock/UMP path is a later refinement.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

namespace nota {

struct MidiInput::Impl {
    MIDIClientRef        client = 0;
    MIDIPortRef          port = 0;
    MidiInput::MessageCallback cb;
};

static void readProc(const MIDIPacketList* pktlist, void* refCon, void* /*srcConn*/) {
    auto* impl = static_cast<MidiInput::Impl*>(refCon);
    if (!impl->cb) return;
    const MIDIPacket* packet = &pktlist->packet[0];
    for (unsigned p = 0; p < pktlist->numPackets; ++p) {
        const Byte* d = packet->data;
        UInt16 len = packet->length;
        for (UInt16 i = 0; i + 2 < len + 1; ) {
            const Byte status = d[i] & 0xF0;
            // Note on/off and control-change all carry two data bytes; report them
            // uniformly and let the engine route. Skip anything else (sysex, etc.).
            if ((status == 0x90 || status == 0x80 || status == 0xB0) && i + 2 < len) {
                impl->cb(status, d[i] & 0x0F, d[i + 1] & 0x7F, d[i + 2] & 0x7F);
                i += 3;
            } else {
                i += 1; // skip unknown/other messages
            }
        }
        packet = MIDIPacketNext(packet);
    }
}

MidiInput::MidiInput() : impl_(new Impl()) {}
MidiInput::~MidiInput() { close(); delete impl_; }

bool MidiInput::open(MessageCallback cb, const std::vector<std::string>& disabledUids) {
    impl_->cb = std::move(cb);
    if (MIDIClientCreate(CFSTR("Nota"), nullptr, nullptr, &impl_->client) != noErr)
        return false;
    if (MIDIInputPortCreate(impl_->client, CFSTR("Nota In"), readProc, impl_, &impl_->port) != noErr)
        return false;

    const ItemCount n = MIDIGetNumberOfSources();
    for (ItemCount i = 0; i < n; ++i) {
        MIDIEndpointRef src = MIDIGetSource(i);
        if (!src) continue;
        const std::string uid = midiUidForEndpoint(src);
        // Skip sources the user has switched off (empty list = connect all).
        if (!uid.empty()
            && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end())
            continue;
        MIDIPortConnectSource(impl_->port, src, nullptr);
    }
    return true;
}

void MidiInput::close() {
    if (impl_->port) { MIDIPortDispose(impl_->port); impl_->port = 0; }
    if (impl_->client) { MIDIClientDispose(impl_->client); impl_->client = 0; }
}

} // namespace nota

#pragma clang diagnostic pop
