// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The pages a controller's eight knobs walk on each built-in device (specs/051, FR-014): up to
// eight parameters per page, the ones you play with first (filter, tone, envelope), named
// exactly as the engine reports them — a name, not an index, so a parameter list that grows or
// reorders never shifts a page (research R8). Racks get their eight macros; a plug-in without
// pages of its own gets its parameters in order, eight per page (FR-015).
//
// The pages themselves are data: DevicePages.json beside this file, embedded, in the same format
// as the user's pages (contracts/device-pages.md). Here: which kind is which type key. A new
// built-in kind needs a slug below and pages in the JSON — the smoke test fails naming any kind
// without them, and any name that does not resolve on a fresh device.

namespace Nota.Application.Controllers;

/// <summary>One page: an optional label and up to eight parameter references (null = an empty
/// knob). References: a built-in's parameter name, <c>id:&lt;PluginParamId&gt;</c> for a plug-in,
/// <c>macro:&lt;n&gt;</c> (1-based) for a rack.</summary>
public sealed record DevicePage(string? Name, IReadOnlyList<string?> Params)
{
    public const int Knobs = 8;
}

/// <summary>Edited pages that win over the catalog (the project copy, then the user's).</summary>
public interface IDevicePageSource
{
    /// <summary>The edited pages for <paramref name="typeKey"/>, or null when it has none.</summary>
    IReadOnlyList<DevicePage>? PagesFor(string typeKey);
}

public static class DevicePageCatalog
{
    public const string MacroPrefix = "macro:", IdPrefix = "id:";
    public const string Resource = "Nota.ControllerPages.json";

    private sealed record Kind(FocusSection Section, int Number, string Slug);

    // Every built-in kind by its engine number (BuiltinDeviceCatalog), with the name type keys use.
    private static readonly Kind[] Kinds =
    {
        new(FocusSection.Instrument, 0, "synth"),
        new(FocusSection.Instrument, 1, "sampler"),
        new(FocusSection.Instrument, 2, "physical"),
        new(FocusSection.Instrument, 5, "aurora"),
        new(FocusSection.Instrument, 6, "volt"),
        new(FocusSection.Instrument, 7, "bass"),
        new(FocusSection.Instrument, 8, "pendulum"),
        new(FocusSection.Instrument, 9, "operator"),
        new(FocusSection.Instrument, 10, "grain"),
        new(FocusSection.Instrument, 11, "flux"),
        new(FocusSection.Instrument, 12, "rhythm"),
        new(FocusSection.Instrument, 13, "monolith"),
        new(FocusSection.Instrument, 14, "pentad"),
        new(FocusSection.Instrument, 15, "consort"),
        new(FocusSection.Instrument, 16, "keys"),
        new(FocusSection.Instrument, 17, "mosaic"),
        new(FocusSection.Instrument, 18, "twin"),
        new(FocusSection.Audio, 0, "eq-8"),
        new(FocusSection.Audio, 1, "compressor"),
        new(FocusSection.Audio, 2, "reverb"),
        new(FocusSection.Audio, 3, "delay"),
        new(FocusSection.Audio, 4, "utility"),
        new(FocusSection.Audio, 6, "valve"),
        new(FocusSection.Audio, 7, "auto-filter"),
        new(FocusSection.Audio, 8, "vintage"),
        new(FocusSection.Audio, 9, "orbit"),
        new(FocusSection.Audio, 10, "auto-shift"),
        new(FocusSection.Audio, 11, "beat-repeat"),
        new(FocusSection.Audio, 12, "crush"),
        new(FocusSection.Audio, 13, "dynamic-eq-8"),
        new(FocusSection.Audio, 14, "ceiling"),
        new(FocusSection.Audio, 15, "strata"),
        new(FocusSection.Audio, 16, "eq-3"),
        new(FocusSection.Audio, 17, "forge"),
        new(FocusSection.Audio, 18, "level"),
        new(FocusSection.Audio, 19, "shutter"),
        new(FocusSection.Audio, 20, "chamber"),
        new(FocusSection.Audio, 21, "prism"),
        new(FocusSection.Audio, 22, "lens"),
        new(FocusSection.Audio, 23, "flanger"),
        new(FocusSection.Audio, 24, "phaser"),
        new(FocusSection.Audio, 25, "chorus"),
        new(FocusSection.Audio, 26, "vocoder"),
        new(FocusSection.Midi, 0, "arp"),
        new(FocusSection.Midi, 1, "chord"),
        new(FocusSection.Midi, 2, "scale"),
        new(FocusSection.Midi, 3, "length"),
        new(FocusSection.Midi, 4, "velocity"),
        new(FocusSection.Midi, 5, "random"),
    };

