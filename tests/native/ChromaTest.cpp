// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Exercise production DSP independently of the host, retaining reproducible listening evidence.
#include "devices/effects/Chroma.h"
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <functional>
#include <limits>
#include <new>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
std::atomic<bool> countAllocations{false};
std::atomic<size_t> allocations{0};
void* allocate(size_t size) {
    if (countAllocations.load(std::memory_order_relaxed)) ++allocations;
    if (void* p = std::malloc(std::max(size, size_t(1)))) return p;
    throw std::bad_alloc();
}
}
void* operator new(size_t n) { return allocate(n); }
void* operator new[](size_t n) { return allocate(n); }
void operator delete(void* p) noexcept { std::free(p); }
void operator delete[](void* p) noexcept { std::free(p); }
void operator delete(void* p, size_t) noexcept { std::free(p); }
void operator delete[](void* p, size_t) noexcept { std::free(p); }
void* operator new(size_t n, std::align_val_t alignment) {
    if (countAllocations.load(std::memory_order_relaxed)) ++allocations;
    void* p = nullptr;
    if (posix_memalign(&p, size_t(alignment), std::max(n, size_t(1))) != 0) throw std::bad_alloc();
    return p;
}
void* operator new[](size_t n, std::align_val_t a) { return ::operator new(n, a); }
void operator delete(void* p, std::align_val_t) noexcept { std::free(p); }
void operator delete[](void* p, std::align_val_t) noexcept { std::free(p); }
void operator delete(void* p, size_t, std::align_val_t) noexcept { std::free(p); }
void operator delete[](void* p, size_t, std::align_val_t) noexcept { std::free(p); }

