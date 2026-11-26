// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#include "devices/effects/Vocoder.h"
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <vector>

using nota::Vocoder;
constexpr int sampleRate = 48000;
constexpr int frames = sampleRate * 3;
constexpr int block = 512;

int main(int argc, char** argv) {
    std::vector<float> mod(frames * 2), carrier(frames * 2), output(frames * 2);
    for (int i = 0; i < frames; ++i) {
        const double t = static_cast<double>(i) / sampleRate;
        const double syllable = std::fabs(std::sin(9.4 * t));
        mod[2*i] = mod[2*i+1] = static_cast<float>(
            (std::sin(942.5 * t) + 0.6 * std::sin(5026.5 * t) + 0.2 * std::sin(12566.4 * t)) * 0.15 * syllable);
        carrier[2*i] = static_cast<float>((std::sin(822 * t) + std::sin(1035.6 * t)) * 0.14);
        carrier[2*i+1] = static_cast<float>((std::sin(821.6 * t) + std::sin(1399 * t)) * 0.14);
    }
    const bool sample = argc == 2 && std::strcmp(argv[1], "--sample") == 0;
    const bool trace = argc == 2 && !sample;
    FILE* file = trace ? std::fopen(argv[1], "wb") : nullptr;
    if (trace && !file) return 1;
    auto render = [&](Vocoder& effect, int count) {
        for (int i = 0; i < count; i += block) {
            const int n = std::min(block, count - i);
            if (trace) {
                if (i == block * 10) {
                    effect.setSampleRate(22050, block);
                } else if (i == block * 20) {
                    effect.setSampleRate(sampleRate, block);
                } else if (i == block * 40) {
                    effect.setParam(Vocoder::Low, 0.85f);
                    effect.setParam(Vocoder::High, 0.91f);
                    effect.setParam(Vocoder::Q, 0.79f);
                    effect.setParam(Vocoder::Bands, 0.42f);
                    effect.setParam(Vocoder::Formant, 0.98f);
                    effect.setParam(Vocoder::Mix, 0.37f);
                    effect.setParam(Vocoder::Tilt, 0.83f);
                    effect.setParam(Vocoder::Pitch, 0.63f);
                    effect.setParam(Vocoder::Carrier, 0.0f);
                } else if (i == block * 78) {
                    effect.setParam(Vocoder::Hold, 1.0f);
                    effect.setParam(Vocoder::Carrier, 0.5f);
                } else if (i == block * 117) {
                    effect.setSampleRate(22050, block);
                    effect.setParam(Vocoder::Hold, 0.0f);
                    effect.setParam(Vocoder::Carrier, 1.0f);
                } else if (i == block * 161) {
                    effect.setSampleRate(sampleRate, block);
                    effect.setParam(Vocoder::Formant, 0.12f);
                    effect.setParam(Vocoder::Bands, 1.0f);
                }
            }
            effect.setSidechain(carrier.data() + 2*i, n);
            effect.process(output.data() + 2*i, n);
            if (trace) {
                std::array<float, Vocoder::kTele> scope;
                effect.scopeRead(scope.data(), static_cast<int>(scope.size()));
                std::fwrite(output.data() + 2*i, sizeof(float), 2*n, file);
                std::fwrite(scope.data(), sizeof(float), scope.size(), file);
            }
        }
    };
    std::array<double, 11> times;
    for (int trial = 0; trial < ((trace || sample) ? 1 : static_cast<int>(times.size())); ++trial) {
        Vocoder effect;
        effect.setSampleRate(sampleRate, block);
        effect.setParam(Vocoder::Carrier, 1.0f);
        effect.setParam(Vocoder::Bands, 1.0f);
        effect.setSidechainSourceTrackId(1);
        if (!trace) {
            output = mod;
            render(effect, sampleRate / 2);
        }
        output = mod;
        const auto start = std::chrono::steady_clock::now();
        render(effect, frames);
        times[trial] = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
    }
    if (trace) return std::fclose(file) != 0;
    if (sample) {
        std::printf("%.6f\n", times[0]);
        return 0;
    }
    std::sort(times.begin(), times.end());
    std::printf("median %.3f ms, min %.3f ms, max %.3f ms (11 x 3 s)\n", times[5], times.front(), times.back());
}
