// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota's articulation map file and the table compiler (specs/085,
// contracts/nota-articulation-map-file.md, contracts/c-abi.md): a map survives writing and reading
// unchanged; what a damaged file gets wrong is repaired and reported, or the file is refused; the
// compiler hands the engine only what it should schedule. Pure: no engine.
// Runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --articulations`.

using System.Text;
using Nota.Application.Articulations;

namespace Nota.SmokeTest;

internal static class ArticulationFileTests
{
    /// <summary>A map using every field the format has.</summary>
    internal static ArticulationMap Full() => new()
    {
        Id = "0123456789abcdef0123456789abcdef",
        Name = "Test Strings",
        InstrumentName = "Test Library",
        Description = "every field",
        DefaultArticulationId = 2,
        Groups = { new ArticulationGroup { Id = 1, Name = "Bowing" }, new ArticulationGroup { Id = 2, Name = "" } },
        Articulations =
        {
            new Articulation
            {
                Id = 2, Name = "Long", ShortName = "Lng", ColorArgb = 0xFF336699, SourceColorIndex = 3, GroupId = 1,
                Actions = { new MidiAction { Kind = MidiActionKind.Keyswitch, Data1 = 24, Data2 = 1 },
                            new MidiAction { Kind = MidiActionKind.ControlChange, Channel = 5, Data1 = 58, Data2 = 81 } },
                NoteOffsetMs = -150.5, TriggerLeadMs = 30, Metadata = { ["description"] = "Long notes" },
            },
            new Articulation
            {
                Id = 7, Name = "Pizz", ShortName = "Pz", Trigger = TriggerMode.Momentary, GroupId = 2,
                Actions = { new MidiAction { Kind = MidiActionKind.BankSelectMsb, Data1 = 0, Data2 = 1 },
                            new MidiAction { Kind = MidiActionKind.BankSelectLsb, Data1 = 32, Data2 = 2 },
                            new MidiAction { Kind = MidiActionKind.ProgramChange, Channel = 16, Data1 = 12 } },
                NoteOffsetMs = 200,
            },
            new Articulation { Id = 9, Name = "Off", ShortName = "Off", Enabled = false,
                Actions = { new MidiAction { Kind = MidiActionKind.Keyswitch, Data1 = 30, Data2 = 127 } } },
        },
        Metadata = { ["source.format"] = "nota" },
    };

    private static ArticulationImportResult Read(string json) => ArticulationMapFile.Read(json, "fallback");

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // ---- write → read → write is stable ----
        var full = Full();
        string w1 = ArticulationMapFile.Write(full);
        var r1 = Read(w1);
        string? w2 = r1.Map is null ? null : ArticulationMapFile.Write(r1.Map);
        yield return (r1.Succeeded && !r1.Warnings.Any() && w2 == w1, "articulations: a map using every field writes, reads and writes back identically");
        var back = r1.Map;
        yield return (back is not null && back.Articulations.Count == 3 && back.Find(7)!.Trigger == TriggerMode.Momentary
                      && back.Find(2)!.Actions[1].Channel == 5 && back.Find(2)!.NoteOffsetMs == -150.5 && back.Find(2)!.ColorArgb == 0xFF336699
                      && !back.Find(9)!.Enabled && back.DefaultArticulationId == 2 && back.Groups.Count == 2,
            "articulations: trigger mode, channel, offset, colour, enabled, default and groups survive the file");

        // ---- repairs, each reported ----
        string dupIds = """
            {"format":"nota-articulation-map","version":1,"id":"x","name":"R","defaultArticulationId":42,
             "groups":[{"id":1,"name":"A"},{"id":1,"name":"B"},{"name":"C"}],
             "articulations":[
               {"id":3,"name":"First","groupId":1,"actions":[{"kind":"keyswitch","channel":null,"data1":24,"data2":1}]},
               {"id":3,"name":"Second","trigger":"sticky","noteOffsetMs":5000,"triggerLeadMs":-4,
                "actions":[{"kind":"keyswitch","data1":300,"data2":1},{"kind":"pitchBend","data1":0,"data2":0},{"kind":"controlChange","channel":17,"data1":1,"data2":1},{"kind":"controlChange","data1":1,"data2":64}]},
               {"name":"Third","groupId":9}
             ]}
            """;
        var rep = Read(dupIds);
        var m = rep.Map;
        yield return (m is not null && m.Articulations.Select(x => x.Id).SequenceEqual(new[] { 3, 4, 5 })
                      && rep.Warnings.Count(x => x.Text.Contains("is now")) == 2,
            "articulations: a repeated or missing articulation id gets the next free id, and each is reported");
        yield return (m is not null && m.DefaultArticulationId is null && rep.Warnings.Any(x => x.Text.Contains("default articulation")),
            "articulations: a default that isn't in the map is dropped and reported");
        var second = m?.Articulations.ElementAtOrDefault(1);
        yield return (second is not null && second.NoteOffsetMs == 2000 && second.TriggerLeadMs == 0
                      && second.Trigger == TriggerMode.Latching && second.Actions.Count == 1 && second.Actions[0].Data2 == 64,
            "articulations: an out-of-range offset and lead are clamped, an unknown trigger plays latching, invalid actions are dropped");
        yield return (rep.Warnings.Any(x => x.Text.Contains("brought into range")) && rep.Warnings.Any(x => x.Text.Contains("Latching"))
                      && rep.Warnings.Any(x => x.Text.Contains("invalid MIDI action")),
            "articulations: each of those repairs is reported");
        yield return (m is not null && m.Groups.Count == 1 && m.Groups[0].Name == "A" && m.Articulations[0].GroupId == 1 && m.Articulations[2].GroupId == 0
                      && rep.Warnings.Any(x => x.Text.Contains("group")),
            "articulations: a repeated or id-less group is dropped and an articulation pointing at a missing group is ungrouped");
        var nameless = Read("""{"format":"nota-articulation-map","articulations":[]}""");
        yield return (nameless.Map?.Name == "fallback" && nameless.Map.Id.Length == 32, "articulations: a nameless map takes the file name and gets a new id");

