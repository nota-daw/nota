// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PolyVoiceManager — fixed-capacity allocation-free polyphonic voice allocator.

#pragma once

#include "nota/dsp/rt/RtAttributes.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {

enum class VoiceMode : uint8_t {
    Poly = 0,
    Mono = 1,
    Legato = 2,
};

template <typename VoiceT, int32_t MaxVoices> class PolyVoiceManager {
    static_assert(MaxVoices > 0 && MaxVoices <= 128, "voice count out of range");

  public:
    PolyVoiceManager() = default;

    void setMode(VoiceMode m) noexcept { mode_ = m; }
    VoiceMode mode() const noexcept { return mode_; }

    void reset() noexcept {
        for (auto& v : voices_) v.kill();
        pedalDown_ = false;
        nextId_ = 0;
        lastRetrigger_ = true;
    }

    VoiceT* noteOn(int32_t pitch, float vel) NOTA_RT_SAFE {
        if (mode_ == VoiceMode::Mono || mode_ == VoiceMode::Legato) {
            VoiceT& v = voices_[0];
            lastRetrigger_ = (mode_ == VoiceMode::Mono) || !v.active;
            v.noteOn(pitch, vel, nextId_++);
            v.tickAge();
            return &v;
        }
        lastRetrigger_ = true;
        for (auto& v : voices_) {
            if (v.active && v.pitch == pitch && !v.sustained) {
                v.noteOn(pitch, vel, nextId_++);
                return &v;
            }
        }
        for (auto& v : voices_) {
            if (!v.active) {
                v.noteOn(pitch, vel, nextId_++);
                return &v;
            }
        }
        VoiceT* best = &voices_[0];
        for (auto& v : voices_) {
            const bool vRel = v.released || v.sustained;
            const bool bRel = best->released || best->sustained;
            if ((vRel && !bRel) || (vRel == bRel && v.age > best->age)) best = &v;
        }
        best->noteOn(pitch, vel, nextId_++);
        return best;
    }

    void noteOff(int32_t pitch) NOTA_RT_SAFE {
        for (auto& v : voices_) {
            if (!v.active || v.pitch != pitch || v.sustained) continue;
            v.noteOff();
            if (pedalDown_) v.sustain(true);
        }
    }

    void allNotesOff() NOTA_RT_SAFE {
        for (auto& v : voices_) v.kill();
    }

    void sustainPedal(bool down) NOTA_RT_SAFE {
        pedalDown_ = down;
        if (!down) {
            for (auto& v : voices_) {
                if (v.active && v.sustained) v.sustain(false);
            }
        }
    }

    void noteExpression(int32_t pitch, int32_t dim, float value) NOTA_RT_SAFE {
        for (auto& v : voices_) {
            if (!v.active) continue;
            if (pitch >= 0 && v.pitch != pitch) continue;
            if (dim == 0) v.bendSemis = value;
            else if (dim == 1)
                v.pressure = value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
            else if (dim == 2)
                v.slide = value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
        }
    }

    void tickAges() NOTA_RT_SAFE {
        for (auto& v : voices_) v.tickAge();
    }

    bool hasActiveVoices() const NOTA_RT_SAFE {
        for (const auto& v : voices_) {
            if (v.active) return true;
        }
        return false;
    }

    int32_t activeCount() const NOTA_RT_SAFE {
        int32_t n = 0;
        for (const auto& v : voices_) {
            if (v.active) ++n;
        }
        return n;
    }

    bool lastRetrigger() const noexcept { return lastRetrigger_; }

    std::array<VoiceT, static_cast<std::size_t>(MaxVoices)>& voices() noexcept { return voices_; }
    const std::array<VoiceT, static_cast<std::size_t>(MaxVoices)>& voices() const noexcept { return voices_; }

  private:
    std::array<VoiceT, static_cast<std::size_t>(MaxVoices)> voices_{};
    VoiceMode mode_ = VoiceMode::Poly;
    bool pedalDown_ = false;
    int32_t nextId_ = 0;
    bool lastRetrigger_ = true;
};

} // namespace nota::dsp
