// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — arrangement clips, waveform/level peaks, geometry, warp, envelopes,
// block/range editing, MIDI clip notes, held notes, note expression, and CC lanes.

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"
#include "nota_engine_internal.h"

#include <algorithm>
#include <cstdint>
#include <string>
#include <utility>
#include <vector>

namespace {

std::vector<std::pair<int32_t, int32_t>> makeSelection(const int32_t* trackIds, const int32_t* clipIndices, int32_t n) {
    std::vector<std::pair<int32_t, int32_t>> sel;
    if (trackIds && clipIndices && n > 0) {
        sel.reserve(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i) sel.emplace_back(trackIds[i], clipIndices[i]);
    }
    return sel;
}

std::vector<int32_t> makeTrackList(const int32_t* trackIds, int32_t n) {
    std::vector<int32_t> ids;
    if (trackIds && n > 0) ids.assign(trackIds, trackIds + n);
    return ids;
}

} // namespace

extern "C" {

int32_t nota_clip_get_peaks(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .peaks(nota::engine::ClipPeakMode::Played, out_min_max, max_points)
             : 0;
}

int32_t nota_clip_get_source_peaks(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .peaks(nota::engine::ClipPeakMode::Source, out_min_max, max_points)
             : 0;
}

int32_t nota_clip_get_warp_full_peaks(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .peaks(nota::engine::ClipPeakMode::WarpFull, out_min_max, max_points)
             : 0;
}

int32_t nota_clip_get_warp_peaks_range(const NotaEngine* e, int32_t track_id, int32_t clip_index, double beat0,
    double beat1, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .warpPeaksRange(beat0, beat1, out_min_max, max_points)
             : 0;
}

int32_t nota_clip_get_source_peaks_range(const NotaEngine* e, int32_t track_id, int32_t clip_index, double frame0,
    double frame1, float* out_min_max, int32_t max_points) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .sourcePeaksRange(frame0, frame1, out_min_max, max_points)
             : 0;
}

int32_t nota_clip_get_levels(const NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t view, double a,
    double b, float* out_min_max, float* out_rms, int32_t max_points) {
    if (!e || !out_min_max || max_points <= 0) return 0;
    const auto clip = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index);
    switch (view) {
    case NOTA_CLIP_LEVELS_PLAYED:
        return clip.peaks(nota::engine::ClipPeakMode::Played, out_min_max, max_points, out_rms);
    case NOTA_CLIP_LEVELS_SOURCE:
        return clip.peaks(nota::engine::ClipPeakMode::Source, out_min_max, max_points, out_rms);
    case NOTA_CLIP_LEVELS_WARP_FULL:
        return clip.peaks(nota::engine::ClipPeakMode::WarpFull, out_min_max, max_points, out_rms);
    case NOTA_CLIP_LEVELS_WARP_RANGE: return clip.warpPeaksRange(a, b, out_min_max, max_points, out_rms);
    case NOTA_CLIP_LEVELS_SOURCE_RANGE: return clip.sourcePeaksRange(a, b, out_min_max, max_points, out_rms);
    default: return 0;
    }
}

// ---- Arrangement geometry & clip editing (M4) ----------------------------

int32_t nota_track_get_clip_info(const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaClipInfo* out) {
    if (!e || !out) return 0;
    const auto ci = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).info();
    if (!ci) return 0;
    *out = {ci->startBeat, ci->lengthBeats, ci->kind, ci->active};
    return 1;
}

