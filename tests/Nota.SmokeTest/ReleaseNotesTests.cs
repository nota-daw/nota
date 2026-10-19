// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What's New shows a release's curated notes (release-notes/X.Y.Z.en.md, the GitHub
// release text) instead of its raw CHANGELOG.md section. Every shipped notes file must
// parse into a lede and titled sections, and the parser must read the format the release
// skill writes. Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --release-notes`.

using Nota.App;

namespace Nota.SmokeTest;

internal static class ReleaseNotesTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        const string sample = """
            <!-- SPDX-License-Identifier: AGPL-3.0-only -->
            Nota 9.9 is a **big** one.

            **1. First feature**
            What it is and why it matters.

            **2. Second feature**
            One paragraph.
            Another line of it.

            **Also**
            • a small fix with `code`
            • **bold** start, plain end
            """;
        var e = ReleaseNotesParser.Parse("9.9.1", "2026-01-01", sample);
        yield return (e.IsReleaseNotes && e.Lede == "Nota 9.9 is a **big** one.",
            "release notes: the lede is the text before the first heading (SPDX line skipped)");
        yield return (e.Sections.Count == 3
                      && e.Sections[0].Title == "First feature"
                      && e.Sections[1].Title == "Second feature"
                      && e.Sections[2].Title == "Also",
            $"release notes: \"**N. Title**\" lines become sections, number dropped ({string.Join(" | ", e.Sections.Select(s => s.Title))})");
        yield return (e.Sections[1].Text == "One paragraph.\nAnother line of it." && e.Sections[1].Items.Count == 0,
            "release notes: a feature's paragraph is its text");
        yield return (e.Sections[2].Items.SequenceEqual(new[] { "a small fix with `code`", "**bold** start, plain end" }),
            "release notes: \"•\" lines become bullets, and a bold-led bullet isn't a heading");

        var root = RepoRoot();
        yield return (root is not null, "release notes: repo root found");
        if (root is null) yield break;

        var changelog = ChangelogParser.Parse(File.ReadAllText(Path.Combine(root, "CHANGELOG.md")));
        var files = Directory.EnumerateFiles(Path.Combine(root, "release-notes"), "*.en.md").OrderBy(f => f).ToList();
        yield return (files.Count > 0, $"release notes: shipped EN notes found ({files.Count})");
        foreach (var f in files)
        {
            var version = Path.GetFileName(f)[..^".en.md".Length];
            var notes = ReleaseNotesParser.Parse(version, "", File.ReadAllText(f));
            yield return (notes.IsReleaseNotes && notes.Sections.Count > 0
                          && notes.Sections.All(s => s.Title.Length > 0 && (s.Text.Length > 0 || s.Items.Count > 0)),
                $"release notes {version}: parses into a lede and {notes.Sections.Count} titled sections");
            yield return (changelog.Any(c => c.Version == version),
                $"release notes {version}: CHANGELOG.md has its section (What's New pairs them by version)");
        }

        var csproj = File.ReadAllText(Path.Combine(root, "src/managed/Nota.App/Nota.App.csproj"));
        yield return (csproj.Contains(@"release-notes\*.en.md") && csproj.Contains("Assets/ReleaseNotes/"),
            "release notes: Nota.App embeds release-notes/*.en.md where AppInfo reads them");
    }

    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        return d?.FullName;
    }
}
