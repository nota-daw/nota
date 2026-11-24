// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CoreMIDI output (specs/051). Channel messages go out with MIDISend on a packet list; SysEx
// with MIDISendSysex, whose request and bytes must stay alive until CoreMIDI calls the
// completion proc — they are heap-owned and freed there.

#include "MidiOutput.h"
#include "MidiConfig.h"

#if defined(__APPLE__)

#import <CoreMIDI/CoreMIDI.h>

#include <cstring>
#include <vector>

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

namespace nota {

struct MidiOutput::Impl {
    MIDIClientRef client = 0;
    MIDIPortRef port = 0;
    MIDIEndpointRef dest = 0;
};

namespace {
struct SysexJob {
    MIDISysexSendRequest req{};
    std::vector<uint8_t> bytes;
};
void sysexDone(MIDISysexSendRequest* req) { delete static_cast<SysexJob*>(req->completionRefCon); }
} // namespace

MidiOutput::MidiOutput() : impl_(new Impl()) {}
MidiOutput::~MidiOutput() {
    close();
    delete impl_;
}

bool MidiOutput::open(const std::string& uid) {
    close();
    for (ItemCount i = 0, n = MIDIGetNumberOfDestinations(); i < n; ++i) {
        MIDIEndpointRef d = MIDIGetDestination(i);
        if (d && midiUidForEndpoint(d) == uid) {
            impl_->dest = d;
            break;
        }
    }
    if (!impl_->dest) return false;
    if (MIDIClientCreate(CFSTR("Nota Out"), nullptr, nullptr, &impl_->client) != noErr) return false;
    if (MIDIOutputPortCreate(impl_->client, CFSTR("Nota Out"), &impl_->port) != noErr) return false;
    return true;
}

bool MidiOutput::send(const uint8_t* data, int32_t len) {
    if (!impl_->port || !impl_->dest || !data || len <= 0) return false;
    int32_t i = 0;
    while (i < len) {
        if (data[i] == 0xF0) { // SysEx: up to its F7
            int32_t j = i;
            while (j < len && data[j] != 0xF7) ++j;
            if (j >= len) return false; // unterminated
            auto* job = new SysexJob();
            job->bytes.assign(data + i, data + j + 1);
            job->req.destination = impl_->dest;
            job->req.data = job->bytes.data();
            job->req.bytesToSend = static_cast<UInt32>(job->bytes.size());
            job->req.completionProc = sysexDone;
            job->req.completionRefCon = job;
            if (MIDISendSysex(&job->req) != noErr) {
                delete job;
                return false;
            }
            i = j + 1;
            continue;
        }
        // Channel messages up to the next SysEx, in one packet list.
        int32_t j = i;
        while (j < len && data[j] != 0xF0) ++j;
        Byte buffer[1024];
        auto* list = reinterpret_cast<MIDIPacketList*>(buffer);
        MIDIPacket* pkt = MIDIPacketListInit(list);
        pkt = MIDIPacketListAdd(list, sizeof buffer, pkt, 0, static_cast<ByteCount>(j - i), data + i);
        if (!pkt || MIDISend(impl_->port, impl_->dest, list) != noErr) return false;
        i = j;
    }
    return true;
}

void MidiOutput::close() {
    if (impl_->port) {
        MIDIPortDispose(impl_->port);
        impl_->port = 0;
    }
    if (impl_->client) {
        MIDIClientDispose(impl_->client);
        impl_->client = 0;
    }
    impl_->dest = 0;
}

} // namespace nota

#pragma clang diagnostic pop

#endif // defined(__APPLE__)
