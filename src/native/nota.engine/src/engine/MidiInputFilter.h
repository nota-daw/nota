// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which live MIDI reaches an instrument track (specs/115 R6, replacing specs/086's one source and
// one channel): either every input (the computer keyboard, gamepad, Nota Remote and every
// device) on a set of channels, or a list of MIDI ports, each with its own set of channels — any
// subset of 1-16, contiguous or not. Channel n on one port is a separate choice from channel n on
// another. A plain value for the audio thread's per-block copies (take lanes); the track keeps an
// atomic one, read lock-free while it renders.

#pragma once

#include "audio_io/MidiInput.h"

#include <array>
#include <atomic>
#include <cstdint>

namespace nota {

struct MidiInputFilter {
    static constexpr uint16_t kAllChannels = 0xFFFF;
    static constexpr int32_t kAllInputs = -2; // the legacy "source" of every input (Track::kMidiAllInputs)

    // Every input mode: the channels any device may play on (bit n = channel n + 1); 0 = this is
    // a port list instead. Not-hardware events (source −1) pass it whatever the channels.
    uint16_t allMask = kAllChannels;
    std::array<uint16_t, MidiInput::kMaxSources> mask{}; // per source index, a port list

    // An event from `source` (−1: not hardware) on `channel` (0-15).
    bool accepts(int32_t source, int32_t channel) const {
        if (channel < 0 || channel > 15) return false;
        if (allMask != 0) return source < 0 || ((allMask >> channel) & 1u);
        return source >= 0 && source < MidiInput::kMaxSources && ((mask[static_cast<size_t>(source)] >> channel) & 1u);
    }

    // specs/086's pick: kAllInputs or a source index (−1: a device that isn't connected), and a
    // channel 0 = all, 1-16.
    static MidiInputFilter legacy(int32_t source, int32_t channel) {
        MidiInputFilter f;
        const uint16_t ch = channel >= 1 && channel <= 16 ? static_cast<uint16_t>(1u << (channel - 1)) : kAllChannels;
        if (source == kAllInputs) {
            f.allMask = ch;
            return f;
        }
        f.allMask = 0;
        if (source >= 0 && source < MidiInput::kMaxSources) f.mask[static_cast<size_t>(source)] = ch;
        return f;
    }

    // The nearest legacy pick, for specs/086's getters: the first listed source and its lowest channel.
    int32_t legacySource() const {
        if (allMask != 0) return kAllInputs;
        for (int32_t s = 0; s < MidiInput::kMaxSources; ++s)
            if (mask[static_cast<size_t>(s)]) return s;
        return -1;
    }
    int32_t legacyChannel() const {
        const int32_t s = legacySource();
        const uint16_t m = allMask != 0 ? allMask : s >= 0 ? mask[static_cast<size_t>(s)] : kAllChannels;
        if (m == kAllChannels || m == 0) return 0;
        for (int32_t c = 0; c < 16; ++c)
            if ((m >> c) & 1u) return c + 1;
        return 0;
    }
};

// The track's copy: written on the message thread, read on the audio thread. A reader may see a
// half-written filter for one block; every value it can see is one of the two filters' parts, so
// nothing is let in that neither allowed for longer than that block.
struct AtomicMidiInputFilter {
    static_assert(MidiInput::kMaxSources % 4 == 0, "four 16-bit port masks per word");
    static constexpr size_t kWords = MidiInput::kMaxSources / 4;
    std::atomic<uint16_t> allMask{MidiInputFilter::kAllChannels};
    // The per-port masks, four to a 64-bit word: a track is built (every edit clones one) with
    // four stores, not sixteen (perf gate edit-warp-split).
    std::array<std::atomic<uint64_t>, kWords> words{};

    // Every input (the usual case) needs only its channels: the per-port masks are neither read
    // nor copied then.
    MidiInputFilter load() const {
        MidiInputFilter f;
        f.allMask = allMask.load(std::memory_order_relaxed);
        if (f.allMask != 0) return f;
        for (size_t w = 0; w < kWords; ++w) {
            const uint64_t v = words[w].load(std::memory_order_relaxed);
            for (size_t k = 0; k < 4; ++k) f.mask[w * 4 + k] = static_cast<uint16_t>(v >> (16 * k));
        }
        return f;
    }
    void store(const MidiInputFilter& f) {
        if (f.allMask == 0)
            for (size_t w = 0; w < kWords; ++w) {
                uint64_t v = 0;
                for (size_t k = 0; k < 4; ++k) v |= static_cast<uint64_t>(f.mask[w * 4 + k]) << (16 * k);
                words[w].store(v, std::memory_order_relaxed);
            }
        allMask.store(f.allMask, std::memory_order_relaxed);
    }
    bool accepts(int32_t source, int32_t channel) const {
        if (channel < 0 || channel > 15) return false;
        const uint16_t all = allMask.load(std::memory_order_relaxed);
        if (all != 0) return source < 0 || ((all >> channel) & 1u);
        if (source < 0 || source >= MidiInput::kMaxSources) return false;
        const uint64_t v = words[static_cast<size_t>(source) / 4].load(std::memory_order_relaxed);
        return (static_cast<uint16_t>(v >> (16 * (static_cast<size_t>(source) % 4))) >> channel) & 1u;
    }
};

} // namespace nota
