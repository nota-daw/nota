// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Tracks, mixer strips, send/return buses, arrangement clips & MIDI notes, audio-clip/sample persistence.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_add_audio_track")]
    internal static partial int AddAudioTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_audio_clip", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddAudioClip(IntPtr engine, int trackId, string path, double startBeat);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_volume")]
    internal static partial NotaResult SetTrackVolume(IntPtr engine, int trackId, float volume);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_pan")]
    internal static partial NotaResult SetTrackPan(IntPtr engine, int trackId, float pan);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_mute")]
    internal static partial NotaResult SetTrackMute(IntPtr engine, int trackId, int mute);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_solo")]
    internal static partial NotaResult SetTrackSolo(IntPtr engine, int trackId, int solo);

    [LibraryImport(Lib, EntryPoint = "nota_engine_track_count")]
    internal static partial int TrackCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_duplicate_track")]
    internal static partial int DuplicateTrack(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_remove_track")]
    internal static partial NotaResult RemoveTrack(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_move_track")]
    internal static partial NotaResult MoveTrack(IntPtr engine, int trackId, int toIndex);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_group_track")]
    internal static partial int AddGroupTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_create_group")]
    internal static partial int CreateGroup(IntPtr engine, [In] int[] trackIds, int n);

    [LibraryImport(Lib, EntryPoint = "nota_engine_ungroup")]
    internal static partial NotaResult Ungroup(IntPtr engine, int groupId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_track_group")]
    internal static partial NotaResult SetTrackGroup(IntPtr engine, int trackId, int groupId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_return_track")]
    internal static partial int AddReturnTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_send")]
    internal static partial NotaResult SetTrackSend(IntPtr engine, int trackId, int bus, float level);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_send")]
    internal static partial float GetTrackSend(IntPtr engine, int trackId, int bus);

    [LibraryImport(Lib, EntryPoint = "nota_engine_return_track_count")]
    internal static partial int ReturnTrackCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_return_index")]
    internal static partial int TrackReturnIndex(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_get_track_info")]
    internal static partial int GetTrackInfo(IntPtr engine, int index, out NotaTrackInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_clip_info")]
    internal static partial int GetClipInfo(IntPtr engine, int trackId, int clipIndex, out NotaClipInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_clip_move")]
    internal static partial NotaResult ClipMove(IntPtr engine, int trackId, int clipIndex, double newStartBeat);

    [LibraryImport(Lib, EntryPoint = "nota_clip_move_to_track")]
    internal static partial NotaResult ClipMoveToTrack(IntPtr engine, int srcTrackId, int clipIndex, int destTrackId, double newStartBeat);

    [LibraryImport(Lib, EntryPoint = "nota_clip_trim")]
    internal static partial NotaResult ClipTrim(IntPtr engine, int trackId, int clipIndex, double newStartBeat, double newLengthBeats);

    [LibraryImport(Lib, EntryPoint = "nota_clip_resize_audio")]
    internal static partial NotaResult ClipResizeAudio(IntPtr engine, int trackId, int clipIndex, double newStartBeat, double newLengthBeats);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_source_region")]
    internal static partial NotaResult ClipSetSourceRegion(IntPtr engine, int trackId, int clipIndex, double offsetFrames, long lengthFrames);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_warp_trim")]
    internal static partial NotaResult ClipSetWarpTrim(IntPtr engine, int trackId, int clipIndex, double playStart, double playEnd);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_gain")]
    internal static partial NotaResult ClipSetGain(IntPtr engine, int trackId, int clipIndex, float gain);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_active")]
    internal static partial NotaResult ClipSetActive(IntPtr engine, int trackId, int clipIndex, int active);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_pitch")]
    internal static partial NotaResult ClipSetPitch(IntPtr engine, int trackId, int clipIndex, float semitones);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_warp")]
    internal static partial NotaResult ClipSetWarp(IntPtr engine, int trackId, int clipIndex, int enabled, int mode);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_warp_length")]
    internal static partial NotaResult ClipSetWarpLength(IntPtr engine, int trackId, int clipIndex, double beats);

    [LibraryImport(Lib, EntryPoint = "nota_clip_auto_warp")]
    internal static partial double ClipAutoWarp(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_clip_beat_warp")]
    internal static partial double ClipBeatWarp(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_defer_warp_build")]
    internal static partial void SetDeferWarpBuild(IntPtr engine, int defer);

    [LibraryImport(Lib, EntryPoint = "nota_engine_warp_build_step")]
    internal static partial long WarpBuildStep(IntPtr engine, int maxFrames);

    [LibraryImport(Lib, EntryPoint = "nota_engine_warp_build_remaining")]
    internal static partial long WarpBuildRemaining(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_clip_volume_env_get")]
    internal static partial int ClipVolumeEnvGet(IntPtr engine, int trackId, int clipIndex, [Out] AutomationPoint[]? outPoints, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_clip_volume_env_set")]
    internal static partial NotaResult ClipVolumeEnvSet(IntPtr engine, int trackId, int clipIndex, [In] AutomationPoint[] points, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clip_pan_env_get")]
    internal static partial int ClipPanEnvGet(IntPtr engine, int trackId, int clipIndex, [Out] AutomationPoint[]? outPoints, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_clip_pan_env_set")]
    internal static partial NotaResult ClipPanEnvSet(IntPtr engine, int trackId, int clipIndex, [In] AutomationPoint[] points, int count);

    [LibraryImport(Lib, EntryPoint = "nota_midi_clip_env_get")]
    internal static partial int MidiClipEnvGet(IntPtr engine, int trackId, int clipIndex, int kind, [Out] AutomationPoint[]? outPoints, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_midi_clip_env_set")]
    internal static partial NotaResult MidiClipEnvSet(IntPtr engine, int trackId, int clipIndex, int kind, [In] AutomationPoint[] points, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_warp_markers")]
    internal static partial NotaResult ClipSetWarpMarkers(IntPtr engine, int trackId, int clipIndex, [In] double[] srcFrames, [In] double[] beats, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clip_get_warp_markers")]
    internal static partial int ClipGetWarpMarkers(IntPtr engine, int trackId, int clipIndex, [Out] double[] outSrc, [Out] double[] outBeat, int maxCount);

    [LibraryImport(Lib, EntryPoint = "nota_clip_split")]
    internal static partial int ClipSplit(IntPtr engine, int trackId, int clipIndex, double atBeat);

    [LibraryImport(Lib, EntryPoint = "nota_clip_duplicate")]
    internal static partial int ClipDuplicate(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_clip_delete")]
    internal static partial NotaResult ClipDelete(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_clip_copy")]
    internal static partial NotaResult ClipCopy(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_clip_cut")]
    internal static partial NotaResult ClipCut(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_clip_paste")]
    internal static partial int ClipPaste(IntPtr engine, int destTrackId, double atBeat);

    [LibraryImport(Lib, EntryPoint = "nota_clip_clipboard_kind")]
    internal static partial int ClipClipboardKind(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_clips_block_copy")]
    internal static partial NotaResult ClipsBlockCopy(IntPtr engine, [In] int[] trackIds, [In] int[] clipIndices, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clips_block_cut")]
    internal static partial NotaResult ClipsBlockCut(IntPtr engine, [In] int[] trackIds, [In] int[] clipIndices, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clips_block_paste")]
    internal static partial int ClipsBlockPaste(IntPtr engine, double atBeat, int destTrackId);

    [LibraryImport(Lib, EntryPoint = "nota_clips_block_duplicate")]
    internal static partial double ClipsBlockDuplicate(IntPtr engine, [In] int[] trackIds, [In] int[] clipIndices, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clips_block_count")]
    internal static partial int ClipsBlockCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_clips_delete_range")]
    internal static partial NotaResult ClipsDeleteRange(IntPtr engine, [In] int[] trackIds, int n, double start, double end);

    [LibraryImport(Lib, EntryPoint = "nota_clips_duplicate_range")]
    internal static partial double ClipsDuplicateRange(IntPtr engine, [In] int[] trackIds, int n, double start, double end);

    [LibraryImport(Lib, EntryPoint = "nota_clips_split_range")]
    internal static partial NotaResult ClipsSplitRange(IntPtr engine, [In] int[] trackIds, int n, double start, double end);

    [LibraryImport(Lib, EntryPoint = "nota_clips_consolidate_range")]
    internal static partial NotaResult ClipsConsolidateRange(IntPtr engine, [In] int[] trackIds, int n, double start, double end);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_automation_lock")]
    internal static partial void SetAutomationLock(IntPtr engine, int locked);

    [LibraryImport(Lib, EntryPoint = "nota_engine_automation_lock")]
    internal static partial int AutomationLock(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_last_move_kept_device_automation")]
    internal static partial int LastMoveKeptDeviceAutomation(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_clips_last_placed")]
    internal static partial int ClipsLastPlaced(IntPtr engine, [Out] int[]? outTrackIds, [Out] int[]? outClipIndices, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult ClipSetName(IntPtr engine, int trackId, int clipIndex, string name);

    [LibraryImport(Lib, EntryPoint = "nota_clip_get_name")]
    internal static partial int ClipGetName(IntPtr engine, int trackId, int clipIndex, [Out] byte[] outBuf, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult TrackSetName(IntPtr engine, int trackId, string name);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_name")]
    internal static partial int TrackGetName(IntPtr engine, int trackId, [Out] byte[] outBuf, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_color")]
    internal static partial NotaResult TrackSetColor(IntPtr engine, int trackId, int colorIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_color")]
    internal static partial int TrackGetColor(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_copy")]
    internal static partial NotaResult TrackCopy(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_paste")]
    internal static partial int TrackPaste(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_has_clipboard")]
    internal static partial int TrackHasClipboard(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_record_input")]
    internal static partial NotaResult TrackSetRecordInput(IntPtr engine, int trackId, int source);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_record_input")]
    internal static partial int TrackGetRecordInput(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_midi_source")]
    internal static partial NotaResult TrackSetMidiSource(IntPtr engine, int trackId, int sourceTrackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_midi_source")]
    internal static partial int TrackGetMidiSource(IntPtr engine, int trackId);

    // --- freeze (M7) ---
    [LibraryImport(Lib, EntryPoint = "nota_track_freeze_begin")]
    internal static partial long TrackFreezeBegin(IntPtr engine, int trackId, double lengthBeats);
    [LibraryImport(Lib, EntryPoint = "nota_track_freeze_end")]
    internal static partial void TrackFreezeEnd(IntPtr engine, int trackId);
    [LibraryImport(Lib, EntryPoint = "nota_track_freeze_cancel")]
    internal static partial void TrackFreezeCancel(IntPtr engine);
    [LibraryImport(Lib, EntryPoint = "nota_track_unfreeze")]
    internal static partial void TrackUnfreeze(IntPtr engine, int trackId);
    [LibraryImport(Lib, EntryPoint = "nota_track_is_frozen")]
    internal static partial int TrackIsFrozen(IntPtr engine, int trackId);
    [LibraryImport(Lib, EntryPoint = "nota_track_freeze_get_state")]
    internal static partial int TrackFreezeGetState(IntPtr engine, int trackId, [Out] byte[]? outBytes, int cap);
    [LibraryImport(Lib, EntryPoint = "nota_track_freeze_set_state")]
    internal static partial void TrackFreezeSetState(IntPtr engine, int trackId, [In] byte[] data, int size);

    [LibraryImport(Lib, EntryPoint = "nota_clip_get_peaks")]
    internal static partial int ClipGetPeaks(IntPtr engine, int trackId, int clipIndex,
                                             [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_clip_get_source_peaks")]
    internal static partial int ClipGetSourcePeaks(IntPtr engine, int trackId, int clipIndex,
                                                   [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_clip_get_warp_full_peaks")]
    internal static partial int ClipGetWarpFullPeaks(IntPtr engine, int trackId, int clipIndex,
                                                     [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_instrument_track")]
    internal static partial int AddInstrumentTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_physical_synth_track")]
    internal static partial int AddPhysicalSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_wavetable_synth_track")]
    internal static partial int AddWavetableSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_volt_synth_track")]
    internal static partial int AddVoltSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_bass_synth_track")]
    internal static partial int AddBassSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_pendulum_synth_track")]
    internal static partial int AddPendulumSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_operator_synth_track")]
    internal static partial int AddOperatorSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_grain_synth_track")]
    internal static partial int AddGrainSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_flux_synth_track")]
    internal static partial int AddFluxSynthTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_rhythm_track")]
    internal static partial int AddRhythmTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_monolith_track")]
    internal static partial int AddMonolithTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_pentad_track")]
    internal static partial int AddPentadTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_consort_track")]
    internal static partial int AddConsortTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_instrument_action")]
    internal static partial void InstrumentAction(IntPtr engine, int trackId, int id, int iarg, float farg);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_grain_sample", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetTrackGrainSample(IntPtr engine, int trackId, string path, int rootNote);

    [LibraryImport(Lib, EntryPoint = "nota_track_grain_info")]
    internal static partial int TrackGrainInfo(IntPtr engine, int trackId, out NotaSamplerInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_track_rhythm_set_voice_sample", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RhythmSetVoiceSample(IntPtr engine, int trackId, int voice, string path);

    [LibraryImport(Lib, EntryPoint = "nota_track_rhythm_voice_info")]
    internal static partial int RhythmVoiceInfo(IntPtr engine, int trackId, int voice, out NotaSamplerInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_track_rhythm_voice_source")]
    internal static partial int RhythmVoiceSource(IntPtr engine, int trackId, int voice);

    [LibraryImport(Lib, EntryPoint = "nota_track_grain_play_positions")]
    internal static partial int TrackGrainPlayPositions(IntPtr engine, int trackId, [Out] float[] outPos, int maxN);

    [LibraryImport(Lib, EntryPoint = "nota_track_active_voices")]
    internal static partial int TrackActiveVoices(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_held_notes")]
    internal static partial int TrackHeldNotes(IntPtr engine, int trackId, [Out] int[] outNotes, int maxN);

    [LibraryImport(Lib, EntryPoint = "nota_engine_live_held_notes")]
    internal static partial int LiveHeldNotes(IntPtr engine, [Out] int[] outNotes, int maxN);

    [LibraryImport(Lib, EntryPoint = "nota_track_instrument_scope")]
    internal static partial int TrackInstrumentScope(IntPtr engine, int trackId, [Out] float[] outv, int maxN);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_sampler_track", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddSamplerTrack(IntPtr engine, string path, int rootNote, int loop);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_sampler_instrument_track")]
    internal static partial int AddSamplerInstrumentTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_sampler_sample", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetTrackSamplerSample(IntPtr engine, int trackId, string path, int rootNote);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_sampler_root")]
    internal static partial int SetTrackSamplerRoot(IntPtr engine, int trackId, int rootNote);

    [LibraryImport(Lib, EntryPoint = "nota_track_sampler_play_position")]
    internal static partial float SamplerPlayPosition(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_midi_clip")]
    internal static partial int AddMidiClip(IntPtr engine, int trackId, double startBeat, double lengthBeats);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_notes")]
    internal static partial NotaResult ClipSetNotes(IntPtr engine, int trackId, int clipIndex,
                                                    [In] NotaNote[] notes, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_notes_live")]
    internal static partial NotaResult ClipSetNotesLive(IntPtr engine, int trackId, int clipIndex,
                                                        [In] NotaNote[] notes, int count);

    [LibraryImport(Lib, EntryPoint = "nota_clip_get_notes")]
    internal static partial int ClipGetNotes(IntPtr engine, int trackId, int clipIndex,
                                             [Out] NotaNote[] outNotes, int maxNotes);

    [LibraryImport(Lib, EntryPoint = "nota_clip_note_count")]
    internal static partial int ClipNoteCount(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_armed")]
    internal static partial NotaResult SetTrackArmed(IntPtr engine, int trackId, int armed);

    [LibraryImport(Lib, EntryPoint = "nota_track_get_audio_clip_info")]
    internal static partial int GetAudioClipInfo(IntPtr engine, int trackId, int clipIndex, out NotaAudioClipInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_audio_clip_ex", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddAudioClipEx(IntPtr engine, int trackId, string path, double startBeat,
                                               double sourceOffsetFrames, long lengthFrames, float gain);

    [LibraryImport(Lib, EntryPoint = "nota_sample_get_info")]
    internal static partial int SampleGetInfo(IntPtr engine, long sampleId, out NotaSampleInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_sample_read")]
    internal static partial long SampleRead(IntPtr engine, long sampleId, [Out] float[]? outBuffer, long cap);

    [LibraryImport(Lib, EntryPoint = "nota_track_sampler_info")]
    internal static partial int TrackSamplerInfo(IntPtr engine, int trackId, out NotaSamplerInfo info);
}
