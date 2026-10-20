// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MosaicProgram — the multisample program behind Nota Mosaic (kind 17), split out of
// Mosaic.h so the instrument header only owns voices and params. A *program* maps many
// samples onto the keyboard: zones (a sample on a key × velocity rectangle with its own
// root, tune, gain, pan, start/end and loop) gathered in groups (layers with a shared
// gain / tune and a round-robin).
//
// The program arrives as text (MosaicProgram.cs writes it; see parseProgram for the
// grammar) and lives in the project / preset as that text. Its samples are referenced, not
// copied: an absolute path, or "<root>:<relative path>" against a root the host sets
// (samples: the user's Samples folder, data: Nota's data folder), so a pack installed on
// another computer resolves where it lives there. Loading is asynchronous — a worker pool
// decodes the files into RAM (shared process-wide by path, so two tracks of one piano hold
// it once); notes stay silent until the whole program is in, and a program that only edits
// zones (same files) swaps in at once.
//
// Threads. The message thread parses and publishes; loader threads decode; the audio thread
// reads the live program through an atomic raw pointer. Parsing/loading math is kept
// verbatim — no FastMath migration (path resolution and the text grammar are exact).

#pragma once

#include "media/AudioFile.h"
#include "media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace nota {

namespace mosaic {

// ---- path roots ----------------------------------------------------------------------
// "samples:Downloaded/Osiris Piano/C4.flac" resolves against the "samples" root. The host
// sets the roots at start and when the user moves a folder; unknown roots don't resolve.
inline std::mutex& rootMx() {
    static std::mutex m;
    return m;
}
inline std::map<std::string, std::string>& roots() {
    static std::map<std::string, std::string> r;
    return r;
}
inline void setRoot(const std::string& name, const std::string& path) {
    std::lock_guard<std::mutex> lk(rootMx());
    roots()[name] = path;
}
inline std::string resolve(const std::string& ref) {
    const auto colon = ref.find(':');
    // "C:\…" (a Windows drive) and plain absolute paths pass through.
    if (colon == std::string::npos || colon < 2 || ref.compare(0, 1, "/") == 0) return ref;
    const std::string name = ref.substr(0, colon);
    std::string base;
    {
        std::lock_guard<std::mutex> lk(rootMx());
        auto it = roots().find(name);
        if (it == roots().end()) return ref;
        base = it->second;
    }
    std::string rel = ref.substr(colon + 1);
    if (!base.empty() && base.back() != '/' && base.back() != '\\') base += '/';
    return base + rel;
}

// ---- the process-wide sample cache -----------------------------------------------------
// Weak by path: a buffer lives while some program holds it.
inline std::mutex& cacheMx() {
    static std::mutex m;
    return m;
}
inline std::map<std::string, std::weak_ptr<SampleBuffer>>& cache() {
    static std::map<std::string, std::weak_ptr<SampleBuffer>> c;
    return c;
}
inline std::shared_ptr<SampleBuffer> cached(const std::string& path) {
    std::lock_guard<std::mutex> lk(cacheMx());
    auto it = cache().find(path);
    if (it == cache().end()) return nullptr;
    auto sp = it->second.lock();
    if (!sp) cache().erase(it);
    return sp;
}
inline void remember(const std::string& path, const std::shared_ptr<SampleBuffer>& b) {
    std::lock_guard<std::mutex> lk(cacheMx());
    cache()[path] = b;
}

// ---- text helpers ----------------------------------------------------------------------
// Locale-proof number parsing (strtod follows the C locale the host may change).
inline double num(const char* s, double def) {
    if (!s || !*s) return def;
    bool neg = false;
    double v = 0, scale = 1;
    bool any = false, frac = false;
    if (*s == '-') {
        neg = true;
        ++s;
    } else if (*s == '+')
        ++s;
    for (; *s; ++s) {
        if (*s >= '0' && *s <= '9') {
            any = true;
            if (frac) {
                scale *= 0.1;
                v += (*s - '0') * scale;
            } else
                v = v * 10 + (*s - '0');
        } else if (*s == '.' && !frac)
            frac = true;
        else if (*s == 'e' || *s == 'E') {
            const double ex = num(s + 1, 0);
            v *= std::pow(10.0, ex);
            break;
        } else
            break;
    }
    if (!any) return def;
    return neg ? -v : v;
}
inline int hexv(char c) {
    return c >= '0' && c <= '9'   ? c - '0'
           : c >= 'a' && c <= 'f' ? c - 'a' + 10
           : c >= 'A' && c <= 'F' ? c - 'A' + 10
                                  : -1;
}
inline std::string unescape(const std::string& s) { // %XX → byte
    std::string o;
    o.reserve(s.size());
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] == '%' && i + 2 < s.size() && hexv(s[i + 1]) >= 0 && hexv(s[i + 2]) >= 0) {
            o += static_cast<char>(hexv(s[i + 1]) * 16 + hexv(s[i + 2]));
            i += 2;
        } else
            o += s[i];
    }
    return o;
}

