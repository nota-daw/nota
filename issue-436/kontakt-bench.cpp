// #436 bench: the kontakt_problem.nota tracks (4 x Kontakt 8 AU, dense short-note loops)
// rendered block by block through the device path at 64 frames / 44.1 kHz.
// Reports callback CPU per block and blocks over the buffer period.
//   bench <tracks.txt> <project dir> <seconds>
#include "nota/nota_engine.h"

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

int main(int argc, char** argv) {
    if (argc < 4) return 2;
    const std::string proj = argv[2];
    const double seconds = std::atof(argv[3]);
    constexpr double sr = 44100.0;
    constexpr int block = 64;

    nota_set_data_dir("/private/tmp/nota-bench-436/data"); // a copy of the user's plugins.xml
    NotaEngine* e = nota_engine_create();
    std::printf("catalog: %d plugins\n", nota_pluginhost_plugin_count());

    std::ifstream in(argv[1]);
    std::string line;
    int track = -1, clip = -1;
    std::vector<NotaNoteData> notes;
    auto flush = [&] {
        if (track > 0 && !notes.empty()) nota_clip_set_notes(e, track, clip, notes.data(), (int)notes.size());
        notes.clear();
    };
    int tracks = 0;
    while (std::getline(in, line)) {
        std::istringstream ss(line);
        std::string tag;
        ss >> tag;
        if (tag == "T") {
            flush();
            std::string id, state;
            double start;
            int len;
            ss >> id >> state >> start >> len;
            for (size_t p; (p = id.find("%20")) != std::string::npos;) id.replace(p, 3, " ");
            const int idx = nota_pluginhost_index_of_id(id.c_str());
            if (idx < 0) { std::printf("plugin not in catalog: %s\n", id.c_str()); return 1; }
            track = nota_engine_add_plugin_instrument_track(e, idx);
            std::ifstream sf(proj + "/" + state, std::ios::binary);
            std::vector<uint8_t> blob((std::istreambuf_iterator<char>(sf)), {});
            nota_plugin_set_state(e, track, -1, blob.data(), (int)blob.size());
            clip = nota_track_add_midi_clip(e, track, start, len);
            ++tracks;
        } else if (tag == "N") {
            NotaNoteData n{};
            ss >> n.pitch >> n.start_beat >> n.length_beats >> n.velocity;
            notes.push_back(n);
        }
    }
    flush();
    std::printf("tracks: %d\n", tracks);

    nota_transport_set_bpm(e, 120.0);
    nota_transport_set_loop(e, 1, 0.0, 16.0);
    std::vector<float> buf(block * 2);
    nota_engine_render_offline_at(e, buf.data(), block, sr); // prepare at the rate
    nota_transport_seek(e, 0.0);
    nota_transport_play(e);

    const double periodMs = 1000.0 * block / sr;
    const int blocks = (int)(seconds * sr / block);
    const int perSec = (int)(sr / block);
    std::vector<double> ms;
    ms.reserve(blocks);
    for (int b = 0; b < blocks; ++b) {
        const auto t0 = std::chrono::steady_clock::now();
        nota_engine_render_device_block(e, buf.data(), block);
        const double dt = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();
        ms.push_back(dt);
        // Pace like a device: wait out the rest of the period.
        const double rest = periodMs - dt;
        if (rest > 0) {
            const auto until = t0 + std::chrono::duration<double, std::milli>(periodMs);
            while (std::chrono::steady_clock::now() < until) {}
        }
    }
    // Per 10-second window: mean callback ms, p99, late blocks.
    std::printf("period %.3f ms\nwindow  mean_ms  p99_ms  max_ms  late\n", periodMs);
    int lateAll = 0;
    double sumAll = 0;
    for (int w = 0; w * 10 * perSec < blocks; ++w) {
        const int a = w * 10 * perSec, z = std::min(blocks, a + 10 * perSec);
        std::vector<double> v(ms.begin() + a, ms.begin() + z);
        double sum = 0;
        int late = 0;
        for (double x : v) { sum += x; late += x > periodMs; }
        std::sort(v.begin(), v.end());
        std::printf("%3d-%3ds  %6.3f  %6.3f  %6.3f  %d\n", w * 10, (w + 1) * 10, sum / v.size(),
            v[(size_t)(0.99 * (v.size() - 1))], v.back(), late);
        lateAll += late;
        sumAll += sum;
    }
    std::vector<double> all(ms);
    std::sort(all.begin(), all.end());
    std::printf("TOTAL mean %.3f ms (%.0f%% of period), p99 %.3f, p99.9 %.3f, late %d of %d\n", sumAll / blocks,
        100.0 * sumAll / blocks / periodMs, all[(size_t)(0.99 * (all.size() - 1))],
        all[(size_t)(0.999 * (all.size() - 1))], lateAll, blocks);
    nota_engine_destroy(e);
    return 0;
}
