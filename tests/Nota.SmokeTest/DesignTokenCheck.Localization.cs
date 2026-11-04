// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface language (spec 068, keyed since spec 077), checked in two parts.
//
//   RunLanguage      the pure layer as data: the language-file contract on the shared fixtures
//                    (Fixtures/Languages/verdicts.json), the key-set hash, lookup and fallback,
//                    plural rules, invariant number format, the settings default and the real
//                    en.json and Languages/*.json files.
//   RunLocalization  the source against the English reference (spec 077): every Loc.T / F /
//                    Plural and {app:T} key in src/managed is a well-formed key of en.json, every
//                    en.json key is used, each is used the way it is shaped; French covers every
//                    key validly; and a shrink-only ratchet on interface text still written as a raw literal
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

        // Plural rules, named by the language file (spec 077 research R6).
        yield return (UiLanguage.Plural(UiLanguage.RuleOneIs1, 1) == PluralForm.One && UiLanguage.Plural(UiLanguage.RuleOneIs1, 0) == PluralForm.Other
                      && UiLanguage.Plural(UiLanguage.RuleOneIs1, 2) == PluralForm.Other,
            "one_is_1 (English): 1 is one; 0 and 2 are other");
        yield return (UiLanguage.Plural(UiLanguage.RuleOneIs0Or1, 0) == PluralForm.One && UiLanguage.Plural(UiLanguage.RuleOneIs0Or1, 1) == PluralForm.One
                      && UiLanguage.Plural(UiLanguage.RuleOneIs0Or1, 2) == PluralForm.Other,
            "one_is_0_1 (French): 0 and 1 are one; 2 is other");
        yield return (UiLanguage.IsKnownRule("one_is_1") && UiLanguage.IsKnownRule("one_is_0_1") && !UiLanguage.IsKnownRule("one_is_42") && !UiLanguage.IsKnownRule(null),
            "only the plural rules this build implements are known");

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

        // The language-file contract (spec 077 contracts/language-file.md), on the shared fixtures.
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var fixtures = Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/Languages");
        TranslationTable? Fixture(string name, out string? error) => TranslationTable.Parse(File.ReadAllText(Path.Combine(fixtures, name)), out error);
        var english = Fixture("en-ref.json", out var enError);
        yield return (english is not null && english.Count == 12 && english.PluralRule == UiLanguage.RuleOneIs1,
            english is null ? "the fixture reference parses: " + enError : "the fixture reference parses (12 keys, English counting)");
        if (english is null) yield break;

        using (var verdicts = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "verdicts.json"))))
            foreach (var v in verdicts.RootElement.GetProperty("files").EnumerateObject())
            {
                var table = Fixture(v.Name, out var error);
                bool loads = v.Value.GetProperty("loads").GetBoolean();
                if (!loads) { yield return (table is null && error is not null, $"{v.Name}: rejected with a reason ({error ?? "accepted!"})"); continue; }
                if (table is null) { yield return (false, $"{v.Name}: should load: {error}"); continue; }
                var (problems, coverage) = table.ValidateAgainst(english);
                var got = problems.Select(p => p.Key).Distinct().Order(StringComparer.Ordinal).ToList();
                var want = v.Value.GetProperty("problems").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal).ToList();
                int wantCoverage = v.Value.GetProperty("coverage").GetInt32();
                yield return (got.SequenceEqual(want) && coverage == wantCoverage,
                    $"{v.Name}: problems [{string.Join(", ", got)}], coverage {coverage} % (verdicts.json: [{string.Join(", ", want)}], {wantCoverage} %)");
            }

        string? e1, e2, e3, e4, e5, e6;
        yield return (TranslationTable.Parse("{ not json", out e1) is null && e1 is not null, "malformed JSON is left out with a reason, not thrown");
        yield return (TranslationTable.Parse("{\"formatVersion\":2,\"code\":\"xx\",\"name\":\"X\",\"plural\":\"one_is_1\",\"strings\":{\"a.b\":\"1\",\"a.b\":\"2\"}}", out e2) is null && e2 is not null,
            "a duplicate key is an error");
        yield return (TranslationTable.Parse("{\"formatVersion\":2,\"code\":\"FR\",\"name\":\"X\",\"plural\":\"one_is_1\"}", out e3) is null
                      && TranslationTable.Parse("{\"formatVersion\":2,\"code\":\"xx\",\"plural\":\"one_is_1\"}", out e4) is null,
            "an upper-case code or a missing name is an error");
        yield return (TranslationTable.Parse("{\"formatVersion\":2,\"code\":\"xx\",\"name\":\"X\"}", out e5) is null && e5 is not null
                      && TranslationTable.Parse("{\"formatVersion\":2,\"code\":\"en\",\"name\":\"English\"}", out e6) is not null,
            "a translation must name its plural rule; English counts as one_is_1 without one");
        yield return (TranslationTable.IsKey("main.save_as") && TranslationTable.IsKey("device.eq8.band_2") && !TranslationTable.IsKey("Save")
                      && !TranslationTable.IsKey("main") && !TranslationTable.IsKey("main.Save") && !TranslationTable.IsKey("main..save")
                      && !TranslationTable.IsKey("a." + new string('b', 80)),
            "keys are lower-case dot-separated segments, at least two, up to 80 characters");

        // The key-set hash: the identity of a Nota version's keys (contract § Key-set hash).
        string Reference(string strings) => $"{{\"formatVersion\":2,\"code\":\"en\",\"name\":\"English\",\"strings\":{{{strings}}}}}";
        string Hash(string strings) => TranslationTable.Parse(Reference(strings), out _)!.KeySetHash();
        var h = Hash("\"a.b\":\"Save {0}\",\"a.c\":{\"one\":\"{0} x\",\"other\":\"{0} xs\"}");
        yield return (h.Length == 16 && h == Hash("\"a.c\":{\"one\":\"{0} y\",\"other\":\"{0} ys\"},\"a.b\":\"Keep {0} safe\""),
            "the key-set hash ignores wording and order");
        yield return (h != Hash("\"a.b\":\"Save {0}\"") && h != Hash("\"a.b\":\"Save {0} {1}\",\"a.c\":{\"one\":\"{0} x\",\"other\":\"{0} xs\"}")
                      && h != Hash("\"a.b\":\"Save {0}\",\"a.c\":\"{0} xs\""),
            "the key-set hash changes with a removed key, new placeholders or a text that stops being counted");

        // Lookup and fallback, on the fixture reference.
        var xx = Fixture("xx.json", out _)!;
        var broken = Fixture("xx-broken.json", out _)!;
        Loc.ResetForTests(english);
        yield return (!Loc.UsedBeforeInstall, "nothing has been asked for yet");
        yield return (Loc.T("test.save") == "Save" && Loc.UsedBeforeInstall, "before Install a text is English, and flagged");
        Loc.ResetForTests(english);
        Loc.Install(null, "en");
        yield return (Loc.T("test.save") == "Save" && !Loc.UsedBeforeInstall && Loc.Language == "en", "after Install nothing is flagged");
        yield return (Loc.Plural("test.tracks", 1) == "1 track" && Loc.Plural("test.tracks", 0) == "0 tracks" && Loc.F("test.delete_clips", 3) == "Delete 3 clips?",
            "English running: counted and formatted texts read as the reference has them");
        string? missing = null;
        void OnMissing(string k) => missing = k;
        Loc.MissingKey += OnMissing;
        var shown = Loc.T("test.not_in_any_table");
        Loc.MissingKey -= OnMissing;
        yield return (shown == "test.not_in_any_table" && missing == "test.not_in_any_table", "a key in no table shows itself and is reported");

        Loc.Install(xx, "xx");
        yield return (Loc.Language == "xx" && Loc.T("test.save") == "Xsave", "a translated text is returned");
        yield return (Loc.T("test.open_menu") == "Xopen" && Loc.T("test.open_clip") == "Xopen clip", "two meanings of one English word are two keys");
        yield return (Loc.T("test.blank") == "Blank" && Loc.F("test.delete_clips", 3) == "Delete 3 clips?", "a text the language lacks is English");
        yield return (Loc.Plural("test.tracks", 1) == "1 xtrack" && Loc.Plural("test.tracks", 0) == "0 xtracks", "a one_is_1 language counts like English");
        yield return (Loc.Plural("test.takes", 1) == "1 take" && Loc.Plural("test.takes", 0) == "0 takes", "a counted text the language lacks falls back to English and its rule");

        Loc.Install(broken, "xx");
        yield return (Loc.Plural("test.tracks", 0) == "0 piste" && Loc.Plural("test.tracks", 1) == "1 piste" && Loc.Plural("test.tracks", 2) == "2 pistes",
            "a one_is_0_1 language counts 0 and 1 as one");
        yield return (Loc.Plural("test.clips", 1) == "1 clips FR", "a counted text with no singular form uses its other form");
        yield return (Loc.F("test.bad", 1) == "Bad 1" && Loc.F("test.delete_clips", 2) == "Delete 2 clips?",
            "an entry with the wrong placeholders shows English, not an exception");
        yield return (Loc.T("test.save") == "Save" && Loc.T("test.blank") == "Blank", "an entry of the wrong shape, or empty, shows English");
        yield return (Loc.F("test.brace", 7) == "Accolade {x} 7", "{{ }} are literal braces");

        // Numbers follow the thread culture, as the interpolated strings they replace did; the
        // language never changes that culture.
        var before = CultureInfo.CurrentCulture;
        string? notation = null;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            notation = Loc.F("test.number", 1.5) + "|" + Loc.Plural("test.bars", 2);
        }
        catch (CultureNotFoundException) { notation = "1,5|2 bars"; }
        finally { CultureInfo.CurrentCulture = before; }
        yield return (notation == "1,5|2 bars", "numbers format with the thread culture, which the language leaves alone");
        Loc.Install(xx, "xx");
        yield return (before.Name == CultureInfo.CurrentCulture.Name, "installing a language leaves the thread culture alone");

        // English running ignores any table passed in.
        Loc.Install(xx, "en");
        yield return (Loc.T("test.save") == "Save" && Loc.Plural("test.tracks", 1) == "1 track", "English running: a table passed in is not used");

        // The real English reference and language files, from disk.
        Loc.ResetForTests();
        var real = Loc.English;
        yield return (Loc.EnglishError is null && real.Count > 1000 && real.Code == "en" && real.Problems.Count == 0,
            Loc.EnglishError is null ? $"the embedded English reference is valid ({real.Count} keys, key set {real.KeySetHash()})" : "the embedded English reference: " + Loc.EnglishError);
        var onDisk = TranslationTable.Parse(File.ReadAllText(Path.Combine(root, "src/managed/Nota.Application/Localization/en.json")), out _);
        yield return (onDisk is not null && onDisk.KeySetHash() == real.KeySetHash(), "the embedded English is the en.json in the source");
        yield return (real.Name == "English", "English is offered by its own name");

        Loc.ResetForTests();
        Loc.Install(null, "en");   // as the app leaves it: English installed
    }

    // ---- The source against the translation ------------------------------------------------

    private const string LocLit = @"""(?:[^""\\\n]|\\.)*""";
    // The keyed calls (spec 077): Loc.T("key"), Loc.F("key", …), Loc.Plural("key", n).
    // The key must be the whole argument: ")" after a T key, "," (or ")") after an F or Plural
    // key — so Loc.T("common.cancel" + suffix) is a computed key, not "common.cancel".
    private static readonly Regex LocKeyed = new($@"\bLoc\.(?<k>T|F|Plural)\(\s*(?<t>{LocLit})\s*(?<end>[,)])");
    private static readonly Regex LocAnyCall = new(@"\bLoc\.(?:T|F|Plural)\(");
    // The XAML form: Header="{app:T 'main.save_as'}" (quotes optional).
    private static readonly Regex LocXaml = new(@"\{app:T\s+(?:'(?<t>[^']*)'|(?<t>[^\s'},]+))\s*\}");
    private static readonly Regex LocXamlAny = new(@"\{app:T\b");

    // FormatArg: the call is the pattern of a string.Format(culture, Loc.T(key), …) — a text with
    // placeholders formatted in a culture of the caller's choosing (NotaNum.Culture), not Loc.F's.
    private readonly record struct LocKey(string Key, char Kind, string Path, int Line, bool FormatArg = false);
    private static readonly Regex FormatOpen = new(@"\bstring\.Format\([^;()]*,\s*$");

    /// <summary>Every Loc.T / F / Plural call in src/managed and every {app:T …} in XAML, with its
    /// key, plus the calls whose key is not a plain literal (interpolated or computed), which the
    /// source cannot be checked against.</summary>
    private static (List<LocKey> Keys, List<Hit> NotLiteral) LocCalls(string root)
    {
        var managed = Path.Combine(root, "src/managed");
        var keys = new List<LocKey>();
        var bad = new List<Hit>();
        foreach (var f in SourceFiles(managed, ".cs"))
        {
            var rel = RelManaged(root, f);
            if (rel.StartsWith("Nota.Application/Localization/") || rel == "Nota.App/TExtension.cs") continue;   // the API itself
            var text = string.Join('\n', File.ReadLines(f).Select(CodeOf));
            if (!text.Contains("Loc.")) continue;
            ScanLocCalls(rel, text, keys, bad);
        }
        foreach (var f in SourceFiles(managed, ".axaml"))
        {
            var rel = RelManaged(root, f);
            var text = BlankXmlComments(File.ReadAllText(f));
            int LineOf(int index) { int n = 1; for (int i = 0; i < index; i++) if (text[i] == '\n') n++; return n; }
            var literal = new HashSet<int>();
            foreach (Match m in LocXaml.Matches(text))
            {
                literal.Add(m.Index);
                keys.Add(new LocKey(WebUtility.HtmlDecode(m.Groups["t"].Value), 'T', rel, LineOf(m.Index)));
            }
            foreach (Match m in LocXamlAny.Matches(text))
                if (!literal.Contains(m.Index)) bad.Add(new Hit(rel, LineOf(m.Index), text.Substring(m.Index, Math.Min(60, text.Length - m.Index))));
        }
        return (keys, bad);
    }

    /// <summary>The Loc calls of one C# text (comments already removed): keys named by a plain
    /// literal that is the whole key argument go to <paramref name="keys"/>, every other Loc call
    /// to <paramref name="bad"/>.</summary>
    private static void ScanLocCalls(string rel, string text, List<LocKey> keys, List<Hit> bad)
    {
        int LineOf(int index) { int n = 1; for (int i = 0; i < index; i++) if (text[i] == '\n') n++; return n; }
        var literal = new HashSet<int>();
        foreach (Match m in LocKeyed.Matches(text))
        {
            char kind = m.Groups["k"].Value[0];
            bool whole = kind == 'T' ? m.Groups["end"].Value == ")" : kind == 'F' || m.Groups["end"].Value == ",";
            if (!whole) continue;
            literal.Add(m.Index);
            keys.Add(new LocKey(Unescape(m.Groups["t"].Value[1..^1]), kind, rel, LineOf(m.Index),
                FormatOpen.IsMatch(text[Math.Max(0, m.Index - 200)..m.Index])));
        }
        foreach (Match m in LocAnyCall.Matches(text))
            if (!literal.Contains(m.Index)) bad.Add(new Hit(rel, LineOf(m.Index), text.Substring(m.Index, Math.Min(60, text.Length - m.Index))));
    }

    public static IEnumerable<(bool Ok, string Label)> RunLocalization()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }

        // -- the source against the English reference (spec 077 FR-006) --
        var (calls, notLiteral) = LocCalls(root);
        yield return (notLiteral.Count == 0,
            notLiteral.Count == 0 ? "every Loc call and {app:T} names its key as a plain literal"
                : "a Loc call whose key is computed or interpolated can never be checked: " + string.Join(", ", notLiteral.Take(8).Select(h => $"{h.Path}:{h.Line}")));

        // The scanner itself (review of #479): a key is only a key when it is the whole argument.
        {
            var k = new List<LocKey>();
            var b = new List<Hit>();
            ScanLocCalls("sample.cs", string.Join('\n',
                "a = Loc.T(\"common.cancel\");",
                "b = Loc.F(\"main.pdc\", ms);",
                "c = Loc.Plural(\"main.clips\", n);",
                "d = Loc.T(\"common.cancel\" + suffix);",
                "e = Loc.F(\"main.pdc\" + suffix, ms);",
                "f = Loc.Plural(\"main.clips\");",
                "g = Loc.T(key);",
                "h = Loc.T( \"common.close\" );"), k, b);
            var found = string.Join(",", k.Select(x => x.Kind + ":" + x.Key));
            var flagged = string.Join(",", b.Select(x => x.Key[..Math.Min(x.Key.Length, 12)]));
            yield return (found == "T:common.cancel,F:main.pdc,P:main.clips,T:common.close" && b.Count == 4,
                $"the key scanner takes only whole literal keys and flags the rest (keys {found}; flagged {b.Count}: {flagged})");
        }

        var badKeys = calls.Where(c => !TranslationTable.IsKey(c.Key)).ToList();
        yield return (badKeys.Count == 0,
            badKeys.Count == 0 ? $"all {calls.Count} Loc calls use well-formed keys (area.segment…, lower-case)"
                : "not a key (an English text, or a malformed key): " + string.Join(", ", badKeys.Take(8).Select(c => $"\"{c.Key}\" at {c.Path}:{c.Line}")));

        var enPath = Path.Combine(root, "src/managed/Nota.Application/Localization/en.json");
        var en = TranslationTable.Parse(File.ReadAllText(enPath), out var enError);
        yield return (en is not null && en.Problems.Count == 0,
            en is null ? "en.json: " + enError : en.Problems.Count == 0 ? $"en.json parsed ({en.Count} keys)"
                : "en.json entries that cannot be used: " + string.Join("; ", en.Problems.Take(6).Select(p => $"{p.Key} — {p.Problem}")));
        if (en is null) yield break;

        var unformattable = en.Keys.Order(StringComparer.Ordinal)
            .Select(k => (k, en.TryGet(k, out var t) ? TranslationTable.PatternProblem(t.Other) ?? (t.One is null ? null : TranslationTable.PatternProblem(t.One)) : null))
            .Where(x => x.Item2 is not null).ToList();
        yield return (unformattable.Count == 0,
            unformattable.Count == 0 ? "every English text formats (braces and placeholders 0–9)"
                : "English texts that cannot be formatted: " + string.Join("; ", unformattable.Take(6).Select(x => $"{x.k} — {x.Item2}")));

        var used = new Dictionary<string, LocKey>(StringComparer.Ordinal);
        foreach (var c in calls) if (!used.ContainsKey(c.Key)) used[c.Key] = c;
        var missing = calls.Where(c => !en.TryGet(c.Key, out _)).ToList();
        yield return (missing.Count == 0,
            missing.Count == 0 ? $"every key used in the source is in en.json ({used.Count} keys)"
                : $"{missing.Count} keys used in the source are not in en.json (add them): " + string.Join(", ", missing.Take(8).Select(c => $"\"{c.Key}\" at {c.Path}:{c.Line}")));
        var unused = en.Keys.Where(k => !used.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        yield return (unused.Count == 0,
            unused.Count == 0 ? "every key in en.json is used by the source"
                : $"en.json keys nothing uses (delete them): {string.Join(", ", unused.Take(8))}" + (unused.Count > 8 ? $" (+{unused.Count - 8} more)" : ""));

        // Every call, not one per key: a text counted in one place and plain in another is a bug.
        var shape = new List<string>();
        foreach (var c in calls)
        {
            if (!en.TryGet(c.Key, out var e)) continue;
            if ((c.Kind == 'P') != e.IsPlural)
                shape.Add($"\"{c.Key}\" at {c.Path}:{c.Line} ({(c.Kind == 'P' ? "Loc.Plural, but en.json has a plain text" : "a plain text call, but en.json counts it")})");
            else if (c.Kind == 'T' && !c.FormatArg && TranslationTable.Placeholders(e.Other).Count > 0)
                shape.Add($"\"{c.Key}\" at {c.Path}:{c.Line} (Loc.T of a text with placeholders — use Loc.F)");
            else if (c.Kind == 'P' && !TranslationTable.Placeholders(e.Other).IsSubsetOf(new HashSet<int> { 0 }))
                shape.Add($"\"{c.Key}\" at {c.Path}:{c.Line} (a counted text has only {{0}}, the count)");
        }
        yield return (shape.Count == 0,
            shape.Count == 0 ? "each key is used the way en.json shapes it (counted, formatted or plain)"
                : "key used against its shape: " + string.Join("; ", shape.Take(6)));

        // -- Nota ships English only: every other language comes from nota-translations (spec 077) --
        var embedded = Directory.EnumerateFiles(Path.Combine(root, "src/managed/Nota.App/Languages"), "*.json").Select(Path.GetFileName).ToList();
        var appProject = File.ReadAllText(Path.Combine(root, "src/managed/Nota.App/Nota.App.csproj"));
        yield return (embedded.Count == 0 && !appProject.Contains("Languages\\"),
            embedded.Count == 0 ? "the app embeds no translation: English is the only built-in language (SC-008)"
                : "translation files in the app (they belong in nota-translations): " + string.Join(", ", embedded));
        // NOTA_LOC_DUMP_MISSING=<file> writes every key with its English (for translators) as JSON.
        if (Environment.GetEnvironmentVariable("NOTA_LOC_DUMP_MISSING") is { Length: > 0 } dump)
            File.WriteAllText(dump, JsonSerializer.Serialize(en.Keys.Order(StringComparer.Ordinal).Select(k => new { key = k, english = en.TryGet(k, out var t) ? t.Other : "", plural = t.IsPlural, file = used.GetValueOrDefault(k).Path })));

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
        // English, plus the languages nota-translations offers today: macOS follows only listed ones.
        var shipped = new List<string> { "en", "fr" };
        var unlisted = new List<string>();
        foreach (var script in new[] { "scripts/bundle-mac.sh", "scripts/package-dmg.sh" })
        {
            var plist = File.ReadAllLines(Path.Combine(root, script)).FirstOrDefault(l => l.Contains("CFBundleLocalizations")) ?? "";
            foreach (var code in shipped.Where(c => !plist.Contains($"<string>{c}</string>"))) unlisted.Add($"{script}: {code}");
        }
        yield return (unlisted.Count == 0,
            unlisted.Count == 0 ? "the macOS Info.plist lists English and the published languages (CFBundleLocalizations)"
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
