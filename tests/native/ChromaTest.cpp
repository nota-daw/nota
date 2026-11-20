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
double level(const Audio& audio, double hz, double sr, double from = .65, double to = 1.15, int channel = 0) {
    const size_t begin = size_t(from * sr), end = size_t(to * sr);
    double cosine = 0, sine = 0;
    for (size_t i = begin; i < end; ++i) {
        const double phase = tau * hz * i / sr;
        cosine += audio[2 * i + channel] * std::cos(phase);
        sine += audio[2 * i + channel] * std::sin(phase);
    }
    return 2 * std::hypot(cosine, sine) / (end - begin);
}
double noteHz(int note) { return 440 * std::exp2((note - 69) / 12.); }
using Fx = nota::Chroma;
// Keep the harness buildable against older sources for --listen-only comparisons.
constexpr int retiredMode = 24, muteLow = 25, muteHigh = 26, fftSize = 28;
void mask(Fx& fx, unsigned classes) {
    fx.setParam(Fx::Scale, 1);
    for (int i = 0; i < 12; ++i) fx.setParam(Fx::NoteC + i, (classes >> i) & 1);
}
void onlyC(Fx& fx) {
    mask(fx, 1);
    fx.setParam(Fx::Resonance, 0);
}
bool finite(const Audio& audio, double bound = 64.) {
    return std::all_of(audio.begin(), audio.end(), [=](float x) { return std::isfinite(x) && std::abs(x) <= bound; });
}

void bassSpectrumTests() {
    constexpr int bins = 512;
    using Scope = std::array<float, 2 + 2 * bins>;
    const auto band = [](double hz) { return int(bins * std::log(hz / 40) / std::log(500.)); };
    const auto peak = [&](const float* line, double low, double high) {
        return *std::max_element(line + band(low), line + band(high) + 1);
    };
    for (int sr : {44100, 48000, 96000}) {
        const auto suffix = " at " + std::to_string(sr) + " Hz";
        Scope mono{}, opposed{};
        for (bool antiphase : {false, true}) {
            Fx fx;
            mask(fx, (1 << 8) | (1 << 3)); // Shift A and E down to G# and D#.
            fx.setParam(Fx::Low, 0);
            fx.setParam(Fx::Resonance, 0);
            fx.setSampleRate(sr, 256);
            auto audio = tone(55, sr, 1., .12);
            const auto second = tone(82, sr, 1., .12);
            for (size_t i = 0; i < audio.size(); ++i) {
                audio[i] += second[i];
                if (antiphase && i % 2 != 0) audio[i] = -audio[i];
            }
            for (int i = 0; i < sr; i += 256) {
                countAllocations.store(true);
                fx.process(audio.data() + 2 * i, std::min(256, sr - i));
                countAllocations.store(false);
            }
            auto& scope = antiphase ? opposed : mono;
            const int count = fx.scopeRead(scope.data(), int(scope.size()));
            const float* dry = scope.data() + 2;
            const float* tuned = dry + bins;
            const std::string phase = antiphase ? " (antiphase)" : " (in phase)";
            check(count == int(scope.size()) && scope[0] == sr && scope[1] == 1024 &&
                    std::min(peak(dry, 51, 59), peak(dry, 78, 86)) > peak(dry, 66, 70) + .15f,
                "Dry resolves 55 Hz and 82 Hz with fixed scope layout" + suffix + phase);
            const float* valleyFirst = tuned + band(62);
            const float* valleyLast = tuned + band(69) + 1;
            check(std::min(peak(tuned, 49, 59), peak(tuned, 74, 87)) >
                    *std::min_element(valleyFirst, valleyLast) + .035f,
                "Tuned resolves both shifted bass components" + suffix + phase);
        }
        check(std::equal(mono.begin() + 2, mono.begin() + 2 + band(250), opposed.begin() + 2),
            "Dry bass power is invariant to stereo polarity" + suffix);
    }

    constexpr int sr = 48000;
    constexpr int publication = 1024; // Four 256-sample hops at 48 kHz.
    Fx fx;
    fx.setSampleRate(sr, publication);
    auto audio = tone(82, sr, 3. * publication / sr);
    const auto treble = tone(2000, sr, 3. * publication / sr);
    for (size_t i = 0; i < audio.size(); ++i) audio[i] += treble[i];
    const auto read = [&](int start) {
        Scope scope{};
        fx.process(audio.data() + 2 * start, publication);
        fx.scopeRead(scope.data(), int(scope.size()));
        return scope;
    };
    const auto first = read(0);
    const auto second = read(publication);
    const auto third = read(2 * publication);
    check(first[2 + band(82)] == second[2 + band(82)] && third[2 + band(82)] > second[2 + band(82)] &&
            second[2 + band(2000)] > first[2 + band(2000)],
        "bass refreshes on alternate publications while treble refreshes every time");

    // A strong tone that would fold onto 82 Hz without filtering.
    auto aliasInput = tone(sr / 9. - 82, sr, .45, .85);
    fx.setSampleRate(sr, int(aliasInput.size() / 2));
    fx.process(aliasInput.data(), int(aliasInput.size() / 2));
    Scope scope{};
    fx.scopeRead(scope.data(), int(scope.size()));
    check(peak(scope.data() + 2, 75, 89) < .025f, "bass decimation rejects a tone at the alias frequency");
}

