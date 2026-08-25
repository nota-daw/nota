// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — tracks, mixer strips, sends/returns, arrangement clips (geometry +
// move/trim/split/duplicate/delete), MIDI clips & notes, and audio persistence
// getters/setters (M7-6b: audio clips, samples, sampler).

#include "nota_engine_internal.h"

#include <algorithm>
#include <string>
#include <utility>
#include <vector>

extern "C" {

// ---- Tracks & clips -------------------------------------------------------

int32_t nota_engine_add_audio_track(NotaEngine* e) {
    return e ? ENG(e)->addAudioTrack() : 0;
}
int32_t nota_track_add_audio_clip(NotaEngine* e, int32_t track_id, const char* path, double start_beat) {
    if (!e || !path) return -1;
    return ENG(e)->addAudioClip(track_id, std::string(path), start_beat);
}
NotaResult nota_track_set_volume(NotaEngine* e, int32_t id, float v) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setTrackVolume(id, v); return NOTA_OK;
}
NotaResult nota_track_set_pan(NotaEngine* e, int32_t id, float p) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setTrackPan(id, p); return NOTA_OK;
}
NotaResult nota_track_set_mute(NotaEngine* e, int32_t id, int32_t m) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setTrackMute(id, m != 0); return NOTA_OK;
}
NotaResult nota_track_set_solo(NotaEngine* e, int32_t id, int32_t s) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setTrackSolo(id, s != 0); return NOTA_OK;
}
int32_t nota_engine_track_count(const NotaEngine* e) {
    return e ? CENG(e)->trackCount() : 0;
}
int32_t nota_engine_duplicate_track(NotaEngine* e, int32_t track_id) {
    return e ? ENG(e)->duplicateTrack(track_id) : -1;
}
NotaResult nota_engine_remove_track(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->removeTrack(track_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_engine_move_track(NotaEngine* e, int32_t track_id, int32_t to_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->moveTrack(track_id, to_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_engine_add_group_track(NotaEngine* e) {
    return e ? ENG(e)->addGroupTrack() : -1;
}
int32_t nota_engine_create_group(NotaEngine* e, const int32_t* track_ids, int32_t n) {
    return e ? ENG(e)->createGroup(track_ids, n) : -1;
}
NotaResult nota_engine_ungroup(NotaEngine* e, int32_t group_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->ungroup(group_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_engine_set_track_group(NotaEngine* e, int32_t track_id, int32_t group_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setTrackGroup(track_id, group_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

// ---- Send / return buses (M6-1) -------------------------------------------
int32_t nota_engine_add_return_track(NotaEngine* e) {
    return e ? ENG(e)->addReturnTrack() : 0;
}
NotaResult nota_track_set_send(NotaEngine* e, int32_t id, int32_t bus, float level) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setTrackSend(id, bus, level); return NOTA_OK;
}
float nota_track_get_send(const NotaEngine* e, int32_t id, int32_t bus) {
    return e ? CENG(e)->trackSend(id, bus) : 0.0f;
}
int32_t nota_engine_return_track_count(const NotaEngine* e) {
    return e ? CENG(e)->returnTrackCount() : 0;
}
int32_t nota_track_return_index(const NotaEngine* e, int32_t id) {
    return e ? CENG(e)->trackReturnIndex(id) : -1;
}
int32_t nota_clip_get_peaks(const NotaEngine* e, int32_t track_id, int32_t clip_index,
                            float* out_min_max, int32_t max_points) {
    if (!e) return 0;
    return CENG(e)->getClipPeaks(track_id, clip_index, out_min_max, max_points);
}
int32_t nota_clip_get_source_peaks(const NotaEngine* e, int32_t track_id, int32_t clip_index,
                                   float* out_min_max, int32_t max_points) {
    if (!e) return 0;
    return CENG(e)->getClipSourcePeaks(track_id, clip_index, out_min_max, max_points);
}
int32_t nota_clip_get_warp_full_peaks(const NotaEngine* e, int32_t track_id, int32_t clip_index,
                                      float* out_min_max, int32_t max_points) {
    if (!e) return 0;
    return CENG(e)->getClipWarpFullPeaks(track_id, clip_index, out_min_max, max_points);
}

// ---- Instrument tracks, MIDI clips & notes (M2) ---------------------------

int32_t nota_engine_add_instrument_track(NotaEngine* e) {
    return e ? ENG(e)->addInstrumentTrack() : 0;
}
int32_t nota_engine_add_physical_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addPhysicalSynthTrack() : 0;
}
int32_t nota_engine_add_wavetable_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addWavetableSynthTrack() : 0;
}
int32_t nota_engine_add_volt_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addVoltSynthTrack() : 0;
}
int32_t nota_engine_add_bass_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addBassSynthTrack() : 0;
}
int32_t nota_engine_add_pendulum_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addPendulumSynthTrack() : 0;
}
int32_t nota_engine_add_operator_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addOperatorSynthTrack() : 0;
}
int32_t nota_engine_add_grain_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addGrainSynthTrack() : 0;
}
int32_t nota_engine_add_flux_synth_track(NotaEngine* e) {
    return e ? ENG(e)->addFluxSynthTrack() : 0;
}
int32_t nota_engine_add_rhythm_track(NotaEngine* e) {
    return e ? ENG(e)->addRhythmSynthTrack() : 0;
}
int32_t nota_engine_add_monolith_track(NotaEngine* e) {
    return e ? ENG(e)->addMonolithTrack() : 0;
}
void nota_track_instrument_action(NotaEngine* e, int32_t track_id, int32_t id, int32_t iarg, float farg) {
    if (e) ENG(e)->instrumentAction(track_id, id, iarg, farg);
}
int32_t nota_track_rhythm_set_voice_sample(NotaEngine* e, int32_t track_id, int32_t voice, const char* path) {
    if (!e || !path) return 0;
    return ENG(e)->setRhythmVoiceSample(track_id, voice, std::string(path)) ? 1 : 0;
}
int32_t nota_track_rhythm_voice_info(const NotaEngine* e, int32_t track_id, int32_t voice, NotaSamplerInfo* out) {
    return (e && CENG(e)->rhythmVoiceInfo(track_id, voice, out)) ? 1 : 0;
}
int32_t nota_track_rhythm_voice_source(const NotaEngine* e, int32_t track_id, int32_t voice) {
    return e ? CENG(e)->rhythmVoiceSource(track_id, voice) : 0;
}
int32_t nota_track_set_grain_sample(NotaEngine* e, int32_t track_id, const char* path, int32_t root) {
    if (!e || !path) return 0;
    return ENG(e)->setTrackGrainSample(track_id, std::string(path), root) ? 1 : 0;
}
int32_t nota_track_grain_info(const NotaEngine* e, int32_t track_id, NotaSamplerInfo* out) {
    return (e && CENG(e)->grainInfo(track_id, out)) ? 1 : 0;
}
int32_t nota_track_grain_play_positions(const NotaEngine* e, int32_t track_id, float* out, int32_t max_n) {
    return e ? CENG(e)->grainPlayPositions(track_id, out, max_n) : 0;
}
int32_t nota_track_active_voices(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->instrumentVoiceCount(track_id) : -1;
}
int32_t nota_track_held_notes(const NotaEngine* e, int32_t track_id, int32_t* out, int32_t max_n) {
    return e ? CENG(e)->instrumentHeldNotes(track_id, out, max_n) : 0;
}
int32_t nota_track_instrument_scope(const NotaEngine* e, int32_t track_id, float* out, int32_t max_n) {
    return e ? CENG(e)->instrumentScope(track_id, out, max_n) : 0;
}
int32_t nota_engine_add_sampler_track(NotaEngine* e, const char* path, int32_t root, int32_t loop) {
    if (!e || !path) return 0;
    return ENG(e)->addSamplerTrack(std::string(path), root, loop != 0);
}
int32_t nota_engine_add_sampler_instrument_track(NotaEngine* e) {
    return e ? ENG(e)->addSamplerInstrumentTrack() : 0;
}
int32_t nota_track_set_sampler_sample(NotaEngine* e, int32_t track_id, const char* path, int32_t root) {
    if (!e || !path) return 0;
    return ENG(e)->setTrackSamplerSample(track_id, std::string(path), root) ? 1 : 0;
}
int32_t nota_track_set_sampler_root(NotaEngine* e, int32_t track_id, int32_t root) {
    return (e && ENG(e)->setTrackSamplerRoot(track_id, root)) ? 1 : 0;
}
float nota_track_sampler_play_position(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->samplerPlayPosition(track_id) : -1.0f;
}

