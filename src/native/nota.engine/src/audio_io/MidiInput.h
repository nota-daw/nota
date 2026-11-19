// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CoreMIDI input (macOS). Connects to all MIDI sources and reports channel messages
// via a callback. The callback runs on CoreMIDI's thread and must only push into
// a lock-free queue (the engine routes it to the live MIDI queue — AR-4/AR-6).
// Complete SysEx messages go to a second callback, and the connected ports are listed with
// their paired outputs, for control surfaces (specs/051).

#pragma once

#include "audio_io/MidiParser.h"

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
    using MessageCallback =
        std::function<void(int32_t source, int32_t status, int32_t channel, int32_t data1, int32_t data2)>;
    static constexpr int32_t kMaxSources = 16;

    // A complete SysEx message (F0 … F7 inclusive, at most MidiParser::kMaxSysex bytes), on the
    // MIDI thread, from the same source numbering as MessageCallback.
    using SysexCallback = std::function<void(int32_t source, const uint8_t* data, int32_t len)>;

    // A connected input as the MIDI thread numbers it, with the output of the same device
    // (CoreMIDI: the entity's first destination; RtMidi: the output with the same name).
    struct PortInfo {
        int32_t source = 0;
        std::string uid;
        std::string name;
        std::string outputUid; // empty = none
    };

    MidiInput();
    ~MidiInput();

    // Opens a client + input port and connects the current sources, skipping any
    // whose stable uid (kMIDIPropertyUniqueID, stringified) is in `disabledUids`.
    // An empty list connects every source (M7-2).
    bool open(MessageCallback cb, const std::vector<std::string>& disabledUids = {});
    bool open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>& disabledUids);
    void close();
    // The uid of each connected source by its index (specs/086), filled by open(). Sources
    // past kMaxSources share the last index; that index then names the first of them.
    const std::vector<std::string>& sourceUids() const { return uids_; }

    // The inputs connected by the last open() (empty after close()). Message thread.
    const std::vector<PortInfo>& ports() const;

    // Bumped whenever MIDI sources / destinations appear or disappear. CoreMIDI reports it
    // (a client notification); on RtMidi the port lists are compared at most once a second.
    static uint32_t portsGeneration();

    // Data bytes that follow a channel-voice status (0 = not one we report).
    static int dataBytes(int status) { return MidiParser::dataBytes(status); }

    // Public so the file-local CoreMIDI read proc can reach it.
    struct Impl;

  private:
    Impl* impl_ = nullptr;
    std::vector<std::string> uids_;
};

} // namespace nota
