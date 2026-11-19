// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The user's corrections to what a controller drives, made from a control's right-click menu
// (specs/051 Story 7, FR-031–FR-034). Device parameters are not kept here — they go into edited
// device pages (FR-018a). This holds the rest:
//   unbinds  a target no hardware control drives any more (a bank fader skips that track, a
//            transport role lets go of its button);
//   pins     one hardware control (profile id + its message) that drives one target whatever the
//            bank, instead of its role.
// Targets are MIDI Learn addresses as plain numbers (Nota.App's MidiTargetKind is appended-only
// and saved as an int in midimap.json too). An entry that names a track lives in the project
// only (track positions on save — tracks differ between projects); one that doesn't lives in the
// user's global file and the project carries a copy, which wins while that project is open.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nota.Application.Controllers;

/// <summary>A target as MIDI Learn addresses it: kind (MidiTargetKind as an int), track
/// (−1 = none), parameter (a send bus, a chain; −1 = none), device (a rack's position in the
/// chain; −1 = none / the Instrument Rack) and sub (a chain device's chain and slot).</summary>
public readonly record struct StoredTarget(int Kind, int TrackId = -1, int Param = -1, int Device = -1, int Sub = -1)
{
    public bool HasTrack => TrackId >= 0;
}

/// <summary>A hardware control pinned to a target: the profile and the control's message key
/// (<see cref="ControllerOverrides.ControlKey"/>), with the control's name for menus.</summary>
public sealed record ControlPin(string ProfileId, string Control, string Label, StoredTarget Target);

public sealed class ControllerOverrides(string globalPath)
{
    public const string ProjectFileName = "controlleroverrides.json";

    private readonly List<StoredTarget> _globalUnbinds = new();
    private readonly List<ControlPin> _globalPins = new();
    private readonly List<StoredTarget> _projectUnbinds = new();   // with a track, or the project's copy of the rest
    private readonly List<ControlPin> _projectPins = new();
    private bool _projectHasGlobalCopy;   // the open project carries its own copy of the track-free entries

    public event Action? Changed;

    /// <summary>What a declared control is called in an override: its message, channel and number.</summary>
    public static string ControlKey(ControlDecl d) => $"{d.Message.ToString().ToLowerInvariant()}:{d.Channel + 1}:{d.Number}";

    public IEnumerable<StoredTarget> Unbinds
        => _projectUnbinds.Where(t => t.HasTrack).Concat(_projectHasGlobalCopy ? _projectUnbinds.Where(t => !t.HasTrack) : _globalUnbinds);

    public IEnumerable<ControlPin> Pins
        => _projectPins.Where(p => p.Target.HasTrack).Concat(_projectHasGlobalCopy ? _projectPins.Where(p => !p.Target.HasTrack) : _globalPins);

    public bool IsUnbound(StoredTarget t) => Unbinds.Contains(t);

    /// <summary>The pin for a control of a profile, or null.</summary>
    public ControlPin? PinOf(string profileId, string control)
        => Pins.FirstOrDefault(p => p.ProfileId == profileId && p.Control == control);

    /// <summary>The pins that drive <paramref name="t"/>.</summary>
    public IEnumerable<ControlPin> PinsFor(StoredTarget t) => Pins.Where(p => p.Target == t);

    /// <summary>Whether the user changed anything for <paramref name="t"/> (Back to default).</summary>
    public bool IsChanged(StoredTarget t) => IsUnbound(t) || PinsFor(t).Any();

    /// <summary>No hardware control drives <paramref name="t"/> (its pins go too).</summary>
    public void Unbind(StoredTarget t)
    {
        EnsureProjectCopy();
        RemoveAll(t);
        _projectUnbinds.Add(t);
        if (!t.HasTrack) _globalUnbinds.Add(t);
        Commit();
    }

    /// <summary>The control drives <paramref name="t"/> from now on, instead of what it did; a
    /// control drives one target, so an older pin of it goes.</summary>
    public void Pin(string profileId, string control, string label, StoredTarget t)
    {
        EnsureProjectCopy();
        RemoveAll(t);
        _globalPins.RemoveAll(p => p.ProfileId == profileId && p.Control == control);
        _projectPins.RemoveAll(p => p.ProfileId == profileId && p.Control == control);
        var pin = new ControlPin(profileId, control, label, t);
        _projectPins.Add(pin);
        if (!t.HasTrack) _globalPins.Add(pin);
        Commit();
    }

    /// <summary>Back to default for <paramref name="t"/>: its unbind and pins go.</summary>
    public void Reset(StoredTarget t)
    {
        EnsureProjectCopy();
        RemoveAll(t);
        Commit();
    }

    // An edit to the track-free entries goes to both files: the project's copy starts as the
    // global entries the first time the open project changes any.
    private void EnsureProjectCopy()
    {
        if (_projectHasGlobalCopy) return;
        _projectUnbinds.RemoveAll(t => !t.HasTrack);
        _projectPins.RemoveAll(p => !p.Target.HasTrack);
        _projectUnbinds.AddRange(_globalUnbinds);
        _projectPins.AddRange(_globalPins);
        _projectHasGlobalCopy = true;
    }

    private void RemoveAll(StoredTarget t)
    {
        _globalUnbinds.RemoveAll(x => x == t);
        _globalPins.RemoveAll(p => p.Target == t);
        _projectUnbinds.RemoveAll(x => x == t);
        _projectPins.RemoveAll(p => p.Target == t);
    }

    private void Commit()
    {
        WriteGlobal();
        Changed?.Invoke();
    }