// ---- Arrangement geometry (M4) --------------------------------------------

int32_t nota_engine_get_track_info(const NotaEngine* e, int32_t index, NotaTrackInfo* out) {
    return (e && CENG(e)->trackInfo(index, out)) ? 1 : 0;
}
int32_t nota_track_get_clip_info(const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaClipInfo* out) {
    return (e && CENG(e)->clipInfo(track_id, clip_index, out)) ? 1 : 0;
}
NotaResult nota_clip_move(NotaEngine* e, int32_t track_id, int32_t clip_index, double new_start_beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->moveClip(track_id, clip_index, new_start_beat) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_move_to_track(NotaEngine* e, int32_t src_track_id, int32_t clip_index, int32_t source_track_id, double new_start_beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->moveClipToTrack(src_track_id, clip_index, source_track_id, new_start_beat) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_trim(NotaEngine* e, int32_t track_id, int32_t clip_index, double new_start_beat, double new_length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->trimClip(track_id, clip_index, new_start_beat, new_length_beats) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_resize_audio(NotaEngine* e, int32_t track_id, int32_t clip_index, double new_start_beat, double new_length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->resizeAudioClip(track_id, clip_index, new_start_beat, new_length_beats) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_source_region(NotaEngine* e, int32_t track_id, int32_t clip_index, double offset_frames, int64_t length_frames) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipSourceRegion(track_id, clip_index, offset_frames, length_frames) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_warp_trim(NotaEngine* e, int32_t track_id, int32_t clip_index, double play_start, double play_end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipWarpTrim(track_id, clip_index, play_start, play_end) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_gain(NotaEngine* e, int32_t track_id, int32_t clip_index, float gain) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipGain(track_id, clip_index, gain) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_active(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t active) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipActive(track_id, clip_index, active != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_pitch(NotaEngine* e, int32_t track_id, int32_t clip_index, float semitones) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipPitch(track_id, clip_index, semitones) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_warp(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t enabled, int32_t mode) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipWarp(track_id, clip_index, enabled != 0, mode) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_warp_length(NotaEngine* e, int32_t track_id, int32_t clip_index, double beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipWarpLength(track_id, clip_index, beats) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
double nota_clip_auto_warp(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? ENG(e)->autoWarpClip(track_id, clip_index) : 0.0;
}
void nota_engine_set_defer_warp_build(NotaEngine* e, int32_t defer) {
    if (e) ENG(e)->setDeferWarpBuild(defer != 0);
}
int64_t nota_engine_warp_build_step(NotaEngine* e, int32_t max_frames) {
    return e ? ENG(e)->warpBuildStep(max_frames) : 0;
}
int64_t nota_engine_warp_build_remaining(const NotaEngine* e) {
    return e ? CENG(e)->warpBuildRemainingFrames() : 0;
}
double nota_clip_beat_warp(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? ENG(e)->beatWarpClip(track_id, clip_index) : 0.0;
}
int32_t nota_clip_volume_env_get(const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaAutomationPoint* out, int32_t cap) {
    return e ? CENG(e)->getAudioClipVolumeEnvelope(track_id, clip_index, out, cap) : 0;
}
NotaResult nota_clip_volume_env_set(NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaAutomationPoint* pts, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setAudioClipVolumeEnvelope(track_id, clip_index, pts, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_clip_pan_env_get(const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaAutomationPoint* out, int32_t cap) {
    return e ? CENG(e)->getAudioClipPanEnvelope(track_id, clip_index, out, cap) : 0;
}
NotaResult nota_clip_pan_env_set(NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaAutomationPoint* pts, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setAudioClipPanEnvelope(track_id, clip_index, pts, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_midi_clip_env_get(const NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t kind, NotaAutomationPoint* out, int32_t cap) {
    return e ? CENG(e)->getMidiClipEnvelope(track_id, clip_index, kind, out, cap) : 0;
}
NotaResult nota_midi_clip_env_set(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t kind, const NotaAutomationPoint* pts, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setMidiClipEnvelope(track_id, clip_index, kind, pts, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_warp_markers(NotaEngine* e, int32_t track_id, int32_t clip_index, const double* src, const double* beats, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipWarpMarkers(track_id, clip_index, src, beats, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_clip_get_warp_markers(const NotaEngine* e, int32_t track_id, int32_t clip_index, double* out_src, double* out_beat, int32_t max_count) {
    return e ? CENG(e)->clipWarpMarkers(track_id, clip_index, out_src, out_beat, max_count) : 0;
}
int32_t nota_clip_split(NotaEngine* e, int32_t track_id, int32_t clip_index, double at_beat) {
    return e ? ENG(e)->splitClip(track_id, clip_index, at_beat) : -1;
}
int32_t nota_clip_duplicate(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? ENG(e)->duplicateClip(track_id, clip_index) : -1;
}
NotaResult nota_clip_delete(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->deleteClip(track_id, clip_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_copy(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->copyClip(track_id, clip_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_cut(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->cutClip(track_id, clip_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_clip_paste(NotaEngine* e, int32_t source_track_id, double at_beat) {
    return e ? ENG(e)->pasteClip(source_track_id, at_beat) : -1;
}
int32_t nota_clip_clipboard_kind(NotaEngine* e) {
    return e ? CENG(e)->clipboardClipKind() : -1;
}

// --- block clip clipboard (multi-selection) ---------------------------------
namespace {
std::vector<std::pair<int32_t,int32_t>> makeSel(const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    std::vector<std::pair<int32_t,int32_t>> sel;
    if (track_ids && clip_indices && n > 0) {
        sel.reserve(n);
        for (int32_t i = 0; i < n; ++i) sel.emplace_back(track_ids[i], clip_indices[i]);
    }
    return sel;
}
} // namespace

NotaResult nota_clips_block_copy(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->copyClipBlock(makeSel(track_ids, clip_indices, n)) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clips_block_cut(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->cutClipBlock(makeSel(track_ids, clip_indices, n)) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_clips_block_paste(NotaEngine* e, double at_beat, int32_t source_track_id) {
    return e ? ENG(e)->pasteClipBlock(at_beat, source_track_id) : 0;
}
double nota_clips_block_duplicate(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    return e ? ENG(e)->duplicateClipBlock(makeSel(track_ids, clip_indices, n)) : -1.0;
}
int32_t nota_clips_block_count(NotaEngine* e) {
    return e ? CENG(e)->clipboardBlockCount() : 0;
}
// Writes the last block paste/duplicate's placed clips into out_track_ids/out_clip_indices
// (up to cap pairs) and returns the total count produced.
int32_t nota_clips_last_placed(NotaEngine* e, int32_t* out_track_ids, int32_t* out_clip_indices, int32_t cap) {
    if (!e) return 0;
    const auto& placed = CENG(e)->lastPlaced();
    const int32_t n = static_cast<int32_t>(placed.size());
    if (out_track_ids && out_clip_indices) {
        const int32_t m = std::min(n, cap);
        for (int32_t i = 0; i < m; ++i) { out_track_ids[i] = placed[i].first; out_clip_indices[i] = placed[i].second; }
    }
    return n;
}
NotaResult nota_clips_delete_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    std::vector<int32_t> ids; if (track_ids && n > 0) ids.assign(track_ids, track_ids + n);
    return ENG(e)->deleteClipsInRange(ids, start, end) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
double nota_clips_duplicate_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return -1.0;
    std::vector<int32_t> ids; if (track_ids && n > 0) ids.assign(track_ids, track_ids + n);
    return ENG(e)->duplicateRange(ids, start, end);
}
NotaResult nota_clips_split_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    std::vector<int32_t> ids; if (track_ids && n > 0) ids.assign(track_ids, track_ids + n);
    return ENG(e)->splitClipsAtRange(ids, start, end) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
void nota_engine_set_automation_lock(NotaEngine* e, int32_t locked) { if (e) ENG(e)->setAutomationLock(locked != 0); }
int32_t nota_engine_automation_lock(const NotaEngine* e) { return (e && CENG(e)->automationLock()) ? 1 : 0; }
int32_t nota_engine_last_move_kept_device_automation(const NotaEngine* e) {
    return (e && CENG(e)->lastMoveKeptDeviceAutomation()) ? 1 : 0;
}
NotaResult nota_clip_set_name(NotaEngine* e, int32_t track_id, int32_t clip_index, const char* name) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipName(track_id, clip_index, name ? std::string(name) : std::string{}) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_clip_get_name(NotaEngine* e, int32_t track_id, int32_t clip_index, char* out, int32_t cap) {
    return e ? CENG(e)->clipName(track_id, clip_index, out, cap) : 0;
}
NotaResult nota_track_set_name(NotaEngine* e, int32_t track_id, const char* name) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setTrackName(track_id, name ? std::string(name) : std::string{}) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_track_get_name(NotaEngine* e, int32_t track_id, char* out, int32_t cap) {
    return e ? CENG(e)->trackName(track_id, out, cap) : 0;
}
NotaResult nota_track_set_color(NotaEngine* e, int32_t track_id, int32_t color_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setTrackColorIndex(track_id, color_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_track_get_color(NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->trackColorIndex(track_id) : -1;
}
NotaResult nota_track_copy(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->copyTrack(track_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_track_paste(NotaEngine* e) {
    return e ? ENG(e)->pasteTrack() : -1;
}
int32_t nota_track_has_clipboard(NotaEngine* e) {
    return (e && CENG(e)->hasTrackClipboard()) ? 1 : 0;
}
NotaResult nota_track_set_record_input(NotaEngine* e, int32_t track_id, int32_t source) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setTrackRecordInput(track_id, source);
    return NOTA_OK;
}
int32_t nota_track_get_record_input(NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->trackRecordInput(track_id) : 0;
}
NotaResult nota_track_set_midi_source(NotaEngine* e, int32_t track_id, int32_t source_track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setTrackMidiSource(track_id, source_track_id);
    return NOTA_OK;
}
int32_t nota_track_get_midi_source(NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->trackMidiSource(track_id) : -1;
}

// --- freeze (M7) ---
int64_t nota_track_freeze_begin(NotaEngine* e, int32_t track_id, double length_beats) {
    return e ? ENG(e)->beginFreeze(track_id, length_beats) : 0;
}
void nota_track_freeze_end(NotaEngine* e, int32_t track_id) {
    if (e) ENG(e)->endFreeze(track_id);
}
void nota_track_freeze_cancel(NotaEngine* e) {
    if (e) ENG(e)->cancelFreeze();
}
void nota_track_unfreeze(NotaEngine* e, int32_t track_id) {
    if (e) ENG(e)->unfreeze(track_id);
}
int32_t nota_track_is_frozen(const NotaEngine* e, int32_t track_id) {
    return (e && CENG(e)->trackFrozen(track_id)) ? 1 : 0;
}
int32_t nota_track_freeze_get_state(const NotaEngine* e, int32_t track_id, uint8_t* out, int32_t cap) {
    return e ? CENG(e)->freezeGetState(track_id, out, cap) : 0;
}
void nota_track_freeze_set_state(NotaEngine* e, int32_t track_id, const uint8_t* data, int32_t size) {
    if (e) ENG(e)->freezeSetState(track_id, data, size);
}

int32_t nota_track_add_midi_clip(NotaEngine* e, int32_t track_id, double start_beat, double length_beats) {
    if (!e) return -1;
    return ENG(e)->addMidiClip(track_id, start_beat, length_beats);
}
NotaResult nota_clip_set_notes(NotaEngine* e, int32_t track_id, int32_t clip_index,
                               const NotaNoteData* notes, int32_t count) {
    if (!e || (count > 0 && !notes) || count < 0) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipNotes(track_id, clip_index, notes, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_clip_set_notes_live(NotaEngine* e, int32_t track_id, int32_t clip_index,
                                    const NotaNoteData* notes, int32_t count) {
    if (!e || (count > 0 && !notes) || count < 0) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setClipNotesLive(track_id, clip_index, notes, count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_engine_live_held_notes(const NotaEngine* e, int32_t* out, int32_t max_n) {
    return e ? CENG(e)->liveHeldNotes(out, max_n) : 0;
}
int32_t nota_clip_get_notes(const NotaEngine* e, int32_t track_id, int32_t clip_index,
                            NotaNoteData* out, int32_t max_notes) {
    if (!e || !out || max_notes <= 0) return 0;
    return CENG(e)->getClipNotes(track_id, clip_index, out, max_notes);
}
int32_t nota_clip_note_count(const NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? CENG(e)->clipNoteCount(track_id, clip_index) : 0;
}

NotaResult nota_track_set_armed(NotaEngine* e, int32_t track_id, int32_t armed) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->setTrackArmed(track_id, armed != 0); return NOTA_OK;
}

// ---- Audio persistence (M7-6b) --------------------------------------------

int32_t nota_track_get_audio_clip_info(const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaAudioClipInfo* out) {
    return (e && CENG(e)->audioClipInfo(track_id, clip_index, out)) ? 1 : 0;
}
int32_t nota_track_add_audio_clip_ex(NotaEngine* e, int32_t track_id, const char* path,
                                     double start_beat, double source_offset_frames,
                                     int64_t length_frames, float gain) {
    if (!e || !path) return -1;
    return ENG(e)->addAudioClipEx(track_id, std::string(path), start_beat, source_offset_frames, length_frames, gain);
}
int32_t nota_sample_get_info(const NotaEngine* e, int64_t sample_id, NotaSampleInfo* out) {
    return (e && CENG(e)->sampleInfo(sample_id, out)) ? 1 : 0;
}
int64_t nota_sample_read(const NotaEngine* e, int64_t sample_id, float* out, int64_t cap) {
    return e ? CENG(e)->sampleRead(sample_id, out, cap) : 0;
}
int32_t nota_track_sampler_info(const NotaEngine* e, int32_t track_id, NotaSamplerInfo* out) {
    return (e && CENG(e)->samplerInfo(track_id, out)) ? 1 : 0;
}

} // extern "C"
