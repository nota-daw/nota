// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// User skins (spec 087): the shared theme-file validator (format 1 and 2), the skin
// geometry's radius mapping, and the user themes folder — as data, with no UI. The
// validator must agree with the independent spec-011 checks above on the built-ins, report
// every problem in a broken file naming its field, and never throw.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nota.Application.Themes;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    public static IEnumerable<(bool Ok, string Label)> RunSkins()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var palette = File.ReadAllText(Path.Combine(app, "Theme/NotaPalette.cs"));
        var catalogue = Regex.Matches(palette, @"\bT\((?:nameof\((?<n>\w+)\)|""(?<n>\w+)"")\)").Select(m => m.Groups["n"].Value)
            .Concat(ExtraTokens).ToList();
        var files = Directory.EnumerateFiles(Path.Combine(app, "Theme/Themes"), "*.json").OrderBy(f => f).ToList();

        // ---- the built-ins: valid, and the T3 port agrees with the spec-011 check ----------
        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var json = File.ReadAllText(file);
            var v = ThemeFile.Validate(json, catalogue, AlphaTokens);
            yield return (v.IsValid && v.Theme!.Id == id && v.Theme.FormatVersion == 1 && v.Theme.Geometry.IsDefault,
                v.IsValid ? $"skins: built-in {id} validates (format 1, default geometry)" : $"skins: built-in {id} — {ThemeFolder.Describe(v.Problems)}");
            if (!v.IsValid) continue;
            var (data, _) = LoadTheme(json, catalogue.ToHashSet());
            int expected = Pairings(data!.Colors).Count(p => p.Value < p.Min - 1e-9);
            int got = v.Warnings.Count(w => w.Kind == ThemeWarning.Readability);
            yield return (got == expected, $"skins: {id} readability warnings match the spec-011 check ({got} = {expected})");

            // A low-contrast copy: both checks see the same failures.
            var node = JsonNode.Parse(json)!;
            node["colors"]!["TextMuted"] = data.Colors["Panel"];
            node["colors"]!["Track2"] = data.Colors["Track1"];
            var mutated = node.ToJsonString();
            var (mData, _) = LoadTheme(mutated, catalogue.ToHashSet());
            int mExpected = Pairings(mData!.Colors).Count(p => p.Value < p.Min - 1e-9);
            var mv = ThemeFile.Validate(mutated, catalogue, AlphaTokens);
            int mGot = mv.Warnings.Count(w => w.Kind == ThemeWarning.Readability);
            yield return (mv.IsValid && mGot == mExpected && mGot > 0
                && mv.Warnings.Any(w => w.Pairing == "T3.1" && w.Tokens.Contains("TextMuted"))
                && mv.Warnings.Any(w => w.Pairing == "T3.7"),
                $"skins: {id} with unreadable text still loads, with {mGot} warnings naming the pairings (spec-011 check: {mExpected})");

            // Write → Validate round-trips, colours and all.
            var back = ThemeFile.Validate(ThemeFile.Write(v.Theme!, catalogue), catalogue, AlphaTokens);
            yield return (back.IsValid && back.Theme!.Colors.Count == v.Theme!.Colors.Count
                && back.Theme.Colors.All(kv => v.Theme.Colors[kv.Key] == kv.Value),
                $"skins: {id} round-trips through the writer");
        }

        // ---- broken files: every problem reported, naming its field ----------------------
        var good = JsonNode.Parse(File.ReadAllText(files.First(f => f.EndsWith("studio-dark.json"))))!;
        JsonNode Copy() => JsonNode.Parse(good.ToJsonString())!;
        string With(Action<JsonNode> edit) { var n = Copy(); n["id"] = "my-skin"; edit(n); return n.ToJsonString(); }

        var cases = new (string What, string Json, string Field)[]
        {
            ("missing token", With(n => n["colors"]!.AsObject().Remove("Panel")), "colors.Panel"),
            ("unknown token", With(n => n["colors"]!["Sparkle"] = "#FFFFFF"), "colors.Sparkle"),
            ("bad hex", With(n => n["colors"]!["Panel"] = "#12345"), "colors.Panel"),
            ("translucent opaque token", With(n => n["colors"]!["Panel"] = "#80141310"), "colors.Panel"),
            ("bad id", With(n => n["id"] = "My Skin"), "id"),
            ("system id", With(n => n["id"] = "system"), "id"),
            ("bad kind", With(n => n["kind"] = "dusk"), "kind"),
            ("long name", With(n => n["name"] = new string('x', 41)), "name"),
            ("format 3", With(n => n["formatVersion"] = 3), "formatVersion"),
            ("v1 with font", With(n => n["font"] = new JsonObject { ["family"] = "Helvetica" }), "font"),
            ("scale 3", With(n => { n["formatVersion"] = 2; n["radius"] = new JsonObject { ["scale"] = 3 }; }), "radius.scale"),
            ("unknown step", With(n => { n["formatVersion"] = 2; n["radius"] = new JsonObject { ["steps"] = new JsonObject { ["huge"] = 4 } }; }), "radius.steps.huge"),
            ("step 30", With(n => { n["formatVersion"] = 2; n["radius"] = new JsonObject { ["steps"] = new JsonObject { ["card"] = 30 } }; }), "radius.steps.card"),
            ("empty family", With(n => { n["formatVersion"] = 2; n["font"] = new JsonObject { ["family"] = "  " }; }), "font.family"),
            ("font as string", With(n => { n["formatVersion"] = 2; n["font"] = "Helvetica"; }), "font"),
        };
        foreach (var (what, json, field) in cases)
        {
            var v = ThemeFile.Validate(json, catalogue, AlphaTokens);
            yield return (!v.IsValid && v.Problems.Any(p => p.Field == field),
                $"skins: {what} is rejected naming {field}" + (v.IsValid ? " (but it validated)" : $" — {ThemeFolder.Describe(v.Problems)}"));
        }

        foreach (var (what, json) in new[] { ("invalid JSON", "{ \"formatVersion\": 1, "), ("an array", "[1,2]"), ("empty", "") })
        {
            ThemeValidation? v = null;
            Exception? ex = null;
            try { v = ThemeFile.Validate(json, catalogue, AlphaTokens); } catch (Exception e) { ex = e; }
            yield return (ex is null && v is { IsValid: false } && v.Problems.Count > 0, $"skins: {what} is a problem, not an exception");
        }

        var three = ThemeFile.Validate(With(n => { n["kind"] = "dusk"; n["colors"]!["Panel"] = "red"; n["colors"]!.AsObject().Remove("BgApp"); }), catalogue, AlphaTokens);
        yield return (three.Problems.Count >= 3 && three.Problems.Any(p => p.Field == "kind")
                && three.Problems.Any(p => p.Field == "colors.Panel") && three.Problems.Any(p => p.Field == "colors.BgApp"),
            $"skins: a file with three problems reports all of them ({three.Problems.Count})");

        // ---- format 2 parsed --------------------------------------------------------------
        var v2 = ThemeFile.Validate(With(n =>
        {
            n["formatVersion"] = 2;
            n["font"] = new JsonObject { ["family"] = "Helvetica" };
            n["radius"] = new JsonObject { ["scale"] = 0.5, ["steps"] = new JsonObject { ["card"] = 12, ["valueBar"] = 0 } };
        }), catalogue, AlphaTokens, fontInstalled: f => f == "Helvetica");
        var g = v2.Theme?.Geometry;
        yield return (v2.IsValid && g is not null && g.FontFamily == "Helvetica" && g.Roundness == 0.5
                && g.RadiusOf(RadiusStep.Card) == 12 && g.RadiusOf(RadiusStep.ValueBar) == 0 && g.RadiusOf(RadiusStep.Group) == 4
                && !v2.Warnings.Any(w => w.Kind == ThemeWarning.FontMissing),
            "skins: format 2 reads the typeface, the roundness and step overrides");
        var missingFont = ThemeFile.Validate(With(n => { n["formatVersion"] = 2; n["font"] = new JsonObject { ["family"] = "No Such Font" }; }),
            catalogue, AlphaTokens, fontInstalled: _ => false);
        yield return (missingFont.IsValid && missingFont.Warnings.Any(w => w.Kind == ThemeWarning.FontMissing),
            "skins: a typeface that is not installed is a warning, and the theme still loads");
        var round = ThemeFile.Validate(ThemeFile.Write(v2.Theme!, catalogue), catalogue, AlphaTokens);
        yield return (round.IsValid && round.Theme!.FormatVersion == 2 && round.Theme.Geometry.Equals(g),
            "skins: a format-2 theme round-trips through the writer");

        // ---- geometry ---------------------------------------------------------------------
        var steps = Enum.GetValues<RadiusStep>();
        var square = new SkinGeometry(null, 0, new Dictionary<RadiusStep, double>());
        var double2 = new SkinGeometry(null, 2, new Dictionary<RadiusStep, double> { [RadiusStep.Fine] = 1 });
        yield return (steps.All(s => SkinGeometry.Default.MapRadius(SkinGeometry.Design(s)) == SkinGeometry.Design(s))
                && SkinGeometry.Default.MapRadius(12) == 12,
            "skins: the default geometry maps every radius to itself");
        yield return (steps.All(s => square.MapRadius(SkinGeometry.Design(s)) == 0) && square.MapRadius(12) == 0 && square.MapRadius(0) == 0,
            "skins: roundness 0 squares every corner");
        yield return (double2.MapRadius(9) == 18 && double2.MapRadius(2) == 1 && double2.MapRadius(0) == 0 && double2.MapRadius(11) == 22,
            "skins: roundness 2 doubles the scale; a step override wins; off-scale radii scale");
        yield return (steps.Select(SkinGeometry.Design).Distinct().Count() == steps.Length,
            "skins: the design radius steps are distinct (a value names its step)");
        var geometry = File.ReadAllText(Path.Combine(app, "Theme/NotaGeometry.cs"));
        var mismatch = steps.Where(s => !Regex.IsMatch(geometry, $@"RadiusStep\.{s}\b")).ToArray();
        yield return (mismatch.Length == 0,
            mismatch.Length == 0 ? "skins: NotaRadius reads every step through the skin" : $"skins: NotaRadius does not use step(s) {string.Join(", ", mismatch)}");

        // ---- ids --------------------------------------------------------------------------
        var taken = new HashSet<string> { "studio-dark-copy" };
        yield return (ThemeFile.FreeId("Studio Dark copy", taken.Contains) == "studio-dark-copy-2"
                && ThemeFile.FreeId("Café Noir", _ => false) == "cafe-noir" && ThemeFile.FreeId("???", _ => false) == "theme",
            "skins: a duplicate's id is a free slug of its name");

        // ---- the folder -------------------------------------------------------------------
        var dir = Path.Combine(Path.GetTempPath(), "nota-skins-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        ThemeFolderScan scan;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.json"), With(n => n["name"] = "A"));
            File.WriteAllText(Path.Combine(dir, "b.json"), With(n => n["name"] = "B"));                 // same id as a.json
            File.WriteAllText(Path.Combine(dir, "c.json"), With(n => n["id"] = "studio-dark"));         // a built-in id
            File.WriteAllText(Path.Combine(dir, "d.json"), With(n => n["colors"]!["Panel"] = "nope"));  // invalid
            File.WriteAllText(Path.Combine(dir, "e.json"), new string(' ', ThemeFile.MaxFileBytes + 1)); // too large
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a theme");
            scan = ThemeFolder.Scan(dir, catalogue, AlphaTokens, id => id == "studio-dark");
        }
        finally { Directory.Delete(dir, true); }
        string Reason(string file) => scan.Problems.FirstOrDefault(p => p.FileName == file).Reason ?? "";
        yield return (scan.Loaded.Count == 1 && scan.Loaded[0].FileName == "a.json",
            $"skins: the folder loads the one good file ({string.Join(", ", scan.Loaded.Select(l => l.FileName))})");
        yield return (Reason("b.json").Contains("duplicate id") && Reason("c.json").Contains("built-in")
                && Reason("d.json").Contains("Panel") && Reason("e.json").Contains("larger") && scan.Problems.Count == 4,
            "skins: duplicate, built-in id, invalid and oversized files are reported by name; other files are ignored");
        var none = ThemeFolder.Scan(Path.Combine(dir, "missing"), catalogue, AlphaTokens, _ => false);
        yield return (none.Loaded.Count == 0 && none.Problems.Count == 0, "skins: a missing folder is an empty one");
    }
}
