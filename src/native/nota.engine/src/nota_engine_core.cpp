// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — the managed/native boundary (AR-7): engine-global slice.
// Lifecycle, transport, master mixer, realtime control (notes/recording/poll),
// undo/redo, offline render, meters, audio preview.
// Thin translation layer: validates handles, catches nothing across the line,
// returns integer result codes.

#include "nota_engine_internal.h"

#include <new>
#include <string>

// Injected by CMake from src/native/nota.engine/VERSION.
#ifndef NOTA_VERSION_STRING
#error "NOTA_VERSION_STRING must be defined by the build (see CMakeLists.txt)"
#endif

extern "C" {

const char* nota_engine_version(void) { return NOTA_VERSION_STRING; }

NotaEngine* nota_engine_create(void) {
    return reinterpret_cast<NotaEngine*>(new (std::nothrow) Engine());
}

void nota_engine_destroy(NotaEngine* engine) {
    delete reinterpret_cast<Engine*>(engine);
}

NotaResult nota_engine_start(NotaEngine* engine) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    return reinterpret_cast<Engine*>(engine)->start() ? NOTA_OK : NOTA_ERR_AUDIO_DEVICE;
}

NotaResult nota_engine_stop(NotaEngine* engine) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    reinterpret_cast<Engine*>(engine)->stop();
    return NOTA_OK;
}

NotaResult nota_engine_set_test_tone(NotaEngine* engine, int32_t enabled) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    reinterpret_cast<Engine*>(engine)->setToneEnabled(enabled != 0);
    return NOTA_OK;
}

NotaResult nota_engine_set_frequency(NotaEngine* engine, float hz) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    if (!(hz > 0.0f) || hz > 20000.0f) return NOTA_ERR_INVALID_ARG;
    reinterpret_cast<Engine*>(engine)->setFrequency(hz);
    return NOTA_OK;
}

double nota_engine_sample_rate(const NotaEngine* engine) {
    if (!engine) return 0.0;
    return reinterpret_cast<const Engine*>(engine)->sampleRate();
}

// ---- Audio preview / audition (M7-4a) -------------------------------------

NotaResult nota_engine_preview_file(NotaEngine* e, const char* path) {
    if (!e || !path) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->previewFile(std::string(path)) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
NotaResult nota_engine_stop_preview(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->stopPreview();
    return NOTA_OK;
}
int32_t nota_engine_preview_active(const NotaEngine* e) {
    return (e && CENG(e)->isPreviewActive()) ? 1 : 0;
}
int32_t nota_engine_preview_selftest(NotaEngine* e) {
    return (e && ENG(e)->previewSelfTest()) ? 1 : 0;
}

int32_t nota_engine_xrun_count(const NotaEngine* e) {
    return e ? CENG(e)->xrunCount() : 0;
}
int32_t nota_engine_xrun_selftest(NotaEngine* e) {
    return (e && ENG(e)->xrunSelfTest()) ? 1 : 0;
}
float nota_engine_cpu_load(const NotaEngine* e) {
    return e ? CENG(e)->cpuLoad() : 0.0f;
}

// ---- Transport ------------------------------------------------------------

NotaResult nota_transport_play(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->transportPlay(); return NOTA_OK;
}
NotaResult nota_transport_stop(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->transportStop(); return NOTA_OK;
}
NotaResult nota_transport_set_bpm(NotaEngine* e, double bpm) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (!(bpm > 0.0) || bpm > 999.0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setBpm(bpm); return NOTA_OK;
}
double nota_transport_bpm(const NotaEngine* e) { return e ? CENG(e)->bpm() : 120.0; }
NotaResult nota_transport_set_time_signature(NotaEngine* e, int32_t num, int32_t denom) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (num <= 0 || denom <= 0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setTimeSignature(num, denom); return NOTA_OK;
}
NotaResult nota_transport_set_loop(NotaEngine* e, int32_t enabled, double s, double en) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setLoop(enabled != 0, s, en); return NOTA_OK;
}
NotaResult nota_transport_set_metronome(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setMetronome(enabled != 0); return NOTA_OK;
}
NotaResult nota_transport_seek(NotaEngine* e, double beat) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->seekBeats(beat); return NOTA_OK;
}
double nota_transport_position_beats(const NotaEngine* e) {
    return e ? CENG(e)->positionBeats() : 0.0;
}
int32_t nota_transport_is_playing(const NotaEngine* e) {
    return (e && CENG(e)->isPlaying()) ? 1 : 0;
}
int32_t nota_transport_loop_enabled(const NotaEngine* e) {
    return (e && CENG(e)->loopEnabled()) ? 1 : 0;
}
double nota_transport_loop_start(const NotaEngine* e) {
    return e ? CENG(e)->loopStart() : 0.0;
}
double nota_transport_loop_end(const NotaEngine* e) {
    return e ? CENG(e)->loopEnd() : 0.0;
}

