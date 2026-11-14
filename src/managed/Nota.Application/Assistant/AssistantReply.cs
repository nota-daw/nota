// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Assistant's reply from the manual (specs/098, contracts/ui.md § Reply): fixed phrases and
// quotes only — nothing is written that the manual doesn't say. Up to three sections, each
// shortened at a paragraph or sentence end, with a link to the page and Show me for a how-to.
// Links use two private schemes the panel resolves: nota-manual:<lang>/<page>#<slug> (the page
// in the browser) and nota:showme/<n> (walkthrough n of this message).

using System.Text;
using Nota.Application.Localization;

namespace Nota.Application.Assistant;

/// <summary>Meaning search, when its model is installed: the question's vector and the
/// sections' vectors, or nulls (words-only).</summary>
public interface IMeaningSearch
{
    Task<(float[]? Query, SectionVectorSet? Sections)> ForQuestionAsync(string question, ManualIndex index, CancellationToken ct);
}

public static class AssistantReply
{
    public const int MaxSections = 3;
    public const int QuoteLength = 600;

    public static string ManualLink(ManualSection s) => $"nota-manual:{s.Lang}/{s.Page}" + (s.Slug.Length > 0 ? "#" + s.Slug : "");
    public static string ManualHome(string lang) => $"nota-manual:{lang}/";
    public static string ShowMeLink(int n) => $"nota:showme/{n}";

    /// <summary>The reply for a search result.</summary>
    public static ChatMessage From(SearchResult result)
    {
        var picked = result.Hits.Where(ManualSearch.IsAnswer).Take(MaxSections).ToList();
        if (picked.Count == 0)
            return Text(Loc.F("assistant.nothing_matches", ManualHome(result.Lang)));

        var md = new StringBuilder();
        md.Append(Loc.T("assistant.here_is_what")).Append("\n\n");
        var walks = new List<Walkthrough>();
        foreach (var hit in picked)
        {
            var s = hit.Section;
            md.Append("### ").Append(Escape(s.PageTitle));
            if (s.Heading.Length > 0) md.Append(" › ").Append(Escape(s.Heading));
            md.Append("\n\n").Append(Quote(s.Markdown)).Append("\n\n");
            md.Append('[').Append(Loc.T("assistant.open_in_manual")).Append("](").Append(ManualLink(s)).Append(')');
            if (s.Steps.Count > 0)
            {
                md.Append(" · [").Append(Loc.T("assistant.show_me")).Append("](").Append(ShowMeLink(walks.Count)).Append(')');
                walks.Add(new Walkthrough(s, s.Steps));
            }
            md.Append("\n\n");
        }
        return new ChatMessage(ChatRole.Assistant, md.ToString().TrimEnd() + "\n", picked.Select(h => h.Section).ToList(), walks, DateTime.Now);
    }

    public static ChatMessage Unavailable() => Text(Loc.F("assistant.unavailable", ManualHome("en")));

    private static ChatMessage Text(string markdown) => new(ChatRole.Assistant, markdown, [], [], DateTime.Now);

    private static string Escape(string s) => s.Replace("[", "\\[").Replace("]", "\\]");

    /// <summary>The start of a section: whole blocks while they fit in about
    /// <see cref="QuoteLength"/> characters, else the first block cut at a sentence end, "…"
    /// when anything was left out. A code block or table is never cut in the middle.</summary>
    public static string Quote(string markdown)
    {
        var blocks = Blocks(markdown);
        if (blocks.Count == 0) return "";
        var sb = new StringBuilder();
        int i = 0;
        for (; i < blocks.Count; i++)
        {
            if (sb.Length > 0 && sb.Length + blocks[i].Length > QuoteLength) break;
            if (sb.Length > 0) sb.Append("\n\n");
            if (sb.Length == 0 && blocks[i].Length > QuoteLength && IsProse(blocks[i]))
            {
                sb.Append(CutAtSentence(blocks[i], QuoteLength));
                return sb.ToString();
            }
            sb.Append(blocks[i]);
        }
        if (i < blocks.Count) sb.Append("\n\n…");
        return sb.ToString();
    }

    private static bool IsProse(string block) =>
        !block.StartsWith("```", StringComparison.Ordinal) && !block.StartsWith('|') && !block.StartsWith('>');

    private static string CutAtSentence(string text, int max)
    {
        int best = -1;
        for (int i = 0; i < Math.Min(text.Length - 1, max); i++)
            if (text[i] is '.' or '!' or '?' && text[i + 1] is ' ' or '\n') best = i + 1;
        if (best < max / 3) best = Math.Min(text.Length, max);
        return text[..best].TrimEnd() + " …";
    }

    // Blank-line separated blocks, keeping fenced code blocks whole.
    private static List<string> Blocks(string markdown)
    {
        var blocks = new List<string>();
        var cur = new StringBuilder();
        bool fence = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fence = !fence;
            if (!fence && line.Trim().Length == 0)
            {
                if (cur.Length > 0) { blocks.Add(cur.ToString().TrimEnd()); cur.Clear(); }
                continue;
            }
            if (cur.Length > 0) cur.Append('\n');
            cur.Append(line);
        }
        if (cur.Length > 0) blocks.Add(cur.ToString().TrimEnd());
        return blocks;
    }
}

/// <summary>Answers from the user manual (this feature's responder).</summary>
public sealed class ManualResponder : IAssistantResponder
{
    private readonly Func<Task<ManualSearch?>> _search;
    private readonly IMeaningSearch? _meaning;
    private readonly Func<string> _uiLanguage;

    public ManualResponder(Func<Task<ManualSearch?>> search, IMeaningSearch? meaning, Func<string> uiLanguage)
    {
        _search = search;
        _meaning = meaning;
        _uiLanguage = uiLanguage;
    }

    public async Task<ChatMessage> ReplyAsync(string question, CancellationToken ct = default)
    {
        var search = await _search().ConfigureAwait(false);
        if (search is null || !search.Index.IsAvailable) return AssistantReply.Unavailable();
        float[]? q = null;
        SectionVectorSet? vectors = null;
        if (_meaning is not null)
        {
            try { (q, vectors) = await _meaning.ForQuestionAsync(question, search.Index, ct).ConfigureAwait(false); }
            catch (Exception e) when (e is not OperationCanceledException) { q = null; vectors = null; }
        }
        var result = search.Search(question, _uiLanguage(), queryVector: q, vectors: vectors);
        return AssistantReply.From(result);
    }
}
