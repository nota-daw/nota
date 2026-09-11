/*
 * SPDX-License-Identifier: AGPL-3.0-only
 * Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
 *
 * nota.engine — public C ABI.
 *
 * This is the ONLY interface the managed (C#) side is allowed to call.
 * Rules for this boundary (see ARCHITECTURE.md):
 *   - Pure C ABI (extern "C"), no C++ types across the line.
 *   - Opaque handles as pointers.
 *   - UTF-8 for strings, plain structs for data.
 *   - No exceptions across the boundary — integer result codes only.
 */
#ifndef NOTA_ENGINE_H
#define NOTA_ENGINE_H

#include <stdint.h>

#if defined(_WIN32)
#  define NOTA_API __declspec(dllexport)
#else
#  define NOTA_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Result codes returned across the boundary (never exceptions). */
typedef enum NotaResult {
    NOTA_OK = 0,
    NOTA_ERR_UNKNOWN = 1,
    NOTA_ERR_INVALID_ARG = 2,
    NOTA_ERR_AUDIO_DEVICE = 3,
    NOTA_ERR_ALREADY_RUNNING = 4,
    NOTA_ERR_NOT_RUNNING = 5
} NotaResult;

/* Opaque engine handle. */
typedef struct NotaEngine NotaEngine;

/* A MIDI note, as exchanged with the UI (Piano Roll). Times are in beats,
 * relative to the containing clip's start. */
typedef struct NotaNoteData {
    int32_t pitch;         /* 0..127 */
    double  start_beat;    /* relative to clip start */
    double  length_beats;
    float   velocity;      /* 0..1 */
} NotaNoteData;

/* A parameter-automation breakpoint (M9). `value` is in the target's native
 * units (volume ~0..2, pan -1..1, device param in its min..max). Sorted by beat.
 * `curve` (M9-D) shapes the segment to the next point: 0 linear, [-1,1]. */
typedef struct NotaAutomationPoint {
    double beat;
    float  value;
    float  curve;
} NotaAutomationPoint;

/* Track summary for the Arrangement View (M4). */
typedef struct NotaTrackInfo {
    int32_t id;
    int32_t type;          /* 0 = audio, 1 = instrument, 2 = return bus (M6-1), 3 = group */
    int32_t clip_count;
    int32_t muted;
    int32_t soloed;
    int32_t armed;
    float   volume;
    float   pan;
    int32_t group_id;      /* parent group track id, or -1 (top-level) */
} NotaTrackInfo;

/* Clip geometry on the timeline (M4). length_beats for audio is derived from the
 * source length at the current tempo (audio is not warped). */
typedef struct NotaClipInfo {
    double  start_beat;
    double  length_beats;
    int32_t kind;          /* 0 = audio, 1 = midi */
    int32_t active;        /* 1 = plays, 0 = deactivated (key 0): stays on the timeline but silent */
} NotaClipInfo;

/* Full audio-clip geometry for project save (M7-6b). sample_id identifies the
 * decoded buffer (0 = none); length_frames == 0 means "to end of sample". */
typedef struct NotaAudioClipInfo {
    double  start_beat;
    double  source_offset_frames;
    int64_t length_frames;
    float   gain;
    int64_t sample_id;
    float   pitch_semitones;   /* varispeed transpose (M-audio-editor) */
    int32_t warp_enabled;      /* 0/1 */
    int32_t warp_mode;         /* WarpMode: 0 Beats,1 Tones,2 Texture,3 Complex,4 ComplexPro,5 RePitch */
    double  warp_beats;        /* full warped material length in beats (0 when unwarped) */
    double  warp_play_start;   /* trimmed play window start (beats within the warp) */
    double  warp_play_end;     /* trimmed play window end (0 = to warp_beats) */
} NotaAudioClipInfo;

/* Decoded-sample metadata (M7-6b). */
typedef struct NotaSampleInfo {
    int32_t channels;
    int64_t frames;
    double  sample_rate;
} NotaSampleInfo;

/* Built-in Sampler settings for project save (M7-6b). */
typedef struct NotaSamplerInfo {
    int64_t sample_id;
    int32_t root_note;
    int32_t loop;          /* 0/1 */
} NotaSamplerInfo;

/* Captured audio session take for project save (M7-6b). */
typedef struct NotaSessionAudioSlot {
    int64_t sample_id;
    double  length_beats;
    double  source_offset_frames;
    int64_t length_frames;
    float   gain;
} NotaSessionAudioSlot;

/* Post-fader level meter reading over the last processed block (M6-2). Linear
 * amplitude 0..1 (peaks may hit 1.0 after the master clamp). */
typedef struct NotaMeter {
    float peak_l;
    float peak_r;
    float rms_l;
    float rms_r;
} NotaMeter;

/* Library version string, e.g. "0.1.0" (M0). Never NULL. */
NOTA_API const char* nota_engine_version(void);

/* Create / destroy the engine. create() returns NULL on failure. */
NOTA_API NotaEngine* nota_engine_create(void);
NOTA_API void        nota_engine_destroy(NotaEngine* engine);

/* Open the default CoreAudio output device and start the audio thread. */
NOTA_API NotaResult nota_engine_start(NotaEngine* engine);

/* Stop the audio thread and close the device. */
NOTA_API NotaResult nota_engine_stop(NotaEngine* engine);

/* M0 diagnostics: a test sine tone routed straight to the master output.
 * Both calls are lock-free and safe to call from the UI thread while the
 * audio thread is running (delivered via the command queue, AR-4/AR-8). */
NOTA_API NotaResult nota_engine_set_test_tone(NotaEngine* engine, int32_t enabled);
NOTA_API NotaResult nota_engine_set_frequency(NotaEngine* engine, float hz);

/* Current output sample rate (0 if not started). Informational. */
NOTA_API double nota_engine_sample_rate(const NotaEngine* engine);

/* ---- Audio preview / audition (M7-4a) ------------------------------------
 * Decode a file and mix it into the live output without a track, for browser
 * auditioning. One preview at a time; needs the backend running to be heard. */
NOTA_API NotaResult nota_engine_preview_file(NotaEngine* engine, const char* path);
NOTA_API NotaResult nota_engine_stop_preview(NotaEngine* engine);
NOTA_API int32_t    nota_engine_preview_active(const NotaEngine* engine);

/* ---- xrun / dropout telemetry (M7-8) -------------------------------------
 * Running count of audio-device overloads/dropouts since launch. The UI polls
 * this each tick and surfaces a non-fatal message; playback never stops. */
NOTA_API int32_t nota_engine_xrun_count(const NotaEngine* engine);
NOTA_API int32_t nota_engine_xrun_selftest(NotaEngine* engine);

/* ---- parameter automation (M9) ------------------------------------------
 * Lanes are structural data on the track (snapshot + undo/redo, M6-6). A lane
 * targets track volume (target 0), pan (1) or a built-in device param (2, with
 * device_index/param_index); add/set/remove checkpoint undo. add returns the
 * lane index (an existing lane on the same target is reused, not duplicated).
 * get_points with out=NULL returns the point count. */
NOTA_API int32_t nota_engine_automation_selftest(NotaEngine* engine);
/* M9-D: device-free check of per-segment curvature shaping. */
NOTA_API int32_t nota_engine_automation_curve_selftest(NotaEngine* engine);
/* Instrument Rack: device-free check that parallel chains sum, mute silences, a
 * macro mapping drives a child param, and the blob round-trips. */
NOTA_API int32_t nota_engine_rack_selftest(NotaEngine* engine);
/* Audio Effect Rack: device-free check of pass-through, parallel summing, mute,
 * and blob round-trip. */
NOTA_API int32_t nota_engine_rack_device_selftest(NotaEngine* engine);
/* Drum Rack: device-free check that notes route only to the matching pad chain
 * and the pad note survives the blob round-trip. */
NOTA_API int32_t nota_engine_drum_rack_selftest(NotaEngine* engine);
/* Plugin-parameter automation (M9-B, target 3). Device-free structural self-test;
 * the plugin-param introspection + lane-add C ABI arrives in B2. */
NOTA_API int32_t nota_engine_plugin_automation_selftest(NotaEngine* engine);
NOTA_API int32_t nota_track_add_automation_lane(NotaEngine* engine, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t param_index);
NOTA_API int32_t nota_track_automation_lane_count(const NotaEngine* engine, int32_t track_id);
NOTA_API NotaResult nota_track_automation_lane_info(const NotaEngine* engine, int32_t track_id,
                    int32_t lane_index, int32_t* out_target, int32_t* out_device_index,
                    int32_t* out_param_index, int32_t* out_point_count);
