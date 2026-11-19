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
    fx.setParam(24, 1);
    check(fx.getParam(Fx::Resonance) == 0 && fx.getParam(24) == 0 && fx.paramCount() == 24,
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
        shapeAndStereoTests();
        automationAndScopeTests();
        attackTest();
        check(allocations.load() == 0, "all guarded renders allocate nothing");
    }
    if (mode != "--tests-only") listening(argv[1], argc - 3, argv + 3);
    std::printf("%d checks, %d failures\n", checks, failures);
    return failures ? 1 : 0;
}
