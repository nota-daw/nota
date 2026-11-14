// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Builds the Assistant's manual index from a nota-docs checkout (specs/098,
// contracts/manual-index.md § Builder rules): every page cut into its intro and its "##"
// sections, MDX reduced to plain Markdown, Show me anchors (<At id="…"/>) lifted out as steps,
// how-tos flagged, translations paired with their English sections by position. Tooling only —
// run through scripts/build-manual-index.sh (`--manual-index-build <docs> <out>`); the app reads
// the result with ManualIndex.Read.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Nota.Application.Assistant;

namespace Nota.SmokeTest;

internal static class ManualIndexBuilder
{
    public sealed record Result(ManualIndex Index, byte[] Json, List<string> Errors, List<string> Warnings, string Summary);

    /// <summary>Verbs that start an instruction (research R6): a section with a sentence that
    /// begins with one of them is a how-to.</summary>
    public static readonly string[] ActionVerbsEn =
        ["Click", "Double-click", "Right-click", "Press", "Hold", "Drag", "Drop", "Choose", "Select", "Open", "Turn", "Type", "Set", "Pick"];
    public static readonly string[] ActionVerbsRu =
        ["Нажмите", "Щёлкните", "Щелкните", "Дважды щёлкните", "Дважды щелкните", "Перетащите", "Выберите", "Откройте", "Включите", "Удерживайте", "Введите", "Задайте"];

    private static readonly Regex Import = new(@"^import\s.+?from\s+['""].+?['""];?\s*$", RegexOptions.Multiline);
    private static readonly Regex JsxComment = new(@"\{/\*.*?\*/\}", RegexOptions.Singleline);
    private static readonly Regex Shot = new(@"<Shot\b[^>]*?/>", RegexOptions.Singleline);
    private static readonly Regex ShotRow = new(@"<ShotRow\b.*?</ShotRow>", RegexOptions.Singleline);
    private static readonly Regex Kbd = new(@"<Kbd>(.*?)</Kbd>", RegexOptions.Singleline);
    private static readonly Regex At = new(@"<At\s+id=""([^""]*)""\s*/>\s?");
    private static readonly Regex LinkCard = new(@"<LinkCard\s+([^>]*?)/>", RegexOptions.Singleline);
    private static readonly Regex Attr = new(@"(\w+)=""([^""]*)""");
    private static readonly Regex Wrapper = new(@"^\s*</?(Steps|CardGrid|Tabs)\b[^>]*>\s*$", RegexOptions.Multiline);
    private static readonly Regex TabItem = new(@"^\s*<TabItem\s+label=""([^""]*)""[^>]*>\s*$", RegexOptions.Multiline);
    private static readonly Regex TabItemEnd = new(@"^\s*</TabItem>\s*$", RegexOptions.Multiline);
    private static readonly Regex Link = new(@"\]\(([^)\s]+)\)");
    private static readonly Regex OrderedItem = new(@"^\s*\d+\.\s", RegexOptions.Multiline);

    public static Result Build(string docsRoot, string builtFor)
    {
        string content = Path.Combine(docsRoot, "src", "content", "docs");
        if (!Directory.Exists(content)) throw new DirectoryNotFoundException(content);
        var errors = new List<string>();
        var warnings = new List<string>();
        var locales = Locales(docsRoot);

        // Pages by language: a top-level folder named for a locale in astro.config.mjs is a translation.
        var pages = new List<(string Lang, string Page, string File)>();
        foreach (var file in Directory.EnumerateFiles(content, "*.md*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".md", StringComparison.Ordinal) && !file.EndsWith(".mdx", StringComparison.Ordinal)) continue;
            var rel = Path.GetRelativePath(content, file).Replace('\\', '/');
            string lang = "en";
            var first = rel.Split('/')[0];
            if (rel.Contains('/') && locales.Contains(first)) { lang = first; rel = rel[(first.Length + 1)..]; }
            string page = Regex.Replace(rel, @"\.mdx?$", "");
            if (page == "index") page = "";
            else if (page.EndsWith("/index", StringComparison.Ordinal)) page = page[..^6];
            pages.Add((lang, page, file));
        }

        var sections = new List<ManualSection>();
        var byPage = new Dictionary<(string, string), List<ManualSection>>();
        foreach (var (lang, page, file) in pages.OrderBy(p => p.Lang == "en" ? 0 : 1).ThenBy(p => p.Lang).ThenBy(p => p.Page, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file).Replace("\r\n", "\n");
            var (front, body) = SplitFrontmatter(text);
            if (front.GetValueOrDefault("draft") == "true" || front.GetValueOrDefault("status") == "todo") continue;
            string title = front.GetValueOrDefault("title") ?? page;
            var list = new List<ManualSection>();
            foreach (var (heading, raw) in SplitSections(body))
            {
                string slug = heading.Length == 0 ? "" : Slug(heading);
                var (md, steps, explanatory, howTo, err) = Convert(raw, lang, page);
                foreach (var e in err) errors.Add($"{lang}/{page}#{slug}: {e}");
                if (heading.Length == 0 && md.Trim().Length == 0) continue;
                string id = $"{lang}/{page}" + (slug.Length > 0 ? "#" + slug : "");
                list.Add(new ManualSection(id, lang, page, title, StripInline(heading), slug, md, null, howTo, explanatory, steps));
            }
            // Duplicate slugs on a page get -1, -2 like Starlight.
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Slug.Length == 0) continue;
                int n = seen.GetValueOrDefault(s.Slug);
                seen[s.Slug] = n + 1;
                if (n > 0) list[i] = s with { Slug = s.Slug + "-" + n, Id = s.Id + "-" + n };
            }
            byPage[(lang, page)] = list;
        }