NotaResult nota_clip_move(NotaEngine* e, int32_t track_id, int32_t clip_index, double new_start_beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).move(new_start_beat)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_move_to_track(
    NotaEngine* e, int32_t src_track_id, int32_t clip_index, int32_t dest_track_id, double new_start_beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), src_track_id)
                   .clip(clip_index)
                   .moveToTrack(dest_track_id, new_start_beat)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clips_block_move(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices,
    const int32_t* dest_track_ids, const double* new_starts, int32_t n) {
    if (!e || !track_ids || !clip_indices || !dest_track_ids || !new_starts || n <= 0) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::ClipMoveSpec> moves;
    moves.reserve(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i) moves.push_back({track_ids[i], clip_indices[i], dest_track_ids[i], new_starts[i]});
    return nota::c_abi::eng(e).tracks().moveClipBlock(moves) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_trim(
    NotaEngine* e, int32_t track_id, int32_t clip_index, double new_start_beat, double new_length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .trim(new_start_beat, new_length_beats)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_resize_audio(
    NotaEngine* e, int32_t track_id, int32_t clip_index, double new_start_beat, double new_length_beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .resizeAudio(new_start_beat, new_length_beats)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_source_region(
    NotaEngine* e, int32_t track_id, int32_t clip_index, double offset_frames, int64_t length_frames) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setSourceRegion(offset_frames, length_frames)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_warp_trim(
    NotaEngine* e, int32_t track_id, int32_t clip_index, double play_start, double play_end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setWarpTrim(play_start, play_end)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_gain(NotaEngine* e, int32_t track_id, int32_t clip_index, float gain) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setGain(gain)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_get_adsr(const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaClipAdsr* out) {
    if (!e || !out) return 0;
    const auto a = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).adsr();
    if (!a) return 0;
    *out = {a->attackBeats, a->decayBeats, a->releaseBeats, a->sustain, 0};
    return 1;
}

NotaResult nota_clip_set_adsr(NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaClipAdsr* adsr) {
    if (!e || !adsr) return NOTA_ERR_INVALID_ARG;
    const nota::engine::ClipAdsr a{adsr->attack_beats, adsr->decay_beats, adsr->release_beats, adsr->sustain};
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setAdsr(a) ? NOTA_OK
                                                                                               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_active(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t active) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setActive(active != 0)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_pitch(NotaEngine* e, int32_t track_id, int32_t clip_index, float semitones) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setPitch(semitones)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_reverse(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t reversed) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setReverse(reversed != 0)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_warp(NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t enabled, int32_t mode) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setWarp(enabled != 0, mode)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_warp_length(NotaEngine* e, int32_t track_id, int32_t clip_index, double beats) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setWarpLength(beats)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

double nota_clip_auto_warp(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).autoWarp() : 0.0;
}

void nota_engine_set_defer_warp_build(NotaEngine* e, int32_t defer) {
    if (e) nota::c_abi::eng(e).diagnostics().setDeferWarpBuild(defer != 0);
}

int64_t nota_engine_warp_build_step(NotaEngine* e, int32_t max_frames) {
    return e ? nota::c_abi::eng(e).diagnostics().warpBuildStep(max_frames) : 0;
}

int64_t nota_engine_warp_build_poll(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).diagnostics().warpBuildPoll() : 0;
}

int64_t nota_engine_warp_build_remaining(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().warpBuildRemainingFrames() : 0;
}

double nota_clip_beat_warp(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).beatWarp() : 0.0;
}

int32_t nota_clip_volume_env_get(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaAutomationPoint* out, int32_t cap) {
    if (!e) return 0;
    const auto clip = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index);
    if (!out || cap <= 0) return clip.getAudioVolumeEnvelope(nullptr, 0);
    std::vector<nota::engine::AutomationPointData> tmp(static_cast<size_t>(cap));
    const int32_t n = clip.getAudioVolumeEnvelope(tmp.data(), cap);
    const int32_t m = std::min(n, cap);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCAutoPoint(tmp[static_cast<size_t>(i)]);
    return n;
}

