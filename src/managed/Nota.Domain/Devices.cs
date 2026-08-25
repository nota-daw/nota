// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Domain;

/// <summary>The built-in Sampler instrument's settings.</summary>
public sealed class Sampler
{
    public string SamplePath { get; set; } = "";
    public Pitch RootNote { get; set; } = new(60);
    public bool Loop { get; set; }
}

/// <summary>The sound source on an instrument track: a built-in Synth/Sampler or
/// a hosted plug-in identified by its stable plugin id.</summary>
public sealed class Instrument
{
    public InstrumentKind Kind { get; }
    /// <summary>Set when <see cref="Kind"/> is <see cref="InstrumentKind.Sampler"/>.</summary>
    public Sampler? Sampler { get; set; }
    /// <summary>Stable plug-in identifier when <see cref="Kind"/> is <see cref="InstrumentKind.Plugin"/>.</summary>
    public string? PluginId { get; set; }

    public Instrument(InstrumentKind kind) => Kind = kind;

    public static Instrument Synth() => new(InstrumentKind.Synth);
    public static Instrument FromSampler(Sampler sampler) =>
        new(InstrumentKind.Sampler) { Sampler = sampler ?? throw new ArgumentNullException(nameof(sampler)) };
    public static Instrument FromPlugin(string pluginId) =>
        new(InstrumentKind.Plugin) { PluginId = Require(pluginId) };

    private static string Require(string id) =>
        string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Plugin id required.", nameof(id)) : id;
}

/// <summary>An insert effect in a track's device chain: a built-in EQ/Comp/Reverb/
/// Delay/Utility or a hosted plug-in. <see cref="Params"/> holds the built-in
/// parameter values (empty for plug-ins, whose state travels as an opaque blob).</summary>
public sealed class Device
{
    public DeviceKind Kind { get; }
    public bool Bypassed { get; set; }
    public IReadOnlyList<float> Params { get; set; } = Array.Empty<float>();
    /// <summary>Stable plug-in identifier when <see cref="Kind"/> is <see cref="DeviceKind.Plugin"/>.</summary>
    public string? PluginId { get; set; }

    public Device(DeviceKind kind) => Kind = kind;

    public bool IsPlugin => Kind == DeviceKind.Plugin;
}
