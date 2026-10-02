// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Words for what a project version changed ("Added Bass · Tempo 120.00 → 124.00 · Edited
// Lead"), most telling first, for the History tab's rows and details.

using Nota.Application;

namespace Nota.Presentation;

public static class VersionSummary
{
    /// <summary>The changes as short phrases, most telling first. Empty when nothing a user
    /// would name changed (a transport toggle, a plug-in's window size).</summary>
    /// <param name="tempo">Formats a tempo (the app's fixed-precision BPM).</param>
    public static IReadOnlyList<string> Parts(VersionChanges c, Func<double, string> tempo)
    {
        var parts = new List<string>();
        if (c.First)
        {
            parts.Add(c.TrackCount == 0 ? "First save" : $"First save · {Count(c.TrackCount, "track")}");
            return parts;
        }
        if (c.TracksAdded.Count > 0) parts.Add("Added " + Names(c.TracksAdded));
        if (c.TracksRemoved.Count > 0) parts.Add("Removed " + Names(c.TracksRemoved));
        foreach (var r in c.TracksRenamed.Take(2)) parts.Add($"{r.From} → {r.To}");
        if (c.TempoFrom is { } tf && c.TempoTo is { } tt) parts.Add($"Tempo {tempo(tf)} → {tempo(tt)}");
        if (c.MeterFrom is { } mf && c.MeterTo is { } mt) parts.Add($"{mf} → {mt}");
        if (c.NewAudio > 0) parts.Add(c.NewAudio == 1 ? "New audio" : $"{c.NewAudio} new audio files");
        if (c.Edited.Count > 0) parts.Add("Edited " + Names(c.Edited));
        if (c.Sound.Count > 0) parts.Add("Devices on " + Names(c.Sound));
        if (c.Mix.Count > 0) parts.Add(c.Mix.Count > 2 ? $"Mix of {Count(c.Mix.Count, "track")}" : "Mix of " + Names(c.Mix));
        foreach (var o in c.Other) parts.Add(char.ToUpperInvariant(o[0]) + o[1..]);
        return parts;
    }

    /// <summary>A row-length headline: the first two phrases, or null when there are none.</summary>
    public static string? Headline(VersionChanges? c, Func<double, string> tempo)
        => c is null ? null : Parts(c, tempo) is { Count: > 0 } p ? string.Join(" · ", p.Take(2)) : null;

    // "Bass", "Bass, Pad", "Bass, Pad +3".
    private static string Names(IReadOnlyList<string> names)
        => names.Count <= 2 ? string.Join(", ", names) : $"{names[0]}, {names[1]} +{names.Count - 2}";

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