namespace {
constexpr double tau = 6.2831853071795864769;
using Audio = std::vector<float>;
using Configure = std::function<void(nota::Chroma&)>;
int failures = 0, checks = 0;
void check(bool ok, const std::string& name) {
    ++checks;
    if (!ok) ++failures;
    std::printf("%s %s\n", ok ? "PASS" : "FAIL", name.c_str());
}
Audio tone(double hz, double sr, double seconds, double amplitude = .1) {
    Audio a(2 * size_t(std::lround(sr * seconds)));
    for (size_t i = 0; i < a.size() / 2; ++i) a[2 * i] = a[2 * i + 1] = float(amplitude * std::sin(tau * hz * i / sr));
    return a;
}
Audio render(const Audio& input, double sr, const Configure& configure = {}, const std::vector<int>& blocks = {256}) {
    nota::Chroma fx;
    if (configure) configure(fx);
    fx.setSampleRate(sr, *std::max_element(blocks.begin(), blocks.end()));
    Audio output = input;
    size_t offset = 0, block = 0;
    while (offset < output.size() / 2) {
        int n = int(std::min(size_t(blocks[block++ % blocks.size()]), output.size() / 2 - offset));
        countAllocations.store(true);
        fx.process(output.data() + 2 * offset, n);
        countAllocations.store(false);
        offset += n;
    }
    return output;
}
double energy(const Audio& a, size_t begin, size_t end, int channel = 0) {
    end = std::min(end, a.size() / 2);
    double e = 0;
    for (size_t i = begin; i < end; ++i) e += double(a[2 * i + channel]) * a[2 * i + channel];
    return e / std::max(size_t(1), end - begin);
}
double residual(const Audio& a, double hz, double sr, size_t begin, size_t end) {
    double yc = 0, ys = 0, cc = 0, ss = 0, cs = 0, e = 0;
    for (size_t i = begin; i < end; ++i) {
        double angle = tau * hz * i / sr, c = std::cos(angle), s = std::sin(angle), y = a[2 * i];
        yc += y * c; ys += y * s; cc += c * c; ss += s * s; cs += c * s; e += y * y;
    }
    double det = cc * ss - cs * cs;
    double ac = (yc * ss - ys * cs) / det, as = (ys * cc - yc * cs) / det;
    return std::max(0., e - ac * yc - as * ys) / std::max(e, 1e-30);
}
double nearest(double hz) {
    double midi = 69 + 12 * std::log2(hz / 440.), distance = 1e9;
    int note = 0;
    for (int n = 0; n < 128; ++n) if (0x5ad & (1 << (n % 12))) {
        double d = std::abs(n - midi);
        if (d < distance - 1e-5) { distance = d; note = n; }
    }
    return 440 * std::exp2((note - 69) / 12.);
}
Configure noRes = [](auto& fx) { fx.setParam(nota::Chroma::Resonance, 0); };
void basicTests() {
    for (double sr : {44100., 48000., 88200., 96000.}) {
        const std::string suffix = " at " + std::to_string(int(sr)) + " Hz";
        nota::Chroma fx; fx.setSampleRate(sr, 512);
        int latency = fx.latencySamples();
        check(latency == (sr <= 48000 ? 2048 : 4096), "unchanged latency" + suffix);
        auto input = tone(440, sr, .5);
        for (int mode = 0; mode < 3; ++mode) {
            auto output = render(input, sr, [mode](auto& f) {
                if (mode == 0) f.setParam(nota::Chroma::Mix, 0);
                if (mode == 1) f.setParam(nota::Chroma::Amount, 0);
                if (mode == 2) { f.setParam(nota::Chroma::Scale, 1); for (int p = 9; p < 21; ++p) f.setParam(p, 0); }
            });
            double error = 0;
            for (size_t i = latency; i < output.size() / 2; ++i) error = std::max(error, std::abs(double(output[2 * i]) - input[2 * (i - latency)]));
            check(error < 1e-5, "delayed dry mode " + std::to_string(mode) + suffix);
        }
        for (double hz : {220., 440., 880., 1760.}) {
            auto output = render(tone(hz, sr, 1.5), sr, noRes);
            double r = residual(output, nearest(hz), sr, size_t(sr * .75), size_t(sr * 1.5));
            double gain = 10 * std::log10(energy(output, size_t(sr * .75), size_t(sr * 1.5)) / .005);
            std::printf("boundary %.0f @ %.0f: residual %.2f dB, level %+.2f dB\n", hz, sr, 10 * std::log10(std::max(r, 1e-20)), gain);
            check(r < 1e-4 && std::abs(gain) < 1., "stable lower-note boundary " + std::to_string(int(hz)) + suffix);
        }
        auto keyed = render(tone(440 * std::exp2(-1. / 12), sr, 1), sr, noRes);
        auto source = tone(440 * std::exp2(-1. / 12), sr, 1);
        double error = 0;
        for (size_t i = size_t(sr * .5); i < keyed.size() / 2; ++i) error = std::max(error, std::abs(double(keyed[2 * i]) - source[2 * (i - latency)]));
        check(error < 2e-5, "in-key tone reconstructs unchanged" + suffix);
    }
    for (double hz : {439., 441., 430., 460.}) {
        auto output = render(tone(hz, 48000, 1.5), 48000, noRes);
        check(residual(output, nearest(hz), 48000, 36000, 72000) < 1e-4, "off-boundary tone " + std::to_string(int(hz)));
    }
    auto late = tone(440, 48000, 1.5);
    late.insert(late.begin(), 48000, 0.f);
    auto lateOutput = render(late, 48000, noRes);
    check(residual(lateOutput, nearest(440), 48000, 72000, 96000) < 1e-4,
          "a new boundary tone after silence settles on the lower note");
    auto input = tone(440, 48000, 1);
    for (size_t i = 0; i < input.size() / 2; ++i) input[2 * i + 1] *= -.4f;
    auto stereo = render(input, 48000, noRes, {1, 17, 113, 512, 37});
    double stereoError = 0;
    for (size_t i = 0; i < input.size() / 2; ++i) stereoError = std::max(stereoError, std::abs(double(stereo[2 * i + 1]) + .4 * stereo[2 * i]));
    check(stereoError < 2e-6, "stereo preserves antiphase and amplitude ratio");
    auto irregular = render(input, 48000, noRes, {29, 251, 1024, 7});
    double blockError = 0;
    for (size_t i = 0; i < stereo.size(); ++i) blockError = std::max(blockError, std::abs(double(stereo[i]) - irregular[i]));
    check(blockError < 1e-7, "arbitrary block partition invariance");
    for (size_t i = 0; i < input.size() / 2; ++i) input[2 * i] = 0;
    auto right = render(input, 48000);
    check(energy(right, 0, 48000, 0) == 0 && energy(right, 24000, 48000, 1) > 1e-5, "right-only stereo stays isolated");
    auto open = render(tone(430, 48000, 1), 48000, [](auto& f) { f.setParam(nota::Chroma::Gate, 1); f.setParam(nota::Chroma::GateThreshold, 0); });
    auto closed = render(tone(430, 48000, 1), 48000, [](auto& f) { f.setParam(nota::Chroma::Gate, 1); f.setParam(nota::Chroma::GateThreshold, 1); });
    check(energy(closed, 24000, 48000) < .01 * energy(open, 24000, 48000), "spectral Gate attenuates below-threshold signal");
    auto shaped = render(tone(430, 48000, 1), 48000, [](auto& f) { f.setParam(nota::Chroma::Morph, 1); });
    check(std::all_of(shaped.begin(), shaped.end(), [](float x) { return std::isfinite(x); }), "full Morph remains finite");
    input = tone(55, 48000, 1);
    auto sub = render(input, 48000, noRes);
    double subError = 0;
    for (int i = 24000; i < 48000; ++i) subError = std::max(subError, std::abs(double(sub[2 * i]) - input[2 * (i - 2048)]));
    check(subError < 2e-5, "subbass outside processing band is preserved");
    input.assign(48000 * 2, 0);
    input[200] = std::numeric_limits<float>::quiet_NaN();
    input[201] = std::numeric_limits<float>::infinity();
    auto sanitized = render(input, 48000);
    check(std::all_of(sanitized.begin(), sanitized.end(), [](float x) { return std::isfinite(x); }), "non-finite input is sanitized");
    check(allocations.load() == 0, "no allocations in process");
}
void motionTests() {
    double sr = 48000;
    Audio input(2 * 144000);
    double phase = 0, phase2 = 0;
    for (int i = 0; i < 144000; ++i) {
        double t = i / sr, hz = t < .25 ? 439 : 440 * std::exp2(.035 * std::sin(tau * 5 * (t - .25)) / 12);
        phase += tau * hz / sr;
        input[2 * i] = input[2 * i + 1] = .1f * float(std::sin(phase));
    }
    auto vibrato = render(input, sr, noRes);
    // Vibrato stays well inside the held-note hysteresis; the last second must be tonal.
    double vibratoResidual = residual(vibrato, nearest(440), sr, 96000, 144000);
    std::printf("boundary vibrato: residual %.2f dB\n", 10 * std::log10(vibratoResidual));
    check(vibratoResidual < .02, "small boundary vibrato holds one selected note");
    phase = 0;
    for (int i = 0; i < 144000; ++i) {
        double t = i / sr;
        double hz = 439 + 9 * std::clamp((t - 1.) / .25, 0., 1.);
        phase += tau * hz / sr;
        input[2 * i] = input[2 * i + 1] = .1f * float(std::sin(phase));
    }
    auto glissando = render(input, sr, noRes);
    check(residual(glissando, nearest(439), sr, 24000, 48000) < 1e-4 &&
          residual(glissando, nearest(448), sr, 96000, 144000) < 1e-4,
          "glissando changes the held note once it passes the hysteresis band");
    for (int i = 0; i < 144000; ++i) {
        double t = i / sr, hz = 300 + 400 * t / 3, hz2 = 700 - 400 * t / 3;
        phase += tau * hz / sr; phase2 += tau * hz2 / sr;
        input[2 * i] = .08f * float(std::sin(phase) + .7 * std::sin(phase2));
        input[2 * i + 1] = -.5f * input[2 * i];
    }
    auto crossing = render(input, sr);
    double step = 0, peak = 0, stereo = 0;
    for (size_t i = 1; i < crossing.size() / 2; ++i) {
        step = std::max(step, std::abs(double(crossing[2 * i]) - crossing[2 * (i - 1)]));
        peak = std::max(peak, std::abs(double(crossing[2 * i])));
        stereo = std::max(stereo, std::abs(double(crossing[2 * i + 1]) + .5 * crossing[2 * i]));
    }
    check(std::isfinite(peak) && peak < .8 && step < .2 && stereo < 2e-6, "crossing partials stay finite, bounded and stereo linked");
    nota::Chroma fx; fx.setSampleRate(sr, 256);
    auto automated = tone(430, sr, 2);
    for (int offset = 0; offset < 96000; offset += 256) {
        if (offset % 4096 == 0) {
            fx.setParam(nota::Chroma::Root, float((offset / 4096) % 12) / 11);
            fx.setParam(nota::Chroma::Amount, offset % 8192 ? 0 : 1);
            fx.setParam(nota::Chroma::Morph, offset % 8192 ? 1 : 0);
            fx.setParam(nota::Chroma::Gate, offset % 8192 ? 1 : 0);
        }
        countAllocations = true;
        fx.process(automated.data() + 2 * offset, std::min(256, 96000 - offset));
        countAllocations = false;
    }
    check(std::all_of(automated.begin(), automated.end(), [](float x) { return std::isfinite(x) && std::abs(x) < 2; }), "rapid scale/Amount/Morph/Gate automation remains bounded");
}
void releaseTests() {
    const double sr = 48000, hz = 440 * std::exp2(-1. / 12);
    Audio input = tone(hz, sr, 2, .2);
    input.resize(2 * 48000 * 14, 0);
    auto longTail = render(input, sr, [](auto& f) { f.setParam(nota::Chroma::Resonance, 1); f.setParam(nota::Chroma::Decay, 1); });
    auto shortTail = render(input, sr, [](auto& f) { f.setParam(nota::Chroma::Resonance, 1); f.setParam(nota::Chroma::Decay, 0); });
    double longE = energy(longTail, 120000, 132000), shortE = energy(shortTail, 120000, 132000);
    std::printf("release: long %.2f dBFS, short %.2f dBFS at 0.5–0.75 sec after input\n", 10 * std::log10(std::max(longE, 1e-30)), 10 * std::log10(std::max(shortE, 1e-30)));
    check(longE > 1e-5 && longE > 100 * shortE, "Gate-off long Decay retains a musical release");
    check(energy(longTail, 13 * 48000, 14 * 48000) < 1e-9, "long resonant tail eventually drains below -90 dBFS");
    auto gated = render(input, sr, [](auto& f) {
        f.setParam(nota::Chroma::Resonance, 1);
        f.setParam(nota::Chroma::Decay, 1);
        f.setParam(nota::Chroma::Gate, 1);
    });
    check(energy(gated, 120000, 132000) < longE * .01, "Gate attenuates a long resonance after input ends");
    nota::Chroma fx; fx.setParam(nota::Chroma::Resonance, 1); fx.setParam(nota::Chroma::Decay, 1); fx.setSampleRate(sr, 256);
    Audio output = input;
    bool premature = false;
    size_t firstIdle = 0;
    for (size_t offset = 0; offset < output.size() / 2; offset += 256) {
        int n = int(std::min(size_t(256), output.size() / 2 - offset));
        countAllocations = true; fx.process(output.data() + 2 * offset, n); countAllocations = false;
        if (fx.isIdle()) {
            if (firstIdle == 0) firstIdle = offset;
            if (energy(output, offset, offset + n) > 1e-9) premature = true;
        }
    }
    std::printf("first idle after %.3f s (0 means never)\n", firstIdle / sr);
    check(!premature && fx.isIdle(), "sleep waits for audible tail and eventually becomes idle");
    auto restart = tone(hz, sr, .5);
    for (size_t offset = 0; offset < restart.size() / 2; offset += 256)
        fx.process(restart.data() + 2 * offset, int(std::min(size_t(256), restart.size() / 2 - offset)));
    check(!fx.isIdle() && energy(restart, 12000, 24000) > 1e-4, "new input wakes a drained Chroma");
}
double noteHz(int note) {
    return 440 * std::exp2((note - 69) / 12.);
}
Audio chord(double sr, std::initializer_list<int> notes, double seconds = 1.5) {
    Audio out(2 * size_t(sr * seconds));
    for (int note : notes) {
        const double f = noteHz(note);
        for (int h = 1; h <= 9; ++h) {
            const double partial = f * h * std::sqrt(1 + .00008 * (h * h - 1));
            for (size_t i = 0; i < out.size() / 2; ++i) {
                double value = .045 / h * std::sin(tau * partial * i / sr + .3 * h);
                out[2 * i] += float(value);
                out[2 * i + 1] += float(value * (note % 2 ? .7 : -.4));
            }
        }
    }
    return out;
}
double partialLevel(const Audio& audio, double sr, double hz, double start = .6, double end = 1.4) {
    double re = 0, im = 0;
    const size_t begin = size_t(start * sr), count = size_t(end * sr) - begin;
    for (size_t i = 0; i < count; ++i) {
        double w = .5 - .5 * std::cos(tau * i / count);
        double x = audio[2 * (begin + i)] * w;
        re += x * std::cos(tau * hz * i / sr);
        im += x * std::sin(tau * hz * i / sr);
    }
    return 4 * std::hypot(re, im) / count;
}
void polyphonicTests() {
    Configure poly = [](auto& f) { f.setParam(24, 1); };
    for (double sr : {44100., 48000., 88200., 96000.}) {
        const auto suffix = " at " + std::to_string(int(sr)) + " Hz";
        auto input = chord(sr, {60, 64, 67});
        auto output = render(input, sr, poly);
        double c = partialLevel(output, sr, noteHz(60)), g = partialLevel(output, sr, noteHz(67));
        double e = partialLevel(output, sr, noteHz(64)), eb = partialLevel(output, sr, noteHz(63));
        double eb2 = partialLevel(output, sr, 2 * noteHz(63) * std::sqrt(1 + .00024));
        std::printf("poly C minor %.0f: C %.4f E %.4f Eb %.4f G %.4f Eb2 %.4f\n", sr, c, e, eb, g, eb2);
        check(c > .035 && g > .035 && eb > .032 && e < .009 && eb2 > .010,
            "polyphonic C–E–G becomes C–Eb–G and E's overtone follows" + suffix);
        // Projection at the exact target over 800 ms rejects a mistuned fundamental.
        double best = 0, tunedHz = 0;
        for (int cent = -20; cent <= 20; ++cent) {
            double hz = noteHz(63) * std::exp2(cent / 1200.);
            double level = partialLevel(output, sr, hz);
            if (level > best) { best = level; tunedHz = hz; }
        }
        check(best > .032 && std::abs(1200 * std::log2(tunedHz / noteHz(63))) <= 10,
              "polyphonic target within 10 cents" + suffix);
        input = chord(sr, {60, 63, 67});
        output = render(input, sr, poly);
        const int latency = sr <= 48000 ? 2048 : 4096;
        double error = 0;
        for (size_t i = size_t(.45 * sr); i < output.size() / 2; ++i)
            for (int ch = 0; ch < 2; ++ch)
                error = std::max(error, std::abs(double(output[2 * i + ch] - input[2 * (i - latency) + ch])));
        std::printf("poly in-scale max error %.6f\n", error);
        check(error < 2e-5, "in-scale chord reconstructs with the original latency" + suffix);
    }
    const double sr = 48000;
    auto aMinor = render(chord(sr, {62, 66, 69}), sr, [](auto& f) {
        f.setParam(24, 1);
        f.setParam(nota::Chroma::Root, 9.f / 11.f);
    });
    check(partialLevel(aMinor, sr, noteHz(62)) > .027 && partialLevel(aMinor, sr, noteHz(65)) > .027 &&
          partialLevel(aMinor, sr, noteHz(66)) < .012 && partialLevel(aMinor, sr, noteHz(69)) > .027,
          "A minor keeps D and A while correcting F-sharp to F");
    auto inversion = render(chord(sr, {55, 60, 64, 69}), sr, poly);
    check(partialLevel(inversion, sr, noteHz(55)) > .025 && partialLevel(inversion, sr, noteHz(60)) > .025 &&
          partialLevel(inversion, sr, noteHz(63)) > .025 && partialLevel(inversion, sr, noteHz(68)) > .025,
          "four voices in an inversion keep their order and tune independently");
    // This voicing keeps the adjacent candidate peaks resolvable at the fixed FFT size.
    auto collision = render(chord(sr, {72, 74, 79}), sr, [](auto& f) {
        f.setParam(24, 1);
        f.setParam(nota::Chroma::Scale, 1);
        for (int p = 0; p < 12; ++p) f.setParam(nota::Chroma::NoteC + p, p == 0 || p == 4 || p == 7 ? 1 : 0);
    });
    check(partialLevel(collision, sr, noteHz(72)) > .025 && partialLevel(collision, sr, noteHz(76)) > .025 &&
          partialLevel(collision, sr, noteHz(74)) < .012,
          "matched C stays while D takes the next free scale note E");
    auto close = render(chord(sr, {63, 64, 67}), sr, poly);
    const double closeEb = partialLevel(close, sr, noteHz(63)), closeE = partialLevel(close, sr, noteHz(64));
    std::printf("unresolved adjacent fundamentals: Eb %.4f, E %.4f\n", closeEb, closeE);
    check(closeEb > .02 && closeE > .02, "unresolved adjacent fundamentals retain their original audio");
    auto low = chord(sr, {40});
    auto lowTuned = render(low, sr, poly);
    check(std::equal(lowTuned.begin() + 2 * 24000, lowTuned.end(), low.begin() + 2 * (24000 - 2048),
                     [](float x, float y) { return std::abs(x - y) < 2e-5f; }),
          "an out-of-range E2 does not have its overtones mistaken for separate notes");
    auto input = chord(sr, {60, 64, 67});
    for (size_t i = 0; i < input.size() / 2; ++i) input[2 * i + 1] = -.4f * input[2 * i];
    auto a = render(input, sr, poly), b = render(input, sr, poly, {37, 113, 1024, 1});
    double difference = 0, stereoError = 0;
    for (size_t i = 0; i < a.size() / 2; ++i) {
        difference = std::max(difference, std::abs(double(a[2 * i] - b[2 * i])));
        stereoError = std::max(stereoError, std::abs(double(a[2 * i + 1] + .4 * a[2 * i])));
    }
    check(difference < 1e-7 && stereoError < 2e-6, "polyphonic block partitions and antiphase stereo agree");
    auto automated = input;
    nota::Chroma fx;
    poly(fx);
    fx.setSampleRate(sr, 480);
    for (int offset = 0; offset < int(automated.size() / 2); offset += 480) {
        if (offset == 19200) fx.setParam(nota::Chroma::Scale, 0); // To C major, while the chord sustains.
        if (offset == 48000) fx.setParam(24, 0);                   // Return to Spectral during playback.
        countAllocations = true;
        fx.process(automated.data() + 2 * offset, 480);
        countAllocations = false;
    }
    check(partialLevel(automated, sr, noteHz(63), .2, .37) > .026 &&
          partialLevel(automated, sr, noteHz(64), .65, .93) > .037,
          "changing the scale during a held chord updates its destination");
    check(fx.latencySamples() == 2048 &&
          std::equal(automated.begin() + 2 * 48700, automated.begin() + 2 * 49700,
                     input.begin() + 2 * (48700 - 2048), [](float x, float y) { return std::abs(x - y) < 2e-6f; }),
          "a playing mode switch passes through delay-aligned dry audio");
}
void writeWav(const std::filesystem::path& path, const Audio& audio, double sr) {
    std::ofstream f(path, std::ios::binary);
    auto u16 = [&](uint16_t n) { f.put(char(n)); f.put(char(n >> 8)); };
    auto u32 = [&](uint32_t n) { for (int i = 0; i < 4; ++i) f.put(char(n >> (8 * i))); };
    uint32_t size = uint32_t(audio.size() * 3);
    f.write("RIFF", 4); u32(36 + size); f.write("WAVEfmt ", 8); u32(16); u16(1); u16(2);
    u32(uint32_t(sr)); u32(uint32_t(sr) * 6); u16(6); u16(24); f.write("data", 4); u32(size);
    for (float x : audio) { int32_t n = int32_t(std::lround(std::clamp(double(x), -1., 1.) * 8388607)); for (int i = 0; i < 3; ++i) f.put(char(n >> (8 * i))); }
    if (!f) throw std::runtime_error("Cannot write listening WAV");
}
Audio scene(const std::string& name) {
    Audio input(48000 * 5 * 2);
    double phase = 0;
    for (int i = 0; i < 48000 * 5; ++i) {
        double t = i / 48000., envelope = 0, s = 0;
        if (name == "fm") { envelope = std::exp(-7 * std::fmod(t, .5)); s = .15 * std::sin(tau * 110 * t + 3 * std::sin(tau * 220 * t)); }
        if (name == "growl") { envelope = .6 + .4 * std::sin(tau * 1.7 * t); s = .12 * std::tanh(3 * (std::sin(tau * 97 * t + 4 * std::sin(tau * 1.1 * t)) + .4 * std::sin(tau * 291 * t))); }
        if (name == "chord") { envelope = .5 - .5 * std::cos(tau * std::min(t, 4.) / 4); for (double hz : {261.625565, 329.627557, 391.995436}) s += .05 * std::sin(tau * hz * t); }
        if (name == "attack") { double u = std::fmod(t, .6); envelope = std::exp(-24 * u); s = .12 * (std::sin(tau * 430 * t) + .35 * std::sin(tau * 1337 * t) + .2 * std::sin(tau * 4283 * t)); }
        if (name == "decay") { envelope = t < 1. ? 1 : 0; s = .12 * std::sin(tau * 415.3046976 * t); }
        if (name == "boundary") { phase += tau * 440 * std::exp2(.03 * std::sin(tau * 5 * t) / 12) / 48000; envelope = 1; s = .12 * std::sin(phase); }
        if (t > 4.5) envelope *= std::max(0., (5 - t) * 2);
        input[2 * i] = input[2 * i + 1] = float(envelope * s);
    }
    return input;
}
Audio readRecording(const std::filesystem::path& path) {
    std::ifstream f(path, std::ios::binary);
    if (!f) throw std::runtime_error("Cannot read listening fixture: " + path.string());
    Audio input(48000 * 5 * 2, 0);
    f.read(reinterpret_cast<char*>(input.data()), std::streamsize(input.size() * sizeof(float)));
    if (f.bad() || f.gcount() == 0) throw std::runtime_error("Invalid listening fixture: " + path.string());
    float peak = 0;
    for (float v : input) peak = std::max(peak, std::abs(v));
    const float gain = .25f / std::max(.25f, peak);
    for (float& v : input) v *= gain;
    return input;
}
void attackTest() {
    auto input = scene("attack");
    auto wet = render(input, 48000, noRes);
    double error = 0, reference = 0;
    for (int onset : {28800, 57600, 86400}) {
        for (int i = onset; i < onset + 240; ++i) {
            double dry = input[2 * i], difference = dry - wet[2 * (i + 2048)];
            error += difference * difference;
            reference += dry * dry;
        }
    }
    std::printf("attack: first 5 ms residual %.2f dB relative to input\n", 10 * std::log10(error / reference));
    check(error < .65 * reference, "short attacks retain the shape of the unprocessed onset");
}
void listening(const std::filesystem::path& out, int recordingCount, char** recordings, bool polyphonic) {
    std::ofstream csv(out / "metrics.csv");
    csv << "scene,median_ms,p99_block_us,max_block_us,rms_match_gain,raw_peak,tail_rms_dbfs\n";
    std::vector<std::string> scenes = {"fm", "growl", "chord", "attack", "decay", "boundary"};
    if (polyphonic) scenes = {"chord"};
    const size_t generated = scenes.size();
    for (int i = 0; i < recordingCount; ++i) scenes.push_back("recording-" + std::to_string(i + 1));
    for (size_t index = 0; index < scenes.size(); ++index) {
        const auto& name = scenes[index];
        Audio input = index < generated ? scene(name) : readRecording(recordings[index - generated]);
        if (polyphonic && name == "chord") {
            input = chord(48000, {60, 64, 67}, 5);
            for (size_t i = 0; i < input.size() / 2; ++i) {
                const double time = i / 48000.;
                const float envelope = float(std::exp(-time * .8) * std::clamp((5 - time) * 2, 0., 1.));
                input[2 * i] *= envelope;
                input[2 * i + 1] *= envelope;
            }
        }
        Audio wet;
        std::vector<double> times;
        std::vector<double> blockTimes;
        blockTimes.reserve(5 * 938);
        for (int run = 0; run < 5; ++run) {
            nota::Chroma fx;
            if (polyphonic) fx.setParam(24, 1);
            if (name == "decay") { fx.setParam(nota::Chroma::Decay, 1); fx.setParam(nota::Chroma::Resonance, .7f); }
            fx.setSampleRate(48000, 256); wet = input;
            auto start = std::chrono::steady_clock::now();
            for (int i = 0; i < 240000; i += 256) {
                auto before = std::chrono::steady_clock::now();
                fx.process(wet.data() + 2 * i, std::min(256, 240000 - i));
                blockTimes.push_back(std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - before).count());
            }
            times.push_back(std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count());
        }
        std::sort(times.begin(), times.end());
        std::sort(blockTimes.begin(), blockTimes.end());
        const double p99 = blockTimes[blockTimes.size() * 99 / 100], maxBlock = blockTimes.back();
        Audio aligned(wet.size(), 0);
        std::copy(wet.begin() + 4096, wet.end(), aligned.begin());
        writeWav(out / (name + "-raw-wet.wav"), wet, 48000);
        const double dryEnergy = energy(input, 0, 240000) + energy(input, 0, 240000, 1);
        const double wetEnergy = energy(aligned, 0, 240000) + energy(aligned, 0, 240000, 1);
        double gain = std::sqrt(dryEnergy / std::max(1e-30, wetEnergy)), peak = 0;
        for (float x : wet) peak = std::max(peak, std::abs(double(x)));
        double pairPeak = 0;
        for (size_t i = 0; i < input.size(); ++i) pairPeak = std::max({pairPeak, std::abs(double(input[i])), std::abs(aligned[i] * gain)});
        double attenuation = std::min(1., .95 / std::max(1e-30, pairPeak));
        for (size_t i = 0; i < aligned.size(); ++i) { aligned[i] *= float(gain * attenuation); input[i] *= float(attenuation); }
        writeWav(out / (name + "-dry.wav"), input, 48000);
        writeWav(out / (name + "-wet.wav"), aligned, 48000);
        if (polyphonic) {
            Audio pair = input;
            pair.resize(pair.size() + 48000, 0);
            pair.insert(pair.end(), aligned.begin(), aligned.end());
            writeWav(out / (name + "-ab.wav"), pair, 48000);
        }
        double tail = 10 * std::log10(std::max(1e-30, energy(wet, 216000, 240000)));
        csv << name << ',' << times[2] << ',' << p99 << ',' << maxBlock << ',' << gain << ',' << peak << ',' << tail << '\n';
        std::printf("%s: median %.2f ms / 5 sec, p99 %.1f us, max %.1f us, level match %.3f\n",
            name.c_str(), times[2], p99, maxBlock, gain);
    }
}
}
int main(int argc, char** argv) {
    if (argc < 3) return 2;
    bool listenOnly = std::string(argv[2]) == "--listen-only";
    if (!listenOnly) {
        basicTests();
        motionTests();
        releaseTests();
        attackTest();
        polyphonicTests();
        check(allocations.load() == 0, "all guarded renders allocate nothing");
    }
    const bool polyphonic = argc > 3 && std::string(argv[3]) == "--polyphonic";
    const int firstRecording = polyphonic ? 4 : 3;
    listening(argv[1], argc - firstRecording, argv + firstRecording, polyphonic);
    std::printf("%d checks, %d failures\n", checks, failures);
    return failures ? 1 : 0;
}