NotaResult nota_clip_volume_env_set(
    NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = nota::c_abi::convertAutoPointsIn(pts, count);
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setAudioVolumeEnvelope(v.empty() ? nullptr : v.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_pan_env_get(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaAutomationPoint* out, int32_t cap) {
    if (!e) return 0;
    const auto clip = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index);
    if (!out || cap <= 0) return clip.getAudioPanEnvelope(nullptr, 0);
    std::vector<nota::engine::AutomationPointData> tmp(static_cast<size_t>(cap));
    const int32_t n = clip.getAudioPanEnvelope(tmp.data(), cap);
    const int32_t m = std::min(n, cap);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCAutoPoint(tmp[static_cast<size_t>(i)]);
    return n;
}

NotaResult nota_clip_pan_env_set(
    NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = nota::c_abi::convertAutoPointsIn(pts, count);
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setAudioPanEnvelope(v.empty() ? nullptr : v.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_midi_clip_env_get(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t kind, NotaAutomationPoint* out, int32_t cap) {
    if (!e) return 0;
    const auto clip = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index);
    if (!out || cap <= 0) return clip.getMidiEnvelope(kind, nullptr, 0);
    std::vector<nota::engine::AutomationPointData> tmp(static_cast<size_t>(cap));
    const int32_t n = clip.getMidiEnvelope(kind, tmp.data(), cap);
    const int32_t m = std::min(n, cap);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCAutoPoint(tmp[static_cast<size_t>(i)]);
    return n;
}

NotaResult nota_midi_clip_env_set(
    NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t kind, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = nota::c_abi::convertAutoPointsIn(pts, count);
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setMidiEnvelope(kind, v.empty() ? nullptr : v.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_warp_markers(
    NotaEngine* e, int32_t track_id, int32_t clip_index, const double* src, const double* beats, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).setWarpMarkers(src, beats, count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_get_warp_markers(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, double* out_src, double* out_beat, int32_t max_count) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .warpMarkers(out_src, out_beat, max_count)
             : 0;
}

int32_t nota_clip_split(NotaEngine* e, int32_t track_id, int32_t clip_index, double at_beat) {
    return e ? nota::engine::internal::EngineDirect::splitClip(nota::c_abi::eng(e), track_id, clip_index, at_beat) : -1;
}

void nota_engine_set_split_fades(NotaEngine* e, int32_t on) {
    if (e) nota::c_abi::eng(e).tracks().setSplitFades(on != 0);
}

int32_t nota_engine_split_fades(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).tracks().splitFades()) ? 1 : 0;
}

int32_t nota_clip_duplicate(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).duplicate() : -1;
}

NotaResult nota_clip_delete(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).remove() ? NOTA_OK
                                                                                             : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_copy(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).copyClip(clip_index) ? NOTA_OK
                                                                                        : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_cut(NotaEngine* e, int32_t track_id, int32_t clip_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).cutClip(clip_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_paste(NotaEngine* e, int32_t track_id, double at_beat) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).pasteClip(at_beat) : -1;
}

int32_t nota_clip_clipboard_kind(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).tracks().clipboardClipKind() : -1;
}

