// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The MIDI out thread (specs/115 R2–R4): the one owner of every MIDI output Nota opens. External
// MIDI instruments hand it their messages through lock-free rings (the audio thread never waits
// on a port); controller feedback (specs/051's LEDs and SysEx) goes through it too, so a port
// both uses is opened once — WinMM opens an output exclusively.
//
// Per External MIDI instance it keeps a book of what it actually sent and has not ended (notes
// per channel, held pedals). That book is what a route change, a stop, a removed track or an
// unplugged-then-returning device releases — exactly what this route started, at the output it
// started it on, nothing of anyone else's.
//
// Threads: the service thread, plus the message thread for setRoutes / sendControl / status. One
// mutex serialises them; the audio thread never takes it.

#pragma once

#include "devices/instruments/ExternalMidi.h"

#include <array>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace nota {

class MidiOutput;

class MidiOutService {
  public:
    // Delivers complete messages to `uid` (dueNs for test capture). False = not there / failed.
    using Sender = std::function<bool(const std::string& uid, const uint8_t* data, int32_t len, uint64_t dueNs)>;
    // Is `uid` connected now? (Used to notice a returning device.)
    using Present = std::function<bool(const std::string& uid)>;

    MidiOutService();
    ~MidiOutService();
    MidiOutService(const MidiOutService&) = delete;
    MidiOutService& operator=(const MidiOutService&) = delete;

    // Test ports ("test:" uids) are delivered by the engine; everything else by a MidiOutput this
    // service opens. Set once, before the first route or send. Message thread.
    void setTestHooks(Sender testSend, Present testPresent);

    // The External MIDI instances in the graph just published, by track id. An instance that
    // left is drained a little longer (a render may still be playing the old graph), then what
    // it holds is released at its output. Message thread.
    struct Route {
        int32_t trackId = -1;
        std::shared_ptr<ExternalMidi> inst;
    };
    void setRoutes(std::vector<Route> routes);

    // A controller's message (LEDs, SysEx, identity requests), sent now. Message thread.
    bool sendControl(const std::string& uid, const uint8_t* data, int32_t len);

    // Drain every ring and send what is due — everything, for an offline render — and release
    // instances that left the graph. The thread does this every millisecond; tests call it
    // before they read what an output got. Message thread.
    void pump();

    void stop();

  private:
    struct Book {
        std::array<std::array<uint64_t, 2>, 16> notes{}; // sounding pitches per channel
        std::array<uint8_t, 16> pedals{};                 // bit 0 CC64, 1 CC66, 2 CC67 down
        uint16_t usedChannels = 0;                        // every channel sent on since the last reset
        bool any() const;
        void clear();
    };
    struct Entry {
        int32_t trackId = -1;
        std::shared_ptr<ExternalMidi> inst;
        std::string uid;      // the output this instance was made for
        bool inGraph = true;
        int64_t leftAtMs = 0; // when it left the graph
        bool available = false;
        Book book;
        std::deque<OutMidiEv> pending; // drained, not yet due
    };

    void ensureThread();
    void run();
    void pumpLocked(bool everything);
    void drainEntry(Entry& e);
    void sendDue(Entry& e, bool everything, uint64_t nowNs);
    void deliver(Entry& e, const OutMidiEv& ev);
    void release(Entry& e);
    void reconnect(const std::vector<Entry*>& back);
    void checkPresence(bool force);
    bool sendRaw(const std::string& uid, const uint8_t* data, int32_t len, uint64_t dueNs);
    bool present(const std::string& uid);
    bool channelHeldByStayer(const std::string& uid, int ch, const std::vector<Entry*>& back) const;

    std::mutex mx_;
    std::condition_variable cv_;
    std::thread thread_;
    bool running_ = false;
    bool quit_ = false;
    std::vector<Entry> entries_;
    std::map<std::string, std::unique_ptr<MidiOutput>> outputs_; // by uid
    std::vector<std::string> presentUids_;                       // real outputs, as last listed
    uint32_t presentGen_ = 0;
    int64_t lastPresenceMs_ = 0;
    Sender testSend_;
    Present testPresent_;
};

} // namespace nota
