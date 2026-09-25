// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A background decode of one audio file (see <see cref="IAudioEngine.OpenAudioImport"/>).
/// Touches no engine state, so it runs on a worker thread; it is NOT thread-safe itself —
/// drive it from one thread at a time. Hand the finished import to
/// <see cref="IAudioEngine.AddImportedAudioClip"/> on the UI thread, then dispose it.</summary>
public interface IAudioImport : IDisposable
{
    int Channels { get; }
    double SampleRate { get; }
    long TotalFrames { get; }
    long DecodedFrames { get; }
    bool IsDone { get; }

    /// <summary>Decodes up to <paramref name="maxFrames"/> more. 1 = more to do, 0 = done, -1 = error.</summary>
    int Step(long maxFrames);

    /// <summary>Waveform over the whole file as (min,max) pairs; not-yet-decoded buckets are
    /// (1,-1) unless a cached overview was seeded. Returns the bucket count.</summary>
    int ReadPeaks(float[] outMinMax, int maxPoints);

    /// <summary>The per-block min/max overview, for the on-disk analysis cache.</summary>
    float[] ReadPeakTable();

    /// <summary>Seeds the overview from the cache so the full waveform shows immediately.
    /// False when the table doesn't fit this file.</summary>
    bool SeedPeakTable(float[] table);

    /// <summary>Tempo of the whole file once done (0 = not detectable).</summary>
    double DetectTempo();
}
