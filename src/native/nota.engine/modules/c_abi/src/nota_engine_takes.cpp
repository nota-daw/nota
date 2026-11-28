// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — track metadata & clipboard, audio/MIDI inputs, Cue & output routing,
// monitoring, articulation maps, freeze/bounce, takes/comping, and sample read.

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"
#include "nota_engine_internal.h"

#include "nota/dsp/rt/OutputRouting.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

extern "C" {

NotaResult nota_track_set_name(NotaEngine* e, int32_t track_id, const char* name) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setName(name ? std::string(name) : std::string{})
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_name(NotaEngine* e, int32_t track_id, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).name(), out, cap);
}

NotaResult nota_track_set_color(NotaEngine* e, int32_t track_id, int32_t color_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setColorIndex(color_index) ? NOTA_OK
                                                                                              : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_color(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).colorIndex() : -1;
}

NotaResult nota_track_set_piano_fold(NotaEngine* e, int32_t track_id, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setPianoFold(on != 0) ? NOTA_OK
                                                                                         : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_piano_fold(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).pianoFold() : -1;
}

NotaResult nota_track_set_edit_link(NotaEngine* e, int32_t track_id, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setEditLink(on != 0) ? NOTA_OK
                                                                                        : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_edit_link(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).editLink() : -1;
}

NotaResult nota_track_copy(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().copy(track_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_paste(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).tracks().paste() : -1;
}

NotaResult nota_track_copy_many(NotaEngine* e, const int32_t* track_ids, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().copyMany(track_ids, n) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_paste_after(NotaEngine* e, int32_t after_track_id, int32_t* out_ids, int32_t cap) {
    return e ? nota::c_abi::eng(e).tracks().pasteAfter(after_track_id, out_ids, cap) : -1;
}

int32_t nota_track_has_clipboard(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).tracks().hasClipboard()) ? 1 : 0;
}

NotaResult nota_track_set_record_input(NotaEngine* e, int32_t track_id, int32_t source) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setRecordInput(source);
    return NOTA_OK;
}

int32_t nota_track_get_record_input(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).recordInput() : 0;
}

NotaResult nota_track_set_input_channels(NotaEngine* e, int32_t track_id, int32_t channels) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setInputChannels(channels);
    return NOTA_OK;
}

int32_t nota_track_get_input_channels(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).inputChannels() : 0;
}

NotaResult nota_track_set_input(NotaEngine* e, int32_t track_id, int32_t first, int32_t width) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setInput(first, width) ? NOTA_OK
                                                                                          : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_input(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).input() : -1;
}

int32_t nota_track_input_available(const NotaEngine* e, int32_t track_id) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).inputAvailable()) ? 1 : 0;
}

NotaResult nota_track_set_midi_input(NotaEngine* e, int32_t track_id, int32_t source, int32_t channel) {
    if (!e || channel < 0 || channel > 16) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setMidiInput(source, channel) ? NOTA_OK
                                                                                                 : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_midi_input(const NotaEngine* e, int32_t track_id) {
    if (!e) return 0;
    const auto tr = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id);
    return (tr.midiInputSource() + 2) | (tr.midiInputChannel() << 8);
}

NotaResult nota_track_set_midi_input_uid(NotaEngine* e, int32_t track_id, const char* uid, int32_t channel) {
    if (!e || channel < 0 || channel > 16) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setMidiInputUid(uid ? uid : "", channel)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_midi_input_uid(const NotaEngine* e, int32_t track_id, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).midiInputUid(), out, cap);
}

int32_t nota_midi_source_of_uid(const NotaEngine* e, const char* uid) {
    return (e && uid) ? nota::c_abi::ceng(e).midi().sourceOfUid(uid) : -1;
}

void nota_engine_push_midi_for_test(
    NotaEngine* e, int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2) {
    if (e) nota::c_abi::eng(e).midi().pushMidiForTest(source, status & 0xF0, channel & 0x0F, d1 & 0x7F, d2 & 0x7F);
}

int32_t nota_audio_input_channel_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().inputChannelCount() : 0;
}

