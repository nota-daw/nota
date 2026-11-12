// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The classic look (spec 091) as data: reading and storing the choice, the classic corner
// scale, how a skin composes with either look, and the format-3 theme file that adds a
// typeface for values. No UI — the wearing is the app's; these are the rules it follows.

using Nota.Application;
using Nota.Application.Themes;

namespace Nota.SmokeTest;

internal static class LooksCheck
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- Resolve / Store ----------------------------------------------------------------
        var cases = new (string Stored, Look Want)[]
        {
            ("", Look.New), ("new", Look.New), ("classic", Look.Classic), ("Classic", Look.New), ("x", Look.New),
        };
        foreach (var (stored, want) in cases)
        {
            var s = new Settings { Look = stored };
            var got = Looks.Resolve(s);
            yield return (got == want && s.Look == stored, $"looks: \"{stored}\" reads as {want} and is not rewritten (got {got})");
        }
        foreach (var look in new[] { Look.New, Look.Classic })
        {
            var s = new Settings();
            Looks.Store(s, look);
            yield return (Looks.Resolve(s) == look, $"looks: {look} round-trips through settings (\"{s.Look}\")");
        }
        yield return (new Settings().Look == "" && Looks.Resolve(new Settings()) == Look.New, "looks: a fresh install is New");
        yield return (Looks.Parse("classic") == Look.Classic && Looks.Parse("new") == Look.New && Looks.Parse("old") is null,
            "looks: MCP ids parse; an unknown id is refused");

        // ---- Clip style (User Story 4) --------------------------------------------------------
        foreach (var (stored, look, want) in new (string, string, ClipStyle)[]
                 {
                     ("", "", ClipStyle.Tinted), ("", "classic", ClipStyle.Solid), ("tinted", "classic", ClipStyle.Tinted),
                     ("solid", "", ClipStyle.Solid), ("x", "classic", ClipStyle.Solid), ("x", "", ClipStyle.Tinted),
                 })
        {
            var s = new Settings { ClipStyle = stored, Look = look };
            yield return (Looks.ClipStyleOf(s) == want, $"clips: \"{stored}\" under \"{look}\" is {want} (got {Looks.ClipStyleOf(s)})");
        }
        {
            var s = new Settings { Look = "classic" };
            Looks.StoreClipStyle(s, ClipStyle.Tinted);
            Looks.Store(s, Look.New); Looks.Store(s, Look.Classic);
            yield return (Looks.ClipStyleOf(s) == ClipStyle.Tinted, "clips: a pick survives look changes");
        }

        // ---- Compose ------------------------------------------------------------------------
        var steps = Enum.GetValues<RadiusStep>();
        var none = new Dictionary<RadiusStep, double>();

        var newDefault = Looks.Compose(SkinGeometry.Default, Look.New);
        yield return (newDefault.IsDefault && steps.All(st => newDefault.MapRadius(SkinGeometry.Design(st)) == SkinGeometry.Design(st)),
            "looks: default skin under New is the design scale, an exact no-op");

        var classic = Looks.Compose(SkinGeometry.Default, Look.Classic);
        var wantSteps = new[] { 3.0, 3, 3, 2, 2, 2, 2 };
        var gotSteps = steps.Select(st => classic.RadiusOf(st)).ToArray();
        yield return (gotSteps.SequenceEqual(wantSteps), $"looks: Classic steps are 3/3/3/2/2/2/2 (got {string.Join("/", gotSteps)})");
        yield return (classic.MapRadius(9) == 3 && classic.MapRadius(6) == 3 && classic.MapRadius(2) == 2
                      && classic.MapRadius(0) == 0 && classic.MapRadius(10) == 4 && classic.FontFamily is null && classic.ValuesFamily is null,
            "looks: Classic maps card 9→3, field 6→3, fine 2→2, docked 0→0, off-scale 10→4; Geist stays");

        var fontOnly = new SkinGeometry("Helvetica", 1, none);
        var fontClassic = Looks.Compose(fontOnly, Look.Classic);
        yield return (fontClassic.FontFamily == "Helvetica" && fontClassic.RadiusOf(RadiusStep.Card) == 3,
            "looks: a skin with only a typeface keeps it under Classic and takes the classic corners");
        yield return (Looks.Compose(fontOnly, Look.New).Equals(fontOnly), "looks: the same skin under New is unchanged");

        var square = new SkinGeometry("Helvetica", 0, none, null, true);
        var squareClassic = Looks.Compose(square, Look.Classic);
        yield return (squareClassic.Equals(square) && steps.All(st => squareClassic.RadiusOf(st) == 0),
            "looks: a skin that sets its radii (scale 0) keeps them under Classic");

        var designRadius = new SkinGeometry(null, 1, none, null, true);
        yield return (Looks.Compose(designRadius, Look.Classic).RadiusOf(RadiusStep.Card) == 9,
            "looks: a skin whose radius object is the design scale keeps 9 under Classic");

        var values = new SkinGeometry(null, 1, none, "Menlo");
        yield return (Looks.Compose(values, Look.Classic).ValuesFamily == "Menlo" && Looks.Compose(values, Look.New).ValuesFamily == "Menlo",
            "looks: a skin's values typeface is worn under both looks");

        // ---- Format 3 -------------------------------------------------------------------------
        foreach (var r in Format3()) yield return r;
    }

    private static IEnumerable<(bool Ok, string Label)> Format3()
    {
        var catalogue = new[] { "BgApp" };
        var none = new HashSet<string>();
        string File(int version, string font, string radius = "") =>
            $$"""{ "formatVersion": {{version}}, "id": "t", "name": "T", "kind": "dark", {{font}}{{radius}} "colors": { "BgApp": "#101010" } }""";
        ThemeValidation V(string json) => ThemeFile.Validate(json, catalogue, none);
        bool Names(ThemeValidation v, string field) => !v.IsValid && v.Problems.Any(p => p.Field == field);

        var both = V(File(3, """ "font": { "family": "Helvetica", "values": "Menlo" }, """));
        yield return (both.IsValid && both.Theme!.Geometry.FontFamily == "Helvetica" && both.Theme.Geometry.ValuesFamily == "Menlo"
                      && both.Theme.FormatVersion == 3, "skins v3: family + values validate");
        var valuesOnly = V(File(3, """ "font": { "values": "Menlo" }, """));
        yield return (valuesOnly.IsValid && valuesOnly.Theme!.Geometry.FontFamily is null && valuesOnly.Theme.Geometry.ValuesFamily == "Menlo",
            "skins v3: values alone validate");
        yield return (Names(V(File(2, """ "font": { "family": "Helvetica", "values": "Menlo" }, """)), "font.values"),
            "skins v3: font.values in format 2 is a problem naming font.values");
        yield return (Names(V(File(3, """ "font": { "values": "  " }, """)), "font.values"), "skins v3: an empty values name is a problem");
        yield return (Names(V(File(3, $$""" "font": { "values": "{{new string('a', 101)}}" }, """)), "font.values"),
            "skins v3: a 101-character values name is a problem");
        yield return (Names(V(File(3, """ "font": { "values": "Menlo", "mono": "x" }, """)), "font.mono"),
            "skins v3: an unknown font key is a problem naming it");
        yield return (Names(V(File(3, """ "font": { }, """)), "font.family"), "skins v3: an empty font object is a problem");
        yield return (!V(File(4, "")).IsValid, "skins v3: format 4 is refused");

        var missing = ThemeFile.Validate(File(3, """ "font": { "values": "Nope Mono" }, """), catalogue, none, _ => false);
        yield return (missing.IsValid && missing.Warnings.Any(w => w.Kind == ThemeWarning.FontMissing && w.Tokens.Contains("Nope Mono")),
            "skins v3: a values font that is not installed warns and still loads");

        var radiusOnly = V(File(2, "", """ "radius": { }, """));
        yield return (radiusOnly.IsValid && radiusOnly.Theme!.Geometry.HasRadius, "skins: an empty radius object counts as the skin setting its radii");
        var v1 = V(File(1, ""));
        yield return (v1.IsValid && v1.Theme!.Geometry.IsDefault && !v1.Theme.Geometry.HasRadius, "skins: format 1 is the default geometry");

        // Round trips: the writer picks the lowest format that holds the geometry.
        foreach (var (label, json, version) in new[]
                 {
                     ("format 1", File(1, ""), 1),
                     ("format 2 font", File(2, """ "font": { "family": "Helvetica" }, """), 2),
                     ("format 2 radius", File(2, "", """ "radius": { "scale": 0.5 }, """), 2),
                     ("format 3", File(3, """ "font": { "values": "Menlo" }, """), 3),
                 })
        {
            var t = V(json).Theme!;
            var again = V(ThemeFile.Write(t, catalogue));
            yield return (again.IsValid && again.Theme!.FormatVersion == version && again.Theme.Geometry.Equals(t.Geometry),
                $"skins: {label} writes back as format {version} with the same geometry");
        }
    }
}
