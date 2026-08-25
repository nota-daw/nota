// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// CoreMIDI input (macOS). Connects to all MIDI sources and reports note on/off
// via a callback. The callback runs on CoreMIDI's thread and must only push into
// a lock-free queue (the engine routes it to the live MIDI queue — AR-4/AR-6).

#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace nota {

class MidiInput {
public:
    // Reports a channel-voice message: status is the high nibble (0x80 note-off,
    // 0x90 note-on, 0xB0 control-change), channel the low nibble (0..15), and
    // data1/data2 the two 7-bit bytes. The engine turns note messages into
    // noteOn/noteOff and also feeds CC + note-on into the MIDI-learn up-queue.
    using MessageCallback = std::function<void(int32_t status, int32_t channel, int32_t data1, int32_t data2)>;

    MidiInput();
    ~MidiInput();

    // Opens a client + input port and connects the current sources, skipping any
    // whose stable uid (kMIDIPropertyUniqueID, stringified) is in `disabledUids`.
    // An empty list connects every source (M7-2).
    bool open(MessageCallback cb, const std::vector<std::string>& disabledUids = {});
    void close();

    // Public so the file-local CoreMIDI read proc can reach it.
    struct Impl;

private:
    Impl* impl_ = nullptr;
};

} // namespace nota
