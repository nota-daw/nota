// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The articulation map manager's rules (specs/085, US5, T066): ids are never reused, the default
// follows removals, copies are deep, typed MIDI values read as numbers or note names, a map is
// checked before saving, and an edit reaches every track holding the map — as one undo step —
// while a duplicate is left alone. Runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --articulations`.

using Nota.Application.Articulations;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ArticulationManagerTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        // ---- ids ----
        var m = ArticulationFileTests.Full();   // ids 2, 7, 9
        int added = ArticulationMapEdits.Add(m, "Tremolo");
        yield return (added == 10 && m.Articulations[^1].Name == "Tremolo" && m.Articulations[^1].ShortName == "Tremol",
            $"articulations: a new articulation takes the next id after the highest (10) and a short name ({added})");
        ArticulationMapEdits.Remove(m, 10);
        int again = ArticulationMapEdits.Add(m, "Trill");
        yield return (again == 11, $"articulations: a removed id is never handed out again, even when it was the highest ({again})");
        // The memory of removed ids survives saving the map: with 10 and 11 removed, the highest id
        // left is 9, so only the remembered ids keep the next one from being 10.
        ArticulationMapEdits.Remove(m, 11);
        var reread = ArticulationMapFile.Read(ArticulationMapFile.Write(m), "").Map!;
        yield return (ArticulationMapEdits.Add(reread, "Spiccato") == 12, "articulations: removed ids are remembered in the saved map (next is 12, not 10)");

        yield return (ArticulationMapEdits.Remove(m, 2) && m.DefaultArticulationId is null && m.Find(2) is null,
            "articulations: removing the default articulation leaves the map without a default");
        yield return (!ArticulationMapEdits.Remove(m, 999), "articulations: removing an id the map doesn't have changes nothing");

        // ---- order ----
        var o = ArticulationFileTests.Full();
        yield return (ArticulationMapEdits.Move(o, 9, -2) && o.Articulations.Select(a => a.Id).SequenceEqual(new[] { 9, 2, 7 })
                      && !ArticulationMapEdits.Move(o, 9, -1),
            "articulations: articulations move up and down and stop at the ends");
        var pizz = o.Find(7)!;
        yield return (ArticulationMapEdits.MoveAction(pizz, 2, -2) && pizz.Actions[0].Kind == MidiActionKind.ProgramChange
                      && !ArticulationMapEdits.MoveAction(pizz, 5, 1),
            "articulations: actions move in their send order");

        // ---- copies are deep ----
        var c = ArticulationFileTests.Full();
        int copyId = ArticulationMapEdits.Copy(c, 2, "Long copy");
        var copy = c.Find(copyId)!;
        copy.Actions[0].Data1 = 99;
        copy.Metadata["x"] = "y";
        yield return (copyId == 10 && c.Articulations[1].Id == copyId && c.Find(2)!.Actions[0].Data1 == 24 && !c.Find(2)!.Metadata.ContainsKey("x"),
            "articulations: a copied articulation sits after its original and shares nothing with it");
        var dup = c.DuplicateAs("Strings B");
        dup.Articulations[0].Name = "Changed";
        yield return (dup.Id != c.Id && c.Articulations[0].Name == "Long" && dup.Articulations.Select(a => a.Id).SequenceEqual(c.Articulations.Select(a => a.Id)),
            "articulations: a duplicated map gets its own id, keeps the articulation ids, and shares nothing");

        // ---- checks before saving ----
        var bad = ArticulationFileTests.Full();
        bad.Name = " ";
        bad.Articulations[0].Name = "";
        bad.Articulations[1].Actions.Add(new MidiAction { Kind = MidiActionKind.ControlChange, Data1 = 200 });
        bad.DefaultArticulationId = 42;
        var problems = ArticulationMapEdits.Check(bad).Select(p => p.Problem).ToHashSet();
        yield return (problems.SetEquals(new[] { ArticulationMapEdits.Problem.NoName, ArticulationMapEdits.Problem.ArticulationWithoutName,
                          ArticulationMapEdits.Problem.InvalidAction, ArticulationMapEdits.Problem.DefaultMissing })
                      && ArticulationMapEdits.Check(ArticulationFileTests.Full()).Count == 0,
            "articulations: a map is checked before saving (no name, a nameless articulation, an invalid action, a missing default); a good one passes");

        // ---- typed MIDI values ----
        var cases = new (string Text, bool Notes, int? Want)[]
        {
            ("24", true, 24), ("C1", true, 24), ("c#1", true, 25), ("Db1", true, 25), ("C-1", true, 0), ("C\u22121", true, 0),
            ("G9", true, 127), ("128", true, null), ("-1", true, null), ("H2", true, null), ("C1", false, null), ("64", false, 64), ("", true, null),
        };
        var wrong = cases.Where(k => ArticulationMapEdits.ParseMidiValue(k.Text, k.Notes) != k.Want).Select(k => k.Text).ToList();
        yield return (wrong.Count == 0, $"articulations: MIDI values read as numbers 0–127 or, for keyswitches, note names (C1 = 24){(wrong.Count > 0 ? "; wrong: " + string.Join(", ", wrong) : "")}");

        // ---- an edit reaches every track holding the map, as one undo step; a duplicate is left alone ----
        using var e = new NotaEngine();
        int t1 = e.AddInstrumentTrack(), t2 = e.AddInstrumentTrack(), t3 = e.AddInstrumentTrack(), t4 = e.AddInstrumentTrack();
        var shared = ArticulationFileTests.Full();
        var other = shared.DuplicateAs("Copy");
        ArticulationMapAssignment.Assign(e, t1, shared);
        ArticulationMapAssignment.Assign(e, t2, shared);
        ArticulationMapAssignment.Assign(e, t3, other);
        yield return (ArticulationMapUsage.TracksUsing(e, shared.Id).SequenceEqual(new[] { t1, t2 }) && ArticulationMapUsage.TracksUsing(e, other.Id).SequenceEqual(new[] { t3 }),
            "articulations: the tracks using a map are found by its id");
        e.ClearUndoHistory();
        var edited = ArticulationMapFile.Read(ArticulationMapFile.Write(shared), "").Map!;
        edited.Find(2)!.NoteOffsetMs = -180;
        int n = ArticulationMapUsage.ApplyEverywhere(e, edited);
        double OffsetOn(int t) => ArticulationMapAssignment.Read(e, t)!.Find(2)!.NoteOffsetMs;
        yield return (n == 2 && OffsetOn(t1) == -180 && OffsetOn(t2) == -180 && OffsetOn(t3) == -150.5 && ArticulationMapAssignment.Read(e, t4) is null,
            $"articulations: an edit reaches both tracks holding the map ({n}); the duplicate's track and a track without a map are untouched");
        e.Undo();
        yield return (OffsetOn(t1) == -150.5 && OffsetOn(t2) == -150.5 && !e.CanUndo,
            "articulations: one undo takes the edit off every track that got it");

        // ---- the manager's list: the library's maps plus project-only copies ----
        string dir = Path.Combine(Path.GetTempPath(), "nota-art-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lib = new Nota.Infrastructure.Articulations.ArticulationLibraryStore(dir);
            lib.Save(shared);
            var cat = ArticulationMapUsage.Catalog(e, lib);
            yield return (cat.Count == 2 && cat.Single(x => x.Map.Id == shared.Id).OnlyInProject == false
                          && cat.Single(x => x.Map.Id == other.Id).OnlyInProject,
                "articulations: the manager lists library maps once and flags a map only the project holds");

            // The library got a newer version (saved, then undone on the tracks): the manager lists
            // what the tracks play, flagged, so saving an edit can't put the library's version on them.
            var newer = ArticulationMapFile.Read(ArticulationMapFile.Write(shared), "").Map!;
            newer.Find(2)!.NoteOffsetMs = -321;
            lib.Save(newer);
            ArticulationMapUsage.ApplyEverywhere(e, newer);
            e.Undo();
            var cat2 = ArticulationMapUsage.Catalog(e, lib);
            var listed = cat2.Single(x => x.Map.Id == shared.Id);
            yield return (cat2.Count == 2 && listed.DiffersFromLibrary && !listed.OnlyInProject
                          && ArticulationMapFile.Write(listed.Map) == ArticulationMapFile.Write(ArticulationMapAssignment.Read(e, t1)!),
                "articulations: when the project's copy differs from the library's, the manager lists the project's, flagged");
            listed.Map.Name = "Restored project map";
            lib.Save(listed.Map);
            int restored = ArticulationMapUsage.ApplyEverywhere(e, listed.Map);
            yield return (restored == 2 && OffsetOn(t1) == -150.5 && OffsetOn(t2) == -150.5
                          && !ArticulationMapUsage.Catalog(e, lib).Single(x => x.Map.Id == shared.Id).DiffersFromLibrary,
                "articulations: saving a small edit after undo preserves the restored project offsets on every track");

            // Copy readable map files to other filenames, including a differing version of the
            // same ID. Both project-backed maps and maps only in the library must remain usable.
            File.Copy(Path.Combine(dir, shared.Id + ArticulationMapFile.Extension), Path.Combine(dir, "copied.notamap"));
            var onlyInLibrary = shared.DuplicateAs("Library-only map");
            lib.Save(onlyInLibrary);
            var different = ArticulationMapFile.Read(ArticulationMapFile.Write(onlyInLibrary), "").Map!;
            different.Name = "Another copy";
            File.WriteAllText(Path.Combine(dir, "different.notamap"), ArticulationMapFile.Write(different));
            var readable = lib.List();
            var duplicateCatalog = ArticulationMapUsage.Catalog(e, lib);
            yield return (readable.Count == 4 && duplicateCatalog.Count == 3
                          && duplicateCatalog.Count(x => x.Map.Id == shared.Id) == 1
                          && duplicateCatalog.Count(x => x.Map.Id == onlyInLibrary.Id) == 1
                          && OffsetOn(t1) == -150.5 && !duplicateCatalog.Single(x => x.Map.Id == shared.Id).DiffersFromLibrary,
                "articulations: copied map files with duplicate IDs do not block the manager or replace the project's map");
            using var emptyProject = new NotaEngine();
            var libraryCatalog = ArticulationMapUsage.Catalog(emptyProject, lib);
            yield return (libraryCatalog.Count == 2 && libraryCatalog.All(x => !x.OnlyInProject && !x.DiffersFromLibrary)
                          && ArticulationMapFile.Write(libraryCatalog.Single(x => x.Map.Id == onlyInLibrary.Id).Map)
                             == ArticulationMapFile.Write(readable.First(x => x.Id == onlyInLibrary.Id)),
                "articulations: a library-only catalog lists each duplicate ID once and retains the first readable copy");
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } }

        // ---- removing an articulation and saving leaves the notes' ids alone (data-model: ids are not reused) ----
        int t5 = e.AddInstrumentTrack();
        int c5 = e.AddMidiClip(t5, 0, 4);
        e.SetClipNotes(t5, c5, new[] { new NotaNote(60, 0, 1, 0.8f) { ArticulationId = 7 }, new NotaNote(62, 1, 1, 0.8f) { ArticulationId = 9 } });
        ArticulationMapAssignment.Assign(e, t5, shared);
        var pruned = ArticulationMapFile.Read(ArticulationMapFile.Write(shared), "").Map!;
        ArticulationMapEdits.Remove(pruned, 7);
        int took = ArticulationMapUsage.ApplyEverywhere(e, pruned);
        var kept = e.GetClipNotes(t5, c5).Select(x => x.ArticulationId).OrderBy(x => x).ToArray();
        var onTrack = ArticulationMapAssignment.Read(e, t5)!;
        yield return (took == 3 && kept.SequenceEqual(new[] { 7, 9 }) && onTrack.Find(7) is null && ArticulationMapEdits.NextId(onTrack) > 9,
            $"articulations: saving a map without an articulation keeps the notes' ids ({string.Join(",", kept)}) and the id stays retired on the track ({took} tracks)");

        // ---- the manager tells unsaved edits by comparing the written map; a map read back must write the same ----
        // An imported map too: the importers fill fields (metadata, palette indices) Full() doesn't.
        var maps = new List<(string Name, ArticulationMap? Map)>
        {
            ("Nota", ArticulationFileTests.Full()),
            ("Cubase", ExpressionMapImporter.Import(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "<InstrumentMap><string name=\"name\" value=\"M\"/><member name=\"slots\"><list name=\"obj\" type=\"obj\">"
                + "<obj class=\"PSoundSlot\"><obj class=\"PSlotMidiAction\" name=\"action\"><member name=\"midiMessages\"><list name=\"obj\" type=\"obj\">"
                + "<obj class=\"POutputEvent\"><int name=\"status\" value=\"144\"/><int name=\"data1\" value=\"24\"/><int name=\"data2\" value=\"1\"/></obj>"
                + "</list></member></obj><member name=\"name\"><string name=\"s\" value=\"Long\"/></member></obj></list></member></InstrumentMap>")), "m").Map),
            ("Studio One", KeySwitchListImporter.Import(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "<Music.KeySwitchList name=\"K\"><Attributes name=\"Long\" id=\"1\" activation=\"note24.1|unknown\"/></Music.KeySwitchList>")), "k").Map),
        };
        var unstable = maps.Where(x => x.Map is null
            || ArticulationMapFile.Write(ArticulationMapFile.Read(ArticulationMapFile.Write(x.Map), x.Map.Name).Map!) != ArticulationMapFile.Write(x.Map)).Select(x => x.Name).ToList();
        // ---- the manager's effective-offset readout takes the right track offset (T053) ----
        e.SetTrackMidiOffsetMs(t1, -100);
        e.SetTrackMidiOffsetMs(t2, -40);
        e.SetTrackMidiOffsetMs(t5, -40);
        double? onSelected = ArticulationMapUsage.TrackOffsetFor(e, shared.Id, t1);
        double? mixed = ArticulationMapUsage.TrackOffsetFor(e, shared.Id, t3);   // t3 holds another map
        e.SetTrackMidiOffsetMs(t1, -40);
        double? sharedOff = ArticulationMapUsage.TrackOffsetFor(e, shared.Id, 0);
        double? unused = ArticulationMapUsage.TrackOffsetFor(e, "no-such-map", t1);
        yield return (onSelected == -100 && mixed is null && sharedOff == -40 && unused is null,
            $"articulations: the manager's readout uses the selected track's offset when it holds the map ({onSelected}), else the one all its tracks share ({sharedOff}); none when they differ or no track holds it");

        yield return (maps.Count == 3 && unstable.Count == 0,
            $"articulations: a map read back from its file writes the same, so an opened map isn't taken for an edited one ({maps.Count} maps{(unstable.Count > 0 ? "; differ: " + string.Join(", ", unstable) : "")})");
    }
}
