// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Piano roll Fold (specs/045, #344 #345): the row map the piano roll lays out with. Unfolded
// it is the old 88-key layout exactly; folded it keeps the scale's pitches or a Drum Rack's
// pads plus every pitch a note sits on, and moves step between visible rows. The Fold switch
// is saved per track.

using System.Diagnostics;
using Nota.Application;
using Nota.Application.Midi;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class FoldTests
{
    private const int Min = 21, Max = 108;   // the piano roll's A0..C8
    private const int A = 9;                 // root A

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Chromatic()) yield return r;
        foreach (var r in ScaleFold()) yield return r;
        foreach (var r in PadFold()) yield return r;
        foreach (var r in Remembered()) yield return r;
    }

    // Unfolded must lay out exactly as before Fold: row = MaxPitch − pitch.
    private static IEnumerable<(bool, string)> Chromatic()
    {
        var m = PianoRowMap.Chromatic(Min, Max);
        bool same = m.Count == 88;
        for (int p = Min; p <= Max; p++) same &= m.RowOf(p) == Max - p && m.PitchAt(Max - p) == p;
        yield return (same, "unfolded rows are the 88 keys, top to bottom, as before");
        yield return (m.Step(60, 1) == 61 && m.Step(60, -12) == 48 && m.Reach(Max) == (-87, 0),
            "unfolded, a row step is a semitone");
    }

    private static PianoRowMap AMinor(params int[] used)
        => PianoRowMap.Folded(Min, Max, null, MidiScales.MaskAt(1), A, used);   // Natural Minor

    private static IEnumerable<(bool, string)> ScaleFold()
    {
        var m = AMinor();
        bool allIn = true;
        for (int r = 0; r < m.Count; r++)
        {
            int pc = m.PitchAt(r) % 12;
            allIn &= pc is 9 or 11 or 0 or 2 or 4 or 5 or 7 && m.Mark(r) == PianoRowMark.InSet;
        }
        yield return (allIn && m.Count <= 52 && !m.IsChromatic, $"A minor folds to its 7 notes per octave ({m.Count} rows, SC-001: at most 52)");
        yield return (m.Step(64, 1) == 65 && m.Step(65, 1) == 67 && m.Step(69, -1) == 67, "a row up from E is F, from F is G; a row down from A is G");
        // A chord keeps its shape in scale degrees: A–C–E up one row is B–D–F.
        int[] chord = { 57, 60, 64 };
        yield return (chord.Select(p => m.Step(p, 1)).SequenceEqual(new[] { 59, 62, 65 }), "a chord moved a row keeps its shape in the scale");
        var withSharp = AMinor(66);   // F♯4 is out of A minor
        int fs = withSharp.RowOf(66);
        yield return (fs >= 0 && withSharp.Mark(fs) == PianoRowMark.OutOfScale && withSharp.Count == m.Count + 1,
            "a note out of the scale keeps its own row, marked out of scale");
        var none = PianoRowMap.Folded(Min, Max, null, null, 0, new[] { 60, 64, 67 });
        yield return (none.Count == 3 && none.PitchAt(0) == 67, "with no scale, Fold shows just the pitches in use");
        var empty = PianoRowMap.Folded(Min, Max, null, null, 0, Array.Empty<int>());
        yield return (empty.IsChromatic && empty.Count == 88, "nothing to fold to keeps the whole keyboard");

        // SC-005: a 2,000-note fold builds well inside a frame.
        var rnd = new Random(45);
        var notes = Enumerable.Range(0, 2000).Select(_ => rnd.Next(Min, Max + 1)).ToArray();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) AMinor(notes);
        double ms = sw.Elapsed.TotalMilliseconds / 10;
        yield return (ms < 16, $"folding 2,000 notes takes {ms:F2} ms (under one 16 ms frame)");
    }

    private static IEnumerable<(bool, string)> PadFold()
    {
        using var e = new NotaEngine();
        int t = e.AddDrumRackTrack();
        (string name, int note)[] kit = { ("Kick", 36), ("Snare", 38), ("Hat closed", 42), ("Kick 2", 36) };
        foreach (var (name, note) in kit)
        {
            int c = e.RackAddChain(t, 0);
            e.RackSetChainTriggerNote(t, c, note);
            e.RackSetChainName(t, c, name);
        }
        var pads = DrumPads.ByNote(DrumPads.Scan(e, t));
        yield return (pads.Count == 3 && pads[36] == "Kick / Kick 2" && pads[42] == "Hat closed", "pads on one note share a row named after both");
        var m = PianoRowMap.Folded(Min, Max, pads, null, 0, new[] { 37 });
        yield return (m.Count == 4 && m.Label(m.RowOf(38)) == "Snare" && m.PitchAt(0) == 42, "a kit folds to one named row per pad, lowest at the bottom");
        int empty = m.RowOf(37);
        yield return (empty >= 0 && m.Mark(empty) == PianoRowMark.NoPad && m.Label(empty) is null, "a note with no pad keeps an unnamed row");
        e.RackSetChainName(t, 1, "Clap");
        var renamed = PianoRowMap.Folded(Min, Max, DrumPads.ByNote(DrumPads.Scan(e, t)), null, 0, Array.Empty<int>());
        yield return (renamed.Label(renamed.RowOf(38)) == "Clap", "renaming a pad renames its row");
    }

    private static IEnumerable<(bool, string)> Remembered()
    {
        using var src = new NotaEngine();
        int drums = src.AddInstrumentTrack(), keys = src.AddInstrumentTrack();
        yield return (!src.GetTrackPianoFold(drums), "a new track opens unfolded");
        src.SetTrackPianoFold(drums, true);
        yield return (src.GetTrackPianoFold(drums) && !src.GetTrackPianoFold(keys), "Fold is per track");
        int dup = src.DuplicateTrack(drums);
        yield return (dup > 0 && src.GetTrackPianoFold(dup), "a duplicated track keeps its Fold");

        var doc = ProjectService.Capture(src, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-fold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var dst = new NotaEngine())
            {
                ProjectService.Apply(doc, dst, dir);
                var ids = Ids(dst);
                yield return (ids.Count == 3 && dst.GetTrackPianoFold(ids[0]) && dst.GetTrackPianoFold(ids[1]) && !dst.GetTrackPianoFold(ids[2]),   // drums, its duplicate, keys
                    "Fold survives save and load, on the track it was set on");
            }
            foreach (var tr in doc.Tracks) tr.PianoFold = null;   // as written before 0.58
            using (var old = new NotaEngine())
            {
                ProjectService.Apply(doc, old, dir);
                yield return (Ids(old).All(id => !old.GetTrackPianoFold(id)), "an older project opens unfolded");
            }
        }
        finally { Directory.Delete(dir, true); }

        // A view change, not an edit: undo after folding takes back the last edit instead.
        using var u = new NotaEngine();
        int a = u.AddInstrumentTrack();
        u.AddInstrumentTrack();
        u.SetTrackPianoFold(a, true);
        u.Undo();
        yield return (u.TrackCount == 1, $"Fold adds no undo step: undo removed the last track ({u.TrackCount} left)");
    }

    private static List<int> Ids(NotaEngine e)
    {
        var ids = new List<int>();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var t) && t.IsInstrument && !t.IsReturn && !t.IsGroup) ids.Add(t.Id);
        return ids;
    }
}
