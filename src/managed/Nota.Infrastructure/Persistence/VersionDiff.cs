// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a project version changed against its parent, from the two manifests (raw JSON, so
// it works across format versions): tracks added / removed / renamed, tempo and meter, and
// per track whether the arrangement (clips, notes, automation), the sound (instrument,
// devices, modulation) or the mix (volume, pan, mute, solo, sends) moved. Tracks have no
// stable id in the manifest, so they are matched by type + name, nearest position first.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Nota.Infrastructure;

internal static class VersionDiff
{
    private static readonly string[] EditKeys = ["midiClips", "audioClips", "sessionSlots", "automation"];
    private static readonly string[] SoundKeys = ["instrument", "midiEffects", "devices", "modulators", "cvLinks", "frozenState"];
    private static readonly string[] MixKeys = ["volume", "pan", "mute", "solo", "sends"];

    // Sidecars a user would recognise, by file name.
    private static readonly Dictionary<string, string> OtherNames = new(StringComparer.Ordinal)
    {
        ["sections.json"] = "sections",
        ["midimap.json"] = "MIDI map",
        ["modular-layout.json"] = "modular layout",
        ["freeze-links.json"] = "freeze",
    };

    /// <summary>The first version of a project.</summary>
    public static VersionChanges First(JsonNode? manifest)
        => new() { First = true, TrackCount = Tracks(manifest).Count };

    /// <param name="changedFiles">Top-level files (besides the manifest) whose content differs.</param>
    /// <param name="newAudio">Samples the parent didn't reference.</param>
    public static VersionChanges Compare(JsonNode? before, JsonNode? after, IEnumerable<string> changedFiles, int newAudio)
    {
        var oldTracks = Tracks(before);
        var newTracks = Tracks(after);

        // Pair tracks of the same type and name, preferring the closest position.
        var pairs = new List<(JsonObject Old, JsonObject New)>();
        var freeOld = Enumerable.Range(0, oldTracks.Count).ToList();
        var addedIdx = new List<int>();
        for (int n = 0; n < newTracks.Count; n++)
        {
            int match = freeOld
                .Where(o => Key(oldTracks[o]) == Key(newTracks[n]))
                .OrderBy(o => Math.Abs(o - n))
                .DefaultIfEmpty(-1).First();
            if (match < 0) { addedIdx.Add(n); continue; }
            freeOld.Remove(match);
            pairs.Add((oldTracks[match], newTracks[n]));
        }

        // An added and a removed track of the same type at the same position is a rename.
        var renamed = new List<TrackRename>();
        foreach (int n in addedIdx.ToList())
        {
            int o = freeOld.FirstOrDefault(i => i == n && Type(oldTracks[i]) == Type(newTracks[n]), -1);
            if (o < 0) continue;
            renamed.Add(new TrackRename(Name(oldTracks[o], o), Name(newTracks[n], n)));
            pairs.Add((oldTracks[o], newTracks[n]));
            freeOld.Remove(o);
            addedIdx.Remove(n);
        }

        var edited = new List<string>();
        var sound = new List<string>();
        var mix = new List<string>();
        foreach (var (o, n) in pairs)
        {
            string name = Name(n, newTracks.IndexOf(n));
            if (Differs(o, n, EditKeys)) edited.Add(name);
            if (Differs(o, n, SoundKeys)) sound.Add(name);
            if (Differs(o, n, MixKeys)) mix.Add(name);
        }
        // The master bus: its devices, volume and volume automation.
        if (!JsonNode.DeepEquals(before?["master"]?["devices"], after?["master"]?["devices"])) sound.Add("Master");
        if (!Same(before?["transport"]?["masterVolume"], after?["transport"]?["masterVolume"])) mix.Add("Master");
        if (!JsonNode.DeepEquals(before?["masterVolumeAutomation"], after?["masterVolumeAutomation"])) edited.Add("Master");

        double? bpmBefore = Num(before?["transport"]?["bpm"]), bpmAfter = Num(after?["transport"]?["bpm"]);
        bool tempo = bpmBefore is { } b && bpmAfter is { } a && Math.Abs(a - b) > 1e-6;
        string? meterBefore = Meter(before), meterAfter = Meter(after);
        bool meter = meterBefore != meterAfter;

        return new VersionChanges
        {
            TrackCount = newTracks.Count,
            TracksAdded = addedIdx.Select(i => Name(newTracks[i], i)).ToList(),
            TracksRemoved = freeOld.Select(i => Name(oldTracks[i], i)).ToList(),
            TracksRenamed = renamed,
            TempoFrom = tempo ? bpmBefore : null,
            TempoTo = tempo ? bpmAfter : null,
            MeterFrom = meter ? meterBefore : null,
            MeterTo = meter ? meterAfter : null,
            Edited = edited,
            Sound = sound,
            Mix = mix,
            NewAudio = newAudio,
            Other = changedFiles
                .Select(f => OtherNames.TryGetValue(f, out var label) ? label : Path.GetFileNameWithoutExtension(f))
                .Distinct().ToList(),
        };
    }

    private static List<JsonObject> Tracks(JsonNode? manifest)
        => (manifest?["tracks"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new();

    private static int Type(JsonObject t) => (int)(Num(t["type"]) ?? 0);

    private static string Key(JsonObject t) => $"{Type(t)}|{t["name"]?.GetValue<string>()}";

    // A track's display name; unnamed ones are called by kind and position, as the app does.
    private static string Name(JsonObject t, int index)
        => t["name"]?.GetValue<string>() is { Length: > 0 } n ? n
           : (Type(t) switch { 0 => "Audio", 2 => "Return", 3 => "Group", _ => "MIDI" }) + " " + (index + 1);

    private static bool Differs(JsonObject a, JsonObject b, string[] keys)
        => keys.Any(k => !Same(a[k], b[k]));

    // Equal JSON, treating a missing value and its default (null / false / 0 / empty list) alike.
    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(Normal(a), Normal(b));

    private static JsonNode? Normal(JsonNode? n) => n switch
    {
        JsonArray { Count: 0 } => null,
        JsonValue v when v.TryGetValue(out bool flag) && !flag => null,
        _ => n,
    };

    private static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;

    private static string? Meter(JsonNode? manifest)
    {
        var t = manifest?["transport"];
        return t is null ? null : $"{Num(t["timeSigNumerator"]) ?? 4}/{Num(t["timeSigDenominator"]) ?? 4}";
    }
}
