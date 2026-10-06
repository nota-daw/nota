// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — Session view (M5): scenes/slots, launch/stop/record, quantization,
// session<->arrangement transfer, session notes, and session audio slots (M7-6b).

#include "nota_engine_internal.h"

#include <algorithm>
#include <cstring>
#include <string>

extern "C" {

int32_t nota_session_scene_count(const NotaEngine* e) {
    return e ? CENG(e)->sceneCount() : 0;
}
NotaResult nota_session_add_midi_clip(NotaEngine* e, int32_t track_id, int32_t scene, double length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->addSessionMidiClip(track_id, scene, length_beats) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
double nota_session_slot_length(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? CENG(e)->sessionSlotLength(track_id, scene) : 0.0;
}
NotaResult nota_session_set_slot_length(NotaEngine* e, int32_t track_id, int32_t scene, double length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSessionSlotLength(track_id, scene, length_beats) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_slot_state(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? CENG(e)->sessionSlotState(track_id, scene) : 0;
}
float nota_session_slot_gain(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? CENG(e)->sessionSlotGain(track_id, scene) : 1.0f;
}
NotaResult nota_session_set_slot_gain(NotaEngine* e, int32_t track_id, int32_t scene, float gain) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSessionSlotGain(track_id, scene, gain) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_session_set_launch_quant(NotaEngine* e, double beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setLaunchQuant(beats);
    return NOTA_OK;
}
NotaResult nota_session_launch_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->launchSlot(track_id, scene);
    return NOTA_OK;
}
NotaResult nota_session_stop_slot(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->stopSlot(track_id);
    return NOTA_OK;
}
NotaResult nota_session_launch_scene(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->launchScene(scene);
    return NOTA_OK;
}
NotaResult nota_session_stop_scene(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->stopScene(scene);
    return NOTA_OK;
}
NotaResult nota_session_stop_all(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->stopAllSession();
    return NOTA_OK;
}
NotaResult nota_session_back_to_arrangement(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->backToArrangement();
    return NOTA_OK;
}
int32_t nota_arrangement_active(const NotaEngine* e) {
    return (e && CENG(e)->arrangementActive()) ? 1 : 0;
}
NotaResult nota_session_clear_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->clearSessionSlot(track_id, scene) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_add_scene(NotaEngine* e) {
    return e ? ENG(e)->addScene() : -1;
}
NotaResult nota_session_remove_scene(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->removeScene(scene) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_session_record_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->recordSessionSlot(track_id, scene);
    return NOTA_OK;
}
NotaResult nota_session_stop_record(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->stopSessionRecord();
    return NOTA_OK;
}
int32_t nota_session_slot_to_arrangement(NotaEngine* e, int32_t track_id, int32_t scene, double start_beat) {
    return e ? ENG(e)->sessionSlotToArrangement(track_id, scene, start_beat) : -1;
}
NotaResult nota_session_from_arrangement(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->arrangementClipToSession(track_id, clip_index, scene) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_session_audio_from_arrangement(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->arrangementAudioClipToSession(track_id, clip_index, scene) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_audio_record_selftest(NotaEngine* e) {
    return (e && ENG(e)->sessionAudioRecordSelfTest()) ? 1 : 0;
}
NotaResult nota_session_set_notes(NotaEngine* e, int32_t track_id, int32_t scene,
                                  const NotaNoteData* notes, int32_t count) {
    if (!e || (count > 0 && !notes) || count < 0) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSessionNotes(track_id, scene, notes, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_get_notes(const NotaEngine* e, int32_t track_id, int32_t scene,
                               NotaNoteData* out, int32_t max_notes) {
    if (!e || !out || max_notes <= 0) return 0;
    return CENG(e)->getSessionNotes(track_id, scene, out, max_notes);
}
int32_t nota_session_note_count(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? CENG(e)->sessionNoteCount(track_id, scene) : 0;
}

// ---- Session audio slots (M7-6b) ------------------------------------------

int32_t nota_session_get_audio_slot(const NotaEngine* e, int32_t track_id, int32_t scene, NotaSessionAudioSlot* out) {
    return (e && CENG(e)->sessionAudioSlotInfo(track_id, scene, out)) ? 1 : 0;
}
int32_t nota_session_add_audio_clip(NotaEngine* e, int32_t track_id, int32_t scene, const char* path,
                                    double length_beats, double source_offset_frames,
                                    int64_t length_frames, float gain) {
    if (!e || !path) return 0;
    return ENG(e)->addSessionAudioClip(track_id, scene, std::string(path), length_beats,
                                       source_offset_frames, length_frames, gain) ? 1 : 0;
}
int32_t nota_session_add_audio_file(NotaEngine* e, int32_t track_id, int32_t scene, const char* path) {
    if (!e || !path) return 0;
    return ENG(e)->addSessionAudioFile(track_id, scene, std::string(path)) ? 1 : 0;
}

// ---- Session P0: clip + scene properties, scene/slot editing, follow ------

static int32_t copyOut(const std::string& v, char* out, int32_t cap) {
    if (out && cap > 0) {
        const int32_t n = std::min<int32_t>(cap - 1, static_cast<int32_t>(v.size()));
        std::memcpy(out, v.data(), static_cast<size_t>(n));
        out[n] = '\0';
    }
    return static_cast<int32_t>(v.size());
}

int32_t nota_session_get_clip_props(const NotaEngine* e, int32_t track_id, int32_t scene, NotaSessionClipProps* out) {
    return (e && out && CENG(e)->sessionClipProps(track_id, scene, out)) ? 1 : 0;
}
NotaResult nota_session_set_clip_props(NotaEngine* e, int32_t track_id, int32_t scene, const NotaSessionClipProps* p) {
    if (!e || !p) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSessionClipProps(track_id, scene, *p) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_get_clip_name(const NotaEngine* e, int32_t track_id, int32_t scene, char* out, int32_t cap) {
    std::string v;
    if (!e || !CENG(e)->sessionClipName(track_id, scene, v)) { if (out && cap > 0) out[0] = '\0'; return 0; }
    return copyOut(v, out, cap);
}
NotaResult nota_session_set_clip_name(NotaEngine* e, int32_t track_id, int32_t scene, const char* name) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSessionClipName(track_id, scene, name ? std::string(name) : std::string()) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_get_slot_stop_button(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return (!e || CENG(e)->sessionSlotStopButton(track_id, scene)) ? 1 : 0;
}
NotaResult nota_session_set_slot_stop_button(NotaEngine* e, int32_t track_id, int32_t scene, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSessionSlotStopButton(track_id, scene, on != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_session_copy_slot(NotaEngine* e, int32_t st, int32_t ss, int32_t dt, int32_t ds) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->copySessionSlot(st, ss, dt, ds) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_get_scene_props(const NotaEngine* e, int32_t scene, NotaSceneProps* out) {
    return (e && out && CENG(e)->sceneProps(scene, out)) ? 1 : 0;
}
NotaResult nota_session_set_scene_props(NotaEngine* e, int32_t scene, const NotaSceneProps* p) {
    if (!e || !p) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSceneProps(scene, *p) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_get_scene_name(const NotaEngine* e, int32_t scene, char* out, int32_t cap) {
    std::string v;
    if (!e || !CENG(e)->sceneName(scene, v)) { if (out && cap > 0) out[0] = '\0'; return 0; }
    return copyOut(v, out, cap);
}
NotaResult nota_session_set_scene_name(NotaEngine* e, int32_t scene, const char* name) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setSceneName(scene, name ? std::string(name) : std::string()) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_session_insert_scene(NotaEngine* e, int32_t at) { return e ? ENG(e)->insertScene(at) : -1; }
int32_t nota_session_duplicate_scene(NotaEngine* e, int32_t scene) { return e ? ENG(e)->duplicateScene(scene) : -1; }
int32_t nota_session_capture_scene(NotaEngine* e, int32_t at) { return e ? ENG(e)->captureScene(at) : -1; }
NotaResult nota_session_launch_slot_vel(NotaEngine* e, int32_t track_id, int32_t scene, float velocity) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->launchSlotVel(track_id, scene, velocity);
    return NOTA_OK;
}
NotaResult nota_session_release_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->releaseSlot(track_id, scene);
    return NOTA_OK;
}
NotaResult nota_session_track_back_to_arrangement(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->trackBackToArrangement(track_id);
    return NOTA_OK;
}
int32_t nota_session_playing_slot(const NotaEngine* e, int32_t track_id) { return e ? CENG(e)->sessionPlayingSlot(track_id) : -1; }
double nota_session_slot_position(const NotaEngine* e, int32_t track_id) { return e ? CENG(e)->sessionSlotPosition(track_id) : 0.0; }
double nota_session_get_launch_quant(const NotaEngine* e) { return e ? CENG(e)->launchQuant() : 0.0; }
NotaResult nota_session_set_follow(NotaEngine* e, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setSessionFollow(on != 0);
    return NOTA_OK;
}
int32_t nota_session_get_follow(const NotaEngine* e) { return (e && CENG(e)->sessionFollow()) ? 1 : 0; }
NotaResult nota_session_set_record_length(NotaEngine* e, double beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setSessionRecordLength(beats);
    return NOTA_OK;
}
double nota_session_get_record_length(const NotaEngine* e) { return e ? CENG(e)->sessionRecordLength() : 0.0; }
int32_t nota_session_record_scene(NotaEngine* e, int32_t scene) { return e ? ENG(e)->recordSessionScene(scene) : 0; }
int32_t nota_session_record_target(const NotaEngine* e, int32_t* track_id, int32_t* scene, double* elapsed) {
    return (e && CENG(e)->sessionRecordTarget(track_id, scene, elapsed)) ? 1 : 0;
}

int32_t nota_session_slot_peaks(const NotaEngine* e, int32_t track_id, int32_t scene, float* out_min_max, int32_t max_points) {
    return e ? CENG(e)->sessionSlotPeaks(track_id, scene, out_min_max, max_points) : 0;
}

NotaResult nota_session_move_scene(NotaEngine* e, int32_t from, int32_t to) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->moveScene(from, to) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_take_scene_tempo(NotaEngine* e, double* bpm, int32_t* num, int32_t* den) {
    return (e && ENG(e)->takeSceneTempoChange(bpm, num, den)) ? 1 : 0;
}

} // extern "C"
