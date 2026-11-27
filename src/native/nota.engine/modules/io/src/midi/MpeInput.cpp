// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/midi/MpeInput.h"
#include "nota/dsp/rt/ExpressionTypes.h"
#include "nota/dsp/rt/MidiControllerTypes.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>

namespace nota::io::midi {

void MpeInput::message(int32_t status, int32_t channel, int32_t d1, int32_t d2, Sink& out) {
    if (channel < 0 || channel > 15) return;
    Channel& c = ch_[static_cast<std::size_t>(channel)];
    const bool member = isMember(channel);
    switch (status) {
    case 0x90:
        if (d2 > 0) {
            out.mpeNoteOn(d1, static_cast<float>(d2) / 127.0f);
            c.held[static_cast<std::size_t>(d1 >> 5)] |= 1u << (d1 & 31);
            if (member) {
                out.mpeExpression(d1, dsp::ExprBend, c.bend * static_cast<float>(c.range));
                out.mpeExpression(d1, dsp::ExprPressure, c.pressure);
                out.mpeExpression(d1, dsp::ExprSlide, c.slide);
            }
            break;
        }
        out.mpeNoteOff(d1);
        c.held[static_cast<std::size_t>(d1 >> 5)] &= ~(1u << (d1 & 31));
        break;
    case 0x80:
        out.mpeNoteOffVelocity(d1, static_cast<float>(d2) / 127.0f);
        c.held[static_cast<std::size_t>(d1 >> 5)] &= ~(1u << (d1 & 31));
        break;
    case 0xE0: {
        const int32_t raw = (d2 << 7) | d1;
        c.bend = std::clamp(static_cast<float>(raw - 8192) / 8191.0f, -1.0f, 1.0f);
        if (member) {
            forHeld(c, [&](int32_t p) { out.mpeExpression(p, dsp::ExprBend, c.bend * static_cast<float>(c.range)); });
        } else {
            out.mpeController(dsp::kCtlPitchWheel, c.bend);
        }
        break;
    }
    case 0xD0:
        c.pressure = static_cast<float>(d1) / 127.0f;
        if (member) {
            forHeld(c, [&](int32_t p) { out.mpeExpression(p, dsp::ExprPressure, c.pressure); });
        } else {
            out.mpeController(dsp::kCtlChannelPressure, c.pressure);
        }
        break;
    case 0xA0: out.mpeExpression(d1, dsp::ExprPressure, static_cast<float>(d2) / 127.0f); break;
    case 0xB0: controlChange(channel, d1, d2, out); break;
    default: break;
    }
}

void MpeInput::controlChange(int32_t channel, int32_t cc, int32_t v, Sink& out) {
    Channel& c = ch_[static_cast<std::size_t>(channel)];
    switch (cc) {
    case 74:
        c.slide = static_cast<float>(v) / 127.0f;
        if (isMember(channel)) {
            forHeld(c, [&](int32_t p) { out.mpeExpression(p, dsp::ExprSlide, c.slide); });
        } else {
            out.mpeController(74, c.slide);
        }
        break;
    case 64: out.mpeController(dsp::kCtlSustain, static_cast<float>(v) / 127.0f); break;
    case 101: c.rpnMsb = v; break;
    case 100: c.rpnLsb = v; break;
    case 6: dataEntry(channel, v); break;
    default:
        if (!dsp::ctlIsReserved(cc) && !isMember(channel)) { out.mpeController(cc, static_cast<float>(v) / 127.0f); }
        break;
    }
}

void MpeInput::dataEntry(int32_t channel, int32_t v) {
    const Channel& c = ch_[static_cast<std::size_t>(channel)];
    if (c.rpnMsb != 0) return;
    if (c.rpnLsb == 0) {
        const int32_t range = std::clamp(v, 1, 96);
        if (isMember(channel)) {
            for (int i = 0; i < 16; ++i) {
                if (isMember(i) && sameZone(i, channel)) ch_[static_cast<std::size_t>(i)].range = range;
            }
        } else {
            ch_[static_cast<std::size_t>(channel)].range = range;
        }
    } else if (c.rpnLsb == 6 && enabled_) {
        const int32_t n = std::clamp(v, 0, 15);
        if (channel == 0) {
            lowerMembers_ = n;
            upperMembers_ = std::max(0, std::min(upperMembers_, 14 - n));
        } else if (channel == 15) {
            upperMembers_ = n;
            lowerMembers_ = std::max(0, std::min(lowerMembers_, 14 - n));
        }
    }
}

} // namespace nota::io::midi
