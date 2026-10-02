// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the analysed sample library (the browser's Files tab): find loops and one-shots
// by tempo, key and name, ask what a file is, find sounds like one, and read / set the project
// key that dropped loops can be transposed to. The index analyses the Samples folder in the
// background; files it hasn't reached yet don't show up in searches.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;
using Nota.Application.Samples;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SampleLibraryTools(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, ISampleIndex index, IProjectKeyAccess projectKey)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record LibrarySample(
        string Path,
        [property: Description("loop, one-shot or unknown")] string Kind,
        [property: Description("Tempo in BPM; 0 = none (one-shots, unpulsed sounds)")] double Bpm,
        [property: Description("\"Am\", \"Eb\" (major), \"F#\" for a one-shot's pitch; null = no key")] string? Key,
        double DurationSec,
        [property: Description("Where the tempo came from: name, duration (a loop's length), audio")] string BpmSource);

    private static LibrarySample Of(SampleInfo s) => new(
        s.Path,
        s.Kind switch { SampleKind.Loop => "loop", SampleKind.OneShot => "one-shot", _ => "unknown" },
        s.Bpm, s.Key?.Short, Math.Round(s.DurationSec, 3), s.BpmSource.ToString().ToLowerInvariant());

    public sealed record SearchResult(
        LibrarySample[] Samples,
        [property: Description("Files the background analysis is still working through (0 = the whole library is searchable)")] int StillAnalysing);

    [McpServerTool(Name = "search_samples"), Description(
        "Search the user's sample library (Settings → Library → Samples folder, installed packs included) by kind, tempo range, "
        + "key and words in the path. A key also matches its relative (\"Am\" finds C major loops). Returns file paths for "
        + "add_audio_track / load_sampler_sample etc.")]
    public SearchResult SearchSamples(
        [Description("loop, one-shot, or empty for any")] string? kind = null,
        [Description("Lowest tempo (BPM), 0 = no bound")] double bpmMin = 0,
        [Description("Highest tempo (BPM), 0 = no bound")] double bpmMax = 0,
        [Description("Key, e.g. \"Am\", \"F# minor\", \"Eb\"; empty = any")] string? key = null,
        [Description("Words that must all appear in the path, e.g. \"kick 808\"")] string? text = null,
        int limit = 50)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "loop" or "loops" => SampleKind.Loop,
            "one-shot" or "oneshot" or "one-shots" or "hit" => SampleKind.OneShot,
            "" => SampleKind.Unknown,
            var other => throw new ArgumentException($"Unknown kind \"{other}\" — use loop or one-shot."),
        };
        MusicalKey? mk = null;
        if (!string.IsNullOrWhiteSpace(key) && (mk = MusicalKey.Parse(key)) is null)
            throw new ArgumentException($"Can't read the key \"{key}\" — try \"Am\", \"C\", \"F# minor\".");
        var hits = index.Search(new SampleFilter(k, bpmMin, bpmMax, mk), text, Math.Clamp(limit, 1, 500));
        var (done, total) = index.Progress;
        return new SearchResult(hits.Select(Of).ToArray(), total - done);
    }

    [McpServerTool(Name = "get_sample_info"), Description(
        "What Nota knows about an audio file: loop or one-shot, tempo, key, length. Analyses it now if the library index hasn't.")]
    public async Task<LibrarySample> GetSampleInfo(string path)
        => await Task.Run(() => index.GetOrAnalyze(path)) is { } s ? Of(s) : throw new ArgumentException($"Can't decode \"{path}\".");

    [McpServerTool(Name = "find_similar_samples"), Description(
        "Samples in the library that sound most like the given file (timbre: spectrum, brightness, noisiness, attack, length), "
        + "closest first. A loop is matched with loops, a hit with hits.")]
    public async Task<LibrarySample[]> FindSimilarSamples(string path, int count = 20)
        => (await Task.Run(() => index.Similar(path, Math.Clamp(count, 1, 200)))).Select(h => Of(h.Info)).ToArray();

    [McpServerTool(Name = "get_project_key"), Description("The project key (\"Am\", \"Eb\"), or null when none is set.")]
    public Task<string?> GetProjectKey() => Read(() => MusicalKey.FromCode(projectKey.KeyCode)?.Short);

    [McpServerTool(Name = "set_project_key"), Description(
        "Set the project key (\"Am\", \"F# minor\", \"Eb\"), or clear it with an empty string. With the browser's "
        + "\"Transpose to project key\" option on, samples dropped on the arrangement are transposed to it.")]
    public Task<string?> SetProjectKey(string? key)
    {
        MusicalKey? mk = null;
        if (!string.IsNullOrWhiteSpace(key) && (mk = MusicalKey.Parse(key)) is null)
            throw new ArgumentException($"Can't read the key \"{key}\" — try \"Am\", \"C\", \"F# minor\".");
        return Read(() => { projectKey.KeyCode = mk?.Code ?? -1; return mk?.Short; });
    }
}
