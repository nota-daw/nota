// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One MIDI byte-stream parser for every place raw bytes come in: the CoreMIDI read proc,
// the engine's test ports (specs/051) and the self-test. It resolves running status into
// whole channel-voice messages and reassembles SysEx across packets, so all of them hand
// the engine the same messages. Real-time bytes (0xF8..0xFF) may sit anywhere — even inside
// a SysEx — and are skipped. A SysEx longer than kMaxSysex is discarded up to its F7.
// No allocation: it runs on the MIDI thread.

#pragma once

#include <cstddef>
#include <cstdint>

namespace nota {

class MidiParser {
  public:
    static constexpr int32_t kMaxSysex = 512;

    // Data bytes that follow a channel-voice status (0 = not one we report).
    static int dataBytes(int status) {
        switch (status & 0xF0) {
        case 0x80:
        case 0x90:
        case 0xA0:
        case 0xB0:
        case 0xE0: return 2;
        case 0xD0: return 1;
        default: return 0; // program change and system messages are skipped
        }
    }

    void reset() {
        running_ = 0;
        inSysex_ = false;
        overflow_ = false;
        sysexLen_ = 0;
    }

    // onChannel(status & 0xF0, channel, d1, d2); onSysex(const uint8_t* data, int32_t len) gets
    // a complete F0 … F7 message.
    template <typename ChannelFn, typename SysexFn>
    void feed(const uint8_t* d, size_t len, ChannelFn&& onChannel, SysexFn&& onSysex) {
        for (size_t i = 0; i < len; ++i) {
            const uint8_t b = d[i];
            if (b >= 0xF8) continue; // real-time: interleaved anywhere, ignore
            if (inSysex_) {
                if (b == 0xF7) {
                    if (!overflow_ && sysexLen_ < kMaxSysex) {
                        sysex_[sysexLen_++] = b;
                        onSysex(static_cast<const uint8_t*>(sysex_), sysexLen_);
                    }
                    inSysex_ = false;
                    continue;
                }
                if (b & 0x80) { // a status byte inside SysEx ends it (malformed): drop it, parse b
                    inSysex_ = false;
                } else {
                    if (sysexLen_ < kMaxSysex - 1) sysex_[sysexLen_++] = b;
                    else overflow_ = true;
                    continue;
                }
            }
            if (b == 0xF0) {
                inSysex_ = true;
                overflow_ = false;
                sysexLen_ = 0;
                sysex_[sysexLen_++] = b;
                running_ = 0;
                continue;
            }
            if (b >= 0xF0) { // system common: clears running status, data ignored
                running_ = 0;
                pendingN_ = 0;
                continue;
            }
            if (b & 0x80) {
                running_ = b;
                pendingN_ = 0;
                continue;
            }
            // data byte
            const int n = dataBytes(running_);
            if (n == 0) continue; // no status yet
            pending_[pendingN_++] = b & 0x7F;
            if (pendingN_ == n) {
                onChannel(running_ & 0xF0, running_ & 0x0F, pending_[0], n > 1 ? pending_[1] : 0);
                pendingN_ = 0;
            }
        }
    }

  private:
    uint8_t running_ = 0;
    uint8_t pending_[2] = {};
    int pendingN_ = 0;
    bool inSysex_ = false;
    bool overflow_ = false;
    int32_t sysexLen_ = 0;
    uint8_t sysex_[kMaxSysex] = {};
};

} // namespace nota