    // ---- files ------------------------------------------------------------------------------

    public void LoadGlobal()
    {
        _globalUnbinds.Clear(); _globalPins.Clear();
        if (Read(globalPath, null) is { } f) { _globalUnbinds.AddRange(f.Unbinds); _globalPins.AddRange(f.Pins); }
        Changed?.Invoke();
    }

    /// <summary>A project opened (null: a new one). <paramref name="trackIdAt"/> turns a saved track
    /// position into today's track id (−1 = gone).</summary>
    public void LoadProject(string? bundleDir, Func<int, int> trackIdAt)
    {
        _projectUnbinds.Clear(); _projectPins.Clear();
        _projectHasGlobalCopy = false;
        if (bundleDir is not null && Read(Path.Combine(bundleDir, ProjectFileName), trackIdAt) is { } f)
        {
            _projectUnbinds.AddRange(f.Unbinds);
            _projectPins.AddRange(f.Pins);
            _projectHasGlobalCopy = f.HasGlobalCopy;
        }
        Changed?.Invoke();
    }

    /// <summary>The project is saved: everything in effect, tracks as positions. No file when empty.</summary>
    public void SaveProject(string bundleDir, Func<int, int> trackIndexOf)
    {
        var unbinds = Unbinds.ToList();
        var pins = Pins.ToList();
        string path = Path.Combine(bundleDir, ProjectFileName);
        try
        {
            if (unbinds.Count == 0 && pins.Count == 0) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllText(path, Write(unbinds, pins, trackIndexOf, globalCopy: true));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void WriteGlobal()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(globalPath)!);
            File.WriteAllText(globalPath, Write(_globalUnbinds, _globalPins, null, globalCopy: false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class TargetDto
    {
        [JsonPropertyName("kind")] public int Kind { get; set; }
        [JsonPropertyName("track")] public int Track { get; set; } = -1;   // a track position, −1 = none
        [JsonPropertyName("param")] public int Param { get; set; } = -1;
        [JsonPropertyName("device")] public int Device { get; set; } = -1;
        [JsonPropertyName("sub")] public int Sub { get; set; } = -1;
    }

    private sealed class PinDto
    {
        [JsonPropertyName("profile")] public string Profile { get; set; } = "";
        [JsonPropertyName("control")] public string Control { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("target")] public TargetDto Target { get; set; } = new();
    }

    private sealed class FileDto
    {
        [JsonPropertyName("format")] public int Format { get; set; } = 1;
        [JsonPropertyName("global")] public bool Global { get; set; }   // a project file that carries the track-free entries
        [JsonPropertyName("unbind")] public List<TargetDto> Unbind { get; set; } = new();
        [JsonPropertyName("pin")] public List<PinDto> Pin { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The file text. <paramref name="trackIndexOf"/> null = the global file (track-free entries only).</summary>
    public static string Write(IEnumerable<StoredTarget> unbinds, IEnumerable<ControlPin> pins, Func<int, int>? trackIndexOf, bool globalCopy)
    {
        TargetDto? Dto(StoredTarget t)
        {
            if (!t.HasTrack) return new TargetDto { Kind = t.Kind, Param = t.Param, Device = t.Device, Sub = t.Sub };
            if (trackIndexOf is null) return null;
            int i = trackIndexOf(t.TrackId);
            return i < 0 ? null : new TargetDto { Kind = t.Kind, Track = i, Param = t.Param, Device = t.Device, Sub = t.Sub };
        }
        var f = new FileDto { Global = globalCopy };
        foreach (var t in unbinds) if (Dto(t) is { } d) f.Unbind.Add(d);
        foreach (var p in pins) if (Dto(p.Target) is { } d) f.Pin.Add(new PinDto { Profile = p.ProfileId, Control = p.Control, Label = p.Label, Target = d });
        return JsonSerializer.Serialize(f, Options);
    }

    /// <summary>Parse a file. <paramref name="trackIdAt"/> null = the global file (entries naming a
    /// track are dropped); an entry whose track is gone is dropped.</summary>
    public static (List<StoredTarget> Unbinds, List<ControlPin> Pins, bool HasGlobalCopy)? Parse(string json, Func<int, int>? trackIdAt)
    {
        FileDto? f;
        try { f = JsonSerializer.Deserialize<FileDto>(json); }
        catch (JsonException) { return null; }
        if (f is null || f.Format != 1) return null;
        StoredTarget? T(TargetDto d)
        {
            if (d.Track < 0) return new StoredTarget(d.Kind, -1, d.Param, d.Device, d.Sub);
            if (trackIdAt is null) return null;
            int id = trackIdAt(d.Track);
            return id < 0 ? null : new StoredTarget(d.Kind, id, d.Param, d.Device, d.Sub);
        }
        var unbinds = new List<StoredTarget>();
        foreach (var d in f.Unbind) if (T(d) is { } t && !unbinds.Contains(t)) unbinds.Add(t);
        var pins = new List<ControlPin>();
        foreach (var p in f.Pin)
            if (p.Profile.Length > 0 && p.Control.Length > 0 && T(p.Target) is { } t
                && !pins.Any(x => x.ProfileId == p.Profile && x.Control == p.Control))
                pins.Add(new ControlPin(p.Profile, p.Control, p.Label, t));
        return (unbinds, pins, f.Global);
    }

    private static (List<StoredTarget> Unbinds, List<ControlPin> Pins, bool HasGlobalCopy)? Read(string path, Func<int, int>? trackIdAt)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path), trackIdAt) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