        // Twins: a translation's sections pair with the English page's by position.
        foreach (var ((lang, page), list) in byPage)
        {
            if (lang != "en")
            {
                if (!byPage.TryGetValue(("en", page), out var en))
                {
                    errors.Add($"{lang}/{page}: no English page");
                    continue;
                }
                bool enIntro = en.Count > 0 && en[0].Slug.Length == 0, intro = list.Count > 0 && list[0].Slug.Length == 0;
                var enBody = en.Where(s => s.Slug.Length > 0).ToList();
                var body = list.Where(s => s.Slug.Length > 0).ToList();
                if (enBody.Count != body.Count)
                {
                    // A translation that fell behind the English page: left out until it catches up
                    // (questions in that language are answered from its other pages).
                    warnings.Add($"{lang}/{page}: {body.Count} sections, the English page has {enBody.Count} — left out");
                    continue;
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    ManualSection? twin = s.Slug.Length == 0 ? (enIntro ? en[0] : null) : enBody[body.IndexOf(s)];
                    if (twin is null) { errors.Add($"{s.Id}: an intro the English page doesn't have"); continue; }
                    // A translation is a how-to exactly when its English section is: the anchors must match.
                    sections.Add(s with { Twin = twin.Id, HowTo = twin.HowTo });
                }
                _ = intro;
            }
            else sections.AddRange(list);
        }

        var langs = sections.Select(s => s.Lang).Distinct().OrderBy(l => l == "en" ? "" : l).ToList();
        string commit = Git(docsRoot, "rev-parse --short HEAD");
        var json = ManualIndex.Write(ManualIndex.CurrentFormat, commit, builtFor, langs, sections);
        var index = ManualIndex.Parse(json);
        if (!index.IsAvailable) errors.Add("index does not read back: " + index.Unavailable);

