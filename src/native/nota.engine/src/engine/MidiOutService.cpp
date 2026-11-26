// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The MIDI out thread (specs/115): see MidiOutService.h for what it owns and why. Every member
// below runs with mx_ held — on the service thread, or on the message thread for the public
// calls — never on the audio thread, which only pushes into the instances' rings.

#include "engine/MidiOutService.h"

#include "audio_io/MidiInput.h"
#include "audio_io/MidiOutput.h"

#include <algorithm>
#include <chrono>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <timeapi.h>
#pragma comment(lib, "winmm.lib")
#endif

namespace nota {

namespace {
uint64_t steadyNs() {
    return static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count());
}
int64_t steadyMs() { return static_cast<int64_t>(steadyNs() / 1000000); }

constexpr int64_t kGoneGraceMs = 100;      // a render may still be playing a graph the instance was in
constexpr int64_t kPresenceEveryMs = 250;  // how often a missing device is looked for
constexpr uint64_t kSendAheadNs = 500000;  // send this close to due (the loop wakes every ~1 ms)
} // namespace

bool MidiOutService::Book::any() const {
    for (int c = 0; c < 16; ++c)
        if (notes[c][0] || notes[c][1] || pedals[c]) return true;
    return false;
}

void MidiOutService::Book::clear() {
    for (auto& n : notes) n = {0, 0};
    pedals.fill(0);
    usedChannels = 0;
}

MidiOutService::MidiOutService() = default;

MidiOutService::~MidiOutService() { stop(); }

void MidiOutService::setTestHooks(Sender testSend, Present testPresent) {
    std::lock_guard lk(mx_);
    testSend_ = std::move(testSend);
    testPresent_ = std::move(testPresent);
}

void MidiOutService::ensureThread() {
    if (running_ || quit_) return;
    running_ = true;
    thread_ = std::thread([this] { run(); });
}

void MidiOutService::run() {
#if defined(_WIN32)
    timeBeginPeriod(1); // ~1 ms waits instead of the 15.6 ms default tick
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
#endif
    std::unique_lock lk(mx_);
    while (!quit_) {
        pumpLocked(false);
        cv_.wait_for(lk, std::chrono::milliseconds(1), [this] { return quit_; });
    }
#if defined(_WIN32)
    timeEndPeriod(1);
#endif
}

void MidiOutService::stop() {
    {
        std::lock_guard lk(mx_);
        if (quit_) return;
        quit_ = true;
        // Whatever is still held goes off before Nota lets go of the ports.
        for (auto& e : entries_) {
            drainEntry(e);
            sendDue(e, true, steadyNs());
            if (e.available) release(e);
        }
        entries_.clear();
    }
    cv_.notify_all();
    if (thread_.joinable()) thread_.join();
    std::lock_guard lk(mx_);
    outputs_.clear();
}

// ---- routes ----------------------------------------------------------------------------------

void MidiOutService::setRoutes(std::vector<Route> routes) {
    std::lock_guard lk(mx_);
    if (routes.empty() && entries_.empty()) return; // every publish comes here: the usual case costs nothing
    const int64_t now = steadyMs();
    for (auto& e : entries_)
        if (e.inGraph) {
            e.inGraph = false;
            e.leftAtMs = now;
        }
    for (auto& r : routes) {
        if (!r.inst) continue;
        auto it = std::find_if(entries_.begin(), entries_.end(), [&](const Entry& e) { return e.inst == r.inst; });
        if (it != entries_.end()) { // still there (or back within the grace, an undo)
            it->inGraph = true;
            it->trackId = r.trackId;
            if (it->uid != it->inst->outputUid()) { // its state was set in place: the old output lets go
                drainEntry(*it);
                sendDue(*it, true, steadyNs());
                if (it->available) release(*it);
                it->book.clear();
                it->uid = it->inst->outputUid();
                it->available = !it->uid.empty() && present(it->uid);
                it->inst->status.store(it->uid.empty() ? 0 : it->available ? 1 : 2, std::memory_order_relaxed);
            }
            continue;
        }
        Entry e;
        e.trackId = r.trackId;
        e.inst = std::move(r.inst);
        e.uid = e.inst->outputUid();
        if (!e.uid.empty() && e.uid.rfind("test:", 0) != 0 && presentGen_ == 0) {
            presentGen_ = MidiInput::portsGeneration(); // the first real route: list the outputs once
            for (auto& d : MidiOutput::list()) presentUids_.push_back(d.uid);
        }
        e.available = !e.uid.empty() && present(e.uid);
        e.inst->status.store(e.uid.empty() ? 0 : e.available ? 1 : 2, std::memory_order_relaxed);
        entries_.push_back(std::move(e));
    }
    if (!entries_.empty()) ensureThread();
}

