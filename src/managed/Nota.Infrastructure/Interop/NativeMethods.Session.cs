// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;
using Nota.Application;

namespace Nota.Infrastructure;

/// <summary>Session view (M5): scenes/slots, launch/record, transfer, notes, audio slots.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_session_scene_count")]
    internal static partial int SessionSceneCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_add_midi_clip")]
    internal static partial NotaResult SessionAddMidiClip(IntPtr engine, int trackId, int scene, double lengthBeats);

    [LibraryImport(Lib, EntryPoint = "nota_session_slot_state")]
    internal static partial int SessionSlotState(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_slot_length")]
    internal static partial double SessionSlotLength(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_slot_length")]
    internal static partial NotaResult SessionSetSlotLength(IntPtr engine, int trackId, int scene, double lengthBeats);

    [LibraryImport(Lib, EntryPoint = "nota_session_slot_gain")]
    internal static partial float SessionSlotGain(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_slot_gain")]
    internal static partial NotaResult SessionSetSlotGain(IntPtr engine, int trackId, int scene, float gain);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_launch_quant")]
    internal static partial NotaResult SessionSetLaunchQuant(IntPtr engine, double beats);

    [LibraryImport(Lib, EntryPoint = "nota_session_launch_slot")]
    internal static partial NotaResult SessionLaunchSlot(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_stop_slot")]
    internal static partial NotaResult SessionStopSlot(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_session_launch_scene")]
    internal static partial NotaResult SessionLaunchScene(IntPtr engine, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_stop_scene")]
    internal static partial NotaResult SessionStopScene(IntPtr engine, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_stop_all")]
    internal static partial NotaResult SessionStopAll(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_back_to_arrangement")]
    internal static partial NotaResult SessionBackToArrangement(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_arrangement_active")]
    internal static partial int ArrangementActive(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_clear_slot")]
    internal static partial NotaResult SessionClearSlot(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_add_scene")]
    internal static partial int SessionAddScene(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_remove_scene")]
    internal static partial NotaResult SessionRemoveScene(IntPtr engine, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_record_slot")]
    internal static partial NotaResult SessionRecordSlot(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_stop_record")]
    internal static partial NotaResult SessionStopRecord(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_slot_to_arrangement")]
    internal static partial int SessionSlotToArrangement(IntPtr engine, int trackId, int scene, double startBeat);

    [LibraryImport(Lib, EntryPoint = "nota_session_from_arrangement")]
    internal static partial NotaResult SessionFromArrangement(IntPtr engine, int trackId, int clipIndex, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_audio_from_arrangement")]
    internal static partial NotaResult SessionAudioFromArrangement(IntPtr engine, int trackId, int clipIndex, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_audio_record_selftest")]
    internal static partial int SessionAudioRecordSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_notes")]
    internal static partial NotaResult SessionSetNotes(IntPtr engine, int trackId, int scene, [In] NotaNote[] notes, int count);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_notes")]
    internal static partial int SessionGetNotes(IntPtr engine, int trackId, int scene, [Out] NotaNote[] outNotes, int maxNotes);

    [LibraryImport(Lib, EntryPoint = "nota_session_note_count")]
    internal static partial int SessionNoteCount(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_audio_slot")]
    internal static partial int SessionGetAudioSlot(IntPtr engine, int trackId, int scene, out NotaSessionAudioSlot info);

    [LibraryImport(Lib, EntryPoint = "nota_session_add_audio_clip", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SessionAddAudioClip(IntPtr engine, int trackId, int scene, string path,
                                                    double lengthBeats, double sourceOffsetFrames,
                                                    long lengthFrames, float gain);

    [LibraryImport(Lib, EntryPoint = "nota_session_add_audio_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SessionAddAudioFile(IntPtr engine, int trackId, int scene, string path);

    // --- Session P0 -------------------------------------------------------------
    [LibraryImport(Lib, EntryPoint = "nota_session_get_clip_props")]
    internal static partial int SessionGetClipProps(IntPtr engine, int trackId, int scene, out NotaSessionClipProps props);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_clip_props")]
    internal static partial NotaResult SessionSetClipProps(IntPtr engine, int trackId, int scene, in NotaSessionClipProps props);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_clip_name")]
    internal static partial int SessionGetClipName(IntPtr engine, int trackId, int scene, [Out] byte[] outBuf, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_clip_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult SessionSetClipName(IntPtr engine, int trackId, int scene, string name);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_slot_stop_button")]
    internal static partial int SessionGetSlotStopButton(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_slot_stop_button")]
    internal static partial NotaResult SessionSetSlotStopButton(IntPtr engine, int trackId, int scene, int on);

    [LibraryImport(Lib, EntryPoint = "nota_session_copy_slot")]
    internal static partial NotaResult SessionCopySlot(IntPtr engine, int srcTrack, int srcScene, int dstTrack, int dstScene);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_scene_props")]
    internal static partial int SessionGetSceneProps(IntPtr engine, int scene, out NotaSceneProps props);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_scene_props")]
    internal static partial NotaResult SessionSetSceneProps(IntPtr engine, int scene, in NotaSceneProps props);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_scene_name")]
    internal static partial int SessionGetSceneName(IntPtr engine, int scene, [Out] byte[] outBuf, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_scene_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult SessionSetSceneName(IntPtr engine, int scene, string name);

    [LibraryImport(Lib, EntryPoint = "nota_session_insert_scene")]
    internal static partial int SessionInsertScene(IntPtr engine, int at);

    [LibraryImport(Lib, EntryPoint = "nota_session_duplicate_scene")]
    internal static partial int SessionDuplicateScene(IntPtr engine, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_capture_scene")]
    internal static partial int SessionCaptureScene(IntPtr engine, int at);

    [LibraryImport(Lib, EntryPoint = "nota_session_launch_slot_vel")]
    internal static partial NotaResult SessionLaunchSlotVel(IntPtr engine, int trackId, int scene, float velocity);

    [LibraryImport(Lib, EntryPoint = "nota_session_release_slot")]
    internal static partial NotaResult SessionReleaseSlot(IntPtr engine, int trackId, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_track_back_to_arrangement")]
    internal static partial NotaResult SessionTrackBackToArrangement(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_session_playing_slot")]
    internal static partial int SessionPlayingSlot(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_session_slot_position")]
    internal static partial double SessionSlotPosition(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_launch_quant")]
    internal static partial double SessionGetLaunchQuant(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_follow")]
    internal static partial NotaResult SessionSetFollow(IntPtr engine, int on);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_follow")]
    internal static partial int SessionGetFollow(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_set_record_length")]
    internal static partial NotaResult SessionSetRecordLength(IntPtr engine, double beats);

    [LibraryImport(Lib, EntryPoint = "nota_session_get_record_length")]
    internal static partial double SessionGetRecordLength(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_session_record_scene")]
    internal static partial int SessionRecordScene(IntPtr engine, int scene);

    [LibraryImport(Lib, EntryPoint = "nota_session_record_target")]
    internal static partial int SessionRecordTarget(IntPtr engine, out int trackId, out int scene, out double elapsed);

    [LibraryImport(Lib, EntryPoint = "nota_session_slot_peaks")]
    internal static partial int SessionSlotPeaks(IntPtr engine, int trackId, int scene, [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_session_move_scene")]
    internal static partial NotaResult SessionMoveScene(IntPtr engine, int from, int to);

    [LibraryImport(Lib, EntryPoint = "nota_session_take_scene_tempo")]
    internal static partial int SessionTakeSceneTempo(IntPtr engine, out double bpm, out int num, out int den);
}
