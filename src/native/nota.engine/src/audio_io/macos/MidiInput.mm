// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "MidiInput.h"
#include "MidiConfig.h"

#if defined(__APPLE__)

#import <CoreMIDI/CoreMIDI.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <mutex>

// Legacy MIDIReadProc byte-list API (simple, adequate for M2). The newer
// MIDIReceiveBlock/UMP path is a later refinement.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

namespace nota {

struct MidiInput::Impl {
    MIDIClientRef client = 0;
    MIDIPortRef port = 0;
    MidiInput::MessageCallback cb;
    MidiInput::SysexCallback sysex;
    // One parser per source: running status and a SysEx in progress survive across packets
    // (an MPE controller streams bend/pressure with running status). CoreMIDI calls the read
    // proc on one thread, so the fixed table needs no lock.
    MidiParser parsers[MidiInput::kMaxSources];
    std::vector<MidiInput::PortInfo> ports;
};

static void readProc(const MIDIPacketList* pktlist, void* refCon, void* srcConn) {
    auto* impl = static_cast<MidiInput::Impl*>(refCon);
    if (!impl->cb) return;
    const int32_t source =
        std::clamp(static_cast<int32_t>(reinterpret_cast<intptr_t>(srcConn)), 0, MidiInput::kMaxSources - 1);
    MidiParser& parser = impl->parsers[source];
    const MIDIPacket* packet = &pktlist->packet[0];
    for (unsigned p = 0; p < pktlist->numPackets; ++p) {
        parser.feed(
            packet->data, packet->length,
            [&](int status, int channel, int d1, int d2) { impl->cb(source, status, channel, d1, d2); },
            [&](const uint8_t* data, int32_t len) {
                if (impl->sysex) impl->sysex(source, data, len);
            });
        packet = MIDIPacketNext(packet);
    }
}

// The first destination of the source's entity: the same device's MIDI out.
static std::string pairedOutputUid(MIDIEndpointRef src) {
    MIDIEntityRef entity = 0;
    if (MIDIEndpointGetEntity(src, &entity) != noErr || !entity) return {};
    if (MIDIEntityGetNumberOfDestinations(entity) == 0) return {};
    MIDIEndpointRef dst = MIDIEntityGetDestination(entity, 0);
    return dst ? midiUidForEndpoint(dst) : std::string{};
}

MidiInput::MidiInput() : impl_(new Impl()) {}
MidiInput::~MidiInput() {
    close();
    delete impl_;
}

bool MidiInput::open(MessageCallback cb, const std::vector<std::string>& disabledUids) {
    return open(std::move(cb), nullptr, disabledUids);
}

bool MidiInput::open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>& disabledUids) {
    impl_->cb = std::move(cb);
    impl_->sysex = std::move(sysex);
    impl_->ports.clear();
    if (MIDIClientCreate(CFSTR("Nota"), nullptr, nullptr, &impl_->client) != noErr) return false;
    if (MIDIInputPortCreate(impl_->client, CFSTR("Nota In"), readProc, impl_, &impl_->port) != noErr) return false;

    for (auto& p : impl_->parsers) p.reset();
    uids_.clear();
    const ItemCount n = MIDIGetNumberOfSources();
    intptr_t index = 0;
    for (ItemCount i = 0; i < n; ++i) {
        MIDIEndpointRef src = MIDIGetSource(i);
        if (!src) continue;
        const std::string uid = midiUidForEndpoint(src);
        // Skip sources the user has switched off (empty list = connect all).
        if (!uid.empty() && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end()) continue;
        // The source's index rides along as the connection refCon (readProc's srcConn);
        // past kMaxSources they share the last slot.
        const intptr_t source = std::min<intptr_t>(index++, MidiInput::kMaxSources - 1);
        if (source == static_cast<intptr_t>(uids_.size())) uids_.push_back(uid);
        MIDIPortConnectSource(impl_->port, src, reinterpret_cast<void*>(source));
        PortInfo info;
        info.source = static_cast<int32_t>(source);
        info.uid = uid;
        info.name = midiEndpointName(src);
        if (info.name.empty()) info.name = "MIDI input " + uid;
        info.outputUid = pairedOutputUid(src);
        impl_->ports.push_back(std::move(info));
    }
    return true;
}

void MidiInput::close() {
    if (impl_->port) {
        MIDIPortDispose(impl_->port);
        impl_->port = 0;
    }
    if (impl_->client) {
        MIDIClientDispose(impl_->client);
        impl_->client = 0;
    }
    impl_->ports.clear();
}

const std::vector<MidiInput::PortInfo>& MidiInput::ports() const { return impl_->ports; }

// Sources and destinations by uid, compared with the last look. Cheap on CoreMIDI (no client
// needed), and a poll works whichever thread the app runs on — a client notification would
// only arrive on the run loop of the thread that created the client.
uint32_t MidiInput::portsGeneration() {
    static std::mutex mx;
    static std::string last;
    static uint32_t gen = 0;
    std::lock_guard lock(mx);
    std::string sig;
    for (ItemCount i = 0, n = MIDIGetNumberOfSources(); i < n; ++i) sig += midiUidForEndpoint(MIDIGetSource(i)) + ",";
    sig += "|";
    for (ItemCount i = 0, n = MIDIGetNumberOfDestinations(); i < n; ++i)
        sig += midiUidForEndpoint(MIDIGetDestination(i)) + ",";
    if (sig != last) {
        last = std::move(sig);
        ++gen;
    }
    return gen;
}

} // namespace nota

#pragma clang diagnostic pop

#endif // defined(__APPLE__)
