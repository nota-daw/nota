// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Non-Apple MIDI input via RtMidi for Windows + Linux (WinMM on Windows; ALSA
// sequencer on Linux — the backend is picked by RtMidi's __WINDOWS_MM__ /
// __LINUX_ALSA__ compile define). Mirrors the macOS MidiInput.mm: connects to every
// enabled input source and reports note on/off through a callback. Each RtMidiIn
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

struct MidiInput::Impl {
    std::vector<std::unique_ptr<RtMidiIn>> ports;
    MidiInput::MessageCallback             cb;
};

// RtMidi thread: forward note on/off + control-change; skip everything else.
static void onMidiMessage(double /*timeStamp*/, std::vector<unsigned char>* message, void* userData) {
    auto* impl = static_cast<MidiInput::Impl*>(userData);
    if (!impl || !impl->cb || !message || message->size() < 3) return;
    const unsigned char status = (*message)[0] & 0xF0;
    if (status != 0x90 && status != 0x80 && status != 0xB0) return;
    impl->cb(status, (*message)[0] & 0x0F, (*message)[1] & 0x7F, (*message)[2] & 0x7F);
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
            port->setCallback(&onMidiMessage, impl_);
            impl_->ports.push_back(std::move(port));
        }
    } catch (...) {
        // partial connection is fine; whatever opened stays live
    }
    return true;
}

void MidiInput::close() {
    impl_->ports.clear(); // each RtMidiIn destructor closes its port
}

} // namespace nota