void delayAndRoutingTests() {
    for (double sr : {44100., 48000., 88200., 96000.}) {
        const auto suffix = " at " + std::to_string(int(sr)) + " Hz";
        auto input = tone(440, sr, .10);
        auto delayed = input;
        std::copy_backward(input.begin(), input.end() - 2048, delayed.end());
        std::fill_n(delayed.begin(), 2048, 0.f);
        check(render(input, sr, [](auto& fx) { fx.setParam(Fx::Mix, 0); }) == delayed, "Mix dry is delayed by 1024 samples" + suffix);
        for (unsigned selection : {0u, 4095u})
            check(render(input, sr, [=](auto& fx) { mask(fx, selection); }) == delayed,
                  "empty/full mask is delay-aligned dry" + suffix);
        for (int sourceNote : {61, 71}) {
            const double source = noteHz(sourceNote);
            const double ratio = sourceNote == 61 ? 242. / 256 : 271. / 256;
            auto shifted = render(tone(source, sr, 1.2), sr, onlyC);
            const double tuned = level(shifted, source * ratio, sr), original = level(shifted, source, sr);
            std::printf("%g Hz @ %.0f: HQ target %.5f, source %.5f\n", source, sr, tuned, original);
            check(tuned > .015 && original < tuned * .35,
                  std::string(sourceNote == 61 ? "down" : "up across B/C") + " semitone suppresses the source" + suffix);
        }
    }
    const double sr = 48000, source = noteHz(61);
    auto tie = render(tone(source, sr, 1.2), sr, [](auto& fx) { onlyC(fx); mask(fx, (1 << 0) | (1 << 2)); });
    check(level(tie, source * 242 / 256, sr) > 5 * level(tie, source * 271 / 256, sr), "lower note wins a two-neighbor tie");
    auto noNeighbor = render(tone(noteHz(66), sr, 1.2), sr, onlyC);
    check(energy(noNeighbor, 36000, 54000) < .0002, "notch suppresses a note with no in-scale neighbor");
    auto diatonic = render(tone(noteHz(61), sr, 1.2), sr, [](auto& fx) { onlyC(fx); fx.setParam(Fx::Scale, 0); });
    check(level(diatonic, source * 242 / 256, sr) > .01 && level(diatonic, source * 271 / 256, sr) < .005,
          "major scale routes its sources through the downward branch only");
}

