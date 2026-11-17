// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Assistant panel (specs/098): its command and toolbar place, the embedded manual index,
// words-only search, replies, and the Show me anchor checks the build relies on (FR-019,
// FR-019a, FR-020). Headless.

using System.Text.Json;
using Markdig.Syntax;
using Nota.Application.Assistant;
using Nota.Application.Palette;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class AssistantTests
{
    private static ManualSearch? _search;
    private static ManualSearch Search => _search ??= new ManualSearch(ManualIndexSource.ReadEmbedded());

    public static IEnumerable<(bool, string)> Run()
    {
        // ---- command and toolbar -----------------------------------------------------------
        var cmd = CommandCatalog.Create().FirstOrDefault(c => c.Id == "view.assistant");
        yield return (cmd is not null, "assistant: view.assistant is a command");
        yield return (cmd is not null && CommandCatalog.Create().Count(c => c.Gesture == cmd.Gesture) == 1,
            "assistant: its default key is unique");
        int mixer = ToolbarOverflow.ShedOrder.ToList().IndexOf(ToolbarPart.Mixer);
        yield return (mixer >= 0 && ToolbarOverflow.ShedOrder.ElementAtOrDefault(mixer + 1) == ToolbarPart.Assistant,
            "assistant: the toolbar sheds the Assistant key right after the Mixer key");
        int barMixer = ToolbarOverflow.BarOrder.ToList().IndexOf(ToolbarPart.Mixer);
        yield return (ToolbarOverflow.BarOrder.ElementAtOrDefault(barMixer + 1) == ToolbarPart.Assistant,
            "assistant: the ⋯ menu lists the Assistant after the Mixer");

        // ---- the embedded index ------------------------------------------------------------
        var index = Search.Index;
        yield return (index.IsAvailable, "assistant: the embedded manual index reads (" + (index.Unavailable ?? index.DocsCommit) + ")");
        if (!index.IsAvailable) yield break;
        yield return (index.Languages.Contains("en") && index.Languages.Contains("ru"), "assistant: the index has English and Russian");
        yield return (index.Sections.Count >= 1500, $"assistant: the index has the whole manual ({index.Sections.Count} sections)");

        var broken = ManualIndex.Parse("{\"format\":99}"u8.ToArray());
        yield return (!broken.IsAvailable, "assistant: an index of an unknown format is unavailable, not an error");
        yield return (!ManualIndex.Parse("{oops"u8.ToArray()).IsAvailable, "assistant: malformed JSON is unavailable");

        // ---- words-only search (FR-007, FR-010) --------------------------------------------
        foreach (var (q, ui, page) in Probes)
        {
            var r = Search.Search(q, ui, take: 3);
            bool ok = r.Hits.Any(h => h.Section.Page == page && ManualSearch.IsAnswer(h));
            yield return (ok, $"assistant: «{q}» → {page} (got {string.Join(", ", r.Hits.Take(3).Select(h => h.Section.Id))})");
        }
        yield return (Search.AnswerLanguage("как включить метроном", "en") == "ru", "assistant: a Russian question gets the Russian manual");
        yield return (Search.AnswerLanguage("how to loop", "ru") == "en", "assistant: an English question in the Russian UI gets the English manual");
        yield return (Search.AnswerLanguage("comment exporter", "fr") == "fr", "assistant: a Latin-script question in the French UI gets the French manual");

        var none = AssistantReply.From(Search.Search("qwzx florbidity zamboni", "en"));
        yield return (none.Quoted.Count == 0 && none.Markdown.Contains("nota-manual:en/"), "assistant: nothing matches → says so with a link to the manual");

        var reply = AssistantReply.From(Search.Search("how do I export stems", "en"));
        yield return (reply.Quoted.Count is > 0 and <= AssistantReply.MaxSections
                      && reply.Markdown.Contains("](nota-manual:en/"), "assistant: a reply quotes up to three sections with manual links");
        // ---- Show me anchors (FR-019, FR-019a, FR-020 / SC-007) -----------------------------
        foreach (var check in AnchorChecks(index)) yield return check;

        // ---- the meaning model (only with NOTA_E5_DIR: model_quantized.onnx + tokenizer.json) ---
        foreach (var check in ModelChecks()) yield return check;

        // ---- Markdown: the panel's pipeline parses every element the manual uses (FR-012) -----
        foreach (var check in MarkdownChecks()) yield return check;

        string longText = string.Join(" ", Enumerable.Repeat("This sentence is ten words long and ends right here.", 30));
        var cut = AssistantReply.Quote(longText);
        yield return (cut.Length <= AssistantReply.QuoteLength + 4 && cut.EndsWith(" …", StringComparison.Ordinal)
                      && cut.Contains("here."), "assistant: a long section is cut at a sentence end");
    }

    private static IEnumerable<(bool, string)> MarkdownChecks()
    {
        const string sample = "## H\n\nText **b** *i* ~~s~~ `c` <kbd>⌘ S</kbd> [l](nota-manual:en/x) https://a.b\n\n"
            + "1. one\n   - nested\n\n> [!TIP]\n> tip\n\n> quote\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```\ncode\n```\n\n---\n";
        var doc = Markdig.Markdown.Parse(sample, Nota.App.AssistantMarkdown.Pipeline);
        var blocks = doc.Descendants<Markdig.Syntax.Block>().Select(b => b.GetType().Name).ToHashSet();
        var inlines = doc.Descendants<Markdig.Syntax.Inlines.Inline>().ToList();
        string[] wantBlocks = ["HeadingBlock", "ParagraphBlock", "ListBlock", "AlertBlock", "QuoteBlock", "Table", "FencedCodeBlock", "ThematicBreakBlock"];
        var missing = wantBlocks.Where(w => !blocks.Contains(w)).ToList();
        yield return (missing.Count == 0, "assistant: Markdown blocks parse (heading, list, alert, quote, table, code, rule)"
            + (missing.Count == 0 ? "" : ": missing " + string.Join(", ", missing)));
        bool strike = inlines.OfType<Markdig.Syntax.Inlines.EmphasisInline>().Any(e => e.DelimiterChar == '~');
        bool kbd = inlines.OfType<Markdig.Syntax.Inlines.HtmlInline>().Any(h => h.Tag.StartsWith("<kbd", StringComparison.Ordinal));
        bool link = inlines.OfType<Markdig.Syntax.Inlines.LinkInline>().Any(l => l.Url == "nota-manual:en/x");
        bool auto = inlines.OfType<Markdig.Syntax.Inlines.AutolinkInline>().Any() || inlines.OfType<Markdig.Syntax.Inlines.LinkInline>().Any(l => l.Url == "https://a.b");
        yield return (strike && kbd && link && auto, "assistant: Markdown inlines parse (strikethrough, <kbd>, manual link, bare link)");
    }

    private static IEnumerable<(bool, string)> AnchorChecks(ManualIndex index)
    {
        var commands = new HashSet<string>(CommandCatalog.Create().Select(c => c.Id), StringComparer.Ordinal);
        foreach (var a in Nota.Application.Input.KeyActionCatalog.Create()) commands.Add(a.Id);

        var unknown = index.Sections.SelectMany(s => s.Steps.Select(t => (s.Id, t.Anchor)))
            .Where(x => !Nota.Presentation.Assistant.AnchorRegistry.IsKnown(x.Anchor, commands)).ToList();
        yield return (unknown.Count == 0, "assistant: every Show me anchor names a control or a command"
            + (unknown.Count == 0 ? "" : ": " + string.Join(", ", unknown.Take(8).Select(u => $"{u.Id} → {u.Anchor}"))));

        // Every how-to gets anchors (FR-020). Sections not anchored yet are listed in a ratchet
        // file that may only shrink: a new how-to must come with anchors, and the feature is done
        // when the list is empty. NOTA_HOWTO_WRITE_BASELINE=1 rewrites it from the index.
        var bare = index.Sections.Where(s => s.HowTo && !s.Explanatory && s.Steps.Count == 0).Select(s => s.Id).ToList();
        int howTos = index.Sections.Count(s => s.HowTo);
        string? root = RepoRoot();
        string listPath = Path.Combine(root ?? ".", "tests/Nota.SmokeTest/ManualHowToRatchet.txt");
        if (Environment.GetEnvironmentVariable("NOTA_HOWTO_WRITE_BASELINE") == "1")
        {
            File.WriteAllLines(listPath, new[]
            {
                "# SPDX-License-Identifier: AGPL-3.0-only",
                "# Manual how-to sections that have no Show me anchors yet (specs/098 FR-020).",
                "# Ratchet: may only shrink. Anchor a section in nota-docs, rebuild the index, delete its line.",
            }.Concat(bare.OrderBy(b => b, StringComparer.Ordinal)));
        }
        var allowed = File.Exists(listPath)
            ? File.ReadAllLines(listPath).Where(l => l.Length > 0 && l[0] != '#').ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var newBare = bare.Where(b => !allowed.Contains(b)).ToList();
        var stale = allowed.Where(a => !bare.Contains(a)).ToList();
        yield return (newBare.Count == 0, $"assistant: every how-to has Show me steps or is marked explanatory, or is on the ratchet ({howTos - bare.Count}/{howTos} anchored)"
            + (newBare.Count == 0 ? "" : ": " + string.Join(", ", newBare.Take(6)) + (newBare.Count > 6 ? $" (+{newBare.Count - 6} more)" : "")));
        yield return (stale.Count == 0, "assistant: the how-to ratchet lists only sections still without anchors (delete the rest)"
            + (stale.Count == 0 ? "" : ": " + string.Join(", ", stale.Take(6))));

        var drift = index.Sections.Where(s => s.Twin is not null && index.Find(s.Twin) is { } en
            && !en.Steps.Select(t => t.Anchor).SequenceEqual(s.Steps.Select(t => t.Anchor))
            || s.Twin is not null && index.Find(s.Twin) is { } e2 && e2.Explanatory != s.Explanatory).ToList();
        yield return (drift.Count == 0, "assistant: translations carry their English section's anchors, in order"
            + (drift.Count == 0 ? "" : ": " + string.Join(", ", drift.Take(6).Select(d => d.Id))));

        var registryCmds = Nota.Presentation.Assistant.AnchorRegistry.CommandButtons;
        var badCmd = registryCmds.Where(kv => !commands.Contains(kv.Key) || Nota.Presentation.Assistant.AnchorRegistry.Find(kv.Value) is null).ToList();
        yield return (badCmd.Count == 0, "assistant: every command button in the anchor registry is a real command and control"
            + (badCmd.Count == 0 ? "" : ": " + string.Join(", ", badCmd.Select(b => b.Key))));
    }

    private static IEnumerable<(bool, string)> ModelChecks()
    {
        string? dir = Environment.GetEnvironmentVariable("NOTA_E5_DIR");
        string? root = RepoRoot();
        if (dir is null || root is null || !File.Exists(Path.Combine(dir, "tokenizer.json")))
        {
            yield return (true, "assistant: meaning model checks skipped (set NOTA_E5_DIR to a folder with model_quantized.onnx and tokenizer.json)");
            yield break;
        }
        var tok = UnigramTokenizer.Load(Path.Combine(dir, "tokenizer.json"));
        var goldens = JsonSerializer.Deserialize<List<Golden>>(File.ReadAllText(Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/e5-tokenizer-goldens.json")))!;
        foreach (var g in goldens)
        {
            var ids = tok.Encode(g.text);
            yield return (ids.SequenceEqual(g.ids), $"assistant: tokenizer matches the reference for «{g.text}»"
                + (ids.SequenceEqual(g.ids) ? "" : $" (got {string.Join(' ', ids.Take(16))} want {string.Join(' ', g.ids.Take(16))})"));
        }

        string? ort = Environment.GetEnvironmentVariable("NOTA_ORT_LIB") ?? new ModelStore().RuntimePath;
        if (ort is null || !File.Exists(Path.Combine(dir, "model_quantized.onnx")))
        {
            yield return (true, "assistant: embedding check skipped (no ONNX Runtime library or model file)");
            yield break;
        }
        var reference = JsonSerializer.Deserialize<EmbeddingRef>(File.ReadAllText(Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/e5-embedding-reference.json")))!;
        using var emb = new TextEmbedder(ort, Path.Combine(dir, "model_quantized.onnx"), Path.Combine(dir, "tokenizer.json"));
        var q = emb.Embed(reference.query, query: true);
        var p = emb.Embed(reference.passage, query: false);
        var o = emb.Embed(reference.other, query: false);
        double maxDiff = q.Take(8).Zip(reference.query_head).Max(t => Math.Abs(t.First - t.Second));
        yield return (maxDiff < 2e-3, $"assistant: the query vector matches the reference (max diff {maxDiff:E1})");
        double cp = q.Zip(p).Sum(t => t.First * t.Second), co = q.Zip(o).Sum(t => t.First * t.Second);
        yield return (Math.Abs(cp - reference.cos_passage) < 2e-3 && Math.Abs(co - reference.cos_other) < 2e-3 && cp > co,
            $"assistant: cosines match the reference and rank the right passage first ({cp:F3} vs {co:F3})");
    }

    private sealed record Golden(string text, int[] ids);
    private sealed record EmbeddingRef(string query, string passage, string other, double[] query_head, double cos_passage, double cos_other);

    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        return d?.FullName;
    }

    // Question, UI language, a page the answer must come from.
    private static readonly (string Q, string Ui, string Page)[] Probes =
    [
        ("how do I set up a sidechain on the compressor", "en", "devices/audio-effects/compressor"),
        ("sidechian compressor", "en", "devices/audio-effects/compressor"),
        ("how do I export stems", "en", "export/export-audio"),
        ("command palette", "en", "basics/command-palette"),
        ("сщььфтв зфдуееу", "en", "basics/command-palette"),   // "command palette" typed on the Russian layout
        ("как открыть палитру команд", "en", "basics/command-palette"),
        ("метроном", "ru", "basics/transport"),
    ];

    /// <summary>`--assistant-ask [--ui xx] question…` — the hits with their scores.</summary>
    public static void Ask(string[] args)
    {
        string ui = "en";
        if (args.Length >= 2 && args[0] == "--ui") { ui = args[1]; args = args[2..]; }
        string q = string.Join(' ', args);
        var r = Search.Search(q, ui, take: 8);
        Console.WriteLine($"lang {r.Lang}");
        foreach (var h in r.Hits)
            Console.WriteLine($"  {(ManualSearch.IsAnswer(h) ? "✓" : " ")} {h.Score:F4} cov {h.Coverage:F2}  {h.Section.Id}");
        Console.WriteLine();
        Console.WriteLine(AssistantReply.From(r).Markdown);
    }
}
