// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A transcribed note: times in seconds from the start of the audio, MIDI pitch, and
/// the model's mean activation over the note (0..1).</summary>
public readonly record struct TranscribedNote(double Start, double End, int Pitch, float Amplitude)
{
    public int Velocity => Math.Clamp((int)Math.Round(127 * Amplitude), 1, 127);
}

/// <summary>An audio clip read for Separate Stems (<see cref="IClipAi.ReadStemSource"/>).</summary>
public abstract class StemJob
{
    public required int TrackId { get; init; }
    public required int ClipIndex { get; init; }
    /// <summary>The clip's name, else its track's: names the stem clips and their group.</summary>
    public required string Name { get; init; }
}

/// <summary>Separated stems waiting to be applied; disposing deletes their temp files.</summary>
public abstract class StemResult : IDisposable
{
    public abstract void Dispose();
}

/// <summary>The AI features on audio clips (models from <see cref="IModelStore"/>). Separate
/// Stems runs in three steps so the engine stays on its thread: read (engine thread), separate
/// (any thread; tens of seconds), apply (engine thread, one undo step). Failures throw
/// <see cref="InvalidOperationException"/> with a user-facing message.</summary>
public interface IClipAi
{
    bool CanSeparate { get; }
    bool CanTranscribe { get; }

    StemJob ReadStemSource(int trackId, int clipIndex);

    StemResult Separate(StemJob job, IProgress<(double Fraction, string Label)>? progress = null, CancellationToken ct = default);

    /// <summary>Puts a group of stem tracks (Drums, Bass, Other, Vocals) under the clip's track,
    /// each with a clip shaped like the original, and switches the original off. Returns the
    /// group's track id.</summary>
    int ApplyStems(StemJob job, StemResult stems);

    /// <summary>Notes in <paramref name="mono"/> (any sample rate), by start time.</summary>
    IReadOnlyList<TranscribedNote> Transcribe(float[] mono, double sampleRate, IProgress<double>? progress = null, CancellationToken ct = default);
}
