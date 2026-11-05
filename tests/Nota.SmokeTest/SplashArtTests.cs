// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The splash's per-release art (specs/074): the composer is deterministic, neighbouring
// versions differ, every scene keeps the colour rules (track palette, one live accent,
// neutral overlays), composing is cheap, and odd version strings never throw. Plus the
// review sheet (`--splash-sheet`): many versions as SVGs on one HTML page, resolved
// against a theme file, so the art can be judged without building each release.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Nota.App;

namespace Nota.SmokeTest;

internal static class SplashArtTests
{
    private static readonly string[] Range = Enumerable.Range(60, 20)
        .SelectMany(minor => Enumerable.Range(0, 10).Select(p => $"0.{minor}.{p}")).ToArray();

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // Seed and PRNG match the reviewed prototype (prototype.html: seedOf / rng).
        var rng = new SplashArtComposer.Mulberry32(SplashArtComposer.Hash("0.58.1"));
        double r1 = rng.Next(), r2 = rng.Next(), r3 = rng.Next();
        yield return (SplashArtComposer.Hash("0.58.1") == 3467694582u && SplashArtComposer.Hash("0.58") == 3356754079u
                      && SplashArtComposer.Hash("") == 535972289u
                      && Math.Abs(r1 - 0.03265987150371075) < 1e-12 && Math.Abs(r2 - 0.4006672299001366) < 1e-12
                      && Math.Abs(r3 - 0.8660609039943665) < 1e-12,
            "seed hash and mulberry32 match the reviewed prototype");

        // SC-001: the same version always gives the same scene.
        var sample = Range.Where((_, i) => i % 4 == 0).ToArray();
        var unstable = sample.Where(v => !SplashArtComposer.Compose(v).Equals(SplashArtComposer.Compose(v))).ToArray();
        yield return (unstable.Length == 0, unstable.Length == 0
            ? $"same version → same scene ({sample.Length} versions)"
            : $"scene differs between two composes: {string.Join(", ", unstable.Take(5))}");

        // SC-002: consecutive patches change family; all 200 scenes distinct and non-empty.
        var scenes = Range.Select(SplashArtComposer.Compose).ToArray();
        var sameFamily = new List<string>();
        for (int i = 1; i < scenes.Length; i++)
            if (i % 10 != 0 && scenes[i].Family == scenes[i - 1].Family) sameFamily.Add(scenes[i].Version);
        yield return (sameFamily.Count == 0, sameFamily.Count == 0
            ? "consecutive patches change motif family (0.60.0–0.79.9)"
            : $"same family as the previous patch: {string.Join(", ", sameFamily.Take(5))}");
        int distinct = scenes.Select(s => s.Items).Distinct().Count();
        yield return (distinct == scenes.Length && scenes.All(s => s.Items.Count > 0),
            $"{distinct}/{scenes.Length} scenes distinct, none empty");
        var families = scenes.Select(s => s.Family).Distinct().Count();
        yield return (families == 6, $"all six motif families occur ({families})");

        // Geometry stays near the card. Motifs may run past the art region on purpose (outer
        // rhythm rings, the last notes of a roll); the renderer clips them. A point more than
        // a card's size away means a broken formula, not a composition.
        static IEnumerable<SPoint> Points(Prim p) => p switch
        {
            RectPrim r => new[] { new SPoint(r.R.X, r.R.Y), new SPoint(r.R.X + r.R.W, r.R.Y + r.R.H) },
            LinePrim l => new[] { l.A, l.B },
            PolylinePrim pl => pl.Points,
            PolygonPrim pg => pg.Points,
            CirclePrim c => new[] { c.C },
            _ => Array.Empty<SPoint>(),
        };
        var stray = scenes.Where(s => s.Items.SelectMany(Points).Any(p =>
            !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < -SplashScene.Width || p.X > 2 * SplashScene.Width || p.Y < -SplashScene.Height || p.Y > 2 * SplashScene.Height))
            .Select(s => s.Version).ToArray();
        yield return (stray.Length == 0, stray.Length == 0 ? "every coordinate is finite and near the card"
            : $"coordinates far off the card: {string.Join(", ", stray.Take(5))}");