int32_t nota_audio_input_channel_name(const NotaEngine* e, int32_t index, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(nota::c_abi::ceng(e).audio().inputChannelName(index), out, cap);
}

int32_t nota_audio_input_probe(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).audio().probeInput() : 0;
}

void nota_audio_input_set_disabled(NotaEngine* e, const int32_t* channels, int32_t count) {
    if (e) nota::c_abi::eng(e).recorder().setDisabledInputs(channels, count);
}

void nota_engine_feed_input_test(NotaEngine* e, const float* interleaved, int32_t frames, int32_t channels) {
    if (e && interleaved && frames > 0 && channels > 0)
        nota::c_abi::eng(e).recorder().feedInputForTest(interleaved, frames, channels);
}

void nota_engine_set_test_input_device(NotaEngine* e, int32_t channels) {
    if (e) nota::c_abi::eng(e).recorder().setTestInputDevice(channels);
}

// --- Cue and output routing (specs/089) --------------------------------------

NotaResult nota_track_set_cue(NotaEngine* e, int32_t track_id, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setCue(on != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_cue(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cue() : -1;
}

int32_t nota_audio_output_channel_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().outputChannelCount() : 0;
}

int32_t nota_audio_output_channel_name(const NotaEngine* e, int32_t index, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(nota::c_abi::ceng(e).audio().outputChannelName(index), out, cap);
}

NotaResult nota_audio_set_output_routing(
    NotaEngine* e, int32_t main_first, int32_t main_width, int32_t cue_first, int32_t cue_width) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    const nota::dsp::OutputRouting r{main_first, main_width, cue_first < 0 ? -1 : cue_first, cue_width};
    return nota::c_abi::eng(e).audio().setOutputRouting(r) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_audio_output_routing_state(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().outputRoutingState() : 0;
}

void nota_engine_set_test_output_device(NotaEngine* e, int32_t channels) {
    if (e) nota::c_abi::eng(e).audio().setTestOutputDevice(channels);
}

int32_t nota_audio_scatter_test(const NotaEngine* e, const float* main_in, const float* cue_in, float* device_out,
    int32_t device_channels, int32_t frames) {
    if (!e || !main_in || !device_out || device_channels <= 0 || frames <= 0) return 0;
    const nota::dsp::ResolvedRouting r =
        nota::dsp::resolveRouting(nota::c_abi::ceng(e).audio().outputRouting(), device_channels);
    nota::dsp::scatterOutputs(main_in, r.cueWidth > 0 ? cue_in : nullptr, device_out, device_channels, frames, r);
    return r.flags;
}

NotaResult nota_track_set_monitor(NotaEngine* e, int32_t track_id, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setMonitor(on != 0);
    return NOTA_OK;
}

int32_t nota_track_get_monitor(NotaEngine* e, int32_t track_id) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).monitor()) ? 1 : 0;
}

void nota_engine_push_monitor_frames(NotaEngine* e, const float* interleaved_stereo, int32_t frames) {
    if (e && interleaved_stereo && frames > 0)
        nota::c_abi::eng(e).recorder().pushMonitorFramesForTest(interleaved_stereo, frames);
}

void nota_engine_set_monitor_input_test(NotaEngine* e, double sample_rate, int32_t block_frames) {
    if (e) nota::c_abi::eng(e).recorder().setMonitorInputForTest(sample_rate, block_frames);
}

NotaResult nota_track_set_midi_source(NotaEngine* e, int32_t track_id, int32_t source_track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setMidiSource(source_track_id);
    return NOTA_OK;
}

NotaResult nota_track_set_midi_offset_ms(NotaEngine* e, int32_t track_id, double ms) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setMidiOffsetMs(ms);
    return NOTA_OK;
}

double nota_track_get_midi_offset_ms(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).midiOffsetMs() : 0.0;
}

