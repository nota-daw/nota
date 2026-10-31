// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — pattern serialization and the arpeggiator's note order (see TwinSeq.h).

#include "devices/instruments/TwinSeq.h"

#include <algorithm>
#include <cstring>

namespace nota {
namespace twin {

namespace {

constexpr uint32_t kMagic = 0x31505754; // "TWP1"

void put8(std::vector<uint8_t>& b, uint8_t v) { b.push_back(v); }
void put16(std::vector<uint8_t>& b, uint16_t v) {
    b.push_back(static_cast<uint8_t>(v));
    b.push_back(static_cast<uint8_t>(v >> 8));
}
void put32(std::vector<uint8_t>& b, uint32_t v) {
    for (int i = 0; i < 4; ++i) b.push_back(static_cast<uint8_t>(v >> (8 * i)));
}

struct Reader {
    const uint8_t* p;
    int32_t left;
    bool ok = true;
    uint8_t u8() {
        if (left < 1) return ok = false, 0;
        --left;
        return *p++;
    }
    uint16_t u16() {
        const uint16_t lo = u8(), hi = u8();
        return static_cast<uint16_t>(lo | (hi << 8));
    }
    uint32_t u32() {
        uint32_t v = 0;
        for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(u8()) << (8 * i);
        return v;
    }
};

uint32_t hash(uint32_t x) {
    x ^= x >> 16;
    x *= 0x7feb352dU;
    x ^= x >> 15;
    x *= 0x846ca68bU;
    x ^= x >> 16;
    return x;
}

} // namespace

std::vector<uint8_t> serializePatterns(const PatternSet& ps) {
    std::vector<uint8_t> b;
    b.reserve(4 + kPatterns * (1 + kSteps * 9 + kLanes * (3 + 2 * kSteps)));
    put32(b, kMagic);
    for (const Pattern& p : ps.pat) {
        put8(b, p.length);
        for (const Step& s : p.step) {
            put8(b, s.n);
            for (uint8_t n : s.note) put8(b, n);
            put8(b, s.vel);
            put8(b, s.gate);
        }
        for (const Lane& l : p.lane) {
            put16(b, static_cast<uint16_t>(l.target));
            put8(b, l.smooth);
            for (uint16_t v : l.val) put16(b, v);
        }
    }
    return b;
}

bool deserializePatterns(const uint8_t* data, int32_t size, PatternSet& out) {
    Reader r{data, size};
    if (!data || r.u32() != kMagic) return false;
    PatternSet ps;
    for (Pattern& p : ps.pat) {
        p.length = static_cast<uint8_t>(std::clamp<int>(r.u8(), 1, kSteps));
        for (Step& s : p.step) {
            s.n = static_cast<uint8_t>(std::min<int>(r.u8(), kStepNotes));
            for (uint8_t& n : s.note) n = static_cast<uint8_t>(std::min<int>(r.u8(), 127));
            s.vel = static_cast<uint8_t>(std::clamp<int>(r.u8(), 1, 127));
            s.gate = static_cast<uint8_t>(std::clamp<int>(r.u8(), 1, kGateTie));
        }
        for (Lane& l : p.lane) {
            l.target = static_cast<int16_t>(r.u16());
            l.smooth = r.u8() ? 1 : 0;
            for (uint16_t& v : l.val) v = r.u16();
        }
    }
    if (!r.ok) return false;
    out = ps;
    return true;
}

int arpNotes(const int* held, int n, int mode, int octaves, int64_t k, uint32_t seed, int* out, int maxN) {
    if (n <= 0 || maxN <= 0) return 0;
    octaves = std::clamp(octaves, 1, 4);
    // The run: the held notes in the mode's order, repeated an octave up per octave.
    int sorted[32];
    const int m = std::min(n, 32);
    std::memcpy(sorted, held, sizeof(int) * static_cast<size_t>(m));
    if (mode != ArpAsPlayed) std::sort(sorted, sorted + m);
    if (mode == ArpChord) {
        const int oct = static_cast<int>(((k % octaves) + octaves) % octaves);
        const int c = std::min(m, maxN);
        for (int i = 0; i < c; ++i) out[i] = std::min(127, sorted[i] + 12 * oct);
        return c;
    }
    const int len = m * octaves;
    auto at = [&](int i) { return std::min(127, sorted[i % m] + 12 * (i / m)); };
    int idx;
    switch (mode) {
    case ArpDown: idx = len - 1 - static_cast<int>(((k % len) + len) % len); break;
    case ArpUpDown: {
        const int cyc = len > 1 ? 2 * len - 2 : 1;
        const int pos = static_cast<int>(((k % cyc) + cyc) % cyc);
        idx = pos < len ? pos : cyc - pos;
        break;
    }
    case ArpRandom: idx = static_cast<int>(hash(seed ^ static_cast<uint32_t>(k) * 0x9E3779B9U) % static_cast<uint32_t>(len)); break;
    default: idx = static_cast<int>(((k % len) + len) % len); break; // Up, As played
    }
    out[0] = at(idx);
    return 1;
}

} // namespace twin
} // namespace nota
