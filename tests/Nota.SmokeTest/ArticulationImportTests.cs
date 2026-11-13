// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Articulation map importers (specs/085) on small maps written here: a Cubase expression map and a
// Studio One keyswitch list in the formats' own shape, plus malformed and hostile input. No vendor
// file is in the repo (they are third-party works), so a real library's map is checked by hand
// (quickstart A, B). Pure, except the library round trip onto a track.
// Runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --articulations`.

using System.Security.Cryptography;
using System.Text;
using Nota.Application.Articulations;

namespace Nota.SmokeTest;

internal static class ArticulationImportTests
{
    private static string Describe(IEnumerable<MidiAction> acts) =>
        string.Join(" ", acts.Select(a => $"{a.Kind}:{a.Data1}/{a.Data2}"));

    private static ArticulationImportResult FromText(Func<Stream, string, ArticulationImportResult> import, string xml) =>
        import(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "test");

    // A Cubase sound slot: its MIDI output events (status, data1, data2) and its name.
    private static string Slot(string name, params (int Status, int D1, int D2)[] events) =>
        "<obj class=\"PSoundSlot\"><obj class=\"PSlotMidiAction\" name=\"action\"><member name=\"midiMessages\"><list name=\"obj\" type=\"obj\">"
        + string.Concat(events.Select(e => $"<obj class=\"POutputEvent\"><int name=\"status\" value=\"{e.Status}\"/><int name=\"data1\" value=\"{e.D1}\"/><int name=\"data2\" value=\"{e.D2}\"/></obj>"))
        + $"</list></member></obj><member name=\"name\"><string name=\"s\" value=\"{name}\"/></member></obj>";

    private static string ExpressionMap(params string[] slots) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><InstrumentMap><string name=\"name\" value=\"Test Map\"/>"
        + "<member name=\"slots\"><list name=\"obj\" type=\"obj\">" + string.Concat(slots) + "</list></member></InstrumentMap>";

    // Two articulations: Long (two keyswitches and a CC, in that order) and Pizz (a velocity-coded keyswitch).
    private static readonly string TestExpressionMap = ExpressionMap(
        Slot("Long", (144, 24, 1), (144, 35, 1), (176, 58, 81)),
        Slot("Pizz", (144, 30, 127)));

