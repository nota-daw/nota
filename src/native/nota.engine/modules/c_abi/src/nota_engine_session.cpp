// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — Session view (M5): scenes/slots, launch/stop/record, quantization,
// session clip notes/props/take-length/landing-rules/CC curves.

#include "nota/nota_engine.h"
#include "nota/engine/Types.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Engine.h"
#include "nota_engine_internal.h"

#include <algorithm>
#include <cstdint>
#include <cstddef>
#include <string>
#include <vector>

namespace {

nota::Engine::Clip sessionCtlClip(const nota::Engine& engRef, int32_t trackId, int32_t scene) {
    if (scene < 0) return {};
    return nota::c_abi::trackHandle(engRef, trackId).clip(-1000 - scene);
}

nota::Engine::Clip sessionCtlClip(nota::Engine& engRef, int32_t trackId, int32_t scene) {
    if (scene < 0) return {};
    return nota::c_abi::trackHandle(engRef, trackId).clip(-1000 - scene);
}

} // namespace

extern "C" {

int32_t nota_session_scene_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).session().sceneCount() : 0;
}

int32_t nota_session_add_scene(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).session().addScene() : -1;
}

NotaResult nota_session_remove_scene(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().removeScene(scene) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_slot_state(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, scene).state() : 0;
}

double nota_session_slot_length(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, scene).length() : 0.0;
}

NotaResult nota_session_set_slot_length(NotaEngine* e, int32_t track_id, int32_t scene, double length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).setLength(length_beats) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_session_set_launch_quant(NotaEngine* e, double beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().setLaunchQuant(beats);
    return NOTA_OK;
}

NotaResult nota_session_launch_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, scene).launch(1.0f);
    return NOTA_OK;
}

NotaResult nota_session_stop_slot(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, 0).stop();
    return NOTA_OK;
}

NotaResult nota_session_launch_scene(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().scene(scene).launch();
    return NOTA_OK;
}

NotaResult nota_session_stop_scene(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().scene(scene).stop();
    return NOTA_OK;
}

NotaResult nota_session_stop_all(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().stopAll();
    return NOTA_OK;
}

NotaResult nota_session_cancel_slot_launch(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).cancelLaunch() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

NotaResult nota_session_cancel_scene_launch(NotaEngine* e, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().scene(scene).cancelLaunch() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

int32_t nota_session_slot_launch_queued(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return (e && nota::c_abi::ceng(e).session().slot(track_id, scene).isQueued()) ? 1 : 0;
}

int32_t nota_session_scene_queued(const NotaEngine* e, int32_t scene) {
    return (e && nota::c_abi::ceng(e).session().scene(scene).isQueued()) ? 1 : 0;
}

NotaResult nota_session_back_to_arrangement(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().backToArrangement();
    return NOTA_OK;
}

int32_t nota_arrangement_active(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).transport().arrangementActive()) ? 1 : 0;
}

NotaResult nota_session_clear_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).clear() ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_session_add_midi_clip(NotaEngine* e, int32_t track_id, int32_t scene, double length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).addMidiClip(length_beats) ? NOTA_OK
                                                                                         : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_session_record_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, scene).record();
    return NOTA_OK;
}

NotaResult nota_session_stop_record_slot(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, 0).stopRecord();
    return NOTA_OK;
}

NotaResult nota_session_stop_record(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().stopRecord();
    return NOTA_OK;
}

int32_t nota_session_slot_to_arrangement(NotaEngine* e, int32_t track_id, int32_t scene, double start_beat) {
    return e ? nota::c_abi::eng(e).session().slot(track_id, scene).toArrangement(start_beat) : -1;
}

NotaResult nota_session_from_arrangement(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).fromArrangementClip(clip_index) ? NOTA_OK
                                                                                               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_session_audio_from_arrangement(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).fromArrangementAudioClip(clip_index)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_audio_record_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().sessionAudioRecordSelfTest()) ? 1 : 0;
}

