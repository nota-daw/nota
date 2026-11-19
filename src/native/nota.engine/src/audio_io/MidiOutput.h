// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI output to one destination (specs/051): control-surface feedback (LEDs, motor faders,
// scribble strips), DAW-mode init SysEx and identity requests. Message thread only — never
// the audio thread. CoreMIDI on macOS, RtMidiOut (WinMM / ALSA) elsewhere. A destination is
// named by the uid MidiInput::PortInfo::outputUid reports.

#pragma once

#include <cstdint>
#include <string>

namespace nota {

class MidiOutput {
  public:
    MidiOutput();
    ~MidiOutput();
    MidiOutput(const MidiOutput&) = delete;
    MidiOutput& operator=(const MidiOutput&) = delete;

    bool open(const std::string& uid);
    // One or more complete messages; SysEx allowed. False if not open or the send failed.
    bool send(const uint8_t* data, int32_t len);
    void close();

    struct Impl;

  private:
    Impl* impl_ = nullptr;
};

} // namespace nota