void fftSizeTests() {
    for (int sr : {44100, 48000, 96000}) {
        for (int size : {512, 1024, 2048}) {
            const float choice = size == 512 ? 0.f : size == 1024 ? .5f : 1.f;
            const auto suffix = " (" + std::to_string(size) + " at " + std::to_string(sr) + " Hz)";
            Audio impulse(8192 * 2);
            impulse[0] = impulse[1] = .5f;
            const auto dry = render(impulse, sr, [=](auto& fx) {
                fx.setParam(Fx::Mix, 0);
                fx.setParam(fftSize, choice);
            }, {64, 256});
            Audio expected(impulse.size());
            expected[2 * size] = expected[2 * size + 1] = .5f;
            check(dry == expected, "FFT matches delay in dry path" + suffix);
            const auto wet = render(impulse, sr, [=](auto& fx) {
                onlyC(fx);
                fx.setParam(Fx::Amount, 0); // The wet crossover remains active without the shifter's pre-ringing.
                fx.setParam(fftSize, choice);
            });
            const auto first = std::find_if(wet.begin(), wet.end(), [](float x) { return std::abs(x) > 1e-7f; });
            check(first == wet.begin() + 2 * size, "FFT matches delay in wet path" + suffix);

            constexpr double source = 554.3652619537442, target = source * 242 / 256;
            auto input = tone(source, sr, 1.2);
            for (size_t i = 1; i < input.size(); i += 2) input[i] = -input[i];
            const auto renderMode = [&](float morph, float gate) {
                return render(input, sr, [&](auto& fx) {
                    onlyC(fx);
                    fx.setParam(fftSize, choice);
                    fx.setParam(Fx::Morph, morph);
                    fx.setParam(Fx::Gate, gate);
                    fx.setParam(Fx::GateThreshold, 1);
                });
            };
            const auto shifted = renderMode(0, 0);
            const auto gated = renderMode(0, 1);
            const auto morphed = renderMode(1, 0);
            const double tuned = level(shifted, target, sr);
            check(finite(shifted) && tuned > .008 && level(shifted, source, sr) < tuned,
                "FFT retains semitone correction" + suffix);
            check(level(gated, target, sr) < .1 * tuned && finite(morphed) && morphed != shifted,
                "FFT supports Gate and Morph" + suffix);
            double stereoDifference = 0;
            for (size_t i = 0; i < morphed.size(); i += 2)
                stereoDifference = std::max(stereoDifference, std::abs(double(morphed[i] + morphed[i + 1])));
            check(stereoDifference < 1e-5, "FFT preserves stereo phase" + suffix);
        }
    }

    Fx fx;
    fx.setParam(Fx::Mix, 0);
    auto fill = tone(300, 48000, .15);
    fx.process(fill.data(), int(fill.size() / 2));
    for (const auto& [choice, size] : {std::pair{0.f, 512}, std::pair{1.f, 2048}, std::pair{.5f, 1024}}) {
        fx.setParam(fftSize, choice);
        Audio impulse(8192, 0);
        impulse[0] = impulse[1] = .5f;
        countAllocations.store(true);
        fx.process(impulse.data(), int(impulse.size() / 2));
        countAllocations.store(false);
        std::array<float, 1026> telemetry{};
        const int count = fx.scopeRead(telemetry.data(), int(telemetry.size()));
        check(fx.latencySamples() == size && count == int(telemetry.size()) && telemetry[1] == size &&
                std::count(impulse.begin(), impulse.end(), 0.f) == int(impulse.size()) - 2 && impulse[2 * size] == .5f,
            "FFT change resets old history and reports the configured latency " + std::to_string(size));
    }
}

void resonanceAndBandTests() {
    constexpr double sr = 48000;
    auto input = tone(440, sr, 1.2);
    const auto voice = [](auto& fx, float resonance, float decay) {
        mask(fx, 1 << 9);
        fx.setParam(Fx::Resonance, resonance);
        fx.setParam(Fx::Decay, decay);
    };
    const float defaultDecay = float(std::log(6.) / std::log(50.));
    const auto base = render(input, sr, [&](auto& fx) { voice(fx, 0, defaultDecay); });
    const double baseLevel = level(base, 440, sr);
    for (const auto& [resonance, expected] : {std::pair{.5f, 5.175}, std::pair{1.f, 7.32605}}) {
        const auto resonated = render(input, sr, [&](auto& fx) { voice(fx, resonance, defaultDecay); });
        const double db = 20 * std::log10(level(resonated, 440, sr) / baseLevel);
        std::printf("single-note resonance %g: %+.2f dB relative to no resonance\n", resonance, db);
        check(std::abs(db - expected) < 1.0, "two cascaded peaking sections and density makeup at " + std::to_string(resonance));
    }
    input.resize(2 * 96000);
    std::fill(input.begin() + 48000, input.end(), 0.f);
    auto shortTail = render(input, sr, [&](auto& fx) { voice(fx, 1, 0); });
    auto longTail = render(input, sr, [&](auto& fx) { voice(fx, 1, 1); });
    check(energy(longTail, 33000, 39000) > 100 * energy(shortTail, 33000, 39000),
          "Decay changes ringdown without moving the target note");
    auto shapedTail = render(input, sr, [&](auto& fx) {
        voice(fx, 1, 1);
        fx.setParam(Fx::Gate, 1);
        fx.setParam(Fx::GateThreshold, 1);
    });
    const double tailRatio = energy(shapedTail, 33000, 39000) / energy(longTail, 33000, 39000);
    check(tailRatio > .9 && tailRatio < 1.1, "spectral Gate leaves the separate peaking tail intact");
    for (double hz : {35., 18000.}) {
        auto side = render(tone(hz, sr, 1.2), sr, onlyC);
        check(std::abs(20 * std::log10(level(side, hz, sr) / .1)) < 1.,
              "side band retains level near " + std::to_string(int(hz)) + " Hz");
    }

    Audio impulse(2 * 5000);
    impulse[0] = impulse[1] = .5f;
    auto split = render(impulse, sr, [](auto& fx) { onlyC(fx); fx.setParam(Fx::Amount, 0); });
    const auto first = std::find_if(split.begin(), split.end(), [](float x) { return std::abs(x) > 1e-7f; });
    check(first - split.begin() == 2048, "crossover mid and side bands start at the reported delay");
}

