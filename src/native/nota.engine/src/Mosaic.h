// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic — the built-in multisample instrument (kind 17). A *program* maps many
// samples onto the keyboard: zones (a sample on a key × velocity rectangle with its own
// root, tune, gain, pan, start/end and loop) gathered in groups (layers with a shared
// gain / tune and a round-robin). On a note-on every zone that covers the key, the
// velocity and the group's current round-robin step sounds at once; crossfade ranges
// blend neighbouring layers. Zones flagged as release triggers sound when the key (or the
// pedal) lets go — the piano's damper thump — quieter the longer the note was held.
//
// The program arrives as text (MosaicProgram.cs writes it; see parseProgram below for the
// grammar) and lives in the project / preset as that text. Its samples are referenced, not
// copied: an absolute path, or "<root>:<relative path>" against a root the host sets
// (samples: the user's Samples folder, data: Nota's data folder), so a pack installed on
// another computer resolves where it lives there. Loading is asynchronous — a worker pool
// decodes the files into RAM (shared process-wide by path, so two tracks of one piano hold
// it once); notes stay silent until the whole program is in, and a program that only edits
// zones (same files) swaps in at once.
//
// Voices: Poly (16 / 32 / 64 / 128), Mono (legato glide) or Choke. The pool steals released
// voices first, then the quietest, then the oldest, each with a short fade (Old fade); a
// key keeps at most Per key strikes ringing. The sustain pedal (CC64) holds released keys.
// Amp ADSR, a TPT state-variable filter (key-tracking, env → cutoff), glide, Vel → Vol and a
// velocity curve are shared by every zone, like Nota Sampler's. MPE: bend retunes the note
// (the wheel by Bend range), pressure adds level, slide opens the filter.
//
// Threads. The message thread parses and publishes; loader threads decode; the audio thread
// reads the live program through an atomic raw pointer. Programs are kept alive by the
// instrument and retired with the audio thread's render epoch (freed once two renders have
// passed), the engine's own preview pattern, so the audio thread never frees memory.
// All continuous controls are plugin params (normalized 0..1, stable ids, APPEND-ONLY).

#pragma once

#include "AudioFile.h"
#include "Instrument.h"
#include "NoteExpression.h"
#include "SampleBuffer.h"

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
inline std::mutex& rootMx() { static std::mutex m; return m; }
inline std::map<std::string, std::string>& roots() { static std::map<std::string, std::string> r; return r; }
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
inline std::mutex& cacheMx() { static std::mutex m; return m; }
inline std::map<std::string, std::weak_ptr<SampleBuffer>>& cache() { static std::map<std::string, std::weak_ptr<SampleBuffer>> c; return c; }
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
    bool neg = false; double v = 0, scale = 1; bool any = false, frac = false;
    if (*s == '-') { neg = true; ++s; } else if (*s == '+') ++s;
    for (; *s; ++s) {
        if (*s >= '0' && *s <= '9') { any = true; if (frac) { scale *= 0.1; v += (*s - '0') * scale; } else v = v * 10 + (*s - '0'); }
        else if (*s == '.' && !frac) frac = true;
        else if (*s == 'e' || *s == 'E') { const double ex = num(s + 1, 0); v *= std::pow(10.0, ex); break; }
        else break;
    }
    if (!any) return def;
    return neg ? -v : v;
}
inline int hexv(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }
inline std::string unescape(const std::string& s) {   // %XX → byte
    std::string o; o.reserve(s.size());
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] == '%' && i + 2 < s.size() && hexv(s[i + 1]) >= 0 && hexv(s[i + 2]) >= 0) {
            o += static_cast<char>(hexv(s[i + 1]) * 16 + hexv(s[i + 2])); i += 2;
        } else o += s[i];
    }
    return o;
}

// ---- the program -------------------------------------------------------------------------
struct Zone {
    int32_t file = -1, group = 0, root = 60;
    int32_t klo = 0, khi = 127, vlo = 0, vhi = 127;
    float   tune = 0.0f, gainDb = 0.0f, pan = 0.0f;          // cents · dB · −1..+1
    double  start = 0, end = -1, loopStart = -1, loopEnd = -1; // frames; −1 = the sample's own edge
    int32_t loopMode = 0;                                       // 0 off · 1 forward · 2 ping-pong
    float   xfade = 0.0f;                                       // loop crossfade, seconds
    int32_t seq = 1;                                            // round-robin step (1-based)
    float   rlo = 0.0f, rhi = 1.0f;                             // random range (SFZ lorand / hirand)
    int32_t xkil = -1, xkih = -1, xkol = -1, xkoh = -1;         // key crossfade in / out ranges
    int32_t xvil = -1, xvih = -1, xvol = -1, xvoh = -1;         // velocity crossfade in / out ranges
    int32_t trig = 0;                                           // 0 attack · 1 release
    int32_t excl = 0, offBy = 0, offMode = 0;                   // exclusive group · off by · 0 fast / 1 normal
};
struct Group {
    float   gainDb = 0.0f, tune = 0.0f;
    int32_t rrMode = 0;   // 0 sequential · 1 random · 2 random without repeat
    int32_t seqLen = 1;
};

struct Program {
    std::vector<std::string> refs;                  // as written (persisted)
    std::vector<std::string> paths;                 // resolved
    std::vector<Group> groups;
    std::vector<Zone> zones;
    std::string text;                               // the source text, verbatim
    // Decoded samples: owned here, published to the audio thread through `ptr`.
    std::vector<std::shared_ptr<SampleBuffer>> owned;
    std::unique_ptr<std::atomic<SampleBuffer*>[]> ptr;
    std::vector<int64_t> diskBytes;
    std::atomic<int32_t> done{0}, missing{0};
    std::atomic<int64_t> bytesDone{0}, ramBytes{0};
    int64_t diskTotal = 0;
    std::atomic<bool> ready{false}, cancel{false};
    std::mutex ownMx;                               // owned[] writes (loader threads)
    int32_t serial = nextSerial();                  // process-unique: an editor notices a new program