bool MidiOutService::sendControl(const std::string& uid, const uint8_t* data, int32_t len) {
    std::lock_guard lk(mx_);
    return sendRaw(uid, data, len, 0);
}

void MidiOutService::pump() {
    std::lock_guard lk(mx_);
    if (entries_.empty()) return;
    checkPresence(true);
    pumpLocked(true);
}

// ---- the loop --------------------------------------------------------------------------------

void MidiOutService::pumpLocked(bool everything) {
    if (entries_.empty()) return;
    checkPresence(false);
    const uint64_t nowNs = steadyNs();
    const int64_t nowMs = static_cast<int64_t>(nowNs / 1000000);
    for (auto& e : entries_) {
        drainEntry(e);
        sendDue(e, everything || !e.inGraph, nowNs);
    }
    // Instances that left the graph: once no render can still be playing them, what they hold
    // is released where they sent it (FR-010: a removed track, a replaced instrument).
    for (auto it = entries_.begin(); it != entries_.end();) {
        if (!it->inGraph && (everything || nowMs - it->leftAtMs >= kGoneGraceMs)) {
            drainEntry(*it);
            sendDue(*it, true, nowNs);
            if (it->available) release(*it);
            it = entries_.erase(it);
        } else {
            ++it;
        }
    }
}

void MidiOutService::drainEntry(Entry& e) {
    OutMidiEv ev;
    while (e.inst->ring().pop(ev)) e.pending.push_back(ev);
}

void MidiOutService::sendDue(Entry& e, bool everything, uint64_t nowNs) {
    while (!e.pending.empty()) {
        const OutMidiEv& ev = e.pending.front();
        if (!everything && ev.realtime && ev.dueNs > nowNs + kSendAheadNs) break;
        deliver(e, ev);
        e.pending.pop_front();
    }
}

void MidiOutService::deliver(Entry& e, const OutMidiEv& ev) {
    if (e.uid.empty()) return;
    if (ev.len == 0) { // a flush: stop, mute, clip stop — release what this route holds
        if (e.available) release(e);
        return; // unavailable: the book waits for the device (released on its return)
    }
    if (!e.available) return; // nothing reaches a missing device; what it holds stays booked
    // An offline render (an export, a bounce) is not a performance: a real synth hears nothing of
    // it. Only test ports take it — that is how the smoke test reads what a track sends.
    if (!ev.realtime && e.uid.rfind("test:", 0) != 0) return;
    if (!sendRaw(e.uid, ev.b, ev.len, ev.dueNs)) {
        e.available = false; // unplugged: keep the book for the return (FR-011)
        e.inst->status.store(2, std::memory_order_relaxed);
        return;
    }
    const int s = ev.b[0] & 0xF0, ch = ev.b[0] & 0x0F;
    auto& b = e.book;
    b.usedChannels = static_cast<uint16_t>(b.usedChannels | (1u << ch));
    const int d1 = ev.len > 1 ? ev.b[1] & 0x7F : 0, d2 = ev.len > 2 ? ev.b[2] & 0x7F : 0;
    auto& bits = b.notes[static_cast<size_t>(ch)][static_cast<size_t>(d1 >> 6)];
    const uint64_t bit = 1ull << (d1 & 63);
    if (s == 0x90 && d2 > 0) bits |= bit;
    else if (s == 0x80 || s == 0x90) bits &= ~bit;
    else if (s == 0xB0) {
        auto pedal = [&](int mask) {
            if (d2 >= 64) b.pedals[static_cast<size_t>(ch)] = static_cast<uint8_t>(b.pedals[static_cast<size_t>(ch)] | mask);
            else b.pedals[static_cast<size_t>(ch)] = static_cast<uint8_t>(b.pedals[static_cast<size_t>(ch)] & ~mask);
        };
        if (d1 == 64) pedal(1);
        else if (d1 == 66) pedal(2);
        else if (d1 == 67) pedal(4);
        else if (d1 == 120 || d1 == 123) b.notes[static_cast<size_t>(ch)] = {0, 0};
    }
}