void sidebandMuteTests() {
    constexpr double sr = 48000;
    constexpr std::array frequencies{35., 523.2511306012, 18000.};
    Audio input = tone(frequencies[0], sr, 1.2, .06);
    for (double hz : {frequencies[1], frequencies[2]}) {
        const auto component = tone(hz, sr, 1.2, .06);
        for (size_t i = 0; i < input.size(); ++i) input[i] += component[i];
    }

    for (unsigned notes : {1u, 0u, 4095u}) {
        const auto variant = [&](int mutes, float mix) {
            return render(input, sr, [&](auto& fx) {
                mask(fx, notes);
                fx.setParam(Fx::Amount, 0);
                fx.setParam(muteLow, (mutes & 1) != 0);
                fx.setParam(muteHigh, (mutes & 2) != 0);
                fx.setParam(Fx::Mix, mix);
            }, {1, 64, 511, 1024});
        };
        const auto open = variant(0, 1);
        const auto dry = variant(3, 0);
        const std::string suffix = " (note mask " + std::to_string(notes) + ")";
        check(std::equal(dry.begin() + 2048, dry.end(), input.begin()), "Mix zero keeps exact delayed full-band dry" + suffix);
        for (int mutes : {1, 2, 3}) {
            const auto muted = variant(mutes, 1);
            const double low = level(muted, frequencies[0], sr) / level(open, frequencies[0], sr);
            const double mid = level(muted, frequencies[1], sr) / level(open, frequencies[1], sr);
            const double high = level(muted, frequencies[2], sr) / level(open, frequencies[2], sr);
            const auto bandMatches = [mutes](int bit, double ratio) {
                return (mutes & bit) ? ratio < .1 : std::abs(ratio - 1.) < .05;
            };
            check(bandMatches(1, low) && std::abs(mid - 1.) < .05 && bandMatches(2, high),
                  "sideband selection " + std::to_string(mutes) + " preserves the other bands" + suffix);
        }
        const auto half = variant(3, .5);
        const auto wet = variant(3, 1);
        double error = 0;
        for (size_t i = 0; i < half.size(); ++i)
            error = std::max(error, std::abs(double(half[i]) - .5 * (double(wet[i]) + dry[i])));
        check(error < 1e-6, "Mix at 50% blends the muted wet with full-band dry" + suffix);
    }

    const double source = noteHz(61), target = source * 242 / 256;
    const auto sourceTone = tone(source, sr, 1.2);
    const auto openCorrection = render(sourceTone, sr, onlyC);
    const auto mutedCorrection = render(sourceTone, sr, [](auto& fx) {
        onlyC(fx);
        fx.setParam(muteLow, 1);
        fx.setParam(muteHigh, 1);
    });
    check(std::abs(level(mutedCorrection, target, sr) / level(openCorrection, target, sr) - 1.) < .05,
          "both sideband mutes retain the in-band pitch correction");

    // Change each mute at a fixed stream position while the other one keeps its state.
    const auto automated = [&](const std::vector<int>& blocks) {
        Fx fx;
        mask(fx, 0);
        fx.setSampleRate(sr, 1024);
        auto audio = input;
        int offset = 0, block = 0;
        for (int end : {24000, 33600, 43200, 57600}) {
            while (offset < end) {
                int n = std::min(end - offset, blocks[block++ % blocks.size()]);
                countAllocations = true;
                fx.process(audio.data() + 2 * offset, n);
                countAllocations = false;
                offset += n;
            }
            if (end == 24000) fx.setParam(muteLow, 1);
            if (end == 33600) fx.setParam(muteHigh, 1);
            if (end == 43200) fx.setParam(muteLow, 0);
        }
        return audio;
    };
    const auto regular = automated({256});
    check(regular == automated({1, 64, 511, 1024}), "mute transitions are invariant across block partitions");
    check(level(regular, 35, sr, .57, .67) < .006 && level(regular, 18000, sr, .57, .67) > .05 &&
          level(regular, 18000, sr, .78, .88) < .006 && level(regular, 35, sr, 1., 1.1) > .05,
          "sideband switches can close and reopen individually during playback");

    Fx fx;
    mask(fx, 0);
    fx.setSampleRate(sr, 1024);
    auto unmuted = tone(35, sr, .5);
    auto transition = unmuted;
    fx.process(transition.data(), 12000);
    fx.setParam(muteLow, 1);
    fx.process(transition.data() + 24000, 12000);
    unmuted = render(unmuted, sr, [](auto& device) { mask(device, 0); });
    double removalStep = 0;
    for (size_t i = 24000; i < 25200; i += 2)
        removalStep = std::max(removalStep, std::abs(double(transition[i] - unmuted[i]) - (transition[i - 2] - unmuted[i - 2])));
    check(removalStep < .001, "muting a sustained sideband ramps instead of making a discontinuity");
}