    static int32_t nextSerial() { static std::atomic<int32_t> n{1}; return n.fetch_add(1, std::memory_order_relaxed); }

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
inline std::shared_ptr<Program> parseProgram(const std::string& text) {
    auto p = std::make_shared<Program>();
    p->text = text;
    size_t i = 0;
    std::vector<std::string> tok;
    while (i <= text.size()) {
        size_t j = text.find('\n', i);
        if (j == std::string::npos) j = text.size();
        std::string line = text.substr(i, j - i);
        i = j + 1;
        if (!line.empty() && line.back() == '\r') line.pop_back();
        tok.clear();
        size_t a = 0;
        while (a < line.size()) {
            while (a < line.size() && (line[a] == ' ' || line[a] == '\t')) ++a;
            size_t b = a;
            while (b < line.size() && line[b] != ' ' && line[b] != '\t') ++b;
            if (b > a) tok.push_back(line.substr(a, b - a));
            a = b;
        }
        if (tok.empty()) { if (j >= text.size()) break; continue; }
        auto kv = [&](const char* key, double def) -> double {
            const size_t n = std::strlen(key);
            for (size_t t = 1; t < tok.size(); ++t)
                if (tok[t].size() > n && tok[t].compare(0, n, key) == 0 && tok[t][n] == '=') return num(tok[t].c_str() + n + 1, def);
            return def;
        };
        const std::string& rec = tok[0];
        if (rec == "file" && tok.size() >= 2) {
            p->refs.push_back(unescape(tok[1]));
        } else if (rec == "group") {
            Group g;
            g.gainDb = static_cast<float>(kv("gain", 0)); g.tune = static_cast<float>(kv("tune", 0));
            g.rrMode = std::clamp(static_cast<int32_t>(kv("rr", 0)), 0, 2);
            g.seqLen = std::clamp(static_cast<int32_t>(kv("seq", 1)), 1, 64);
            p->groups.push_back(g);
        } else if (rec == "zone") {
            Zone z;
            z.file = static_cast<int32_t>(kv("f", -1)); z.group = static_cast<int32_t>(kv("g", 0));
            z.root = std::clamp(static_cast<int32_t>(kv("root", 60)), 0, 127);
            z.klo = std::clamp(static_cast<int32_t>(kv("klo", 0)), 0, 127); z.khi = std::clamp(static_cast<int32_t>(kv("khi", 127)), 0, 127);
            z.vlo = std::clamp(static_cast<int32_t>(kv("vlo", 0)), 0, 127); z.vhi = std::clamp(static_cast<int32_t>(kv("vhi", 127)), 0, 127);
            z.tune = static_cast<float>(kv("tune", 0)); z.gainDb = static_cast<float>(kv("gain", 0));
            z.pan = std::clamp(static_cast<float>(kv("pan", 0)), -1.0f, 1.0f);
            z.start = std::max(0.0, kv("s", 0)); z.end = kv("e", -1);
            z.loopMode = std::clamp(static_cast<int32_t>(kv("lm", 0)), 0, 2);
            z.loopStart = kv("ls", -1); z.loopEnd = kv("le", -1);
            z.xfade = static_cast<float>(std::clamp(kv("xf", 0), 0.0, 2.0));
            z.seq = std::max(1, static_cast<int32_t>(kv("seq", 1)));
            z.rlo = static_cast<float>(kv("rlo", 0)); z.rhi = static_cast<float>(kv("rhi", 1));
            z.xkil = static_cast<int32_t>(kv("xkil", -1)); z.xkih = static_cast<int32_t>(kv("xkih", -1));
            z.xkol = static_cast<int32_t>(kv("xkol", -1)); z.xkoh = static_cast<int32_t>(kv("xkoh", -1));
            z.xvil = static_cast<int32_t>(kv("xvil", -1)); z.xvih = static_cast<int32_t>(kv("xvih", -1));
            z.xvol = static_cast<int32_t>(kv("xvol", -1)); z.xvoh = static_cast<int32_t>(kv("xvoh", -1));
            z.trig = kv("trig", 0) >= 0.5 ? 1 : 0;
            z.excl = static_cast<int32_t>(kv("excl", 0)); z.offBy = static_cast<int32_t>(kv("off", 0));
            z.offMode = kv("om", 0) >= 0.5 ? 1 : 0;
            if (z.klo > z.khi) std::swap(z.klo, z.khi);
            if (z.vlo > z.vhi) std::swap(z.vlo, z.vhi);
            p->zones.push_back(z);
        }
        if (j >= text.size()) break;
    }
    if (p->groups.empty()) p->groups.push_back(Group{});
    const int32_t nf = static_cast<int32_t>(p->refs.size()), ng = static_cast<int32_t>(p->groups.size());
    // Drop zones that point nowhere; clamp groups.
    p->zones.erase(std::remove_if(p->zones.begin(), p->zones.end(), [&](const Zone& z) { return z.file < 0 || z.file >= nf; }), p->zones.end());
    for (auto& z : p->zones) z.group = std::clamp(z.group, 0, ng - 1);
    for (const auto& r : p->refs) p->paths.push_back(resolve(r));
    p->owned.resize(p->paths.size());
    p->ptr.reset(new std::atomic<SampleBuffer*>[std::max<size_t>(1, p->paths.size())]);
    for (size_t f = 0; f < p->paths.size(); ++f) p->ptr[f].store(nullptr, std::memory_order_relaxed);
    p->diskBytes.assign(p->paths.size(), 0);
    for (size_t f = 0; f < p->paths.size(); ++f) {
        std::error_code ec;
        const auto sz = std::filesystem::file_size(std::filesystem::path(std::u8string(p->paths[f].begin(), p->paths[f].end())), ec);
        p->diskBytes[f] = ec ? 0 : static_cast<int64_t>(sz);
        p->diskTotal += p->diskBytes[f];
    }
    return p;
}

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
inline void loadProgram(std::shared_ptr<Program> p) {
    const size_t n = p->paths.size();
    std::vector<size_t> todo;
    for (size_t f = 0; f < n; ++f) {
        if (p->ptr[f].load(std::memory_order_relaxed)) continue;
        if (auto c = cached(p->paths[f])) { installSample(*p, f, c); p->done.fetch_add(1); p->bytesDone.fetch_add(p->diskBytes[f]); continue; }
        todo.push_back(f);
    }
    if (todo.empty()) { p->ready.store(true, std::memory_order_release); return; }
    auto next = std::make_shared<std::atomic<size_t>>(0);
    const unsigned hw = std::max(1u, std::thread::hardware_concurrency());
    const int workers = static_cast<int>(std::clamp<unsigned>(hw / 2, 1u, 4u));
    auto left = std::make_shared<std::atomic<int>>(workers);
    for (int w = 0; w < workers; ++w) {
        std::thread([p, todo, next, left]() {
            for (;;) {
                if (p->cancel.load(std::memory_order_relaxed)) break;
                const size_t k = next->fetch_add(1);
                if (k >= todo.size()) break;
                const size_t f = todo[k];
                std::shared_ptr<SampleBuffer> b = cached(p->paths[f]);
                if (!b) {
                    b = decodeAudioFile(p->paths[f]);
                    if (b && !b->empty()) remember(p->paths[f], b);
                }
                if (b && !b->empty()) installSample(*p, f, b);
                else p->missing.fetch_add(1);
                p->bytesDone.fetch_add(p->diskBytes[f]);
                p->done.fetch_add(1);
            }
            if (left->fetch_sub(1) == 1 && !p->cancel.load(std::memory_order_relaxed))
                p->ready.store(true, std::memory_order_release);
        }).detach();
    }
}

} // namespace mosaic

class Mosaic final : public Instrument {
public:
    enum Param {
        Volume = 0, Pan, Transpose, Detune, Attack, Decay, Sustain, Release,
        FilterType, Cutoff, Resonance, VoiceMode, FilterKeyTrack, VelAmount, Glide, PitchTrack,
        EnvCutoff, EnvAmount, Output, Polyphony, PerKey, OldFade, RelOn, RelVol, RelLen, VelCurve,
        Pedal, BendRange, MpePressure, MpeSlide, View, kNumParams
    };
    static constexpr int kPool = 160;   // voices incl. those fading out after a steal

