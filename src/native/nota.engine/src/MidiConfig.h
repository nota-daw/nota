// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MIDI device configuration (M7-2): which CoreMIDI input sources the engine
// listens to, persisted across launches, plus source enumeration. Sources are
// keyed by their stable unique id (kMIDIPropertyUniqueID, an SInt32 stringified
// so it reuses the same string-key plumbing as AudioConfig). The persisted
// value is a blocklist of DISABLED uids — empty means "all inputs on", so a
// newly connected controller is active by default.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nota {

struct MidiConfig {
    std::vector<std::string> disabledInputUids; // empty = every input on
};

struct MidiDeviceInfo {
    std::string uid;
    std::string name;
};

// All current CoreMIDI input sources (uid + display name).
std::vector<MidiDeviceInfo> enumerateMidiInputs();

// Stable uid of a CoreMIDI endpoint (a MIDIEndpointRef, carried as uint32_t so
// this header stays free of CoreMIDI types). Empty if it has no unique id.
std::string midiUidForEndpoint(uint32_t endpoint);

// Persistence: ~/Library/Application Support/Nota/midi.json (atomic write).
MidiConfig loadMidiConfig();
void       saveMidiConfig(const MidiConfig& cfg);

} // namespace nota
