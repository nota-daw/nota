// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace nota::io::midi {

/// Zero-allocation MIDI byte-stream parser for MIDI threads and loopback test ports.
class MidiParser {
  public:
    static constexpr int32_t kMaxSysex = 512;

    static constexpr int dataBytes(int status) noexcept {
        switch (status & 0xF0) {
        case 0x80:
        case 0x90:
        case 0xA0:
        case 0xB0:
        case 0xE0: return 2;
        case 0xD0: return 1;
        default: return 0;
        }
    }

    void reset() noexcept {
        running_ = 0;
        pendingN_ = 0;
        inSysex_ = false;
        overflow_ = false;
        sysexLen_ = 0;
    }

    template <typename ChannelFn, typename SysexFn>
    void feed(const uint8_t* d, std::size_t len, ChannelFn&& onChannel, SysexFn&& onSysex) {
        if (!d) return;
        for (std::size_t i = 0; i < len; ++i) {
            const uint8_t b = d[i];
            if (b >= 0xF8) continue;
            if (inSysex_) {
                if (b == 0xF7) {
                    if (!overflow_ && sysexLen_ < kMaxSysex) {
                        sysex_[static_cast<std::size_t>(sysexLen_++)] = b;
                        onSysex(sysex_.data(), sysexLen_);
                    }
                    inSysex_ = false;
                    continue;
                }
                if (b & 0x80) {
                    inSysex_ = false;
                } else {
                    if (sysexLen_ < kMaxSysex - 1) sysex_[static_cast<std::size_t>(sysexLen_++)] = b;
                    else
                        overflow_ = true;
                    continue;
                }
            }
            if (b == 0xF0) {
                inSysex_ = true;
                overflow_ = false;
                sysexLen_ = 0;
                sysex_[static_cast<std::size_t>(sysexLen_++)] = b;
                running_ = 0;
                pendingN_ = 0;
                continue;
            }
            if (b >= 0xF0) {
                running_ = 0;
                pendingN_ = 0;
                continue;
            }
            if (b & 0x80) {
                running_ = b;
                pendingN_ = 0;
                continue;
            }
            const int n = dataBytes(running_);
            if (n == 0) continue;
            pending_[static_cast<std::size_t>(pendingN_++)] = b & 0x7F;
            if (pendingN_ == n) {
                onChannel(running_ & 0xF0, running_ & 0x0F, pending_[0], n > 1 ? pending_[1] : 0);
                pendingN_ = 0;
            }
        }
    }

  private:
    uint8_t running_ = 0;
    std::array<uint8_t, 2> pending_{};
    int pendingN_ = 0;
    bool inSysex_ = false;
    bool overflow_ = false;
    int32_t sysexLen_ = 0;
    std::array<uint8_t, static_cast<std::size_t>(kMaxSysex)> sysex_{};
};

} // namespace nota::io::midi
