// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

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
    Byte                 running[MidiInput::kMaxSources] = {};
};

static void readProc(const MIDIPacketList* pktlist, void* refCon, void* srcConn) {
    auto* impl = static_cast<MidiInput::Impl*>(refCon);
    if (!impl->cb) return;
    const int32_t source = static_cast<int32_t>(reinterpret_cast<intptr_t>(srcConn));
    // Running status survives across packets of one source (an MPE controller streams
    // bend/pressure with it); a fixed table — CoreMIDI calls this on one thread.
    Byte& running = impl->running[std::clamp(source, 0, MidiInput::kMaxSources - 1)];
    const MIDIPacket* packet = &pktlist->packet[0];
    for (unsigned p = 0; p < pktlist->numPackets; ++p) {
        const Byte* d = packet->data;
        const UInt16 len = packet->length;
        UInt16 i = 0;
        while (i < len) {
            const Byte b = d[i];
            if (b >= 0xF8) { ++i; continue; }                       // real-time: interleaved, ignore
            if (b == 0xF0) {                                        // sysex: skip to its end
                while (i < len && d[i] != 0xF7) ++i;
                ++i; running = 0; continue;
            }
            if (b >= 0xF0) { ++i; running = 0; continue; }          // system common: skip
            Byte status = running;
            if (b & 0x80) { status = b; running = b; ++i; }
            const int n = MidiInput::dataBytes(status);
            if (n == 0 || i + n > len) { ++i; continue; }           // no status yet / truncated
            const int d1 = d[i] & 0x7F, d2 = n > 1 ? d[i + 1] & 0x7F : 0;
            impl->cb(source, status & 0xF0, status & 0x0F, d1, d2);
            i += n;
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

    std::fill(std::begin(impl_->running), std::end(impl_->running), Byte{0});
    const ItemCount n = MIDIGetNumberOfSources();
    intptr_t index = 0;
    for (ItemCount i = 0; i < n; ++i) {
        MIDIEndpointRef src = MIDIGetSource(i);
        if (!src) continue;
        const std::string uid = midiUidForEndpoint(src);
        // Skip sources the user has switched off (empty list = connect all).
        if (!uid.empty()
            && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end())
            continue;
        // The source's index rides along as the connection refCon (readProc's srcConn);
        // past kMaxSources they share the last slot.
        MIDIPortConnectSource(impl_->port, src, reinterpret_cast<void*>(std::min<intptr_t>(index++, MidiInput::kMaxSources - 1)));
    }
    return true;
}

void MidiInput::close() {
    if (impl_->port) { MIDIPortDispose(impl_->port); impl_->port = 0; }
    if (impl_->client) { MIDIClientDispose(impl_->client); impl_->client = 0; }
}

} // namespace nota

#pragma clang diagnostic pop
