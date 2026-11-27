// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"

#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>

namespace nota::io::midi {

/// Decodes channel-voice MIDI messages from one source into note-on/off, MPE expression, and controllers.
class NOTA_IO_API MpeInput {
  public:
    struct Sink {
        virtual ~Sink() = default;
        virtual void mpeNoteOn(int32_t pitch, float velocity) = 0;
        virtual void mpeNoteOff(int32_t pitch) = 0;
        virtual void mpeNoteOffVelocity(int32_t pitch, float /*velocity*/) { mpeNoteOff(pitch); }
        virtual void mpeExpression(int32_t pitch, int32_t dim, float value) = 0;
        virtual void mpeController(int32_t ctl, float value) = 0;
    };

    void configure(bool enabled, int32_t bendRange) {
        enabled_ = enabled;
        defaultRange_ = std::clamp(bendRange, 1, 96);
        reset();
    }

    void reset() {
        lowerMembers_ = enabled_ ? 15 : 0;
        upperMembers_ = 0;
        for (auto& c : ch_) {
            c = Channel{};
            c.range = defaultRange_;
        }
    }

    void message(int32_t status, int32_t channel, int32_t d1, int32_t d2, Sink& out);

    bool enabled() const noexcept { return enabled_; }
    int32_t lowerMembers() const noexcept { return lowerMembers_; }
    int32_t upperMembers() const noexcept { return upperMembers_; }
    int32_t bendRange(int32_t channel) const noexcept {
        return (channel >= 0 && channel < 16) ? ch_[static_cast<std::size_t>(channel)].range : 0;
    }

  private:
    struct Channel {
        std::array<uint32_t, 4> held{};
        float bend = 0.0f;
        float pressure = 0.0f;
        float slide = 0.5f;
        int32_t range = 48;
        int32_t rpnMsb = 127;
        int32_t rpnLsb = 127;
    };

    bool isMember(int32_t channel) const noexcept {
        if (!enabled_) return false;
        if (lowerMembers_ > 0 && channel >= 1 && channel <= lowerMembers_) return true;
        if (upperMembers_ > 0 && channel <= 14 && channel >= 15 - upperMembers_) return true;
        return false;
    }

    bool sameZone(int32_t a, int32_t b) const noexcept {
        const bool aLow = lowerMembers_ > 0 && a >= 1 && a <= lowerMembers_;
        const bool bLow = lowerMembers_ > 0 && b >= 1 && b <= lowerMembers_;
        return aLow == bLow;
    }

    template <class F> static void forHeld(const Channel& c, F fn) {
        for (int w = 0; w < 4; ++w) {
            for (uint32_t bits = c.held[static_cast<std::size_t>(w)]; bits; bits &= bits - 1u) {
                fn(w * 32 + std::countr_zero(bits));
            }
        }
    }

    void controlChange(int32_t channel, int32_t cc, int32_t v, Sink& out);
    void dataEntry(int32_t channel, int32_t v);

    bool enabled_ = true;
    int32_t defaultRange_ = 48;
    int32_t lowerMembers_ = 15;
    int32_t upperMembers_ = 0;
    std::array<Channel, 16> ch_{};
};

} // namespace nota::io::midi
