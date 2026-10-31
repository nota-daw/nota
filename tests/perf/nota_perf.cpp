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
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <random>
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

// Sets a track instrument's param by id (no-op for an unknown id).
void setParamById(NotaEngine* e, int track, const char* id, float v) {
    const int n = nota_plugin_param_count(e, track, -1);
    for (int i = 0; i < n; ++i) {
        const char* pid = nota_plugin_param_id(e, track, -1, i);
        if (pid && std::strcmp(pid, id) == 0) {
            nota_plugin_param_set(e, track, -1, i, v);
            return;
        }
    }
}

// A held `voices`-note chord for 4 bars, retriggered every bar: a synth at full polyphony.
void fillFullChord(NotaEngine* e, int track, int voices) {
    const int clip = nota_track_add_midi_clip(e, track, 0.0, 16.0);
    std::vector<NotaNoteData> notes;
    for (int bar = 0; bar < 4; ++bar)
        for (int k = 0; k < voices; ++k) notes.push_back({36 + k * 4 + (bar & 1), bar * 4.0, 3.9, 0.75f, 0});
    nota_clip_set_notes(e, track, clip, notes.data(), (int)notes.size());
}

// An 8 s stereo 48 kHz WAV of a chord with a little noise (so the stretch has real work), in
// the temp dir; the warp scenarios load it.
// Each process writes its own file (perf-gate runs scenarios in parallel), removed once
// the clip has loaded it.
std::string warpTestWav() {
    const auto path = (std::filesystem::temp_directory_path() /
                       ("nota_perf_warp_" + std::to_string(std::random_device{}()) + ".wav")).string();
    constexpr double kTwoPi = 6.283185307179586;
    const int sr = 48000, frames = sr * 8;
    std::vector<int16_t> pcm((size_t)frames * 2);
    uint32_t rnd = 1;
    for (int i = 0; i < frames; ++i) {
        const double t = (double)i / sr;
        rnd = rnd * 1664525u + 1013904223u;
        const double v = 0.25 * (std::sin(kTwoPi * 220 * t) + std::sin(kTwoPi * 277 * t) + std::sin(kTwoPi * 330 * t)) +
                         0.05 * ((double)(rnd >> 8) / (1 << 24) - 0.5);
        pcm[(size_t)i * 2] = pcm[(size_t)i * 2 + 1] = (int16_t)(std::clamp(v, -1.0, 1.0) * 32000);
    }
    std::ofstream f(path, std::ios::binary);
    auto u32 = [&](uint32_t v) { f.write(reinterpret_cast<const char*>(&v), 4); };
    auto u16 = [&](uint16_t v) { f.write(reinterpret_cast<const char*>(&v), 2); };
    const uint32_t bytes = (uint32_t)pcm.size() * 2;
    f.write("RIFF", 4); u32(36 + bytes); f.write("WAVEfmt ", 8); u32(16); u16(1); u16(2); u32(sr); u32(sr * 4); u16(4); u16(16);
    f.write("data", 4); u32(bytes);
    f.write(reinterpret_cast<const char*>(pcm.data()), bytes);
    return path;
}

int gWarpTrack = -1; // the track warpedClipTrack made last

