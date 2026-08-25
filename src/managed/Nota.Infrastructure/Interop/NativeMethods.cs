// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>
/// Raw P/Invoke bindings to nota.engine's C ABI (see nota_engine.h).
/// Mirrors the header exactly; do not add logic here — wrap it in
/// <see cref="NotaEngine"/> instead. Bindings are split by domain across
/// the NativeMethods.*.cs partials; this file holds the shared plumbing and
/// the engine-global slice (lifecycle, transport, master, realtime, render).
/// </summary>
internal static partial class NativeMethods
{
    // Resolves to libnota_engine.dylib on macOS (placed next to the app binary).
    private const string Lib = "nota_engine";

    internal enum NotaResult
    {
        Ok = 0,
        Unknown = 1,
        InvalidArg = 2,
        AudioDevice = 3,
        AlreadyRunning = 4,
        NotRunning = 5,
    }

    [LibraryImport(Lib, EntryPoint = "nota_engine_version")]
    internal static partial IntPtr Version();

    [LibraryImport(Lib, EntryPoint = "nota_engine_create")]
    internal static partial IntPtr Create();

    [LibraryImport(Lib, EntryPoint = "nota_engine_destroy")]
    internal static partial void Destroy(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_start")]
    internal static partial NotaResult Start(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_stop")]
    internal static partial NotaResult Stop(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_test_tone")]
    internal static partial NotaResult SetTestTone(IntPtr engine, int enabled);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_frequency")]
    internal static partial NotaResult SetFrequency(IntPtr engine, float hz);

    [LibraryImport(Lib, EntryPoint = "nota_engine_sample_rate")]
    internal static partial double SampleRate(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_play")]
    internal static partial NotaResult TransportPlay(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_stop")]
    internal static partial NotaResult TransportStop(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_set_bpm")]
    internal static partial NotaResult SetBpm(IntPtr engine, double bpm);

    [LibraryImport(Lib, EntryPoint = "nota_transport_bpm")]
    internal static partial double TransportBpm(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_set_time_signature")]
    internal static partial NotaResult SetTimeSignature(IntPtr engine, int num, int denom);

    [LibraryImport(Lib, EntryPoint = "nota_transport_set_loop")]
    internal static partial NotaResult SetLoop(IntPtr engine, int enabled, double startBeat, double endBeat);

    [LibraryImport(Lib, EntryPoint = "nota_transport_set_metronome")]
    internal static partial NotaResult SetMetronome(IntPtr engine, int enabled);

    [LibraryImport(Lib, EntryPoint = "nota_transport_seek")]
    internal static partial NotaResult Seek(IntPtr engine, double beat);

    [LibraryImport(Lib, EntryPoint = "nota_transport_position_beats")]
    internal static partial double PositionBeats(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_is_playing")]
    internal static partial int IsPlaying(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_loop_enabled")]
    internal static partial int LoopEnabled(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_loop_start")]
    internal static partial double LoopStart(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_transport_loop_end")]
    internal static partial double LoopEnd(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_master_volume")]
    internal static partial NotaResult SetMasterVolume(IntPtr engine, float volume);

    [LibraryImport(Lib, EntryPoint = "nota_engine_track_meter")]
    internal static partial int TrackMeter(IntPtr engine, int trackId, out NotaMeter meter);

    [LibraryImport(Lib, EntryPoint = "nota_engine_master_meter")]
    internal static partial NotaResult MasterMeter(IntPtr engine, out NotaMeter meter);

    [LibraryImport(Lib, EntryPoint = "nota_engine_note_on")]
    internal static partial NotaResult NoteOn(IntPtr engine, int pitch, float velocity);

    [LibraryImport(Lib, EntryPoint = "nota_engine_note_off")]
    internal static partial NotaResult NoteOff(IntPtr engine, int pitch);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_audition_track")]
    internal static partial void SetAuditionTrack(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_recording")]
    internal static partial NotaResult SetRecording(IntPtr engine, int enabled);

    [LibraryImport(Lib, EntryPoint = "nota_engine_is_recording")]
    internal static partial int IsRecording(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_record_start_status")]
    internal static partial int RecordStartStatus(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_audio_record_track")]
    internal static partial int AudioRecordTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_audio_record_start_beat")]
    internal static partial double AudioRecordStartBeat(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_audio_record_length_beats")]
    internal static partial double AudioRecordLengthBeats(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_master_track_id")]
    internal static partial int MasterTrackId(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_audio_record_peaks")]
    internal static partial int AudioRecordPeaks(IntPtr engine, [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_engine_poll")]
    internal static partial NotaResult Poll(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_undo")]
    internal static partial NotaResult Undo(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_redo")]
    internal static partial NotaResult Redo(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_can_undo")]
    internal static partial int CanUndo(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_can_redo")]
    internal static partial int CanRedo(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_reset")]
    internal static partial void Reset(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_render_offline")]
    internal static partial NotaResult RenderOffline(IntPtr engine, [Out] float[] outBuffer, int frames);

    [LibraryImport(Lib, EntryPoint = "nota_engine_render_offline_at")]
    internal static partial NotaResult RenderOfflineAt(IntPtr engine, [Out] float[] outBuffer, int frames, double sampleRate);

    [LibraryImport(Lib, EntryPoint = "nota_engine_preview_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult PreviewFile(IntPtr engine, string path);

    [LibraryImport(Lib, EntryPoint = "nota_engine_stop_preview")]
    internal static partial NotaResult StopPreview(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_preview_active")]
    internal static partial int PreviewActive(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_preview_selftest")]
    internal static partial int PreviewSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_xrun_count")]
    internal static partial int XrunCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_xrun_selftest")]
    internal static partial int XrunSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_cpu_load")]
    internal static partial float CpuLoad(IntPtr engine);
}
