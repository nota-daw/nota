// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A transcribed note: times in seconds from the start of the audio, MIDI pitch, and
/// the model's mean activation over the note (0..1).</summary>
public readonly record struct TranscribedNote(double Start, double End, int Pitch, float Amplitude)
{
    public int Velocity => Math.Clamp((int)Math.Round(127 * Amplitude), 1, 127);
}

/// <summary>Turning transcribed notes into a clip's notes; shared by Convert and its MCP tool.</summary>
public static class Transcription
{
    /// <summary>One line out of a transcription: wherever notes overlap, the strongest one
    /// wins and the others are dropped (Convert Melody).</summary>
    public static List<TranscribedNote> Monophonic(IReadOnlyList<TranscribedNote> notes)
    {
        var kept = new List<TranscribedNote>();
        foreach (var n in notes.OrderByDescending(n => n.Amplitude * (n.End - n.Start)))
            if (!kept.Any(k => k.Start < n.End && n.Start < k.End)) kept.Add(n);
        kept.Sort((a, b) => a.Start.CompareTo(b.Start));
        return kept;
    }

    /// <summary>Transcribed notes as a MIDI clip's notes: <paramref name="audioSeconds"/> of
    /// audio spans <paramref name="clipBeats"/> (so a warped clip's notes follow its tempo);
    /// notes are kept inside the clip.</summary>
    public static NotaNote[] ToClipNotes(IReadOnlyList<TranscribedNote> notes, double audioSeconds, double clipBeats)
    {
        double toBeat = audioSeconds > 0 ? clipBeats / audioSeconds : 0;
        var outp = new List<NotaNote>(notes.Count);
        foreach (var n in notes)
        {
            double start = Math.Max(0, n.Start) * toBeat;
            if (start >= clipBeats) continue;
            outp.Add(new NotaNote(n.Pitch, start, Math.Max(0.05, Math.Min(n.End * toBeat, clipBeats) - start), n.Velocity / 127f));
        }
        return outp.ToArray();
    }
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
