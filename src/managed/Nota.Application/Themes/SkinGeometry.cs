// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a skin does to type and corners (spec 087): an optional system typeface and a corner
// roundness over the style guide's radius scale (spec 038). The design scale stays the base
// every control is written against; MapRadius turns a design value into the skin's value, so
// mapping never compounds and the default geometry is an exact no-op. Format 3 (spec 091) adds
// a typeface for values and readouts, and remembers whether the file set its radii at all —
// a skin that did keeps them under the classic look; one that did not takes the classic scale.

namespace Nota.Application.Themes;

/// <summary>The steps of the style guide's radius scale, largest first. Docked panels (0) are
/// not a step: they meet their neighbours on a seam and never round.</summary>
public enum RadiusStep { Card, Group, Field, Cell, ValueBar, Switch, Fine }

/// <param name="ValuesFamily">The typeface for values and readouts (format 3 <c>font.values</c>), or null
/// for the look's own (Geist under New, Geist Mono under Classic).</param>
/// <param name="HasRadius">The file carried a <c>radius</c> object — even one equal to the design
/// scale — so its radii are worn under both looks.</param>
public sealed record SkinGeometry(string? FontFamily, double Roundness, IReadOnlyDictionary<RadiusStep, double> StepOverrides,
    string? ValuesFamily = null, bool HasRadius = false)
{
    public const double MinRoundness = 0, MaxRoundness = 2, MinStep = 0, MaxStep = 24;
    public const int MaxFamilyLength = 100;

    /// <summary>Geist and the design scale — every built-in theme and every format-1 file.</summary>
    public static readonly SkinGeometry Default = new(null, 1, new Dictionary<RadiusStep, double>());

    /// <summary>The style guide's value for each step (NotaRadius in the app mirrors these).</summary>
    public static double Design(RadiusStep step) => step switch
    {
        RadiusStep.Card => 9,
        RadiusStep.Group => 7,
        RadiusStep.Field => 6,
        RadiusStep.Cell => 5,
        RadiusStep.ValueBar => 4,
        RadiusStep.Switch => 3,
        _ => 2,
    };

    /// <summary>The key a step has in a theme file's <c>radius.steps</c>.</summary>
    public static string Key(RadiusStep step) => step switch
    {
        RadiusStep.ValueBar => "valueBar",
        _ => step.ToString().ToLowerInvariant(),
    };

    public static RadiusStep? FromKey(string key) =>
        Enum.GetValues<RadiusStep>().Cast<RadiusStep?>().FirstOrDefault(s => Key(s!.Value) == key);

    public bool IsDefault => FontFamily is null && Roundness == 1 && StepOverrides.Count == 0
                             && ValuesFamily is null && !HasRadius;

    /// <summary>The radii are the design scale (whatever the typefaces): mapping a radius is a no-op.</summary>
    public bool IsDesignScale => Roundness == 1 && StepOverrides.Count == 0;

    /// <summary>The radius this skin gives <paramref name="step"/>, in whole pixels.</summary>
    public double RadiusOf(RadiusStep step) =>
        StepOverrides.TryGetValue(step, out var v) ? v : Math.Round(Design(step) * Roundness, MidpointRounding.AwayFromZero);

    /// <summary>A radius as written in the code (a design value) as this skin draws it: 0 stays
    /// square, a value on the scale takes its step's radius, any other is scaled by roundness.</summary>
    public double MapRadius(double designValue)
    {
        if (designValue <= 0 || IsDesignScale) return designValue;
        foreach (var step in Enum.GetValues<RadiusStep>())
            if (designValue == Design(step)) return RadiusOf(step);
        return Math.Round(designValue * Roundness, MidpointRounding.AwayFromZero);
    }

    // Records compare dictionaries by reference; two geometries are the same skin when their
    // values are.
    public bool Equals(SkinGeometry? other) =>
        other is not null && FontFamily == other.FontFamily && Roundness == other.Roundness
        && ValuesFamily == other.ValuesFamily && HasRadius == other.HasRadius
        && StepOverrides.Count == other.StepOverrides.Count
        && StepOverrides.All(kv => other.StepOverrides.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(FontFamily, Roundness, StepOverrides.Count, ValuesFamily, HasRadius);
}
