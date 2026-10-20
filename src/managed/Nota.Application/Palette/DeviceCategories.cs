// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's shelves: which category each built-in device sits under on its tab, and how
// a plug-in's own reported category maps onto the same shelves. Kept apart from
// BuiltinDeviceCatalog (whose descriptors serve the palette's search) so one block reads like
// the list a musician would review. A new built-in device is added here too — the browser
// design check (RunBrowser) fails until it is.

namespace Nota.Application;

public static class DeviceCategories
{
    /// <summary>Where a plug-in (or an unlisted built-in) lands when nothing maps it. Always last.</summary>
    public const string Other = "Other";

    public sealed record Category(string Name, string[] Builtins);

    private static readonly Category[] Instruments =
    {
        new("Synths", ["Nota Volt", "Nota Monolith", "Nota Pentad", "Nota Consort",
                       "Nota Operator", "Nota Aurora", "Nota Flux", "Nota Pendulum"]),
        new("Keys & physical", ["Nota Keys", "Nota Physical"]),
        new("Drums", ["Nota Rhythm", "Nota Drum Rack"]),
        new("Samplers", ["Nota Sampler", "Nota Mosaic", "Nota Grain"]),
        new("Racks", ["Nota Instrument Rack"]),
    };

    private static readonly Category[] AudioEffects =
    {
        new("EQ & filter", ["Nota EQ-3", "Nota EQ-8", "Nota Dynamic EQ-8", "Nota Auto Filter"]),
        new("Dynamics", ["Nota Compressor", "Nota Prism", "Nota Ceiling", "Nota Shutter"]),
        new("Reverb & delay", ["Nota Reverb", "Nota Chamber", "Nota Delay"]),
        new("Modulation", ["Nota Chorus", "Nota Flanger", "Nota Phaser", "Nota Orbit", "Nota Vocoder"]),
        new("Distortion", ["Nota Valve", "Nota Vintage", "Nota Forge", "Nota Crush"]),
        new("Pitch & time", ["Nota Auto Shift", "Nota Beat Repeat", "Nota Strata"]),
        new("Utility", ["Nota Utility", "Nota Level", "Nota Lens"]),
        new("Racks", ["Nota Audio Effect Rack"]),
    };

    private static readonly Category[] MidiEffects =
    {
        new("Generate", ["Nota Arp", "Nota Chord", "Nota Random"]),
        new("Shape", ["Nota Scale", "Nota Length", "Nota Velocity"]),
    };

    /// <summary>The categories of one tab, in display order, with their built-in members.</summary>
    public static IReadOnlyList<Category> Of(BuiltinDeviceType type) => type switch
    {
        BuiltinDeviceType.Instrument => Instruments,
        BuiltinDeviceType.AudioEffect => AudioEffects,
        _ => MidiEffects,
    };

    /// <summary>The category names of one tab, in display order (<see cref="Other"/> not included).</summary>
    public static IReadOnlyList<string> Order(BuiltinDeviceType type) => Of(type).Select(c => c.Name).ToArray();

    /// <summary>A category's place on its tab; <see cref="Other"/> and unknown names sort last.</summary>
    public static int Position(BuiltinDeviceType type, string name)
    {
        var cats = Of(type);
        for (int i = 0; i < cats.Count; i++) if (cats[i].Name == name) return i;
        return int.MaxValue;
    }

    /// <summary>The category a built-in device is shelved under, or <see cref="Other"/>.</summary>
    public static string For(BuiltinDevice device)
    {
        foreach (var c in Of(device.Type))
            if (Array.IndexOf(c.Builtins, device.Name) >= 0) return c.Name;
        return Other;
    }

    // A plug-in's reported category, token by token: VST3 sub-categories ("Fx|Dynamics|EQ",
    // which the plug-in host hands over as "Fx/Dynamics/EQ"), or an AU type name. The first
    // token a table knows wins. Skipped on purpose: the markers "fx", "instrument", "mono",
    // "stereo"; "generator", "external", "network"; AU type names ("effect", "music effect"),
    // which say what a plug-in is but not what it does — those land in Other.
    private static readonly Dictionary<string, string> InstrumentTokens = new(StringComparer.Ordinal)
    {
        ["synth"] = "Synths",
        ["piano"] = "Keys & physical",
        ["drum"] = "Drums",
        ["sampler"] = "Samplers",
    };

    private static readonly Dictionary<string, string> EffectTokens = new(StringComparer.Ordinal)
    {
        ["eq"] = "EQ & filter", ["filter"] = "EQ & filter",
        ["dynamics"] = "Dynamics", ["mastering"] = "Dynamics",
        ["reverb"] = "Reverb & delay", ["delay"] = "Reverb & delay",
        ["modulation"] = "Modulation",
        ["distortion"] = "Distortion",
        ["pitch shift"] = "Pitch & time",
        ["analyzer"] = "Utility", ["tools"] = "Utility", ["spatial"] = "Utility",
        ["surround"] = "Utility", ["restoration"] = "Utility", ["up-downmix"] = "Utility",
    };

    /// <summary>The category an installed plug-in joins, from the category it reports.</summary>
    public static string ForPlugin(bool instrument, string reported)
    {
        var table = instrument ? InstrumentTokens : EffectTokens;
        foreach (var raw in (reported ?? "").Split('/', '|'))
        {
            var token = raw.Trim().ToLowerInvariant();
            if (table.TryGetValue(token, out var name)) return name;
        }
        return Other;
    }
}