// ---- the program -------------------------------------------------------------------------
struct Zone {
    int32_t file = -1, group = 0, root = 60;
    int32_t klo = 0, khi = 127, vlo = 0, vhi = 127;
    float tune = 0.0f, gainDb = 0.0f, pan = 0.0f;             // cents · dB · −1..+1
    double start = 0, end = -1, loopStart = -1, loopEnd = -1; // frames; −1 = the sample's own edge
    int32_t loopMode = 0;                                     // 0 off · 1 forward · 2 ping-pong
    float xfade = 0.0f;                                       // loop crossfade, seconds
    int32_t seq = 1;                                          // round-robin step (1-based)
    float rlo = 0.0f, rhi = 1.0f;                             // random range (SFZ lorand / hirand)
    int32_t xkil = -1, xkih = -1, xkol = -1, xkoh = -1;       // key crossfade in / out ranges
    int32_t xvil = -1, xvih = -1, xvol = -1, xvoh = -1;       // velocity crossfade in / out ranges
    int32_t trig = 0;                                         // 0 attack · 1 release
    int32_t excl = 0, offBy = 0, offMode = 0;                 // exclusive group · off by · 0 fast / 1 normal
};
struct Group {
    float gainDb = 0.0f, tune = 0.0f;
    int32_t rrMode = 0; // 0 sequential · 1 random · 2 random without repeat
    int32_t seqLen = 1;
};

struct Program {
    std::vector<std::string> refs;  // as written (persisted)
    std::vector<std::string> paths; // resolved
    std::vector<Group> groups;
    std::vector<Zone> zones;
    std::string text; // the source text, verbatim
    // Decoded samples: owned here, published to the audio thread through `ptr`.
    std::vector<std::shared_ptr<SampleBuffer>> owned;
    std::unique_ptr<std::atomic<SampleBuffer*>[]> ptr;
    std::vector<int64_t> diskBytes;
    std::atomic<int32_t> done{0}, missing{0};
    std::atomic<int64_t> bytesDone{0}, ramBytes{0};
    int64_t diskTotal = 0;
    std::atomic<bool> ready{false}, cancel{false};
    std::mutex ownMx;              // owned[] writes (loader threads)
    int32_t serial = nextSerial(); // process-unique: an editor notices a new program

    static int32_t nextSerial() {
        static std::atomic<int32_t> n{1};
        return n.fetch_add(1, std::memory_order_relaxed);
    }

    const SampleBuffer* sample(int32_t f) const {
        return (f >= 0 && f < static_cast<int32_t>(paths.size())) ? ptr[f].load(std::memory_order_acquire) : nullptr;
    }
};

// Parses the program text. One record per line, "key=value" tokens after the record name;
// names and paths are %-escaped (no spaces). Unknown records and keys are skipped, so a
// newer program still loads its known part.
//   mosaic 1
//   name <escaped>            · source <kind> <escaped ref> · pack <id> <escaped name>   (informational)
//   file <escaped ref>        (index = order)
//   group gain= tune= rr= seq= name=
//   zone f= g= root= klo= khi= vlo= vhi= tune= gain= pan= s= e= lm= ls= le= xf= seq= rlo= rhi=
//        xkil= xkih= xkol= xkoh= xvil= xvih= xvol= xvoh= trig= excl= off= om=
std::shared_ptr<Program> parseProgram(const std::string& text);

inline void installSample(Program& p, size_t f, std::shared_ptr<SampleBuffer> b) {
    {
        std::lock_guard<std::mutex> lk(p.ownMx);
        p.owned[f] = b;
    }
    p.ptr[f].store(b.get(), std::memory_order_release);
    p.ramBytes.fetch_add(static_cast<int64_t>(b->samples.size() * sizeof(float)), std::memory_order_relaxed);
}

// Decodes every file not yet in; marks the program ready at the end. Runs on its own
// threads (a small pool) — the shared_ptr keeps the program alive however long it takes.
void loadProgram(std::shared_ptr<Program> p);

} // namespace mosaic

} // namespace nota