NotaResult nota_session_set_notes(
    NotaEngine* e, int32_t track_id, int32_t scene, const NotaNoteData* notes, int32_t count) {
    if (!e || count < 0 || (count > 0 && !notes)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::NoteData> conv;
    if (count > 0) {
        conv.reserve(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) conv.push_back(nota::c_abi::toCppNote(notes[i]));
    }
    return nota::c_abi::eng(e).session().slot(track_id, scene).setNotes(conv.empty() ? nullptr : conv.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_notes(
    const NotaEngine* e, int32_t track_id, int32_t scene, NotaNoteData* out, int32_t max_notes) {
    if (!e || !out || max_notes <= 0) return 0;
    std::vector<nota::engine::NoteData> tmp(static_cast<size_t>(max_notes));
    const int32_t n = nota::c_abi::ceng(e).session().slot(track_id, scene).notes(tmp.data(), max_notes);
    const int32_t m = std::min(n, max_notes);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCNote(tmp[static_cast<size_t>(i)]);
    return n;
}

int32_t nota_session_note_count(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, scene).noteCount() : 0;
}

int32_t nota_session_get_audio_slot(const NotaEngine* e, int32_t track_id, int32_t scene, NotaSessionAudioSlot* out) {
    if (!e || !out) return 0;
    nota::engine::SessionAudioSlotInfo info{};
    if (!nota::c_abi::ceng(e).session().slot(track_id, scene).audioInfo(info)) return 0;
    out->sample_id = info.sampleId;
    out->length_beats = info.lengthBeats;
    out->source_offset_frames = info.sourceOffsetFrames;
    out->length_frames = info.lengthFrames;
    out->gain = info.gain;
    return 1;
}

int32_t nota_session_add_audio_clip(NotaEngine* e, int32_t track_id, int32_t scene, const char* path_utf8,
    double length_beats, double source_offset_frames, int64_t length_frames, float gain) {
    if (!e || !path_utf8) return 0;
    return nota::c_abi::eng(e)
                   .session()
                   .slot(track_id, scene)
                   .addAudioClip(std::string(path_utf8), length_beats, source_offset_frames, length_frames, gain)
               ? 1
               : 0;
}

int32_t nota_session_add_audio_file(NotaEngine* e, int32_t track_id, int32_t scene, const char* path_utf8) {
    if (!e || !path_utf8) return 0;
    return nota::c_abi::eng(e).session().slot(track_id, scene).addAudioFile(std::string(path_utf8)) ? 1 : 0;
}

float nota_session_slot_gain(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, scene).gain() : 1.0f;
}

NotaResult nota_session_set_slot_gain(NotaEngine* e, int32_t track_id, int32_t scene, float gain) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).setGain(gain) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_clip_props(const NotaEngine* e, int32_t track_id, int32_t scene, NotaSessionClipProps* out) {
    if (!e || !out) return 0;
    nota::engine::SessionClipProps pr{};
    if (!nota::c_abi::ceng(e).session().slot(track_id, scene).props(pr)) return 0;
    out->color = pr.color;
    out->launch_mode = pr.launchMode;
    out->quant_beats = pr.quantBeats;
    out->legato = pr.legato;
    out->loop = pr.loop;
    out->velocity_amount = pr.velocityAmount;
    out->follow_a = pr.followA;
    out->follow_b = pr.followB;
    out->chance_a = pr.chanceA;
    out->chance_b = pr.chanceB;
    out->follow_beats = pr.followBeats;
    out->jump_scene = pr.jumpScene;
    out->follow_bypass = pr.followBypass;
    return 1;
}

NotaResult nota_session_set_clip_props(
    NotaEngine* e, int32_t track_id, int32_t scene, const NotaSessionClipProps* props) {
    if (!e || !props) return NOTA_ERR_INVALID_ARG;
    nota::engine::SessionClipProps pr{};
    pr.color = props->color;
    pr.launchMode = props->launch_mode;
    pr.quantBeats = props->quant_beats;
    pr.legato = props->legato;
    pr.loop = props->loop;
    pr.velocityAmount = props->velocity_amount;
    pr.followA = props->follow_a;
    pr.followB = props->follow_b;
    pr.chanceA = props->chance_a;
    pr.chanceB = props->chance_b;
    pr.followBeats = props->follow_beats;
    pr.jumpScene = props->jump_scene;
    pr.followBypass = props->follow_bypass;
    return nota::c_abi::eng(e).session().slot(track_id, scene).setProps(pr) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_clip_name(const NotaEngine* e, int32_t track_id, int32_t scene, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(nota::c_abi::ceng(e).session().slot(track_id, scene).name(), out, cap);
}

NotaResult nota_session_set_clip_name(NotaEngine* e, int32_t track_id, int32_t scene, const char* name_utf8) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).setName(name_utf8 ? name_utf8 : "")
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_slot_stop_button(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return (e && nota::c_abi::ceng(e).session().slot(track_id, scene).stopButton()) ? 1 : 0;
}

