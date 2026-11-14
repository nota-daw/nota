// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — the managed/native boundary (AR-7): engine-global slice.
// Lifecycle, transport, master mixer, realtime control (notes/recording/poll),
// undo/redo, offline render, meters, audio preview.
// Thin translation layer: validates handles, catches nothing across the line,
// returns integer result codes.

#include "nota_engine_internal.h"

#include <cmath>
#include <limits>
#include <new>
#include <string>

#include "audio_io/NotaConfigFile.h"

// Injected by CMake from src/native/nota.engine/VERSION.
#ifndef NOTA_VERSION_STRING
#error "NOTA_VERSION_STRING must be defined by the build (see CMakeLists.txt)"
#endif

extern "C" {

const char* nota_engine_version(void) {
    return NOTA_VERSION_STRING;
}

NotaEngine* nota_engine_create(void) {
    return reinterpret_cast<NotaEngine*>(new (std::nothrow) Engine());
}

void nota_engine_destroy(NotaEngine* engine) {
    delete reinterpret_cast<Engine*>(engine);
}

NotaResult nota_set_data_dir(const char* path_utf8) {
    // NULL / "" clears the override (persistence off unless NOTA_DATA_DIR is set).
    // Called once at startup from the managed side; never on
    // the audio thread (AR-6: create_directories does file I/O).
    if (!path_utf8 || !*path_utf8) {
        nota::cfgfile::setDataDirOverride({});
        return NOTA_OK;
    }
    nota::cfgfile::setDataDirOverride(std::string(path_utf8));
    return NOTA_OK;
}

const char* nota_get_data_dir(void) {
    static std::string s; // owned by the engine, valid until the next call
    s = nota::cfgfile::dataDir();
    return s.c_str();
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

int32_t nota_engine_is_running(const NotaEngine* engine) {
    if (!engine) return 0;
    return reinterpret_cast<const Engine*>(engine)->isRunning() ? 1 : 0;
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
NotaResult nota_engine_preview_file_at(NotaEngine* e, const char* path, double start_seconds) {
    if (!e || !path) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->previewFile(std::string(path), start_seconds) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
NotaResult nota_engine_set_preview_loop(NotaEngine* e, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setPreviewLoop(on != 0);
    return NOTA_OK;
}
NotaResult nota_engine_set_preview_gain(NotaEngine* e, float gain) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setPreviewGain(gain);
    return NOTA_OK;
}
double nota_engine_preview_position(const NotaEngine* e) {
    return e ? CENG(e)->previewPosition() : 0.0;
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
int32_t nota_engine_graph_retire_selftest(NotaEngine* e) {
    return (e && ENG(e)->graphRetireSelfTest()) ? 1 : 0;
}
float nota_engine_cpu_load(const NotaEngine* e) {
    return e ? CENG(e)->cpuLoad() : 0.0f;
}
NotaResult nota_engine_set_render_threads(NotaEngine* e, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setRenderThreads(count) ? NOTA_OK : NOTA_ERR_ALREADY_RUNNING;
}
int32_t nota_engine_render_threads(const NotaEngine* e) {
    return e ? CENG(e)->renderThreads() : 0;
}
double nota_engine_render_threads_cpu_seconds(const NotaEngine* e) {
    return e ? CENG(e)->renderThreadsCpuSeconds() : 0.0;
}

// ---- Transport ------------------------------------------------------------

NotaResult nota_transport_play(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->transportPlay();
    return NOTA_OK;
}
NotaResult nota_transport_play_count_in(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->transportPlayCountIn();
    return NOTA_OK;
}
NotaResult nota_transport_set_count_in(NotaEngine* e, int32_t bars) {
    if (!e || bars < 0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setCountInBars(bars);
    return NOTA_OK;
}
double nota_transport_count_in_beats(const NotaEngine* e) {
    return e ? CENG(e)->countInBeatsRemaining() : 0.0;
}
NotaResult nota_transport_set_loop_record_mode(NotaEngine* e, int32_t mode) {
    if (!e || mode < 0 || mode > 1) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setLoopRecordReplace(mode == 1);
    return NOTA_OK;
}
int32_t nota_transport_loop_record_mode(const NotaEngine* e) {
    return e && CENG(e)->loopRecordReplace() ? 1 : 0;
}
NotaResult nota_transport_stop(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->transportStop();
    return NOTA_OK;
}
NotaResult nota_transport_set_bpm(NotaEngine* e, double bpm) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (!(bpm > 0.0) || bpm > 999.0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setBpm(bpm);
    return NOTA_OK;
}
double nota_transport_bpm(const NotaEngine* e) {
    return e ? CENG(e)->bpm() : 120.0;
}
int32_t nota_tempo_point_count(const NotaEngine* e) {
    return e && CENG(e)->tempoMap() ? static_cast<int32_t>(CENG(e)->tempoMap()->points.size()) : 0;
}
NotaResult nota_tempo_get_point(const NotaEngine* e, int32_t index, NotaTempoPoint* p) {
    if (!e || !p || index < 0 || index >= nota_tempo_point_count(e)) return NOTA_ERR_INVALID_ARG;
    *p = CENG(e)->tempoMap()->points[index];
    return NOTA_OK;
}
NotaResult nota_tempo_set_points(NotaEngine* e, const NotaTempoPoint* p, int32_t count, int32_t checkpoint) {
    if (!e || count < 0 || count > 4096 || (count && !p)) return NOTA_ERR_INVALID_ARG;
    std::vector<NotaTempoPoint> points;
    if (count) points.assign(p, p + count);
    return ENG(e)->setTempoPoints(std::move(points), checkpoint != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
double nota_tempo_beat_to_seconds(const NotaEngine* e, double b) { return e ? CENG(e)->beatToSeconds(b) : 0; }
double nota_tempo_seconds_to_beat(const NotaEngine* e, double s) { return e ? CENG(e)->secondsToBeat(s) : 0; }
double nota_tempo_bpm_at(const NotaEngine* e, double b) {
    return e ? (CENG(e)->tempoMap() ? CENG(e)->tempoMap()->bpmAt(b) : CENG(e)->bpm()) : 120;
}
NotaTempoPreview* nota_tempo_preview_create(const NotaTempoPoint* p, int32_t count) {
    if (!p || count < 1 || count > 4096) return nullptr;
    std::vector<NotaTempoPoint> points(p, p + count);
    if (!nota::TempoMap::valid(points)) return nullptr;
    return reinterpret_cast<NotaTempoPreview*>(new nota::TempoMap(std::move(points)));
}
void nota_tempo_preview_destroy(NotaTempoPreview* p) { delete reinterpret_cast<nota::TempoMap*>(p); }
double nota_tempo_preview_seconds(const NotaTempoPreview* p, double b) {
    return p ? reinterpret_cast<const nota::TempoMap*>(p)->secondsAt(b) : 0;
}
double nota_tempo_preview_beat(const NotaTempoPreview* p, double s) {
    return p ? reinterpret_cast<const nota::TempoMap*>(p)->beatAt(s) : 0;
}
double nota_tempo_preview_bpm(const NotaTempoPreview* p, double b) {
    return p ? reinterpret_cast<const nota::TempoMap*>(p)->bpmAt(b) : 120;
}
NotaResult nota_transport_set_time_signature(NotaEngine* e, int32_t num, int32_t denom) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (num <= 0 || denom <= 0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setTimeSignature(num, denom);
    return NOTA_OK;
}
NotaResult nota_transport_set_loop(NotaEngine* e, int32_t enabled, double s, double en) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setLoop(enabled != 0, s, en);
    return NOTA_OK;
}
NotaResult nota_transport_set_metronome(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setMetronome(enabled != 0);
    return NOTA_OK;
}
NotaResult nota_transport_seek(NotaEngine* e, double beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->seekBeats(beat);
    return NOTA_OK;
}
double nota_transport_position_beats(const NotaEngine* e) {
    return e ? CENG(e)->positionBeats() : 0.0;
}
int32_t nota_transport_snapshot(const NotaEngine* e, NotaTransportSnapshot* out_snapshot) {
    return e && out_snapshot && CENG(e)->transportSnapshot(*out_snapshot) ? 1 : 0;
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
    ENG(e)->setMasterVolume(v);
    return NOTA_OK;
}

// ---- Live MIDI, arming & recording (M2) -----------------------------------

NotaResult nota_engine_note_on(NotaEngine* e, int32_t pitch, float velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->noteOn(pitch, velocity);
    return NOTA_OK;
}
NotaResult nota_engine_note_off(NotaEngine* e, int32_t pitch) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->noteOff(pitch);
    return NOTA_OK;
}
NotaResult nota_engine_note_off_velocity(NotaEngine* e, int32_t pitch, float release_velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->noteOff(pitch, release_velocity);
    return NOTA_OK;
}
NotaResult nota_track_note_on(NotaEngine* e, int32_t track_id, int32_t pitch, float velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->trackNoteOn(track_id, pitch, velocity);
    return NOTA_OK;
}
NotaResult nota_track_note_off(NotaEngine* e, int32_t track_id, int32_t pitch) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->trackNoteOff(track_id, pitch);
    return NOTA_OK;
}
NotaResult nota_track_note_off_velocity(NotaEngine* e, int32_t track_id, int32_t pitch, float release_velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    ENG(e)->trackNoteOff(track_id, pitch, release_velocity);
    return NOTA_OK;
}
NotaResult nota_engine_note_expression(NotaEngine* e, int32_t pitch, int32_t dim, float value) {
    if (!e || pitch < -1 || pitch > 127 || dim < 0 || dim > 3 || (dim == 3 && pitch >= 0)) return NOTA_ERR_INVALID_ARG;
    ENG(e)->noteExpression(pitch, dim, value);
    return NOTA_OK;
}
NotaResult nota_track_note_expression(NotaEngine* e, int32_t track_id, int32_t pitch, int32_t dim, float value) {
    if (!e || pitch < -1 || pitch > 127 || dim < 0 || dim > 3 || (dim == 3 && pitch >= 0)) return NOTA_ERR_INVALID_ARG;
    ENG(e)->trackNoteExpression(track_id, pitch, dim, value);
    return NOTA_OK;
}
NotaResult nota_engine_controller(NotaEngine* e, int32_t ctl, float value) {
    if (!e || nota::ctlIsReserved(ctl) || !std::isfinite(value)) return NOTA_ERR_INVALID_ARG;
    ENG(e)->controller(ctl, value);
    return NOTA_OK;
}
NotaResult nota_track_controller(NotaEngine* e, int32_t track_id, int32_t ctl, float value) {
    if (!e || nota::ctlIsReserved(ctl) || !std::isfinite(value)) return NOTA_ERR_INVALID_ARG;
    ENG(e)->trackController(track_id, ctl, value);
    return NOTA_OK;
}
float nota_track_delivered_controller(const NotaEngine* e, int32_t track_id, int32_t ctl) {
    return e ? CENG(e)->deliveredController(track_id, ctl) : std::numeric_limits<float>::quiet_NaN();
}
int32_t nota_engine_midi_ctl_selftest(NotaEngine* e) {
    return e ? ENG(e)->midiCtlSelfTest() : -1;
}
void nota_engine_set_audition_track(NotaEngine* e, int32_t track_id) {
    if (e) ENG(e)->setAuditionTrack(track_id);
}
NotaResult nota_engine_set_recording(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setRecording(enabled != 0);
    return NOTA_OK;
}
int32_t nota_engine_is_recording(const NotaEngine* e) {
    return (e && CENG(e)->isRecording()) ? 1 : 0;
}
int32_t nota_engine_retry_take(NotaEngine* e) {
    return e ? ENG(e)->retryTake() : 0;
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
int32_t nota_engine_recording_tracks(const NotaEngine* e, int32_t* out, int32_t cap) {
    return e ? CENG(e)->audioRecordingTracks(out, cap) : 0;
}
double nota_engine_audio_record_length_beats_of(const NotaEngine* e, int32_t track_id) {
    return e && track_id > 0 ? CENG(e)->audioRecordLengthBeats(track_id) : 0.0;
}
int32_t nota_engine_audio_record_peaks_of(const NotaEngine* e, int32_t track_id, float* out, int32_t maxPoints) {
    return e && track_id > 0 ? CENG(e)->audioRecordPeaks(out, maxPoints, track_id) : 0;
}
int32_t nota_engine_record_skipped(const NotaEngine* e, int32_t* track_ids, int32_t* reasons, int32_t cap) {
    return e ? CENG(e)->recordSkipped(track_ids, reasons, cap) : 0;
}
int32_t nota_audio_record_selftest(NotaEngine* e) {
    return (e && ENG(e)->audioRecordSelfTest()) ? 1 : 0;
}
NotaResult nota_engine_poll(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->poll();
    return NOTA_OK;
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
void nota_engine_undo_group_begin(NotaEngine* e) {
    if (e) ENG(e)->beginUndoGroup();
}
void nota_engine_undo_group_end(NotaEngine* e) {
    if (e) ENG(e)->endUndoGroup();
}
void nota_engine_clear_undo_history(NotaEngine* e) {
    if (e) ENG(e)->clearUndoHistory();
}

// ---- Value undo (009, 016) -------------------------------------------------

int32_t nota_mix_gesture_begin(NotaEngine* e, const NotaMixTarget* targets, int32_t count) {
    if (!e || !targets || count <= 0 || count > 64) return 0;
    nota::MixTarget t[64];
    int32_t n = 0;
    for (int32_t i = 0; i < count; ++i) {
        const int32_t k = targets[i].kind;
        if ((k < NOTA_MIX_TRACK_VOLUME || k > NOTA_MIX_PRESET_TAG) && k != NOTA_MIX_TRACK_SEND) continue; // 7 is internal
        t[n++] = {static_cast<nota::MixKind>(k), targets[i].track_id, targets[i].device_index, targets[i].param_index};
    }
    return n > 0 ? ENG(e)->beginMixGesture(t, n) : 0;
}
void nota_mix_gesture_end(NotaEngine* e, int32_t handle) {
    if (e) ENG(e)->endMixGesture(handle);
}
float nota_engine_master_volume(const NotaEngine* e) {
    return e ? CENG(e)->masterVolume() : 1.0f;
}
int32_t nota_engine_mix_undo_selftest(NotaEngine* e) {
    return (e && ENG(e)->mixUndoSelfTest()) ? 1 : 0;
}

// ---- Stereo measurements (008) ---------------------------------------------

int32_t nota_engine_stereo_measures(const NotaEngine* e, NotaStereoMeasure* out, int32_t cap) {
    return e ? CENG(e)->stereoMeasures(out, cap < 0 ? 0 : cap) : 0;
}

// ---- Pan law (006) ---------------------------------------------------------

NotaResult nota_engine_set_pan_law(NotaEngine* e, int32_t law, int32_t checkpoint) {
    if (!e || (law != NOTA_PAN_LAW_BALANCE && law != NOTA_PAN_LAW_STEREO)) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setPanLaw(static_cast<nota::PanLaw>(law), checkpoint != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_engine_pan_law(const NotaEngine* e) {
    return e ? static_cast<int32_t>(CENG(e)->panLaw()) : NOTA_PAN_LAW_BALANCE;
}

// ---- Arrangement sections (016) ----------------------------------------------

int32_t nota_engine_section_count(const NotaEngine* e) {
    return e ? static_cast<int32_t>(CENG(e)->sections().size()) : 0;
}
static const nota::Section* sectionAt(const NotaEngine* e, int32_t i) {
    if (!e) return nullptr;
    const auto& v = CENG(e)->sections();
    return (i >= 0 && i < static_cast<int32_t>(v.size())) ? &v[static_cast<size_t>(i)] : nullptr;
}
double nota_engine_section_start(const NotaEngine* e, int32_t i) {
    auto* s = sectionAt(e, i);
    return s ? s->startBeat : 0.0;
}
double nota_engine_section_length(const NotaEngine* e, int32_t i) {
    auto* s = sectionAt(e, i);
    return s ? s->lengthBeats : 0.0;
}
const char* nota_engine_section_name(const NotaEngine* e, int32_t i) {
    auto* s = sectionAt(e, i);
    return s ? s->name.c_str() : "";
}
NotaResult nota_engine_set_sections(NotaEngine* e, const NotaSection* sections, int32_t count, int32_t checkpoint) {
    if (!e || count < 0 || (count > 0 && !sections)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::Section> v;
    v.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i)
        v.push_back(
            {sections[i].start_beat, sections[i].length_beats, sections[i].name_utf8 ? sections[i].name_utf8 : ""});
    ENG(e)->setSections(std::move(v), checkpoint != 0);
    return NOTA_OK;
}
NotaResult nota_section_move(NotaEngine* e, int32_t index, int32_t to_boundary) {
    return e && ENG(e)->moveSection(index, to_boundary) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_section_duplicate_with_content(NotaEngine* e, int32_t index) {
    return e && ENG(e)->duplicateSectionWithContent(index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_section_delete_with_content(NotaEngine* e, int32_t index) {
    return e && ENG(e)->deleteSectionWithContent(index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_section_overlaps(const NotaEngine* e, int32_t index) {
    return e && CENG(e)->sectionOverlaps(index) ? 1 : 0;
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

// ---- Cue (specs/089) --------------------------------------------------------

NotaResult nota_engine_set_cue_volume(NotaEngine* e, float gain) {
    if (!e || !std::isfinite(gain)) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setCueVolume(gain);
    return NOTA_OK;
}
float nota_engine_cue_volume(const NotaEngine* e) {
    return e ? CENG(e)->cueVolume() : 0.0f;
}
NotaResult nota_engine_cue_meter(const NotaEngine* e, NotaMeter* out) {
    if (!e || !out) return NOTA_ERR_INVALID_ARG;
    CENG(e)->cueMeter(out->peak_l, out->peak_r, out->rms_l, out->rms_r);
    return NOTA_OK;
}
NotaResult nota_transport_set_click_destination(NotaEngine* e, int32_t dest) {
    if (!e || dest < 0 || dest > 2) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setClickDestination(dest);
    return NOTA_OK;
}
int32_t nota_transport_click_destination(const NotaEngine* e) {
    return e ? CENG(e)->clickDestination() : 0;
}
NotaResult nota_transport_set_click_volume(NotaEngine* e, float gain) {
    if (!e || !std::isfinite(gain)) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setClickVolume(gain);
    return NOTA_OK;
}

// ---- Offline render -------------------------------------------------------

NotaResult nota_engine_render_offline(NotaEngine* e, float* out, int32_t frames) {
    if (!e || !out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->renderOffline(out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_device_block_cue(NotaEngine* e, float* main_out, float* cue_out, int32_t frames) {
    if (!e || !main_out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    if (ENG(e)->isRunning()) return NOTA_ERR_ALREADY_RUNNING;
    ENG(e)->renderDeviceBlock(main_out, cue_out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_device_block(NotaEngine* e, float* out, int32_t frames) {
    if (!e || !out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    if (ENG(e)->isRunning()) return NOTA_ERR_ALREADY_RUNNING; // the device renders itself
    ENG(e)->renderDeviceBlock(out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_offline_at(NotaEngine* e, float* out, int32_t frames, double sr) {
    if (!e || !out || frames <= 0 || sr <= 0.0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->renderOffline(out, frames, sr);
    return NOTA_OK;
}

} // extern "C"