        // ---- refusals ----
        yield return (Read("{ not json").Messages.Any(x => x.Severity == ImportSeverity.Error) && !Read("{ not json").Succeeded,
            "articulations: a file that isn't JSON is refused");
        yield return (!Read("""{"format":"something-else"}""").Succeeded && !Read("[1,2,3]").Succeeded,
            "articulations: JSON that isn't a Nota map is refused by its format field");
        var newer = Read("""{"format":"nota-articulation-map","version":99}""");
        yield return (!newer.Succeeded && newer.Messages.Any(x => x.Text.Contains("newer")), "articulations: a map from a newer version is refused, not half read");
        var deep = new StringBuilder("{\"format\":\"nota-articulation-map\",\"metadata\":");
        for (int i = 0; i < 100; i++) deep.Append("{\"a\":");
        deep.Append("1").Append('}', 100).Append('}');
        yield return (!Read(deep.ToString()).Succeeded, "articulations: JSON nested past 64 levels is refused, not a crash");
        yield return (!ArticulationMapFile.ReadFile(Path.Combine(Path.GetTempPath(), "nota-missing-" + Guid.NewGuid().ToString("N") + ".notamap")).Succeeded,
            "articulations: a missing map file is reported");

        // ---- the compiler ----
        var c = ArticulationMapCompiler.Compile(full);
        yield return (c.Entries.Select(e => e.Id).SequenceEqual(new[] { 2, 7 }), "articulations: the compiler leaves disabled articulations out, ids ascending");
        yield return (c.DefaultId == 2 && c.Entries[0].OffsetMs == -150.5 && c.Entries[0].LeadMs == 30 && c.Entries[1].TriggerMode == 1,
            "articulations: offsets, leads, trigger mode and the default reach the table");
        yield return (c.Entries[0].ActionStart == 0 && c.Entries[0].ActionCount == 2 && c.Entries[1].ActionStart == 2 && c.Entries[1].ActionCount == 3
                      && c.Actions[0].Channel == 0 && c.Actions[1].Channel == 5 && c.Actions[2].Kind == 3 && c.Actions[3].Data1 == 32 && c.Actions[4].Kind == 2 && c.Actions[4].Channel == 16,
            "articulations: actions keep their order; the note's own channel compiles to 0");
        var noDefault = Full();
        noDefault.DefaultArticulationId = 9;   // the disabled one
        yield return (ArticulationMapCompiler.Compile(noDefault).DefaultId == 0, "articulations: a disabled default compiles to no default");

        // ---- how actions read in the preview and inspector ----
        var longArt = full.Find(2)!;
        string longText = ArticulationText.Actions(longArt);
        yield return (longText == "Keyswitch C1 · vel 1  ·  CC 58 = 81 (ch 5)", $"articulations: actions read in send order with their channel ({longText})");
        string pizz = ArticulationText.Actions(full.Find(7)!);
        yield return (pizz == "Bank MSB 1  ·  Bank LSB 2  ·  Program 12 (ch 16)", $"articulations: bank and program actions read plainly ({pizz})");
        yield return (ArticulationText.Actions(new Articulation()) == "Sends nothing", "articulations: an articulation without actions says it sends nothing");
        yield return (ArticulationText.Count(full) == "3 articulations" && ArticulationText.Count(new ArticulationMap { Articulations = { new Articulation { Id = 1 } } }) == "1 articulation",
            "articulations: the count is counted (1 articulation, 3 articulations)");
        yield return (ArticulationText.Group(full.Groups[0]) == "Bowing" && ArticulationText.Group(full.Groups[1]) == "Group 2",
            "articulations: an unnamed group reads as a numbered group");

        // FR-018 / T053: track + articulation, once; only articulations with an offset of their own, enabled ones.
        var eff = ArticulationText.EffectiveOffsets(full, -100);
        yield return (eff.Count == 2 && eff[0] == ("Long", -250.5) && eff[1] == ("Pizz", 100.0),
            $"articulations: effective offsets add the track's once (Long −100 + −150.5 = −250.5, Pizz −100 + 200 = 100; the disabled one left out) ({string.Join(", ", eff)})");
    }
}
