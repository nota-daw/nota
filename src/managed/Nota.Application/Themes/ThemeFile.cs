// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The theme file (spec 011, format 1; spec 087, format 2) as data: one validator for the
// built-in loader, the user themes folder, the MCP tools and the smoke test. It never throws
// and reports every problem it finds, so a hand-written file can be fixed in one pass. The
// readability pairings of spec 011 (T3) are warnings here: a user may choose a low-contrast
// look for themselves; the built-ins are held to them by the smoke test. No Avalonia types —
// colours are ARGB uints — so the MCP project and the tests use it without the UI.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nota.Application.Localization;

namespace Nota.Application.Themes;

/// <summary>A theme file that validated: everything the app needs to wear it.</summary>
public sealed record ParsedTheme(string Id, string Name, bool IsDark, IReadOnlyDictionary<string, uint> Colors,
    SkinGeometry Geometry, int FormatVersion);

/// <summary>A rule the file breaks. <see cref="Field"/> is the token or key path, when there is one.</summary>
public sealed record ThemeProblem(string Rule, string? Field, string Message);

/// <summary>Something that does not stop the theme loading: a readability pairing below its
/// minimum, or a typeface that is not installed.</summary>
public sealed record ThemeWarning(string Kind, string? Pairing, IReadOnlyList<string> Tokens, double Measured, double Minimum, string Message)
{
    public const string Readability = "readability", FontMissing = "font-missing";
}

public sealed record ThemeValidation(IReadOnlyList<ThemeProblem> Problems, IReadOnlyList<ThemeWarning> Warnings, ParsedTheme? Theme)
{
    public bool IsValid => Problems.Count == 0 && Theme is not null;
}

public static class ThemeFile
{
    public const int MaxNameLength = 40;
    /// <summary>A theme file larger than this is not read (the folder loader's guard).</summary>
    public const int MaxFileBytes = 256 * 1024;

    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HexPattern = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    /// <summary>Check <paramref name="json"/> against the theme-file rules. <paramref name="catalogue"/>
    /// is every colour token a theme must define, in file order; <paramref name="translucent"/> the
    /// tokens that may carry alpha. <paramref name="fontInstalled"/>, when given, turns a typeface
    /// that is not installed into a warning.</summary>
    public static ThemeValidation Validate(string json, IReadOnlyList<string> catalogue, IReadOnlySet<string> translucent,
        Func<string, bool>? fontInstalled = null)
    {
        var problems = new List<ThemeProblem>();
        var warnings = new List<ThemeWarning>();
        void P(string rule, string? field, string message) => problems.Add(new(rule, field, message));

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException e)
        {
            P("JSON", null, Loc.F("themes.problem.not_json", e.Message));
            return new(problems, warnings, null);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                P("JSON", null, Loc.T("themes.problem.not_object"));
                return new(problems, warnings, null);
            }

            // F1′ — format version 1 (colours) or 2 (colours + typeface + radius).
            int version = 0;
            if (!root.TryGetProperty("formatVersion", out var fv) || fv.ValueKind != JsonValueKind.Number
                || !fv.TryGetInt32(out version) || version is not (1 or 2))
                P("F1", "formatVersion", Loc.T("themes.problem.format_version"));

            static string? Str(JsonElement root, string key) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            // F2 — id.
            var id = Str(root, "id") ?? "";
            if (!IdPattern.IsMatch(id) || id == "system")
                P("F2", "id", Loc.F("themes.problem.id", id));

            // F3 — name.
            var name = Str(root, "name") ?? "";
            if (name.Trim().Length < 1 || name.Length > MaxNameLength)
                P("F3", "name", Loc.F("themes.problem.name", MaxNameLength));

            // F4 — kind.
            var kindText = Str(root, "kind");
            bool isDark = kindText == "dark";
            if (kindText is not ("dark" or "light"))
                P("F4", "kind", Loc.F("themes.problem.kind", kindText ?? ""));

