// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Non-Apple MIDI input via RtMidi for Windows + Linux (WinMM on Windows; ALSA
// sequencer on Linux — the backend is picked by RtMidi's __WINDOWS_MM__ /
// __LINUX_ALSA__ compile define). Mirrors the macOS MidiInput.mm: connects to every
// enabled input source and reports channel-voice messages through a callback. Each RtMidiIn
// instance drives one port (RtMidi is single-port), so we hold one per connected
// source. The callback runs on RtMidi's thread and only forwards to the engine's
// note lambda, which pushes into a lock-free queue (AR-4/AR-6). Complete SysEx messages
// (RtMidi delivers them whole) go to the SysEx callback, for control surfaces (specs/051).

#include "audio_io/MidiInput.h"
#include "audio_io/MidiConfig.h"

#include <RtMidi.h>

#include <algorithm>
#include <chrono>
#include <memory>
#include <mutex>
#include <vector>

namespace nota {

// One connected port: its callback needs the shared callback and its own source index.
struct PortTag {
    MidiInput::MessageCallback* cb = nullptr;
    MidiInput::SysexCallback* sysex = nullptr;
    int32_t source = 0;
};

struct MidiInput::Impl {
    std::vector<std::unique_ptr<RtMidiIn>> ports;
    std::vector<std::unique_ptr<PortTag>> tags;
    MidiInput::MessageCallback cb;
    MidiInput::SysexCallback sysex;
    std::vector<MidiInput::PortInfo> info;
};

// RtMidi thread: forward the channel-voice messages we use (RtMidi has already resolved
// running status into whole messages); skip everything else.
static void onMidiMessage(double /*timeStamp*/, std::vector<unsigned char>* message, void* userData) {
    auto* tag = static_cast<PortTag*>(userData);
    if (!tag || !tag->cb || !*tag->cb || !message || message->empty()) return;
    const int status = (*message)[0];
    if (status == 0xF0) {
        const auto len = static_cast<int32_t>(message->size());
        if (tag->sysex && *tag->sysex && len <= MidiParser::kMaxSysex && message->back() == 0xF7)
            (*tag->sysex)(tag->source, message->data(), len);
        return;
    }
    const int n = MidiInput::dataBytes(status);
    if (n == 0 || static_cast<int>(message->size()) < 1 + n) return;
    (*tag->cb)(tag->source, status & 0xF0, status & 0x0F, (*message)[1] & 0x7F, n > 1 ? (*message)[2] & 0x7F : 0);
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
    impl_->info.clear();
    uids_.clear();
    try {
        RtMidiIn scanner(RtMidi::UNSPECIFIED, "Nota-scan");
        std::vector<std::string> outputs;
        try {
            RtMidiOut outScanner(RtMidi::UNSPECIFIED, "Nota-scan");
            for (unsigned int o = 0, m = outScanner.getPortCount(); o < m; ++o) outputs.push_back(outScanner.getPortName(o));
        } catch (...) {
        }
        const unsigned int n = scanner.getPortCount();
        for (unsigned int i = 0; i < n; ++i) {
            const std::string uid = scanner.getPortName(i); // UID == port name
            if (!uid.empty() && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end())
                continue; // user switched this source off (empty list = connect all)

            auto port = std::make_unique<RtMidiIn>(RtMidi::UNSPECIFIED, "Nota In");
            port->openPort(i, "Nota In");
            port->ignoreTypes(false, true, true); // keep sysex (control surfaces); drop timing/sensing
            auto tag = std::make_unique<PortTag>();
            tag->cb = &impl_->cb;
            tag->sysex = &impl_->sysex;
            tag->source = std::min<int32_t>(static_cast<int32_t>(impl_->ports.size()), kMaxSources - 1);
            if (static_cast<int32_t>(uids_.size()) < kMaxSources) uids_.push_back(uid);
            PortInfo info;
            info.source = tag->source;
            info.uid = uid;
            info.name = uid;
            // The output of the same device carries the same port name on WinMM and ALSA.
            if (std::find(outputs.begin(), outputs.end(), uid) != outputs.end()) info.outputUid = uid;
            impl_->info.push_back(std::move(info));
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
    impl_->info.clear();
}

const std::vector<MidiInput::PortInfo>& MidiInput::ports() const { return impl_->info; }

// RtMidi has no change notification: compare the input and output port names, at most once
// a second (each look opens a scanning client).
uint32_t MidiInput::portsGeneration() {
    static std::mutex mx;
    static std::string last;
    static uint32_t gen = 0;
    static std::chrono::steady_clock::time_point lastLook{};
    std::lock_guard lock(mx);
    const auto now = std::chrono::steady_clock::now();
    if (gen != 0 && now - lastLook < std::chrono::seconds(1)) return gen;
    lastLook = now;
    std::string sig;
    try {
        RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
        for (unsigned int i = 0, n = in.getPortCount(); i < n; ++i) sig += in.getPortName(i) + ",";
        sig += "|";
        RtMidiOut out(RtMidi::UNSPECIFIED, "Nota-scan");
        for (unsigned int i = 0, n = out.getPortCount(); i < n; ++i) sig += out.getPortName(i) + ",";
    } catch (...) {
        return gen;
    }
    if (gen == 0 || sig != last) {
        last = std::move(sig);
        ++gen;
    }
    return gen;
}

} // namespace nota