NotaResult nota_clips_fade_splits(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().fadeTouchingSplits(makeSelection(track_ids, clip_indices, n))
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clips_block_copy(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().copyClipBlock(makeSelection(track_ids, clip_indices, n)) ? NOTA_OK
                                                                                                 : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clips_block_cut(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().cutClipBlock(makeSelection(track_ids, clip_indices, n)) ? NOTA_OK
                                                                                                : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clips_block_paste(NotaEngine* e, double at_beat, int32_t dest_track_id) {
    return e ? nota::c_abi::eng(e).tracks().pasteClipBlock(at_beat, dest_track_id) : 0;
}

double nota_clips_block_duplicate(NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n) {
    return e ? nota::c_abi::eng(e).tracks().duplicateClipBlock(makeSelection(track_ids, clip_indices, n)) : -1.0;
}

int32_t nota_clips_block_count(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).tracks().clipboardBlockCount() : 0;
}

NotaResult nota_clips_copy_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().copyRange(makeTrackList(track_ids, n), start, end) ? NOTA_OK
                                                                                           : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clips_cut_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().cutRange(makeTrackList(track_ids, n), start, end) ? NOTA_OK
                                                                                          : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clips_to_session(
    NotaEngine* e, const int32_t* track_ids, const int32_t* clip_indices, int32_t n, int32_t start_scene) {
    return e ? nota::c_abi::eng(e).session().arrangementClipsToSession(
                   makeSelection(track_ids, clip_indices, n), start_scene)
             : -1;
}

int32_t nota_clips_block_paste_to_session(NotaEngine* e, int32_t dest_track_id, int32_t start_scene) {
    return e ? nota::c_abi::eng(e).session().pasteClipBlockToSession(dest_track_id, start_scene) : -1;
}

int32_t nota_session_slots_to_arrangement(
    NotaEngine* e, const int32_t* track_ids, const int32_t* scenes, int32_t n, double at_beat, int32_t dest_track_id) {
    return e ? nota::c_abi::eng(e).session().slotsToArrangement(
                   makeSelection(track_ids, scenes, n), at_beat, dest_track_id)
             : 0;
}

int32_t nota_clips_last_placed(NotaEngine* e, int32_t* out_track_ids, int32_t* out_clip_indices, int32_t cap) {
    if (!e) return 0;
    const auto tracks = nota::c_abi::eng(e).tracks();
    const auto& placed = tracks.lastPlacedClips();
    const int32_t n = static_cast<int32_t>(placed.size());
    if (out_track_ids && out_clip_indices && cap > 0) {
        const int32_t m = std::min(n, cap);
        for (int32_t i = 0; i < m; ++i) {
            out_track_ids[i] = placed[static_cast<size_t>(i)].first;
            out_clip_indices[i] = placed[static_cast<size_t>(i)].second;
        }
    }
    return n;
}

NotaResult nota_clips_delete_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().deleteClipsInRange(makeTrackList(track_ids, n), start, end)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

double nota_clips_duplicate_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return -1.0;
    return nota::c_abi::eng(e).tracks().duplicateRange(makeTrackList(track_ids, n), start, end);
}

NotaResult nota_arrangement_insert_time(NotaEngine* e, double at, double len, int32_t duplicate) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().insertTime(at, len, duplicate != 0) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clips_split_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().splitClipsAtRange(makeTrackList(track_ids, n), start, end)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clips_consolidate_range(NotaEngine* e, const int32_t* track_ids, int32_t n, double start, double end) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().consolidateRange(makeTrackList(track_ids, n), start, end)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

void nota_engine_set_automation_lock(NotaEngine* e, int32_t locked) {
    if (e) nota::c_abi::eng(e).tracks().setAutomationLock(locked != 0);
}

int32_t nota_engine_automation_lock(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).tracks().automationLock()) ? 1 : 0;
}

int32_t nota_engine_last_move_kept_device_automation(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).tracks().lastMoveKeptDeviceAutomation()) ? 1 : 0;
}

NotaResult nota_clip_set_name(NotaEngine* e, int32_t track_id, int32_t clip_index, const char* name) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setName(name ? std::string(name) : std::string{})
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_clip_get_name(NotaEngine* e, int32_t track_id, int32_t clip_index, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).name(), out, cap);
}

// ---- MIDI clips, notes, expression & CCs ----------------------------------

int32_t nota_track_add_midi_clip(NotaEngine* e, int32_t track_id, double start_beat, double length_beats) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).addMidiClip(start_beat, length_beats) : -1;
}

