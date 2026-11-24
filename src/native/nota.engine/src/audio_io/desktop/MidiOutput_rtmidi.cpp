// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI output via RtMidiOut for Windows + Linux (specs/051). A destination's uid is its port
// name, like the inputs (MidiInput_rtmidi.cpp). RtMidiOut::sendMessage takes one message at a
// time, so the byte stream is split on status bytes.

#include "audio_io/MidiOutput.h"
#include "audio_io/MidiParser.h"

#include <RtMidi.h>

#include <memory>
#include <vector>

namespace nota {

struct MidiOutput::Impl {
    std::unique_ptr<RtMidiOut> out;
};

MidiOutput::MidiOutput() : impl_(new Impl()) {}
MidiOutput::~MidiOutput() {
    close();
    delete impl_;
}

bool MidiOutput::open(const std::string& uid) {
    close();
    try {
        auto out = std::make_unique<RtMidiOut>(RtMidi::UNSPECIFIED, "Nota Out");
        for (unsigned int i = 0, n = out->getPortCount(); i < n; ++i) {
            if (out->getPortName(i) == uid) {
                out->openPort(i, "Nota Out");
                impl_->out = std::move(out);
                return true;
            }
        }
    } catch (...) {
    }
    return false;
}

bool MidiOutput::send(const uint8_t* data, int32_t len) {
    if (!impl_->out || !data || len <= 0) return false;
    try {
        int32_t i = 0;
        while (i < len) {
            int32_t j = i + 1;
            if (data[i] == 0xF0) {
                while (j < len && data[j - 1] != 0xF7) ++j;
                if (data[j - 1] != 0xF7) return false; // unterminated
            } else {
                if (!(data[i] & 0x80)) return false; // must start on a status byte
                j = i + 1 + MidiParser::dataBytes(data[i]);
                if (j > len) return false;
            }
            std::vector<unsigned char> msg(data + i, data + j);
            impl_->out->sendMessage(&msg);
            i = j;
        }
    } catch (...) {
        return false;
    }
    return true;
}

void MidiOutput::close() { impl_->out.reset(); }

} // namespace nota