NotaResult nota_track_set_articulation_map(NotaEngine* e, int32_t track_id, int32_t default_id,
    const NotaArticulationEntry* entries, int32_t entry_count, const NotaArticulationAction* actions,
    int32_t action_count, const char* map_json, int32_t map_json_len) {
    if (!e || entry_count < 0 || action_count < 0 || (entry_count > 0 && !entries) || (action_count > 0 && !actions))
        return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::ArticulationEntryData> ent;
    ent.reserve(static_cast<size_t>(entry_count));
    for (int32_t i = 0; i < entry_count; ++i)
        ent.push_back({entries[i].id, entries[i].trigger_mode, entries[i].offset_ms, entries[i].lead_ms,
            entries[i].action_start, entries[i].action_count});
    std::vector<nota::engine::ArticulationActionData> act;
    act.reserve(static_cast<size_t>(action_count));
    for (int32_t i = 0; i < action_count; ++i)
        act.push_back({actions[i].kind, actions[i].channel, actions[i].data1, actions[i].data2});
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .setArticulationMap(default_id, ent.empty() ? nullptr : ent.data(), entry_count,
                       act.empty() ? nullptr : act.data(), action_count, map_json, map_json_len)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_clear_articulation_map(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clearArticulationMap() ? NOTA_OK
                                                                                          : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_get_articulation_map_json(const NotaEngine* e, int32_t track_id, char* out, int32_t max_len) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).articulationMapJson(out, max_len) : 0;
}

int32_t nota_track_get_midi_source(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).midiSource() : -1;
}

// --- Freeze & bounce (M7) ---------------------------------------------------

int64_t nota_track_freeze_begin(NotaEngine* e, int32_t track_id, double length_beats) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).beginFreeze(length_beats) : 0;
}

void nota_track_freeze_end(NotaEngine* e, int32_t track_id) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).endFreeze();
}

void nota_track_freeze_cancel(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).tracks().master().cancelFreeze();
}

int64_t nota_track_bounce_begin(NotaEngine* e, int32_t track_id, double length_beats) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).beginFreeze(length_beats, 0.0) : 0;
}

int64_t nota_track_bounce_range_begin(NotaEngine* e, int32_t track_id, double start_beat, double end_beat) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .beginFreeze(end_beat - start_beat, 0.0, start_beat)
             : 0;
}

int64_t nota_track_freeze_take(NotaEngine* e, float* out, int64_t cap_frames) {
    return e ? nota::c_abi::eng(e).tracks().master().takeFreezeCapture(out, cap_frames) : 0;
}

int32_t nota_track_paste_audio_frames(
    NotaEngine* e, int32_t track_id, const float* interleaved, int64_t frames, double at_beat, const char* name_utf8) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .pasteAudioFrames(interleaved, frames, at_beat, name_utf8 ? name_utf8 : "")
             : -1;
}

void nota_track_unfreeze(NotaEngine* e, int32_t track_id) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).unfreeze();
}

int32_t nota_track_is_frozen(const NotaEngine* e, int32_t track_id) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).isFrozen()) ? 1 : 0;
}

int32_t nota_track_freeze_get_state(const NotaEngine* e, int32_t track_id, uint8_t* out, int32_t cap) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).freezeGetState(out, cap) : 0;
}

void nota_track_freeze_set_state(NotaEngine* e, int32_t track_id, const uint8_t* data, int32_t size) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).freezeSetState(data, size);
}

NotaResult nota_track_set_armed(NotaEngine* e, int32_t track_id, int32_t armed) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setArmed(armed != 0);
    return NOTA_OK;
}

// ---- Audio persistence & takes (M7-6b) -------------------------------------

int32_t nota_track_get_audio_clip_info(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaAudioClipInfo* out) {
    if (!e || !out) return 0;
    const auto ai = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).audioInfo();
    if (!ai) return 0;
    *out = {ai->startBeat, ai->sourceOffsetFrames, ai->lengthFrames, ai->gain, ai->sampleId, ai->pitchSemitones,
        ai->warpEnabled, ai->warpMode, ai->warpBeats, ai->warpPlayStart, ai->warpPlayEnd, ai->reversed};
    return 1;
}

int32_t nota_track_add_audio_clip_ex(NotaEngine* e, int32_t track_id, const char* path, double start_beat,
    double source_offset_frames, int64_t length_frames, float gain) {
    if (!e || !path) return -1;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
        .addAudioClipEx(std::string(path), start_beat, source_offset_frames, length_frames, gain);
}

