// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- take lanes (specs/055) ---

    public int TakeCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TakeCount(_handle, trackId); }

    public bool TryGetTakeInfo(int trackId, int index, out NotaTakeInfo info)
    { ThrowIfDisposed(); return NativeMethods.TakeInfo(_handle, trackId, index, out info) != 0; }

    public int GetTakePeaks(int trackId, int index, float[] outMinMax, int maxPoints)
    { ThrowIfDisposed(); return NativeMethods.TakePeaks(_handle, trackId, index, outMinMax, maxPoints); }

    public bool CompTake(int trackId, int takeId, double startBeat, double endBeat)
    { ThrowIfDisposed(); return NativeMethods.TakeComp(_handle, trackId, takeId, startBeat, endBeat) == NativeMethods.NotaResult.Ok; }

    public bool DeleteTake(int trackId, int takeId)
    { ThrowIfDisposed(); return NativeMethods.TakeDelete(_handle, trackId, takeId) == NativeMethods.NotaResult.Ok; }

    public bool FlattenTakes(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TakesFlatten(_handle, trackId) == NativeMethods.NotaResult.Ok; }

    public int ClipTakeId(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipTakeId(_handle, trackId, clipIndex); }

    public int RestoreTake(int trackId, string path, int number, int group, double originBeat, double sourceOffsetFrames, long lengthFrames)
    { ThrowIfDisposed(); return NativeMethods.RestoreTake(_handle, trackId, path, number, group, originBeat, sourceOffsetFrames, lengthFrames); }

    public void SetClipTakeId(int trackId, int clipIndex, int takeId)
    { ThrowIfDisposed(); NativeMethods.ClipSetTakeId(_handle, trackId, clipIndex, takeId); }

    /// <summary>Smoke-test hook: record interleaved stereo into an audio track as a take started at
    /// <paramref name="startBeat"/> under the current loop, through the real capture path, no device.</summary>
    public bool TakeRecordTest(int trackId, float[] interleavedStereo, int frames, double startBeat)
    { ThrowIfDisposed(); return NativeMethods.TakeRecordTest(_handle, trackId, interleavedStereo, frames, startBeat) != 0; }
}
