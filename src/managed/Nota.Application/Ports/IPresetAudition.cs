// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>What a browser row auditions: a preset (factory "factory:…" id, a saved .notapreset
/// path, a "kit:" / "rhythmkit:" drum kit) or a bare built-in device (its default sound).</summary>
public enum AuditionSubjectKind { Preset, Instrument, AudioEffect, MidiEffect }

public readonly record struct AuditionSubject(AuditionSubjectKind Kind, int BuiltinKind, string Path, string Name);

/// <summary>A demo track an audio effect is heard through: a single part (drums, keys, bass)
/// or a short genre loop. <see cref="Group"/> sections the player's menu.</summary>
public sealed record AuditionTrack(string Id, string Name, string Group, string Blurb);

/// <summary>The two track ids that aren't demo tracks.</summary>
public static class AuditionTrackIds
{
    /// <summary>Pick per effect: dynamics hear the drums, time / modulation the keys, tone
    /// shaping a full mix.</summary>
    public const string Auto = "auto";
    /// <summary>The sample last selected in the Files tab.</summary>
    public const string Sample = "sample";
}

/// <summary>A resolved audition: the cache key, what the player shows, and how to build and
/// render the rig. <see cref="Build"/> and the render run on a worker thread.</summary>
public sealed record AuditionPlan(
    string Key,
    string Device,                        // "Nota Volt"
    string Phrase,                        // "pad chord", "groove · Kompakt"
    bool IsEffect,                        // an audio effect: the player offers the track choice
    string TrackId,                       // resolved (never Auto) for effects, "" otherwise
    string TrackName,                     // "House", or the sample's file name
    Func<IAuditionRig, bool> Build,
    IReadOnlyList<AuditionNote> Notes,
    double Bpm,
    double PhraseBeats,
    double TailSeconds,
    bool Rolling);

/// <summary>Turns browser rows into offline auditions (see <see cref="IAuditionRig"/>).</summary>
public interface IPresetAudition
{
    /// <summary>The demo tracks, in menu order (parts first, then genres).</summary>
    IReadOnlyList<AuditionTrack> Tracks { get; }

    /// <summary>The plan for <paramref name="subject"/>, or null with a short user-facing
    /// reason (plug-in presets and racks have nothing to render here). <paramref name="trackId"/>
    /// is a <see cref="Tracks"/> id, <see cref="AuditionTrackIds.Auto"/> or
    /// <see cref="AuditionTrackIds.Sample"/> (<paramref name="samplePath"/>: the Files tab's sample).</summary>
    AuditionPlan? Plan(AuditionSubject subject, string trackId, string? samplePath, out string reason);

    /// <summary>The track Auto resolves to for an audio effect kind.</summary>
    string AutoTrack(int effectKind);
}
