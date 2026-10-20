// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MpeInput implementation — channel-voice routing

#include "MpeInput.h"

namespace nota {

void MpeInput::message(int32_t status, int32_t channel, int32_t d1, int32_t d2, Sink& out) {
    if (channel < 0 || channel > 15) return;
    Channel& c = ch_[channel];
    const bool member = isMember(channel);
    switch (status) {
    case 0x90:
        if (d2 > 0) {
            out.mpeNoteOn(d1, d2 / 127.0f);
            c.held[d1 >> 5] |= 1u << (d1 & 31);
            if (member) { // the channel's current state is this note's initial state
                out.mpeExpression(d1, ExprBend, c.bend * c.range);
                out.mpeExpression(d1, ExprPressure, c.pressure);
                out.mpeExpression(d1, ExprSlide, c.slide);
            }
            break;
        }
        [[fallthrough]];
    case 0x80:
        out.mpeNoteOff(d1);
        c.held[d1 >> 5] &= ~(1u << (d1 & 31));
        break;
    case 0xE0: {
        const int32_t raw = (d2 << 7) | d1; // 0..16383, 8192 = centre
        c.bend = std::clamp((raw - 8192) / 8191.0f, -1.0f, 1.0f);
        if (member) forHeld(c, [&](int32_t p) { out.mpeExpression(p, ExprBend, c.bend * c.range); });
        else
            out.mpeController(kCtlPitchWheel, c.bend);
        break;
    }
    case 0xD0:
        c.pressure = d1 / 127.0f;
        if (member) forHeld(c, [&](int32_t p) { out.mpeExpression(p, ExprPressure, c.pressure); });
        else
            out.mpeController(kCtlChannelPressure, c.pressure);
        break;
    case 0xA0: out.mpeExpression(d1, ExprPressure, d2 / 127.0f); break;
    case 0xB0: controlChange(channel, d1, d2, out); break;
    default: break;
    }
}

void MpeInput::controlChange(int32_t channel, int32_t cc, int32_t v, Sink& out) {
    Channel& c = ch_[channel];
    switch (cc) {
    case 74:
        c.slide = v / 127.0f;
        if (isMember(channel)) forHeld(c, [&](int32_t p) { out.mpeExpression(p, ExprSlide, c.slide); });
        else
            out.mpeController(74, c.slide);
        break;
    case 64: // sustain pedal: instrument-wide on any channel (an MPE master or member);
             // its real value, so a plug-in piano can half-pedal (built-ins: >= 0.5 down)
        out.mpeController(kCtlSustain, v / 127.0f);
        break;
    case 101: c.rpnMsb = v; break;
    case 100: c.rpnLsb = v; break;
    case 6: dataEntry(channel, v); break;
    default: // any other controller of an ordinary / master channel: instrument-wide
        if (!ctlIsReserved(cc) && !isMember(channel)) out.mpeController(cc, v / 127.0f);
        break;
    }
}

void MpeInput::dataEntry(int32_t channel, int32_t v) {
    const Channel& c = ch_[channel];
    if (c.rpnMsb != 0) return;
    if (c.rpnLsb == 0) {
        // Sent on any member channel it sets every member of the zone (MPE spec);
        // a master or plain channel keeps its own.
        const int32_t range = std::clamp(v, 1, 96);
        if (isMember(channel)) {
            for (int i = 0; i < 16; ++i)
                if (isMember(i) && sameZone(i, channel)) ch_[i].range = range;
        } else
            ch_[channel].range = range;
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

} // namespace nota