void shapeAndStereoTests() {
    constexpr double sr = 48000, source = 277.1826309768721, target = source * 242 / 256;
    const auto input = tone(source, sr, 1.2);
    auto neutral = render(input, sr, onlyC);
    auto closed = render(input, sr, [](auto& fx) { onlyC(fx); fx.setParam(Fx::Gate, 1); fx.setParam(Fx::GateThreshold, 1); });
    auto open = render(input, sr, [](auto& fx) { onlyC(fx); fx.setParam(Fx::Gate, 1); fx.setParam(Fx::GateThreshold, 0); });
    const double neutralLevel = level(neutral, target, sr);
    check(level(closed, target, sr) < .1 * neutralLevel && level(open, target, sr) > .9 * neutralLevel,
          "spectral Gate attenuates the correction according to the dry-bin threshold");
    auto morph = render(input, sr, [](auto& fx) { onlyC(fx); fx.setParam(Fx::Morph, 1); });
    check(finite(morph) && std::abs(level(morph, target, sr) - neutralLevel) > .005,
          "full Morph changes the magnitudes of the correction");
    Audio anti = input;
    for (size_t i = 1; i < anti.size(); i += 2) anti[i] = -anti[i];
    const Configure shape = [](auto& fx) { onlyC(fx); fx.setParam(Fx::Morph, .35f); fx.setParam(Fx::Gate, 1); };
    auto stereo = render(anti, sr, shape, {1, 64, 256, 511, 1024});
    double difference = 0;
    for (size_t i = 0; i < stereo.size(); i += 2)
        difference = std::max(difference, std::abs(double(stereo[i] + stereo[i + 1])));
    check(difference < 1e-5, "anti-phase stereo retains phase through Morph and Gate");
    check(stereo == render(anti, sr, shape, {1024}), "steady parameters render identically across arbitrary block sizes");
    Audio right = input;
    for (size_t i = 0; i < right.size(); i += 2) right[i] = 0;
    right = render(right, sr, shape);
    check(energy(right, 0, right.size() / 2) == 0 && energy(right, 20000, 50000, 1) > 1e-5,
          "right-only stereo remains isolated");
    Audio invalid = input;
    invalid[200] = std::numeric_limits<float>::quiet_NaN();
    invalid[201] = std::numeric_limits<float>::infinity();
    check(finite(render(invalid, sr)), "non-finite input is sanitized");
    Fx fx;
    onlyC(fx);
    fx.setParam(Fx::Resonance, std::numeric_limits<float>::quiet_NaN());
    fx.setParam(retiredMode, 1);
    fx.setParam(fftSize, std::numeric_limits<float>::infinity());
    check(fx.getParam(Fx::Resonance) == 0 && fx.getParam(retiredMode) == 0 && fx.getParam(fftSize) == .5f,
          "non-finite parameters and retired Mode are ignored");
}

