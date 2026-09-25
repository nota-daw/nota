// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Background audio import -------------------------------------------

    /// <summary>Opens a block-wise decode of a WAV/FLAC/MP3 (null if unreadable). Doesn't
    /// touch the engine, so the returned job can be driven from a worker thread.</summary>
    public IAudioImport? OpenAudioImport(string path)
    {
        ThrowIfDisposed();
        var h = NativeMethods.AudioImportOpen(path);
        return h == IntPtr.Zero ? null : new AudioImport(h);
    }

    /// <summary>Places a finished import on a track. Returns the clip index, or -1.</summary>
    public int AddImportedAudioClip(int trackId, IAudioImport import, double startBeat)
    {
        ThrowIfDisposed();
        if (import is not AudioImport ai) throw new ArgumentException("Not a NotaEngine import.", nameof(import));
        return NativeMethods.TrackAddImportedClip(_handle, trackId, ai.Handle, startBeat);
    }

    /// <summary>Auto-warp with an already-detected tempo. Returns the BPM used (0 = failed).</summary>
    public double AutoWarpClipAtBpm(int trackId, int clipIndex, double bpm)
    { ThrowIfDisposed(); return NativeMethods.ClipAutoWarpBpm(_handle, trackId, clipIndex, bpm); }

    private sealed class AudioImport : IAudioImport
    {
        private IntPtr _h;
        public AudioImport(IntPtr h) => _h = h;

        internal IntPtr Handle => _h != IntPtr.Zero ? _h : throw new ObjectDisposedException(nameof(AudioImport));

        private NativeMethods.NotaAudioImportInfo Info
        {
            get { NativeMethods.AudioImportInfo(Handle, out var i); return i; }
        }

        public int Channels => Info.Channels;
        public double SampleRate => Info.SampleRate;
        public long TotalFrames => Info.TotalFrames;
        public long DecodedFrames => Info.DecodedFrames;
        public bool IsDone => Info.Done != 0;

        public int Step(long maxFrames) => NativeMethods.AudioImportStep(Handle, maxFrames);
        public int ReadPeaks(float[] outMinMax, int maxPoints)
            => NativeMethods.AudioImportPeaks(Handle, outMinMax, Math.Min(maxPoints, outMinMax.Length / 2));

        public float[] ReadPeakTable()
        {
            long n = NativeMethods.AudioImportPeakTable(Handle, null, 0);
            if (n <= 0) return Array.Empty<float>();
            var t = new float[n];
            NativeMethods.AudioImportPeakTable(Handle, t, n);
            return t;
        }

        public bool SeedPeakTable(float[] table)
            => NativeMethods.AudioImportSeedPeakTable(Handle, table, table.Length) != 0;

        public double DetectTempo() => NativeMethods.AudioImportDetectTempo(Handle);

        public void Dispose()
        {
            var h = Interlocked.Exchange(ref _h, IntPtr.Zero);
            if (h != IntPtr.Zero) NativeMethods.AudioImportClose(h);
        }
    }
}
