// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Take lanes (specs/067): the passes of an audio loop recording and the comp built from them.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_track_take_count")]
    internal static partial int TakeCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_take_info")]
    internal static partial int TakeInfo(IntPtr engine, int trackId, int index, out NotaTakeInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_track_take_peaks")]
    internal static partial int TakePeaks(IntPtr engine, int trackId, int index, [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_track_take_comp")]
    internal static partial NotaResult TakeComp(IntPtr engine, int trackId, int takeId, double startBeat, double endBeat);

    [LibraryImport(Lib, EntryPoint = "nota_track_take_delete")]
    internal static partial NotaResult TakeDelete(IntPtr engine, int trackId, int takeId);

    [LibraryImport(Lib, EntryPoint = "nota_track_takes_flatten")]
    internal static partial NotaResult TakesFlatten(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_clip_take_id")]
    internal static partial int ClipTakeId(IntPtr engine, int trackId, int clipIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_restore_take", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RestoreTake(IntPtr engine, int trackId, string path, int number, int group,
                                            double originBeat, double sourceOffsetFrames, long lengthFrames);

    [LibraryImport(Lib, EntryPoint = "nota_clip_set_take_id")]
    internal static partial NotaResult ClipSetTakeId(IntPtr engine, int trackId, int clipIndex, int takeId);

    [LibraryImport(Lib, EntryPoint = "nota_take_record_test")]
    internal static partial int TakeRecordTest(IntPtr engine, int trackId, float[] interleavedStereo, int frames, double startBeat);
}