void automationAndScopeTests() {
    constexpr double sr = 48000, source = 277.1826309768721;
    Audio input = tone(source, sr, 2);
    Fx fx;
    onlyC(fx);
    fx.setSampleRate(sr, 480);
    for (int i = 0; i < 96000; i += 480) {
        if (i == 33600) mask(fx, 1 << 2);
        if (i == 48000) {
            fx.setParam(Fx::Low, 1);
            fx.setParam(Fx::High, 0);
            fx.setParam(Fx::Resonance, 1);
            fx.setParam(Fx::Decay, 1);
        }
        if (i == 52800) {
            fx.setParam(Fx::Low, .25);
            fx.setParam(Fx::High, .5);
            fx.setParam(Fx::Resonance, 0);
            fx.setParam(Fx::Decay, .45);
        }
        countAllocations = true;
        fx.process(input.data() + 2 * i, 480);
        countAllocations = false;
    }
    check(level(input, source * 242 / 256, sr, .25, .6) > .02 &&
          level(input, source * 271 / 256, sr, 1.4, 1.9) > .02,
          "changing scale on sustained audio crossfades to the new direction");
    double step = 0;
    for (size_t i = 2; i < input.size(); i += 2) step = std::max(step, std::abs(double(input[i] - input[i - 2])));
    check(finite(input, 5) && step < .35, "live band, resonance and scale changes remain bounded");
    float scope[1026];
    check(fx.scopeRead(scope, 1026) == 1026 && scope[0] == sr && scope[1] == 1024 &&
          *std::max_element(scope + 2, scope + 514) > .3f && *std::max_element(scope + 514, scope + 1026) > .3f,
          "scope reports dry and correction with fixed latency");
    fx.setParam(Fx::Mix, 0);
    Audio zeroMix = tone(source, sr, .6);
    fx.process(zeroMix.data(), int(zeroMix.size() / 2));
    fx.scopeRead(scope, 1026);
    check(*std::max_element(scope + 514, scope + 1026) < .05f, "Tuned scope follows Mix");
    fx.setParam(Fx::Mix, 1);
    Audio quiet(2 * 96000);
    fx.process(quiet.data(), int(quiet.size() / 2));
    check(fx.isIdle(), "settled silence permits idle");
    auto wake = tone(source, sr, .25);
    fx.process(wake.data(), int(wake.size() / 2));
    check(!fx.isIdle() && energy(wake, 7000, 12000) > .0001, "audio wakes after silence");
}

