// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

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
}