    Mosaic() {
        pn_[Volume].store(1.0f); pn_[Pan].store(0.5f); pn_[Transpose].store(0.5f); pn_[Detune].store(0.5f);
        pn_[Attack].store(0.0f); pn_[Decay].store(0.3f); pn_[Sustain].store(1.0f); pn_[Release].store(0.3f);
        pn_[FilterType].store(0.0f); pn_[Cutoff].store(1.0f); pn_[Resonance].store(0.0f);
        pn_[VoiceMode].store(0.0f); pn_[FilterKeyTrack].store(0.0f); pn_[VelAmount].store(0.7f);
        pn_[Glide].store(0.0f); pn_[PitchTrack].store(1.0f); pn_[EnvCutoff].store(0.0f); pn_[EnvAmount].store(0.75f);
        pn_[Output].store(0.79f);
        pn_[Polyphony].store(2.0f / 3.0f);                  // 64
        pn_[PerKey].store(1.0f / 3.0f);                     // 2
        pn_[OldFade].store(oldFadeNorm(0.020));             // 20 ms
        pn_[RelOn].store(1.0f);
        pn_[RelVol].store(static_cast<float>((-9.0 + 36.0) / 42.0));   // −9 dB on −36 … +6
        pn_[RelLen].store(0.25f);                           // −6 dB after 2 s on 0 … −24
        pn_[VelCurve].store(0.5f);                          // linear
        pn_[Pedal].store(1.0f);
        pn_[BendRange].store(2.0f / 24.0f);
        pn_[MpePressure].store(0.5f); pn_[MpeSlide].store(0.5f);
        pn_[View].store(0.0f);                              // L
        for (auto& v : voices_) v.active = false;
        for (int k = 0; k < 128; ++k) { noteOnAt_[k] = 0; noteVel_[k] = 0.0f; }
    }

    ~Mosaic() override {
        std::lock_guard<std::mutex> lk(holdMx_);
        if (pending_) pending_->cancel.store(true);
    }

