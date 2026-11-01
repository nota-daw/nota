// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface language (spec 068), checked in two parts.
//
//   RunLanguage      the pure layer as data: the language-file contract, lookup and fallback,
//                    plurals, invariant number format, the settings default and the real
//                    Languages/*.json files.
//   RunLocalization  the source against the translation: every Loc.T / F / FC / Plural key in
//                    src/managed has a French entry (a missing one fails the build), none is
//                    orphaned, placeholders match; and a shrink-only ratchet on interface text still written as a raw literal
//                    (LocalizationAllowList.txt), the same technique as the label ratchet of
//                    spec 012. Wrapping a literal in Loc.T takes it off the list; a new raw
//                    literal fails until it is wrapped or listed as data.
//
// NOTA_LOC_WRITE_BASELINE=1 rewrites LocalizationAllowList.txt from the current source
// (keeping the reasons already given) — how the baseline is made and how it is refreshed.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    // ---- The language layer as data ---------------------------------------------------------

    public static IEnumerable<(bool Ok, string Label)> RunLanguage()
    {
        var available = new[] { "en", "fr" };

        // Plural rules (data-model § Available languages).
        yield return (UiLanguage.Plural("en", 1) == PluralForm.One && UiLanguage.Plural("en", 0) == PluralForm.Other
                      && UiLanguage.Plural("en", 2) == PluralForm.Other,
            "English: 1 is one; 0 and 2 are other");
        yield return (UiLanguage.Plural("fr", 0) == PluralForm.One && UiLanguage.Plural("fr", 1) == PluralForm.One
                      && UiLanguage.Plural("fr", 2) == PluralForm.Other,
            "French: 0 and 1 are one; 2 is other");
        yield return (UiLanguage.Plural("xx", 1) == PluralForm.One && UiLanguage.Plural("xx", 0) == PluralForm.Other,
            "a language with no rule counts like English");

        // Stored value → running language.
        yield return (UiLanguage.Normalize(null, available) == "en" && UiLanguage.Normalize("", available) == "en"
                      && UiLanguage.Normalize("zz", available) == "en" && UiLanguage.Normalize("EN-gb", available) == "en",
            "no, empty or unknown language reads as English");
        yield return (UiLanguage.Normalize("fr", available) == "fr" && UiLanguage.Normalize(" FR ", available) == "fr",
            "a known language is kept (case and spaces ignored)");
        yield return (UiLanguage.IsPending("fr", "en", available) && !UiLanguage.IsPending("en", "en", available)
                      && !UiLanguage.IsPending("fr", "fr", available) && !UiLanguage.IsPending("zz", "en", available)
                      && UiLanguage.IsPending("en", "fr", available),
            "a restart is pending exactly when the stored language is not the running one");

        // Settings: nothing stored → English, other settings untouched.
        yield return (new Settings().Language == "en", "no setting → English");
        var older = JsonSerializer.Deserialize<Settings>("{\"InterfaceScale\":150}")!;
        yield return (older.Language == "en" && older.InterfaceScale == 150, "a settings file from before the language keeps its values and reads English");
        yield return (JsonSerializer.Deserialize<Settings>("{\"Language\":\"fr\"}")!.Language == "fr", "the stored language is read");

        // Choosing the running language again cancels a pending change (data-model state diagram).
        var pendingStored = "fr";
        bool pendingBefore = UiLanguage.IsPending(pendingStored, "en", available);
        pendingStored = "en";
        yield return (pendingBefore && !UiLanguage.IsPending(pendingStored, "en", available),
            "choosing the running language again clears the pending change");

        // A hand-edited unknown language reads as English, and what is stored is left as written.
        var edited = JsonSerializer.Deserialize<Settings>("{\"Language\":\"zz\",\"InterfaceScale\":125}")!;
        yield return (UiLanguage.Normalize(edited.Language, available) == "en" && edited.Language == "zz" && edited.InterfaceScale == 125,
            "an unknown stored language runs as English and is not rewritten by reading it");

        // The language-file contract (contracts/translation-file.md).
        const string frJson = """
            { "formatVersion": 1, "code": "fr", "name": "Français", "strings": {
                "Save": "Enregistrer",
                "Open|menu": "Ouvrir",
                "Open|clip": "Ouvrir le clip",
                "Blank": "",
                "Bad {0}": "Mauvais {",
                "{0} tracks": { "one": "{0} piste", "other": "{0} pistes" },
                "{0} clips": { "other": "{0} clips FR" },
                "Brace {0}": "Accolade {{x}} {0}"
            } }
            """;
        var fr = TranslationTable.Parse(frJson, out var frError);
        yield return (fr is not null && fr.Code == "fr" && fr.Name == "Français" && fr.Count == 8,
            fr is null ? "a language file parses: " + frError : "a language file parses (8 texts)");
        if (fr is null) yield break;

        string? e1, e2, e3, e4, e5, e6;
        yield return (TranslationTable.Parse("{\"formatVersion\":2,\"code\":\"xx\",\"name\":\"X\"}", out e1) is null && e1 is not null,
            "a newer formatVersion is left out with a reason, not thrown");
        yield return (TranslationTable.Parse("{ not json", out e2) is null && e2 is not null, "malformed JSON is left out with a reason, not thrown");
        yield return (TranslationTable.Parse("{\"formatVersion\":1,\"code\":\"xx\",\"name\":\"X\",\"strings\":{\"A\":\"1\",\"A\":\"2\"}}", out e3) is null && e3 is not null,
            "a duplicate key is an error");
        yield return (TranslationTable.Parse("{\"formatVersion\":1,\"code\":\"xx\",\"name\":\"X\",\"strings\":{\"A\":5}}", out e4) is null && e4 is not null,
            "a text that is neither a string nor a plural object is an error");
        yield return (TranslationTable.Parse("{\"formatVersion\":1,\"code\":\"FR\",\"name\":\"X\"}", out e5) is null
                      && TranslationTable.Parse("{\"formatVersion\":1,\"code\":\"xx\"}", out e6) is null,
            "an upper-case code or a missing name is an error");

        // Lookup and fallback, with French running.
        Loc.ResetForTests();
        yield return (!Loc.UsedBeforeInstall, "nothing has been asked for yet");
        _ = Loc.T("anything");
        yield return (Loc.UsedBeforeInstall, "a text asked for before Install is flagged");
        Loc.ResetForTests();
        Loc.Install(null, "en");
        _ = Loc.T("anything");
        yield return (!Loc.UsedBeforeInstall && Loc.Language == "en", "after Install nothing is flagged");

        Loc.Install(fr, "fr");
        yield return (Loc.Language == "fr", "French is running");
        yield return (Loc.T("Save") == "Enregistrer", "a translated text is returned");
        yield return (Loc.T("Never translated") == "Never translated", "a missing text is its English original");
        yield return (Loc.T("Open", "menu") == "Ouvrir" && Loc.T("Open", "clip") == "Ouvrir le clip", "a context picks the meaning");
        yield return (Loc.T("Open") == "Open" && Loc.T("Close", "menu") == "Close", "no context leaks into the shown text");
        yield return (Loc.T("Blank") == "Blank", "an empty translation is never shown blank");
        yield return (Loc.Plural(0, "{0} track", "{0} tracks") == "0 piste" && Loc.Plural(1, "{0} track", "{0} tracks") == "1 piste"
                      && Loc.Plural(2, "{0} track", "{0} tracks") == "2 pistes",
            "French counts 0 and 1 as one");
        yield return (Loc.Plural(1, "{0} clip", "{0} clips") == "1 clips FR", "a counted text with no singular form uses its other form");
        yield return (Loc.Plural(1, "{0} take", "{0} takes") == "1 take" && Loc.Plural(0, "{0} take", "{0} takes") == "0 takes",
            "a counted text with no entry falls back to English and its rule");
        yield return (Loc.F("Bad {0}", 1) == "Bad 1", "a broken pattern in a language file falls back to English, not an exception");
        yield return (Loc.F("Brace {0}", 7) == "Accolade {x} 7", "{{ }} are literal braces");

        // Numbers follow the thread culture, as the interpolated strings they replace did; the
        // language never changes that culture.
        var before = CultureInfo.CurrentCulture;
        string? notation = null;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            notation = Loc.F("{0}", 1.5) + "|" + Loc.Plural(2, "{0} bar", "{0} bars");
        }
        catch (CultureNotFoundException) { notation = "1,5|2 bars"; }
        finally { CultureInfo.CurrentCulture = before; }
        yield return (notation == "1,5|2 bars", "numbers format with the thread culture, which the language leaves alone");
        Loc.Install(fr, "fr");
        yield return (before.Name == CultureInfo.CurrentCulture.Name, "installing a language leaves the thread culture alone");

        // English running: every call is its own text.
        Loc.Install(fr, "en");
        yield return (Loc.T("Save") == "Save" && Loc.T("Open", "menu") == "Open" && Loc.Plural(1, "{0} track", "{0} tracks") == "1 track",
            "English running: wrapping a text never changes it");
        yield return (Loc.Plural(2, "{0} track", "{0} tracks") == "2 tracks" && Loc.F("Delete {0} clips?", 3) == "Delete 3 clips?",
            "English running: counted and formatted texts read as written");

        // A language the app does not ship, from a fixture: any file in the format works (spec 068 US5).
        var fixtureRoot = RepoRoot();
        if (fixtureRoot is not null)
        {
            var xx = TranslationTable.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "tests/Nota.SmokeTest/Fixtures/Languages/xx.json")), out var xxError);
            yield return (xx is not null && xx.Code == "xx" && xx.Name == "Xxxx", xx is null ? "the fixture language parses: " + xxError : "the fixture language parses");
            if (xx is not null)
            {
                Loc.Install(xx, "xx");
                yield return (Loc.T("Save") == "Xsave" && Loc.T("Open", "menu") == "Xopen" && Loc.T("Open", "clip") == "Xopen clip",
                    "a new language translates its texts, contexts included");
                yield return (Loc.T("Never written") == "Never written" && Loc.F("Delete {0} clips?", 3) == "Delete 3 clips?",
                    "a text the new language does not cover shows in English");
                yield return (Loc.Plural(1, "{0} track", "{0} tracks") == "1 xtrack" && Loc.Plural(0, "{0} track", "{0} tracks") == "0 xtracks",
                    "a language with no plural rule counts like English");
            }
        }
        Loc.Install(fr, "en");

        // The real language files, from disk.
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var dir = Path.Combine(root, "src/managed/Nota.App/Languages");
        var files = Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file)!;
            var table = TranslationTable.Parse(File.ReadAllText(file), out var error);
            yield return (table is not null && table.Code == id, table is null ? $"{id}.json: {error}" : $"{id}.json: valid ({table.Name}, {table.Count} texts)");
        }
        var names = files.Select(f => TranslationTable.Parse(File.ReadAllText(f), out _)).Where(t => t is not null)
            .ToDictionary(t => t!.Code, t => t!.Name);
        yield return (names.GetValueOrDefault("en") == "English" && names.GetValueOrDefault("fr") == "Français",
            "the picker offers English and Français, each by its own name");

        Loc.ResetForTests();
        Loc.Install(null, "en");   // as the app leaves it: English installed
    }

    // ---- The source against the translation ------------------------------------------------

    private const string LocLit = @"""(?:[^""\\\n]|\\.)*""";
    private static readonly Regex LocT = new($@"\bLoc\.T\(\s*(?<t>{LocLit})(?:\s*,\s*(?:context:\s*)?(?<c>{LocLit}))?");
    private static readonly Regex LocF = new($@"\bLoc\.F\(\s*(?<t>{LocLit})");
    private static readonly Regex LocFC = new($@"\bLoc\.FC\(\s*(?<c>{LocLit})\s*,\s*(?<t>{LocLit})");
    private static readonly Regex LocPlural = new($@"\bLoc\.Plural\([^""]*?,\s*(?<one>{LocLit})\s*,\s*(?<t>{LocLit})(?:\s*,\s*(?:context:\s*)?(?<c>{LocLit}))?");
    private static readonly Regex LocInterpolated = new(@"\bLoc\.(?:T|F)\(\s*[$@]+""|\bLoc\.FC\(\s*""[^""]*""\s*,\s*[$@]+""");
    private static readonly Regex LocPlaceholder = new(@"\{(\d+)(?:[,:][^{}]*)?\}");
    // The XAML form: Header="{app:T 'Save as…'}" or {app:T File}, optionally with ", Context=menu".
    private static readonly Regex LocXaml = new(@"\{app:T\s+(?:'(?<t>(?:[^'\\]|\\.)*)'|(?<t>[^\s'},][^},]*?))\s*(?:,\s*Context=(?<c>[^}\s]+))?\s*\}");

    private static HashSet<int> Placeholders(string s)
        => LocPlaceholder.Matches(s.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

    private readonly record struct LocKey(string Key, bool Plural, string Path, int Line);

    /// <summary>Every Loc.T / F / FC / Plural call in src/managed with a literal text, plus the
    /// calls whose text is interpolated (which can never be translated).</summary>
    private static (List<LocKey> Keys, List<Hit> Interpolated) LocCalls(string root)
    {
        var managed = Path.Combine(root, "src/managed");
        var keys = new List<LocKey>();
        var bad = new List<Hit>();
        foreach (var f in SourceFiles(managed, ".cs"))
        {
            var rel = RelManaged(root, f);
            if (rel.StartsWith("Nota.Application/Localization/")) continue;   // its own examples
            var text = string.Join('\n', File.ReadLines(f).Select(CodeOf));
            if (!text.Contains("Loc.")) continue;
            int LineOf(int index) { int n = 1; for (int i = 0; i < index; i++) if (text[i] == '\n') n++; return n; }
            string Key(Match m) => TranslationTable.KeyOf(Unescape(m.Groups["t"].Value[1..^1]),
                m.Groups["c"].Success ? Unescape(m.Groups["c"].Value[1..^1]) : null);
            foreach (Match m in LocT.Matches(text)) keys.Add(new LocKey(Key(m), false, rel, LineOf(m.Index)));
            foreach (Match m in LocF.Matches(text)) keys.Add(new LocKey(Key(m), false, rel, LineOf(m.Index)));
            foreach (Match m in LocFC.Matches(text)) keys.Add(new LocKey(Key(m), false, rel, LineOf(m.Index)));
            foreach (Match m in LocPlural.Matches(text)) keys.Add(new LocKey(Key(m), true, rel, LineOf(m.Index)));
            foreach (Match m in LocInterpolated.Matches(text)) bad.Add(new Hit(rel, LineOf(m.Index), m.Value));
        }
        foreach (var f in SourceFiles(managed, ".axaml"))
        {
            var rel = RelManaged(root, f);
            var text = BlankXmlComments(File.ReadAllText(f));
            foreach (Match m in LocXaml.Matches(text))
            {
                int line = 1; for (int i = 0; i < m.Index; i++) if (text[i] == '\n') line++;
                keys.Add(new LocKey(TranslationTable.KeyOf(WebUtility.HtmlDecode(m.Groups["t"].Value).Replace("\\'", "'"),
                    m.Groups["c"].Success ? m.Groups["c"].Value : null), false, rel, line));
            }
        }
        return (keys, bad);
    }

    public static IEnumerable<(bool Ok, string Label)> RunLocalization()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }

        // -- coverage: source keys ↔ the French file --
        var (calls, interpolated) = LocCalls(root);
        yield return (interpolated.Count == 0,
            interpolated.Count == 0 ? "no Loc call passes an interpolated string (build the pattern with {0} and Loc.F)"
                : "interpolated text in a Loc call can never be translated: " + string.Join(", ", interpolated.Take(8).Select(h => $"{h.Path}:{h.Line}")));

        var used = new Dictionary<string, LocKey>(StringComparer.Ordinal);
        foreach (var c in calls) if (!used.ContainsKey(c.Key)) used[c.Key] = c;

        var frPath = Path.Combine(root, "src/managed/Nota.App/Languages/fr.json");
        var fr = TranslationTable.Parse(File.ReadAllText(frPath), out var frError);
        yield return (fr is not null, fr is null ? "fr.json: " + frError : "fr.json parsed");
        if (fr is null) yield break;

        var frKeys = fr.Keys.ToHashSet(StringComparer.Ordinal);
        var missing = used.Keys.Where(k => !frKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        // NOTA_LOC_DUMP_MISSING=<file> writes the texts French still lacks (key, counted?, file) as JSON.
        if (Environment.GetEnvironmentVariable("NOTA_LOC_DUMP_MISSING") is { Length: > 0 } dump)
            File.WriteAllText(dump, JsonSerializer.Serialize(missing.Select(k => new { key = k, plural = used[k].Plural, file = used[k].Path, line = used[k].Line })));
        yield return (missing.Count == 0,
            missing.Count == 0 ? $"French covers all {used.Count} interface texts in Loc calls"
                : $"{missing.Count} texts have no French: {string.Join(", ", missing.Take(8).Select(k => $"\"{k}\""))}");

        var orphans = frKeys.Where(k => !used.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        yield return (orphans.Count == 0,
            orphans.Count == 0 ? "every French text is used by a Loc call"
                : $"French texts no Loc call uses (renamed or removed in the source — fix or delete them): {string.Join(", ", orphans.Take(8).Select(k => $"\"{k}\""))}"
                  + (orphans.Count > 8 ? $" (+{orphans.Count - 8} more)" : ""));

        var parity = new List<string>();
        foreach (var key in fr.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            fr.TryGet(key, out var t);
            var want = Placeholders(key);
            if (!Placeholders(t.Other).SetEquals(want)) parity.Add($"\"{key}\" (other form has {{{string.Join(",", Placeholders(t.Other).Order())}}}, key has {{{string.Join(",", want.Order())}}})");
            // The singular form may drop the number ("Une piste") but may not invent a placeholder.
            else if (t.One is not null && !Placeholders(t.One).IsSubsetOf(want)) parity.Add($"\"{key}\" (singular form has a placeholder the key lacks)");
        }
        // Every call, not one per key: a text counted in one place and plain in another is a bug in the plain one.
        foreach (var c in calls)
            if (fr.TryGet(c.Key, out var ct) && c.Plural != (ct.One is not null))
                parity.Add($"\"{c.Key}\" at {c.Path}:{c.Line} ({(c.Plural ? "counted in the source but has no singular form" : "plain in the source but has a singular form")})");
        yield return (parity.Count == 0,
            parity.Count == 0 ? "French placeholders match their English and counted texts have both forms"
                : "French placeholder or plural mismatch: " + string.Join("; ", parity.Take(6)));

        // -- the language is a view setting: it follows only the stored choice, and writes nothing --
        var osLanguage = new List<string>();
        var persistedText = new List<string>();
        foreach (var f in SourceFiles(Path.Combine(root, "src/managed"), ".cs"))
        {
            var rel = RelManaged(root, f);
            var source = File.ReadAllText(f);
            if ((source.Contains("CurrentUICulture") || source.Contains("InstalledUICulture")) && rel != "Nota.App/NotaDocs.cs") osLanguage.Add(rel);
            bool writesWork = rel.StartsWith("Nota.Domain/") || rel.StartsWith("Nota.Infrastructure/Settings/")
                || Regex.IsMatch(rel, @"^Nota\.Infrastructure/Persistence/(Project(Model|Service|Store|Template|Migrations)|Recovery|PresetModel|PresetStore|PresetLibrary|AudioClipState)\w*\.cs$");
            if (writesWork && source.Contains("Loc.")) persistedText.Add(rel);
        }
        yield return (osLanguage.Count == 0,
            osLanguage.Count == 0 ? "the interface language never comes from the OS language (spec 068 FR-006)"
                : "the OS language is read in: " + string.Join(", ", osLanguage));
        yield return (persistedText.Count == 0,
            persistedText.Count == 0 ? "no project, preset or settings writer uses Loc (the language never changes saved work)"
                : "translated text in code that is saved: " + string.Join(", ", persistedText));
        // macOS shows its own text (Hide, Quit, the open panels) in a language the Info.plist lists.
        var shipped = Directory.EnumerateFiles(Path.Combine(root, "src/managed/Nota.App/Languages"), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).ToList();
        var unlisted = new List<string>();
        foreach (var script in new[] { "scripts/bundle-mac.sh", "scripts/package-dmg.sh" })
        {
            var plist = File.ReadAllLines(Path.Combine(root, script)).FirstOrDefault(l => l.Contains("CFBundleLocalizations")) ?? "";
            foreach (var code in shipped.Where(c => !plist.Contains($"<string>{c}</string>"))) unlisted.Add($"{script}: {code}");
        }
        yield return (unlisted.Count == 0,
            unlisted.Count == 0 ? "the macOS Info.plist lists every shipped language (CFBundleLocalizations)"
                : "language missing from CFBundleLocalizations: " + string.Join(", ", unlisted));
        var appStart = File.ReadAllText(Path.Combine(root, "src/managed/Nota.App/App.axaml.cs"));
        int installAt = appStart.IndexOf("private static void InstallLanguage", StringComparison.Ordinal);
        int installEnd = installAt < 0 ? -1 : appStart.IndexOf("private void TranslateAppMenu", installAt, StringComparison.Ordinal);
        yield return (installAt >= 0 && installEnd > installAt && !appStart[installAt..installEnd].Contains(".Save("),
            "starting in a language writes nothing (a stored value that is unknown is left as written)");

        // -- the ratchet: interface text still written as a raw literal --
        var managed = Path.Combine(root, "src/managed");
        var app = Path.Combine(managed, "Nota.App");
        var palette = Path.Combine(managed, "Nota.Application/Palette");
        var labelFiles = SourceFiles(app, ".cs", ".axaml").Concat(SourceFiles(palette, ".cs")).ToList();
        var defs = labelFiles.Where(f => f.EndsWith(".cs")).ToDictionary(f => f, f => BuilderDefs(File.ReadAllText(f)));

        var raw = new List<Hit>();
        // 1. Text set through the label contexts (Text / Content / Header / Title, tooltips, label
        //    builders, command titles, palette headings, XAML attributes).
        foreach (var f in labelFiles)
        {
            var rel = RelManaged(root, f);
            if (rel.StartsWith("Nota.App/Languages/")) continue;
            foreach (var (line, key, text, _) in LabelLiterals(f, rel, defs, includeWrapped: false))
                if (LooksLikeWords(text)) raw.Add(new Hit(rel, line, key));
        }
        // 2. Any other literal that reads as interface text, wherever it is handed over: the local
        //    helpers (Row, Caption, Section, Btn, SwitchRow …) are too many and too differently
        //    named to list, and a view model sets its text through properties other than Text.
        foreach (var dir in new[] { "Nota.App", "Nota.Presentation", "Nota.Application" })
            foreach (var f in SourceFiles(Path.Combine(managed, dir), ".cs"))
            {
                var rel = RelManaged(root, f);
                if (rel.StartsWith("Nota.Application/Localization/")
                    || rel.StartsWith("Nota.App/Languages/")) continue;
                int n = 0;
                foreach (var line in File.ReadLines(f))
                {
                    n++;
                    var code = CodeOf(line);
                    if (LocNotText.IsMatch(code)) continue;
                    foreach (var (index, body) in Literals(code))
                    {
                        var before = code[..index];
                        if (InsideLoc(before)) continue;   // already wrapped
                        if (LocNotTextBefore.IsMatch(before)) continue;
                        if (LooksLikeInterfaceText(Unescape(body))) raw.Add(new Hit(rel, n, body));
                    }
                    // A plural worked out by hand ("{n} clip{(n == 1 ? "" : "s")}") can never be translated: Loc.Plural.
                    foreach (Match m in HandPlural.Matches(code))
                        if (!InsideLoc(code[..m.Index])) raw.Add(new Hit(rel, n, "plural by hand: " + m.Value));
                    // $"Delete {n} clips?" — Literals() leaves interpolated strings out (their text is
                    // built), but a sentence with holes is interface text all the same: Loc.F it.
                    foreach (Match m in LocInterpolatedLit.Matches(code))
                    {
                        if (LocNotTextBefore.IsMatch(code[..m.Index])) continue;
                        if (LooksLikeInterpolatedText(Regex.Replace(m.Groups[1].Value, @"\{[^{}]*\}", "{0}")))
                            raw.Add(new Hit(rel, n, "$\"" + m.Groups[1].Value + "\""));
                    }
                }
            }

        var listPath = Path.Combine(root, RatchetDir, "LocalizationAllowList.txt");
        if (Environment.GetEnvironmentVariable("NOTA_LOC_WRITE_BASELINE") == "1")
        {
            WriteLocBaseline(listPath, raw);
            yield return (true, $"wrote {raw.Select(h => (h.Path, h.Key)).Distinct().Count()} entries to LocalizationAllowList.txt");
        }

        var list = ReadRatchet(listPath);
        var allowed = list.ToHashSet();
        var usedEntries = new HashSet<(string, string)>();
        yield return RatchetVerdict("raw interface text (wrap it in Loc.T, or list it as data)", raw, allowed, usedEntries);
        yield return RatchetStale("localization allow-list", list, usedEntries);
        yield return (true, $"localization allow-list: {allowed.Count} entries (ratchet: may only shrink)");
    }

    // Log, exception, attribute and diagnostic lines hold text that is not interface text.
    private static readonly Regex LocNotText = new(
        @"\.(?:Info|Warn|Error|Write|WriteLine|Debug|Trace)\(|\bthrow\b|\bDebug\.|\bTrace\.|\bConsole\.|\bnameof\(|^\s*\[|Exception\(|Description\(|McpServer|^\s*(?:using|namespace)\b");

    // A literal that is compared, looked up, split or used as a key is not shown to anyone.
    private static readonly Regex LocNotTextBefore = new(
        @"(?:==|!=|\bcase|\bis|\bnot|\bor|\band|StartsWith\(|EndsWith\(|Contains\(|Equals\(|IndexOf\(|Replace\(|Split\(|Trim\w*\(|TryGetValue\(|ContainsKey\(|FindResource\(|TryFindResource\(|new Uri\(|new Regex\(|Regex\.\w+\(|GetManifestResource\w*\(|GetEnvironmentVariable\(|NotaNum\.F\(|\[|Classes\s*=\s*\{[^}]*|Classes\.Add\(|Path\.Combine\([^;]*|SetEnvironmentVariable\()\s*$");

    private static readonly Regex HandPlural = new(@"==\s*1\s*\?\s*""[A-Za-z ]*""\s*:\s*""[A-Za-z ]*""");

    private static readonly Regex LocInterpolatedLit = new(@"\$""((?:[^""\\\n]|\\.)*)""");

    // A sentence once the holes are taken out: a space, a word of three letters, no code marks.
    private static bool LooksLikeInterpolatedText(string shape)
        => shape.Contains(' ') && Regex.IsMatch(shape, "[A-Za-z]{3,}") && !Regex.IsMatch(shape, "[_<>=#]")
           && !shape.StartsWith("http", StringComparison.Ordinal);

    // "Tab   ⇧Tab", "⌘C  ⌘X": key names and modifier glyphs only — a binding, not a sentence.
    private static readonly HashSet<string> KeyWords = new(StringComparer.Ordinal)
        { "Space", "Enter", "Esc", "Tab", "Shift", "Ctrl", "Alt", "Backspace", "Home", "End", "Option", "Cmd", "Command", "Control", "Escape", "PgUp", "PgDn", "Del" };
    private static bool IsKeyCombo(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(k => KeyWords.Contains(k) || "⌘⇧⌥⌃↑↓←→".Contains(k[0]) || k.Length == 1);
    }

    // Whether the text ends inside an unclosed Loc.T( / F( / FC( / Plural( call, so a literal that follows a
    // closed call on the same line ("Loc.T("a") : "b"") is not mistaken for a wrapped one.
    private static bool InsideLoc(string before)
    {
        var s = Regex.Replace(before, @"""(?:[^""\\]|\\.)*""", "\"\"");
        foreach (Match m in Regex.Matches(s, @"\bLoc\.(?:T|F|FC|Plural)\("))
        {
            int depth = 1;
            for (int i = m.Index + m.Length; i < s.Length && depth > 0; i++)
                depth += s[i] == '(' ? 1 : s[i] == ')' ? -1 : 0;
            if (depth > 0) return true;
        }
        return false;
    }

    // A label worth translating has at least two letters in a row ("Rec" yes, "–" and "0" no).
    private static bool LooksLikeWords(string text) => Regex.IsMatch(text, @"[A-Za-z]{2,}");

    private static readonly HashSet<string> UnitWords = new(StringComparer.Ordinal)
        { "kHz", "Hz", "dB", "dBFS", "dBTP", "ms", "BPM", "bit", "oct", "LUFS", "RMS", "Mid", "Side", "fps", "smp", "MB", "GB", "kB" };

    // Reads as interface text: starts with a capital, then either has a space (a phrase) or is one
    // plain word ("Port", "Status") — not an identifier (BgApp), path, URL or format string.
    private static bool LooksLikeInterfaceText(string text)
    {
        // A slash makes a path ("Samples/Downloaded"), unless there are words around it ("your VST/AU plug-ins").
        bool slashIsPath = Regex.IsMatch(text, @"\S/\S") && text.Count(c => c == ' ') < 2;
        // Code marks (_ < > = # @) make an identifier or an expression, unless there are words around them
        // ("Shift = fine", "off_by group"): a sentence keeps them.
        bool codeMarks = text.Count(c => c == ' ') < 3 && Regex.IsMatch(text, @"[_<>=#@]");
        if (IsKeyCombo(text) || text.Length < 3 || slashIsPath || codeMarks || Regex.IsMatch(text, @"[\\]|://")) return false;
        // Starts with a number or a symbol: "16-bit only", "1 bar", "≈ 1 file · {0} MB" — interface text
        // when a word that is not a unit follows ("2.5 ms", "0 dB" and "4×" are not).
        if (Regex.IsMatch(text, @"^[0-9≈±+−][0-9.,×°\- ]*[ ‑-]") && text.Contains(' '))
        {
            var words = Regex.Matches(text, @"[A-Za-z][A-Za-z’']{2,}").Select(m => m.Value);
            if (words.Any(w => !UnitWords.Contains(w))) return true;
        }
        // A leading space, symbol or opening mark does not make it code: " Nota never shows …", "⇧ + wheel …".
        var s = text.TrimStart(" \t·—–-+⇧⌘⌥⌃↑↓←→(“\"'‘".ToCharArray());
        if (Regex.IsMatch(s, @"^[A-Z][A-Za-z ]") && (s.Contains(' ') || Regex.IsMatch(s, @"^[A-Z][a-z]{2,}(?:-[a-z]+)?[….:]?$"))) return true;
        // Lower-case prose: three or more plain words ("close to the limit", "plenty of headroom").
        return Regex.IsMatch(s, @"^[a-z]+(?: [a-z’']+){2,}");
    }

    private static void WriteLocBaseline(string path, List<Hit> raw)
    {
        var reasons = new Dictionary<(string, string), string>();
        var header = new List<string>();
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith('#')) { if (reasons.Count == 0) header.Add(line); continue; }
                var p = line.Split('\t');
                if (p.Length >= 3) reasons[(p[0].Trim(), p[1])] = p[2];
                else if (p.Length == 2) reasons[(p[0].Trim(), p[1])] = "# todo: wrap in Loc.T";
            }
        var sb = new StringBuilder();
        foreach (var h in header) sb.Append(h).Append('\n');
        foreach (var e in raw.Select(h => (h.Path, h.Key)).Distinct().OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal))
            sb.Append(e.Path).Append('\t').Append(e.Key).Append('\t').Append(reasons.GetValueOrDefault(e, "# todo: wrap in Loc.T")).Append('\n');
        File.WriteAllText(path, sb.ToString());
    }
}
