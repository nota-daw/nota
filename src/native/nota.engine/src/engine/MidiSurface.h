// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Control surfaces (specs/051): a controller recognised by a profile *claims* some of its
// messages. A claimed message is a role (transport, a fader, a knob on the focused device), so
// it must never also play a note or move an instrument's CC — and that decision has to be made
// where routing happens, on the MIDI thread, before MpeInput. This class holds what that thread
// needs: the claim table (one immutable table for all sources, swapped atomically by the
// message thread), the surface queue the app drains, and a small ring of complete SysEx
// messages (identity replies, surface handshakes).
//
// MIDI-thread paths never allocate. Producers take a small mutex because RtMidi may run one
// thread per port (as liveMidi_ does); the audio thread never touches any of this.

#pragma once

#include "engine/CommandQueue.h"

#include <array>
#include <atomic>
#include <bitset>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

namespace nota {

struct SurfaceEvent {
    int32_t source;
    int32_t status; // high nibble; note-on with velocity 0 reported as 0x80
    int32_t channel;
    int32_t d1;
    int32_t d2;
};

struct MidiClaimEntry {
    int32_t source;
    int32_t wholePort;
    int32_t status; // 0x80 note (on and off), 0xB0 CC, 0xD0 channel pressure, 0xE0 pitch bend
    int32_t channel;
    int32_t data1; // 0 for pressure / bend
};

class MidiSurface {
  public:
    static constexpr int32_t kSources = 16;
    static constexpr int32_t kMaxSysex = 512;

    MidiSurface();
    ~MidiSurface();
    MidiSurface(const MidiSurface&) = delete;
    MidiSurface& operator=(const MidiSurface&) = delete;

    // Message thread. Replaces the whole table; false (table unchanged) on a malformed entry.
    bool setClaims(const MidiClaimEntry* claims, int32_t count);
    // MIDI thread. Is this channel message a role?
    bool claimed(int32_t source, int32_t status, int32_t channel, int32_t d1) const;
    bool sourceHasClaim(int32_t source) const;

    void setLearnListening(bool on) { learnListening_.store(on, std::memory_order_relaxed); }
    bool learnListening() const { return learnListening_.load(std::memory_order_relaxed); }

    // The routing rule both the engine and the self-test run: a claimed message goes to the
    // surface queue (and, while MIDI Learn listens, to the learn copy); anything else to the
    // instruments and the learn copy, as before. learn(kind, channel, number, value) gets
    // CC (kind 0) and note-on (kind 1) only.
    template <typename InstrumentsFn, typename LearnFn>
    void route(int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2, InstrumentsFn&& instruments,
               LearnFn&& learn) {
        auto learnCopy = [&] {
            if (status == 0xB0) learn(0, channel, d1, d2);
            else if (status == 0x90 && d2 > 0)
                learn(1, channel, d1, d2);
        };
        if (claimed(source, status, channel, d1)) {
            const int32_t st = (status == 0x90 && d2 == 0) ? 0x80 : status;
            pushEvent({source, st, channel, d1, d2});
            if (learnListening()) learnCopy();
            return;
        }
        instruments();
        learnCopy();
    }

    // MIDI thread → message thread.
    void pushEvent(const SurfaceEvent& ev);
    int32_t pollEvents(SurfaceEvent* out, int32_t max);
    uint32_t dropped() const { return dropped_.load(std::memory_order_relaxed); }

    // SysEx: accepted from a source with a claim, or while an identity request to it is
    // outstanding (armIdentity, 2 s). Longer than kMaxSysex never gets here (the parser drops it).
    void pushSysex(int32_t source, const uint8_t* data, int32_t len);
    // Copies one message; 0 = none, -n = it had n bytes and did not fit (dropped).
    int32_t pollSysex(int32_t* outSource, uint8_t* out, int32_t cap);
    void armIdentity(int32_t source);

    // Deterministic, hardware-free checks of the claim / SysEx / loopback rules. 0 = pass,
    // else the number of the first failed check.
    static int32_t selfTest();

  private:
    struct Table {
        struct Source {
            bool whole = false;
            bool any = false;
            std::bitset<4 * 16 * 128> keys; // status index × channel × data1
        };
        std::array<Source, kSources> sources;
    };
    static int statusIndex(int32_t status);
    static size_t key(int statusIdx, int32_t channel, int32_t d1) {
        return static_cast<size_t>((statusIdx * 16 + channel) * 128 + d1);
    }

    std::atomic<const Table*> table_{nullptr};
    mutable std::atomic<int32_t> readers_{0};
    std::vector<const Table*> retired_; // message thread
    void reclaim();

    std::atomic<bool> learnListening_{false};

    std::mutex producerMx_;
    SpscRingBuffer<SurfaceEvent, 1024> events_;
    std::atomic<uint32_t> dropped_{0};

    struct SysexMsg {
        int32_t source = 0;
        int32_t len = 0;
        uint8_t data[kMaxSysex] = {};
    };
    std::mutex sysexMx_;
    SpscRingBuffer<SysexMsg, 16> sysex_;
    std::array<std::atomic<int64_t>, kSources> identityUntilMs_{};
};

// A test port's output (specs/051, MCP controller_sent): keeps what was sent, capped.
class MidiLoopback {
  public:
    static constexpr size_t kCap = 64 * 1024;
    void write(const uint8_t* data, size_t len);
    // Moves up to cap bytes out (oldest first); returns the count.
    int32_t take(uint8_t* out, int32_t cap);

  private:
    std::mutex mx_;
    std::vector<uint8_t> bytes_;
};

} // namespace nota