    // ---- value maps (mirrored by MosaicModel.cs) -----------------------------------------
    static double glideSeconds(float v) { return v <= 0.001f ? 0.0 : 0.001 * std::pow(2000.0, std::clamp(v, 0.0f, 1.0f)); }
    static double envOctaves(float v)   { return (std::clamp(v, 0.0f, 1.0f) - 0.5) * 12.0; }
    static int    polyOf(float v)       { static const int n[] = { 16, 32, 64, 128 }; return n[std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3)]; }
    static int    perKeyOf(float v)     { return 1 + std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3); }
    static double oldFadeSec(float v)   { return 0.001 * std::pow(200.0, std::clamp(v, 0.0f, 1.0f)); }      // 1 … 200 ms
    static float  oldFadeNorm(double s) { return static_cast<float>(std::log(std::max(0.001, s) / 0.001) / std::log(200.0)); }
    static double relVolDb(float v)     { return -36.0 + 42.0 * std::clamp(v, 0.0f, 1.0f); }                // −36 … +6 dB
    static double relLenDb(float v)     { return -24.0 * std::clamp(v, 0.0f, 1.0f); }                       // drop after 2 s
    static double bendSemis(float v)    { return std::round(24.0 * std::clamp(v, 0.0f, 1.0f)); }
    // Velocity curve: −1 soft (quiet notes louder) … +1 hard. v in 0..1.
    static double velCurve(double vel, float c) {
        const double k = (std::clamp(c, 0.0f, 1.0f) - 0.5) * 2.0;   // −1..+1
        const double e = std::pow(4.0, k);                          // 0.25 … 4
        return std::pow(std::clamp(vel, 0.0, 1.0), e);
    }

    int32_t kind() const override { return 17; }
    const char* displayName() const override { return "Nota Mosaic"; }
    void setSampleRate(double sr) override { sr_ = sr > 0 ? sr : 44100.0; }
    bool supportsMpe() const override { return true; }

    // ---- the program (message thread) ---------------------------------------------------
    // Parses and starts loading `text`. A program whose files are all loaded already (a zone
    // edit, a clone) publishes at once; otherwise the instrument is silent until it is in.
    void setProgram(const std::string& text) {
        auto p = mosaic::parseProgram(text);
        // Reuse what this instrument already holds (same resolved path) before the cache.
        std::shared_ptr<mosaic::Program> cur;
        { std::lock_guard<std::mutex> lk(holdMx_); cur = pending_; }
        if (cur) {
            std::map<std::string, std::shared_ptr<SampleBuffer>> have;
            { std::lock_guard<std::mutex> lk(cur->ownMx); for (size_t f = 0; f < cur->paths.size(); ++f) if (cur->owned[f]) have[cur->paths[f]] = cur->owned[f]; }
            for (size_t f = 0; f < p->paths.size(); ++f) {
                auto it = have.find(p->paths[f]);
                if (it != have.end()) { mosaic::installSample(*p, f, it->second); p->done.fetch_add(1); p->bytesDone.fetch_add(p->diskBytes[f]); }
            }
            if (!cur->ready.load()) cur->cancel.store(true);   // stop decoding what nobody plays
        }
        publish(p);
        mosaic::loadProgram(p);
        reap();
    }

    // The program text (the newest one set, loaded or not).
    std::string programText() const {
        std::lock_guard<std::mutex> lk(holdMx_);
        return pending_ ? pending_->text : std::string{};
    }

    // Loading state: 0 empty · 1 loading · 2 ready. Polled by the editor; also frees retired
    // programs (message thread).
    struct Status { int32_t state = 0, filesDone = 0, filesTotal = 0, missing = 0, zones = 0, serial = 0; int64_t diskDone = 0, diskTotal = 0, ram = 0; };
    Status status() {
        std::shared_ptr<mosaic::Program> p;
        { std::lock_guard<std::mutex> lk(holdMx_); p = pending_; }
        Status s;
        if (!p) { reap(); return s; }
        s.state = p->paths.empty() ? 0 : (p->ready.load(std::memory_order_acquire) ? 2 : 1);
        s.filesDone = p->done.load(); s.filesTotal = static_cast<int32_t>(p->paths.size());
        s.missing = p->missing.load(); s.zones = static_cast<int32_t>(p->zones.size()); s.serial = p->serial;
        s.diskDone = p->bytesDone.load(); s.diskTotal = p->diskTotal; s.ram = p->ramBytes.load();
        reap();
        return s;
    }

    // The decoded buffer behind a zone of the newest program (the editor's waveform), or null.
    std::shared_ptr<SampleBuffer> zoneSample(int32_t zone) const {
        std::lock_guard<std::mutex> lk(holdMx_);
        if (!pending_ || zone < 0 || zone >= static_cast<int32_t>(pending_->zones.size())) return nullptr;
        const int32_t f = pending_->zones[zone].file;
        std::lock_guard<std::mutex> lk2(pending_->ownMx);
        return (f >= 0 && f < static_cast<int32_t>(pending_->owned.size())) ? pending_->owned[f] : nullptr;
    }
    // Every decoded buffer the program holds (project save looks samples up by id).
    std::vector<std::shared_ptr<SampleBuffer>> samples() const {
        std::lock_guard<std::mutex> lk(holdMx_);
        std::vector<std::shared_ptr<SampleBuffer>> out;
        if (!pending_) return out;
        std::lock_guard<std::mutex> lk2(pending_->ownMx);
        for (auto& b : pending_->owned) if (b) out.push_back(b);
        return out;
    }

    // ---- state: params + program text ----------------------------------------------------
    std::vector<uint8_t> getState() const override {
        const std::string text = programText();
        std::vector<uint8_t> out;
        auto put32 = [&](uint32_t v) { for (int i = 0; i < 4; ++i) out.push_back(static_cast<uint8_t>(v >> (8 * i))); };
        put32(0x43534F4Du);   // "MOSC"
        put32(1);
        put32(kNumParams);
        for (int i = 0; i < kNumParams; ++i) { float f = pn_[i].load(std::memory_order_relaxed); uint32_t u; std::memcpy(&u, &f, 4); put32(u); }
        put32(static_cast<uint32_t>(text.size()));
        out.insert(out.end(), text.begin(), text.end());
        return out;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size < 12) return;
        size_t at = 0;
        auto get32 = [&](uint32_t& v) { if (at + 4 > static_cast<size_t>(size)) return false; v = 0; for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(data[at + i]) << (8 * i); at += 4; return true; };
        uint32_t magic = 0, ver = 0, n = 0;
        if (!get32(magic) || magic != 0x43534F4Du || !get32(ver) || !get32(n)) return;
        for (uint32_t i = 0; i < n; ++i) {
            uint32_t u; if (!get32(u)) return;
            float f; std::memcpy(&f, &u, 4);
            if (i < static_cast<uint32_t>(kNumParams) && std::isfinite(f)) pn_[i].store(std::clamp(f, 0.0f, 1.0f), std::memory_order_relaxed);
        }
        uint32_t len = 0;
        if (!get32(len) || at + len > static_cast<size_t>(size)) return;
        setProgram(std::string(reinterpret_cast<const char*>(data + at), len));
    }

    std::shared_ptr<Instrument> clone() const override {
        auto m = std::make_shared<Mosaic>();
        m->setSampleRate(sr_);
        for (int i = 0; i < kNumParams; ++i) m->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        std::shared_ptr<mosaic::Program> cur;
        { std::lock_guard<std::mutex> lk(holdMx_); cur = pending_; }
        if (cur) {   // shares the decoded buffers through setProgram's reuse
            m->adopt(cur);
        }
        return m;
    }

    // ---- plugin params ----------------------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[kNumParams] = {
            "volume", "pan", "transpose", "detune", "attack", "decay", "sustain", "release",
            "filtertype", "cutoff", "resonance", "voicemode", "keytrack", "velamount", "glide", "pitchtrack",
            "envcutoff", "envamount", "output", "polyphony", "perkey", "oldfade", "relon", "relvol", "rellen", "velcurve",
            "pedal", "bendrange", "mpepressure", "mpeslide", "view" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        // Grouped by the head word in the automation menu; ids persist, names may change.
        static const char* nm[kNumParams] = {
            "Out Volume", "Out Pan", "Pitch Transpose", "Pitch Detune", "Env Attack", "Env Decay", "Env Sustain", "Env Release",
            "Filter Type", "Filter Cutoff", "Filter Resonance", "Voice Mode", "Filter Keytrack", "Voice Velocity", "Voice Glide", "Pitch Keytrack",
            "Filter Env On", "Filter Env Amount", "Out Level", "Voice Polyphony", "Voice Per Key", "Voice Old Fade",
            "Release Triggers", "Release Volume", "Release Length", "Voice Vel Curve",
            "Voice Pedal", "MPE Bend Range", "MPE Pressure", "MPE Slide", "View" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override { return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  pluginParamSet(int32_t i, float v) override { if (i >= 0 && i < kNumParams && std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    float pluginParamDefault(int32_t i) const override { static const Mosaic d; return d.pluginParamGet(i); }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- telemetry --------------------------------------------------------------------------
    // [0] voices · [1] voice limit · [2] held · [3] held by the pedal · [4] release-trigger
    // voices · [5] pedal down · [6] output peak since the last read · [7] loudest voice's play
    // position 0..1 · [8] its envelope · [9] its stage (0 A · 1 D · 2 S · 3 R) · [10] its note
    // (fractional while gliding) · [11] its cutoff Hz (−1 off) · [12] its zone · [13] last
    // note · [14] last velocity 0..127 · [15] sounding entries N, then N entries
    // zone × 4 + state (0 held · 1 pedal · 2 release / fading); then [128 …] the keys held or
    // sustained now, 128 bits as 8 floats of 16 bits each.
    static constexpr int kScHead = 16, kScZones = 112, kScKeys = kScHead + kScZones, kScope = kScKeys + 8;
    int32_t scopeRead(float* out, int32_t maxN) const override {
        if (!out || maxN <= 0) return 0;
        const int n = std::min<int>(maxN, kScope);
        for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
        if (n > 6) tele_[6].store(0.0f, std::memory_order_relaxed);
        return n;
    }
    int32_t activeVoiceCount() const override { return static_cast<int32_t>(tele_[0].load(std::memory_order_relaxed)); }

    // ---- notes --------------------------------------------------------------------------------
    void noteExpression(int32_t pitch, int32_t dim, float value) override {
        if (pitch < 0) {
            if (dim == ExprSustain) cc64_ = value >= 0.5f;
            else gexpr_.set(dim, value);
            return;
        }
        if (dim < 0 || dim >= kExprDims) return;
        float add[kExprDims]; gexpr_.offsets(static_cast<float>(bendSemis(get(BendRange))), add);
        for (auto& v : voices_) if (v.active && v.pitch == pitch && v.held) v.ex.set(dim, value, add);
    }

    void noteOn(int32_t pitch, float velocity) override {
        if (pitch < 0 || pitch > 127) return;
        const mosaic::Program* p = live();
        noteOnAt_[pitch] = clock_; noteVel_[pitch] = velocity; keyDown_[pitch] = true;
        lastNote_ = pitch; lastVel_ = velocity;
        if (!p || p->zones.empty()) return;
        const int vm = voiceModeOf();
        const bool glide = get(Glide) > 0.001f;
        const double from = glideFrom_ >= 0 ? static_cast<double>(glideFrom_) : static_cast<double>(pitch);
        glideFrom_ = pitch;
        if (vm == 1) {
            pushHeld(pitch);
            if (glide) {   // legato: sounding voices slide, no new strike
                bool slid = false;
                for (auto& v : voices_) if (v.active && v.held && !v.relTrig && v.kill == 0) { v.pitch = pitch; slid = true; }
                if (slid) return;
            }
            for (auto& v : voices_) if (v.active && !v.relTrig && v.kill == 0) fade(v);
        } else if (vm == 2) {
            for (auto& v : voices_) if (v.active && v.kill == 0) fade(v, 0.004);
        }
        // Per key: older strikes of this key beyond the limit fade out.
        if (vm == 0) limitPerKey(pitch);
        const int vel7 = std::clamp(static_cast<int>(std::lround(velocity * 127.0f)), 0, 127);
        const uint32_t id = ++noteSerial_;
        spawn(*p, pitch, vel7, velocity, id, /*release*/false, 1.0f, glide ? from : static_cast<double>(pitch));
    }

    void noteOff(int32_t pitch) override {
        if (pitch < 0 || pitch > 127) return;
        const int vm = voiceModeOf();
        if (vm == 1) {
            const bool top = heldN_ > 0 && held_[heldN_ - 1] == pitch;
            popHeld(pitch);
            if (top && heldN_ > 0 && get(Glide) > 0.001f) {   // legato back to the key still down
                const int32_t back = held_[heldN_ - 1];
                bool slid = false;
                for (auto& v : voices_) if (v.active && v.held && v.pitch == pitch && v.kill == 0 && !v.relTrig) { v.pitch = back; slid = true; }
                if (slid) { glideFrom_ = back; return; }
            }
        }
        for (auto& v : voices_) {
            if (!v.active || v.pitch != pitch || !v.held || v.relTrig) continue;
            v.held = false;
            if (pedalDown_) v.sustained = true;
            else v.stage = Stage::Release;
        }
        // The release sample follows the key (or the pedal), even when the note has died away.
        if (keyDown_[pitch]) {
            keyDown_[pitch] = false;
            if (pedalDown_) pedalKeys_[pitch] = true;
            else releaseTrigger(pitch);
        }
    }

    void allNotesOff() override {
        for (auto& v : voices_) v.active = false;
        heldN_ = 0; cc64_ = false; pedalDown_ = false; gexpr_.reset();
        for (auto& k : pedalKeys_) k = false;
        for (auto& k : keyDown_) k = false;
    }

    // ---- render -----------------------------------------------------------------------------
    void render(float* out, int32_t frames) override {
        const mosaic::Program* p = live();
        // A new program: voices keep sounding only if it still holds their sample (a zone
        // edit keeps the files) — a retired program's buffers live just two more renders.
        if (p != lastLive_) {
            for (auto& v : voices_) {
                if (!v.active) continue;
                bool kept = false;
                if (p) for (size_t f = 0; f < p->paths.size() && !kept; ++f) kept = p->sample(static_cast<int32_t>(f)) == v.buf;
                if (!kept) v.active = false;
                else if (v.zone >= static_cast<int32_t>(p->zones.size())) v.zone = -1;
            }
            lastLive_ = p;
        }

        // The pedal: CC64 (when Pedal is on). Lifting it releases every held-over key.
        const bool pedal = get(Pedal) >= 0.5f && cc64_;
        if (pedalDown_ && !pedal) {
            for (auto& v : voices_) if (v.active && v.sustained) { v.sustained = false; v.stage = Stage::Release; }
            pedalDown_ = false;
            for (int k = 0; k < 128; ++k) if (pedalKeys_[k]) { pedalKeys_[k] = false; releaseTrigger(k); }
        }
        pedalDown_ = pedal;

        const double pitchOff = (get(Transpose) - 0.5) * 48.0 + (get(Detune) - 0.5) * 1.0;
        const double pTrack = get(PitchTrack);
        const float vol = get(Volume) * get(Output);
        const double pan = get(Pan) * 2.0 - 1.0;
        const float atkRate = static_cast<float>(1.0 / (expMap(get(Attack), 0.0005, 4.0) * sr_));
        const float decRate = static_cast<float>(1.0 / (expMap(get(Decay), 0.002, 6.0) * sr_));
        const float relRate = static_cast<float>(1.0 / (expMap(get(Release), 0.002, 6.0) * sr_));
        const float sustain = get(Sustain);
        const double glideSec = glideSeconds(get(Glide));
        const double glideK = glideSec > 0.0 ? 1.0 - std::exp(-static_cast<double>(kCtl) / (glideSec * sr_)) : 1.0;
        const int fType = std::clamp(static_cast<int>(std::lround(get(FilterType) * 3.0f)), 0, 3);
        const double baseCut = expMap(get(Cutoff), 20.0, 20000.0);
        const double kTrack = get(FilterKeyTrack);
        const double k = 2.0 - 1.9 * get(Resonance);
        const bool envOn = get(EnvCutoff) > 0.5f;
        const double envOct = envOn ? envOctaves(get(EnvAmount)) : 0.0;
        const float bendR = static_cast<float>(bendSemis(get(BendRange)));
        const float mpePress = get(MpePressure), mpeSlide = get(MpeSlide);
        float exAdd[kExprDims]; gexpr_.offsets(bendR, exAdd);
        const float exCoef = exprSmoothCoef(sr_);

        int nActive = 0;
        for (auto& v : voices_) {
            if (!v.active) continue;
            const SampleBuffer* sb = v.buf;
            if (!sb || sb->empty()) { v.active = false; continue; }
            ++nActive;
            const double srcRatio = sb->sourceSampleRate / sr_;
            const double loopLen = v.loopB - v.loopA;
            double inc = 0.0, a1 = 0, a2 = 0, a3 = 0;
            for (int32_t i = 0; i < frames; ++i) {
                v.ex.tick(exAdd, exCoef);
                if ((i % kCtl) == 0) {
                    v.note += (v.pitch - v.note) * glideK;
                    if (std::abs(v.pitch - v.note) < 1e-4) v.note = v.pitch;
                    const double semis = (v.note - v.root) * pTrack + pitchOff + v.tuneSemis + v.ex.bend();
                    inc = std::pow(2.0, semis / 12.0) * srcRatio;
                    if (fType > 0) {
                        double fc = baseCut;
                        if (kTrack > 0.0) fc *= std::pow(2.0, kTrack * (v.note - 60.0) / 12.0);
                        if (envOn) fc *= std::pow(2.0, envOct * v.env);
                        const double slide = (v.ex.cur[ExprSlide] - 0.5) * 2.0;   // −1..+1
                        fc *= std::pow(2.0, slide * (mpeSlide - 0.5) * 8.0);      // ±2 oct at the extremes
                        v.cutoff = std::clamp(fc, 20.0, sr_ * 0.49);
                        const double g = std::tan(kPi * v.cutoff / sr_);
                        a1 = 1.0 / (1.0 + g * (g + k)); a2 = g * a1; a3 = g * a2;
                    }
                }
                if (v.relTrig) {   // a release sample plays through on its own level
                    if (v.env < 1.0f) v.env = std::min(1.0f, v.env + atkRateRel_);
                } else switch (v.stage) {
                    case Stage::Attack:  v.env += atkRate; if (v.env >= 1.0f) { v.env = 1.0f; v.stage = Stage::Decay; } break;
                    case Stage::Decay:   v.env -= decRate; if (v.env <= sustain) { v.env = sustain; v.stage = Stage::Sustain; } break;
                    case Stage::Sustain: break;
                    case Stage::Release: v.env -= relRate; if (v.env <= 0.0f) { v.env = 0.0f; v.active = false; } break;
                }
                if (!v.active) break;
                float kg = 1.0f;
                if (v.kill > 0) { kg = static_cast<float>(v.kill) / static_cast<float>(v.killLen); if (--v.kill == 0) { v.active = false; break; } }

                float ls, rs; readHermite(*sb, v.pos, ls, rs);
                if (v.loopMode == 1 && v.xf > 1.0 && v.dir > 0 && v.pos > v.loopB - v.xf) {
                    const double t = (v.pos - (v.loopB - v.xf)) / v.xf;
                    float hl, hr; readHermite(*sb, v.pos - loopLen, hl, hr);
                    ls = static_cast<float>(ls * (1.0 - t) + hl * t);
                    rs = static_cast<float>(rs * (1.0 - t) + hr * t);
                }
                if (fType > 0) { ls = svf(v.ic1L, v.ic2L, ls, a1, a2, a3, k, fType); rs = svf(v.ic1R, v.ic2R, rs, a1, a2, a3, k, fType); }
                const float press = 1.0f + (v.ex.pressure() * (mpePress * 2.0f));   // up to +3 at full amount
                const float amp = v.env * v.level * vol * kg * press;
                out[i * 2]     += ls * amp * v.gl;
                out[i * 2 + 1] += rs * amp * v.gr;
                v.peak = std::max(v.peak * 0.9995f, std::abs(ls * amp));

                v.pos += inc * v.dir;
                if (v.loopMode == 1 && !v.relTrig) {
                    if (v.pos >= v.loopB) v.pos -= loopLen;
                } else if (v.loopMode == 2 && !v.relTrig) {
                    if (v.pos >= v.loopB) { v.pos = v.loopB - (v.pos - v.loopB); v.dir = -1; }
                    else if (v.pos <= v.loopA && v.dir < 0) { v.pos = v.loopA + (v.loopA - v.pos); v.dir = 1; }
                } else {
                    if (v.pos >= v.endF) { v.active = false; break; }
                    // A one-shot's last 2 ms fade so a hard cut never clicks.
                    const double left = (v.endF - v.pos) / std::max(1e-9, inc);
                    if (left < fadeTail_ && v.kill == 0) { v.kill = std::max(1, static_cast<int>(left)); v.killLen = v.kill; }
                }
            }
        }
        clock_ += frames;
        publishTelemetry(out, frames, nActive, pan);
        epoch_.fetch_add(1, std::memory_order_release);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kCtl = 16;
    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool active = false, held = false, sustained = false, relTrig = false;
        int32_t pitch = 0, root = 60, zone = -1, dir = 1, loopMode = 0;
        uint32_t id = 0;
        uint64_t age = 0;
        const SampleBuffer* buf = nullptr;
        double pos = 0, note = 0, tuneSemis = 0, startF = 0, endF = 0, loopA = 0, loopB = 0, xf = 0, cutoff = 20000;
        float level = 1, gl = 1, gr = 1, env = 0, peak = 0;
        int kill = 0, killLen = 1;
        double ic1L = 0, ic2L = 0, ic1R = 0, ic2R = 0;
        Stage stage = Stage::Attack;
        VoiceExpr ex;
    };

    float get(int p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    int voiceModeOf() const { return std::clamp(static_cast<int>(std::lround(get(VoiceMode) * 2.0f)), 0, 2); }

    // ---- program publication (message thread) -------------------------------------------------
    void adopt(const std::shared_ptr<mosaic::Program>& src) {
        // A clone: same text, same buffers — nothing to decode.
        auto p = mosaic::parseProgram(src->text);
        {
            std::lock_guard<std::mutex> lk(src->ownMx);
            for (size_t f = 0; f < p->paths.size() && f < src->owned.size(); ++f)
                if (src->owned[f]) { mosaic::installSample(*p, f, src->owned[f]); p->done.fetch_add(1); p->bytesDone.fetch_add(p->diskBytes[f]); }
        }
        publish(p);
        mosaic::loadProgram(p);
    }
    // The newest program becomes the one the audio thread reads; it plays once its loaders
    // mark it ready. The one it replaces is kept until two more renders have finished.
    void publish(const std::shared_ptr<mosaic::Program>& p) {
        std::lock_guard<std::mutex> lk(holdMx_);
        if (pending_) retired_.emplace_back(epoch_.load(std::memory_order_acquire), pending_);
        pending_ = p;
        cur_.store(p.get(), std::memory_order_release);
    }
    // The program the audio thread plays now: the newest, once loaded (audio thread).
    const mosaic::Program* live() const {
        const mosaic::Program* p = cur_.load(std::memory_order_acquire);
        return p && p->ready.load(std::memory_order_acquire) ? p : nullptr;
    }
    void reap() {
        std::lock_guard<std::mutex> lk(holdMx_);
        const uint64_t now = epoch_.load(std::memory_order_acquire);
        retired_.erase(std::remove_if(retired_.begin(), retired_.end(), [&](auto& r) { return now >= r.first + 2; }), retired_.end());
    }

    // ---- voices ---------------------------------------------------------------------------------
    void fade(Voice& v, double sec = -1) {
        if (v.kill > 0) return;
        const double s = sec > 0 ? sec : oldFadeSec(get(OldFade));
        v.killLen = std::max(1, static_cast<int>(s * sr_)); v.kill = v.killLen;
        v.held = false; v.sustained = false;
    }
    void limitPerKey(int32_t pitch) {
        const int lim = perKeyOf(get(PerKey));
        // Distinct strikes (note ids) of this key still sounding, oldest first.
        uint32_t ids[kPool]; int n = 0;
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.kill == 0 && !v.relTrig) {
                bool seen = false; for (int i = 0; i < n; ++i) if (ids[i] == v.id) { seen = true; break; }
                if (!seen && n < kPool) ids[n++] = v.id;
            }
        std::sort(ids, ids + n);
        for (int i = 0; i + lim <= n; ++i)   // keep lim − 1 old ones: the new strike is the lim-th
            for (auto& v : voices_) if (v.active && v.id == ids[i]) fade(v);
    }
    int liveCount() const { int c = 0; for (auto& v : voices_) if (v.active && v.kill == 0) ++c; return c; }
    // The voice to steal: released first, then the quietest, then the oldest.
    Voice* victim() {
        Voice* q = nullptr;
        auto rank = [](const Voice& v) { return v.held ? 2 : (v.sustained ? 1 : 0); };
        for (auto& v : voices_) {
            if (!v.active || v.kill > 0) continue;
            if (!q) { q = &v; continue; }
            if (rank(v) != rank(*q)) { if (rank(v) < rank(*q)) q = &v; continue; }
            if (std::abs(v.peak - q->peak) > 1e-4f) { if (v.peak < q->peak) q = &v; continue; }
            if (v.age < q->age) q = &v;
        }
        return q;
    }
    Voice* freeSlot() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];   // everything busy (fades included): the quietest outright
        for (auto& v : voices_) if (v.peak < q->peak) q = &v;
        return q;
    }

    static float xfadeGain(int x, int inLo, int inHi, int outLo, int outHi) {
        double g = 1.0;
        if (inLo >= 0 && inHi > inLo) { if (x <= inLo) return 0.0f; if (x < inHi) g *= std::sin(0.5 * kPi * (x - inLo) / double(inHi - inLo)); }
        if (outLo >= 0 && outHi > outLo) { if (x >= outHi) return 0.0f; if (x > outLo) g *= std::cos(0.5 * kPi * (x - outLo) / double(outHi - outLo)); }
        return static_cast<float>(g);
    }

    // Starts the voices of a note (attack zones) or of a key's release (release zones).
    void spawn(const mosaic::Program& p, int32_t pitch, int vel7, float velocity, uint32_t id, bool release, float relGain, double fromNote) {
        // One random number per event, shared by every group (SFZ lorand / hirand).
        rng_ = rng_ * 1664525u + 1013904223u;
        const float r = static_cast<float>((rng_ >> 8) & 0xFFFFFF) / 16777216.0f;
        // Round-robin: each group with matching zones steps once per event.
        const size_t ng = std::min(p.groups.size(), kGroups);
        bool touched[kGroups] = {};
        int32_t step[kGroups];
        for (size_t g = 0; g < ng; ++g) step[g] = 1;
        for (const auto& z : p.zones) {
            if ((z.trig == 1) != release || pitch < z.klo || pitch > z.khi || vel7 < z.vlo || vel7 > z.vhi) continue;
            const size_t g = static_cast<size_t>(z.group);
            if (g >= kGroups || touched[g]) continue;
            touched[g] = true;
            const auto& gr = p.groups[g];
            if (gr.seqLen <= 1) { step[g] = 1; continue; }
            if (gr.rrMode == 0) { rrStep_[g] = rrStep_[g] % gr.seqLen + 1; step[g] = rrStep_[g]; }
            else {
                rng_ = rng_ * 1664525u + 1013904223u;
                int s = 1 + static_cast<int>((rng_ >> 8) % static_cast<uint32_t>(gr.seqLen));
                if (gr.rrMode == 2 && s == rrLast_[g]) s = s % gr.seqLen + 1;
                rrLast_[g] = s; step[g] = s;
            }
        }
        const double velAmt = get(VelAmount);
        const double curved = velCurve(velocity, get(VelCurve));
        const float velGain = static_cast<float>(velAmt * curved + (1.0 - velAmt));
        const int limit = polyOf(get(Polyphony));
        const double masterPan = get(Pan) * 2.0 - 1.0;
        float add[kExprDims]; gexpr_.offsets(static_cast<float>(bendSemis(get(BendRange))), add);
        for (size_t zi = 0; zi < p.zones.size(); ++zi) {
            const auto& z = p.zones[zi];
            if ((z.trig == 1) != release || pitch < z.klo || pitch > z.khi || vel7 < z.vlo || vel7 > z.vhi) continue;
            const auto& gr = p.groups[static_cast<size_t>(z.group)];
            if (gr.seqLen > 1 && static_cast<size_t>(z.group) < kGroups && z.seq != step[z.group]) continue;
            if (z.rlo > 0.0f || z.rhi < 1.0f) { if (r < z.rlo || r >= z.rhi) continue; }
            const SampleBuffer* sb = p.sample(z.file);
            if (!sb || sb->empty()) continue;
            const float xg = xfadeGain(pitch, z.xkil, z.xkih, z.xkol, z.xkoh) * xfadeGain(vel7, z.xvil, z.xvih, z.xvol, z.xvoh);
            if (xg <= 0.0f) continue;
            // Exclusive groups: this zone chokes every voice whose off_by names its group.
            if (z.excl != 0)
                for (auto& v : voices_)
                    if (v.active && v.zone >= 0 && v.zone < static_cast<int32_t>(p.zones.size()) && p.zones[v.zone].offBy == z.excl)
                        { if (p.zones[v.zone].offMode == 1) { v.held = false; v.sustained = false; v.stage = Stage::Release; } else fade(v, 0.006); }
            // Make room within the polyphony.
            while (liveCount() >= limit) { Voice* q = victim(); if (!q) break; fade(*q); }
            Voice& v = *freeSlot();
            const double N = static_cast<double>(sb->frames);
            v = Voice{};
            v.active = true; v.held = !release; v.relTrig = release;
            v.pitch = pitch; v.root = z.root; v.zone = static_cast<int32_t>(zi); v.id = id; v.age = ++ageSerial_;
            v.buf = sb;
            v.startF = std::clamp(z.start, 0.0, std::max(0.0, N - 1));
            v.endF = z.end > 0 ? std::clamp(z.end, v.startF + 1, N) : N;
            v.loopMode = z.loopMode;
            v.loopA = z.loopStart >= 0 ? std::clamp(z.loopStart, v.startF, v.endF - 1) : v.startF;
            v.loopB = z.loopEnd > 0 ? std::clamp(z.loopEnd, v.loopA + 1, v.endF) : v.endF;
            v.xf = std::clamp(static_cast<double>(z.xfade) * sb->sourceSampleRate, 0.0, (v.loopB - v.loopA) * 0.5);
            v.pos = v.startF; v.dir = 1;
            v.tuneSemis = (z.tune + gr.tune) / 100.0;
            v.note = release ? static_cast<double>(pitch) : fromNote;
            const double zg = std::pow(10.0, (z.gainDb + gr.gainDb) / 20.0);
            v.level = static_cast<float>(zg) * xg * velGain * relGain;
            const double pn = std::clamp(masterPan + z.pan, -1.0, 1.0);
            v.gl = static_cast<float>(std::cos((pn + 1.0) * kPi / 4.0));
            v.gr = static_cast<float>(std::sin((pn + 1.0) * kPi / 4.0));
            v.env = release ? 1.0f : 0.0f;   // a release sample starts at its own level
            v.stage = Stage::Attack;
            v.ex.reset(add);
            v.peak = v.level;
        }
    }

    void releaseTrigger(int32_t pitch) {
        if (get(RelOn) < 0.5f) return;
        const mosaic::Program* p = live();
        if (!p) return;
        bool has = false;
        for (const auto& z : p->zones) if (z.trig == 1 && pitch >= z.klo && pitch <= z.khi) { has = true; break; }
        if (!has) return;
        const double heldSec = static_cast<double>(clock_ - noteOnAt_[pitch]) / sr_;
        const double db = relVolDb(get(RelVol)) + relLenDb(get(RelLen)) * std::clamp(heldSec / 2.0, 0.0, 1.0);
        const float g = static_cast<float>(std::pow(10.0, db / 20.0));
        const float vel = noteVel_[pitch];
        const int vel7 = std::clamp(static_cast<int>(std::lround(vel * 127.0f)), 0, 127);
        spawn(*p, pitch, vel7, vel, ++noteSerial_, true, g, pitch);
    }

    // 4-point Hermite read of a stereo frame at a fractional position.
    static void readHermite(const SampleBuffer& b, double pos, float& l, float& r) {
        const int64_t i1 = static_cast<int64_t>(std::floor(pos));
        const float t = static_cast<float>(pos - static_cast<double>(i1));
        float l0, r0, l1, r1, l2, r2, l3, r3;
        b.readStereo(i1 - 1, l0, r0); b.readStereo(i1, l1, r1); b.readStereo(i1 + 1, l2, r2); b.readStereo(i1 + 2, l3, r3);
        auto h = [t](float y0, float y1, float y2, float y3) {
            const float c1 = 0.5f * (y2 - y0), c2 = y0 - 2.5f * y1 + 2.0f * y2 - 0.5f * y3, c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
            return ((c3 * t + c2) * t + c1) * t + y1;
        };
        l = h(l0, l1, l2, l3); r = h(r0, r1, r2, r3);
    }
    static float svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type) {
        const double x = xf;
        const double v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = 2.0 * v1 - ic1; ic2 = 2.0 * v2 - ic2;
        if (type == 1) return static_cast<float>(v2);
        if (type == 2) return static_cast<float>(x - k * v1 - v2);
        return static_cast<float>(v1);
    }

    void publishTelemetry(const float* out, int32_t frames, int nActive, double /*pan*/) {
        float peak = 0.0f;
        for (int32_t i = 0; i < frames * 2; ++i) peak = std::max(peak, std::abs(out[i]));
        int held = 0, sus = 0, rel = 0;
        const Voice* best = nullptr;
        int n = 0;
        uint32_t keys[8] = {};
        for (auto& v : voices_) {
            if (!v.active) continue;
            if (v.relTrig) ++rel; else if (v.held) ++held; else if (v.sustained) ++sus;
            if ((v.held || v.sustained) && v.kill == 0 && v.pitch >= 0 && v.pitch < 128) keys[v.pitch >> 4] |= 1u << (v.pitch & 15);
            if (!best || v.peak * v.env > best->peak * best->env) best = &v;
            if (n < kScZones) {
                const int st = v.relTrig || v.kill > 0 || (!v.held && !v.sustained) ? 2 : (v.held ? 0 : 1);
                const float code = static_cast<float>(v.zone * 4 + st);
                bool dup = false;
                for (int i = 0; i < n; ++i) if (tele_[kScHead + i].load(std::memory_order_relaxed) == code) { dup = true; break; }
                if (!dup) tele_[kScHead + n++].store(code, std::memory_order_relaxed);
            }
        }
        tele_[0].store(static_cast<float>(nActive), std::memory_order_relaxed);
        tele_[1].store(static_cast<float>(polyOf(get(Polyphony))), std::memory_order_relaxed);
        tele_[2].store(static_cast<float>(held), std::memory_order_relaxed);
        tele_[3].store(static_cast<float>(sus), std::memory_order_relaxed);
        tele_[4].store(static_cast<float>(rel), std::memory_order_relaxed);
        tele_[5].store(pedalDown_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        if (peak > tele_[6].load(std::memory_order_relaxed)) tele_[6].store(peak, std::memory_order_relaxed);
        const double bn = best && best->buf ? static_cast<double>(best->buf->frames) : 1.0;
        tele_[7].store(best ? static_cast<float>(best->pos / bn) : -1.0f, std::memory_order_relaxed);
        tele_[8].store(best ? best->env : 0.0f, std::memory_order_relaxed);
        tele_[9].store(best ? static_cast<float>(best->relTrig ? 3 : static_cast<int>(best->stage)) : -1.0f, std::memory_order_relaxed);
        tele_[10].store(best ? static_cast<float>(best->note) : -1.0f, std::memory_order_relaxed);
        const int fType = std::clamp(static_cast<int>(std::lround(get(FilterType) * 3.0f)), 0, 3);
        tele_[11].store(best && fType > 0 ? static_cast<float>(best->cutoff) : -1.0f, std::memory_order_relaxed);
        tele_[12].store(best ? static_cast<float>(best->zone) : -1.0f, std::memory_order_relaxed);
        tele_[13].store(static_cast<float>(lastNote_), std::memory_order_relaxed);
        tele_[14].store(static_cast<float>(std::lround(lastVel_ * 127.0f)), std::memory_order_relaxed);
        tele_[15].store(static_cast<float>(n), std::memory_order_relaxed);
        for (int i = 0; i < 8; ++i) tele_[kScKeys + i].store(static_cast<float>(keys[i]), std::memory_order_relaxed);
    }

    void pushHeld(int32_t p) {
        popHeld(p);
        if (heldN_ == kHeld) { std::memmove(held_, held_ + 1, sizeof(int32_t) * (kHeld - 1)); --heldN_; }
        held_[heldN_++] = p;
    }
    void popHeld(int32_t p) {
        for (int i = 0; i < heldN_; ++i)
            if (held_[i] == p) { std::memmove(held_ + i, held_ + i + 1, sizeof(int32_t) * (heldN_ - i - 1)); --heldN_; return; }
    }

    // Program (see the header): the newest set (pending_) and the one the audio thread plays.
    mutable std::mutex holdMx_;
    std::shared_ptr<mosaic::Program> pending_;
    std::vector<std::pair<uint64_t, std::shared_ptr<mosaic::Program>>> retired_;
    std::atomic<const mosaic::Program*> cur_{nullptr};
    std::atomic<uint64_t> epoch_{0};
    const mosaic::Program* lastLive_ = nullptr;   // audio thread

    // Audio-thread state.
    Voice voices_[kPool];
    static constexpr int kHeld = 16;
    int32_t held_[kHeld] = {};
    int heldN_ = 0;
    int32_t glideFrom_ = -1, lastNote_ = -1;
    float lastVel_ = 0.0f;
    uint32_t noteSerial_ = 0, rng_ = 0x2545F491u;
    uint64_t ageSerial_ = 0, clock_ = 0;
    uint64_t noteOnAt_[128];
    float noteVel_[128];
    bool pedalKeys_[128] = {}, keyDown_[128] = {};
    static constexpr size_t kGroups = 512;   // round-robin state for the first 512 groups
    int32_t rrStep_[kGroups] = {}, rrLast_[kGroups] = {};
    GlobalExpr gexpr_;
    bool cc64_ = false, pedalDown_ = false;
    double sr_ = 44100.0;
    double fadeTail_ = 96.0;            // frames of fade before a one-shot's end
    float atkRateRel_ = 1.0f;
    mutable std::atomic<float> tele_[kScope] = {};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
