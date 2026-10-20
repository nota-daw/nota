// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MosaicProgram implementation. Mosaic.h: the text grammar, path resolution, sample cache and async loader stay
// exact — no FastMath migration (parsing and publishing are bit-critical).

#include "MosaicProgram.h"

namespace nota {
namespace mosaic {

std::shared_ptr<Program> parseProgram(const std::string& text) {
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
        if (tok.empty()) {
            if (j >= text.size()) break;
            continue;
        }
        auto kv = [&](const char* key, double def) -> double {
            const size_t n = std::strlen(key);
            for (size_t t = 1; t < tok.size(); ++t)
                if (tok[t].size() > n && tok[t].compare(0, n, key) == 0 && tok[t][n] == '=')
                    return num(tok[t].c_str() + n + 1, def);
            return def;
        };
        const std::string& rec = tok[0];
        if (rec == "file" && tok.size() >= 2) {
            p->refs.push_back(unescape(tok[1]));
        } else if (rec == "group") {
            Group g;
            g.gainDb = static_cast<float>(kv("gain", 0));
            g.tune = static_cast<float>(kv("tune", 0));
            g.rrMode = std::clamp(static_cast<int32_t>(kv("rr", 0)), 0, 2);
            g.seqLen = std::clamp(static_cast<int32_t>(kv("seq", 1)), 1, 64);
            p->groups.push_back(g);
        } else if (rec == "zone") {
            Zone z;
            z.file = static_cast<int32_t>(kv("f", -1));
            z.group = static_cast<int32_t>(kv("g", 0));
            z.root = std::clamp(static_cast<int32_t>(kv("root", 60)), 0, 127);
            z.klo = std::clamp(static_cast<int32_t>(kv("klo", 0)), 0, 127);
            z.khi = std::clamp(static_cast<int32_t>(kv("khi", 127)), 0, 127);
            z.vlo = std::clamp(static_cast<int32_t>(kv("vlo", 0)), 0, 127);
            z.vhi = std::clamp(static_cast<int32_t>(kv("vhi", 127)), 0, 127);
            z.tune = static_cast<float>(kv("tune", 0));
            z.gainDb = static_cast<float>(kv("gain", 0));
            z.pan = std::clamp(static_cast<float>(kv("pan", 0)), -1.0f, 1.0f);
            z.start = std::max(0.0, kv("s", 0));
            z.end = kv("e", -1);
            z.loopMode = std::clamp(static_cast<int32_t>(kv("lm", 0)), 0, 2);
            z.loopStart = kv("ls", -1);
            z.loopEnd = kv("le", -1);
            z.xfade = static_cast<float>(std::clamp(kv("xf", 0), 0.0, 2.0));
            z.seq = std::max(1, static_cast<int32_t>(kv("seq", 1)));
            z.rlo = static_cast<float>(kv("rlo", 0));
            z.rhi = static_cast<float>(kv("rhi", 1));
            z.xkil = static_cast<int32_t>(kv("xkil", -1));
            z.xkih = static_cast<int32_t>(kv("xkih", -1));
            z.xkol = static_cast<int32_t>(kv("xkol", -1));
            z.xkoh = static_cast<int32_t>(kv("xkoh", -1));
            z.xvil = static_cast<int32_t>(kv("xvil", -1));
            z.xvih = static_cast<int32_t>(kv("xvih", -1));
            z.xvol = static_cast<int32_t>(kv("xvol", -1));
            z.xvoh = static_cast<int32_t>(kv("xvoh", -1));
            z.trig = kv("trig", 0) >= 0.5 ? 1 : 0;
            z.excl = static_cast<int32_t>(kv("excl", 0));
            z.offBy = static_cast<int32_t>(kv("off", 0));
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
    p->zones.erase(
        std::remove_if(p->zones.begin(), p->zones.end(), [&](const Zone& z) { return z.file < 0 || z.file >= nf; }),
        p->zones.end());
    for (auto& z : p->zones) z.group = std::clamp(z.group, 0, ng - 1);
    for (const auto& r : p->refs) p->paths.push_back(resolve(r));
    p->owned.resize(p->paths.size());
    p->ptr.reset(new std::atomic<SampleBuffer*>[std::max<size_t>(1, p->paths.size())]);
    for (size_t f = 0; f < p->paths.size(); ++f) p->ptr[f].store(nullptr, std::memory_order_relaxed);
    p->diskBytes.assign(p->paths.size(), 0);
    for (size_t f = 0; f < p->paths.size(); ++f) {
        std::error_code ec;
        const auto sz = std::filesystem::file_size(
            std::filesystem::path(std::u8string(p->paths[f].begin(), p->paths[f].end())), ec);
        p->diskBytes[f] = ec ? 0 : static_cast<int64_t>(sz);
        p->diskTotal += p->diskBytes[f];
    }
    return p;
}

void loadProgram(std::shared_ptr<Program> p) {
    const size_t n = p->paths.size();
    std::vector<size_t> todo;
    for (size_t f = 0; f < n; ++f) {
        if (p->ptr[f].load(std::memory_order_relaxed)) continue;
        if (auto c = cached(p->paths[f])) {
            installSample(*p, f, c);
            p->done.fetch_add(1);
            p->bytesDone.fetch_add(p->diskBytes[f]);
            continue;
        }
        todo.push_back(f);
    }
    if (todo.empty()) {
        p->ready.store(true, std::memory_order_release);
        return;
    }
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
                else
                    p->missing.fetch_add(1);
                p->bytesDone.fetch_add(p->diskBytes[f]);
                p->done.fetch_add(1);
            }
            if (left->fetch_sub(1) == 1 && !p->cancel.load(std::memory_order_relaxed))
                p->ready.store(true, std::memory_order_release);
        }).detach();
    }
}

} // namespace mosaic
} // namespace nota