NOTA_API int32_t nota_track_automation_get_points(const NotaEngine* engine, int32_t track_id,
                    int32_t lane_index, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_track_automation_set_points(NotaEngine* engine, int32_t track_id,
                    int32_t lane_index, const NotaAutomationPoint* pts, int32_t count);
// Live variant (no undo checkpoint) — for real-time updates while dragging a point.
NOTA_API NotaResult nota_track_automation_set_points_live(NotaEngine* engine, int32_t track_id,
                    int32_t lane_index, const NotaAutomationPoint* pts, int32_t count);
NOTA_API NotaResult nota_track_remove_automation_lane(NotaEngine* engine, int32_t track_id, int32_t lane_index);

/* ---- CV modulation (Phase 3, Modular editor) ----------------------------
 * Modulator (LFO) sources + edges to built-in device params on the same track.
 * Modulator fields via a selector: 0 waveform, 1 tempoSync, 2 rateHz,
 * 3 rateSyncBeats, 4 depth, 5 phase. CV modes: 0 Add, 1 Multiply, 2 Override. */
NOTA_API int32_t nota_engine_modulation_selftest(NotaEngine* engine);
NOTA_API int32_t nota_track_add_modulator(NotaEngine* engine, int32_t track_id, int32_t kind);
NOTA_API NotaResult nota_track_remove_modulator(NotaEngine* engine, int32_t track_id, int32_t mod_id);
NOTA_API int32_t nota_track_modulator_count(const NotaEngine* engine, int32_t track_id);
NOTA_API int32_t nota_track_modulator_id_at(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_modulator_kind(const NotaEngine* engine, int32_t track_id, int32_t mod_id);
NOTA_API float   nota_track_modulator_get(const NotaEngine* engine, int32_t track_id, int32_t mod_id, int32_t field);
NOTA_API NotaResult nota_track_modulator_set(NotaEngine* engine, int32_t track_id, int32_t mod_id, int32_t field, float value);
NOTA_API float   nota_track_modulator_value(const NotaEngine* engine, int32_t track_id, int32_t mod_id);
NOTA_API int32_t nota_track_modulator_scope(const NotaEngine* engine, int32_t track_id, int32_t mod_id, float* out, int32_t cap);
NOTA_API int32_t nota_track_add_cv_link(NotaEngine* engine, int32_t track_id, int32_t mod_id, int32_t device_index, int32_t param_index);
/* Cross-track: modulator on track_id modulates a param on target_track (-1 = self). */
NOTA_API int32_t nota_track_add_cv_link_to(NotaEngine* engine, int32_t track_id, int32_t mod_id, int32_t target_track, int32_t device_index, int32_t param_index);
/* Param → param: a source param (src_device/src_param on track_id) drives a target param. */
NOTA_API int32_t nota_track_add_cv_link_from_param(NotaEngine* engine, int32_t track_id, int32_t src_device, int32_t src_param, int32_t target_track, int32_t target_device, int32_t target_param);
/* Target-kind variants (target_kind: 0 device, 1 instrument (dev=-1), 2 MIDI-FX). */
NOTA_API int32_t nota_track_add_cv_link_to_target(NotaEngine* engine, int32_t track_id, int32_t mod_id, int32_t target_kind, int32_t target_track, int32_t target_device, int32_t target_param);
NOTA_API int32_t nota_track_add_cv_link_from_param_target(NotaEngine* engine, int32_t track_id, int32_t src_device, int32_t src_param, int32_t target_kind, int32_t target_track, int32_t target_device, int32_t target_param);
NOTA_API int32_t nota_track_cv_link_target_kind(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_source_kind(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_source_device(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_source_param(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API NotaResult nota_track_remove_cv_link(NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_count(const NotaEngine* engine, int32_t track_id);
NOTA_API int32_t nota_track_cv_link_source(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_target_track(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_device(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_param(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API float   nota_track_cv_link_depth(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t nota_track_cv_link_mode(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API NotaResult nota_track_set_cv_link_depth(NotaEngine* engine, int32_t track_id, int32_t index, float depth);
NOTA_API NotaResult nota_track_set_cv_link_mode(NotaEngine* engine, int32_t track_id, int32_t index, int32_t mode);
NOTA_API float nota_track_cv_link_base(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API NotaResult nota_track_set_cv_link_base(NotaEngine* engine, int32_t track_id, int32_t index, float base);
NOTA_API int32_t nota_track_device_param_modulated(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API int32_t nota_track_param_modulated(const NotaEngine* engine, int32_t target_kind, int32_t track_id, int32_t device_index, int32_t param_index);

/* Master-volume automation (graph-level, M9 follow-up). get(out=NULL) returns the count. */
NOTA_API int32_t nota_engine_master_volume_automation_count(const NotaEngine* engine);
NOTA_API int32_t nota_engine_master_volume_automation_get(const NotaEngine* engine, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_engine_master_volume_automation_set(NotaEngine* engine, const NotaAutomationPoint* pts, int32_t count);

/* ---- hosted-plugin parameters (M9-B) ------------------------------------
 * device_index < 0 = the track's instrument; otherwise the effect at that chain
 * position. Values are normalized 0..1; identity is a stable string paramID. The
 * string getters return a pointer valid until the next call (engine-owned). */
NOTA_API int32_t     nota_plugin_param_count(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API const char* nota_plugin_param_id(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API const char* nota_plugin_param_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API float       nota_plugin_param_get(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API NotaResult  nota_plugin_param_set(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index, float normalized);
/* Add (or reuse) a PluginParam automation lane bound to a stable paramID; the
 * engine resolves it to the current index. Returns the lane index, or -1. */
NOTA_API int32_t     nota_track_add_plugin_automation_lane(NotaEngine* engine, int32_t track_id, int32_t device_index, const char* param_id);
/* A lane's paramID (empty for non-PluginParam lanes). Engine-owned string. */
NOTA_API const char* nota_track_automation_lane_param_id(const NotaEngine* engine, int32_t track_id, int32_t lane_index);
/* "Learn" (M9-B3): index of the param last moved in the plugin's own GUI since
 * the previous call, or -1. Consume-once. */
NOTA_API int32_t nota_plugin_last_touched_param(NotaEngine* engine, int32_t track_id, int32_t device_index);

/* ---- automation write / record (M9-C) -----------------------------------
 * Device-free self-test of the write path (Touch gesture + Write-via-arm). The
 * full mode/begin/end/arm C ABI arrives in W2. */
NOTA_API int32_t nota_engine_automation_write_selftest(NotaEngine* engine);
/* Record mode: 0=Read, 1=Touch, 2=Latch, 3=Write. begin/end bracket a control
 * gesture (Touch/Latch); arm marks a target for Write (records from play). For a
 * PluginParam target pass param_id; otherwise param_id may be NULL/empty. */
NOTA_API void    nota_engine_set_automation_write_mode(NotaEngine* engine, int32_t mode);
NOTA_API int32_t nota_engine_automation_write_mode(const NotaEngine* engine);
NOTA_API void    nota_track_begin_automation_write(NotaEngine* engine, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t param_index, const char* param_id);
NOTA_API void    nota_track_end_automation_write(NotaEngine* engine, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t param_index, const char* param_id);
NOTA_API void    nota_track_set_automation_arm(NotaEngine* engine, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t param_index, const char* param_id, int32_t armed);
NOTA_API int32_t nota_track_automation_armed(const NotaEngine* engine, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t param_index, const char* param_id);

/* ---- DSP load (Phase 11) -------------------------------------------------
 * Smoothed real-time render load, 0..1 (render time / audio block budget).
 * 0 when no live device is running. */
NOTA_API float nota_engine_cpu_load(const NotaEngine* engine);

/* ---- Transport (M1) ------------------------------------------------------ */
NOTA_API NotaResult nota_transport_play(NotaEngine* engine);
NOTA_API NotaResult nota_transport_stop(NotaEngine* engine);
NOTA_API NotaResult nota_transport_set_bpm(NotaEngine* engine, double bpm);
NOTA_API double     nota_transport_bpm(const NotaEngine* engine);
NOTA_API NotaResult nota_transport_set_time_signature(NotaEngine* engine, int32_t num, int32_t denom);
NOTA_API NotaResult nota_transport_set_loop(NotaEngine* engine, int32_t enabled,
                                            double start_beat, double end_beat);
NOTA_API NotaResult nota_transport_set_metronome(NotaEngine* engine, int32_t enabled);
NOTA_API NotaResult nota_transport_seek(NotaEngine* engine, double beat);
/* UI polls these (lock-free reads of the mirrored transport state). */
NOTA_API double     nota_transport_position_beats(const NotaEngine* engine);
NOTA_API int32_t    nota_transport_is_playing(const NotaEngine* engine);
/* Loop region (message-thread mirror) for UI display + toggling. */
NOTA_API int32_t    nota_transport_loop_enabled(const NotaEngine* engine);
NOTA_API double     nota_transport_loop_start(const NotaEngine* engine);
NOTA_API double     nota_transport_loop_end(const NotaEngine* engine);

/* ---- Mixer / master ------------------------------------------------------ */
NOTA_API NotaResult nota_engine_set_master_volume(NotaEngine* engine, float volume);

/* ---- Tracks & clips (message thread) ------------------------------------- */
/* Returns the new track id (>0), or 0 on failure. */
NOTA_API int32_t nota_engine_add_audio_track(NotaEngine* engine);
/* Loads a WAV/FLAC/MP3 file and places it at start_beat. Returns the clip index
 * within the track (>=0), or -1 on failure (bad track / decode error). */
NOTA_API int32_t nota_track_add_audio_clip(NotaEngine* engine, int32_t track_id,
                                           const char* path_utf8, double start_beat);
NOTA_API NotaResult nota_track_set_volume(NotaEngine* engine, int32_t track_id, float volume);
NOTA_API NotaResult nota_track_set_pan(NotaEngine* engine, int32_t track_id, float pan);
NOTA_API NotaResult nota_track_set_mute(NotaEngine* engine, int32_t track_id, int32_t mute);
NOTA_API NotaResult nota_track_set_solo(NotaEngine* engine, int32_t track_id, int32_t solo);
NOTA_API int32_t    nota_engine_track_count(const NotaEngine* engine);
/* Track lifecycle (context menu). duplicate deep-copies the track (instrument/
 * devices incl. hosted plugins, clips, session, automation, mixer) after the
 * source and returns the new id (-1 on failure); remove drops the track. */
NOTA_API int32_t    nota_engine_duplicate_track(NotaEngine* engine, int32_t track_id);
NOTA_API NotaResult nota_engine_remove_track(NotaEngine* engine, int32_t track_id);
/* Reorders a non-return track to to_index within the regular (non-return) track list;
 * returns/master keep their trailing slots. No-op for return tracks. Checkpoints undo. */
NOTA_API NotaResult nota_engine_move_track(NotaEngine* engine, int32_t track_id, int32_t to_index);

/* Track groups (submix buses). A Group track sums its child tracks and runs them through
 * its own device chain + fader/pan before the master; groups may nest. All checkpoint undo. */
NOTA_API int32_t    nota_engine_add_group_track(NotaEngine* engine);
NOTA_API int32_t    nota_engine_create_group(NotaEngine* engine, const int32_t* track_ids, int32_t n);
NOTA_API NotaResult nota_engine_ungroup(NotaEngine* engine, int32_t group_id);
NOTA_API NotaResult nota_engine_set_track_group(NotaEngine* engine, int32_t track_id, int32_t group_id);

/* ---- Send / return buses (M6-1) ----------------------------------------- */
/* Adds a return (aux) track: an effect bus that other tracks send to, running
 * its device chain then mixing to master. Returns id (>0), or 0 if the fixed
 * number of return slots is exhausted. */
NOTA_API int32_t nota_engine_add_return_track(NotaEngine* engine);
/* Post-fader send level (0 = off) from a track to return bus `bus`
 * (0..return_count-1). Atomic — a continuous control like the fader. */
NOTA_API NotaResult nota_track_set_send(NotaEngine* engine, int32_t track_id, int32_t bus, float level);
NOTA_API float      nota_track_get_send(const NotaEngine* engine, int32_t track_id, int32_t bus);
/* Number of return buses currently in the graph. */
NOTA_API int32_t    nota_engine_return_track_count(const NotaEngine* engine);
/* Bus slot for a return track (>=0), or -1 if the track is not a return. */
NOTA_API int32_t    nota_track_return_index(const NotaEngine* engine, int32_t track_id);

/* Fills up to max_points (min,max) float pairs (out_min_max length >= 2*max_points)
 * with waveform peaks for a clip. Returns the number of buckets written. */
NOTA_API int32_t nota_clip_get_peaks(const NotaEngine* engine, int32_t track_id,
                                     int32_t clip_index, float* out_min_max, int32_t max_points);

/* Peaks over the WHOLE sample (ignores the clip's trimmed region) for the clip
 * editor's Start/End brackets. Same output layout as nota_clip_get_peaks. */
NOTA_API int32_t nota_clip_get_source_peaks(const NotaEngine* engine, int32_t track_id,
                                            int32_t clip_index, float* out_min_max, int32_t max_points);

/* Peaks over the FULL warped material [0..warpBeats] (ignores the trim window) for the
 * clip editor's warped-clip Start/End brackets. Same output layout. */
NOTA_API int32_t nota_clip_get_warp_full_peaks(const NotaEngine* engine, int32_t track_id,
                                               int32_t clip_index, float* out_min_max, int32_t max_points);

/* ---- Instrument tracks, MIDI clips & notes (M2) -------------------------- */
/* Adds an instrument track with the built-in Nota Synth. Returns id (>0). */
NOTA_API int32_t nota_engine_add_instrument_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Physical (physical modeling). id (>0). */
NOTA_API int32_t nota_engine_add_physical_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Aurora (wavetable). id (>0). */
NOTA_API int32_t nota_engine_add_wavetable_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Volt (virtual analog). id (>0). */
NOTA_API int32_t nota_engine_add_volt_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Bass (bass synthesizer). id (>0). */
NOTA_API int32_t nota_engine_add_bass_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Pendulum (generative keys). id (>0). */
NOTA_API int32_t nota_engine_add_pendulum_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Operator (4-op FM). id (>0). */
NOTA_API int32_t nota_engine_add_operator_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Grain (granular). id (>0). */
NOTA_API int32_t nota_engine_add_grain_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Flux (vector-morph, sidechain React). id (>0). */
NOTA_API int32_t nota_engine_add_flux_synth_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Rhythm (drum machine, kind 12). id (>0). */
NOTA_API int32_t nota_engine_add_rhythm_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Monolith (mono Model-D synth, kind 13). id (>0). */
NOTA_API int32_t nota_engine_add_monolith_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Pentad (5-voice Prophet-5-style poly, kind 14). id (>0). */
NOTA_API int32_t nota_engine_add_pentad_track(NotaEngine* engine);
/* Adds an instrument track with the built-in Nota Consort (paraphonic semi-modular synth, kind 15). id (>0). */
NOTA_API int32_t nota_engine_add_consort_track(NotaEngine* engine);
/* UI editing channel for the track's instrument (e.g. Nota Rhythm step patterns):
 * id/iarg/farg are instrument-specific (see the instrument's action()). */
NOTA_API void    nota_track_instrument_action(NotaEngine* engine, int32_t track_id, int32_t id, int32_t iarg, float farg);
/* Nota Rhythm Phase 2 — per-voice one-shot samples. set returns 1 on success; info fills the
 * voice's sample id (0 = none); source returns 0 Synth / 1 Sample. */
NOTA_API int32_t nota_track_rhythm_set_voice_sample(NotaEngine* engine, int32_t track_id, int32_t voice, const char* path);
NOTA_API int32_t nota_track_rhythm_voice_info(const NotaEngine* engine, int32_t track_id, int32_t voice, NotaSamplerInfo* out);
NOTA_API int32_t nota_track_rhythm_voice_source(const NotaEngine* engine, int32_t track_id, int32_t voice);
/* Adds an instrument track with the built-in sampler loaded from a file.
 * Returns id (>0), or 0 on failure (decode error). */
NOTA_API int32_t nota_engine_add_sampler_track(NotaEngine* engine, const char* path_utf8,
                                               int32_t root_note, int32_t loop);
/* Adds an empty Sampler instrument track (a sample is loaded later). Returns id (>0). */
NOTA_API int32_t nota_engine_add_sampler_instrument_track(NotaEngine* engine);
/* Loads a sample file into an existing Sampler track (keeps its params). 1 = ok, 0 = fail. */
NOTA_API int32_t nota_track_set_sampler_sample(NotaEngine* engine, int32_t track_id, const char* path_utf8, int32_t root_note);
/* Loads a sample file into an existing Nota Grain track (kind 10, keeps params). 1 = ok. */
NOTA_API int32_t nota_track_set_grain_sample(NotaEngine* engine, int32_t track_id, const char* path_utf8, int32_t root_note);
/* Sets the Sampler's root note (lock-free). 1 = ok. */
NOTA_API int32_t nota_track_set_sampler_root(NotaEngine* engine, int32_t track_id, int32_t root_note);
/* Live playback position of the Sampler (0..1 of the sample, -1 = silent) for the UI cursor. */
NOTA_API float   nota_track_sampler_play_position(const NotaEngine* engine, int32_t track_id);
/* Adds an empty MIDI clip to an instrument track. Returns clip index (>=0) or -1. */
NOTA_API int32_t nota_track_add_midi_clip(NotaEngine* engine, int32_t track_id,
                                          double start_beat, double length_beats);
/* Replaces all notes of a MIDI clip (Piano Roll edits push the whole set). */
NOTA_API NotaResult nota_clip_set_notes(NotaEngine* engine, int32_t track_id, int32_t clip_index,
                                        const NotaNoteData* notes, int32_t count);
/* Same, but a LIVE update with no undo checkpoint (piano-roll drag pushing every frame). */
NOTA_API NotaResult nota_clip_set_notes_live(NotaEngine* engine, int32_t track_id, int32_t clip_index,
                                             const NotaNoteData* notes, int32_t count);
/* Currently-pressed live-input pitches (keyboard + MIDI). Returns count written to `out`. */
NOTA_API int32_t nota_engine_live_held_notes(const NotaEngine* engine, int32_t* out, int32_t max_n);
/* Reads notes of a clip into `out` (length >= max_notes). Returns note count written. */
NOTA_API int32_t nota_clip_get_notes(const NotaEngine* engine, int32_t track_id, int32_t clip_index,
                                     NotaNoteData* out, int32_t max_notes);
NOTA_API int32_t nota_clip_note_count(const NotaEngine* engine, int32_t track_id, int32_t clip_index);

/* ---- Live MIDI input, arming & recording (M2) ---------------------------- */
NOTA_API NotaResult nota_track_set_armed(NotaEngine* engine, int32_t track_id, int32_t armed);
/* Live note events (on-screen keyboard / CoreMIDI). Routed to armed instrument
 * tracks; captured into the record clip while recording. Lock-free. */
NOTA_API NotaResult nota_engine_note_on(NotaEngine* engine, int32_t pitch, float velocity);
NOTA_API NotaResult nota_engine_note_off(NotaEngine* engine, int32_t pitch);
/* Audition target: live notes also reach this track even when unarmed (rack/drum
 * pad preview). Pass -1 to clear. */
NOTA_API void       nota_engine_set_audition_track(NotaEngine* engine, int32_t track_id);
/* Arm/disarm recording. When enabled and the transport is playing, live notes on
 * the armed instrument track are recorded into a clip. */
NOTA_API NotaResult nota_engine_set_recording(NotaEngine* engine, int32_t enabled);
NOTA_API int32_t    nota_engine_is_recording(const NotaEngine* engine);
/* Why the last set_recording(1) did (not) start: 0 ok, 1 no armed track, 2 audio input failed. */
NOTA_API int32_t    nota_engine_record_start_status(const NotaEngine* engine);
/* Live audio-capture geometry for drawing a growing take: the armed track id
 * (0 when not capturing) and the beat where the current take began. */
NOTA_API int32_t    nota_engine_audio_record_track(const NotaEngine* engine);
NOTA_API double     nota_engine_audio_record_start_beat(const NotaEngine* engine);
/* Length (beats) captured so far in the in-progress take (for the growing live box). */
NOTA_API double     nota_engine_audio_record_length_beats(const NotaEngine* engine);
/* Reserved id for the master effect chain — pass it to the track device/name/colour ops
 * to drive the master through the same path as a track. */
NOTA_API int32_t    nota_engine_master_track_id(const NotaEngine* engine);
/* Live min/max peaks (interleaved, out_min_max[2*max_points]) over the in-progress
 * capture buffer so the take draws a growing waveform. Returns buckets written. */
NOTA_API int32_t    nota_engine_audio_record_peaks(const NotaEngine* engine, float* out_min_max, int32_t max_points);
/* Message-thread pump: materialises recorded notes into clips and drains engine
 * events. Call periodically from the UI (e.g. the position timer). */
NOTA_API NotaResult nota_engine_poll(NotaEngine* engine);

/* ---- Undo / redo (M6-6) -------------------------------------------------- */
/* Undo/redo of structural edits (tracks, clips, notes, device chains, session
 * slots). Restores a retained Graph snapshot. Returns NOTA_OK if a step was
 * applied, NOTA_ERR_UNKNOWN if there was nothing to undo/redo. Atomic params
 * (volume/pan/mute/bypass/device params) and transport are not covered. */
NOTA_API NotaResult nota_engine_undo(NotaEngine* engine);
NOTA_API NotaResult nota_engine_redo(NotaEngine* engine);
NOTA_API int32_t    nota_engine_can_undo(const NotaEngine* engine);
NOTA_API int32_t    nota_engine_can_redo(const NotaEngine* engine);

/* ---- Project load (M7-6) ------------------------------------------------- */
/* Clear the session to an empty project: stop transport, drop all tracks,
 * reset the track-id counter and undo/redo history. Message thread only. */
NOTA_API void    nota_engine_reset(NotaEngine* engine);
/* Instrument identity for save: 0=Synth, 1=Sampler, -1=plugin/unknown,
 * -2=no instrument (audio/return track). */
NOTA_API int32_t nota_track_instrument_kind(const NotaEngine* engine, int32_t track_id);
/* Built-in device kind (0=EQ,1=Comp,2=Reverb,3=Delay,4=Utility) or -1 for a
 * hosted plugin. */
NOTA_API int32_t nota_track_device_builtin_kind(const NotaEngine* engine, int32_t track_id,
                                                int32_t device_index);

/* ---- Audio persistence (M7-6b) ------------------------------------------- */
/* Full geometry of an audio clip; returns 1 if the clip is audio, else 0. */
NOTA_API int32_t nota_track_get_audio_clip_info(const NotaEngine* engine, int32_t track_id,
                                                int32_t clip_index, NotaAudioClipInfo* out);
/* Add an audio clip with restored offset/length/gain. Returns clip index or -1. */
NOTA_API int32_t nota_track_add_audio_clip_ex(NotaEngine* engine, int32_t track_id,
                                              const char* path_utf8, double start_beat,
                                              double source_offset_frames, int64_t length_frames,
                                              float gain);
/* Decoded-sample metadata by id; returns 1 if found, else 0. */
NOTA_API int32_t nota_sample_get_info(const NotaEngine* engine, int64_t sample_id, NotaSampleInfo* out);
/* Copies up to `cap` interleaved floats into `out`; returns the total count
 * (frames*channels). Pass out=NULL to query the size. */
NOTA_API int64_t nota_sample_read(const NotaEngine* engine, int64_t sample_id, float* out, int64_t cap);
/* Built-in Sampler settings; returns 1 if the instrument is a Sampler, else 0. */
NOTA_API int32_t nota_track_sampler_info(const NotaEngine* engine, int32_t track_id, NotaSamplerInfo* out);
/* Nota Grain (kind 10) sample info: sample_id / root_note into `out`. 1 = ok. */
NOTA_API int32_t nota_track_grain_info(const NotaEngine* engine, int32_t track_id, NotaSamplerInfo* out);
/* Live Nota Grain read positions (0..1) of active voices into `out`; returns the count. */
NOTA_API int32_t nota_track_grain_play_positions(const NotaEngine* engine, int32_t track_id, float* out, int32_t max_n);
/* Active synth-voice count for a built-in instrument, or -1 if unsupported. */
NOTA_API int32_t nota_track_active_voices(const NotaEngine* engine, int32_t track_id);
/* Held chord pitches (MIDI note numbers) for a generative instrument (Nota Pendulum)
   into `out`; returns the count written (0 if unsupported). */
NOTA_API int32_t nota_track_held_notes(const NotaEngine* engine, int32_t track_id, int32_t* out, int32_t max_n);
/* Live viz telemetry (e.g. the Nota Operator harmonic spectrum) into `out`; returns the
   count of floats written (0 if the instrument exposes no scope). */
NOTA_API int32_t nota_track_instrument_scope(const NotaEngine* engine, int32_t track_id, float* out, int32_t max_n);
/* Captured session audio take; returns 1 if the slot holds audio, else 0. */
NOTA_API int32_t nota_session_get_audio_slot(const NotaEngine* engine, int32_t track_id,
                                             int32_t scene, NotaSessionAudioSlot* out);
/* Load a file into a session audio slot (looped over length_beats). Returns 1 on success. */
NOTA_API int32_t nota_session_add_audio_clip(NotaEngine* engine, int32_t track_id, int32_t scene,
                                             const char* path_utf8, double length_beats,
                                             double source_offset_frames, int64_t length_frames,
                                             float gain);
/* Drop a whole audio file into a session slot (M7-5): decodes + auto-computes the
 * loop length in beats at the current tempo. 1 = ok, 0 = bad track/file. */
NOTA_API int32_t nota_session_add_audio_file(NotaEngine* engine, int32_t track_id, int32_t scene, const char* path_utf8);

/* ---- Session view (M5) --------------------------------------------------- */
/* Number of scenes (grid rows). */
NOTA_API int32_t nota_session_scene_count(const NotaEngine* engine);
/* Create an empty MIDI clip in the (track, scene) slot. */
NOTA_API NotaResult nota_session_add_midi_clip(NotaEngine* engine, int32_t track_id, int32_t scene, double length_beats);
/* Loop length of a slot in beats (0 if empty), and set it (M5-5). */
NOTA_API double nota_session_slot_length(const NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API NotaResult nota_session_set_slot_length(NotaEngine* engine, int32_t track_id, int32_t scene, double length_beats);
/* Audio-slot playback gain (linear). Get returns 1.0 when the slot isn't an audio take. */
NOTA_API float      nota_session_slot_gain(const NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API NotaResult nota_session_set_slot_gain(NotaEngine* engine, int32_t track_id, int32_t scene, float gain);
/* Slot state: 0 empty, 1 filled, 2 queued, 3 playing, 4 recording. */
NOTA_API int32_t nota_session_slot_state(const NotaEngine* engine, int32_t track_id, int32_t scene);
/* Quantized launch/stop (M5-2). Launch of an empty slot stops the track. */
NOTA_API NotaResult nota_session_set_launch_quant(NotaEngine* engine, double beats);
NOTA_API NotaResult nota_session_launch_slot(NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API NotaResult nota_session_stop_slot(NotaEngine* engine, int32_t track_id);
/* Launch/stop a whole scene (row) at once (M5-3). Launching a scene fires every
 * track's slot in that row (empty slots stop that track); stopping a scene stops
 * tracks whose playing or queued slot is that row. */
NOTA_API NotaResult nota_session_launch_scene(NotaEngine* engine, int32_t scene);
NOTA_API NotaResult nota_session_stop_scene(NotaEngine* engine, int32_t scene);
NOTA_API NotaResult nota_session_stop_all(NotaEngine* engine);
/* Session vs Arrangement (M5): launching a session clip starts the clock but leaves the
 * Arrangement inactive (non-session tracks stay silent — a session-only jam). back_to_arrangement
 * stops every session clip and re-activates the timeline; arrangement_active returns 1 when the
 * Arrangement is playing (main Play / record), 0 in a session-only jam. */
NOTA_API NotaResult nota_session_back_to_arrangement(NotaEngine* engine);
NOTA_API int32_t    nota_arrangement_active(const NotaEngine* engine);
/* Slot + scene editing (M5): clear empties a slot (stopping it if playing); add_scene appends
 * a scene row and returns its index; remove_scene drops a row (always keeps at least one). */
NOTA_API NotaResult nota_session_clear_slot(NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API int32_t    nota_session_add_scene(NotaEngine* engine);
NOTA_API NotaResult nota_session_remove_scene(NotaEngine* engine, int32_t scene);
/* Overdub-record live MIDI into a slot (M5-4): launches the slot looping and
 * captures armed input against the slot's loop clock. stop_record ends recording
 * (the slot keeps playing). */
NOTA_API NotaResult nota_session_record_slot(NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API NotaResult nota_session_stop_record(NotaEngine* engine);
/* Move MIDI between Session and Arrangement (M5-6). slot->arrangement returns the
 * new arrangement clip index (or -1); arrangement->slot returns 1/0. */
NOTA_API int32_t nota_session_slot_to_arrangement(NotaEngine* engine, int32_t track_id, int32_t scene, double start_beat);
NOTA_API NotaResult nota_session_from_arrangement(NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t scene);
/* Copy an arrangement AUDIO clip into a session slot (loops the whole sample). Returns 1/0. */
NOTA_API NotaResult nota_session_audio_from_arrangement(NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t scene);

/* Audio input recording (M4-3). Recording is normally driven via nota_engine_set_recording
 * when an audio track is armed (input capture routes automatically). This self-test feeds a
 * synthetic signal through the capture ring and materialises a clip without a device; 1 = ok. */
NOTA_API int32_t nota_audio_record_selftest(NotaEngine* engine);
/* Session audio-slot capture self-test (M5-4): feeds synthetic input into a slot
 * and plays it back through the looped renderer, all without a device; 1 = ok. */
NOTA_API int32_t nota_session_audio_record_selftest(NotaEngine* engine);
/* Preview/audition self-test (M7-4a): auditions a synthetic buffer and renders
 * it offline, asserting rms>0, all without a device; 1 = ok. */
NOTA_API int32_t nota_engine_preview_selftest(NotaEngine* engine);
/* Slot MIDI notes (like the clip notes API, addressed by scene). */
NOTA_API NotaResult nota_session_set_notes(NotaEngine* engine, int32_t track_id, int32_t scene, const NotaNoteData* notes, int32_t count);
NOTA_API int32_t nota_session_get_notes(const NotaEngine* engine, int32_t track_id, int32_t scene, NotaNoteData* out, int32_t max_notes);
NOTA_API int32_t nota_session_note_count(const NotaEngine* engine, int32_t track_id, int32_t scene);

/* ---- Arrangement geometry (M4) ------------------------------------------- */
/* Fills `out` for the track at `index` (0..track_count-1). Returns 1, or 0 if
 * the index is out of range. */
NOTA_API int32_t nota_engine_get_track_info(const NotaEngine* engine, int32_t index, NotaTrackInfo* out);
/* Fills `out` for clip `clip_index` on track `track_id`. Returns 1, or 0. */
NOTA_API int32_t nota_track_get_clip_info(const NotaEngine* engine, int32_t track_id, int32_t clip_index, NotaClipInfo* out);

/* ---- Clip editing (M4-2) ------------------------------------------------- */
/* All beat arguments are already snapped by the UI. Indices are per-track and
 * shift after split/duplicate/delete — re-query geometry afterwards. */
NOTA_API NotaResult nota_clip_move(NotaEngine* engine, int32_t track_id, int32_t clip_index, double new_start_beat);
/* Move a clip to another same-type track (instrument→instrument or audio→audio,
 * atomic). Same-track == nota_clip_move. */
NOTA_API NotaResult nota_clip_move_to_track(NotaEngine* engine, int32_t src_track_id, int32_t clip_index, int32_t source_track_id, double new_start_beat);
NOTA_API NotaResult nota_clip_trim(NotaEngine* engine, int32_t track_id, int32_t clip_index, double new_start_beat, double new_length_beats);
/* Grid resize for an audio clip (grid-relative): warped clips (or unwarped clips
 * dragged past their source length) stretch; unwarped clips within source bounds
 * trim. Auto-enables warp when a stretch is needed. */
NOTA_API NotaResult nota_clip_resize_audio(NotaEngine* engine, int32_t track_id, int32_t clip_index, double new_start_beat, double new_length_beats);
/* Set an unwarped audio clip's source region directly (clip Start/End): which
 * part of the sample plays. Timeline position (start_beat) is unchanged. No-op on
 * warped clips (their region is governed by warp markers). */
NOTA_API NotaResult nota_clip_set_source_region(NotaEngine* engine, int32_t track_id, int32_t clip_index, double offset_frames, int64_t length_frames);
/* Trim a WARPED clip's played window (clip Start/End over the warp beats).
 * Markers/warpBeats are unchanged. No-op on unwarped clips. */
NOTA_API NotaResult nota_clip_set_warp_trim(NotaEngine* engine, int32_t track_id, int32_t clip_index, double play_start, double play_end);
/* Audio-clip runtime edits: gain (linear) and varispeed transpose (semitones). */
NOTA_API NotaResult nota_clip_set_gain(NotaEngine* engine, int32_t track_id, int32_t clip_index, float gain);
NOTA_API NotaResult nota_clip_set_pitch(NotaEngine* engine, int32_t track_id, int32_t clip_index, float semitones);
/* Clip deactivate (key 0): active=0 keeps the clip on the timeline but plays nothing
 * (audio or MIDI). Works on audio and instrument tracks. */
NOTA_API NotaResult nota_clip_set_active(NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t active);
/* Warp (time-stretch to tempo). set_warp toggles + picks the mode (rebuilds the
 * stretch cache); set_warp_length sets a warped clip's target length in beats. */
NOTA_API NotaResult nota_clip_set_warp(NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t enabled, int32_t mode);
NOTA_API NotaResult nota_clip_set_warp_length(NotaEngine* engine, int32_t track_id, int32_t clip_index, double beats);
/* Auto-warp: estimate the clip's source tempo, enable warp, and snap its length
 * to the beat grid so it conforms to the project BPM. Returns the detected BPM,
 * or 0 when detection fails (clip left unchanged). */
NOTA_API double     nota_clip_auto_warp(NotaEngine* engine, int32_t track_id, int32_t clip_index);
/* Incremental warp-cache build (project load): set defer=1 before applying a project so
 * warp edits skip the heavy stretch, then drive nota_engine_warp_build_step until it
 * returns 0 to fill the caches with a progress bar. build_step does ~max_frames of
 * stretching and returns frames still remaining; warp_build_remaining reads that count. */
NOTA_API void       nota_engine_set_defer_warp_build(NotaEngine* engine, int32_t defer);
NOTA_API int64_t    nota_engine_warp_build_step(NotaEngine* engine, int32_t max_frames);
NOTA_API int64_t    nota_engine_warp_build_remaining(const NotaEngine* engine);
/* Beats-mode warp: detect transients and pin a grid-snapped warp marker at each hit
 * so percussion locks tightly to the grid. Sets Beats mode. Returns the detected BPM,
 * or 0 when detection fails (clip left unchanged). */
NOTA_API double     nota_clip_beat_warp(NotaEngine* engine, int32_t track_id, int32_t clip_index);
/* Per-clip volume envelope (M9 follow-up): 0..1 curve in clip-local beats.
 * get(out=NULL) returns the point count. */
NOTA_API int32_t    nota_clip_volume_env_get(const NotaEngine* engine, int32_t track_id, int32_t clip_index, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_clip_volume_env_set(NotaEngine* engine, int32_t track_id, int32_t clip_index, const NotaAutomationPoint* pts, int32_t count);
/* Per-clip pan envelope (M9 follow-up): -1..1 balance curve in clip-local beats. */
NOTA_API int32_t    nota_clip_pan_env_get(const NotaEngine* engine, int32_t track_id, int32_t clip_index, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_clip_pan_env_set(NotaEngine* engine, int32_t track_id, int32_t clip_index, const NotaAutomationPoint* pts, int32_t count);
/* MIDI clip envelopes (M9 follow-up). kind: 0 = velocity, 1 = volume (0..1, clip-local beats). */
NOTA_API int32_t    nota_midi_clip_env_get(const NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t kind, NotaAutomationPoint* out, int32_t cap);
NOTA_API NotaResult nota_midi_clip_env_set(NotaEngine* engine, int32_t track_id, int32_t clip_index, int32_t kind, const NotaAutomationPoint* pts, int32_t count);
/* Warp markers (source-frame ↔ beat anchors). set replaces the list (>=2, any
 * order); get fills out_src/out_beat (may be NULL) and returns the total count. */
NOTA_API NotaResult nota_clip_set_warp_markers(NotaEngine* engine, int32_t track_id, int32_t clip_index, const double* src_frames, const double* beats, int32_t count);
NOTA_API int32_t     nota_clip_get_warp_markers(const NotaEngine* engine, int32_t track_id, int32_t clip_index, double* out_src, double* out_beat, int32_t max_count);
NOTA_API int32_t    nota_clip_split(NotaEngine* engine, int32_t track_id, int32_t clip_index, double at_beat);   /* -> new clip index, or -1 */
NOTA_API int32_t    nota_clip_duplicate(NotaEngine* engine, int32_t track_id, int32_t clip_index);              /* -> new clip index, or -1 */
NOTA_API NotaResult nota_clip_delete(NotaEngine* engine, int32_t track_id, int32_t clip_index);
/* Time-range clip ops for the arrangement time-selection: split at the range boundaries
 * so only covered content is affected, in one undo step, no ripple. delete_range carves
 * [start,end) out of each listed track. duplicate_range copies that slice to
 * [end, end+(end-start)) and returns the range length (>0), or -1 on no-op. */
NOTA_API NotaResult nota_clips_delete_range(NotaEngine* engine, const int32_t* track_ids, int32_t n, double start, double end);
NOTA_API double     nota_clips_duplicate_range(NotaEngine* engine, const int32_t* track_ids, int32_t n, double start, double end);
/* split_range cuts each listed track's clips at both boundaries, keeping all content, so the
 * covered slice becomes its own clip(s). One undo step, no ripple. */
NOTA_API NotaResult nota_clips_split_range(NotaEngine* engine, const int32_t* track_ids, int32_t n, double start, double end);
/* consolidate_range (Consolidate) replaces each listed track's content in [start,end) with one
 * clip spanning the range: MIDI merges the covered notes, audio renders the covered clips
 * (gain, pitch, warp, fades, clip envelopes baked) into a new sample. Parts of clips outside
 * the range survive. One undo step; nota_clips_last_placed then reports the new clips. */
NOTA_API NotaResult nota_clips_consolidate_range(NotaEngine* engine, const int32_t* track_ids, int32_t n, double start, double end);
/* Automation-follows-clips (req 8.3). Lock freezes envelopes so clip moves don't carry them.
 * last_move_kept_device_automation is 1 when the most recent cross-track move left device/plugin
 * automation on the source (UI hint); it resets at the start of each move. */
NOTA_API void    nota_engine_set_automation_lock(NotaEngine* engine, int32_t locked);
NOTA_API int32_t nota_engine_automation_lock(const NotaEngine* engine);
NOTA_API int32_t nota_engine_last_move_kept_device_automation(const NotaEngine* engine);
/* Clip clipboard (copy/cut/paste, incl. cross-track). Copy stores a clone; paste inserts
 * it into a type-matching track at/after at_beat, shifted right so it never overlaps.
 * The clip's track automation (points inside its span) travels with it: copy captures it,
 * paste/duplicate re-land it (clearing the destination span first). Cut also removes the
 * automation from the source; delete leaves it. clipboard_kind: -1 empty, 0 audio, 1 midi. */
NOTA_API NotaResult nota_clip_copy(NotaEngine* engine, int32_t track_id, int32_t clip_index);
NOTA_API NotaResult nota_clip_cut(NotaEngine* engine, int32_t track_id, int32_t clip_index);
NOTA_API int32_t    nota_clip_paste(NotaEngine* engine, int32_t source_track_id, double at_beat); /* -> new clip index, or -1 */
NOTA_API int32_t    nota_clip_clipboard_kind(NotaEngine* engine);
/* Block clip clipboard (multi-selection). A block = a set of clips (track_ids[i],
 * clip_indices[i]) captured with their track ids, beats relative to the block start, and
 * each clip's automation. block_paste re-lands the block onto the same tracks at at_beat;
 * block_duplicate places the copy right after the block. Overlap is avoided by shifting the
 * WHOLE block by one shared delta, so relative geometry is preserved. Each op is a single
 * undo step. block_duplicate returns the block length (>0), or -1. last_placed writes the
 * (track_id, clip_index) of every clip the last paste/duplicate produced (up to cap pairs)
 * and returns the total count, so the UI can re-select them. */
NOTA_API NotaResult nota_clips_block_copy(NotaEngine* engine, const int32_t* track_ids, const int32_t* clip_indices, int32_t count);
NOTA_API NotaResult nota_clips_block_cut(NotaEngine* engine, const int32_t* track_ids, const int32_t* clip_indices, int32_t count);
NOTA_API int32_t    nota_clips_block_paste(NotaEngine* engine, double at_beat, int32_t source_track_id); /* dest -1 = source tracks; -> clips pasted */
NOTA_API double     nota_clips_block_duplicate(NotaEngine* engine, const int32_t* track_ids, const int32_t* clip_indices, int32_t count);
NOTA_API int32_t    nota_clips_block_count(NotaEngine* engine);
NOTA_API int32_t    nota_clips_last_placed(NotaEngine* engine, int32_t* out_track_ids, int32_t* out_clip_indices, int32_t cap);
/* Clip / track UI metadata: names (UTF-8) + track colour (palette slot, -1 = auto).
 * get_* write up to cap-1 chars + NUL into out and return the full length. */
NOTA_API NotaResult nota_clip_set_name(NotaEngine* engine, int32_t track_id, int32_t clip_index, const char* name_utf8);
NOTA_API int32_t    nota_clip_get_name(NotaEngine* engine, int32_t track_id, int32_t clip_index, char* out, int32_t cap);
NOTA_API NotaResult nota_track_set_name(NotaEngine* engine, int32_t track_id, const char* name_utf8);
NOTA_API int32_t    nota_track_get_name(NotaEngine* engine, int32_t track_id, char* out, int32_t cap);
NOTA_API NotaResult nota_track_set_color(NotaEngine* engine, int32_t track_id, int32_t color_index);
NOTA_API int32_t    nota_track_get_color(NotaEngine* engine, int32_t track_id);
/* Track clipboard: copy stores an independent clone, paste appends a fresh copy (new id).
 * Cut = copy + nota_engine_remove_track. has_clipboard: 1 when a track is copied. */
NOTA_API NotaResult nota_track_copy(NotaEngine* engine, int32_t track_id);
NOTA_API int32_t    nota_track_paste(NotaEngine* engine);   /* -> new track id, or -1 */
NOTA_API int32_t    nota_track_has_clipboard(NotaEngine* engine);
/* Record input source for an audio track (internal resampling): 0 = hardware input,
 * -1 = master bus, >0 = another track's post-fader output (by id). */
NOTA_API NotaResult nota_track_set_record_input(NotaEngine* engine, int32_t track_id, int32_t source);
NOTA_API int32_t    nota_track_get_record_input(NotaEngine* engine, int32_t track_id);

/* MIDI routing: forward an instrument track's MIDI to another instrument track
 * (source_track_id = -1 turns it off). */
NOTA_API NotaResult nota_track_set_midi_source(NotaEngine* engine, int32_t track_id, int32_t source_track_id);
NOTA_API int32_t    nota_track_get_midi_source(NotaEngine* engine, int32_t track_id);

/* ---- Freeze (M7) ---------------------------------------------------------- */
/* Bounce a track's post-device, pre-fader audio to a buffer and play it back in
 * place of the live instrument + device chain (CPU relief); the mixer strip stays
 * live. Drive from the UI thread with the backend stopped:
 *   frames = nota_track_freeze_begin(e, id, length_beats);   // 0 = failed/unsupported
 *   seek(0); pump nota_engine_render_offline() until `frames` are rendered;
 *   nota_track_freeze_end(e, id);                            // publishes the frozen track
 * nota_track_freeze_cancel aborts an armed capture. length_beats is the arrangement
 * length; a couple seconds of tail are added for effect ring-out. */
NOTA_API int64_t    nota_track_freeze_begin(NotaEngine* engine, int32_t track_id, double length_beats);
NOTA_API void       nota_track_freeze_end(NotaEngine* engine, int32_t track_id);
NOTA_API void       nota_track_freeze_cancel(NotaEngine* engine);
NOTA_API void       nota_track_unfreeze(NotaEngine* engine, int32_t track_id);
NOTA_API int32_t    nota_track_is_frozen(const NotaEngine* engine, int32_t track_id);
/* Opaque frozen-audio blob for project save/restore (header + interleaved PCM). get
 * copies up to `cap` bytes into out and returns the full size (out=NULL probes the
 * size); set restores it and marks the track frozen. 0 when the track isn't frozen. */
NOTA_API int32_t    nota_track_freeze_get_state(const NotaEngine* engine, int32_t track_id, uint8_t* out, int32_t cap);
NOTA_API void       nota_track_freeze_set_state(NotaEngine* engine, int32_t track_id, const uint8_t* data, int32_t size);

/* ---- Hosted plugins (M3-3) ----------------------------------------------- */
/* Create a new instrument track whose instrument is catalog plugin
 * `catalog_index`. Returns the track id (>0), or 0 on failure (bad index, not an
 * instrument, or instantiation failed). Call from the UI/main thread. */
NOTA_API int32_t nota_engine_add_plugin_instrument_track(NotaEngine* engine, int32_t catalog_index);

/* Replace the instrument on an existing instrument track with a hosted plugin. */
NOTA_API NotaResult nota_track_set_instrument_plugin(NotaEngine* engine, int32_t track_id, int32_t catalog_index);

/* Replace the instrument on an existing instrument track with a fresh built-in synth `kind`.
 * Returns NOTA_ERR_UNKNOWN for Racks / unknown kinds or non-instrument tracks. */
NOTA_API NotaResult nota_track_set_builtin_instrument(NotaEngine* engine, int32_t track_id, int32_t kind);

/* Append catalog plugin `catalog_index` as an insert effect on the track's
 * device chain. Returns the new device index (>=0), or <0 on failure. */
NOTA_API int32_t nota_track_add_effect_plugin(NotaEngine* engine, int32_t track_id, int32_t catalog_index);

/* Number of insert effects on the track's device chain. */
NOTA_API int32_t nota_track_device_count(const NotaEngine* engine, int32_t track_id);

/* Offline analysis (audio→MIDI): copy an audio clip's played region down-mixed to mono.
 * out == NULL returns the frame count; otherwise writes up to max_frames and returns the count
 * written. out_sr (optional) receives the sample's source sample rate. */
NOTA_API int32_t nota_clip_audio_mono(const NotaEngine* engine, int32_t track_id, int32_t clip_index,
                                      float* out, int32_t max_frames, double* out_sr);

/* ---- MIDI effects (before the instrument) -------------------------------- */
/* Append a built-in MIDI effect. kind: 0 = Arpeggiator. Returns index (>=0) or <0. */
NOTA_API int32_t     nota_track_add_midi_effect(NotaEngine* engine, int32_t track_id, int32_t kind);
NOTA_API int32_t     nota_track_midi_effect_count(const NotaEngine* engine, int32_t track_id);
NOTA_API NotaResult  nota_track_move_midi_effect(NotaEngine* engine, int32_t track_id, int32_t from_index, int32_t to_index);
NOTA_API NotaResult  nota_track_remove_midi_effect(NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t     nota_midi_effect_kind(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API const char* nota_midi_effect_name(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t     nota_midi_effect_param_count(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API const char* nota_midi_effect_param_name(const NotaEngine* engine, int32_t track_id, int32_t index, int32_t param_index);
NOTA_API float       nota_midi_effect_param_min(const NotaEngine* engine, int32_t track_id, int32_t index, int32_t param_index);
NOTA_API float       nota_midi_effect_param_max(const NotaEngine* engine, int32_t track_id, int32_t index, int32_t param_index);
NOTA_API float       nota_midi_effect_get_param(const NotaEngine* engine, int32_t track_id, int32_t index, int32_t param_index);
NOTA_API NotaResult  nota_midi_effect_set_param(NotaEngine* engine, int32_t track_id, int32_t index, int32_t param_index, float value);
NOTA_API void        nota_midi_effect_set_bypassed(NotaEngine* engine, int32_t track_id, int32_t index, int32_t bypassed);
NOTA_API int32_t     nota_midi_effect_bypassed(const NotaEngine* engine, int32_t track_id, int32_t index);
/* Live IN/OUT of the last note-on a MIDI effect remapped (Nota Scale readout); -1 = none. */
NOTA_API int32_t     nota_midi_effect_last_in(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t     nota_midi_effect_last_out(const NotaEngine* engine, int32_t track_id, int32_t index);
/* Float scope buffer for a MIDI effect editor (Nota Velocity in/out pairs); returns count written. */
NOTA_API int32_t     nota_midi_effect_scope(const NotaEngine* engine, int32_t track_id, int32_t index, float* out, int32_t max_n);
/* Map/CC routing: the effect's CC lane modulates an audio-device param on the track. */
NOTA_API void        nota_midi_effect_set_cc_dest(NotaEngine* engine, int32_t track_id, int32_t index, int32_t dest_device, int32_t dest_param);
NOTA_API void        nota_midi_effect_set_cc_depth(NotaEngine* engine, int32_t track_id, int32_t index, float depth);
NOTA_API int32_t     nota_midi_effect_cc_dest_device(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t     nota_midi_effect_cc_dest_param(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API float       nota_midi_effect_cc_depth(const NotaEngine* engine, int32_t track_id, int32_t index);

/* ---- Built-in devices + generic params (M4-4/5) -------------------------- */
/* Append a built-in device to the track's chain. kind: 0 = EQ, 1 = Compressor,
 * 2 = Reverb, 3 = Delay, 4 = Utility, 6 = Amp. Returns the device index (>=0), or <0. */
NOTA_API int32_t     nota_track_add_builtin_device(NotaEngine* engine, int32_t track_id, int32_t kind);
/* Reorder / remove a device in the track chain (M4.1-D). */
NOTA_API NotaResult  nota_track_move_device(NotaEngine* engine, int32_t track_id, int32_t from_index, int32_t to_index);
NOTA_API NotaResult  nota_track_remove_device(NotaEngine* engine, int32_t track_id, int32_t device_index);
/* Device display name (built-ins: "EQ"/"Compressor"; plugins: plugin name). */
NOTA_API const char* nota_device_name(const NotaEngine* engine, int32_t track_id, int32_t device_index);
/* Generic float params (built-ins expose these; hosted plugins report 0). */
NOTA_API int32_t     nota_device_param_count(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API const char* nota_device_param_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API float       nota_device_param_min(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API float       nota_device_param_max(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API float       nota_device_get_param(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API NotaResult  nota_device_set_param(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index, float value);
/* Live gain reduction (dB, >=0) of a dynamics device (built-in Compressor); 0 otherwise. */
NOTA_API float       nota_device_gain_reduction(const NotaEngine* engine, int32_t track_id, int32_t device_index);

/* Factory-default parameter values (double-click reset). */
NOTA_API float       nota_device_param_default(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t param_index);
NOTA_API float       nota_instrument_param_default(const NotaEngine* engine, int32_t track_id, int32_t param_index);
NOTA_API float       nota_midi_effect_param_default(const NotaEngine* engine, int32_t track_id, int32_t index, int32_t param_index);

/* Real-time analyzer feed: copies up to max_samples of the device's recent mono
 * signal into out (oldest->newest), returning the count written. Non-zero only for
 * the built-in EQ-8 (its pre-EQ spectrum ring). Lock-free; torn reads are fine. */
NOTA_API int32_t     nota_device_scope(const NotaEngine* engine, int32_t track_id, int32_t device_index, float* out, int32_t max_samples);
/* Interactive-device command channel (the looper's Record/Overdub/Play/Stop/Undo/
 * Clear and per-layer mute/gain). The engine applies it at the next quantize boundary. */
NOTA_API void        nota_device_action(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t id, int32_t iarg, float farg);
/* Per-layer waveform envelope for multi-layer devices (the looper): fills up to
 * max_samples peak bins of a layer's buffer (oldest->newest), returning the count. */
NOTA_API int32_t     nota_device_layer_wave(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t layer, float* out, int32_t max_samples);
/* Opaque device-state blob for project save/restore (the looper's recorded PCM).
 * get copies up to cap bytes into out and returns the full size (pass out=NULL to
 * probe the size first); set restores from data. Empty for param-only devices. */
NOTA_API int32_t     nota_device_get_state(const NotaEngine* engine, int32_t track_id, int32_t device_index, uint8_t* out, int32_t cap);
NOTA_API void        nota_device_set_state(NotaEngine* engine, int32_t track_id, int32_t device_index, const uint8_t* data, int32_t size);
/* Load an auxiliary audio file into a device (Nota Chamber: a user impulse response). */
NOTA_API NotaResult  nota_device_load_file(NotaEngine* engine, int32_t track_id, int32_t device_index, const char* path);
/* A device's resource text (Chamber: 0 IR name, 1 category, 2 user IR name, 10 built-in IR list).
   Returns the full UTF-8 length; copies up to cap-1 bytes + NUL when out is non-null. */
NOTA_API int32_t     nota_device_text(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t id, char* out, int32_t cap);
/* Sidechain routing (Phase B): point a device's detector at another track's signal.
 * source_track_id -1 clears it; get returns the current source (-1 = none). */
NOTA_API void        nota_device_set_sidechain_source(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t source_track_id);
NOTA_API int32_t     nota_device_sidechain_source(const NotaEngine* engine, int32_t track_id, int32_t device_index);
/* Whether a device can key off a sidechain source: 1 for the built-in Compressor and
 * for hosted plugins that expose a sidechain input bus (Phase C); 0 otherwise. */
NOTA_API int32_t     nota_device_accepts_sidechain(const NotaEngine* engine, int32_t track_id, int32_t device_index);
/* Instrument sidechain ("React", Nota Flux): route a listening instrument to a source
 * track. source_track_id -1 clears it; get returns the current source (-1 = none);
 * accepts is 1 when the track's instrument listens (Nota Flux), 0 otherwise. */
NOTA_API void        nota_track_set_instrument_sidechain_source(NotaEngine* engine, int32_t track_id, int32_t source_track_id);
NOTA_API int32_t     nota_track_instrument_sidechain_source(const NotaEngine* engine, int32_t track_id);
NOTA_API int32_t     nota_track_instrument_accepts_sidechain(const NotaEngine* engine, int32_t track_id);
/* Sidechain shaping (Phase D): detector gain (dB), dry/wet mix (0..1), tap point
 * (0 = post-FX post-fader, 1 = pre-FX pre-fader). Getters return sane defaults. */
NOTA_API void        nota_device_set_sidechain_gain(NotaEngine* engine, int32_t track_id, int32_t device_index, float gain_db);
NOTA_API float       nota_device_sidechain_gain(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_device_set_sidechain_mix(NotaEngine* engine, int32_t track_id, int32_t device_index, float mix);
NOTA_API float       nota_device_sidechain_mix(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_device_set_sidechain_tap_pre(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t pre);
NOTA_API int32_t     nota_device_sidechain_tap_pre(const NotaEngine* engine, int32_t track_id, int32_t device_index);
/* Deterministic DSP self-test of the built-in EQ + Compressor: 1 = sane. */
NOTA_API int32_t     nota_fx_selftest(void);

/* ---- Instrument Rack (nested container instrument) ----------------- */
/* A RackInstrument hosts N parallel chains (each an instrument + insert devices)
 * plus 8 macros. It lives on an instrument track; the calls below address into
 * it by (track_id, chain, dev, param). Rack-internal edits publish the rack's own
 * snapshot (thread-safe) and are outside engine undo in v1. Chain instruments are
 * built-in (kind 0=Synth, 2=Physical); chain devices are built-in (0..4). Chain
 * instrument params ride the normalized 0..1 plugin-param surface; chain device
 * params are native floats in [min,max]. Boolean queries return 1/0. */
NOTA_API int32_t     nota_engine_add_instrument_rack_track(NotaEngine* engine);
/* Drum Rack: an Instrument Rack whose chains are pads (each triggered by one MIDI
 * note via nota_rack_set_chain_trigger_note). Starts empty; the UI adds pads. */
NOTA_API int32_t     nota_engine_add_drum_rack_track(NotaEngine* engine);
/* Adds a Sampler chain loaded from `path` to the track's instrument rack (drum
 * pad / sampler chain). Returns the chain index or -1. Set its pad note with
 * nota_rack_set_chain_trigger_note. */
NOTA_API int32_t     nota_rack_add_sampler_chain(NotaEngine* engine, int32_t track_id, const char* path, int32_t root_note, int32_t loop);
NOTA_API int32_t     nota_rack_set_chain_sampler_sample(NotaEngine* engine, int32_t track_id, int32_t chain, const char* path, int32_t root_note);
/* Hosted plugins (VST3/AU) inside a rack: instrument as a new chain (instrument /
 * drum rack), or effect into a chain. catalog_index is the browser catalog entry. */
NOTA_API int32_t     nota_rack_add_plugin_instrument_chain(NotaEngine* engine, int32_t track_id, int32_t catalog_index);
NOTA_API int32_t     nota_rack_add_plugin_chain_device(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t catalog_index);
NOTA_API int32_t     nota_rackdev_add_plugin_chain_device(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t catalog_index);
NOTA_API int32_t     nota_rack_chain_trigger_note(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API void        nota_rack_set_chain_trigger_note(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t note);
NOTA_API int32_t     nota_rackdev_chain_trigger_note(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API void        nota_rackdev_set_chain_trigger_note(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t note);
NOTA_API int32_t     nota_rack_chain_count(const NotaEngine* engine, int32_t track_id);
NOTA_API int32_t     nota_rack_add_chain(NotaEngine* engine, int32_t track_id, int32_t inst_kind);
NOTA_API int32_t     nota_rack_remove_chain(NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_set_chain_instrument(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t inst_kind);
NOTA_API int32_t     nota_rack_chain_instrument_kind(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API const char* nota_rack_chain_instrument_name(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_chain_instrument_param_count(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API const char* nota_rack_chain_instrument_param_name(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t param);
NOTA_API const char* nota_rack_chain_instrument_param_id(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t param);
NOTA_API float       nota_rack_chain_instrument_param_get(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t param);
NOTA_API void        nota_rack_chain_instrument_param_set(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t param, float normalized);
NOTA_API float       nota_rack_chain_instrument_param_default(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t param);
/* Chain instrument GUI + preset (hosted plugins). */
NOTA_API void        nota_rack_open_chain_instrument_editor(NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API void        nota_rack_open_chain_device_editor(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev);
NOTA_API const char* nota_rack_chain_instrument_plugin_id(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_chain_instrument_get_state(const NotaEngine* engine, int32_t track_id, int32_t chain, uint8_t* out, int32_t capacity);
/* Built-in Sampler in a chain (0 / -1 if the chain instrument isn't a Sampler). */
NOTA_API int32_t     nota_rack_chain_sampler_info(const NotaEngine* engine, int32_t track_id, int32_t chain, NotaSamplerInfo* out);
NOTA_API int32_t     nota_rack_set_chain_sampler_root(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t root_note);
NOTA_API float       nota_rack_chain_sampler_play_position(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_chain_device_count(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_add_chain_device(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t device_kind);
NOTA_API int32_t     nota_rack_remove_chain_device(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev);
NOTA_API int32_t     nota_rack_move_chain_device(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t from_index, int32_t to_index);
NOTA_API const char* nota_rack_chain_device_name(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev);
NOTA_API int32_t     nota_rack_chain_device_builtin_kind(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev);
NOTA_API int32_t     nota_rack_chain_device_param_count(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev);
NOTA_API const char* nota_rack_chain_device_param_name(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev, int32_t param);
NOTA_API float       nota_rack_chain_device_param_min(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev, int32_t param);
NOTA_API float       nota_rack_chain_device_param_max(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev, int32_t param);
NOTA_API float       nota_rack_chain_device_param_get(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev, int32_t param);
NOTA_API void        nota_rack_chain_device_param_set(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev, int32_t param, float value);
NOTA_API void        nota_rack_set_chain_device_bypassed(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev, int32_t bypassed);
NOTA_API int32_t     nota_rack_chain_device_bypassed(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t dev);
NOTA_API void        nota_rack_set_chain_gain(NotaEngine* engine, int32_t track_id, int32_t chain, float v);
NOTA_API void        nota_rack_set_chain_pan (NotaEngine* engine, int32_t track_id, int32_t chain, float v);
NOTA_API void        nota_rack_set_chain_mute(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t b);
NOTA_API void        nota_rack_set_chain_solo(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t b);
NOTA_API float       nota_rack_chain_gain(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API float       nota_rack_chain_pan (const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_chain_mute(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API int32_t     nota_rack_chain_solo(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API float       nota_rack_macro_get(const NotaEngine* engine, int32_t track_id, int32_t macro);
NOTA_API void        nota_rack_macro_set(NotaEngine* engine, int32_t track_id, int32_t macro, float v);
/* Add a macro mapping. device_index < 0 targets the chain's instrument (param is
 * a plugin-param index); >= 0 targets that device (param is a generic index).
 * range_min/max are in the target's own units. Returns the mapping index or <0. */
NOTA_API int32_t     nota_rack_add_macro_mapping(NotaEngine* engine, int32_t track_id, int32_t macro, int32_t chain,
                                                 int32_t device_index, int32_t param_index, float range_min, float range_max);
NOTA_API int32_t     nota_rack_mapping_count(const NotaEngine* engine, int32_t track_id);
/* Fills the out params for mapping `index`; returns 1 on success, 0 otherwise. */
NOTA_API int32_t     nota_rack_mapping_info(const NotaEngine* engine, int32_t track_id, int32_t index,
                                            int32_t* macro, int32_t* chain, int32_t* device_index,
                                            int32_t* param_index, float* range_min, float* range_max);
NOTA_API int32_t     nota_rack_remove_mapping(NotaEngine* engine, int32_t track_id, int32_t index);
/* Instrument-Rack extras (mockup 2p): key/velocity zones, per-chain meter, named
   macros, rack output (volume/glide), macro-map range/curve editing. */
NOTA_API void        nota_rack_chain_zone(const NotaEngine* engine, int32_t track_id, int32_t chain, int32_t* key_lo, int32_t* key_hi, int32_t* vel_lo, int32_t* vel_hi);
NOTA_API void        nota_rack_set_chain_zone(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t key_lo, int32_t key_hi, int32_t vel_lo, int32_t vel_hi);
NOTA_API float       nota_rack_chain_meter(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API const char* nota_rack_macro_name(const NotaEngine* engine, int32_t track_id, int32_t macro);
NOTA_API void        nota_rack_set_macro_name(NotaEngine* engine, int32_t track_id, int32_t macro, const char* name);
NOTA_API float       nota_rack_volume(const NotaEngine* engine, int32_t track_id);
NOTA_API void        nota_rack_set_volume(NotaEngine* engine, int32_t track_id, float v);
NOTA_API float       nota_rack_glide(const NotaEngine* engine, int32_t track_id);
NOTA_API void        nota_rack_set_glide(NotaEngine* engine, int32_t track_id, float v);
NOTA_API int32_t     nota_rack_set_mapping_range(NotaEngine* engine, int32_t track_id, int32_t index, float range_min, float range_max);
NOTA_API int32_t     nota_rack_mapping_curve(const NotaEngine* engine, int32_t track_id, int32_t index);
NOTA_API int32_t     nota_rack_set_mapping_curve(NotaEngine* engine, int32_t track_id, int32_t index, int32_t curve);

/* ---- Drum Rack per-pad shaping + kit-level timing ------------------------
 * choke: 0 = none, 1..8 monophonic choke group; tune: pad transpose in
 * semitones (-48..+48); decay: 0..1 pad amp VCA (1 = sustain). swing/humanize
 * are kit-level 0..1. All addressed by the drum rack's own track (di = -1). */
NOTA_API void        nota_rack_set_chain_choke(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t group);
NOTA_API int32_t     nota_rack_chain_choke(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API void        nota_rack_set_chain_tune(NotaEngine* engine, int32_t track_id, int32_t chain, int32_t semitones);
NOTA_API int32_t     nota_rack_chain_tune(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API void        nota_rack_set_chain_decay(NotaEngine* engine, int32_t track_id, int32_t chain, float v);
NOTA_API float       nota_rack_chain_decay(const NotaEngine* engine, int32_t track_id, int32_t chain);
NOTA_API void        nota_rack_set_swing(NotaEngine* engine, int32_t track_id, float v);
NOTA_API float       nota_rack_swing(const NotaEngine* engine, int32_t track_id);
NOTA_API void        nota_rack_set_humanize(NotaEngine* engine, int32_t track_id, float v);
NOTA_API float       nota_rack_humanize(const NotaEngine* engine, int32_t track_id);

/* ---- Audio Effect Rack (a RackDevice in a track's device chain) ----------
 * Same rack surface as nota_rack_* but addressed by a device_index (the rack
 * device's position in the track chain). Its chains are effects-only, so the
 * chain-instrument calls report "no instrument". Add one via
 * nota_track_add_builtin_device(kind 5). */
NOTA_API int32_t     nota_rackdev_chain_count(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API int32_t     nota_rackdev_add_chain(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t inst_kind);
NOTA_API int32_t     nota_rackdev_remove_chain(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API int32_t     nota_rackdev_set_chain_instrument(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t inst_kind);
NOTA_API int32_t     nota_rackdev_chain_instrument_kind(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API const char* nota_rackdev_chain_instrument_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API int32_t     nota_rackdev_chain_instrument_param_count(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API const char* nota_rackdev_chain_instrument_param_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t param);
NOTA_API float       nota_rackdev_chain_instrument_param_get(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t param);
NOTA_API void        nota_rackdev_chain_instrument_param_set(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t param, float normalized);
NOTA_API int32_t     nota_rackdev_chain_device_count(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API int32_t     nota_rackdev_add_chain_device(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t device_kind);
NOTA_API int32_t     nota_rackdev_remove_chain_device(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev);
NOTA_API int32_t     nota_rackdev_move_chain_device(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t from_index, int32_t to_index);
NOTA_API const char* nota_rackdev_chain_device_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev);
NOTA_API int32_t     nota_rackdev_chain_device_builtin_kind(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev);
NOTA_API void        nota_rackdev_open_chain_device_editor(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev);
NOTA_API int32_t     nota_rackdev_chain_device_param_count(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev);
NOTA_API const char* nota_rackdev_chain_device_param_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev, int32_t param);
NOTA_API float       nota_rackdev_chain_device_param_min(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev, int32_t param);
NOTA_API float       nota_rackdev_chain_device_param_max(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev, int32_t param);
NOTA_API float       nota_rackdev_chain_device_param_get(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev, int32_t param);
NOTA_API void        nota_rackdev_chain_device_param_set(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev, int32_t param, float value);
NOTA_API void        nota_rackdev_set_chain_device_bypassed(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev, int32_t bypassed);
NOTA_API int32_t     nota_rackdev_chain_device_bypassed(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t dev);
NOTA_API void        nota_rackdev_set_chain_gain(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, float v);
NOTA_API void        nota_rackdev_set_chain_pan (NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, float v);
NOTA_API void        nota_rackdev_set_chain_mute(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t b);
NOTA_API void        nota_rackdev_set_chain_solo(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t b);
NOTA_API float       nota_rackdev_chain_gain(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API float       nota_rackdev_chain_pan (const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API int32_t     nota_rackdev_chain_mute(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API int32_t     nota_rackdev_chain_solo(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API float       nota_rackdev_macro_get(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t macro);
NOTA_API void        nota_rackdev_macro_set(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t macro, float v);
NOTA_API int32_t     nota_rackdev_add_macro_mapping(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t macro, int32_t chain,
                                                    int32_t target_device, int32_t param_index, float range_min, float range_max);
NOTA_API int32_t     nota_rackdev_mapping_count(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API int32_t     nota_rackdev_mapping_info(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t index,
                                               int32_t* macro, int32_t* chain, int32_t* target_device,
                                               int32_t* param_index, float* range_min, float* range_max);
NOTA_API int32_t     nota_rackdev_remove_mapping(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t index);

/* Audio Effect Rack: rack-out (gain / dry-wet), routing (mode 0 Parallel / 1 Series
 * / 2 Select), PDC toggle, chain selector (manual + follow-input + live position),
 * per-chain meter, chain-select zone (velLo..velHi 0..127), macro names and mapping
 * range/curve — all addressed by the rack device's deviceIndex. */
NOTA_API float       nota_rackdev_chain_meter(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain);
NOTA_API void        nota_rackdev_chain_zone(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t* velLo, int32_t* velHi);
NOTA_API void        nota_rackdev_set_chain_zone(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t chain, int32_t velLo, int32_t velHi);
NOTA_API const char* nota_rackdev_macro_name(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t macro);
NOTA_API void        nota_rackdev_set_macro_name(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t macro, const char* name);
NOTA_API int32_t     nota_rackdev_set_mapping_range(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t index, float range_min, float range_max);
NOTA_API int32_t     nota_rackdev_mapping_curve(const NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t index);
NOTA_API int32_t     nota_rackdev_set_mapping_curve(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t index, int32_t curve);
NOTA_API float       nota_rackdev_volume(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_rackdev_set_volume(NotaEngine* engine, int32_t track_id, int32_t device_index, float v);
NOTA_API int32_t     nota_rackdev_mode(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_rackdev_set_mode(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t mode);
NOTA_API float       nota_rackdev_drywet(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_rackdev_set_drywet(NotaEngine* engine, int32_t track_id, int32_t device_index, float v);
NOTA_API int32_t     nota_rackdev_pdc(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_rackdev_set_pdc(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t on);
NOTA_API float       nota_rackdev_chain_select(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_rackdev_set_chain_select(NotaEngine* engine, int32_t track_id, int32_t device_index, float v);
NOTA_API int32_t     nota_rackdev_sel_follow(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void        nota_rackdev_set_sel_follow(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t on);
NOTA_API float       nota_rackdev_live_selector(const NotaEngine* engine, int32_t track_id, int32_t device_index);

/* Open/close a hosted plugin's native GUI in a separate window (M3-4, FR-35).
 * device_index < 0 targets the track's instrument; >= 0 targets that effect in
 * the chain. No-op for built-in instruments. Call from the UI/main thread. */
NOTA_API NotaResult nota_plugin_open_editor(NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API NotaResult nota_plugin_close_editor(NotaEngine* engine, int32_t track_id, int32_t device_index);

/* Opaque plugin state (M3-5, FR-36), for project save/restore. get writes up to
 * `capacity` bytes into `out` (pass out=NULL to query the size) and returns the
 * full state size in bytes. set restores a previously saved blob. Message
 * thread. Project-file persistence itself lands with the .nota format (M7-6). */
NOTA_API int32_t    nota_plugin_get_state(const NotaEngine* engine, int32_t track_id, int32_t device_index,
                                          uint8_t* out, int32_t capacity);
NOTA_API NotaResult nota_plugin_set_state(NotaEngine* engine, int32_t track_id, int32_t device_index,
                                          const uint8_t* data, int32_t size);

/* Device bypass (M3-6, FR-37). When bypassed, the effect is skipped in the
 * render chain (buffer passes through untouched). */
NOTA_API NotaResult nota_track_set_device_bypassed(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t bypassed);
NOTA_API int32_t    nota_track_device_bypassed(const NotaEngine* engine, int32_t track_id, int32_t device_index);

/* Plugin delay compensation (M3-7, FR-38). Total reported latency of a track's
 * chain (instrument + effects), in samples; the engine delays other tracks to
 * match the highest so they stay aligned. */
NOTA_API int32_t    nota_track_latency_samples(const NotaEngine* engine, int32_t track_id);
/* Deterministic self-test of the compensation delay line: returns 1 if an
 * impulse emerges at exactly the requested delay, else 0. */
NOTA_API int32_t    nota_pdc_selftest(void);

/* Level meters (M6-2, FR-12). Fill `out` with the track's / master's post-fader
 * peak + RMS over the last processed block. track_meter returns 0 for an unknown
 * track id, else 1; master_meter always succeeds. Poll from the UI (~30 Hz). */
NOTA_API int32_t    nota_engine_track_meter(const NotaEngine* engine, int32_t track_id, NotaMeter* out);
NOTA_API NotaResult nota_engine_master_meter(const NotaEngine* engine, NotaMeter* out);

/* ---- Offline render (tests / export) ------------------------------------- */
/* Renders `frames` of interleaved stereo into out (length >= 2*frames) without
 * an audio device. Uses the engine's current sample rate. */
NOTA_API NotaResult nota_engine_render_offline(NotaEngine* engine, float* out, int32_t frames);

/* As above, but renders at `sampleRate` (used by WAV export / M6-4). Sets the
 * transport sample rate first, so the audio backend MUST be stopped before
 * calling — otherwise the audio thread and this call race on transport state. */
NOTA_API NotaResult nota_engine_render_offline_at(NotaEngine* engine, float* out,
                                                  int32_t frames, double sampleRate);

/* ---- Plugin hosting spike (M3-0) ----------------------------------------- */
/* Initialises JUCE's GUI subsystem on the calling thread and opens a small test
 * window. Purpose: verify a JUCE message loop coexists with the host app's run
 * loop (Avalonia's NSApp) — the main integration risk for M3 (see
 * ARCHITECTURE.md § Plugin hosting). MUST be called from the main/UI thread. JUCE init
 * is idempotent; repeated calls just re-show the window. Temporary entry point. */
NOTA_API NotaResult nota_pluginhost_open_test_window(void);

/* ---- Plugin scanning & catalog (M3-1, FR-33/NFR-5) ----------------------- */
/* Scans installed plugins out-of-process, AudioUnit first then VST3. Each
 * candidate is probed in a separate `worker_path` child process with a timeout,
 * so a crashing/hanging plugin cannot take down the host. Results are merged
 * into the in-memory catalog and persisted to app-support. Blocking; call from
 * the message thread. Returns the number of known plugins, or <0 on error.
 * `worker_path` is the absolute path to the nota-scanworker executable. */
NOTA_API int32_t nota_pluginhost_scan(const char* worker_path);

/* Number of plugins in the catalog (after a scan or a load of the saved list). */
NOTA_API int32_t nota_pluginhost_plugin_count(void);

/* Human-readable description line for catalog entry `index`, UTF-8:
 * "Name | Format | inst|fx | Manufacturer". Returns NULL if out of range.
 * The pointer is owned by the engine and valid until the next call. */
NOTA_API const char* nota_pluginhost_plugin_desc(int32_t index);

/* Stable identifier for catalog entry `index` (JUCE createIdentifierString),
 * used by project save to reference a plugin across machines/scans. UTF-8,
 * owned by the engine, valid until the next call. NULL if out of range. */
NOTA_API const char* nota_pluginhost_plugin_id(int32_t index);
/* Catalog index whose identifier matches `identifier`, or -1 if not found. */
NOTA_API int32_t nota_pluginhost_index_of_id(const char* identifier);
/* Stable identifier of a track's instrument / effect plugin (M7-6c). UTF-8,
 * owned by the engine, valid until the next call; empty string if not a plugin. */
NOTA_API const char* nota_track_instrument_plugin_id(const NotaEngine* engine, int32_t track_id);
NOTA_API const char* nota_track_device_plugin_id(const NotaEngine* engine, int32_t track_id, int32_t device_index);

/* Extra plugin search directories added on top of the OS defaults during a scan
 * (affects file-based formats like VST3; AudioUnit uses the system component
 * registry and ignores paths). Persisted to app-support, reloaded on init. */
NOTA_API NotaResult  nota_pluginhost_add_scan_path(const char* dir);
NOTA_API NotaResult  nota_pluginhost_remove_scan_path(int32_t index);
NOTA_API int32_t     nota_pluginhost_scan_path_count(void);
/* Directory at `index`, UTF-8, owned by the engine, valid until the next call. */
NOTA_API const char* nota_pluginhost_scan_path(int32_t index);

/* --- audio device settings (M7-1) -----------------------------------------
 * The chosen output/input device (by stable UID), sample rate and buffer size
 * are persisted to ~/Library/Application Support/Nota/audio.json and applied on
 * the next nota_audio_apply() (which restarts the audio backend). Setters only
 * stage the value; nothing changes until apply. */

/* Rescan the connected CoreAudio devices (call before reading the lists). */
NOTA_API void        nota_audio_refresh_devices(void);
/* Number of playback- / capture-capable devices from the last refresh. */
NOTA_API int32_t     nota_audio_output_device_count(void);
NOTA_API int32_t     nota_audio_input_device_count(void);
/* UID / display name of device `index`, UTF-8, owned by the engine, valid until
 * the next audio-device call. NULL if out of range. UID is the stable key to
 * pass to nota_audio_set_output_device / _input_device. */
NOTA_API const char* nota_audio_output_device_uid(int32_t index);
NOTA_API const char* nota_audio_output_device_name(int32_t index);
NOTA_API const char* nota_audio_input_device_uid(int32_t index);
NOTA_API const char* nota_audio_input_device_name(int32_t index);

/* Stage the desired config. Pass "" for a device to mean "system default",
 * 0 for sample_rate / buffer_frames to mean "device default". */
NOTA_API NotaResult  nota_audio_set_output_device(NotaEngine* engine, const char* uid);
NOTA_API NotaResult  nota_audio_set_input_device(NotaEngine* engine, const char* uid);
NOTA_API NotaResult  nota_audio_set_sample_rate(NotaEngine* engine, double sample_rate);
NOTA_API NotaResult  nota_audio_set_buffer_frames(NotaEngine* engine, int32_t frames);

/* WASAPI exclusive mode (Windows only; ignored on other platforms). When enabled,
 * Nota opens the output device exclusively for the lowest latency — other apps
 * can't play through it, and the device's native sample rate / buffer size override
 * the staged values. If the device refuses exclusive access, the backend falls
 * back to shared mode and nota_audio_exclusive_fallback() reports it. */
NOTA_API NotaResult  nota_audio_set_wasapi_exclusive(NotaEngine* engine, int32_t enabled);
NOTA_API int32_t     nota_audio_wasapi_exclusive(const NotaEngine* engine);

/* Current staged config (UTF-8 strings owned by the engine, valid until the
 * next audio-config call). */
NOTA_API const char* nota_audio_output_device(const NotaEngine* engine);
NOTA_API const char* nota_audio_input_device(const NotaEngine* engine);
NOTA_API double      nota_audio_sample_rate(const NotaEngine* engine);
NOTA_API int32_t     nota_audio_buffer_frames(const NotaEngine* engine);

/* Persist the staged config and restart the backend. NOTA_OK on success,
 * NOTA_ERR_AUDIO_DEVICE if the device could not be opened. */
NOTA_API NotaResult  nota_audio_apply(NotaEngine* engine);
/* Negotiated values after the backend started (0 if not running). */
NOTA_API double      nota_audio_negotiated_sample_rate(const NotaEngine* engine);
NOTA_API int32_t     nota_audio_negotiated_buffer_frames(const NotaEngine* engine);
/* True if the last nota_audio_apply() requested WASAPI exclusive mode but the
 * device refused it and the backend fell back to shared. Always 0 on platforms
 * without exclusive mode. */
NOTA_API int32_t     nota_audio_exclusive_fallback(const NotaEngine* engine);

/* --- MIDI device settings (M7-2) ------------------------------------------
 * Which CoreMIDI inputs the engine listens to. The chosen set is persisted as a
 * blocklist of disabled uids in ~/Library/Application Support/Nota/midi.json
 * (empty = all inputs on). set_input_enabled only stages the choice;
 * nota_midi_apply reopens the MIDI port (the audio backend keeps running). */

/* Rescan connected MIDI input sources (call before reading the list). */
NOTA_API void        nota_midi_refresh_devices(void);
/* Number of MIDI input sources from the last refresh. */
NOTA_API int32_t     nota_midi_input_device_count(void);
/* uid / display name of input `index`, UTF-8, owned by the engine, valid until
 * the next MIDI-device call. NULL if out of range. */
NOTA_API const char* nota_midi_input_device_uid(int32_t index);
NOTA_API const char* nota_midi_input_device_name(int32_t index);

/* Stage whether the input with `uid` is active (1) or muted (0). */
NOTA_API NotaResult  nota_midi_set_input_enabled(NotaEngine* engine, const char* uid, int32_t enabled);
/* 1 if the input with `uid` is currently enabled in the staged config. */
NOTA_API int32_t     nota_midi_input_enabled(const NotaEngine* engine, const char* uid);
/* Persist the staged selection and reconnect the MIDI port. NOTA_OK on success. */
NOTA_API NotaResult  nota_midi_apply(NotaEngine* engine);

/* MIDI-learn: drain incoming CC/note-on events into a caller buffer of int32 quads
 * {kind, channel, number, value}; returns the event count written (<= max). */
NOTA_API int32_t     nota_midi_poll_control_events(NotaEngine* engine, int32_t* out, int32_t max);

/* --- Gamepad input (live note source) ---------------------------------------
 * A gamepad is polled on its own thread; button edges queue internally and are
 * drained into `out` with nota_gamepad_poll_events(). The UI then maps each
 * edge to nota_engine_note_on / nota_engine_note_off — the same entry points
 * the computer keyboard uses, so live play, armed-track recording and the
 * piano-roll key highlight all reuse the existing pipeline. Only buttons are
 * reported (d-pad hats resolve to U/D/L/R buttons); sticks/triggers are not
 * note input and are ignored in v1. Currently macOS only; other platforms
 * report zero pads. */

/* One button edge: pressed = 1 down / 0 up. buttonId identifies the control
 * (1..N = HID button usage; DpadUp/Down/Left/Right = -1000..-1003).
 * `pad` is the pad slot (0..gamepad_count-1) that pressed it. */
typedef struct NotaGamepadButtonEvent {
    int32_t pressed;
    int32_t buttonId;
    int32_t pad;
} NotaGamepadButtonEvent;

typedef enum NotaGamepadButton {
    NOTA_GAMEPAD_DPAD_UP    = -1000,
    NOTA_GAMEPAD_DPAD_DOWN  = -1001,
    NOTA_GAMEPAD_DPAD_LEFT  = -1002,
    NOTA_GAMEPAD_DPAD_RIGHT = -1003
} NotaGamepadButton;

/* Start/stop the gamepad poll thread. start is a no-op if already running. */
NOTA_API void        nota_gamepad_start(NotaEngine* engine);
NOTA_API void        nota_gamepad_stop(NotaEngine* engine);

/* Number of currently connected pads (slots for NotaGamepadButtonEvent.pad). */
NOTA_API int32_t     nota_gamepad_count(NotaEngine* engine);
/* uid / display name of pad `index`, UTF-8, owned by the engine, valid until
 * the next gamepad call. NULL on out-of-range or non-macOS platforms. */
NOTA_API const char* nota_gamepad_uid(NotaEngine* engine, int32_t index);
NOTA_API const char* nota_gamepad_name(NotaEngine* engine, int32_t index);

/* Drain queued button edges into `out` (array of NotaGamepadButtonEvent).
 * Returns the count written (<= max). Call from a UI-thread tick. */
NOTA_API int32_t     nota_gamepad_poll_events(NotaEngine* engine, NotaGamepadButtonEvent* out, int32_t max);


#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* NOTA_ENGINE_H */
