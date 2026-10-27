// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI Effect Rack (specs/040, US4): notes routed by key, velocity and select zones through
// chains of MIDI effects, merged into the instrument — routing, no stuck notes under edits,
// and persistence (blob, undo, copies).
// The rig: a Pentad track whose MIDI chain is [MIDI Effect Rack, Nota Velocity]. What reaches
// the instrument is read from Pentad's held keys; Nota Velocity reports the last note-on's
// velocity and counts the note-ons that pass it.
// Runs with the other rack sections: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

using System.Globalization;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MidiRackTests
{
    private const int RackKind = 6, VelocityKind = 4, ArpKind = 0, ChordKind = 1;
    private const int Rate = 48000, Block = 256;

    /// <summary>The smoke-test sections, in run order.</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield return ("racks: midi rack routing", Routing);
        yield return ("racks: midi rack no stuck notes", NoStuckNotes);
        yield return ("racks: midi rack persistence", Persistence);
        yield return ("racks: midi rack MCP", Mcp);
    }

    private sealed class Rig : IDisposable
    {
        public readonly NotaEngine E = new();
        public readonly int T;
        public const int Mi = 0, Vel = 1;
        private readonly float[] _buf = new float[Block * 2];

        public Rig()
        {
            T = E.AddPentadTrack();
            E.AddMidiEffect(T, RackKind);
            E.AddMidiEffect(T, VelocityKind);
        }

        public void Render(int blocks = 2) { for (int i = 0; i < blocks; i++) E.RenderOffline(_buf, Block, Rate); }

        public string Held()
        {
            var n = new int[32];
            int k = E.InstrumentHeldNotes(T, n);
            return string.Join(",", n.Take(Math.Max(0, k)).OrderBy(x => x));
        }

        public int NoteOns()
        {
            var s = new float[25];
            return E.MidiEffectScope(T, Vel, s) == 25 ? (int)s[24] : -1;
        }

        public string Play(int pitch, float vel = 0.8f)
        {
            E.TrackNoteOn(T, pitch, vel);
            Render();
            string held = Held();
            E.TrackNoteOff(T, pitch);
            Render(4);
            return held;
        }

        public void Dispose() => E.Dispose();
    }

    private static IEnumerable<(bool, string)> Routing()
    {
        using var r = new Rig();
        var e = r.E;
        int t = r.T, mi = Rig.Mi;
        yield return (e.MidiEffectKind(t, mi) == RackKind && e.MidiRackChainCount(t, mi) == 1, "a new MIDI Effect Rack has one empty chain");
        e.MidiRackAddChain(t, mi);
        e.MidiRackSetChainZone(t, mi, 0, 60, 127, 0, 127);
        e.MidiRackSetChainZone(t, mi, 1, 0, 59, 0, 127);
        e.MidiRackSetChainTranspose(t, mi, 0, 12);
        yield return (r.Play(72) == "84" && r.Play(48) == "48", "a note goes to the chain whose key zone holds it (C5 → +12 on the upper chain, C3 untouched)");
        yield return (r.Held() == "", "a transposed note ends on its transposed pitch");

        e.MidiRackSetChainVelScale(t, mi, 0, 0.5f);
        r.Play(72, 100 / 127f);
        int half = e.MidiEffectLastIn(t, Rig.Vel);
        e.MidiRackSetChainVelScale(t, mi, 0, 0f);
        string silent = r.Play(72);
        e.MidiRackSetChainVelScale(t, mi, 0, 1f);
        yield return (Math.Abs(half - 50) <= 1 && silent == "", $"Volume scales velocity (100 → {half} at 50 %) and 0 % sends nothing ({(silent == "" ? "silent" : silent)})");

        e.MidiRackSetChainMute(t, mi, 0, true);
        string thru = r.Play(72);
        e.MidiRackSetThru(t, mi, false);
        string dropped = r.Play(72);
        e.MidiRackSetChainMute(t, mi, 0, false);
        yield return (thru == "72" && dropped == "", $"a note no chain takes passes through, or not with pass-through off ({thru} / {(dropped == "" ? "dropped" : dropped)})");
        e.MidiRackSetThru(t, mi, true);

        e.MidiRackSetChainSolo(t, mi, 1, true);
        yield return (r.Play(72) == "72" && r.Play(48) == "48", "solo: only the soloed chain takes notes (the rest pass through)");
        e.MidiRackSetChainSolo(t, mi, 1, false);

        e.MidiRackSetChainZone(t, mi, 0, 60, 127, 110, 127);
        e.MidiRackSetThru(t, mi, false);
        string soft = r.Play(72, 100 / 127f), hard = r.Play(72, 120 / 127f);
        yield return (soft == "" && hard == "84", $"a velocity zone takes only hard hits (vel 100: {(soft == "" ? "none" : soft)}, vel 120: {hard})");
        e.MidiRackSetThru(t, mi, true);

        // Select mode: the selector picks the chain whose select range holds it.
        e.MidiRackSetChainZone(t, mi, 0, 0, 127, 0, 127);
        e.MidiRackSetChainZone(t, mi, 1, 0, 127, 0, 127);
        e.MidiRackSetChainTranspose(t, mi, 1, -12);
        e.MidiRackSetChainSelect(t, mi, 0, 0, 63);
        e.MidiRackSetChainSelect(t, mi, 1, 64, 127);
        e.MidiRackSetMode(t, mi, 2);
        e.MidiRackSetSelector(t, mi, 0.2f);
        string low = r.Play(60);
        e.MidiRackSetSelector(t, mi, 0.8f);
        string high = r.Play(60);
        yield return (e.MidiRackMode(t, mi) == 2 && low == "72" && high == "48", $"Select mode: the selector picks the chain ({low} at 25, {high} at 102)");
        e.MidiRackSetMode(t, mi, 1);
        yield return (e.MidiRackMode(t, mi) == 2, "a MIDI Effect Rack has no Serial mode");
        e.MidiRackSetMode(t, mi, 0);

        // Merge: two chains sending one pitch play it once.
        e.MidiRackSetChainTranspose(t, mi, 0, 0);
        e.MidiRackSetChainTranspose(t, mi, 1, 0);
        int before = r.NoteOns();
        r.Play(64);
        int merged = r.NoteOns() - before;
        e.MidiRackSetMerge(t, mi, false);
        before = r.NoteOns();
        r.Play(64);
        int both = r.NoteOns() - before;
        e.MidiRackSetMerge(t, mi, true);
        yield return (merged == 1 && both == 2 && r.Held() == "", $"merge duplicate notes: one note-on with merge, {both} without, none left");

        // A chain's MIDI effects run on its notes: a Chord on the lower chain stacks its notes.
        e.MidiRackSetChainZone(t, mi, 0, 60, 127, 0, 127);
        e.MidiRackSetChainZone(t, mi, 1, 0, 59, 0, 127);
        int chord = e.MidiRackChainDeviceAdd(t, mi, 1, ChordKind);
        for (int p = 0; p < e.MidiRackChainDeviceParamCount(t, mi, 1, chord); p++)
            if (e.MidiRackChainDeviceParamName(t, mi, 1, chord, p) == "Strum") e.MidiRackChainDeviceParamSet(t, mi, 1, chord, p, 0f); // all at once
        string stacked = r.Play(48), plain = r.Play(72);
        yield return (chord == 0 && stacked.Split(',').Length > 1 && plain == "72",
            $"a chain's MIDI effects shape only its notes (C3 → {stacked}, C5 → {plain})");
        e.SetMidiEffectBypassed(t, mi, true);
        yield return (r.Play(48) == "48", "a bypassed rack passes notes straight through");
        e.SetMidiEffectBypassed(t, mi, false);

        // Bypass switched while notes sound: what the chains sent ends, and so does a note held
        // through the bypass once the rack is back on.
        e.MidiRackSetChainTranspose(t, mi, 0, 12);
        e.TrackNoteOn(t, 72, 0.8f);
        r.Render();
        string sounding = r.Held();
        e.SetMidiEffectBypassed(t, mi, true);
        r.Render();
        e.TrackNoteOff(t, 72);
        r.Render(4);
        string afterOff = r.Held();
        e.TrackNoteOn(t, 72, 0.8f);
        r.Render();
        e.SetMidiEffectBypassed(t, mi, false);
        r.Render();
        e.TrackNoteOff(t, 72);
        r.Render(4);
        yield return (sounding.Contains("84") && afterOff == "" && r.Held() == "",
            $"switching bypass with notes held leaves none hanging ({sounding} → {(afterOff == "" ? "none" : afterOff)} / {(r.Held() == "" ? "none" : r.Held())})");

        // A key struck again within one block keeps sounding, then ends.
        e.TrackNoteOn(t, 72, 0.8f);
        r.Render();
        e.TrackNoteOff(t, 72);
        e.TrackNoteOn(t, 72, 0.8f);
        r.Render();
        string again = r.Held();
        e.TrackNoteOff(t, 72);
        r.Render(4);
        yield return (again != "" && r.Held() == "", $"a key struck again in the same block keeps its route ({again}, then {(r.Held() == "" ? "none" : r.Held())})");
        e.MidiRackSetChainTranspose(t, mi, 0, 0);
    }

    private static IEnumerable<(bool, string)> NoStuckNotes()
    {
        using var r = new Rig();
        var e = r.E;
        int t = r.T, mi = Rig.Mi;
        e.MidiRackAddChain(t, mi);
        e.MidiRackAddChain(t, mi);
        e.MidiRackChainDeviceAdd(t, mi, 0, ChordKind);
        e.MidiRackChainDeviceAdd(t, mi, 1, ArpKind);
        var rng = new Random(4040);
        var down = new HashSet<int>();
        int edits = 0;
        for (int step = 0; step < 1000; step++)
        {
            int p = 36 + rng.Next(48);
            if (down.Contains(p)) { e.TrackNoteOff(t, p); down.Remove(p); }
            else { e.TrackNoteOn(t, p, (float)rng.NextDouble()); down.Add(p); }
            if (rng.Next(10) == 0)
            {
                int c = rng.Next(e.MidiRackChainCount(t, mi));
                switch (rng.Next(7))
                {
                    case 0: { int lo = rng.Next(100); e.MidiRackSetChainZone(t, mi, c, lo, lo + rng.Next(27), rng.Next(60), 127); break; }
                    case 1: e.MidiRackSetMode(t, mi, rng.Next(2) * 2); break;
                    case 2: e.MidiRackSetChainMute(t, mi, c, rng.Next(2) == 0); break;
                    case 3: e.MidiRackSetChainTranspose(t, mi, c, rng.Next(25) - 12); break;
                    case 4: e.MidiRackSetSelector(t, mi, (float)rng.NextDouble()); break;
                    case 5: if (e.MidiRackChainCount(t, mi) > 1) e.MidiRackRemoveChain(t, mi, c); else e.MidiRackAddChain(t, mi); break;
                    default: e.MidiRackAddChain(t, mi); break;
                }
                edits++;
            }
            r.Render(1);
        }
        foreach (int p in down) e.TrackNoteOff(t, p);
        r.Render(Rate / Block); // a second for releases and the Arp to stop
        string left = r.Held();
        yield return (left == "", $"1000 random notes under {edits} zone / mode / mute / transpose / chain edits leave no note hanging ({(left == "" ? "none" : left)})");
    }

    private static string Dump(NotaEngine e, int t, int mi)
    {
        var s = new System.Text.StringBuilder();
        string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        int n = e.MidiRackChainCount(t, mi);
        s.Append($"mode {e.MidiRackMode(t, mi)} sel {F(e.MidiRackSelector(t, mi))} merge {e.MidiRackMerge(t, mi)} thru {e.MidiRackThru(t, mi)} chains {n}\n");
        for (int c = 0; c < n; c++)
        {
            e.MidiRackChainZone(t, mi, c, out int kl, out int kh, out int vl, out int vh);
            e.MidiRackChainSelectRange(t, mi, c, out int sl, out int sh);
            s.Append($" {c} '{e.MidiRackChainName(t, mi, c)}' {kl}-{kh}/{vl}-{vh} sel {sl}-{sh} tr {e.MidiRackChainTranspose(t, mi, c)} vs {F(e.MidiRackChainVelScale(t, mi, c))} m {e.MidiRackChainMute(t, mi, c)} s {e.MidiRackChainSolo(t, mi, c)}\n");
            for (int d = 0; d < e.MidiRackChainDeviceCount(t, mi, c); d++)
            {
                s.Append($"   dev {e.MidiRackChainDeviceKind(t, mi, c, d)} byp {e.MidiRackChainDeviceBypassed(t, mi, c, d)}");
                for (int p = 0; p < e.MidiRackChainDeviceParamCount(t, mi, c, d); p++) s.Append(' ').Append(F(e.MidiRackChainDeviceParamGet(t, mi, c, d, p)));
                s.Append('\n');
            }
        }
        for (int m = 0; m < 8; m++) s.Append($" macro {m} '{e.MidiRackMacroName(t, mi, m)}' {F(e.MidiRackMacroGet(t, mi, m))}\n");
        for (int i = 0; i < e.MidiRackMappingCount(t, mi); i++)
        {
            e.MidiRackMappingInfo(t, mi, i, out int mm, out int ch, out int dv, out int pp, out float lo, out float hi, out int cv);
            s.Append($" map {mm} c{ch} d{dv} p{pp} {F(lo)}..{F(hi)} curve {cv}\n");
        }
        return s.ToString();
    }

    // A rack with a bit of everything, for the round trips.
    private static void Furnish(NotaEngine e, int t, int mi)
    {
        e.MidiRackAddChain(t, mi);
        e.MidiRackSetChainName(t, mi, 0, "Arp lead");
        e.MidiRackSetChainZone(t, mi, 0, 60, 127, 0, 127);
        e.MidiRackSetChainZone(t, mi, 1, 0, 59, 10, 120);
        e.MidiRackSetChainSelect(t, mi, 1, 64, 127);
        e.MidiRackSetChainTranspose(t, mi, 1, -7);
        e.MidiRackSetChainVelScale(t, mi, 1, 0.75f);
        e.MidiRackSetChainSolo(t, mi, 0, true);
        int arp = e.MidiRackChainDeviceAdd(t, mi, 0, ArpKind);
        e.MidiRackChainDeviceParamSet(t, mi, 0, arp, 3, 0.4f);
        e.MidiRackChainDeviceAdd(t, mi, 1, ChordKind);
        e.MidiRackChainDeviceSetBypassed(t, mi, 1, 0, true);
        e.MidiRackSetMode(t, mi, 2);
        e.MidiRackSetSelector(t, mi, 0.7f);
        e.MidiRackSetMerge(t, mi, false);
        e.MidiRackSetMacroName(t, mi, 0, "Rate");
        e.MidiRackMacroSet(t, mi, 0, 0.3f);
        int m = e.MidiRackAddMacroMapping(t, mi, 0, 0, arp, 0, 0.2f, 0.9f);
        e.MidiRackSetMappingCurve(t, mi, m, 1);
        e.MidiRackAddMacroMapping(t, mi, 1, 0, -3, 0, 0f, 1f); // the selector
    }

    private static IEnumerable<(bool, string)> Persistence()
    {
        using var e = new NotaEngine();
        int t = e.AddPentadTrack();
        int mi = e.AddMidiEffect(t, RackKind);
        Furnish(e, t, mi);
        string want = Dump(e, t, mi);

        byte[] blob = e.MidiEffectGetState(t, mi);
        int t2 = e.AddPentadTrack();
        int mi2 = e.AddMidiEffect(t2, RackKind);
        bool set = e.MidiEffectSetState(t2, mi2, blob, false);
        string got = Dump(e, t2, mi2);
        yield return (blob.Length > 0 && set && got == want, "the state blob carries every chain, device, zone, macro and output setting" + (got == want ? "" : $"\n--- want\n{want}--- got\n{got}"));

        int dupTrack = e.DuplicateTrack(t);
        yield return (dupTrack > 0 && Dump(e, dupTrack, 0) == want, "duplicating the track copies the rack");

        e.CopyMidiEffect(t, mi);
        int pasted = e.PasteMidiEffect(t2, -1);
        yield return (pasted >= 0 && Dump(e, t2, pasted) == want, "copy / paste of the rack keeps it all");

        int chains = e.MidiRackChainCount(t, mi);
        e.MidiRackAddChain(t, mi);
        bool added = e.MidiRackChainCount(t, mi) == chains + 1;
        e.Undo();
        bool undone = Dump(e, t, mi) == want;
        e.Redo();
        yield return (added && undone && e.MidiRackChainCount(t, mi) == chains + 1, "adding a chain is one undo step");
        e.Undo();

        e.MidiRackChainDeviceAdd(t, mi, 1, ArpKind);
        e.Undo();
        string afterDev = Dump(e, t, mi);
        e.MidiRackSetChainName(t, mi, 1, "Chords");
        e.Undo();
        string afterName = Dump(e, t, mi);
        e.MidiRackSetMappingCurve(t, mi, 0, 3);
        e.Undo();
        string afterCurve = Dump(e, t, mi);
        string bad = afterDev != want ? $"device\n{want}---\n{afterDev}" : afterName != want ? $"name\n{afterName}" : afterCurve != want ? $"curve\n{afterCurve}" : "";
        yield return (bad == "", "device, name and curve edits each undo back to where they were" + (bad == "" ? "" : $" — differs after {bad}"));

        // Project save → reopen, and a user preset save → load on another track.
        string dir = Path.Combine(Path.GetTempPath(), $"nota-midirack-{Guid.NewGuid():N}");
        try
        {
            var transport = new TransportState(120.0, 1.0, false, false);
            ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e);
            using var dst = new NotaEngine();
            var warn = ProjectService.Apply(ProjectService.Load(dir), dst, dir);
            int lt = -1;
            for (int i = 0; i < dst.TrackCount && lt < 0; i++)
            {
                if (!dst.TryGetTrackInfo(i, out var info)) continue;
                if (dst.TrackMidiEffectCount(info.Id) > 0 && dst.MidiEffectKind(info.Id, 0) == RackKind) lt = info.Id;
            }
            yield return (warn.Count == 0 && lt > 0 && Dump(dst, lt, 0) == want, $"the rack survives a project save and reopen ({warn.Count} warnings)");

            var preset = PresetService.CaptureMidiEffect(e, t, mi, "Split arp");
            int pt = e.AddPentadTrack();
            string err = preset is null ? "no preset" : PresetService.Apply(preset, e, pt);
            yield return (err == "" && Dump(e, pt, 0) == want, "a rack preset brings back the whole rack" + (err == "" ? "" : $" ({err})"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        for (int i = 0; i < 20; i++) e.MidiRackAddChain(t, mi);
        yield return (e.MidiRackChainCount(t, mi) == 16 && e.MidiRackAddChain(t, mi) < 0, "a MIDI Effect Rack holds up to 16 chains");
    }

    // The quickstart's MCP walkthrough: an arp above C3, chords below, accents on hard hits.
    private static IEnumerable<(bool, string)> Mcp()
    {
        using var r = new Rig();
        var e = r.E;
        int t = r.T;
        var fx = new Nota.Mcp.Tools.MidiFxTools(e, new SyncDispatch(), new NoRefresh());
        var rack = new Nota.Mcp.Tools.MidiRackTools(e, new SyncDispatch(), new NoRefresh());
        yield return (fx.ListMidiFxKinds().Any(k => k.Kind == RackKind), "list_midi_fx_kinds lists the MIDI Effect Rack");
        int mi = Rig.Mi;
        rack.AddChain(t, mi, "Chords").Wait();
        rack.AddChain(t, mi, "Accents").Wait();
        rack.SetChainName(t, mi, 0, "Arp lead").Wait();
        rack.SetChainZone(t, mi, 0, keyLo: "C3").Wait();
        rack.SetChainZone(t, mi, 1, keyHi: "B2").Wait();
        rack.SetChainZone(t, mi, 2, velLo: 110).Wait();
        rack.SetChainMix(t, mi, 0, transpose: 12).Wait();
        rack.SetChainMix(t, mi, 1, transpose: -12).Wait();
        rack.SetChainMix(t, mi, 2, transpose: 7).Wait();
        rack.SetOutput(t, mi, merge: false).Wait();
        var snap = rack.GetMidiRack(t, mi).Result;
        yield return (snap.Chains.Length == 3 && snap.Chains[0].KeyLo == 48 && snap.Chains[1].KeyHi == 47 && snap.Chains[2].VelLo == 110 && !snap.Merge,
            "zones by note name and output switches land (C3 = 48)");
        string low = r.Play(36), mid = r.Play(64), hard = r.Play(64, 120 / 127f);
        yield return (low == "24" && mid == "76" && hard == "71,76", $"notes follow the zones (C2 → {low}, E4 → {mid}, a hard E4 → {hard})");
        rack.SetMode(t, mi, "select").Wait();
        rack.SetChainZone(t, mi, 0, selLo: 0, selHi: 63).Wait();
        rack.SetSelector(t, mi, 100).Wait();
        string sel = r.Play(64);
        yield return (rack.GetMidiRack(t, mi).Result.Mode == "select" && sel == "64", $"Select mode with the selector outside the chain's range passes the note through ({sel})");
        int map = rack.AddMapping(t, mi, 0, 0, -3, 0, 0f, 1f).Result;
        rack.SetMacro(t, mi, 0, value: 0.2f, name: "Section").Wait();
        var macros = rack.GetMacros(t, mi).Result;
        yield return (map >= 0 && macros[0].Name == "Section" && macros[0].Targets == 1 && Math.Abs(rack.GetMidiRack(t, mi).Result.Selector - 25) <= 1,
            "a macro mapped to the selector drives it");

        // In-place edits over MCP undo one call at a time.
        int tr = e.MidiRackChainTranspose(t, mi, 0), mode = e.MidiRackMode(t, mi);
        float macro = e.MidiRackMacroGet(t, mi, 3);
        rack.SetChainMix(t, mi, 0, transpose: tr + 5).Wait();
        rack.SetMode(t, mi, mode == 2 ? "parallel" : "select").Wait();
        rack.SetMacro(t, mi, 3, value: macro > 0.5f ? 0.1f : 0.9f).Wait();
        bool changed = e.MidiRackChainTranspose(t, mi, 0) == tr + 5 && e.MidiRackMode(t, mi) != mode;
        e.Undo();
        bool macroBack = Math.Abs(e.MidiRackMacroGet(t, mi, 3) - macro) < 1e-4;
        e.Undo();
        bool modeBack = e.MidiRackMode(t, mi) == mode && e.MidiRackChainTranspose(t, mi, 0) == tr + 5;
        e.Undo();
        yield return (changed && macroBack && modeBack && e.MidiRackChainTranspose(t, mi, 0) == tr,
            "set_midi_rack_chain_mix / _mode / _macro each undo as one step");
    }
}
