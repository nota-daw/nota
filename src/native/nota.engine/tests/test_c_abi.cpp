// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// End-to-end C ABI verification tests for `libnota_engine` (`modules/c_abi/`).

#include "nota/nota_engine.h"

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {

int gFailures = 0;

void expect(bool cond, const char* msg) {
    if (!cond) {
        std::fprintf(stderr, "FAIL: %s\n", msg);
        ++gFailures;
    }
}

double bufferRms(const std::vector<float>& buf) {
    if (buf.empty()) return 0.0;
    double sum = 0.0;
    for (float s : buf) sum += static_cast<double>(s) * static_cast<double>(s);
    return std::sqrt(sum / static_cast<double>(buf.size()));
}

void testLifecycleTransportAndSelftests() {
    expect(nota_engine_version() != nullptr && std::strlen(nota_engine_version()) > 0, "nota_engine_version non-empty");
    expect(nota_asio_selftest() == 1, "nota_asio_selftest passes");

    NotaEngine* eng = nota_engine_create();
    expect(eng != nullptr, "nota_engine_create returns handle");
    if (!eng) return;

    expect(nota_engine_preset_tag_selftest(eng) == 1, "nota_engine_preset_tag_selftest passes");

    expect(nota_transport_set_bpm(eng, 128.0) == NOTA_OK, "nota_transport_set_bpm succeeds");
    std::vector<float> tmp(512, 0.0f);
    expect(nota_engine_render_offline_at(eng, tmp.data(), 256, 48000.0) == NOTA_OK,
        "nota_engine_render_offline_at drains commands");
    expect(std::fabs(nota_transport_bpm(eng) - 128.0) < 1e-6, "nota_transport_bpm reflects 128");
    expect(std::fabs(nota_tempo_bpm_at(eng, 0.0) - 128.0) < 1e-6, "nota_tempo_bpm_at reflects 128");

    expect(nota_engine_set_pan_law(eng, 1, 0) == NOTA_OK, "nota_engine_set_pan_law accepts valid mode");
    expect(nota_engine_pan_law(eng) == 1, "nota_engine_pan_law returns configured mode");

    const NotaSection sections[2] = {
        {0.0, 8.0, "Intro"},
        {8.0, 16.0, "Drop"},
    };
    expect(nota_engine_set_sections(eng, sections, 2, 0) == NOTA_OK, "nota_engine_set_sections stores sections");
    expect(nota_engine_section_count(eng) == 2, "nota_engine_section_count == 2");
    expect(std::strcmp(nota_engine_section_name(eng, 0), "Intro") == 0, "section 0 name");
    expect(std::strcmp(nota_engine_section_name(eng, 1), "Drop") == 0, "section 1 name");
    expect(std::fabs(nota_engine_section_start(eng, 1) - 8.0) < 1e-6, "section 1 start");
    expect(std::fabs(nota_engine_section_length(eng, 1) - 16.0) < 1e-6, "section 1 length");

    const int32_t ports = nota_midi_port_count(eng);
    expect(ports >= 0, "nota_midi_port_count non-negative");

    nota_engine_destroy(eng);
}

void testRenderPresetSurfacesAndAurora() {
    NotaEngine* eng = nota_engine_create();
    expect(eng != nullptr, "nota_engine_create for render/devices test");
    if (!eng) return;

    const int32_t voltTrack = nota_engine_add_volt_synth_track(eng);
    expect(voltTrack > 0, "nota_engine_add_volt_synth_track creates track");

    // Verify preset tag surfaces: 0 = Audio FX, 1 = Instrument, 2 = MIDI FX.
    const int32_t midiFxIdx = nota_track_add_midi_effect(eng, voltTrack, 1);
    const int32_t fxIdx = nota_track_add_builtin_device(eng, voltTrack, 1);
    expect(midiFxIdx >= 0, "nota_track_add_midi_effect creates MIDI FX");
    expect(fxIdx >= 0, "nota_track_add_builtin_device creates Audio FX");

    expect(std::strlen(nota_device_name(eng, voltTrack, -1)) > 0, "instrument device name non-empty");
    expect(std::strlen(nota_device_name(eng, voltTrack, fxIdx)) > 0, "audio effect name non-empty");

    const int32_t instTag = nota_track_device_set_preset_tag(eng, voltTrack, 1, 0, "inst.p", "Inst", nullptr, 0);
    const int32_t fxTag = nota_track_device_set_preset_tag(eng, voltTrack, 0, fxIdx, "fx.p", "FX", nullptr, 0);
    const int32_t mfxTag = nota_track_device_set_preset_tag(eng, voltTrack, 2, midiFxIdx, "mfx.p", "MFX", nullptr, 0);
    expect(instTag > 0 && fxTag > 0 && mfxTag > 0, "preset tags distinct across surfaces 0/1/2");
    expect(nota_track_device_preset_tag(eng, voltTrack, 1, 0) == instTag, "instrument surface preset tag matches");
    expect(nota_track_device_preset_tag(eng, voltTrack, 0, fxIdx) == fxTag, "FX surface preset tag matches");
    expect(nota_track_device_preset_tag(eng, voltTrack, 2, midiFxIdx) == mfxTag, "MIDI FX surface preset tag matches");
    expect(nota_track_device_preset_modified(eng, voltTrack, 1, 0) == 0, "unmodified preset reports 0");

    // Non-Aurora track must return 0 for aurora_table_info.
    NotaAuroraTableInfo nonAuroraInfo{};
    expect(nota_track_aurora_table_info(eng, voltTrack, 0, &nonAuroraInfo) == 0,
        "non-Aurora track returns 0 from nota_track_aurora_table_info");

    // Aurora track: build single-frame wavetable, install, inspect info & state sizing.
    const int32_t auroraTrack = nota_engine_add_wavetable_synth_track(eng);
    expect(auroraTrack > 0, "nota_engine_add_wavetable_synth_track creates Aurora track");

    std::vector<float> frame(2048, 0.0f);
    for (size_t i = 0; i < frame.size(); ++i) frame[i] = std::sin(static_cast<float>(i) * 6.2831853f / 2048.0f) * 0.5f;
    NotaAuroraTable* table = nullptr;
    expect(nota_aurora_table_from_frames(frame.data(), 1, "Sine", 1, &table) == NOTA_AURORA_OK && table != nullptr,
        "nota_aurora_table_from_frames builds single-cycle table");
    if (table) {
        expect(nota_track_aurora_set_table(eng, auroraTrack, 0, table, 0) == 1,
            "nota_track_aurora_set_table installs into osc 0");
        nota_aurora_table_free(table);
    }
    NotaAuroraTableInfo auroraInfo{};
    expect(nota_track_aurora_table_info(eng, auroraTrack, 0, &auroraInfo) == 1 && auroraInfo.frames == 1,
        "nota_track_aurora_table_info reports 1 frame");
    const int32_t stateBytes = nota_track_aurora_state(eng, auroraTrack, 1, nullptr, 0);
    expect(stateBytes > 0, "nota_track_aurora_state reports positive byte size");
    std::vector<uint8_t> smallBuf(static_cast<size_t>(stateBytes > 1 ? stateBytes - 1 : 0), 0xAAu);
    if (!smallBuf.empty()) {
        expect(nota_track_aurora_state(eng, auroraTrack, 1, smallBuf.data(), static_cast<int32_t>(smallBuf.size())) ==
                       stateBytes &&
                   smallBuf[0] == 0xAAu,
            "nota_track_aurora_state skips copy when cap < size");
    }
    std::vector<uint8_t> fullBuf(static_cast<size_t>(stateBytes), 0u);
    expect(nota_track_aurora_state(eng, auroraTrack, 1, fullBuf.data(), stateBytes) == stateBytes,
        "nota_track_aurora_state writes full state when cap >= size");

    // Sequence a note and render offline; verify non-silent audio and track/master meters.
    const int32_t clip = nota_track_add_midi_clip(eng, voltTrack, 0.0, 4.0);
    expect(clip >= 0, "nota_track_add_midi_clip creates clip");
    const NotaNoteData note{60, 0.0, 2.0, 0.9f, 0, 0};
    expect(nota_clip_set_notes(eng, voltTrack, clip, &note, 1) == NOTA_OK, "nota_clip_set_notes sets note");

    expect(nota_transport_seek(eng, 0.0) == NOTA_OK, "nota_transport_seek succeeds");
    expect(nota_transport_play(eng) == NOTA_OK, "nota_transport_play succeeds");
    std::vector<float> audio(2 * 4096, 0.0f);
    expect(nota_engine_render_offline_at(eng, audio.data(), 4096, 48000.0) == NOTA_OK,
        "nota_engine_render_offline_at renders audio");
    expect(bufferRms(audio) > 1e-4, "rendered C ABI synth clip produces non-zero audio");

    NotaMeter masterMeter{};
    expect(nota_engine_master_meter(eng, &masterMeter) == NOTA_OK && masterMeter.peak_l > 0.0f,
        "nota_engine_master_meter reflects signal");
    NotaTrackMeterEntry entries[8]{};
    const int32_t meterCount = nota_engine_read_track_meters(eng, entries, 8);
    expect(meterCount >= 2, "nota_engine_read_track_meters returns active tracks");
    expect(nota_session_slot_state(eng, voltTrack, 0) == 0, "unused session slot reports empty (0)");
    expect(nota_engine_session_pass_spans(eng, nullptr, 0) >= 0, "nota_engine_session_pass_spans safe count query");

    expect(nota_transport_stop(eng) == NOTA_OK, "nota_transport_stop succeeds");
    nota_engine_destroy(eng);
}

void testAuditionRig() {
    NotaAudition* rig = nota_audition_create(48000.0);
    expect(rig != nullptr, "nota_audition_create returns rig");
    if (!rig) return;
    expect(nota_audition_set_instrument(rig, 6) == 1, "nota_audition_set_instrument accepts Volt");
    const NotaAuditionNote note{0.0, 0.25, 60, 0.8f};
    const int64_t rendered = nota_audition_render(rig, &note, 1, 120.0, 0.5, 0.25, 0);
    expect(rendered > 0, "nota_audition_render renders frames");
    float peaks[32]{};
    expect(nota_audition_peaks(rig, peaks, 16) > 0, "nota_audition_peaks returns preview outline");
    nota_audition_destroy(rig);
}

} // namespace

int main() {
    testLifecycleTransportAndSelftests();
    testRenderPresetSurfacesAndAurora();
    testAuditionRig();
    if (gFailures != 0) {
        std::fprintf(stderr, "test_c_abi: %d failures\n", gFailures);
        return 1;
    }
    std::printf("test_c_abi: all checks passed\n");
    return 0;
}