        // Edge versions: never throw, always a valid scene; a pre-release keeps its release's family.
        var edges = new[] { "", "0.0.0", "abc", "0.71.0-beta.1", "1.2.3+deadbeef", "  0.58.1  ", "99999999999.1.2" };
        var broken = new List<string>();
        foreach (var v in edges)
        {
            try { if (SplashArtComposer.Compose(v).Items.Count == 0) broken.Add($"'{v}' empty"); }
            catch (Exception e) { broken.Add($"'{v}' threw {e.GetType().Name}"); }
        }
        yield return (broken.Count == 0, broken.Count == 0 ? "odd version strings give valid scenes" : string.Join("; ", broken));
        yield return (SplashArtComposer.Compose("0.71.0-beta.1").Family == SplashArtComposer.Compose("0.71.0").Family,
            "a pre-release keeps its release's motif family");

        // FR-006: track palette by mode, at most one live accent mark, overlays neutral; FR-004 weights.
        var colourIssues = new List<string>();
        foreach (var s in scenes)
        {
            int want = s.PaletteMode switch { SplashPaletteMode.Mono => 1, SplashPaletteMode.Pair => 2, _ => 3 };
            if (s.Palette.Count != want || s.Palette.Any(i => i is < 0 or > 8)) colourIssues.Add($"{s.Version} palette");
            if (!new[] { 1.5, 2, 2.5, 3 }.Contains(s.StrokeWeight)) colourIssues.Add($"{s.Version} weight {s.StrokeWeight}");
            int accents = s.Items.Count(p => p.Paints.SelectMany(x => x.Colors).Any(c => c.Role == SplashRole.Accent));
            if (accents > 2) colourIssues.Add($"{s.Version} {accents} accent items");
            if (s.Items.Skip(s.OverlayStart).Any(p => p.Paints.SelectMany(x => x.Colors).Any(c => !c.IsNeutral)))
                colourIssues.Add($"{s.Version} coloured overlay");
            if (s.Items.Any(p => p.Paints.SelectMany(x => x.Colors).Any(c => c.Role == SplashRole.Track && !s.Palette.Contains(c.TrackIndex))))
                colourIssues.Add($"{s.Version} track colour outside its palette");
        }
        yield return (colourIssues.Count == 0, colourIssues.Count == 0
            ? "colour: version palette, ≤ 1 live accent mark, neutral overlays, weights 1.5–3"
            : $"colour rules broken: {string.Join(", ", colourIssues.Take(6))}");

        // FR-006: how much is coloured varies with the version. In a roll, the share of notes
        // (the 2-radius rects) in palette colours follows the version's colour share.
        var rolls = scenes.Where(s => s.Family == SplashFamily.Roll).Select(s =>
        {
            var notes = s.Items.Take(s.OverlayStart).OfType<RectPrim>().Where(r => r.Radius == 2).ToArray();
            double lit = notes.Count(n => n.Fill?.Colors.Any(c => c.Role == SplashRole.Track) == true);
            return (Share: s.ColourShare, Lit: notes.Length == 0 ? s.ColourShare : lit / notes.Length);
        }).ToArray();
        double meanError = rolls.Average(x => Math.Abs(x.Lit - x.Share));
        double spread = rolls.Max(x => x.Lit) - rolls.Min(x => x.Lit);
        yield return (rolls.Length > 10 && meanError < 0.15 && spread > 0.3,
            $"colour share varies by version: coloured notes follow it (mean error {meanError.ToString("0.00", CultureInfo.InvariantCulture)}, spread {spread.ToString("0.00", CultureInfo.InvariantCulture)}, {rolls.Length} rolls)");