NotaResult nota_session_set_slot_stop_button(NotaEngine* e, int32_t track_id, int32_t scene, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(track_id, scene).setStopButton(on != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_slot_take_length(
    const NotaEngine* e, int32_t track_id, int32_t scene, NotaSlotTakeLength* out) {
    if (!e || !out) return 0;
    nota::engine::SlotTakeLength v{};
    if (!nota::c_abi::ceng(e).session().slot(track_id, scene).takeLength(v)) return 0;
    out->mode = v.mode;
    out->bars = v.bars;
    out->beats = v.beats;
    return 1;
}

NotaResult nota_session_set_slot_take_length(
    NotaEngine* e, int32_t track_id, int32_t scene, const NotaSlotTakeLength* in) {
    if (!e || !in) return NOTA_ERR_INVALID_ARG;
    const nota::engine::SlotTakeLength v{in->mode, in->bars, in->beats};
    return nota::c_abi::eng(e).session().slot(track_id, scene).setTakeLength(v) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

double nota_session_slot_take_beats(const NotaEngine* e, int32_t track_id, int32_t scene) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, scene).takeBeats() : 0.0;
}

int32_t nota_session_get_slot_landing_rule(
    const NotaEngine* e, int32_t track_id, int32_t scene, NotaSlotLandingRule* out) {
    if (!e || !out) return 0;
    nota::engine::SlotLandingRule v{};
    if (!nota::c_abi::ceng(e).session().slot(track_id, scene).landingRule(v)) return 0;
    out->action = v.action;
    out->scene = v.scene;
    out->quant_beats = v.quantBeats;
    return 1;
}

NotaResult nota_session_set_slot_landing_rule(
    NotaEngine* e, int32_t track_id, int32_t scene, const NotaSlotLandingRule* in) {
    if (!e || !in) return NOTA_ERR_INVALID_ARG;
    const nota::engine::SlotLandingRule v{in->action, in->scene, in->quant_beats};
    return nota::c_abi::eng(e).session().slot(track_id, scene).setLandingRule(v) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_session_copy_slot(
    NotaEngine* e, int32_t src_track, int32_t src_scene, int32_t dst_track, int32_t dst_scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().slot(src_track, src_scene).copyTo(dst_track, dst_scene) ? NOTA_OK
                                                                                                 : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_scene_props(const NotaEngine* e, int32_t scene, NotaSceneProps* out) {
    if (!e || !out) return 0;
    nota::engine::SceneProps pr{};
    if (!nota::c_abi::ceng(e).session().scene(scene).props(pr)) return 0;
    out->color = pr.color;
    out->tempo = pr.tempo;
    out->sig_num = pr.sigNum;
    out->sig_den = pr.sigDen;
    out->follow = pr.follow;
    out->follow_beats = pr.followBeats;
    return 1;
}

NotaResult nota_session_set_scene_props(NotaEngine* e, int32_t scene, const NotaSceneProps* props) {
    if (!e || !props) return NOTA_ERR_INVALID_ARG;
    nota::engine::SceneProps pr{};
    pr.color = props->color;
    pr.tempo = props->tempo;
    pr.sigNum = props->sig_num;
    pr.sigDen = props->sig_den;
    pr.follow = props->follow;
    pr.followBeats = props->follow_beats;
    return nota::c_abi::eng(e).session().scene(scene).setProps(pr) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_get_scene_name(const NotaEngine* e, int32_t scene, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(nota::c_abi::ceng(e).session().scene(scene).name(), out, cap);
}

NotaResult nota_session_set_scene_name(NotaEngine* e, int32_t scene, const char* name_utf8) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().scene(scene).setName(name_utf8 ? name_utf8 : "") ? NOTA_OK
                                                                                          : NOTA_ERR_INVALID_ARG;
}

int32_t nota_session_insert_scene(NotaEngine* e, int32_t at) {
    return e ? nota::c_abi::eng(e).session().insertScene(at) : -1;
}

int32_t nota_session_duplicate_scene(NotaEngine* e, int32_t scene) {
    return e ? nota::c_abi::eng(e).session().duplicateScene(scene) : -1;
}

int32_t nota_session_capture_scene(NotaEngine* e, int32_t at) {
    return e ? nota::c_abi::eng(e).session().captureScene(at) : -1;
}

NotaResult nota_session_move_scene(NotaEngine* e, int32_t from, int32_t to) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).session().moveScene(from, to) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_session_launch_slot_vel(NotaEngine* e, int32_t track_id, int32_t scene, float velocity) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, scene).launch(velocity);
    return NOTA_OK;
}

