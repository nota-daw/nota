// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Labels are sentence case (DESIGN.md § Labels and copy), but nothing held that
// rule: caps literals and Title Case crept back one view at a time. This check reads the
// label literals as text — the strings a view hands to Text/Content/Header/Title, a tooltip,
// a command title, a palette or Settings heading, or a local label builder — and fails on
//
//   - ALL CAPS: two or more letters, no lower case, not made only of NotaText.Fixed
//     acronyms, and not on its way through NotaText.Label (which sentence-cases it);
//   - Title Case: capitalised words after the first that are not an acronym, a view or
//     product name, or part of a "Nota …" device name — and that outnumber the literal's
//     lower-case words. A tooltip sentence may name a control ("Shift for fine steps");
//     a label whose words are mostly capitals is Title Case.
//
// Interpolated strings are skipped: their text is built at run time.
//
// It started as a ratchet (spec 012 T006): LabelAllowList.txt lists every offender found on
// the day it landed, by path and exact literal, so later changes can only remove lines.
// When spec 012 is done the list holds only data — preset and parameter names, proper nouns.
// See specs/012-ui-polish/data-model.md § Label rules.

using System.Net;
using System.Text.RegularExpressions;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    // Capitalised mid-label because they name a thing, not because of Title Case: the views
    // and panels, the product and its themes, plug-in formats, keys.
    private static readonly HashSet<string> ProperNouns = new(StringComparer.Ordinal)
    {
        "Arrangement", "Session", "Modular", "Mixer", "Finder", "Nota", "Studio", "Ember",
        "Graphite", "Paper", "MIDI", "VST3", "AU", "CLAP",
        // Keys, named as the keyboard and Settings → Shortcuts spell them: "Play (Space)".
        "Space", "Return", "Enter", "Esc", "Tab", "Delete", "Shift", "Ctrl", "Alt",
    };

    // Local helpers that build a label from their first argument.
    private const string Builders = "Mono|SectionHead|Field|Caps|DrumCap|Header|SectionLabel";

    // C# label contexts. Group lit is the literal with its quotes; b names the builder.
    private static readonly Regex[] CsContexts =
    {
        new(@"\b(?:Text|Content|Header|Title)\s*=(?!=)\s*(?<lit>""(?:[^""\\\n]|\\.)*"")"),
        new(@"\b(?:Text|Content|Header|Title)\s*=(?!=)[^;,""{}]*?\?\s*(?<lit>""(?:[^""\\\n]|\\.)*"")\s*:\s*(?<lit>""(?:[^""\\\n]|\\.)*"")"),
        new(@"\bToolTip\.SetTip\([^,()""]+,\s*(?<lit>""(?:[^""\\\n]|\\.)*"")"),
        new($@"(?<![\w.]|new\s)(?<b>{Builders})\(\s*(?<lit>""(?:[^""\\\n]|\\.)*"")"),
        // A heading table row, ("DEVICES", new[] { … }) — the Settings sidebar and shortcut groups.
        new(@"^\s*\(\s*(?<lit>""(?:[^""\\\n]|\\.)*"")\s*,\s*new\s*(?:\w+\s*)?\[\]"),
    };
    // Command titles: C("file.new", "New Project", "File", …).
    private static readonly Regex CatalogTitle = new(@"\bC\(\s*(?:""[^""]*""|\w+)\s*,\s*(?<lit>""(?:[^""\\\n]|\\.)*"")");
    // Palette group headings: new PaletteRow("RECENT", …) and the GroupLabel switch arms.
    private static readonly Regex[] PaletteHeadings =
    {
        new(@"\bnew PaletteRow\(\s*(?<lit>""(?:[^""\\\n]|\\.)*"")"),
        new(@"(?:\b\w+\.\w+|\b_)\s*=>\s*(?<lit>""(?:[^""\\\n]|\\.)*"")"),
    };
    private static readonly Regex XamlContext = new(@"(?<![\w.:])(?:Header|Text|Content|Title|ToolTip\.Tip)=""(?<v>[^""{][^""]*)""");

    public static IEnumerable<(bool Ok, string Label)> RunLabels()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var managed = Path.Combine(root, "src/managed");
        var app = Path.Combine(managed, "Nota.App");

        var fixedMap = FixedMap(File.ReadAllText(Path.Combine(app, "Theme/NotaText.cs")));
        yield return (fixedMap.Count > 0, $"NotaText.Fixed parsed ({fixedMap.Count} acronyms and units)");

        var files = SourceFiles(app, ".cs", ".axaml")
            .Concat(SourceFiles(Path.Combine(managed, "Nota.Application/Palette"), ".cs")).ToList();
        var defs = files.Where(f => f.EndsWith(".cs")).ToDictionary(f => f, f => BuilderDefs(File.ReadAllText(f)));

        var caps = new List<Hit>();
        var title = new List<Hit>();
        foreach (var f in files)
        {
            var rel = RelManaged(root, f);
            foreach (var (line, key, text, viaLabel) in LabelLiterals(f, rel, defs))
            {
                if (Shouts(text, fixedMap)) { if (!viaLabel) caps.Add(new Hit(rel, line, key)); }
                else if (TitleCaseWord(text, fixedMap) is not null) title.Add(new Hit(rel, line, key));
            }
        }

        var list = ReadRatchet(Path.Combine(root, RatchetDir, "LabelAllowList.txt"));
        var allowed = list.ToHashSet();
        var used = new HashSet<(string, string)>();
        yield return RatchetVerdict("ALL CAPS label", caps, allowed, used);
        yield return RatchetVerdict("Title Case label", title, allowed, used);
        yield return RatchetStale("label allow-list", list, used);
        yield return (true, $"label allow-list: {allowed.Count} entries (ratchet: may only shrink)");
    }

    /// <summary>NotaText.Fixed as KEY → Value, read from the source like KeyMap is.</summary>
    private static Dictionary<string, string> FixedMap(string notaText)
    {
        var body = Regex.Match(notaText, @"\bFixed\s*=\s*new\([^)]*\)\s*\{(?<b>.*?)\n    \};", RegexOptions.Singleline).Groups["b"].Value;
        return Regex.Matches(body, @"\[""(?<k>[^""]+)""\]\s*=\s*""(?<v>[^""]+)""")
            .ToDictionary(m => m.Groups["k"].Value, m => m.Groups["v"].Value, StringComparer.Ordinal);
    }

    /// <summary>The label literals in one file: (line, literal as written, its text, whether it
    /// reaches the screen through NotaText.Label).</summary>
    private static IEnumerable<(int Line, string Key, string Text, bool ViaLabel)> LabelLiterals(
        string file, string rel, Dictionary<string, Dictionary<string, List<bool>>> defs)
    {
        if (file.EndsWith(".axaml"))
        {
            var lines = BlankXmlComments(File.ReadAllText(file)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
                foreach (Match m in XamlContext.Matches(lines[i]))
                {
                    var v = m.Groups["v"].Value;
                    // "_File": the underscore marks the access key, it is not shown.
                    yield return (i + 1, v, Regex.Replace(WebUtility.HtmlDecode(v), @"_(?=\w)", ""), false);
                }
            yield break;
        }

        var contexts = CsContexts.AsEnumerable();
        if (rel.EndsWith("Palette/CommandCatalog.cs")) contexts = contexts.Append(CatalogTitle);
        if (rel.EndsWith("Palette/PaletteSearch.cs")) contexts = contexts.Concat(PaletteHeadings);
        var routed = RoutedBuilders(file, defs);
        int n = 0;
        foreach (var line in File.ReadLines(file))
        {
            n++;
            var code = CodeOf(line);
            var seen = new HashSet<int>();
            foreach (var re in contexts)
                foreach (Match m in re.Matches(code))
                {
                    bool viaBuilder = m.Groups["b"].Success && routed.Contains(m.Groups["b"].Value);
                    foreach (Capture c in m.Groups["lit"].Captures)
                    {
                        if (!seen.Add(c.Index)) continue;
                        var body = c.Value[1..^1];
                        yield return (n, body, Unescape(body), viaBuilder || InsideLabelCall(code, c.Index));
                    }
                }
        }
    }

    /// <summary>Whether the literal at <paramref name="at"/> is an argument of a Label( call
    /// that is still open there — "NotaText.Label(on ? "ON" : "OFF")".</summary>
    private static bool InsideLabelCall(string code, int at)
    {
        int depth = 0;
        for (int i = at - 1; i >= 0; i--)
        {
            if (code[i] == ')') depth++;
            else if (code[i] == '(')
            {
                if (depth > 0) { depth--; continue; }
                if (code[..i].TrimEnd().EndsWith("Label")) return true;
            }
        }
        return false;
    }

    /// <summary>The label builders a file defines, each with whether every body routes its
    /// text through NotaText.Label.</summary>
    private static Dictionary<string, List<bool>> BuilderDefs(string text)
    {
        var defs = new Dictionary<string, List<bool>>(StringComparer.Ordinal);
        // A declaration: a return type, the name, then a typed first parameter.
        foreach (Match m in Regex.Matches(text, $@"(?<type>[\w.<>?\[\]]+)\s+(?<b>{Builders})\s*\(\s*[\w.<>?\[\]]+\s+\w+\s*[,)=]"))
        {
            if (m.Groups["type"].Value is "return" or "new" or "await" or "else" or "yield" or "in" or "is") continue;
            var body = DeclarationBody(text, m.Index + m.Length - 1);
            var name = m.Groups["b"].Value;
            if (!defs.TryGetValue(name, out var l)) defs[name] = l = new List<bool>();
            l.Add(body.Contains("NotaText.Label("));
        }
        return defs;
    }

    /// <summary>The text of a declaration from inside its parameter list to the end of its
    /// body: an expression body to its ';', a block body to its closing brace.</summary>
    private static string DeclarationBody(string text, int from)
    {
        // Close the parameter list first: we start inside it, at depth 1.
        int i = from, depth = 1;
        while (i < text.Length && depth > 0)
        {
            i = SkipString(text, i);
            if (i >= text.Length) break;
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            i++;
        }
        int start = i;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        bool expression = i + 1 < text.Length && text[i] == '=' && text[i + 1] == '>';
        depth = 0;
        for (; i < text.Length; i++)
        {
            i = SkipString(text, i);
            if (i >= text.Length) break;
            char c = text[i];
            if (c is '{' or '(') depth++;
            else if (c is '}' or ')')
            {
                depth--;
                if (!expression && depth == 0 && c == '}') return text[start..(i + 1)];
            }
            else if (expression && depth == 0 && c == ';') return text[start..(i + 1)];
        }
        return text[start..];
    }

    private static int SkipString(string text, int i)
    {
        while (i < text.Length && text[i] == '"')
        {
            i++;
            while (i < text.Length && text[i] != '"' && text[i] != '\n') i += text[i] == '\\' ? 2 : 1;
            i++;
        }
        return i;
    }

    /// <summary>The builders whose literals reach the screen through NotaText.Label: every
    /// definition in the file routes, or — when the file defines none — every definition in
    /// its partial-class siblings (SessionView.Inspector.cs → SessionView*.cs).</summary>
    private static HashSet<string> RoutedBuilders(string file, Dictionary<string, Dictionary<string, List<bool>>> defs)
    {
        var routed = new HashSet<string>(StringComparer.Ordinal);
        var name = Path.GetFileName(file);
        var stem = name[..name.IndexOf('.')];
        var dir = Path.GetDirectoryName(file);
        var siblings = defs.Where(d => d.Key != file && Path.GetDirectoryName(d.Key) == dir
                                       && Path.GetFileName(d.Key).StartsWith(stem + ".", StringComparison.Ordinal)).ToArray();
        foreach (var b in Builders.Split('|'))
        {
            var own = defs[file].GetValueOrDefault(b);
            var found = own ?? siblings.SelectMany(s => s.Value.GetValueOrDefault(b) ?? new List<bool>()).ToList();
            if (found.Count > 0 && found.All(r => r)) routed.Add(b);
        }
        return routed;
    }

    /// <summary>All caps: two or more letters, none lower case, and some word that is not an
    /// acronym kept as written (one letter, a note or code with digits, or a NotaText.Fixed
    /// entry whose value is its key).</summary>
    private static bool Shouts(string text, Dictionary<string, string> fixedMap)
    {
        if (text.Count(char.IsLetter) < 2 || text.Any(char.IsLower)) return false;
        var whole = text.Trim();
        if (fixedMap.TryGetValue(whole, out var wv) && wv == whole) return false;
        return Regex.Matches(text, @"[\p{L}\p{N}#]+").Any(m => !KeepsCase(m.Value, fixedMap));
    }

    // Mirrors NotaText.Word: which upper-case words NotaText.Label would leave as they are.
    private static bool KeepsCase(string w, Dictionary<string, string> fixedMap)
    {
        if (fixedMap.TryGetValue(w, out var f)) return f == w;
        if (w.Length == 1 || w.Any(char.IsDigit) || w.Contains('#'))
        {
            var stem = w.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (stem.Length > 1 && stem.Length < w.Length && fixedMap.TryGetValue(stem, out var fs)) return fs == stem;
            return true;
        }
        return false;
    }

    // Short words that stay lower case in Title Case too, so they say nothing either way.
    private static readonly HashSet<string> MinorWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "nor", "but", "for", "to", "of", "in", "on", "at", "by",
        "with", "from", "as", "into", "onto", "per", "via", "vs", "than", "it", "its",
    };

    /// <summary>The first offending capitalised word if the literal is Title Case, else null.
    /// A word offends when it is capitalised, is not the first of its phrase (a phrase restarts
    /// after · — – : | ? ! → or a full stop), and has no reason to be: not an acronym, a view
    /// or product name, or part of a "Nota …" device name. The literal is Title Case when its
    /// offending words outnumber its lower-case ones — so "Copy to Scene" and "Stop All Clips"
    /// fail, while a tooltip sentence that names a control ("drag up / down, Shift for fine")
    /// does not: there the capital marks a name, which the label rule allows.</summary>
    private static string? TitleCaseWord(string text, Dictionary<string, string> fixedMap)
    {
        if (!text.Any(char.IsLower)) return null;
        bool first = true, nota = false;
        string? offender = null;
        int offending = 0, lower = 0;
        foreach (Match m in Regex.Matches(text, @"(?<w>[\p{L}\p{N}][\p{L}\p{N}#'’\-&+]*)|(?<s>[·—–:|?!•→]|\.(?=\s|$)|\s-\s)"))
        {
            if (m.Groups["s"].Success) { first = true; nota = false; continue; }
            var w = m.Groups["w"].Value;
            if (first) { first = false; nota = w == "Nota"; continue; }
            if (!char.IsLetter(w[0])) continue;
            if (!char.IsUpper(w[0]))
            {
                nota = false;
                if (!MinorWords.Contains(w)) lower++;
                continue;
            }
            if (w == "Nota") { nota = true; continue; }
            // "Nota Auto Filter": every word of a Nota device name is part of the name.
            if (nota || NameLike(w, fixedMap)) continue;
            offender ??= w;
            offending++;
        }
        return offending > lower ? offender : null;
    }

    private static bool NameLike(string w, Dictionary<string, string> fixedMap)
    {
        var head = Regex.Replace(w.Split('-')[0], "['’]s$", "");
        return head.Length <= 1 || head.Any(char.IsDigit) || head.Contains('#') || ProperNouns.Contains(head)
               || fixedMap.ContainsKey(head) || fixedMap.ContainsValue(head);
    }
}
