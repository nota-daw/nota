// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Claim table, surface queue and SysEx ring for control surfaces (specs/051). See the header
// for why routing is decided here, on the MIDI thread.

#include "engine/MidiSurface.h"

#include "audio_io/MidiParser.h"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstring>
#include <thread>

namespace nota {

namespace {
int64_t nowMs() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch())
        .count();
}
} // namespace

MidiSurface::MidiSurface() {
    for (auto& t : identityUntilMs_) t.store(0, std::memory_order_relaxed);
}

MidiSurface::~MidiSurface() {
    delete table_.load();
    for (const Table* t : retired_) delete t;
}

int MidiSurface::statusIndex(int32_t status) {
    switch (status & 0xF0) {
    case 0x80:
    case 0x90: return 0; // a note claim covers on and off
    case 0xB0: return 1;
    case 0xD0: return 2;
    case 0xE0: return 3;
    default: return -1;
    }
}

bool MidiSurface::setClaims(const MidiClaimEntry* claims, int32_t count) {
    if (count < 0 || (count > 0 && !claims)) return false;
    auto* t = new Table();
    for (int32_t i = 0; i < count; ++i) {
        const MidiClaimEntry& c = claims[i];
        if (c.source < 0 || c.source >= kSources) {
            delete t;
            return false;
        }
        auto& s = t->sources[static_cast<size_t>(c.source)];
        if (c.wholePort) {
            s.whole = s.any = true;
            continue;
        }
        const int si = (c.status == 0x80 || c.status == 0xB0 || c.status == 0xD0 || c.status == 0xE0)
                           ? statusIndex(c.status)
                           : -1;
        if (si < 0 || c.channel < 0 || c.channel > 15 || c.data1 < 0 || c.data1 > 127) {
            delete t;
            return false;
        }
        s.keys.set(key(si, c.channel, si >= 2 ? 0 : c.data1));
        s.any = true;
    }
    const Table* old = table_.exchange(t);
    if (old) retired_.push_back(old);
    reclaim();
    return true;
}

// A MIDI thread that loaded the old table may still be reading it; free retired tables only
// once no reader is inside claimed(). Any reader arriving after the exchange sees the new one.
void MidiSurface::reclaim() {
    for (int spin = 0; spin < 1000 && readers_.load() != 0; ++spin) std::this_thread::yield();
    if (readers_.load() != 0) return; // try again on the next publish
    for (const Table* t : retired_) delete t;
    retired_.clear();
}

bool MidiSurface::claimed(int32_t source, int32_t status, int32_t channel, int32_t d1) const {
    if (source < 0 || source >= kSources) return false;
    const int si = statusIndex(status);
    if (si < 0 || channel < 0 || channel > 15) return false;
    readers_.fetch_add(1);
    const Table* t = table_.load();
    bool hit = false;
    if (t) {
        const auto& s = t->sources[static_cast<size_t>(source)];
        hit = s.whole || (s.any && s.keys.test(key(si, channel, si >= 2 ? 0 : (d1 & 0x7F))));
    }
    readers_.fetch_sub(1);
    return hit;
}

bool MidiSurface::sourceHasClaim(int32_t source) const {
    if (source < 0 || source >= kSources) return false;
    readers_.fetch_add(1);
    const Table* t = table_.load();
    const bool any = t && t->sources[static_cast<size_t>(source)].any;
    readers_.fetch_sub(1);
    return any;
}

void MidiSurface::pushEvent(const SurfaceEvent& ev) {
    std::lock_guard lock(producerMx_);
    if (!events_.push(ev)) dropped_.fetch_add(1, std::memory_order_relaxed);
}

int32_t MidiSurface::pollEvents(SurfaceEvent* out, int32_t max) {
    if (!out || max <= 0) return 0;
    int32_t n = 0;
    while (n < max && events_.pop(out[n])) ++n;
    return n;
}

void MidiSurface::armIdentity(int32_t source) {
    if (source < 0 || source >= kSources) return;
    identityUntilMs_[static_cast<size_t>(source)].store(nowMs() + 2000, std::memory_order_relaxed);
}

void MidiSurface::pushSysex(int32_t source, const uint8_t* data, int32_t len) {
    if (source < 0 || source >= kSources || !data || len <= 0 || len > kMaxSysex) return;
    const bool identity = identityUntilMs_[static_cast<size_t>(source)].load(std::memory_order_relaxed) > nowMs();
    if (!identity && !sourceHasClaim(source)) return;
    SysexMsg m;
    m.source = source;
    m.len = len;
    std::memcpy(m.data, data, static_cast<size_t>(len));
    std::lock_guard lock(sysexMx_);
    sysex_.push(m); // full → dropped: SysEx is a handshake / reply, the app re-asks
}

int32_t MidiSurface::pollSysex(int32_t* outSource, uint8_t* out, int32_t cap) {
    SysexMsg m;
    if (!sysex_.pop(m)) return 0;
    if (outSource) *outSource = m.source;
    if (!out || cap < m.len) return -m.len;
    std::memcpy(out, m.data, static_cast<size_t>(m.len));
    return m.len;
}

void MidiLoopback::write(const uint8_t* data, size_t len) {
    if (!data || len == 0) return;
    std::lock_guard lock(mx_);
    bytes_.insert(bytes_.end(), data, data + len);
    if (bytes_.size() > kCap) bytes_.erase(bytes_.begin(), bytes_.begin() + static_cast<std::ptrdiff_t>(bytes_.size() - kCap));
}