NotaResult nota_session_release_slot(NotaEngine* e, int32_t track_id, int32_t scene) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, scene).release();
    return NOTA_OK;
}

NotaResult nota_session_track_back_to_arrangement(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().slot(track_id, 0).backToArrangement();
    return NOTA_OK;
}

int32_t nota_session_playing_slot(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, 0).playingSlotOnTrack() : -1;
}

double nota_session_slot_position(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, 0).positionOnTrack() : 0.0;
}

double nota_session_get_launch_quant(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).session().launchQuant() : 4.0;
}

NotaResult nota_session_set_follow(NotaEngine* e, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().setFollow(on != 0);
    return NOTA_OK;
}

int32_t nota_session_get_follow(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).session().follow()) ? 1 : 0;
}

NotaResult nota_session_set_record_length(NotaEngine* e, double beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).session().setRecordLength(beats);
    return NOTA_OK;
}

double nota_session_get_record_length(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).session().recordLength() : 0.0;
}

int32_t nota_session_record_scene(NotaEngine* e, int32_t scene) {
    return e ? nota::c_abi::eng(e).session().scene(scene).record() : 0;
}

int32_t nota_session_take_scene_tempo(NotaEngine* e, double* bpm, int32_t* num, int32_t* den) {
    return (e && nota::c_abi::eng(e).session().takeSceneTempoChange(bpm, num, den)) ? 1 : 0;
}

int32_t nota_session_slot_peaks(
    const NotaEngine* e, int32_t track_id, int32_t scene, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::ceng(e).session().slot(track_id, scene).peaks(out_min_max, max_points) : 0;
}

int32_t nota_session_record_target(const NotaEngine* e, int32_t* track_id, int32_t* scene, double* elapsed_beats) {
    return (e && nota::c_abi::ceng(e).session().recordTarget(track_id, scene, elapsed_beats)) ? 1 : 0;
}

int32_t nota_session_ctl_list(const NotaEngine* e, int32_t track_id, int32_t scene, int32_t* out_ctls, int32_t cap) {
    return e ? sessionCtlClip(nota::c_abi::ceng(e), track_id, scene).ctlList(out_ctls, cap) : 0;
}

int32_t nota_session_ctl_get(
    const NotaEngine* e, int32_t track_id, int32_t scene, int32_t ctl, NotaAutomationPoint* out, int32_t cap) {
    if (!e) return 0;
    const auto clip = sessionCtlClip(nota::c_abi::ceng(e), track_id, scene);
    if (!out || cap <= 0) return clip.ctlGet(ctl, nullptr, 0);
    std::vector<nota::engine::AutomationPointData> tmp(static_cast<size_t>(cap));
    const int32_t n = clip.ctlGet(ctl, tmp.data(), cap);
    const int32_t m = std::min(n, cap);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCAutoPoint(tmp[static_cast<size_t>(i)]);
    return n;
}

NotaResult nota_session_ctl_set(
    NotaEngine* e, int32_t track_id, int32_t scene, int32_t ctl, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::AutomationPointData> conv;
    if (count > 0) {
        conv.reserve(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) conv.push_back(nota::c_abi::toCppAutoPoint(pts[i]));
    }
    return sessionCtlClip(nota::c_abi::eng(e), track_id, scene).ctlSet(ctl, conv.empty() ? nullptr : conv.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

} // extern "C"
