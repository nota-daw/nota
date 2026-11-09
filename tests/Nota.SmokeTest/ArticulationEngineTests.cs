// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Articulation maps through the engine (specs/085): the note's articulation id crosses the C ABI
// intact (the NotaNoteData / NotaNote layouts agree), the track MIDI offset is clamped, copied with
// the track and undone with it, moves playback by the requested milliseconds without moving notes,
// and both survive saving and reopening; a v29 project opens with neither.
// Runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --articulations`.

using System.Text.Json.Nodes;
using Nota.Application.Midi;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ArticulationEngineTests
{
    private const int Block = 512, Rate = 48000;

    private static float[] Render(NotaEngine e, int blocks)
    {
        var all = new float[blocks * Block];
        var b = new float[Block * 2];
        for (int k = 0; k < blocks; k++)
        {
            e.RenderOffline(b, Block, Rate);
            for (int i = 0; i < Block; i++) all[k * Block + i] = (b[i * 2] + b[i * 2 + 1]) * 0.5f;
        }
        return all;
    }

    /// <summary>First sample louder than a quiet threshold, or -1.</summary>
    private static int Onset(float[] x)
    {
        for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i]) > 1e-3f) return i;
        return -1;
    }

    /// <summary>Where one note at beat 1 (0.5 s at 120 BPM) starts sounding, with the track at <paramref name="offsetMs"/>.</summary>
    private static int OnsetWithOffset(double offsetMs)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        int c = e.AddMidiClip(t, 0, 4);
        e.SetClipNotes(t, c, new[] { new NotaNote(57, 1.0, 1.0, 0.9f) });
        e.SetTrackMidiOffsetMs(t, offsetMs);
        Render(e, 2);                                 // let the graph settle before the clock starts
        e.StopTransport(); e.Seek(0); e.Play();
        return Onset(Render(e, 94));                  // ~1 s
    }

    // The clip tools (generate / transform) keep articulations (T011): notes outside the selection
    // pass through untouched, and notes made from a note play that note's articulation.
    private static IEnumerable<(bool ok, string label)> ClipTools()
    {
        var ctx = new MidiToolContext { LengthBeats = 4, Grid = 0.25, ScaleOn = false, ScaleRoot = 0, ScaleMask = MidiScales.MaskAt(0), Seed = 11 };
        var clip = new[]
        {
            new NotaNote(60, 0.0, 2.0, 0.8f) { ArticulationId = 1 },
            new NotaNote(64, 0.0, 2.0, 0.8f) { ArticulationId = 2 },
            new NotaNote(72, 2.0, 2.0, 0.8f) { ArticulationId = 9 },   // outside the selection below
        };
        var selection = new[] { 0, 1 };

        int lost = 0;
        string firstLost = "";
        foreach (var tool in MidiToolCatalog.All)
        {
            var outp = MidiToolRunner.Apply(tool, clip, selection, tool.NewSettings(), ctx);
            bool kept = outp.Any(n => n.Pitch == 72 && n.ArticulationId == 9) || !outp.Any(n => n.Pitch == 72 && Math.Abs(n.StartBeat - 2.0) < 1e-9);
            if (!kept) { lost++; if (firstLost.Length == 0) firstLost = tool.Id; }
        }
        yield return (lost == 0, $"articulations: no clip tool strips the articulation of a note outside its selection ({lost} did{(lost > 0 ? ", first " + firstLost : "")})");

        var trill = MidiTransforms.Ornament.NewSettings();
        trill.Set("type", 1);
        trill.Set("chance", 1.0);
        var orn = MidiToolRunner.Apply(MidiTransforms.Ornament, new[] { new NotaNote(60, 0, 2, 0.8f) { ArticulationId = 4 } }, null, trill, ctx);
        yield return (orn.Count > 1 && orn.All(n => n.ArticulationId == 4),
            $"articulations: a trill's notes all play the ornamented note's articulation ({orn.Count} notes)");

        var arp = MidiToolRunner.Apply(MidiTransforms.Arpeggiate, clip[..2], null, MidiTransforms.Arpeggiate.NewSettings(), ctx);
        bool byChordNote = arp.Count > 2 && arp.All(n => n.ArticulationId == ((n.Pitch % 12) == 0 ? 1 : (n.Pitch % 12) == 4 ? 2 : -1));
        yield return (byChordNote, $"articulations: each arpeggio step plays the articulation of the chord note it came from ({arp.Count} notes)");
    }

    // A map on a track (T016–T023): the engine validates the table, keeps the map JSON, undoes and
    // redoes it, copies it with the track, and a project saves and reopens it — or opens without it.
    private static IEnumerable<(bool ok, string label)> MapOnTrack()
    {
        var map = ArticulationFileTests.Full();
        string json = Nota.Application.Articulations.ArticulationMapFile.Write(map);
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int audio = e.AddAudioTrack();
            e.ClearUndoHistory();
            yield return (e.GetTrackArticulationMapJson(t) is null, "articulations: a new track has no map");
            yield return (Nota.Application.Articulations.ArticulationMapAssignment.Assign(e, t, map) && e.GetTrackArticulationMapJson(t) == json,
                "articulations: a compiled map is accepted and the track keeps its JSON exactly");
            var readBack = Nota.Application.Articulations.ArticulationMapAssignment.Read(e, t);
            yield return (readBack is not null && Nota.Application.Articulations.ArticulationMapFile.Write(readBack) == json,
                "articulations: the map read back from the track equals the one given");

            // Every rule of the table, each refused with nothing changed.
            var ok = Nota.Application.Articulations.ArticulationMapCompiler.Compile(map);
            NotaArticulationEntry[] E(params NotaArticulationEntry[] x) => x;
            var bad = new List<(string why, int def, NotaArticulationEntry[] en, NotaArticulationAction[] ac)>
            {
                ("ids not ascending", 0, E(ok.Entries[1], ok.Entries[0]), ok.Actions),
                ("a repeated id", 0, E(ok.Entries[0], ok.Entries[0]), ok.Actions),
                ("an id of 0", 0, E(ok.Entries[0] with { Id = 0 }), ok.Actions),
                ("an action range past the array", 0, E(ok.Entries[0] with { ActionCount = 99 }), ok.Actions),
                ("a negative action start", 0, E(ok.Entries[0] with { ActionStart = -1 }), ok.Actions),
                ("an unknown trigger mode", 0, E(ok.Entries[0] with { TriggerMode = 5 }), ok.Actions),
                ("a NaN offset", 0, E(ok.Entries[0] with { OffsetMs = double.NaN }), ok.Actions),
                ("an offset past 2000 ms", 0, E(ok.Entries[0] with { OffsetMs = 2500 }), ok.Actions),
                ("a negative lead", 0, E(ok.Entries[0] with { LeadMs = -1 }), ok.Actions),
                ("a default that isn't an entry", 5, ok.Entries, ok.Actions),
                ("data past 127", 0, ok.Entries, ok.Actions.Select((x, i) => i == 0 ? x with { Data1 = 128 } : x).ToArray()),
                ("channel 17", 0, ok.Entries, ok.Actions.Select((x, i) => i == 0 ? x with { Channel = 17 } : x).ToArray()),
                ("an unknown action kind", 0, ok.Entries, ok.Actions.Select((x, i) => i == 0 ? x with { Kind = 9 } : x).ToArray()),
            };
            var refusedWrongly = new List<string>();
            foreach (var (why, def, en, ac) in bad)
                if (e.SetTrackArticulationMap(t, def, en, ac, "{}") || e.GetTrackArticulationMapJson(t) != json) refusedWrongly.Add(why);
            yield return (refusedWrongly.Count == 0, $"articulations: the engine refuses a broken table and keeps the map it had ({bad.Count} cases{(refusedWrongly.Count > 0 ? "; not refused: " + string.Join(", ", refusedWrongly) : "")})");
            yield return (!e.SetTrackArticulationMap(audio, ok.DefaultId, ok.Entries, ok.Actions, json) && e.GetTrackArticulationMapJson(audio) is null,
                "articulations: an audio track can't take a map");
            yield return (!e.SetTrackArticulationMap(9999, ok.DefaultId, ok.Entries, ok.Actions, json), "articulations: no such track is refused");
            yield return (e.SetTrackArticulationMap(t, 0, Array.Empty<NotaArticulationEntry>(), Array.Empty<NotaArticulationAction>(), "{}")
                          && e.GetTrackArticulationMapJson(t) == "{}", "articulations: an empty map is allowed (it sends nothing)");

            // Undo and redo, one step each.
            e.Undo();
            yield return (e.GetTrackArticulationMapJson(t) == json, "articulations: undo restores the previous map");
            e.Undo();
            yield return (e.GetTrackArticulationMapJson(t) is null, "articulations: undoing the first assignment leaves no map");
            e.Redo();
            yield return (e.GetTrackArticulationMapJson(t) == json, "articulations: redo assigns it again");
            yield return (e.ClearTrackArticulationMap(t) && e.GetTrackArticulationMapJson(t) is null, "articulations: clearing removes the map");
            e.Undo();
            yield return (e.GetTrackArticulationMapJson(t) == json, "articulations: undoing the clear brings it back");
            int dup = e.DuplicateTrack(t);
            yield return (dup > 0 && e.GetTrackArticulationMapJson(dup) == json, "articulations: a duplicated track keeps its map");
            int plain = e.AddInstrumentTrack();
            bool couldUndo = e.CanUndo;
            e.ClearUndoHistory();
            yield return (couldUndo && e.ClearTrackArticulationMap(plain) && !e.CanUndo,
                "articulations: clearing an instrument track with no map succeeds and adds no undo step");
            yield return (!e.ClearTrackArticulationMap(audio), "articulations: an audio track refuses a clear too");
        }

        // Save and reopen; a damaged map in a project opens without it.
        string bundle = Path.Combine(Path.GetTempPath(), "nota-artmap-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            using var e = new NotaEngine();
            int t = e.AddInstrumentTrack();
            e.AddInstrumentTrack();   // a second one without a map
            Nota.Application.Articulations.ArticulationMapAssignment.Assign(e, t, map);
            var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
            yield return (doc.Tracks.Count(d => d.ArticulationMap is not null) == 1, "articulations: only the track with a map writes one");
            ProjectService.Save(doc, bundle, e);

            using var e2 = new NotaEngine();
            var warnings = ProjectService.Apply(ProjectService.Load(bundle), e2, bundle);
            var jsons = new List<string?>();
            for (int i = 0; i < e2.TrackCount; i++)
                if (e2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) jsons.Add(e2.GetTrackArticulationMapJson(ti.Id));
            yield return (jsons.Count == 2 && jsons[0] == json && jsons[1] is null && warnings.Count == 0,
                "articulations: the map survives saving and reopening exactly");

            // The same document with its map damaged: it opens, the track has no map, a warning says why.
            var broken = ProjectService.Load(bundle);
            broken.Tracks.First(d => d.ArticulationMap is not null).ArticulationMap = new System.Text.Json.Nodes.JsonObject { ["format"] = "nonsense" };
            using var e3 = new NotaEngine();
            var w3 = ProjectService.Apply(broken, e3, bundle);
            bool noMap = true;
            for (int i = 0; i < e3.TrackCount; i++)
                if (e3.TryGetTrackInfo(i, out var ti) && e3.GetTrackArticulationMapJson(ti.Id) is not null) noMap = false;
            yield return (noMap && w3.Count >= 1 && e3.TrackCount == 2, $"articulations: a project with a damaged map still opens, without it, and warns ({w3.Count})");
        }
        finally
        {
            try { if (Directory.Exists(bundle)) Directory.Delete(bundle, recursive: true); } catch (IOException) { }
        }
    }

    // Phase 4/5 (US2, US3): the engine's own check of the scheduler, then the articulation offset
    // moving real notes, through the C ABI, at two tempos.
    private static IEnumerable<(bool ok, string label)> Playback()
    {
        using (var e = new NotaEngine())
        {
            int code = e.ArticulationSelfTest();
            yield return (code == 0, $"articulations: the engine schedules keyswitches, controllers and programs block by block — map order, one switch per phrase, offsets and lead, momentary holds, chase on seek and loop wrap, seam releases, delivery ahead of the note on its channel (failed check: {code})");
        }

        // A Legato note with track −100 ms and articulation −150 ms sounds 250 ms early, at 120 and 60 BPM.
        foreach (double bpm in new[] { 120.0, 60.0 })
        {
            int plain = OnsetWithMap(bpm, trackMs: 0, articulationMs: 0);
            int moved = OnsetWithMap(bpm, trackMs: -100, articulationMs: -150);
            int want = Rate / 4;   // 250 ms = 12000 samples
            yield return (plain > 0 && moved > 0 && Math.Abs((plain - moved) - want) <= 64,
                $"articulations: at {bpm:0} BPM, track −100 ms + Legato −150 ms plays the note 250 ms early ({plain - moved} samples, want {want})");
        }

        // Session slots (T049): the same offset moves a slot's note, the slot's clock untouched.
        int slot0 = OnsetInSlot(0), slotEarly = OnsetInSlot(-100), slotLate = OnsetInSlot(100);
        yield return (slot0 > 0 && slotEarly > 0 && slotLate > 0
                      && Math.Abs((slot0 - slotEarly) - Rate / 10) <= 64 && Math.Abs((slotLate - slot0) - Rate / 10) <= 64,
            $"articulations: a Session slot's note moves with the track offset (−100 ms: {slot0 - slotEarly}, +100 ms: {slotLate - slot0} samples, want {Rate / 10})");
    }

    /// <summary>Where a Session slot's note on beat 1 starts sounding after the slot is launched, with a
    /// track MIDI offset (T049).</summary>
    private static int OnsetInSlot(double trackMs)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetLaunchQuant(0.0);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        e.AddSessionMidiClip(t, 0, 4.0);
        e.SetSessionNotes(t, 0, new[] { new NotaNote(57, 1.0, 1.0, 0.9f) });
        e.SetTrackMidiOffsetMs(t, trackMs);
        Render(e, 2);
        e.StopTransport(); e.Seek(0); e.Play();
        e.LaunchSlot(t, 0);
        return Onset(Render(e, 94));   // ~1 s: beat 1 is at 0.5 s
    }

    /// <summary>Where a note on beat 2 with articulation 2 starts sounding (built-in synth: it gets
    /// no keyswitches, only the timing), with the track and the articulation offset given.</summary>
    private static int OnsetWithMap(double bpm, double trackMs, double articulationMs)
    {
        using var e = new NotaEngine();
        e.SetBpm(bpm);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        int c = e.AddMidiClip(t, 0, 8);
        e.SetClipNotes(t, c, new[] { new NotaNote(57, 2.0, 1.0, 0.9f) { ArticulationId = 2 } });
        var map = new Nota.Application.Articulations.ArticulationMap
        {
            Name = "Timing",
            Articulations =
            {
                new Nota.Application.Articulations.Articulation { Id = 1, Name = "Long" },
                new Nota.Application.Articulations.Articulation { Id = 2, Name = "Legato", NoteOffsetMs = articulationMs, TriggerLeadMs = 30 },
            },
        };
        Nota.Application.Articulations.ArticulationMapAssignment.Assign(e, t, map);
        e.SetTrackMidiOffsetMs(t, trackMs);
        Render(e, 2);
        e.StopTransport(); e.Seek(0); e.Play();
        double seconds = 2.0 * 60.0 / bpm + 0.6;   // past beat 2 with room
        return Onset(Render(e, (int)(seconds * Rate / Block) + 1));
    }

    // Regression (owner report, 2026-11-09): an articulation-only edit in the piano roll never reached
    // the engine because the roll's "did this clip change?" test compared fields by hand and skipped
    // the articulation. NotaNote.Same is that test now; it must notice a change to every field.
    private static IEnumerable<(bool ok, string label)> NoteEquality()
    {
        var baseNote = new NotaNote(60, 1.0, 2.0, 0.5f) { ExprId = 3, ArticulationId = 4 };
        var fields = typeof(NotaNote).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var missed = new List<string>();
        foreach (var f in fields)
        {
            object boxed = baseNote;
            object? v = f.GetValue(boxed);
            // Each arm boxed as object: a switch expression would otherwise widen them all to double.
            object changed = v switch
            {
                int i => (object)(i + 1),
                double d => (object)(d + 0.5),
                float s => (object)(s * 0.5f),
                _ => throw new InvalidOperationException($"NotaNote.{f.Name}: add its type to this test"),
            };
            f.SetValue(boxed, changed);
            if (NotaNote.Same(baseNote, (NotaNote)boxed)) missed.Add(f.Name);
        }
        yield return (fields.Length >= 6 && missed.Count == 0,
            $"articulations: NotaNote.Same notices a change to every field ({fields.Length} fields{(missed.Count > 0 ? "; missed: " + string.Join(", ", missed) : "")})");
        yield return (NotaNote.Same(baseNote, baseNote with { }), "articulations: NotaNote.Same holds for an unchanged copy");
    }

    // T045 / FR-013: a note's articulation survives every clip and note operation that keeps the note.
    private static IEnumerable<(bool ok, string label)> EditsKeepArticulations()
    {
        // The articulation of each note, by (pitch, absolute start).
        static Dictionary<(int, double), int> Arts(NotaEngine e, int t)
        {
            var d = new Dictionary<(int, double), int>();
            for (int c = 0; e.TryGetClipInfo(t, c, out var info); c++)
                if (info.IsMidi)
                    foreach (var n in e.GetClipNotes(t, c)) d[(n.Pitch, Math.Round(info.StartBeat + n.StartBeat, 6))] = n.ArticulationId;
            return d;
        }
        var notes = new[]
        {
            new NotaNote(60, 0.0, 1.0, 0.8f) { ArticulationId = 1 },
            new NotaNote(64, 0.0, 1.0, 0.8f) { ArticulationId = 2 },   // a chord, two articulations
            new NotaNote(67, 2.0, 1.0, 0.8f) { ArticulationId = 3 },
        };

        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int c = e.AddMidiClip(t, 0, 4);
            e.SetClipNotes(t, c, notes);
            int dup = e.DuplicateClip(t, c);   // lands at beat 4
            var a = Arts(e, t);
            yield return (dup >= 0 && a[(60, 4.0)] == 1 && a[(64, 4.0)] == 2 && a[(67, 6.0)] == 3,
                "articulations: duplicating a clip keeps every note's articulation");
            int right = e.SplitClip(t, c, 2.0);
            a = Arts(e, t);
            yield return (right >= 0 && a[(60, 0.0)] == 1 && a[(64, 0.0)] == 2 && a[(67, 2.0)] == 3,
                "articulations: splitting a clip keeps the articulations on both sides");
            e.TrimClip(t, c, 0.0, 1.5);
            yield return (Arts(e, t)[(64, 0.0)] == 2, "articulations: trimming a clip keeps the articulations of the notes left in it");
            // The split put its right half after the original, so find the duplicate (at beat 4) by place.
            int dupNow = -1;
            for (int k = 0; e.TryGetClipInfo(t, k, out var ci); k++) if (ci.IsMidi && ci.StartBeat == 4.0) dupNow = k;
            e.CopyClip(t, dupNow);
            int pasted = e.PasteClip(t, 16.0);
            a = Arts(e, t);
            yield return (pasted >= 0 && a.Where(kv => kv.Key.Item2 >= 16).Select(kv => kv.Value).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 }),
                "articulations: copying and pasting a clip keeps the articulations");
        }
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int c1 = e.AddMidiClip(t, 0, 2), c2 = e.AddMidiClip(t, 2, 2);
            e.SetClipNotes(t, c1, notes[..2]);
            e.SetClipNotes(t, c2, new[] { new NotaNote(67, 0.0, 1.0, 0.8f) { ArticulationId = 3 } });
            bool merged = e.ConsolidateRange(new[] { t }, 0, 4);
            var a = Arts(e, t);
            yield return (merged && a[(60, 0.0)] == 1 && a[(64, 0.0)] == 2 && a[(67, 2.0)] == 3,
                "articulations: consolidating clips into one keeps every note's articulation");

            e.AddSessionMidiClip(t, 0, 4.0);
            e.SetSessionNotes(t, 0, notes);
            var back = e.GetSessionNotes(t, 0);
            int moved = e.SessionSlotToArrangement(t, 0, 8.0);
            a = Arts(e, t);
            yield return (back.Select(n => n.ArticulationId).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 })
                          && moved >= 0 && a[(60, 8.0)] == 1 && a[(64, 8.0)] == 2 && a[(67, 10.0)] == 3,
                "articulations: a Session slot keeps its notes' articulations, and so does moving it to the arrangement");
        }

        // The piano roll's own edits run through ClipSet (multi-clip split / merge) and NoteOverwrite.
        var set = new Nota.Application.Editing.ClipSet(new[]
        {
            new Nota.Application.Editing.OpenClip(1, 0, 0.0, 4.0), new Nota.Application.Editing.OpenClip(1, 1, 4.0, 4.0),
        }, 1, 0);
        var editor = new List<NotaNote> { notes[0], notes[1] with { StartBeat = 4.5 } };
        var owners = new List<int> { 0, 1 };
        var split = set.Split(editor, owners);
        var mergedBack = new List<NotaNote>();
        set.Merge(split, mergedBack, new List<int>());
        yield return (split.Sum(s => s.Notes.Count) == 2 && split.SelectMany(s => s.Notes).Select(n => n.ArticulationId).OrderBy(x => x).SequenceEqual(new[] { 1, 2 })
                      && mergedBack.Select(n => n.ArticulationId).OrderBy(x => x).SequenceEqual(new[] { 1, 2 }),
            "articulations: the piano roll's multi-clip split and merge keep each note's articulation");
        var resized = new List<NotaNote> { notes[0], notes[2] };
        Nota.Application.Editing.NoteOverwrite.ResizeBy(resized, new[] { 0, 1 }, 0.5, 0.05, 8.0);
        yield return (resized[0].ArticulationId == 1 && resized[1].ArticulationId == 3 && resized[0].LengthBeats == 1.5,
            "articulations: resizing notes keeps their articulations");
    }

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in NoteEquality()) yield return r;
        foreach (var r in EditsKeepArticulations()) yield return r;

        // ---- the note field crosses the ABI with every other field intact ----
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int c = e.AddMidiClip(t, 0, 4);
            int expr = e.CreateNoteExpression(new[] { new NotaExprPoint(NoteExpressionDim.Bend, 0f, 2f) });
            var notes = new[]
            {
                new NotaNote(60, 0.0, 1.0, 0.5f) { ArticulationId = 7 },
                new NotaNote(64, 0.0, 1.0, 0.6f) { ArticulationId = 12, ExprId = expr },   // a chord, two articulations
                new NotaNote(67, 1.5, 0.25, 0.7f),                                          // none
            };
            e.SetClipNotes(t, c, notes);
            var back = e.GetClipNotes(t, c);
            var byPitch = back.ToDictionary(n => n.Pitch);
            yield return (back.Length == 3
                          && byPitch.TryGetValue(60, out var a) && a.ArticulationId == 7 && a.StartBeat == 0.0 && a.LengthBeats == 1.0 && Math.Abs(a.Velocity - 0.5f) < 1e-6
                          && byPitch.TryGetValue(64, out var b) && b.ArticulationId == 12 && b.ExprId == expr
                          && byPitch.TryGetValue(67, out var d) && d.ArticulationId == 0 && d.StartBeat == 1.5 && d.LengthBeats == 0.25,
                "articulations: each note's articulation id crosses the C ABI with every other field intact");

            // One note's change touches only that note, and undoes in one step.
            var edited = back.ToArray();
            for (int i = 0; i < edited.Length; i++) if (edited[i].Pitch == 60) edited[i].ArticulationId = 3;
            e.SetClipNotes(t, c, edited);
            var after = e.GetClipNotes(t, c).ToDictionary(n => n.Pitch);
            yield return (after[60].ArticulationId == 3 && after[64].ArticulationId == 12 && after[67].ArticulationId == 0,
                "articulations: changing one note's articulation leaves the others alone");
            e.Undo();
            yield return (e.GetClipNotes(t, c).First(n => n.Pitch == 60).ArticulationId == 7, "articulations: undo restores a note's articulation");
            e.Redo();
            yield return (e.GetClipNotes(t, c).First(n => n.Pitch == 60).ArticulationId == 3, "articulations: redo reapplies it");

            int bad = e.GetClipNotes(t, c).Length;
            e.SetClipNotes(t, c, new[] { new NotaNote(60, 0, 1, 0.5f) { ArticulationId = -5 } });
            yield return (e.GetClipNotes(t, c).Single().ArticulationId == 0 && bad == 3, "articulations: a negative articulation id is stored as none");

            // ---- the track offset ----
            yield return (e.GetTrackMidiOffsetMs(t) == 0.0, "articulations: a new track has no MIDI offset");
            e.SetTrackMidiOffsetMs(t, -150);
            yield return (e.GetTrackMidiOffsetMs(t) == -150.0, "articulations: the track offset is stored (-150 ms)");
            e.SetTrackMidiOffsetMs(t, 5000);
            yield return (e.GetTrackMidiOffsetMs(t) == 2000.0, "articulations: the track offset is clamped to +2000 ms");
            e.SetTrackMidiOffsetMs(t, double.NaN);
            yield return (e.GetTrackMidiOffsetMs(t) == 0.0, "articulations: a NaN offset is stored as 0");
            e.SetTrackMidiOffsetMs(t, -80);
            int dup = e.DuplicateTrack(t);
            yield return (dup > 0 && e.GetTrackMidiOffsetMs(dup) == -80.0, "articulations: a duplicated track keeps its MIDI offset");

            // T051: the offset is one undo step; setting the same value is none.
            e.ClearUndoHistory();
            e.SetTrackMidiOffsetMs(t, -150);
            e.SetTrackMidiOffsetMs(t, -150);
            e.Undo();
            bool undone = e.GetTrackMidiOffsetMs(t) == -80.0 && !e.CanUndo;
            e.Redo();
            yield return (undone && e.GetTrackMidiOffsetMs(t) == -150.0,
                "articulations: a track offset change is one undo step (undo −150 → −80, redo back); the same value again adds none");
            yield return (e.GetTrackMidiOffsetMs(9999) == 0.0, "articulations: no such track reads offset 0");
        }

        // ---- the offset moves playback, not the notes ----
        int at0 = OnsetWithOffset(0), early = OnsetWithOffset(-100), late = OnsetWithOffset(100);
        int ms100 = Rate / 10;   // 4800 samples
        yield return (at0 > 0 && Math.Abs(at0 - 24000) < 256, $"articulations: with no offset the note sounds on its beat (onset {at0}, beat at 24000)");
        yield return (at0 > 0 && early > 0 && Math.Abs((at0 - early) - ms100) <= 64,
            $"articulations: a -100 ms track offset plays the note 100 ms early ({at0 - early} samples, want {ms100})");
        yield return (at0 > 0 && late > 0 && Math.Abs((late - at0) - ms100) <= 64,
            $"articulations: a +100 ms track offset plays the note 100 ms late ({late - at0} samples, want {ms100})");

        // ---- save and reopen; old projects ----
        string bundle = Path.Combine(Path.GetTempPath(), "nota-art-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            using var e = new NotaEngine();
            int t = e.AddInstrumentTrack();
            int plain = e.AddInstrumentTrack();
            int c = e.AddMidiClip(t, 0, 4);
            e.SetClipNotes(t, c, new[] { new NotaNote(60, 0, 1, 0.8f) { ArticulationId = 5 }, new NotaNote(62, 1, 1, 0.8f) });
            e.SetTrackMidiOffsetMs(t, -120);
            var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
            var savedTrack = doc.Tracks.FirstOrDefault(d => d.MidiOffsetMs is not null);
            yield return (savedTrack?.MidiOffsetMs == -120.0 && doc.Tracks.Count(d => d.MidiOffsetMs is not null) == 1,
                "articulations: only the track with an offset writes one");
            ProjectService.Save(doc, bundle, e);

            using var e2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(bundle), e2, bundle);
            int lt = -1, lplain = -1;
            for (int i = 0; i < e2.TrackCount; i++)
                if (e2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument)
                {
                    if (ti.ClipCount > 0) lt = ti.Id; else lplain = ti.Id;
                }
            var ln = lt > 0 ? e2.GetClipNotes(lt, 0).ToDictionary(n => n.Pitch) : new Dictionary<int, NotaNote>();
            yield return (lt > 0 && e2.GetTrackMidiOffsetMs(lt) == -120.0 && lplain > 0 && e2.GetTrackMidiOffsetMs(lplain) == 0.0,
                "articulations: the track offset survives saving and reopening");
            yield return (ln.Count == 2 && ln[60].ArticulationId == 5 && ln[62].ArticulationId == 0,
                "articulations: each note's articulation survives saving and reopening");
        }
        finally
        {
            try { if (Directory.Exists(bundle)) Directory.Delete(bundle, recursive: true); } catch (IOException) { }
        }

        foreach (var r in ClipTools()) yield return r;
        foreach (var r in MapOnTrack()) yield return r;
        foreach (var r in Playback()) yield return r;

        var old = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 29 });
        yield return (old["formatVersion"]?.GetValue<int>() == ProjectService.CurrentFormatVersion && ProjectService.CurrentFormatVersion == 30
                      && new TrackDto().MidiOffsetMs is null && new NoteDto().Articulation is null && new NoteDto().ToNote().ArticulationId == 0,
            "articulations: a v29 project migrates to v30 with no offsets and no articulations");
    }
}