    // Racks: their macros, whatever the kind (racks key by rack type, not by kind).
    private static readonly (FocusSection Section, int Kind, string Key)[] Racks =
    {
        (FocusSection.Instrument, 3, DeviceTypeKey.RackInstrument),   // Instrument Rack
        (FocusSection.Instrument, 4, DeviceTypeKey.RackInstrument),   // Drum Rack
        (FocusSection.Audio, 5, DeviceTypeKey.RackAudio),
        (FocusSection.Midi, 6, DeviceTypeKey.RackMidi),
    };

    private static readonly Lazy<Dictionary<string, IReadOnlyList<DevicePage>>> _shipped = new(() =>
    {
        using var s = typeof(DevicePageCatalog).Assembly.GetManifestResourceStream(Resource);
        if (s is null) return new();
        using var r = new StreamReader(s);
        return DevicePagesFile.Parse(r.ReadToEnd(), out _) ?? new();
    });

    /// <summary>The type key of a rack kind, or null for any other kind.</summary>
    public static string? RackKey(FocusSection section, int kind)
    {
        foreach (var r in Racks) if (r.Section == section && r.Kind == kind) return r.Key;
        return null;
    }

    /// <summary>A built-in kind's name in type keys ("volt", "auto-filter"); "kind-N" if unknown.</summary>
    public static string Slug(FocusSection section, int kind)
    {
        foreach (var k in Kinds) if (k.Section == section && k.Number == kind) return k.Slug;
        return "kind-" + kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Every built-in kind the catalog knows (racks included), for the completeness check.</summary>
    public static IEnumerable<(FocusSection Section, int Kind, string TypeKey)> Known
        => Kinds.Select(k => (k.Section, k.Number, DeviceTypeKey.Builtin(k.Section, k.Number)))
            .Concat(Racks.Select(r => (r.Section, r.Kind, r.Key)));

    /// <summary>The shipped pages for a built-in or rack type key, or null.</summary>
    public static IReadOnlyList<DevicePage>? Builtin(string typeKey)
        => _shipped.Value.TryGetValue(typeKey, out var pages) ? pages : null;

    /// <summary>A plug-in without pages: its parameters in order, eight per page (FR-015).</summary>
    public static IReadOnlyList<DevicePage> PluginDefault(IReadOnlyList<string> paramIds)
    {
        var pages = new List<DevicePage>();
        for (int i = 0; i < paramIds.Count; i += DevicePage.Knobs)
        {
            var refs = new string?[Math.Min(DevicePage.Knobs, paramIds.Count - i)];
            for (int k = 0; k < refs.Length; k++) refs[k] = IdPrefix + paramIds[i + k];
            pages.Add(new DevicePage(null, refs));
        }
        return pages;
    }

    /// <summary>The pages a device of <paramref name="typeKey"/> shows: edited pages first
    /// (<paramref name="edited"/>: project copy, then the user's), then the catalog, then — for a
    /// plug-in — its parameters in order.</summary>
    public static IReadOnlyList<DevicePage> Resolve(string typeKey, IDevicePageSource? edited, Func<IReadOnlyList<string>> pluginParamIds)
    {
        if (edited?.PagesFor(typeKey) is { Count: > 0 } mine) return mine;
        if (Builtin(typeKey) is { } shipped) return shipped;
        return DeviceTypeKey.IsPlugin(typeKey) ? PluginDefault(pluginParamIds()) : Array.Empty<DevicePage>();
    }
}