            // F5, F6 — the exact catalogue, valid colours, opaque but the translucent tokens.
            var colors = new Dictionary<string, uint>();
            if (!root.TryGetProperty("colors", out var colorsEl) || colorsEl.ValueKind != JsonValueKind.Object)
                P("F5", "colors", Loc.T("themes.problem.colors_missing"));
            else
            {
                var known = catalogue as IReadOnlySet<string> ?? catalogue.ToHashSet();
                foreach (var p in colorsEl.EnumerateObject())
                {
                    if (!known.Contains(p.Name)) { P("F5", $"colors.{p.Name}", Loc.F("themes.problem.unknown_token", p.Name)); continue; }
                    var hex = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
                    if (!HexPattern.IsMatch(hex)) { P("F6", $"colors.{p.Name}", Loc.F("themes.problem.bad_colour", p.Name, hex)); continue; }
                    var argb = ParseHex(hex);
                    if (argb >> 24 != 0xFF && !translucent.Contains(p.Name))
                    {
                        P("F6", $"colors.{p.Name}", Loc.F("themes.problem.opaque", p.Name));
                        continue;
                    }
                    colors[p.Name] = argb;
                }
                var missing = catalogue.Where(t => !colorsEl.TryGetProperty(t, out _)).ToList();
                foreach (var t in missing) P("F5", $"colors.{t}", Loc.F("themes.problem.missing_token", t));
            }

            // V1–V4 — the format-2 parts.
            var geometry = ReadGeometry(root, version, P);

            if (problems.Count > 0) return new(problems, warnings, null);

