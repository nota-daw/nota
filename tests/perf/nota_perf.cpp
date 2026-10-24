// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine performance scenarios for the CI regression gate (specs/034-engine-perf-gate).
// Drives the engine through the public C ABI only: builds a session, plays it and
// renders offline. Under Valgrind Callgrind (started with --instr-atstart=no) only the
// measured render loop is instrumented, across every engine thread, so the instruction
// count is the render's work and nothing else. Outside Valgrind the client requests are
// no-ops and the wall time is the useful number.
//
//   nota_perf list                      one scenario id per line
//   nota_perf run <id> [seconds]        prints: id <TAB> label <TAB> wall_ms_per_s <TAB> peak

#include "nota/nota_engine.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#if defined(__has_include)
#if __has_include(<valgrind/callgrind.h>)
#include <valgrind/callgrind.h>
#endif
#endif
#ifndef CALLGRIND_START_INSTRUMENTATION
#define CALLGRIND_START_INSTRUMENTATION
#define CALLGRIND_STOP_INSTRUMENTATION
#endif

namespace {

constexpr double kSampleRate = 48000.0;
constexpr int kBlock = 512;
constexpr int kMaxKind = 64;    // probe built-in kinds 0..63; new devices are picked up automatically
constexpr int kEffectCopies = 8; // instances per effect scenario, so the effect dominates the count

// A 4-bar loop of 4-note chords plus 8th-note stabs: realistic polyphony.
void fillClip(NotaEngine* e, int track, int transpose, double startBeat, double lengthBeats) {
    const int clip = nota_track_add_midi_clip(e, track, startBeat, lengthBeats);
    static const int chords[4][4] = {{48, 55, 60, 64}, {45, 52, 57, 60}, {41, 48, 53, 57}, {43, 50, 55, 59}};
    std::vector<NotaNoteData> notes;
    for (int bar = 0; bar < (int)(lengthBeats / 4); ++bar) {
        const int* c = chords[bar % 4];
        for (int k = 0; k < 4; ++k) notes.push_back({c[k] + transpose, bar * 4.0, 4.0, 0.7f, 0});
        for (int s = 0; s < 8; ++s) notes.push_back({c[s % 4] + 12 + transpose, bar * 4.0 + s * 0.5, 0.4, 0.8f, 0});
    }
    nota_clip_set_notes(e, track, clip, notes.data(), (int)notes.size());
}

int instrumentTrack(NotaEngine* e, int kind) {
    const int t = nota_engine_add_instrument_track(e);
    if (kind != 0 && nota_track_set_builtin_instrument(e, t, kind) != NOTA_OK) return -1;
    return t;
}

// Super Saw factory preset (specs/036-aurora-perf/supersaw-params.md), applied by
// param id so preset edits stay in sync without hardcoded indices.
void applySuperSaw(NotaEngine* e, int track) {
    static const char* const ids[] = {"table", "position", "osc2on", "osc2table", "osc2position", "osc2level",
        "osc2detune", "unison", "unidetune", "unispread", "cutoff", "resonance", "fil1env", "attack", "decay",
        "sustain", "release", "fxdrive", "fxchorus", "fxreverb", "gain"};
    static const float values[] = {0, 0.9f, 1, 0, 0.9f, 0.8f, 0.62f, 1, 0.4f, 0.9f, 0.8f, 0.14f, 0.55f, 0, 0.4f,
        0.85f, 0.28f, 0.2f, 0.26f, 0.24f, 0.72f};
    const int n = nota_plugin_param_count(e, track, -1);
    for (size_t p = 0; p < sizeof ids / sizeof ids[0]; ++p)
        for (int i = 0; i < n; ++i) {
            const char* pid = nota_plugin_param_id(e, track, -1, i);
            if (pid && std::strcmp(pid, ids[p]) == 0) {
                nota_plugin_param_set(e, track, -1, i, values[p]);
                break;
            }
        }
}

// A 4-bar loop of 6-note chords: the reference load for aurora-supersaw-2x.
void fillChords6(NotaEngine* e, int track, int transpose, double startBeat, double lengthBeats) {
    const int clip = nota_track_add_midi_clip(e, track, startBeat, lengthBeats);
    static const int chords[4][6] = {
        {48, 55, 60, 64, 67, 72}, {45, 52, 57, 60, 64, 69}, {41, 48, 53, 57, 60, 65}, {43, 50, 55, 59, 62, 67}};
    std::vector<NotaNoteData> notes;
    for (int bar = 0; bar < (int)(lengthBeats / 4); ++bar) {
        const int* c = chords[bar % 4];
        for (int k = 0; k < 6; ++k) notes.push_back({c[k] + transpose, bar * 4.0, 4.0, 0.7f, 0});
    }
    nota_clip_set_notes(e, track, clip, notes.data(), (int)notes.size());
}

// Display names for the report; the scenario id (the kind number) is what is compared.
std::string instrumentName(int kind) {
    switch (kind) {
    case 0: return "Synth";
    case 1: return "Sampler (no sample)";
    case 2: return "Physical";
    case 5: return "Aurora";
    case 6: return "Volt";
    case 7: return "Bass";
    case 8: return "Pendulum";
    case 9: return "Operator";
    case 10: return "Grain";
    case 11: return "Flux";
    case 12: return "Rhythm Machine";
    case 13: return "Monolith";
    case 14: return "Pentad";
    case 15: return "Consort";
    case 16: return "Keys";
    case 17: return "Mosaic (no program)";
    default: return "Instrument kind " + std::to_string(kind);
    }
}

bool instrumentKindExists(int kind) {
    if (kind == 0) return true;
    NotaEngine* e = nota_engine_create();
    const int t = nota_engine_add_instrument_track(e);
    const bool ok = nota_track_set_builtin_instrument(e, t, kind) == NOTA_OK && nota_track_instrument_kind(e, t) == kind;
    nota_engine_destroy(e);
    return ok;
}

std::string effectName(int kind) {
    NotaEngine* e = nota_engine_create();
    const int t = nota_engine_add_instrument_track(e);
    const int idx = nota_track_add_builtin_device(e, t, kind);
    std::string name = idx >= 0 ? nota_device_name(e, t, idx) : "";
    nota_engine_destroy(e);
    return name;
}

struct Scenario {
    std::string id, label;
};

std::vector<Scenario> scenarios() {
    std::vector<Scenario> s;
    char id[64];
    for (int k = 0; k < kMaxKind; ++k)
        if (instrumentKindExists(k)) {
            std::snprintf(id, sizeof id, "instrument-%02d", k);
            s.push_back({id, instrumentName(k) + ", one track"});
        }
    s.push_back({"drum-rack", "Drum Rack, 4 Synth pads with decay"});
    s.push_back({"instrument-rack", "Instrument Rack, 2 Synth chains"});
    s.push_back({"effect-base", "Synth alone (effect baseline)"});
    s.push_back({"aurora-supersaw-2x", "Aurora Super Saw x2"});
    for (int k = 0; k < kMaxKind; ++k) {
        const std::string name = effectName(k);
        if (name.empty()) continue;
        std::snprintf(id, sizeof id, "effect-%02d", k);
        s.push_back({id, name + " x" + std::to_string(kEffectCopies) + " on Synth"});
    }
    for (int n : {1, 4, 8, 16, 32}) s.push_back({"mix-" + std::to_string(n), std::to_string(n) + " tracks, all playing"});
    s.push_back({"sparse-32", "32 tracks, 1 in 4 playing"});
    s.push_back({"silent-32", "32 tracks, no clips"});
    return s;
}

// Builds the scenario into a fresh engine. Returns false for an unknown id.
bool build(NotaEngine* e, const std::string& id) {
    nota_transport_set_bpm(e, 120);
    int kind = -1;
    if (std::sscanf(id.c_str(), "instrument-%d", &kind) == 1) {
        const int t = instrumentTrack(e, kind);
        if (t < 0) return false;
        fillClip(e, t, 0, 0, 16);
        return true;
    }
    if (id == "effect-base" || std::sscanf(id.c_str(), "effect-%d", &kind) == 1) {
        const int t = instrumentTrack(e, 0);
        fillClip(e, t, 0, 0, 16);
        if (id != "effect-base")
            for (int i = 0; i < kEffectCopies; ++i)
                if (nota_track_add_builtin_device(e, t, kind) < 0) return false;
        return true;
    }
    // The racks render their chains themselves (pad decay VCA, key zones), so they get their own rows.
    if (id == "drum-rack") {
        const int t = nota_engine_add_drum_rack_track(e);
        if (t < 0) return false;
        static const int pads[4] = {36, 38, 42, 46};
        for (int p : pads) {
            const int c = nota_rack_add_chain(e, t, 0);
            if (c < 0) return false;
            nota_rack_set_chain_trigger_note(e, t, c, p);
            nota_rack_set_chain_decay(e, t, c, 0.5f);
        }
        const int clip = nota_track_add_midi_clip(e, t, 0.0, 16.0);
        std::vector<NotaNoteData> notes;
        for (int s16 = 0; s16 < 64; ++s16) notes.push_back({pads[s16 % 4], s16 * 0.25, 0.2, 0.9f, 0});
        nota_clip_set_notes(e, t, clip, notes.data(), (int)notes.size());
        return true;
    }
    if (id == "instrument-rack") {
        const int t = nota_engine_add_instrument_rack_track(e);
        if (t < 0) return false;
        for (int i = 0; i < 2; ++i)
            if (nota_rack_add_chain(e, t, 0) < 0) return false;
        fillClip(e, t, 0, 0, 16);
        return true;
    }
    if (id == "aurora-supersaw-2x") {
        for (int t = 0; t < 2; ++t) {
            const int tr = instrumentTrack(e, 5);
            if (tr < 0) return false;
            applySuperSaw(e, tr);
            fillChords6(e, tr, t == 0 ? 0 : 7, 0, 16);
        }
        return true;
    }
    int n = 0;
    if (std::sscanf(id.c_str(), "mix-%d", &n) == 1) {
        // The stress-test chain on each track: Volt + EQ-8 + Compressor + Auto Filter.
        for (int i = 0; i < n; ++i) {
            const int t = instrumentTrack(e, 6);
            fillClip(e, t, (i % 5) * 2 - 4, 0, 16);
            nota_track_add_builtin_device(e, t, 0);
            nota_track_add_builtin_device(e, t, 1);
            nota_track_add_builtin_device(e, t, 7);
        }
        return n > 0;
    }
    if (id == "sparse-32" || id == "silent-32") {
        for (int i = 0; i < 32; ++i) {
            const int t = instrumentTrack(e, 6);
            if (id == "sparse-32") fillClip(e, t, 0, (i % 4) * 4.0, 4); // one bar in four
            nota_track_add_builtin_device(e, t, 0);
            nota_track_add_builtin_device(e, t, 1);
            nota_track_add_builtin_device(e, t, 3);
        }
        return true;
    }
    return false;
}

int run(const std::string& id, double seconds) {
    std::string label = id;
    for (const auto& s : scenarios())
        if (s.id == id) label = s.label;

    NotaEngine* e = nota_engine_create();
    if (!build(e, id)) {
        std::fprintf(stderr, "nota_perf: unknown or unsupported scenario '%s'\n", id.c_str());
        nota_engine_destroy(e);
        return 2;
    }
    nota_transport_set_loop(e, 1, 0.0, 16.0);
    nota_transport_seek(e, 0.0);
    nota_transport_play(e);

    std::vector<float> buf((size_t)kBlock * 2);
    for (int i = 0; i < (int)(0.25 * kSampleRate / kBlock); ++i) // warm-up: voices, caches, pool threads
        nota_engine_render_offline_at(e, buf.data(), kBlock, kSampleRate);

    const int blocks = std::max(1, (int)(seconds * kSampleRate / kBlock));
    float peak = 0.0f;
    const auto t0 = std::chrono::steady_clock::now();
    CALLGRIND_START_INSTRUMENTATION;
    for (int i = 0; i < blocks; ++i) {
        nota_engine_render_offline_at(e, buf.data(), kBlock, kSampleRate);
        for (float v : buf) peak = std::max(peak, std::fabs(v));
    }
    CALLGRIND_STOP_INSTRUMENTATION;
    const auto t1 = std::chrono::steady_clock::now();

    nota_transport_stop(e);
    nota_engine_destroy(e);
    const double audio = blocks * (double)kBlock / kSampleRate;
    const double wallMs = std::chrono::duration<double, std::milli>(t1 - t0).count() / audio;
    std::printf("%s\t%s\t%.3f\t%.4f\n", id.c_str(), label.c_str(), wallMs, peak);
    return 0;
}

} // namespace

int main(int argc, char** argv) {
    if (argc >= 2 && std::strcmp(argv[1], "list") == 0) {
        for (const auto& s : scenarios()) std::printf("%s\n", s.id.c_str());
        return 0;
    }
    if (argc >= 3 && std::strcmp(argv[1], "run") == 0) return run(argv[2], argc >= 4 ? std::atof(argv[3]) : 1.0);
    std::fprintf(stderr, "usage: nota_perf list | nota_perf run <scenario> [seconds]\n");
    return 2;
}