// Note Offs for every note this route still sounds, and its pedals up — on the channels it
// played them on, at the output it played them to.
void MidiOutService::release(Entry& e) {
    auto& b = e.book;
    for (int ch = 0; ch < 16; ++ch) {
        for (int w = 0; w < 2; ++w)
            for (int k = 0; k < 64; ++k)
                if ((b.notes[static_cast<size_t>(ch)][static_cast<size_t>(w)] >> k) & 1u) {
                    const uint8_t m[3] = {static_cast<uint8_t>(0x80 | ch), static_cast<uint8_t>(w * 64 + k), 0};
                    sendRaw(e.uid, m, 3, 0);
                }
        static constexpr uint8_t kPedalCc[3] = {64, 66, 67};
        for (int p = 0; p < 3; ++p)
            if ((b.pedals[static_cast<size_t>(ch)] >> p) & 1u) {
                const uint8_t m[3] = {static_cast<uint8_t>(0xB0 | ch), kPedalCc[p], 0};
                sendRaw(e.uid, m, 3, 0);
            }
        b.notes[static_cast<size_t>(ch)] = {0, 0};
        b.pedals[static_cast<size_t>(ch)] = 0;
    }
}

// Devices came back (FR-011, clarification Q4). Every route that sent to one first releases what
// it held when the device went; then, once per output and channel any of them had sent on,
// Sustain off and All Notes Off — except a channel that a route which stayed connected to that
// output is holding notes on (its notes are none of theirs to end).
void MidiOutService::reconnect(const std::vector<Entry*>& back) {
    std::map<std::string, uint16_t> used; // by output: the channels the returning routes had sent on
    for (Entry* e : back) {
        // What the track played while the device was gone was never heard: it doesn't start
        // now, late. (Its notes' ends are covered by the release below.)
        drainEntry(*e);
        e->pending.clear();
        e->available = true;
        outputs_.erase(e->uid); // a stale handle from before the unplug
        used[e->uid] = static_cast<uint16_t>(used[e->uid] | e->book.usedChannels);
        release(*e);
        e->book.clear();
    }
    for (const auto& [uid, mask] : used)
        for (int ch = 0; ch < 16; ++ch) {
            if (!((mask >> ch) & 1u) || channelHeldByStayer(uid, ch, back)) continue;
            const uint8_t sustainOff[3] = {static_cast<uint8_t>(0xB0 | ch), 64, 0};
            const uint8_t allOff[3] = {static_cast<uint8_t>(0xB0 | ch), 123, 0};
            sendRaw(uid, sustainOff, 3, 0);
            sendRaw(uid, allOff, 3, 0);
        }
}

bool MidiOutService::channelHeldByStayer(const std::string& uid, int ch, const std::vector<Entry*>& back) const {
    for (const auto& o : entries_) {
        if (o.uid != uid || !o.available || std::find(back.begin(), back.end(), &o) != back.end()) continue;
        const auto& n = o.book.notes[static_cast<size_t>(ch)];
        if (n[0] || n[1] || o.book.pedals[static_cast<size_t>(ch)]) return true;
    }
    return false;
}

// ---- outputs ---------------------------------------------------------------------------------

void MidiOutService::checkPresence(bool force) {
    const int64_t now = steadyMs();
    if (!force && now - lastPresenceMs_ < kPresenceEveryMs) return;
    lastPresenceMs_ = now;
    const uint32_t gen = MidiInput::portsGeneration();
    if (force || gen != presentGen_ || presentUids_.empty()) {
        presentGen_ = gen;
        presentUids_.clear();
        for (auto& d : MidiOutput::list()) presentUids_.push_back(d.uid);
    }
    std::vector<Entry*> back;
    for (auto& e : entries_) {
        if (e.uid.empty()) continue;
        const bool here = present(e.uid);
        if (here && !e.available) back.push_back(&e);
        else if (!here && e.available) e.available = false; // gone: what it holds stays booked for its return
        e.inst->status.store(here ? 1 : 2, std::memory_order_relaxed);
    }
    if (!back.empty()) reconnect(back);
}

bool MidiOutService::present(const std::string& uid) {
    if (uid.rfind("test:", 0) == 0) return testPresent_ && testPresent_(uid);
    // Exactly one: two outputs listed under one uid (RtMidi names them; Windows can list twins)
    // are ambiguous, and a route never guesses which one it meant (FR-006, FR-009).
    return std::count(presentUids_.begin(), presentUids_.end(), uid) == 1;
}

bool MidiOutService::sendRaw(const std::string& uid, const uint8_t* data, int32_t len, uint64_t dueNs) {
    if (uid.empty() || !data || len <= 0) return false;
    if (uid.rfind("test:", 0) == 0) return testSend_ && testSend_(uid, data, len, dueNs);
    auto it = outputs_.find(uid);
    if (it == outputs_.end()) {
        auto out = std::make_unique<MidiOutput>();
        if (!out->open(uid)) return false;
        it = outputs_.emplace(uid, std::move(out)).first;
    }
    if (it->second->send(data, len)) return true;
    outputs_.erase(it); // unplugged: reopened on the next send
    return false;
}

} // namespace nota