            var theme = new ParsedTheme(id, name, isDark, colors, geometry, version);
            warnings.AddRange(Readability(colors));
            if (geometry.FontFamily is { } family && fontInstalled is not null && !fontInstalled(family))
                warnings.Add(new(ThemeWarning.FontMissing, null, new[] { family }, 0, 0,
                    Loc.F("themes.warning.font_missing", family)));
            return new(problems, warnings, theme);
        }
    }

    private static SkinGeometry ReadGeometry(JsonElement root, int version, Action<string, string?, string> P)
    {
        bool hasFont = root.TryGetProperty("font", out var fontEl);
        bool hasRadius = root.TryGetProperty("radius", out var radiusEl);
        if ((hasFont || hasRadius) && version == 1)
        {
            if (hasFont) P("V1", "font", Loc.F("themes.problem.needs_v2", "font"));
            if (hasRadius) P("V1", "radius", Loc.F("themes.problem.needs_v2", "radius"));
            return SkinGeometry.Default;
        }

        string? family = null;
        if (hasFont)
        {
            if (fontEl.ValueKind != JsonValueKind.Object)
                P("V2", "font", Loc.T("themes.problem.font_object"));
            else
            {
                foreach (var p in fontEl.EnumerateObject())
                    if (p.Name != "family") P("V2", $"font.{p.Name}", Loc.F("themes.problem.unknown_key", $"font.{p.Name}"));
                var f = fontEl.TryGetProperty("family", out var fe) && fe.ValueKind == JsonValueKind.String ? fe.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(f) || f.Length > SkinGeometry.MaxFamilyLength)
                    P("V2", "font.family", Loc.F("themes.problem.font_family", SkinGeometry.MaxFamilyLength));
                else family = f;
            }
        }

        double scale = 1;
        var steps = new Dictionary<RadiusStep, double>();
        if (hasRadius)
        {
            if (radiusEl.ValueKind != JsonValueKind.Object)
                P("V3", "radius", Loc.T("themes.problem.radius_object"));
            else
            {
                foreach (var p in radiusEl.EnumerateObject())
                    if (p.Name is not ("scale" or "steps")) P("V3", $"radius.{p.Name}", Loc.F("themes.problem.unknown_key", $"radius.{p.Name}"));
                if (radiusEl.TryGetProperty("scale", out var se))
                {
                    if (se.ValueKind != JsonValueKind.Number || !se.TryGetDouble(out scale)
                        || scale < SkinGeometry.MinRoundness || scale > SkinGeometry.MaxRoundness || double.IsNaN(scale))
                    {
                        P("V3", "radius.scale", Loc.F("themes.problem.radius_scale", SkinGeometry.MinRoundness, SkinGeometry.MaxRoundness));
                        scale = 1;
                    }
                }
                if (radiusEl.TryGetProperty("steps", out var st))
                {
                    if (st.ValueKind != JsonValueKind.Object) P("V4", "radius.steps", Loc.T("themes.problem.radius_steps"));
                    else
                        foreach (var p in st.EnumerateObject())
                        {
                            var step = SkinGeometry.FromKey(p.Name);
                            if (step is null)
                            {
                                P("V4", $"radius.steps.{p.Name}", Loc.F("themes.problem.radius_step_unknown", p.Name,
                                    string.Join(", ", Enum.GetValues<RadiusStep>().Select(SkinGeometry.Key))));
                                continue;
                            }
                            if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetDouble(out var v)
                                || v < SkinGeometry.MinStep || v > SkinGeometry.MaxStep || double.IsNaN(v))
                            {
                                P("V4", $"radius.steps.{p.Name}", Loc.F("themes.problem.radius_step_range", p.Name, SkinGeometry.MinStep, SkinGeometry.MaxStep));
                                continue;
                            }
                            steps[step.Value] = v;
                        }
                }
            }
        }

        return family is null && scale == 1 && steps.Count == 0 ? SkinGeometry.Default : new SkinGeometry(family, scale, steps);
    }

    /// <summary>The file text for <paramref name="theme"/>: tokens in catalogue order, 2-space
    /// indent, format 1 when it only has colours.</summary>
    public static string Write(ParsedTheme theme, IReadOnlyList<string> catalogue)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            var g = theme.Geometry;
            w.WriteStartObject();
            w.WriteNumber("formatVersion", g.IsDefault ? 1 : 2);
            w.WriteString("id", theme.Id);
            w.WriteString("name", theme.Name);
            w.WriteString("kind", theme.IsDark ? "dark" : "light");
            if (g.FontFamily is { } family)
            {
                w.WriteStartObject("font");
                w.WriteString("family", family);
                w.WriteEndObject();
            }
            if (g.Roundness != 1 || g.StepOverrides.Count > 0)
            {
                w.WriteStartObject("radius");
                if (g.Roundness != 1) w.WriteNumber("scale", g.Roundness);
                if (g.StepOverrides.Count > 0)
                {
                    w.WriteStartObject("steps");
                    foreach (var step in Enum.GetValues<RadiusStep>())
                        if (g.StepOverrides.TryGetValue(step, out var v)) w.WriteNumber(SkinGeometry.Key(step), v);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteStartObject("colors");
            foreach (var token in catalogue)
                if (theme.Colors.TryGetValue(token, out var c)) w.WriteString(token, ToHex(c));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        // Utf8JsonWriter indents with two spaces; end with a newline like the built-in files.
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    /// <summary>A theme id made from <paramref name="name"/> that is not in <paramref name="taken"/>:
    /// lower-case, hyphenated, numbered on a clash ("studio-dark-copy", "studio-dark-copy-2").</summary>
    public static string FreeId(string name, Func<string, bool> taken)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0 || slug == "system") slug = "theme";
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        if (!taken(slug)) return slug;
        for (int n = 2; ; n++)
            if (!taken($"{slug}-{n}")) return $"{slug}-{n}";
    }

    public static uint ParseHex(string hex)
    {
        var h = hex.TrimStart('#');
        uint v = uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return h.Length == 6 ? 0xFF000000 | v : v;
    }

    public static string ToHex(uint argb) =>
        argb >> 24 == 0xFF ? $"#{argb & 0xFFFFFF:X6}" : $"#{argb:X8}";

    // ---- T3: readability pairings (specs/011-colour-themes/contracts/ui-contract.md) -------

    private static readonly string[] FiveSurfaces = { "BgApp", "Panel", "SurfaceRaised", "SurfaceHover", "BgSunken" };

    /// <summary>Every T3 pairing below its minimum, as warnings. Pairings whose tokens the
    /// colours lack are skipped.</summary>
    public static IEnumerable<ThemeWarning> Readability(IReadOnlyDictionary<string, uint> c)
    {
        var list = new List<(string Id, string[] Tokens, string What, double Value, double Min, bool DeltaE)>();
        bool Has(params string[] t) => t.All(c.ContainsKey);
        void Cr(string id, string a, string b, double min)
        {
            if (Has(a, b)) list.Add((id, new[] { a, b }, Loc.F("themes.warning.on", a, b), Contrast(c[a], c[b]), min, false));
        }
        void Ink(string id, string fill, double min)
        {
            if (Has(fill)) list.Add((id, new[] { fill }, Loc.F("themes.warning.text_on", fill), Contrast(ReadableInk(c[fill]), c[fill]), min, false));
        }
        void De(string id, string a, string b, double min)
        {
            if (Has(a, b)) list.Add((id, new[] { a, b }, Loc.F("themes.warning.vs", a, b), DeltaE(c[a], c[b]), min, true));
        }

        foreach (var t in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
            foreach (var s in FiveSurfaces) Cr("T3.1", t, s, 4.5);
        foreach (var s in FiveSurfaces) Cr("T3.2", "TextTertiary", s, 3.0);
        foreach (var t in new[] { "TextFaint", "TextDisabled", "TextAxis" })
            foreach (var s in FiveSurfaces) Cr("T3.3", t, s, 2.5);
        foreach (var s in new[] { "BgApp", "Panel", "BgSunken" }) Cr("T3.4", "AccentBright", s, 2.3);
        foreach (var f in new[] { "Accent", "Mute", "Solo", "Record" }) Ink("T3.5", f, 4.5);
        Cr("T3.9", "RecordInk", "Record", 4.5);
        Cr("T3.9", "SuccessInk", "Success", 4.5);
        foreach (var f in Enumerable.Range(1, 9).Select(i => $"Track{i}").Append("ReturnA").Append("ReturnB")) Ink("T3.6", f, 3.0);
        var tracks = Enumerable.Range(1, 9).Select(i => $"Track{i}").ToArray();
        for (int i = 0; i < tracks.Length; i++)
            for (int j = i + 1; j < tracks.Length; j++) De("T3.7", tracks[i], tracks[j], 12);
        var states = new[] { "Accent", "Mute", "Solo", "Record" };
        for (int i = 0; i < states.Length; i++)
            for (int j = i + 1; j < states.Length; j++) De("T3.8", states[i], states[j], 15);

        foreach (var p in list)
            if (p.Value < p.Min - 1e-9)
                yield return new(ThemeWarning.Readability, p.Id, p.Tokens, Math.Round(p.Value, 2), p.Min,
                    p.DeltaE ? Loc.F("themes.warning.distance", p.What, Math.Round(p.Value, 1), p.Min)
                             : Loc.F("themes.warning.contrast", p.What, Math.Round(p.Value, 2), p.Min));
    }

    /// <summary>NotaPalette.ReadableInkOn: dark ink or white, whichever reads better on the fill.</summary>
    public static uint ReadableInk(uint fill) =>
        Contrast(0xFF17181A, fill) >= Contrast(0xFFFFFFFF, fill) ? 0xFF17181A : 0xFFFFFFFF;

    /// <summary>WCAG 2.x contrast ratio of two colours (alpha ignored).</summary>
    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint c)
    {
        static double Ch(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch((c >> 16) & 0xFF) + 0.7152 * Ch((c >> 8) & 0xFF) + 0.0722 * Ch(c & 0xFF);
    }

    /// <summary>CIE76 colour distance.</summary>
    public static double DeltaE(uint a, uint b)
    {
        static (double L, double A, double B) Lab(uint c)
        {
            static double Lin(double v) { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
            double r = Lin((c >> 16) & 0xFF), g = Lin((c >> 8) & 0xFF), bl = Lin(c & 0xFF);
            double x = (r * .4124 + g * .3576 + bl * .1805) / .95047, y = r * .2126 + g * .7152 + bl * .0722,
                   z = (r * .0193 + g * .1192 + bl * .9505) / 1.08883;
            return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
        }
        var (l1, a1, b1) = Lab(a); var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }
}