        // One role → token table: every name it gives exists in every theme file.
        var root = RepoRoot();
        var missingTokens = new List<string>();
        if (root is not null)
        {
            var refs = Enum.GetValues<SplashRole>().Where(r => r != SplashRole.Track).Select(r => new ColorRef(r))
                .Concat(Enumerable.Range(0, 9).Select(i => new ColorRef(SplashRole.Track, i))).ToArray();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "src/managed/Nota.App/Theme/Themes"), "*.json"))
            {
                var colors = JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("colors");
                missingTokens.AddRange(refs.Select(SplashTokens.Name).Where(n => !colors.TryGetProperty(n, out _))
                    .Select(n => $"{Path.GetFileNameWithoutExtension(f)}:{n}"));
            }
        }
        yield return (root is not null && missingTokens.Count == 0, missingTokens.Count == 0
            ? "every splash colour role names a token every theme defines"
            : $"tokens missing: {string.Join(", ", missingTokens.Take(6))}");

        // SC-005 budget: composing is cheap enough to do synchronously before the first paint.
        for (int i = 0; i < 5; i++) SplashArtComposer.Compose($"0.1.{i}");   // JIT warm-up
        var times = Range.Take(20).Select(v =>
        {
            var sw = Stopwatch.StartNew();
            SplashArtComposer.Compose(v);
            return sw.Elapsed.TotalMilliseconds;
        }).OrderBy(t => t).ToArray();
        double median = times[times.Length / 2];
        yield return (median < 10, $"compose median {median.ToString("0.00", CultureInfo.InvariantCulture)} ms (< 10 ms)");
    }

    // ---- review sheet -------------------------------------------------------------------

    /// <summary>Writes an HTML page of the art for <paramref name="count"/> versions starting
    /// at <paramref name="from"/> (the next 4 patches, then successive minors), resolved
    /// against Theme/Themes/<paramref name="themeId"/>.json. Returns an error or null.</summary>
    public static string? WriteSheet(string outPath, string themeId, string? from, int count)
    {
        var root = RepoRoot();
        if (root is null) return "repo root not found";
        var themeFile = Path.Combine(root, "src/managed/Nota.App/Theme/Themes", themeId + ".json");
        if (!File.Exists(themeFile)) return $"no theme '{themeId}' in Theme/Themes";
        var colours = JsonDocument.Parse(File.ReadAllText(themeFile)).RootElement.GetProperty("colors")
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "#000000");
        from ??= File.ReadAllText(Path.Combine(root, "VERSION")).Trim();

        var versions = new List<string> { from };
        var m = System.Text.RegularExpressions.Regex.Match(from, @"^(\d+)\.(\d+)\.(\d+)");
        if (m.Success)
        {
            int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value), patch = int.Parse(m.Groups[3].Value);
            for (int i = 1; i <= 4 && versions.Count < count; i++) versions.Add($"{major}.{minor}.{patch + i}");
            for (int i = 1; versions.Count < count; i++) versions.Add($"{major}.{minor + i}.0");
        }

        var page = new StringBuilder();
        page.Append("<!doctype html><meta charset=\"utf-8\"><title>Nota splash art — ").Append(themeId).Append("</title>")
            .Append("<style>body{margin:0;padding:24px;background:#111214;font:12px ui-monospace,Menlo,monospace;color:#92969D}")
            .Append(".g{display:grid;grid-template-columns:repeat(4,420px);gap:18px}svg{display:block}</style><div class=\"g\">");
        int uid = 0;
        foreach (var v in versions)
        {
            var s = SplashArtComposer.Compose(v);
            page.Append("<div>").Append(Svg(s, colours, ref uid))
                .Append($"<div>{v} · {s.Family}{(s.Overlay is { } o ? "+" + o : "")} · {s.PaletteMode} · {s.Layout}</div></div>");
        }
        page.Append("</div>");
        try { File.WriteAllText(outPath, page.ToString()); }
        catch (Exception e) { return $"cannot write {outPath}: {e.Message}"; }
        return null;
    }

    private static string Svg(SplashScene s, IReadOnlyDictionary<string, string> theme, ref int uid)
    {
        var defs = new StringBuilder();
        var body = new StringBuilder();
        string id = "s" + uid++;
        int g = 0;
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        (byte, byte, byte) Hex(string token)
        {
            var h = theme.TryGetValue(token, out var x) ? x.TrimStart('#') : "000000";
            return (Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
        }
        string Col(ColorRef c, out double alpha)
        {
            var (a, r, gg, b) = SplashTokens.Shade(c, Hex(SplashTokens.Name(c)), Hex("BgSunken"));
            alpha = a / 255.0;
            return $"#{r:X2}{gg:X2}{b:X2}";
        }
        string Fill(Paint? p)
        {
            switch (p)
            {
                case null: return "none";
                case SolidPaint sp:
                {
                    var hex = Col(sp.Color, out var a);
                    return a < 1 ? $"{hex}\" fill-opacity=\"{N(a)}" : hex;
                }
                case LinearPaint lp:
                {
                    var gid = $"{id}g{g++}";
                    defs.Append($"<linearGradient id=\"{gid}\" x1=\"{N(lp.Start.X)}\" y1=\"{N(lp.Start.Y)}\" x2=\"{N(lp.End.X)}\" y2=\"{N(lp.End.Y)}\"")
                        .Append(lp.Space == PaintSpace.Art ? " gradientUnits=\"userSpaceOnUse\">" : ">");
                    foreach (var st in lp.Stops) defs.Append($"<stop offset=\"{N(st.Offset)}\" stop-color=\"{Col(st.Color, out var a)}\" stop-opacity=\"{N(a)}\"/>");
                    defs.Append("</linearGradient>");
                    return $"url(#{gid})";
                }
                case RadialPaint rp:
                {
                    var gid = $"{id}g{g++}";
                    defs.Append($"<radialGradient id=\"{gid}\" cx=\"{N(rp.Center.X)}\" cy=\"{N(rp.Center.Y)}\" r=\"{N(rp.Radius)}\"")
                        .Append(rp.Space == PaintSpace.Art ? " gradientUnits=\"userSpaceOnUse\">" : ">");
                    foreach (var st in rp.Stops) defs.Append($"<stop offset=\"{N(st.Offset)}\" stop-color=\"{Col(st.Color, out var a)}\" stop-opacity=\"{N(a)}\"/>");
                    defs.Append("</radialGradient>");
                    return $"url(#{gid})";
                }
                default: return "none";
            }
        }
        // A solid paint with alpha carries its own opacity attribute; as a stroke it must name stroke-opacity.
        string Strk(Paint? p) => Fill(p).Replace("fill-opacity", "stroke-opacity");
        string Pts(IEnumerable<SPoint> pts) => string.Join(" ", pts.Select(q => $"{N(q.X)},{N(q.Y)}"));

        foreach (var p in s.Items)
        {
            switch (p)
            {
                case RectPrim r:
                    body.Append($"<rect x=\"{N(r.R.X)}\" y=\"{N(r.R.Y)}\" width=\"{N(r.R.W)}\" height=\"{N(r.R.H)}\" rx=\"{N(r.Radius)}\" fill=\"{Fill(r.Fill)}\" stroke=\"{Strk(r.Stroke)}\" stroke-width=\"{N(r.StrokeWidth)}\"/>");
                    break;
                case LinePrim l:
                    body.Append($"<line x1=\"{N(l.A.X)}\" y1=\"{N(l.A.Y)}\" x2=\"{N(l.B.X)}\" y2=\"{N(l.B.Y)}\" stroke=\"{Strk(l.Stroke)}\" stroke-width=\"{N(l.StrokeWidth)}\" stroke-linecap=\"round\"/>");
                    break;
                case PolylinePrim pl:
                    body.Append($"<polyline points=\"{Pts(pl.Points)}\" fill=\"{Fill(pl.Fill)}\" stroke=\"{Strk(pl.Stroke)}\" stroke-width=\"{N(pl.StrokeWidth)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
                    break;
                case PolygonPrim pg:
                    body.Append($"<polygon points=\"{Pts(pg.Points)}\" fill=\"{Fill(pg.Fill)}\"/>");
                    break;
                case CirclePrim c:
                    body.Append($"<circle cx=\"{N(c.C.X)}\" cy=\"{N(c.C.Y)}\" r=\"{N(c.R)}\" fill=\"{Fill(c.Fill)}\" stroke=\"{Strk(c.Stroke)}\" stroke-width=\"{N(c.StrokeWidth)}\"/>");
                    break;
            }
        }

        var a = s.ArtRect;
        var ground = Col(new ColorRef(SplashRole.Ground), out _);
        var seam = Col(new ColorRef(SplashRole.Seam), out _);
        string seamLine = s.Layout switch
        {
            SplashLayout.TextLeft => $"<line x1=\"{N(a.X + 0.5)}\" y1=\"0\" x2=\"{N(a.X + 0.5)}\" y2=\"300\" stroke=\"{seam}\"/>",
            SplashLayout.TextRight => $"<line x1=\"{N(a.X + a.W - 0.5)}\" y1=\"0\" x2=\"{N(a.X + a.W - 0.5)}\" y2=\"300\" stroke=\"{seam}\"/>",
            _ => $"<line x1=\"0\" y1=\"{N(a.Y + a.H - 0.5)}\" x2=\"420\" y2=\"{N(a.Y + a.H - 0.5)}\" stroke=\"{seam}\"/>",
        };
        return $"<svg width=\"420\" height=\"300\" viewBox=\"0 0 420 300\" xmlns=\"http://www.w3.org/2000/svg\"><defs>{defs}"
             + $"<clipPath id=\"{id}a\"><rect x=\"{N(a.X)}\" y=\"{N(a.Y)}\" width=\"{N(a.W)}\" height=\"{N(a.H)}\"/></clipPath>"
             + $"<clipPath id=\"{id}k\"><rect width=\"420\" height=\"300\" rx=\"3\"/></clipPath></defs>"
             + $"<g clip-path=\"url(#{id}k)\"><rect width=\"420\" height=\"300\" fill=\"{ground}\"/><g clip-path=\"url(#{id}a)\">{body}</g>{seamLine}"
             + $"<rect x=\".5\" y=\".5\" width=\"419\" height=\"299\" rx=\"3\" fill=\"none\" stroke=\"{seam}\"/></g></svg>";
    }

    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        return d?.FullName;
    }
}