int32_t MidiLoopback::take(uint8_t* out, int32_t cap) {
    std::lock_guard lock(mx_);
    if (!out || cap <= 0) return 0;
    const auto n = static_cast<int32_t>(std::min<size_t>(bytes_.size(), static_cast<size_t>(cap)));
    std::memcpy(out, bytes_.data(), static_cast<size_t>(n));
    bytes_.erase(bytes_.begin(), bytes_.begin() + n);
    return n;
}

// ---- self-test -----------------------------------------------------------------------------

int32_t MidiSurface::selfTest() {
    MidiSurface s;
    int instruments = 0, learned = 0;
    auto send = [&](int32_t source, int32_t status, int32_t ch, int32_t d1, int32_t d2) {
        s.route(
            source, status, ch, d1, d2, [&] { ++instruments; }, [&](int, int, int, int) { ++learned; });
    };
    SurfaceEvent ev[8];

    // 1. claimed CC → surface queue, not instruments, no learn copy.
    const MidiClaimEntry cc20{2, 0, 0xB0, 0, 20};
    if (!s.setClaims(&cc20, 1)) return 1;
    send(2, 0xB0, 0, 20, 64);
    if (instruments != 0 || learned != 0 || s.pollEvents(ev, 8) != 1 || ev[0].d1 != 20 || ev[0].source != 2) return 1;

    // 2. unclaimed: another CC, the same CC on another channel or source, a note → as before.
    send(2, 0xB0, 0, 21, 1);
    send(2, 0xB0, 1, 20, 1);
    send(3, 0xB0, 0, 20, 1);
    send(2, 0x90, 0, 60, 100);
    if (instruments != 4 || learned != 4 || s.pollEvents(ev, 8) != 0) return 2;

    // 3. whole port: every channel message, note-off velocity 0 reported as 0x80; learn copy
    // only while MIDI Learn listens.
    instruments = learned = 0;
    const MidiClaimEntry whole{5, 1, 0, 0, 0};
    if (!s.setClaims(&whole, 1)) return 3;
    send(5, 0x90, 3, 94, 127);
    send(5, 0x90, 3, 94, 0);
    send(5, 0xE0, 0, 0, 64);
    if (instruments != 0 || learned != 0 || s.pollEvents(ev, 8) != 3 || ev[1].status != 0x80) return 3;
    s.setLearnListening(true);
    send(5, 0xB0, 0, 7, 10);
    s.setLearnListening(false);
    if (instruments != 0 || learned != 1 || s.pollEvents(ev, 8) != 1) return 3;
    const MidiClaimEntry bad{5, 0, 0xC0, 0, 0}; // program change can't be claimed
    if (s.setClaims(&bad, 1) || !s.claimed(5, 0xB0, 9, 1)) return 3;

    // 4. SysEx reassembled across packets, with real-time bytes inside, from a claimed source;
    // ignored from an unclaimed one unless an identity request is outstanding.
    MidiParser p;
    auto feed = [&](int32_t source, std::initializer_list<uint8_t> bytes) {
        std::vector<uint8_t> v(bytes);
        p.feed(
            v.data(), v.size(), [](int, int, int, int) {},
            [&](const uint8_t* d, int32_t len) { s.pushSysex(source, d, len); });
    };
    feed(5, {0xF0, 0x7E, 0x00});
    feed(5, {0x06, 0xF8, 0x02, 0x00, 0x20, 0x6B, 0xF7});
    uint8_t buf[600];
    int32_t src = -1;
    const int32_t n4 = s.pollSysex(&src, buf, sizeof buf);
    if (n4 != 9 || src != 5 || buf[0] != 0xF0 || buf[3] != 0x06 || buf[4] != 0x02 || buf[8] != 0xF7) return 4;
    feed(6, {0xF0, 0x01, 0xF7});
    if (s.pollSysex(&src, buf, sizeof buf) != 0) return 4;
    s.armIdentity(6);
    feed(6, {0xF0, 0x01, 0xF7});
    if (s.pollSysex(&src, buf, sizeof buf) != 3 || src != 6) return 4;

    // 5. a SysEx over the cap is dropped whole; the parser recovers for the next one.
    {
        std::vector<uint8_t> big(600, 0x11);
        big.front() = 0xF0;
        big.back() = 0xF7;
        p.feed(
            big.data(), big.size(), [](int, int, int, int) {},
            [&](const uint8_t* d, int32_t len) { s.pushSysex(5, d, len); });
        feed(5, {0xF0, 0x02, 0xF7});
        if (s.pollSysex(&src, buf, sizeof buf) != 3 || buf[1] != 0x02) return 5;
        if (s.pollSysex(&src, buf, sizeof buf) != 0) return 5;
    }

    // 6. output loopback returns exactly what was written, in order.
    {
        MidiLoopback lb;
        const uint8_t a[] = {0xE0, 0x00, 0x40};
        const uint8_t b[] = {0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, 0x00, 'N', 'o', 't', 'a', 0xF7};
        lb.write(a, sizeof a);
        lb.write(b, sizeof b);
        uint8_t out[64];
        const int32_t n6 = lb.take(out, sizeof out);
        if (n6 != static_cast<int32_t>(sizeof a + sizeof b) || std::memcmp(out, a, sizeof a) != 0 ||
            std::memcmp(out + sizeof a, b, sizeof b) != 0 || lb.take(out, sizeof out) != 0)
            return 6;
    }
    return 0;
}

} // namespace nota
