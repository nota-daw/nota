// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// engine-bench — measures one engine scenario through the public C ABI and prints
// one CSV row. Built and driven by scripts/bench-engine.sh, which links it against
// each engine build being compared (so it only uses functions every compared build
// exports — keep it to long-standing ABI).
//
//   engine-bench idle
//   engine-bench <audio|inst> <tracks> <offline|rt> <clip.wav>
//
// idle     — engine created, nothing playing: process CPU over 5 s (% of one core).
// audio    — <tracks> audio tracks, each a WAV clip + Reverb.
// inst     — <tracks> instrument tracks (Nota Synth), each a MIDI clip of chords.
// offline  — 10 s rendered back-to-back in 512-frame blocks (a bounce/export).
// rt       — the same blocks paced like a 512 @ 48 kHz device callback.
//
// Row: scenario,tracks,mode,cpu_ms_per_s,wall_ms_per_s,cpu_pct
// (per second of audio; idle reports cpu_pct only).

#include "nota/nota_engine.h"

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

#include <sys/resource.h>

namespace {

double processCpuSeconds() {
    rusage u{};
    getrusage(RUSAGE_SELF, &u);
    return u.ru_utime.tv_sec + u.ru_utime.tv_usec * 1e-6 + u.ru_stime.tv_sec + u.ru_stime.tv_usec * 1e-6;
}

int usage() {
    std::fprintf(stderr, "usage: engine-bench idle | engine-bench <audio|inst> <tracks> <offline|rt> <clip.wav>\n");
    return 2;
}

} // namespace

int main(int argc, char** argv) {
    if (argc < 2) return usage();
    const std::string scenario = argv[1];
    using Clock = std::chrono::steady_clock;

    NotaEngine* e = nota_engine_create();
    if (!e) return 1;

    if (scenario == "idle") {
        std::this_thread::sleep_for(std::chrono::seconds(1)); // let threads settle
        const double c0 = processCpuSeconds();
        const auto t0 = Clock::now();
        std::this_thread::sleep_for(std::chrono::seconds(5));
        const double wall = std::chrono::duration<double>(Clock::now() - t0).count();
        std::printf("idle,0,idle,,,%.1f\n", 100.0 * (processCpuSeconds() - c0) / wall);
        nota_engine_destroy(e);
        return 0;
    }

    if (argc < 5) return usage();
    const int tracks = std::atoi(argv[2]);
    const std::string mode = argv[3];
    const char* clip = argv[4];
    if ((scenario != "audio" && scenario != "inst") || (mode != "offline" && mode != "rt") || tracks <= 0)
        return usage();

    for (int i = 0; i < tracks; ++i) {
        if (scenario == "audio") {
            const int32_t t = nota_engine_add_audio_track(e);
            nota_track_add_audio_clip(e, t, clip, 0.0);
            nota_track_add_builtin_device(e, t, 2); // Reverb
        } else {
            const int32_t t = nota_engine_add_instrument_track(e);
            const int32_t c = nota_track_add_midi_clip(e, t, 0.0, 32.0);
            std::vector<NotaNoteData> notes;
            for (int beat = 0; beat < 32; ++beat)
                for (int k : {0, 4, 7}) { // a triad on every beat, root moving with the track
                    NotaNoteData n;
                    std::memset(&n, 0, sizeof n);
                    n.pitch = 48 + (i % 12) + k;
                    n.start_beat = beat;
                    n.length_beats = 0.9;
                    n.velocity = 0.8f;
                    notes.push_back(n);
                }
            nota_clip_set_notes(e, t, c, notes.data(), (int32_t)notes.size());
        }
    }

    const int block = 512, rate = 48000, seconds = 10;
    std::vector<float> buf((size_t)block * 2);
    nota_engine_render_offline_at(e, buf.data(), block, rate); // prepares every node at the rate
    nota_transport_play(e);

    const bool paced = mode == "rt";
    const double c0 = processCpuSeconds();
    const auto t0 = Clock::now();
    auto next = t0;
    for (int f = 0; f < seconds * rate; f += block) {
        nota_engine_render_offline(e, buf.data(), block);
        if (paced) {
            next += std::chrono::microseconds((int64_t)block * 1000000 / rate);
            std::this_thread::sleep_until(next);
        }
    }
    const double wall = std::chrono::duration<double>(Clock::now() - t0).count();
    const double cpu = processCpuSeconds() - c0;
    std::printf("%s,%d,%s,%.1f,%.1f,%.1f\n", scenario.c_str(), tracks, mode.c_str(), 1000.0 * cpu / seconds,
        1000.0 * wall / seconds, 100.0 * cpu / wall);
    nota_engine_destroy(e);
    return 0;
}
