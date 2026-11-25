// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// External MIDI (specs/115, #690): the instrument that plays a hardware (or software) synth
// over a MIDI output instead of making sound. It sits in the instrument slot like any built-in,
// so it receives exactly what an instrument hears — the track's clips (Arrangement and Session)
// and live input, after the track's MIDI effects, with articulations, at sample offsets — and
// turns it back into MIDI bytes. It renders silence.
//
// The bytes leave the audio thread through this instance's own single-producer ring (AR-6): the
// MIDI out thread (engine/MidiOutService) drains it, waits until each message is due and sends
// it. The destination — an output uid and a channel mode — is fixed for an instance: changing it
// makes a new instance (Engine::setExternalMidi), so undo restores the old one and the old
// instance leaving the graph is what releases its notes at the old output.

#pragma once

#include "devices/Instrument.h"
#include "engine/CommandQueue.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {

// The engine's block clock for MIDI out: when the current block's first frame is due. A device
// render stamps wall time (steady clock) plus the output latency, so the synth lines up with
// Nota's own audio; an offline render counts frames instead (tests, nothing real is sent then).
struct MidiRenderClock {
    std::atomic<uint64_t> blockNs{0};
    std::atomic<uint32_t> serial{0};
    std::atomic<bool> realtime{false};
    std::atomic<double> sampleRate{48000.0};
};

// One message (or a flush) on its way to the MIDI out thread.
struct OutMidiEv {
    uint64_t dueNs = 0;
    uint8_t len = 0;       // 1..3 bytes; 0 = a flush: release what this route holds now
    uint8_t realtime = 0;  // dueNs is wall time (wait for it) rather than counted frames
    uint8_t b[3] = {0, 0, 0};
};

class ExternalMidi final : public Instrument {
  public:
    static constexpr int32_t kKind = 19;
    static constexpr int32_t kKeepChannels = 0; // channel mode: MPE member channels (research R5)
    static constexpr int32_t kRingSize = 4096;
    static constexpr float kMpeBendRange = 48.0f; // MPE's default per-note pitch-bend range, semitones

    explicit ExternalMidi(std::string outputUid = {}, int32_t channel = 1, std::string outputName = {}, std::string roleId = {});

    int32_t kind() const override { return kKind; }
    const char* displayName() const override { return "External MIDI"; }
    bool takesTimedEvents() const override { return true; }
    bool handlesSustain() const override { return true; }    // the synth gets CC64 itself
    bool takesArticulations() const override { return true; } // keyswitches, programs: it's a real synth
    bool supportsMpe() const override { return true; }
    void setSampleRate(double sr) override { sr_ = sr > 0.0 ? sr : 48000.0; }

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override { noteOffVelocity(pitch, 0.0f); }
    void noteOffVelocity(int32_t pitch, float velocity) override;
    void allNotesOff() override; // any thread: the flush goes out with the next audio-thread event
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void controller(int32_t ctl, float value) override;
    void programChange(int32_t program) override;
    void render(float* out, int32_t frames) override;

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // The destination, fixed for this instance (message thread reads it; set before publication).
    const std::string& outputUid() const { return uid_; }
    const std::string& outputName() const { return name_; } // as saved: shown while the output is missing
    const std::string& roleId() const { return role_; }     // the project output role it follows, "" = a port (US4)
    int32_t channel() const { return channel_; } // 1..16, or kKeepChannels

    // Engine wiring (message thread, before the instance is published).
    void attachClock(const MidiRenderClock* clock) { clock_.store(clock, std::memory_order_release); }
    // A flush from the audio thread itself, pushed at once (a track going silent).
    void flushFromAudio();

    // The MIDI out thread's side.
    SpscRingBuffer<OutMidiEv, kRingSize>& ring() { return ring_; }
    std::atomic<int32_t> status{0};   // 0 no destination, 1 sending, 2 unavailable (MIDI out thread writes)
    std::atomic<uint32_t> dropped{0}; // messages lost to a full ring (audio thread writes)

  private:
    uint8_t chanFor(int32_t pitch) const; // the channel (0..15) a note's messages go on
    uint8_t master() const;               // the channel for instrument-wide messages
    void push(uint8_t s, uint8_t d1, uint8_t d2, uint8_t len);
    void pushFlush();
    uint64_t dueNow();
    void takePendingFlush();
    void sendMpeConfig();

    std::string uid_;
    std::string name_;
    std::string role_;
    int32_t channel_ = 1;
    double sr_ = 48000.0;
    std::atomic<const MidiRenderClock*> clock_{nullptr};
    SpscRingBuffer<OutMidiEv, kRingSize> ring_;
    std::atomic<bool> pendingFlush_{false};

    // Audio thread: frames already rendered in the current block (offsets count from there).
    uint32_t serialSeen_ = 0xFFFFFFFFu;
    int64_t framesIntoBlock_ = 0;

    // Keep channels (MPE lower zone): each sounding pitch's member channel (0 = none), how many
    // sounding notes share each channel, and the member channels in least-recently-freed order.
    std::array<uint8_t, 128> mpeChan_{};
    std::array<uint8_t, 16> chanUse_{};
    std::array<uint8_t, 15> lru_{};
    bool mpeConfigured_ = false;
};

} // namespace nota
