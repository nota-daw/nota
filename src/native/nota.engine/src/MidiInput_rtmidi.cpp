// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Non-Apple MIDI input via RtMidi for Windows + Linux (WinMM on Windows; ALSA
// sequencer on Linux — the backend is picked by RtMidi's __WINDOWS_MM__ /
// __LINUX_ALSA__ compile define). Mirrors the macOS MidiInput.mm: connects to every
// enabled input source and reports channel-voice messages through a callback. Each RtMidiIn
// instance drives one port (RtMidi is single-port), so we hold one per connected
// source. The callback runs on RtMidi's thread and only forwards to the engine's
// note lambda, which pushes into a lock-free queue (AR-4/AR-6).

#include "MidiInput.h"
#include "MidiConfig.h"

#include "rtmidi/RtMidi.h"

#include <algorithm>
#include <memory>
#include <vector>

namespace nota {

// One connected port: its callback needs the shared callback and its own source index.
struct PortTag {
    MidiInput::MessageCallback* cb = nullptr;
    int32_t                     source = 0;
};

struct MidiInput::Impl {
    std::vector<std::unique_ptr<RtMidiIn>> ports;
    std::vector<std::unique_ptr<PortTag>>  tags;
    MidiInput::MessageCallback             cb;
};

// RtMidi thread: forward the channel-voice messages we use (RtMidi has already resolved
// running status into whole messages); skip everything else.
static void onMidiMessage(double /*timeStamp*/, std::vector<unsigned char>* message, void* userData) {
    auto* tag = static_cast<PortTag*>(userData);
    if (!tag || !tag->cb || !*tag->cb || !message || message->empty()) return;
    const int status = (*message)[0];
    const int n = MidiInput::dataBytes(status);
    if (n == 0 || static_cast<int>(message->size()) < 1 + n) return;
    (*tag->cb)(tag->source, status & 0xF0, status & 0x0F, (*message)[1] & 0x7F, n > 1 ? (*message)[2] & 0x7F : 0);
}

MidiInput::MidiInput() : impl_(new Impl()) {}
MidiInput::~MidiInput() { close(); delete impl_; }

bool MidiInput::open(MessageCallback cb, const std::vector<std::string>& disabledUids) {
    impl_->cb = std::move(cb);
    try {
        RtMidiIn scanner(RtMidi::UNSPECIFIED, "Nota-scan");
        const unsigned int n = scanner.getPortCount();
        for (unsigned int i = 0; i < n; ++i) {
            const std::string uid = scanner.getPortName(i); // UID == port name
            if (!uid.empty()
                && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end())
                continue; // user switched this source off (empty list = connect all)

            auto port = std::make_unique<RtMidiIn>(RtMidi::UNSPECIFIED, "Nota In");
            port->openPort(i, "Nota In");
            port->ignoreTypes(true, true, true);          // drop sysex/timing/sensing
            auto tag = std::make_unique<PortTag>();
            tag->cb = &impl_->cb;
            tag->source = std::min<int32_t>(static_cast<int32_t>(impl_->ports.size()), kMaxSources - 1);
            port->setCallback(&onMidiMessage, tag.get());
            impl_->tags.push_back(std::move(tag));
            impl_->ports.push_back(std::move(port));
        }
    } catch (...) {
        // partial connection is fine; whatever opened stays live
    }
    return true;
}

void MidiInput::close() {
    impl_->ports.clear(); // each RtMidiIn destructor closes its port
    impl_->tags.clear();
}

} // namespace nota