        var sb = new StringBuilder();
        sb.Append($"manual index: docs {commit}, {pages.Count} pages");
        foreach (var l in langs)
        {
            var ls = sections.Where(s => s.Lang == l).ToList();
            sb.Append($"; {l}: {ls.Count} sections, {ls.Count(s => s.HowTo)} how-to, {ls.Count(s => s.Steps.Count > 0)} anchored, {ls.Count(s => s.Explanatory)} explanatory");
        }
        return new Result(index, json, errors, warnings, sb.ToString());
    }

    /// <summary>The app version (VERSION, stamped on every assembly by Directory.Build.props).</summary>
    public static string AppVersion() => typeof(ManualIndex).Assembly.GetName().Version?.ToString(3) ?? "";

    public static void WriteGz(byte[] json, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        // mtime is not written by GZipStream, so the output is reproducible.
        using var gz = new GZipStream(fs, CompressionLevel.SmallestSize);
        gz.Write(json);
    }

    // The translation folders: Starlight's locales in astro.config.mjs ("root" is English).
    private static HashSet<string> Locales(string docsRoot)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var cfg = Path.Combine(docsRoot, "astro.config.mjs");
        if (!File.Exists(cfg)) return set;
        var text = File.ReadAllText(cfg);
        int at = text.IndexOf("locales:", StringComparison.Ordinal);
        if (at < 0) return set;
        int close = text.IndexOf('}', text.IndexOf('{', at) + 1);
        // Each entry closes its own braces; read up to the block's end.
        int depth = 0, end = at;
        for (int i = text.IndexOf('{', at); i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) { end = i; break; }
        }
        _ = close;
        foreach (Match m in Regex.Matches(text[at..end], @"(\w+)\s*:\s*\{\s*label"))
            if (m.Groups[1].Value != "root") set.Add(m.Groups[1].Value);
        return set;
    }

    // ---- page parts ----------------------------------------------------------------------

    private static (Dictionary<string, string> Front, string Body) SplitFrontmatter(string text)
    {
        var front = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (front, text);
        int end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (front, text);
        foreach (var line in text[4..end].Split('\n'))
        {
            if (line.StartsWith(' ') || line.StartsWith('-')) continue;   // nested YAML: not needed
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string v = line[(colon + 1)..].Trim();
            if (v.Length >= 2 && (v[0] == '"' && v[^1] == '"' || v[0] == '\'' && v[^1] == '\'')) v = v[1..^1];
            front[line[..colon].Trim()] = v;
        }
        int bodyStart = text.IndexOf('\n', end + 4);
        return (front, bodyStart < 0 ? "" : text[(bodyStart + 1)..]);
    }

    // The intro (heading "") and each "##" section, fences respected.
    private static List<(string Heading, string Body)> SplitSections(string body)
    {
        var result = new List<(string, string)>();
        string heading = "";
        var cur = new StringBuilder();
        bool fence = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fence = !fence;
            if (!fence && line.StartsWith("## ", StringComparison.Ordinal))
            {
                result.Add((heading, cur.ToString()));
                heading = line[3..].Trim();
                cur.Clear();
                continue;
            }
            cur.Append(line).Append('\n');
        }
        result.Add((heading, cur.ToString()));
        return result;
    }

    /// <summary>github-slugger's rule, which Starlight uses for heading ids.</summary>
    public static string Slug(string heading)
    {
        var sb = new StringBuilder();
        foreach (char c in StripInline(heading).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        }
        return sb.ToString();
    }

    private static string StripInline(string s) =>
        Regex.Replace(Kbd.Replace(s, "$1"), @"[*_`]|<[^>]+>", "").Trim();

    // ---- MDX → Markdown -----------------------------------------------------------------

    private static (string Md, List<AnchoredStep> Steps, bool Explanatory, bool HowTo, List<string> Errors)
        Convert(string raw, string lang, string page)
    {
        var errors = new List<string>();
        // How-to is judged on the source: <Steps>, an ordered list or an action verb.
        bool howTo = raw.Contains("<Steps>") || OrderedItem.IsMatch(StripFences(raw)) || StartsWithVerb(raw, lang);

        string s = Import.Replace(raw, "");
        s = JsxComment.Replace(s, "");
        s = ShotRow.Replace(s, "");
        s = Shot.Replace(s, "");
        s = Kbd.Replace(s, m => "<kbd>" + m.Groups[1].Value.Trim() + "</kbd>");
        s = LinkCard.Replace(s, m =>
        {
            var a = Attr.Matches(m.Groups[1].Value).ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);
            string line = $"- [{a.GetValueOrDefault("title")}]({a.GetValueOrDefault("href")})";
            if (a.GetValueOrDefault("description") is { Length: > 0 } d) line += " — " + d;
            return line;
        });
        s = TabItem.Replace(s, m => "**" + m.Groups[1].Value + "**\n");
        s = TabItemEnd.Replace(s, "");
        s = Wrapper.Replace(s, "");
        s = Asides(s, errors);
        s = Link.Replace(s, m => "](" + RewriteLink(m.Groups[1].Value, lang, page) + ")");

        // Anchors: lift out with the step text around them.
        var steps = new List<AnchoredStep>();
        bool explanatory = false;
        var lines = s.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            foreach (Match m in At.Matches(lines[i]))
            {
                string id = m.Groups[1].Value;
                if (id == "none") { explanatory = true; continue; }
                steps.Add(new AnchoredStep(id, At.Replace(StepText(lines, i, m.Index), "").Trim()));
            }
        }
        if (explanatory && steps.Count > 0) errors.Add("<At id=\"none\"/> together with anchors");
        s = At.Replace(s, "");
        s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
        return (s, steps, explanatory, howTo, errors);
    }

    private static string StripFences(string s) => Regex.Replace(s, "```.*?```", "", RegexOptions.Singleline);

    private static bool StartsWithVerb(string raw, string lang)
    {
        var verbs = lang == "ru" ? ActionVerbsRu : ActionVerbsEn;
        string text = StripFences(raw);
        // Sentence starts: a line start (after list / quote markers) or after ". ".
        foreach (Match m in Regex.Matches(text, @"(?:^|\n)\s*(?:[-*]\s+|\d+\.\s+|>\s*)?(?:<At[^>]*/>\s*)?\**([\p{L}-]+(?: [\p{L}-]+)?)|[.!?]\s+\**([\p{L}-]+(?: [\p{L}-]+)?)"))
        {
            string w = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            foreach (var v in verbs)
                if (w.StartsWith(v, StringComparison.Ordinal) && (w.Length == v.Length || w[v.Length] == ' ')) return true;
        }
        return false;
    }

    private static string Asides(string s, List<string> errors)
    {
        var rx = new Regex(@"<Aside(?<attrs>[^>]*)>(?<body>.*?)</Aside>", RegexOptions.Singleline);
        return rx.Replace(s, m =>
        {
            var a = Attr.Matches(m.Groups["attrs"].Value).ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);
            string type = (a.GetValueOrDefault("type") ?? "note").ToUpperInvariant();
            if (type is not ("NOTE" or "TIP" or "CAUTION" or "DANGER")) { errors.Add("unknown Aside type " + type); type = "NOTE"; }
            var sb = new StringBuilder();
            sb.Append("> [!").Append(type).Append("]\n");
            if (a.GetValueOrDefault("title") is { Length: > 0 } t) sb.Append("> **").Append(t).Append("**\n>\n");
            foreach (var line in m.Groups["body"].Value.Trim('\n').Split('\n'))
                sb.Append('>').Append(line.Length > 0 ? " " + line.TrimStart() : "").Append('\n');
            return sb.ToString().TrimEnd('\n');
        });
    }

    /// <summary>A link as the panel reads it: manual pages become nota-manual:&lt;lang&gt;/&lt;page&gt;#…,
    /// web links stay.</summary>
    public static string RewriteLink(string href, string lang, string page)
    {
        if (href.StartsWith("http:", StringComparison.Ordinal) || href.StartsWith("https:", StringComparison.Ordinal)
            || href.StartsWith("mailto:", StringComparison.Ordinal)) return href;
        string frag = "";
        int hash = href.IndexOf('#');
        if (hash >= 0) { frag = href[hash..]; href = href[..hash]; }
        string target;
        if (href.Length == 0) target = page;
        else if (href.StartsWith('/'))
        {
            target = href.Trim('/');
            if (target.StartsWith("nota-docs/", StringComparison.Ordinal)) target = target[10..];
            if (target.Length >= 2 && target[..2].All(char.IsAsciiLetterLower) && (target.Length == 2 || target[2] == '/'))
            {
                lang = target[..2];
                target = target.Length > 3 ? target[3..] : "";
            }
        }
        else
        {
            // Relative to the page's URL, which ends in a slash (Starlight's directory URLs).
            var parts = (page.Length > 0 ? page.Split('/').ToList() : new List<string>());
            foreach (var seg in href.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg == ".") continue;
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else parts.Add(seg);
            }
            target = string.Join('/', parts);
        }
        return $"nota-manual:{lang}/{target.Trim('/')}{frag}";
    }

    // The list item (with its continuation lines) or, in prose, the sentence holding an anchor.
    private static string StepText(List<string> lines, int i, int col)
    {
        var item = Regex.Match(lines[i], @"^(\s*)([-*]|\d+\.)\s+");
        if (item.Success)
        {
            var sb = new StringBuilder(lines[i][item.Length..]);
            int indent = item.Groups[1].Value.Length;
            for (int j = i + 1; j < lines.Count; j++)
            {
                var l = lines[j];
                if (l.Trim().Length == 0 || Regex.IsMatch(l, @"^\s*([-*]|\d+\.)\s") || l.Length - l.TrimStart().Length <= indent) break;
                sb.Append(' ').Append(l.Trim());
            }
            return sb.ToString();
        }
        // Prose: the paragraph around line i, then the sentence that holds the anchor.
        int a = i, b = i;
        while (a > 0 && lines[a - 1].Trim().Length > 0) a--;
        while (b + 1 < lines.Count && lines[b + 1].Trim().Length > 0) b++;
        int offset = 0;
        for (int k = a; k < i; k++) offset += lines[k].Length + 1;
        string para = string.Join("\n", lines.Skip(a).Take(b - a + 1));
        int pos = offset + col;
        int start = 0;
        foreach (Match m in Regex.Matches(para, @"[.!?](\s+)"))
            if (m.Index + m.Length <= pos) start = m.Index + m.Length;
        var end = Regex.Match(para[pos..], @"[.!?](\s|$)");
        int stop = end.Success ? pos + end.Index + 1 : para.Length;
        return para[start..stop].Replace('\n', ' ');
    }

    private static string Git(string dir, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("git", $"-C \"{dir}\" {args}") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            string o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 ? o : "unknown";
        }
        catch { return "unknown"; }
    }
}