    private const string TestKeySwitchList =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Music.KeySwitchList name=\"Test List\">"
        + "<Attributes name=\"Long\" id=\"1\" activation=\"note24.1|note35.1|unknown\"/>"
        + "<Attributes name=\"Pizz\" id=\"2\" activation=\"note30\"/></Music.KeySwitchList>";

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-art-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string cubasePath = Path.Combine(dir, "test.expressionmap"), ksPath = Path.Combine(dir, "test.keyswitch");
        File.WriteAllText(cubasePath, TestExpressionMap);
        File.WriteAllText(ksPath, TestKeySwitchList);
        try
        {
            // ---- content sniffing, not extensions -------------------------------------------------
            yield return (ExpressionMapImporter.Sniff(cubasePath), "articulations: a Cubase map is recognised by its content");
            yield return (!ExpressionMapImporter.Sniff(ksPath), "articulations: a keyswitch list is not taken for an expression map");
            yield return (KeySwitchListImporter.Sniff(ksPath), "articulations: a keyswitch list is recognised by its content");
            yield return (!KeySwitchListImporter.Sniff(cubasePath), "articulations: an expression map is not taken for a keyswitch list");

            // ---- Cubase expression map ----------------------------------------------------------
            byte[] before = SHA256.HashData(File.ReadAllBytes(cubasePath));
            var cub = ExpressionMapImporter.ImportFile(cubasePath);
            byte[] after = SHA256.HashData(File.ReadAllBytes(cubasePath));
            yield return (before.AsSpan().SequenceEqual(after), "articulations: importing never modifies the source file");
            var longArt = cub.Map?.Articulations.FirstOrDefault(a => a.Name == "Long");
            yield return (cub.Map?.Articulations.Count == 2 && longArt is not null
                          && Describe(longArt.Actions) == "Keyswitch:24/1 Keyswitch:35/1 ControlChange:58/81" && longArt.Actions.All(a => a.Channel is null),
                $"articulations: a Cubase map imports its slots, actions in file order on the note's channel ({(longArt is null ? "none" : Describe(longArt.Actions))})");
            yield return (cub.Map?.Articulations.All(a => a.Trigger == TriggerMode.Latching) == true
                          && cub.Messages.Any(m => m.Text.Contains("Switch mode is not stored")),
                "articulations: switch mode is reported as not in the file, defaulting to Latching");

            // ---- Studio One keyswitch list ----------------------------------------------------
            var ks = KeySwitchListImporter.ImportFile(ksPath);
            var ksLong = ks.Map?.Articulations.FirstOrDefault(a => a.Name == "Long");
            yield return (ks.Map?.Articulations.Count == 2 && ksLong?.Id == 1 && ksLong is not null && Describe(ksLong.Actions) == "Keyswitch:24/1 Keyswitch:35/1"
                          && Describe(ks.Map.Articulations[1].Actions) == "Keyswitch:30/127",
                "articulations: a keyswitch list keeps its ids; noteP.V is velocity V, noteP is velocity 127");
            yield return (ks.Warnings.Any(m => m.Text.Contains("\"unknown\"")) && ksLong?.Metadata.ContainsKey("source.activation") == true,
                "articulations: the action the keyswitch format cannot describe is reported, and the activation string kept");
            yield return (cub.Map is { } cm && ks.Map is { } km && km.Articulations.All(a =>
                              cm.Articulations.FirstOrDefault(c => c.Name == a.Name) is { } twin
                              && Describe(a.Actions) == Describe(twin.Actions.Where(x => x.Kind == MidiActionKind.Keyswitch))),
                "articulations: the same articulations read from both formats send the same keyswitches");

            // ---- malformed and hostile input ------------------------------------------------------
            var broken = FromText(ExpressionMapImporter.Import, "<InstrumentMap><member name=\"slots\">");
            yield return (!broken.Succeeded && broken.Messages.Any(m => m.Severity == ImportSeverity.Error && m.Text.Contains("XML error")),
                "articulations: truncated XML is an error, not a crash");
            var wrongRoot = FromText(ExpressionMapImporter.Import, "<?xml version=\"1.0\"?><Something/>");
            yield return (!wrongRoot.Succeeded && wrongRoot.Messages.Any(m => m.Text.Contains("<InstrumentMap>")),
                "articulations: a different XML document is rejected by its root");
            var noSlots = FromText(ExpressionMapImporter.Import, "<InstrumentMap><string name=\"name\" value=\"X\"/></InstrumentMap>");
            yield return (!noSlots.Succeeded && noSlots.Messages.Any(m => m.Text.Contains("no sound slots")),
                "articulations: a map with no slots is rejected");
            var badEvent = FromText(ExpressionMapImporter.Import, ExpressionMap(Slot("Odd", (224, 0, 64), (144, 300, 1), (192, 5, 0))));
            var odd = badEvent.Map?.Articulations.FirstOrDefault();
            yield return (odd is not null && Describe(odd.Actions) == "ProgramChange:5/0"
                          && badEvent.Warnings.Any(m => m.Text.Contains("unsupported"))
                          && badEvent.Warnings.Any(m => m.Text.Contains("out of range")),
                "articulations: unsupported and out-of-range messages are reported; valid ones (program change) are kept");
            var dtd = FromText(ExpressionMapImporter.Import,
                "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;\">]><InstrumentMap>&lol2;</InstrumentMap>");
            yield return (!dtd.Succeeded, "articulations: a document type declaration (entity expansion) is refused");
            var ksBad = FromText(KeySwitchListImporter.Import,
                "<Music.KeySwitchList name=\"T\"><Attributes name=\"A\" id=\"1\" activation=\"note200.1|cc12|note60\"/></Music.KeySwitchList>");
            var ksA = ksBad.Map?.Articulations.FirstOrDefault();
            yield return (ksA is not null && Describe(ksA.Actions) == "Keyswitch:60/127" && ksBad.Warnings.Count() >= 2,
                "articulations: bad keyswitch tokens are reported and skipped, the good one is kept");
            var empty = FromText(KeySwitchListImporter.Import, "");
            yield return (!empty.Succeeded, "articulations: an empty file is an error, not a crash");
            var missing = ExpressionMapImporter.ImportFile(Path.Combine(dir, "does-not-exist.expressionmap"));
            yield return (!missing.Succeeded && missing.Messages.Any(m => m.Text.Contains("not found")), "articulations: a missing file is reported");

            foreach (var r in Dispatch()) yield return r;
            foreach (var r in Library(cubasePath)) yield return r;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // T028: the importer is picked by content, whatever the file is named.
    private static IEnumerable<(bool ok, string label)> Dispatch()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-art-dispatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cubaseAsTxt = Path.Combine(dir, "violins.txt");
            string ksAsExpr = Path.Combine(dir, "strings.expressionmap");
            string notaAsKs = Path.Combine(dir, "mine.keyswitch");
            string fakeExpr = Path.Combine(dir, "fake.expressionmap");
            string soundVar = Path.Combine(dir, "strings.soundvariation");
            File.WriteAllText(cubaseAsTxt, TestExpressionMap);
            File.WriteAllText(ksAsExpr, TestKeySwitchList);
            File.WriteAllText(notaAsKs, ArticulationMapFile.Write(ArticulationFileTests.Full()));
            File.WriteAllText(fakeExpr, "just some text, not a map");
            File.WriteAllText(soundVar, "<?xml version=\"1.0\"?><SoundVariation/>");

            yield return (ArticulationImport.Detect(cubaseAsTxt) == ArticulationFileKind.CubaseExpressionMap
                          && ArticulationImport.ImportFile(cubaseAsTxt).Map?.Articulations.Count == 2,
                "articulations: a Cubase map renamed to .txt is still recognised and imported");
            yield return (ArticulationImport.Detect(ksAsExpr) == ArticulationFileKind.StudioOneKeySwitchList
                          && ArticulationImport.ImportFile(ksAsExpr).Map?.Articulations.Count == 2,
                "articulations: a keyswitch list named .expressionmap goes to the keyswitch importer");
            var nota = ArticulationImport.ImportFile(notaAsKs);
            yield return (ArticulationImport.Detect(notaAsKs) == ArticulationFileKind.NotaMap && nota.Map?.Name == "Test Strings",
                "articulations: a Nota map named .keyswitch is read as a Nota map");
            var fake = ArticulationImport.ImportFile(fakeExpr);
            yield return (!fake.Succeeded && fake.Messages.Any(m => m.Severity == ImportSeverity.Error),
                "articulations: a text file with a map's extension is refused");
            var sv = ArticulationImport.ImportFile(soundVar);
            yield return (!sv.Succeeded && sv.Messages.Any(m => m.Text.Contains(".soundvariation")),
                "articulations: a .soundvariation file is refused with a message saying it isn't supported yet");
            yield return (!ArticulationImport.ImportFile(Path.Combine(dir, "nope.expressionmap")).Succeeded,
                "articulations: a missing file is refused through the facade too");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // T029 + T034: the library store, and an imported map onto a track and back.
    private static IEnumerable<(bool ok, string label)> Library(string cubasePath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-art-library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lib = new Nota.Infrastructure.Articulations.ArticulationLibraryStore(dir);
            yield return (lib.List().Count == 0, "articulations: an empty library lists nothing (no folder yet)");

            var imported = ArticulationImport.ImportFile(cubasePath).Map!;
            var full = ArticulationFileTests.Full();
            lib.Save(imported);
            lib.Save(full);
            var listed = lib.List();
            yield return (listed.Count == 2 && listed.Select(m => m.Name).SequenceEqual(new[] { "Test Map", "Test Strings" }),
                "articulations: saved maps are listed by name");
            var got = lib.Get(imported.Id);
            yield return (got is not null && ArticulationMapFile.Write(got) == ArticulationMapFile.Write(imported),
                "articulations: a map read back from the library equals the one saved");

            full.Name = "Renamed";
            lib.Save(full);
            yield return (lib.List().Count == 2 && lib.Get(full.Id)?.Name == "Renamed"
                          && Directory.GetFiles(dir, "*.notamap").Length == 2 && !Directory.GetFiles(dir, "*.tmp").Any(),
                "articulations: saving the same map again replaces it in place (file named by id, no temp file left)");

            File.WriteAllText(Path.Combine(dir, "broken.notamap"), "{ not a map");
            var withBroken = lib.List();
            yield return (withBroken.Count == 2 && lib.Unreadable.Count == 1, "articulations: an unreadable file is skipped and reported, not thrown");

            bool refusedPath;
            try { lib.Save(new ArticulationMap { Id = "../escape", Name = "x" }); refusedPath = false; }
            catch (ArgumentException) { refusedPath = true; }
            yield return (refusedPath && !File.Exists(Path.Combine(Path.GetDirectoryName(dir)!, "escape.notamap")) && lib.Get("../escape") is null,
                "articulations: a map id that is a path is refused (nothing written outside the folder)");

            yield return (lib.Delete(full.Id) && lib.Get(full.Id) is null && !lib.Delete(full.Id),
                "articulations: deleting a map removes it; deleting it again reports nothing to delete");

            // Import → library → track → read back, through the real engine.
            using var e = new Nota.Infrastructure.NotaEngine();
            int t = e.AddInstrumentTrack();
            var fromLib = lib.Get(imported.Id)!;
            yield return (ArticulationMapAssignment.Assign(e, t, fromLib)
                          && ArticulationMapAssignment.Read(e, t) is { } onTrack
                          && ArticulationMapFile.Write(onTrack) == ArticulationMapFile.Write(imported),
                "articulations: an imported map goes from the library onto a track and reads back equal");
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