// One audio track holding the 8 s WAV warped (Complex) over 16 beats, its stretch built.
int warpedClipTrack(NotaEngine* e) {
    const int t = nota_engine_add_audio_track(e);
    const std::string wav = warpTestWav();
    const bool loaded = t >= 0 && nota_track_add_audio_clip(e, t, wav.c_str(), 0.0) == 0;
    std::error_code ec;
    std::filesystem::remove(wav, ec);
    if (!loaded) return -1;
    if (nota_clip_set_warp(e, t, 0, 1, 3) != NOTA_OK) return -1;
    return gWarpTrack = t;
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
    case 18: return "Twin";
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
    // Full-polyphony synth loads (specs/055 SC-001: Twin at 12 voices against Pentad at 16).
    s.push_back({"pentad-full-16", "Pentad, 16-voice chord"});
    s.push_back({"twin-full-12", "Twin, 12-voice chord, both filters, 16 routes, FX"});
    s.push_back({"twin-layer-6x6", "Twin Layer, 6 + 6 voices, both parts' FX"});
    for (int k = 0; k < kMaxKind; ++k) {
        const std::string name = effectName(k);
        if (name.empty()) continue;
        std::snprintf(id, sizeof id, "effect-%02d", k);
        s.push_back({id, name + " x" + std::to_string(kEffectCopies) + " on Synth"});
    }
    for (int n : {1, 4, 8, 16, 32}) s.push_back({"mix-" + std::to_string(n), std::to_string(n) + " tracks, all playing"});
    s.push_back({"sparse-32", "32 tracks, 1 in 4 playing"});
    s.push_back({"silent-32", "32 tracks, no clips"});
    s.push_back({"audio-warp-split", "Warped audio clip split in 8, playing"});
    // An edit, not a render: the measured loop splits a warped clip 8 times and undoes it (#299).
    s.push_back({"edit-warp-split", "Split a warped 8 s clip 8x + undo (ms per edit)"});
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
    if (id == "pentad-full-16") {
        const int t = instrumentTrack(e, 14);
        if (t < 0) return false;
        setParamById(e, t, "polyphony", 1.0f); // 16 voices
        fillFullChord(e, t, 16);
        return true;
    }
    if (id == "twin-full-12") {
        // Both oscillators and noise into both filters, half series / half parallel, resonant.
        // and the part's effects on: chorus, delay and reverb.
        const int t = instrumentTrack(e, 18);
        if (t < 0) return false;
        for (auto [pid, v] : {std::pair{"lo.a.o2level", 0.8f}, {"lo.a.noiselevel", 0.2f}, {"lo.a.o1bal", 0.5f},
                 {"lo.a.o2bal", 0.5f}, {"lo.a.route", 0.5f}, {"lo.a.mmres", 0.4f}, {"lo.a.ldres", 0.4f},
                 {"lo.a.o1metal", 0.3f}, {"lo.a.drive", 0.3f}, {"lo.a.brute", 0.3f}, {"lo.a.modfxon", 1.0f},
                 {"lo.a.dlyon", 1.0f}, {"lo.a.revon", 1.0f}})
            setParamById(e, t, pid, v);
        // 16 live matrix routes (slots 7..22; 1..6 are Init's expression routes, idle here): LFOs,
        // envelopes, velocity, key and random onto cutoffs, wave, width, morph and more.
            setParamById(e, t, "lo.m7.src", 1.0f / 63.0f); setParamById(e, t, "lo.m7.dst", 28.0f / 255.0f); setParamById(e, t, "lo.a.m7amt", 0.6f); setParamById(e, t, "lo.b.m7amt", 0.6f);
            setParamById(e, t, "lo.m8.src", 2.0f / 63.0f); setParamById(e, t, "lo.m8.dst", 23.0f / 255.0f); setParamById(e, t, "lo.a.m8amt", 0.6f); setParamById(e, t, "lo.b.m8amt", 0.6f);
            setParamById(e, t, "lo.m9.src", 3.0f / 63.0f); setParamById(e, t, "lo.m9.dst", 6.0f / 255.0f); setParamById(e, t, "lo.a.m9amt", 0.6f); setParamById(e, t, "lo.b.m9amt", 0.6f);
            setParamById(e, t, "lo.m10.src", 4.0f / 63.0f); setParamById(e, t, "lo.m10.dst", 13.0f / 255.0f); setParamById(e, t, "lo.a.m10amt", 0.6f); setParamById(e, t, "lo.b.m10amt", 0.6f);
            setParamById(e, t, "lo.m11.src", 6.0f / 63.0f); setParamById(e, t, "lo.m11.dst", 1.0f / 255.0f); setParamById(e, t, "lo.a.m11amt", 0.6f); setParamById(e, t, "lo.b.m11amt", 0.6f);
            setParamById(e, t, "lo.m12.src", 7.0f / 63.0f); setParamById(e, t, "lo.m12.dst", 33.0f / 255.0f); setParamById(e, t, "lo.a.m12amt", 0.6f); setParamById(e, t, "lo.b.m12amt", 0.6f);
            setParamById(e, t, "lo.m13.src", 9.0f / 63.0f); setParamById(e, t, "lo.m13.dst", 29.0f / 255.0f); setParamById(e, t, "lo.a.m13amt", 0.6f); setParamById(e, t, "lo.b.m13amt", 0.6f);
            setParamById(e, t, "lo.m14.src", 18.0f / 63.0f); setParamById(e, t, "lo.m14.dst", 15.0f / 255.0f); setParamById(e, t, "lo.a.m14amt", 0.6f); setParamById(e, t, "lo.b.m14amt", 0.6f);
            setParamById(e, t, "lo.m15.src", 1.0f / 63.0f); setParamById(e, t, "lo.m15.dst", 67.0f / 255.0f); setParamById(e, t, "lo.a.m15amt", 0.6f); setParamById(e, t, "lo.b.m15amt", 0.6f);
            setParamById(e, t, "lo.m16.src", 2.0f / 63.0f); setParamById(e, t, "lo.m16.dst", 34.0f / 255.0f); setParamById(e, t, "lo.a.m16amt", 0.6f); setParamById(e, t, "lo.b.m16amt", 0.6f);
            setParamById(e, t, "lo.m17.src", 3.0f / 63.0f); setParamById(e, t, "lo.m17.dst", 24.0f / 255.0f); setParamById(e, t, "lo.a.m17amt", 0.6f); setParamById(e, t, "lo.b.m17amt", 0.6f);
            setParamById(e, t, "lo.m18.src", 5.0f / 63.0f); setParamById(e, t, "lo.m18.dst", 20.0f / 255.0f); setParamById(e, t, "lo.a.m18amt", 0.6f); setParamById(e, t, "lo.b.m18amt", 0.6f);
            setParamById(e, t, "lo.m19.src", 6.0f / 63.0f); setParamById(e, t, "lo.m19.dst", 8.0f / 255.0f); setParamById(e, t, "lo.a.m19amt", 0.6f); setParamById(e, t, "lo.b.m19amt", 0.6f);
            setParamById(e, t, "lo.m20.src", 2.0f / 63.0f); setParamById(e, t, "lo.m20.dst", 32.0f / 255.0f); setParamById(e, t, "lo.a.m20amt", 0.6f); setParamById(e, t, "lo.b.m20amt", 0.6f);
            setParamById(e, t, "lo.m21.src", 1.0f / 63.0f); setParamById(e, t, "lo.m21.dst", 16.0f / 255.0f); setParamById(e, t, "lo.a.m21amt", 0.6f); setParamById(e, t, "lo.b.m21amt", 0.6f);
            setParamById(e, t, "lo.m22.src", 4.0f / 63.0f); setParamById(e, t, "lo.m22.dst", 25.0f / 255.0f); setParamById(e, t, "lo.a.m22amt", 0.6f); setParamById(e, t, "lo.b.m22amt", 0.6f);
        fillFullChord(e, t, 12);
        return true;
    }
    if (id == "twin-layer-6x6") {
        // Layer with the voices shared 6 + 6: a 6-note chord sounds on both parts (SC-010).
        const int t = instrumentTrack(e, 18);
        if (t < 0) return false;
        for (auto [pid, v] : {std::pair{"mode", 0.5f}, {"share", 0.5f}, {"lo.a.o2level", 0.8f}, {"up.a.o2level", 0.8f},
                 {"lo.a.o1bal", 0.5f}, {"up.a.o1bal", 0.5f}, {"lo.a.route", 0.5f}, {"up.a.route", 0.5f},
                 {"lo.a.modfxon", 1.0f}, {"lo.a.revon", 1.0f}, {"up.a.dlyon", 1.0f}, {"up.a.revon", 1.0f}})
            setParamById(e, t, pid, v);
        fillFullChord(e, t, 6);
        return true;
    }
    if (id == "audio-warp-split" || id == "edit-warp-split") {
        const int t = warpedClipTrack(e);
        if (t < 0) return false;
        if (id == "audio-warp-split")
            for (int i = 1; i < 8; ++i)
                if (nota_clip_split(e, t, i - 1, i * 2.0) < 0) return false;
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
    if (id == "edit-warp-split") { // split the last piece 8 times, then undo them
        constexpr int kEdits = 8;
        // Warm-up, not measured: one split + undo first, so the counted edits don't include the
        // first-call costs (lazy symbol binding, function-local statics). Those depend on the
        // library's size and layout rather than on the edit path, and they made this tiny
        // scenario move by a percent or two when unrelated code was added. The clip is back as
        // it was after the undo.
        nota_clip_split(e, gWarpTrack, 0, 1.0);
        nota_engine_undo(e);
        const auto t0 = std::chrono::steady_clock::now();
        CALLGRIND_START_INSTRUMENTATION;
        for (int i = 0; i < kEdits; ++i) nota_clip_split(e, gWarpTrack, i, 1.5 + i * 1.75);
        for (int i = 0; i < kEdits; ++i) nota_engine_undo(e);
        CALLGRIND_STOP_INSTRUMENTATION;
        const auto t1 = std::chrono::steady_clock::now();
        nota_engine_destroy(e);
        const double ms = std::chrono::duration<double, std::milli>(t1 - t0).count() / (2 * kEdits);
        std::printf("%s\t%s\t%.3f\t%.4f\n", id.c_str(), label.c_str(), ms, 0.0);
        return 0;
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
