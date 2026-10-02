// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Background audio import (block-wise decode off the UI thread).</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NotaAudioImportInfo
    {
        public int Channels;
        public double SampleRate;
        public long TotalFrames;
        public long DecodedFrames;
        public int Done;
    }

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_open", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr AudioImportOpen(string path);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_close")]
    internal static partial void AudioImportClose(IntPtr job);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_step")]
    internal static partial int AudioImportStep(IntPtr job, long maxFrames);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_info")]
    internal static partial NotaResult AudioImportInfo(IntPtr job, out NotaAudioImportInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_peaks")]
    internal static partial int AudioImportPeaks(IntPtr job, [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_peak_table")]
    internal static partial long AudioImportPeakTable(IntPtr job, [Out] float[]? outTable, long maxFloats);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_seed_peak_table")]
    internal static partial int AudioImportSeedPeakTable(IntPtr job, float[] table, long count);

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_detect_tempo")]
    internal static partial double AudioImportDetectTempo(IntPtr job);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_imported_clip")]
    internal static partial int TrackAddImportedClip(IntPtr engine, int trackId, IntPtr job, double startBeat);

    [LibraryImport(Lib, EntryPoint = "nota_clip_auto_warp_bpm")]
    internal static partial double ClipAutoWarpBpm(IntPtr engine, int trackId, int clipIndex, double bpm);

    /// <summary>Mirrors NotaSampleAnalysis (doubles first, so no padding surprises).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NotaSampleAnalysis
    {
        public double DurationSec;
        public double SampleRate;
        public double Bpm;
        public int Channels;
        public int KeyTonic;
        public int KeyMode;
        public float KeyConfidence;
        public float TailRatio;
        public float PeakDb;
        public float RmsDb;
        public fixed float Timbre[16];   // NOTA_TIMBRE_DIMS

        public readonly Nota.Application.Samples.SampleAnalysis ToAnalysis()
        {
            var t = new float[16];
            for (int i = 0; i < t.Length; i++) t[i] = Timbre[i];
            return new(DurationSec, SampleRate, Channels, Bpm, KeyTonic, KeyMode, KeyConfidence,
                       TailRatio, PeakDb, RmsDb, t);
        }
    }

    [LibraryImport(Lib, EntryPoint = "nota_audio_import_analyze")]
    internal static partial NotaResult AudioImportAnalyze(IntPtr job, out NotaSampleAnalysis analysis);

    [LibraryImport(Lib, EntryPoint = "nota_sample_analyze_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult SampleAnalyzeFile(string path, double maxSeconds, out NotaSampleAnalysis analysis);
}
