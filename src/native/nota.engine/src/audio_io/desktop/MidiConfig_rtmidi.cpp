// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Non-Apple device enumeration for MidiConfig.h (Windows + Linux, RtMidi
// WinMM / ALSA). Persistence (midi.json) lives in the shared ConfigJson.cpp.
// RtMidi exposes no numeric unique id, so the port's display name serves as the
// stable UID (a documented v1 limitation: two inputs with identical names collide).

#include "audio_io/MidiConfig.h"

#include <RtMidi.h>

#include <string>

namespace nota {

// portIndex is an RtMidi input port number; the UID is that port's name.
std::string midiUidForEndpoint(uint32_t endpoint) {
    try {
        RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
        if (endpoint < in.getPortCount()) return in.getPortName(endpoint);
    } catch (...) {
        // fall through
    }
    return {};
}

std::vector<MidiDeviceInfo> enumerateMidiInputs() {
    std::vector<MidiDeviceInfo> out;
    try {
        RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
        const unsigned int n = in.getPortCount();
        for (unsigned int i = 0; i < n; ++i) {
            MidiDeviceInfo info;
            info.name = in.getPortName(i);
            info.uid = info.name; // RtMidi has no numeric id
            if (info.uid.empty()) continue;
            out.push_back(std::move(info));
        }
    } catch (...) {
        // no MIDI subsystem / no ports
    }
    return out;
}

} // namespace nota