void sleepTests() {
    constexpr int sr = 48000;
    constexpr int prefix = 37;
    const auto configure = [](Fx& fx) {
        mask(fx, 1);
        fx.setParam(Fx::Resonance, .8f);
        fx.setParam(Fx::Decay, .6f);
    };
    for (const auto& [choice, latency] : {std::pair{0.f, 512}, std::pair{.5f, 1024}, std::pair{1.f, 2048}}) {
        const auto suffix = " (FFT " + std::to_string(latency) + ")";
        Fx fx;
        configure(fx);
        fx.setSampleRate(sr, 256);
        check(fx.isIdle(), "Chroma starts idle with empty history" + suffix);
        const auto process = [&](Audio& audio) {
            countAllocations.store(true);
            fx.process(audio.data(), int(audio.size() / 2));
            countAllocations.store(false);
        };
        auto input = tone(noteHz(60), sr, .8);
        process(input);
        Audio tail(2 * 2400);
        process(tail);
        check(!fx.isIdle() && energy(tail, 1200, 2400) > 1e-8, "Chroma renders its resonant tail before sleeping" + suffix);

        Audio silence(2 * 3 * sr);
        process(silence);
        std::array<float, 1026> scope{};
        fx.scopeRead(scope.data(), int(scope.size()));
        Audio idleBlock(2 * 256);
        process(idleBlock);
        check(fx.isIdle() && energy(idleBlock, 0, 256) == 0 && energy(idleBlock, 0, 256, 1) == 0 &&
                std::all_of(scope.begin() + 2, scope.end(), [](float v) { return v == 0; }),
            "Chroma stays silent with empty telemetry while asleep" + suffix);

        fx.setParam(fftSize, choice);
        fx.setParam(Fx::Mix, 0);
        process(idleBlock);
        fx.scopeRead(scope.data(), int(scope.size()));
        check(fx.isIdle() && scope[1] == latency, "Chroma accepts FFT configuration while asleep" + suffix);

        Audio quietInput(2 * (prefix + latency + sr / 2));
        for (int i = 0; i < sr / 2; ++i) quietInput[2 * (prefix + i) + 1] = 1e-6f;
        Audio expected(quietInput.size());
        std::copy(quietInput.begin(), quietInput.end() - 2 * latency, expected.begin() + 2 * latency);
        process(quietInput);
        check(!fx.isIdle() && quietInput == expected, "Chroma wakes on quiet right-only audio at the exact delay" + suffix);
    }

    const auto phrase = tone(noteHz(61), sr, .24);
    Audio separated = phrase;
    separated.resize(separated.size() + 2 * (sr + prefix));
    separated.insert(separated.end(), phrase.begin(), phrase.end());
    check(render(separated, sr, onlyC, {64}) == render(separated, sr, onlyC, {511, 17, 256}),
        "Chroma sleep and wake are invariant across block partitions");
}

void midiKeyTests() {
    constexpr int sr = 48000, onset = 6000, next = 12000, release = 18000, sourceTrack = 7;
    const std::array events{
        nota::MidiEv{onset, true, 60, 1}, nota::MidiEv{onset, true, 72, 1},
        nota::MidiEv{onset + 2000, false, 60, 0}, nota::MidiEv{next, false, 72, 0},
        nota::MidiEv{next, true, 62, 1}, nota::MidiEv{release, false, 62, 0},
    };
    const auto input = tone(noteHz(61), sr, .55);
    for (const auto& [choice, latency] : {std::pair{0.f, 512}, std::pair{.5f, 1024}, std::pair{1.f, 2048}}) {
        const auto suffix = " (FFT " + std::to_string(latency) + ")";
        auto run = [&](const std::vector<int>& blocks) {
            Fx fx;
            onlyC(fx);
            fx.setParam(fftSize, choice);
            fx.setSampleRate(sr, 1024);
            fx.setSidechainSourceTrackId(sourceTrack);
            nota::MidiKeyNotes held;
            Audio output = input;
            size_t index = 0;
            for (int start = 0, block = 0; start < int(output.size() / 2); ++block) {
                const int frames = std::min(blocks[block % blocks.size()], int(output.size() / 2) - start);
                std::array<nota::MidiEv, events.size()> pending{};
                int count = 0;
                while (index < events.size() && events[index].off < start + frames) {
                    pending[count] = events[index++];
                    pending[count++].off -= start;
                }
                countAllocations = true;
                fx.setMidiKeyBlock({sourceTrack, pending.data(), count, &held});
                fx.process(output.data() + 2 * start, frames);
                countAllocations = false;
                for (int i = 0; i < count; ++i) held.apply(pending[i]);
                start += frames;
            }
            return output;
        };
        const auto output = run({256});
        const auto dry = [&](int frame) { return input[2 * (frame - latency)]; };
        bool aligned = output[2 * latency] == input[0];
        for (int i = latency; i < onset + latency; ++i) aligned &= std::abs(output[2 * i] - dry(i)) < 1e-9f;
        for (int i = release + latency + latency / 4; i < int(output.size() / 2); ++i)
            aligned &= std::abs(output[2 * i] - dry(i)) < 1e-9f;
        check(aligned && output == run({257, 17, 511, 64}), "MIDI correction follows delayed offsets across block partitions" + suffix);
        auto at = [&](double hz, int begin) { return level(output, hz, sr, (begin + latency + 2600.) / sr, (begin + latency + 5800.) / sr); };
        const double down = noteHz(61) * 242 / 256, up = noteHz(61) * 271 / 256;
        check(at(down, onset) > 2 * at(up, onset) && at(up, next) > 2 * at(down, next),
            "MIDI changes the audible target every sixteenth and keeps an overlapping octave" + suffix);
    }

    Fx idle;
    idle.setSidechainSourceTrackId(sourceTrack);
    nota::MidiKeyNotes held;
    const std::array chord{nota::MidiEv{18, true, 60, 1}, nota::MidiEv{63, true, 64, 1}};
    std::array<float, 512> zero{};
    idle.setMidiKeyBlock({sourceTrack, chord.data(), int(chord.size()), &held, 17});
    idle.process(zero.data(), int(zero.size() / 2));
    std::array<float, 1027> scope{};
    check(idle.isIdle() && idle.scopeRead(scope.data(), int(scope.size())) == int(scope.size()) && scope.back() == 17
            && std::all_of(zero.begin(), zero.end(), [](float v) { return v == 0; }),
        "MIDI updates the note display without waking silent audio DSP");
    idle.setMidiKeyBlock({sourceTrack});
    idle.process(zero.data(), int(zero.size() / 2));
    idle.scopeRead(scope.data(), int(scope.size()));
    check(idle.isIdle() && scope.back() == 0, "A missing MIDI source clears the displayed notes");
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
    constexpr int sr = 48000;
    Audio hits(2 * sr);
    for (int i = 24000; i < 26400; ++i) {
        const double t = (i - 24000) / double(sr);
        hits[2 * i] = float(.4 * std::sin(tau * 700 * t) * std::exp(-90 * t));
        hits[2 * i + 1] = hits[2 * i];
    }
    auto guarded = render(hits, sr, onlyC);
    auto antiphase = hits;
    for (size_t i = 1; i < antiphase.size(); i += 2) antiphase[i] = -antiphase[i];
    const auto unguarded = render(antiphase, sr, onlyC); // The summed transient detector sees zero.
    double guardedError = 0, unguardedError = 0;
    const auto midOnly = render(hits, sr, [](auto& fx) { onlyC(fx); fx.setParam(Fx::Amount, 0); });
    for (int i = 24000 + 1024; i < 24000 + 1024 + 240; ++i) {
        guardedError += std::pow(guarded[2 * i] - midOnly[2 * i], 2);
        unguardedError += std::pow(unguarded[2 * i] - midOnly[2 * i], 2);
    }
    check(guardedError < .8 * unguardedError, "guard keeps the first 5 ms closer to the dry crossover response");
}

