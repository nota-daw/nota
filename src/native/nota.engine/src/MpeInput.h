// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MPE input: turns one MIDI source's channel-voice messages into notes + per-note expression
// (NoteExpression.h). One instance per source, driven on that source's MIDI thread only.
//
// MPE (MIDI Polyphonic Expression) gives every sounding note its own channel, so the
// channel's pitch bend, channel pressure and CC74 belong to that note alone. A zone has a
// master channel (zone-wide controls) and member channels (one note each):
//   lower zone — master channel 1, members 2..(1+n);  upper zone — master 16, members down.
// With MPE on, the lower zone with 15 members is assumed until the controller says
// otherwise with an MPE Configuration Message (RPN 6 on a master channel). A member
// channel's bend range defaults to 48 semitones (the MPE default; RPN 0 changes it).
// Everything else — MPE off, a master channel, a channel outside the zones — is an
// ordinary keyboard: its wheel / channel pressure / CC74 address the whole instrument
// (pitch −1), the wheel normalized so the synth applies its own bend range.
// Polyphonic aftertouch (0xA0) is per-note on any channel.
//
// A member channel's state persists between notes, and MPE controllers send a note's
// initial bend / pressure / slide BEFORE its note-on — so a note-on is followed by the
// channel's current values, landing on the new note.

#pragma once

#include <algorithm>
#include <bit>
#include <cstdint>

#include "NoteExpression.h"

namespace nota {

class MpeInput {
public:
    struct Sink {
        virtual ~Sink() = default;
        virtual void mpeNoteOn(int32_t pitch, float velocity) = 0;
        virtual void mpeNoteOff(int32_t pitch) = 0;
        virtual void mpeExpression(int32_t pitch, int32_t dim, float value) = 0;   // pitch −1 = all
    };

    // enabled = treat channels per MPE zones; bendRange = the member channels' default
    // pitch-bend range in semitones (until RPN 0 says otherwise).
    void configure(bool enabled, int32_t bendRange) {
        enabled_ = enabled;
        defaultRange_ = std::clamp(bendRange, 1, 96);
        reset();
    }

    void reset() {
        lowerMembers_ = enabled_ ? 15 : 0;
        upperMembers_ = 0;
        for (auto& c : ch_) c = Channel{};
        for (int i = 0; i < 16; ++i) ch_[i].range = defaultRange_;
    }

    // One channel-voice message. status = high nibble, channel 0..15, data1/data2 7-bit
    // (channel pressure carries its value in data1).
    void message(int32_t status, int32_t channel, int32_t d1, int32_t d2, Sink& out) {
        if (channel < 0 || channel > 15) return;
        Channel& c = ch_[channel];
        const bool member = isMember(channel);
        switch (status) {
            case 0x90:
                if (d2 > 0) {
                    out.mpeNoteOn(d1, d2 / 127.0f);
                    c.held[d1 >> 5] |= 1u << (d1 & 31);
                    if (member) {   // the channel's current state is this note's initial state
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
                const int32_t raw = (d2 << 7) | d1;                       // 0..16383, 8192 = centre
                c.bend = std::clamp((raw - 8192) / 8191.0f, -1.0f, 1.0f);
                if (member) forHeld(c, [&](int32_t p) { out.mpeExpression(p, ExprBend, c.bend * c.range); });
                else        out.mpeExpression(-1, ExprBend, c.bend);
                break;
            }
            case 0xD0:
                c.pressure = d1 / 127.0f;
                if (member) forHeld(c, [&](int32_t p) { out.mpeExpression(p, ExprPressure, c.pressure); });
                else        out.mpeExpression(-1, ExprPressure, c.pressure);
                break;
            case 0xA0:
                out.mpeExpression(d1, ExprPressure, d2 / 127.0f);
                break;
            case 0xB0:
                controlChange(channel, d1, d2, out);
                break;
            default: break;
        }
    }

    bool    enabled() const { return enabled_; }
    int32_t lowerMembers() const { return lowerMembers_; }
    int32_t upperMembers() const { return upperMembers_; }
    int32_t bendRange(int32_t channel) const { return (channel >= 0 && channel < 16) ? ch_[channel].range : 0; }

private:
    struct Channel {
        uint32_t held[4] = {};          // pitches sounding on this channel
        float    bend = 0.0f;           // −1..+1
        float    pressure = 0.0f;
        float    slide = 0.5f;
        int32_t  range = 48;            // semitones at full bend
        int32_t  rpnMsb = 127, rpnLsb = 127;   // selected RPN (127/127 = none)
    };

    bool isMember(int32_t channel) const {
        if (!enabled_) return false;
        if (lowerMembers_ > 0 && channel >= 1 && channel <= lowerMembers_) return true;
        if (upperMembers_ > 0 && channel <= 14 && channel >= 15 - upperMembers_) return true;
        return false;
    }

    template <class F> static void forHeld(const Channel& c, F f) {
        for (int w = 0; w < 4; ++w)
            for (uint32_t bits = c.held[w]; bits; bits &= bits - 1)
                f(w * 32 + std::countr_zero(bits));
    }

    void controlChange(int32_t channel, int32_t cc, int32_t v, Sink& out) {
        Channel& c = ch_[channel];
        switch (cc) {
            case 74:
                c.slide = v / 127.0f;
                if (isMember(channel)) forHeld(c, [&](int32_t p) { out.mpeExpression(p, ExprSlide, c.slide); });
                else                   out.mpeExpression(-1, ExprSlide, c.slide);
                break;
            case 101: c.rpnMsb = v; break;
            case 100: c.rpnLsb = v; break;
            case 6:   dataEntry(channel, v); break;
            default: break;
        }
    }

    // RPN 0 (pitch-bend sensitivity) and RPN 6 (MPE Configuration Message).
    void dataEntry(int32_t channel, int32_t v) {
        const Channel& c = ch_[channel];
        if (c.rpnMsb != 0) return;
        if (c.rpnLsb == 0) {
            // Sent on any member channel it sets every member of the zone (MPE spec);
            // a master or plain channel keeps its own.
            const int32_t range = std::clamp(v, 1, 96);
            if (isMember(channel)) { for (int i = 0; i < 16; ++i) if (isMember(i) && sameZone(i, channel)) ch_[i].range = range; }
            else ch_[channel].range = range;
        } else if (c.rpnLsb == 6 && enabled_) {
            const int32_t n = std::clamp(v, 0, 15);
            if (channel == 0) { lowerMembers_ = n; upperMembers_ = std::max(0, std::min(upperMembers_, 14 - n)); }
            else if (channel == 15) { upperMembers_ = n; lowerMembers_ = std::max(0, std::min(lowerMembers_, 14 - n)); }
        }
    }

    bool sameZone(int32_t a, int32_t b) const {
        const bool aLow = lowerMembers_ > 0 && a >= 1 && a <= lowerMembers_;
        const bool bLow = lowerMembers_ > 0 && b >= 1 && b <= lowerMembers_;
        return aLow == bLow;
    }

    bool    enabled_ = true;
    int32_t defaultRange_ = 48;
    int32_t lowerMembers_ = 15, upperMembers_ = 0;
    Channel ch_[16];
};

} // namespace nota