// ---- Mixer ----------------------------------------------------------------

NotaResult nota_engine_set_master_volume(NotaEngine* e, float v) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (v < 0.0f || v > 4.0f) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setMasterVolume(v); return NOTA_OK;
}

// ---- Live MIDI, arming & recording (M2) -----------------------------------

NotaResult nota_engine_note_on(NotaEngine* e, int32_t pitch, float velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->noteOn(pitch, velocity); return NOTA_OK;
}
NotaResult nota_engine_note_off(NotaEngine* e, int32_t pitch) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->noteOff(pitch); return NOTA_OK;
}
void nota_engine_set_audition_track(NotaEngine* e, int32_t track_id) {
    if (e) ENG(e)->setAuditionTrack(track_id);
}
NotaResult nota_engine_set_recording(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setRecording(enabled != 0); return NOTA_OK;
}
int32_t nota_engine_is_recording(const NotaEngine* e) {
    return (e && CENG(e)->isRecording()) ? 1 : 0;
}
int32_t nota_engine_record_start_status(const NotaEngine* e) {
    return e ? CENG(e)->recordStartStatus() : 0;
}
int32_t nota_engine_audio_record_track(const NotaEngine* e) {
    return e ? CENG(e)->audioRecordTrackId() : 0;
}
double nota_engine_audio_record_start_beat(const NotaEngine* e) {
    return e ? CENG(e)->audioRecordStartBeat() : 0.0;
}
double nota_engine_audio_record_length_beats(const NotaEngine* e) {
    return e ? CENG(e)->audioRecordLengthBeats() : 0.0;
}
int32_t nota_engine_master_track_id(const NotaEngine* e) {
    return e ? CENG(e)->masterTrackId() : 0;
}
int32_t nota_engine_audio_record_peaks(const NotaEngine* e, float* out, int32_t maxPoints) {
    return e ? CENG(e)->audioRecordPeaks(out, maxPoints) : 0;
}
int32_t nota_audio_record_selftest(NotaEngine* e) {
    return (e && ENG(e)->audioRecordSelfTest()) ? 1 : 0;
}
NotaResult nota_engine_poll(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->poll(); return NOTA_OK;
}

// ---- Undo / redo (M6-6) ---------------------------------------------------

NotaResult nota_engine_undo(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->undo() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
NotaResult nota_engine_redo(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->redo() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
int32_t nota_engine_can_undo(const NotaEngine* e) {
    return e ? (CENG(e)->canUndo() ? 1 : 0) : 0;
}
int32_t nota_engine_can_redo(const NotaEngine* e) {
    return e ? (CENG(e)->canRedo() ? 1 : 0) : 0;
}

// ---- Project reset (M7-6) -------------------------------------------------

void nota_engine_reset(NotaEngine* e) {
    if (e) ENG(e)->reset();
}

// ---- Meters (M6-2) --------------------------------------------------------

int32_t nota_engine_track_meter(const NotaEngine* e, int32_t track_id, NotaMeter* out) {
    if (!e || !out) return 0;
    return CENG(e)->trackMeter(track_id, out->peak_l, out->peak_r, out->rms_l, out->rms_r) ? 1 : 0;
}
NotaResult nota_engine_master_meter(const NotaEngine* e, NotaMeter* out) {
    if (!e || !out) return NOTA_ERR_INVALID_ARG;
    CENG(e)->masterMeter(out->peak_l, out->peak_r, out->rms_l, out->rms_r);
    return NOTA_OK;
}

// ---- Offline render -------------------------------------------------------

NotaResult nota_engine_render_offline(NotaEngine* e, float* out, int32_t frames) {
    if (!e || !out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->renderOffline(out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_offline_at(NotaEngine* e, float* out, int32_t frames, double sr) {
    if (!e || !out || frames <= 0 || sr <= 0.0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->renderOffline(out, frames, sr);
    return NOTA_OK;
}

} // extern "C"