void listening(const std::filesystem::path& out, int recordingCount, char** recordings) {
    std::ofstream csv(out / "metrics.csv");
    csv << "scene,median_ms,p99_block_us,max_block_us,rms_match_gain,raw_peak,tail_rms_dbfs\n";
    std::vector<std::string> scenes = {"fm", "growl", "chord", "attack", "decay", "boundary"};
    const size_t generated = scenes.size();
    for (int i = 0; i < recordingCount; ++i) scenes.push_back("recording-" + std::to_string(i + 1));
    for (size_t index = 0; index < scenes.size(); ++index) {
        const auto& name = scenes[index];
        Audio input = index < generated ? scene(name) : readRecording(recordings[index - generated]);
        writeWav(out / (name + "-source.wav"), input, 48000);
        Audio wet;
        int latency = 0;
        std::vector<double> times;
        std::vector<double> blockTimes;
        blockTimes.reserve(5 * 938);
        for (int run = 0; run < 5; ++run) {
            nota::Chroma fx;
            if (name == "decay") { fx.setParam(nota::Chroma::Decay, 1); fx.setParam(nota::Chroma::Resonance, .7f); }
            fx.setSampleRate(48000, 256); latency = fx.latencySamples(); wet = input;
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
        std::copy(wet.begin() + 2 * latency, wet.end(), aligned.begin());
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
        {
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
    const std::string mode = argv[2];
    if (mode != "--listen-only") {
        delayAndRoutingTests();
        resonanceAndBandTests();
        sidebandMuteTests();
        shapeAndStereoTests();
        automationAndScopeTests();
        bassSpectrumTests();
        fftSizeTests();
        sleepTests();
        midiKeyTests();
        attackTest();
        check(allocations.load() == 0, "all guarded renders allocate nothing");
    }
    if (mode != "--tests-only") listening(argv[1], argc - 3, argv + 3);
    std::printf("%d checks, %d failures\n", checks, failures);
    return failures ? 1 : 0;
}
