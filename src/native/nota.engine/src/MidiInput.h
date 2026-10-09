// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CoreMIDI input (macOS). Connects to all MIDI sources and reports channel messages
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
    // 0x90 note-on, 0xA0 poly pressure, 0xB0 control-change, 0xD0 channel pressure,
    // 0xE0 pitch bend), channel the low nibble (0..15), and data1/data2 the 7-bit data
    // bytes (channel pressure: its value in data1, data2 = 0). source identifies the
    // connected input (0..kMaxSources-1), so per-source channel state (MPE) stays apart.
    // The engine turns these into notes + expression and feeds CC + note-on to MIDI learn.
    using MessageCallback = std::function<void(int32_t source, int32_t status, int32_t channel, int32_t data1, int32_t data2)>;
    static constexpr int32_t kMaxSources = 16;

    MidiInput();
    ~MidiInput();

    // Opens a client + input port and connects the current sources, skipping any
    // whose stable uid (kMIDIPropertyUniqueID, stringified) is in `disabledUids`.
    // An empty list connects every source (M7-2).
    bool open(MessageCallback cb, const std::vector<std::string>& disabledUids = {});
    void close();

    // Data bytes that follow a channel-voice status (0 = not one we report).
    static int dataBytes(int status) {
        switch (status & 0xF0) {
            case 0x80: case 0x90: case 0xA0: case 0xB0: case 0xE0: return 2;
            case 0xD0: return 1;
            default: return 0;   // program change and system messages are skipped
        }
    }

    // Public so the file-local CoreMIDI read proc can reach it.
    struct Impl;

private:
    Impl* impl_ = nullptr;
};

} // namespace nota
