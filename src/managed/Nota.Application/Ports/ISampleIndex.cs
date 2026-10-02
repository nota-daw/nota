// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application.Samples;

namespace Nota.Application;

/// <summary>Decodes and measures a sample file (tempo, key, envelope, timbre). Thread-safe;
/// slow (it decodes) — call it off the UI thread.</summary>
public interface ISampleAnalyzer
{
    /// <summary>Analyses the file's first <paramref name="maxSeconds"/>. Null when it can't be decoded.</summary>
    SampleAnalysis? Analyze(string path, double maxSeconds);
}

/// <summary>A sample found by <see cref="ISampleIndex.Similar"/>; smaller distance = closer.</summary>
public readonly record struct SimilarSample(SampleInfo Info, double Distance);

/// <summary>The sample library's analysis index: every audio file in the Samples folder,
/// analysed once in the background and remembered across launches (keyed by path, refreshed
/// when a file's size or date changes). Feeds the Files tab's tags, filters and "similar
/// sounds", and tells a dropped loop its tempo without re-measuring it.</summary>
public interface ISampleIndex
{
    /// <summary>What the index knows about <paramref name="path"/> (no disk access), or null
    /// when it hasn't been analysed yet.</summary>
    SampleInfo? Get(string path);

    /// <summary>Like <see cref="Get"/>, but checks the file is unchanged and analyses it now if
    /// not (decodes — worker thread). Null when it can't be decoded.</summary>
    SampleInfo? GetOrAnalyze(string path);

    /// <summary>Records an analysis made elsewhere (the import path already decoded the file).</summary>
    SampleInfo Store(string path, SampleAnalysis analysis);

    /// <summary>Indexes everything under <paramref name="root"/> in the background (replaces
    /// any running scan). Files the index already knows are skipped.</summary>
    void Watch(string root);

    /// <summary>Moves <paramref name="folder"/> to the front of the queue — a pack just installed.</summary>
    void Prioritize(string folder);

    /// <summary>Files analysed and found so far in the current scan; Total 0 when idle.</summary>
    (int Done, int Total) Progress { get; }

    /// <summary>Raised (throttled, on a worker thread) as analyses land and when a scan ends.</summary>
    event Action? Changed;

    /// <summary>The <paramref name="count"/> analysed samples that sound most like
    /// <paramref name="path"/> (itself excluded), closest first.</summary>
    IReadOnlyList<SimilarSample> Similar(string path, int count);

    /// <summary>Analysed samples passing <paramref name="filter"/> whose path contains every
    /// word of <paramref name="text"/>, up to <paramref name="limit"/>, by path.</summary>
    IReadOnlyList<SampleInfo> Search(SampleFilter filter, string? text, int limit);
}