int32_t nota_track_take_count(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).takeCount() : 0;
}

int32_t nota_track_take_info(const NotaEngine* e, int32_t track_id, int32_t index, NotaTakeInfo* out) {
    if (!e || !out) return 0;
    nota::engine::TakeInfo ti{};
    if (!nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).takeInfo(index, ti)) return 0;
    *out = {ti.id, ti.number, ti.group, 0, ti.originBeat, ti.startBeat, ti.lengthBeats, ti.sourceOffsetFrames,
        ti.lengthFrames, ti.sampleId};
    return 1;
}

int32_t nota_track_take_peaks(
    const NotaEngine* e, int32_t track_id, int32_t index, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).takePeaks(index, out_min_max, max_points) : 0;
}

NotaResult nota_track_take_comp(NotaEngine* e, int32_t track_id, int32_t take_id, double start_beat, double end_beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).compTake(take_id, start_beat, end_beat)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_take_delete(NotaEngine* e, int32_t track_id, int32_t take_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).deleteTake(take_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_takes_flatten(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).flattenTakes() ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_take_id(const NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).takeId() : 0;
}

int32_t nota_track_restore_take(NotaEngine* e, int32_t track_id, const char* path, int32_t number, int32_t group,
    double origin_beat, double source_offset_frames, int64_t length_frames) {
    if (!e || !path) return -1;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
        .restoreTake(std::string(path), number, group, origin_beat, source_offset_frames, length_frames);
}

NotaResult nota_clip_set_take_id(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t take_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setTakeId(take_id)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_joined_edges(const NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).joinedEdges() : 0;
}

NotaResult nota_track_restore_joined_edges(NotaEngine* e, int32_t track_id, const int32_t* edges, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).restoreJoinedEdges(edges, count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_take_record_test(
    NotaEngine* e, int32_t track_id, const float* interleaved_stereo, int32_t frames, double start_beat) {
    return (e && nota::c_abi::eng(e).recorder().takeRecordTest(track_id, interleaved_stereo, frames, start_beat)) ? 1
                                                                                                                  : 0;
}

int32_t nota_take_record_test_armed(
    NotaEngine* e, const float* interleaved, int32_t channels, int32_t frames, double start_beat) {
    return e ? nota::c_abi::eng(e).recorder().takeRecordTestArmed(interleaved, channels, frames, start_beat) : 0;
}

int32_t nota_take_record_test_armed_disarm(NotaEngine* e, const float* interleaved, int32_t channels, int32_t frames,
    double start_beat, int32_t disarm_track_id, int32_t disarm_at_frame) {
    return e ? nota::c_abi::eng(e).recorder().takeRecordTestArmed(
                   interleaved, channels, frames, start_beat, disarm_track_id, disarm_at_frame)
             : 0;
}

int32_t nota_take_record_test_n(
    NotaEngine* e, int32_t track_id, const float* interleaved, int32_t channels, int32_t frames, double start_beat) {
    return (e && nota::c_abi::eng(e).recorder().takeRecordTestN(track_id, interleaved, channels, frames, start_beat))
               ? 1
               : 0;
}

void nota_engine_input_block_test(NotaEngine* e, const float* interleaved, int32_t frames, int32_t channels) {
    if (e) nota::c_abi::eng(e).recorder().inputBlockForTest(interleaved, frames, channels);
}

int32_t nota_sample_get_info(const NotaEngine* e, int64_t sample_id, NotaSampleInfo* out) {
    if (!e || !out) return 0;
    nota::engine::SampleInfo si{};
    if (!nota::c_abi::ceng(e).tracks().sampleInfo(sample_id, si)) return 0;
    *out = {si.channels, si.frames, si.sampleRate};
    return 1;
}

int64_t nota_sample_read(const NotaEngine* e, int64_t sample_id, float* out, int64_t cap) {
    return e ? nota::c_abi::ceng(e).tracks().sampleRead(sample_id, out, cap) : 0;
}

} // extern "C"
