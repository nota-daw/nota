// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Automatic section names (specs/058-section-automatic-names): an empty name is automatic and
// reads "Section N" for its position, custom names counted; typed names are trimmed and a blank
// one is automatic; a reset is one step whose undo brings the custom name back; the mode
// survives save / load; MCP lists the shown name and the mode and round-trips them.
// Runs with `dotnet run --project tests/Nota.SmokeTest -- --paramundo` and the full run.

using Nota.Application;
using Nota.Infrastructure;
using McpSection = Nota.Mcp.Tools.ProjectTools.Section;

namespace Nota.SmokeTest;

internal static class SectionNamingTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Rules()) yield return r;
        foreach (var r in ResetUndo()) yield return r;
        foreach (var r in Files()) yield return r;
        foreach (var r in Mcp()) yield return r;
    }

    private static IEnumerable<(bool, string)> Rules()
    {
        var list = new[] { new SectionInfo(0, 8, ""), new SectionInfo(8, 8, "Chorus"), new SectionInfo(16, 8, "") };
        yield return (SectionNames.Displayed(list).SequenceEqual(new[] { "Section 1", "Chorus", "Section 3" }),
            "section names: automatic ones count custom-named sections too (Section 1 · Chorus · Section 3)");

        var inserted = new[] { new SectionInfo(0, 4, "") }.Concat(list.Select(s => s with { StartBeat = s.StartBeat + 4 })).ToArray();
        yield return (SectionNames.Displayed(inserted).SequenceEqual(new[] { "Section 1", "Section 2", "Chorus", "Section 4" }),
            "section names: a section added in front renumbers the automatic ones, the custom one stays");

        yield return (SectionNames.Normalize("  Drop ") == "Drop" && SectionNames.Normalize("   ") == "" && SectionNames.Normalize(null) == ""
                      && SectionNames.IsAutomatic("  ") && !SectionNames.IsAutomatic("Section 3"),
            "section names: typed names are trimmed, a blank one is automatic, typed \"Section 3\" is custom");
    }

    private static IEnumerable<(bool, string)> ResetUndo()
    {
        using var eng = new NotaEngine();
        eng.SetSections(new[] { new SectionInfo(0, 8, ""), new SectionInfo(8, 8, "Chorus") }, checkpoint: false);
        eng.ClearUndoHistory();

        eng.SetSections(new[] { new SectionInfo(0, 8, ""), new SectionInfo(8, 8, "") });
        bool reset = SectionNames.Displayed(eng.Sections)[1] == "Section 2" && eng.CanUndo;
        eng.Undo();
        bool back = eng.Sections[1].Name == "Chorus" && !eng.CanUndo;
        eng.Redo();
        yield return (reset && back && SectionNames.IsAutomatic(eng.Sections[1].Name),
            "section names: a reset is one step; undo restores the custom name, redo the automatic one");

        eng.ClearUndoHistory();
        eng.SetSections(new[] { new SectionInfo(0, 8, ""), new SectionInfo(8, 8, "") });
        yield return (!eng.CanUndo, "section names: resetting an automatic section records no step");
    }

    private static IEnumerable<(bool, string)> Files()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-section-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var eng = new NotaEngine();
            ProjectTemplate.ApplyDefault(eng);
            var want = new[] { new SectionInfo(0, 16, ""), new SectionInfo(16, 16, "Verse"), new SectionInfo(32, 16, "") };
            eng.SetSections(want);
            new ProjectStore().Save(eng, new TransportState(120, 1, false, false), dir);
            using var e2 = new NotaEngine();
            new ProjectStore().Load(e2, dir);
            yield return (e2.Sections.SequenceEqual(want)
                          && SectionNames.Displayed(e2.Sections).SequenceEqual(new[] { "Section 1", "Verse", "Section 3" }),
                "section names: automatic and custom names survive save / load");
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    private static IEnumerable<(bool, string)> Mcp()
    {
        using var eng = new NotaEngine();
        var proj = new Nota.Mcp.Tools.ProjectTools(eng, new SyncDispatch(), new NoRefresh());
        eng.ClearUndoHistory();
        var stored = proj.SetSections(new[]
        {
            new McpSection(16, 8, "Drop"),
            new McpSection(0, 8, "ignored", Automatic: true),
            new McpSection(24, 8, "  "),
        }).GetAwaiter().GetResult();
        bool listed = stored.SequenceEqual(new[]
        {
            new McpSection(0, 8, "Section 1", true),
            new McpSection(16, 8, "Drop", false),
            new McpSection(24, 8, "Section 3", true),
        });
        bool raw = eng.Sections.Select(s => s.Name).SequenceEqual(new[] { "", "Drop", "" });

        proj.SetSections(proj.ListSections().GetAwaiter().GetResult()).GetAwaiter().GetResult();
        bool roundTrip = eng.Sections.Select(s => s.Name).SequenceEqual(new[] { "", "Drop", "" });
        eng.Undo();
        yield return (listed && raw && roundTrip && eng.Sections.Count == 0 && !eng.CanUndo,
            "section names: MCP lists shown names with the automatic flag and a list → set round-trip changes nothing");

        // A legacy custom name can carry surrounding spaces. Listing it and setting that list
        // back must not trim it, and must not record a step.
        eng.SetSections(new[] { new SectionInfo(0, 8, " Chorus ") }, checkpoint: false);
        eng.ClearUndoHistory();
        proj.SetSections(proj.ListSections().GetAwaiter().GetResult()).GetAwaiter().GetResult();
        yield return (!eng.CanUndo && eng.Sections.Count == 1 && eng.Sections[0].Name == " Chorus ",
            "section names: MCP list → set keeps a padded custom name and records no step");
    }
}