NotaResult nota_clip_set_notes(
    NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaNoteData* notes, int32_t count) {
    if (!e || count < 0 || (count > 0 && !notes)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::NoteData> conv;
    if (count > 0) {
        conv.reserve(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) conv.push_back(nota::c_abi::toCppNote(notes[i]));
    }
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setNotes(conv.empty() ? nullptr : conv.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_clip_set_notes_live(
    NotaEngine* e, int32_t track_id, int32_t clip_index, const NotaNoteData* notes, int32_t count) {
    if (!e || count < 0 || (count > 0 && !notes)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::NoteData> conv;
    if (count > 0) {
        conv.reserve(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i) conv.push_back(nota::c_abi::toCppNote(notes[i]));
    }
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .setNotesLive(conv.empty() ? nullptr : conv.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_engine_live_held_notes(const NotaEngine* e, int32_t* out, int32_t max_n) {
    return e ? nota::c_abi::ceng(e).midi().liveHeldNotes(out, max_n) : 0;
}

int32_t nota_engine_record_held_notes(const NotaEngine* e, NotaRecordHeldNote* out, int32_t max_n, int32_t* track_id,
    int32_t* session_scene, double* now_beat) {
    if (!e) {
        if (track_id) *track_id = 0;
        if (session_scene) *session_scene = -1;
        if (now_beat) *now_beat = 0.0;
        return 0;
    }
    const auto rec = nota::c_abi::ceng(e).recorder();
    if (!out || max_n <= 0) return rec.heldNotes(nullptr, 0, track_id, session_scene, now_beat);
    std::vector<nota::engine::RecordHeldNote> tmp(static_cast<size_t>(max_n));
    const int32_t n = rec.heldNotes(tmp.data(), max_n, track_id, session_scene, now_beat);
    const int32_t m = std::min(n, max_n);
    for (int32_t i = 0; i < m; ++i) {
        const auto& src = tmp[static_cast<size_t>(i)];
        out[i] = {src.pitch, src.source, src.startBeat, src.velocity};
    }
    return n;
}

int32_t nota_clip_get_notes(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, NotaNoteData* out, int32_t max_notes) {
    if (!e || !out || max_notes <= 0) return 0;
    std::vector<nota::engine::NoteData> tmp(static_cast<size_t>(max_notes));
    const int32_t n =
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).notes(tmp.data(), max_notes);
    const int32_t m = std::min(n, max_notes);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCNote(tmp[static_cast<size_t>(i)]);
    return n;
}

int32_t nota_clip_note_count(const NotaEngine* e, int32_t track_id, int32_t clip_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).noteCount() : 0;
}

int32_t nota_note_expr_create(NotaEngine* e, const NotaExprPoint* points, int32_t count) {
    if (!e || !points || count <= 0) return 0;
    std::vector<nota::engine::ExprPointData> conv;
    conv.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) conv.push_back({points[i].dim, points[i].beat, points[i].value});
    return nota::c_abi::eng(e).tracks().createNoteExpr(conv.data(), count);
}

int32_t nota_note_expr_points(const NotaEngine* e, int32_t expr_id, NotaExprPoint* out, int32_t max_points) {
    if (!e) return 0;
    if (!out || max_points <= 0) return nota::c_abi::ceng(e).tracks().noteExprPoints(expr_id, nullptr, 0);
    std::vector<nota::engine::ExprPointData> tmp(static_cast<size_t>(max_points));
    const int32_t n = nota::c_abi::ceng(e).tracks().noteExprPoints(expr_id, tmp.data(), max_points);
    const int32_t m = std::min(n, max_points);
    for (int32_t i = 0; i < m; ++i) {
        const auto& pt = tmp[static_cast<size_t>(i)];
        out[i] = {pt.dim, pt.beat, pt.value};
    }
    return n;
}

int32_t nota_midi_clip_ctl_list(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t* out_ctls, int32_t cap) {
    if (!e || clip_index < 0) return 0;
    return nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index).ctlList(out_ctls, cap);
}

int32_t nota_midi_clip_ctl_get(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t ctl, NotaAutomationPoint* out, int32_t cap) {
    if (!e || clip_index < 0) return 0;
    const auto clip = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).clip(clip_index);
    if (!out || cap <= 0) return clip.ctlGet(ctl, nullptr, 0);
    std::vector<nota::engine::AutomationPointData> tmp(static_cast<size_t>(cap));
    const int32_t n = clip.ctlGet(ctl, tmp.data(), cap);
    const int32_t m = std::min(n, cap);
    for (int32_t i = 0; i < m; ++i) out[i] = nota::c_abi::toCAutoPoint(tmp[static_cast<size_t>(i)]);
    return n;
}

NotaResult nota_midi_clip_ctl_set(
    NotaEngine* e, int32_t track_id, int32_t clip_index, int32_t ctl, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || clip_index < 0 || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = nota::c_abi::convertAutoPointsIn(pts, count);
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .clip(clip_index)
                   .ctlSet(ctl, v.empty() ? nullptr : v.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

} // extern "C"
