// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The AI features on an audio clip, shared by the clip menu and the MCP tools. Each runs in
// three steps so the caller can keep the engine on its own thread: read the clip (engine
// thread), run the model (any thread — tens of seconds for stems), apply the result (engine
// thread, one undo step).
//
// Separate Stems works on the clip's *source*: the stems are written as files as long as the
// source, at its sample rate, silent outside the part the clip uses, so each stem clip can take
// the original clip's exact shape (region, warp markers, reverse, envelopes, ADSR —
// AudioClipState) and line up with it sample for sample. The original clip is switched off,
// not deleted.

namespace Nota.Infrastructure;

/// <summary>An audio clip read for separation: its shape, and the source audio to separate.</summary>
public sealed class StemSource : StemJob
{
    public required AudioClipDto Shape { get; init; }        // the clip, for the stem clips to copy
    public required double SampleRate { get; init; }
    public required long SourceFrames { get; init; }          // the whole source's length
    public required long From { get; init; }                  // separated region of the source
    public required float[] Left { get; init; }               // the region, de-interleaved
    public required float[] Right { get; init; }
}

/// <summary>Separated stems written to temp WAVs (one per <see cref="StemSeparator.Stems"/>).</summary>
public sealed class StemFiles : StemResult
{
    public required string[] Paths { get; init; }

    public override void Dispose()
    {
        foreach (var p in Paths)
            try { File.Delete(p); } catch { /* temp; best-effort */ }
    }
}

public sealed class ClipAi(IAudioEngine engine, IModelStore models) : IClipAi
{
    // Unwarped clips: separate only the region the clip plays, plus a second either side so the
    // model hears some context at the edges.
    private const double MarginSeconds = 1.0;

    /// <summary>The longest audio Separate Stems takes: the four stereo stems are held in memory
    /// (~1 GB at this length).</summary>
    public const double MaxSeconds = 10 * 60;

    // ---- Separate Stems ------------------------------------------------------------------

    /// <summary>Step 1 (engine thread). Throws <see cref="InvalidOperationException"/> with a
    /// user-facing message when the clip can't be separated.</summary>
    public bool CanSeparate => models.IsInstalled(AiModels.Stems) && models.RuntimePath is not null;

    public StemJob ReadStemSource(int trackId, int clipIndex)
    {
        if (!engine.TryGetAudioClipInfo(trackId, clipIndex, out var ac))
            throw new InvalidOperationException("That isn't an audio clip.");
        if (!engine.TryGetSampleInfo(ac.SampleId, out var si) || si.Frames <= 0 || si.Channels <= 0 || si.SampleRate <= 0)
            throw new InvalidOperationException("This clip has no audio to separate.");

        long from = 0, to = si.Frames;
        if (ac.WarpEnabled == 0)
        {
            long margin = (long)(MarginSeconds * si.SampleRate);
            long start = (long)Math.Floor(ac.SourceOffsetFrames);
            long end = ac.LengthFrames > 0 ? start + ac.LengthFrames : si.Frames;
            from = Math.Clamp(start - margin, 0, si.Frames);
            to = Math.Clamp(end + margin, from, si.Frames);
        }
        if ((to - from) / si.SampleRate > MaxSeconds)
            throw new InvalidOperationException($"This clip is longer than {MaxSeconds / 60:0} minutes — split it first, then separate the parts.");
        if (to - from < si.SampleRate * 0.1)
            throw new InvalidOperationException("This clip is too short to separate.");

        var data = engine.ReadSample(ac.SampleId);
        int ch = si.Channels, n = (int)(to - from);
        var left = new float[n];
        var right = new float[n];
        for (int i = 0; i < n; i++)
        {
            long at = (from + i) * ch;
            if ((at + ch) > data.Length) break;
            left[i] = data[at];
            right[i] = ch > 1 ? data[at + 1] : data[at];
        }

        string name = engine.GetClipName(trackId, clipIndex) is { Length: > 0 } cn ? cn
                    : engine.GetTrackName(trackId) is { Length: > 0 } tn ? tn : "Audio";
        return new StemSource
        {
            TrackId = trackId, ClipIndex = clipIndex, Name = name,
            Shape = AudioClipState.Capture(engine, trackId, clipIndex, ac),
            SampleRate = si.SampleRate, SourceFrames = si.Frames, From = from, Left = left, Right = right,
        };
    }

    /// <summary>Step 2 (any thread): runs htdemucs and writes the stems as temp WAVs.
    /// <paramref name="progress"/> reports 0..1 with a stage label.</summary>
    public StemResult Separate(StemJob job, IProgress<(double Fraction, string Label)>? progress = null, CancellationToken ct = default)
    {
        var src = (StemSource)job;
        var (runtime, model) = Require(AiModels.Stems);
        progress?.Report((0, "Preparing audio…"));
        double sr = src.SampleRate;
        var l = Resampler.Convert(src.Left, sr, StemSeparator.SampleRate, ct);
        var r = Resampler.Convert(src.Right, sr, StemSeparator.SampleRate, ct);

        float[][][] stems;
        progress?.Report((0, "Loading the model…"));
        using (var sep = new StemSeparator(runtime, model))
            stems = sep.Separate(l, r, new Progress(p => progress?.Report((p, "Separating stems…"))), ct);

        progress?.Report((1, "Writing stems…"));
        var files = new StemFiles { Paths = new string[StemSeparator.Stems.Length] };
        try
        {
            int rate = (int)Math.Round(sr);
            int regionLen = src.Left.Length;
            for (int s = 0; s < StemSeparator.Stems.Length; s++)
            {
                ct.ThrowIfCancellationRequested();
                var sl = Fit(Resampler.Convert(stems[s][0], StemSeparator.SampleRate, sr, ct), regionLen);
                var sr2 = Fit(Resampler.Convert(stems[s][1], StemSeparator.SampleRate, sr, ct), regionLen);
                var path = Path.Combine(Path.GetTempPath(), $"nota-stem-{Guid.NewGuid():N}.wav");
                files.Paths[s] = path;
                WriteStem(path, rate, src.SourceFrames, src.From, sl, sr2);
            }
            return files;
        }
        catch
        {
            files.Dispose();
            throw;
        }
    }

    /// <summary>Step 3 (engine thread): a group of stem tracks under the source track, each with
    /// a clip shaped like the original, which is switched off. One undo step. Returns the group's id.</summary>
    public int ApplyStems(StemJob job, StemResult stems)
    {
        var src = (StemSource)job;
        var files = (StemFiles)stems;
        int srcIndex = -1, parent = -1;
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == src.TrackId) { srcIndex = i; parent = ti.GroupId; break; }
        if (srcIndex < 0) throw new InvalidOperationException("The clip's track is gone.");

        engine.BeginUndoGroup();
        try
        {
            var ids = new int[StemSeparator.Stems.Length];
            for (int s = 0; s < ids.Length; s++)
            {
                int id = engine.AddAudioTrack();
                ids[s] = id;
                engine.SetTrackName(id, StemSeparator.Stems[s]);
                if (parent >= 0) engine.SetTrackGroup(id, parent);
                engine.MoveTrack(id, srcIndex + 1 + s);
                var shape = Clone(src.Shape);
                shape.Name = $"{src.Name} {StemSeparator.Stems[s]}";
                shape.Active = true;
                if (AudioClipState.Restore(engine, id, shape, files.Paths[s]) < 0)
                    throw new InvalidOperationException("Nota couldn't load the separated stems.");
            }
            int group = engine.CreateGroup(ids);
            if (group > 0) engine.SetTrackName(group, $"{src.Name} Stems");
            engine.SetClipActive(src.TrackId, src.ClipIndex, false);
            return group;
        }
        finally
        {
            engine.EndUndoGroup();
        }
    }

    // ---- Convert to MIDI ----------------------------------------------------------------

    /// <summary>Whether Convert Melody / Harmony can use basic-pitch.</summary>
    public bool CanTranscribe => models.IsInstalled(AiModels.Transcription) && models.RuntimePath is not null;

    /// <summary>Notes in <paramref name="mono"/> (any rate) with basic-pitch; times in seconds.</summary>
    public IReadOnlyList<TranscribedNote> Transcribe(float[] mono, double sampleRate, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var (runtime, model) = Require(AiModels.Transcription);
        var x = Resampler.Convert(mono, sampleRate, PitchTranscriber.SampleRate, ct);
        using var tr = new PitchTranscriber(runtime, model);
        return tr.Transcribe(x, progress, ct);
    }

    // ---- helpers ------------------------------------------------------------------------

    private (string Runtime, string Model) Require(string id)
        => models.RuntimePath is { } rt && models.ModelPath(id) is { } m
            ? (rt, m)
            : throw new InvalidOperationException("The AI model isn't installed — get it in Settings → Downloads → AI Models.");

    private static float[] Fit(float[] x, int n)
    {
        if (x.Length == n) return x;
        var y = new float[n];
        Array.Copy(x, y, Math.Min(n, x.Length));
        return y;
    }

    // The stem as long as the whole source, silent outside the separated region (24-bit is
    // plenty for a stem and keeps the project a third smaller than float).
    private static void WriteStem(string path, int rate, long sourceFrames, long from, float[] l, float[] r)
    {
        using var w = new WavWriter(path, rate, 2, WavBitDepth.Pcm24);
        const int block = 1 << 15;
        var buf = new float[block * 2];
        for (long pos = 0; pos < sourceFrames; pos += block)
        {
            int n = (int)Math.Min(block, sourceFrames - pos);
            for (int i = 0; i < n; i++)
            {
                long k = pos + i - from;
                bool inside = k >= 0 && k < l.Length;
                buf[2 * i] = inside ? l[k] : 0f;
                buf[2 * i + 1] = inside ? r[k] : 0f;
            }
            w.WriteFrames(buf, n);
        }
    }

    private static AudioClipDto Clone(AudioClipDto d)
        => System.Text.Json.JsonSerializer.Deserialize<AudioClipDto>(System.Text.Json.JsonSerializer.Serialize(d))!;

    // IProgress that reports on the calling thread (the separator's), not via a context.
    private sealed class Progress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
