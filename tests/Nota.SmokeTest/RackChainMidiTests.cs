// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI effects inside Instrument Rack chains (specs/040, US6, issue #329): a chain runs MIDI
// effects → instrument → audio effects, sample-accurate and on the same clock as a track's MIDI
// effects, and keeps them through save, copy and undo.
// Timing is checked by sound: a Sampler on a generated sine is deterministic, so an Arp in a
// rack chain must render the same samples as the same Arp on a plain Sampler track, across a
// loop seam.
// Runs with the other rack sections: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

using System.Globalization;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RackChainMidiTests
{
    private const int Rate = 48000, Block = 512, Arp = 0, Chord = 1, Pentad = 14;

    /// <summary>The smoke-test sections, in run order.</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield return ("racks: chain MIDI effects", ChainMidi);
    }

    private static string Sine()
    {
        string path = Path.Combine(Path.GetTempPath(), "nota-rack-chain-midi-sine.wav");
        WavWriter.WriteSine(path, 2.0, 220.0, Rate);
        return path;
    }

    private static readonly NotaNote[] Chord3 = [new(60, 0, 3.5, 0.8f), new(64, 0, 3.5, 0.8f), new(67, 0, 3.5, 0.8f)];

    // Render seconds of a track playing a held chord from beat 0, looping beats 0–2.
    private static float[] Play(NotaEngine e, int t, double seconds)
    {
        int clip = e.AddMidiClip(t, 0, 4);
        e.SetClipNotes(t, clip, Chord3);
        e.SetLoop(true, 0, 2);
        e.Seek(0);
        e.Play();
        int frames = (int)(seconds * Rate);
        var all = new float[frames * 2];
        var buf = new float[Block * 2];
        for (int done = 0; done < frames; done += Block)
        {
            int n = Math.Min(Block, frames - done);
            e.RenderOffline(buf, n, Rate);
            Array.Copy(buf, 0, all, done * 2, n * 2);
        }
        e.Stop();
        return all;
    }

    private static string Held(NotaEngine e, int t, int c)
    {
        var b = new int[32];
        int n = e.RackChainInstrumentHeldNotes(t, c, b);
        return string.Join(",", b.Take(Math.Max(0, n)).OrderBy(x => x));
    }

    private static void Render(NotaEngine e, int blocks)
    {
        var buf = new float[Block * 2];
        for (int i = 0; i < blocks; i++) e.RenderOffline(buf, Block, Rate);
    }

    private static IEnumerable<(bool, string)> ChainMidi()
    {
        string wav = Sine();

        // 1. Same Arp, same notes, same samples: on a track vs in a rack chain, over a loop seam.
        float[] track, rack;
        using (var e = new NotaEngine())
        {
            int t = e.AddSamplerTrack(wav, 60, false);
            e.AddMidiEffect(t, Arp);
            track = Play(e, t, 3.0);
        }
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            while (e.RackChainCount(t) > 0) e.RackRemoveChain(t, 0);
            int c = e.RackAddSamplerChain(t, wav, 60, false);
            e.RackChainMidiAdd(t, c, Arp);
            rack = Play(e, t, 3.0);
        }
        double diff = 0, peak = 0;
        for (int i = 0; i < Math.Min(track.Length, rack.Length); i++) { diff = Math.Max(diff, Math.Abs(track[i] - rack[i])); peak = Math.Max(peak, Math.Abs(track[i])); }
        yield return (peak > 0.01 && diff < 1e-4,
            string.Create(CultureInfo.InvariantCulture, $"an Arp in a rack chain plays the samples it plays on a track, across a loop seam (max diff {diff:E1}, peak {peak:F2})"));

        // The same through a MIDI Effect Rack chain: a key played again on the loop seam keeps
        // its route, so the Arp in the chain lets go of the old one.
        float[] viaMidiRack;
        using (var e = new NotaEngine())
        {
            int t = e.AddSamplerTrack(wav, 60, false);
            int mi = e.AddMidiEffect(t, 6);
            e.MidiRackChainDeviceAdd(t, mi, 0, Arp);
            viaMidiRack = Play(e, t, 3.0);
        }
        double d2 = 0;
        for (int i = 0; i < Math.Min(track.Length, viaMidiRack.Length); i++) d2 = Math.Max(d2, Math.Abs(track[i] - viaMidiRack[i]));
        yield return (d2 < 1e-4, string.Create(CultureInfo.InvariantCulture, $"an Arp in a MIDI Effect Rack chain plays what it plays on the track, across the seam (max diff {d2:E1})"));

        // 2. A chain's MIDI effects shape only that chain; zones come first.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            while (e.RackChainCount(t) > 0) e.RackRemoveChain(t, 0);
            int a = e.RackAddChain(t, Pentad), b = e.RackAddChain(t, Pentad);
            int ch = e.RackChainMidiAdd(t, a, Chord);
            for (int p = 0; p < e.RackChainMidiParamCount(t, a, ch); p++)
                if (e.RackChainMidiParamName(t, a, ch, p) == "Strum") e.RackChainMidiParamSet(t, a, ch, p, 0f);
            e.TrackNoteOn(t, 60, 0.8f);
            Render(e, 4);
            string stacked = Held(e, t, a), plain = Held(e, t, b);
            e.TrackNoteOff(t, 60);
            Render(e, 8);
            yield return (stacked.Split(',').Length > 1 && plain == "60", $"a chain's Chord stacks only that chain's notes ({stacked} | {plain})");

            e.RackSetChainZone(t, a, 0, 59, 0, 127);
            e.TrackNoteOn(t, 72, 0.8f);
            Render(e, 4);
            string outside = Held(e, t, a);
            e.TrackNoteOff(t, 72);
            Render(e, 8);
            yield return (outside == "" && Held(e, t, a) == "", "the chain's zone picks the notes before its MIDI effects run");

            // A macro drives a chain MIDI effect's parameter (deviceIndex −1000 − j).
            e.RackSetChainZone(t, a, 0, 127, 0, 127);
            int spread = -1;
            for (int p = 0; p < e.RackChainMidiParamCount(t, a, ch); p++) if (e.RackChainMidiParamName(t, a, ch, p) == "Spread") spread = p;
            float lo = e.RackChainMidiParamMin(t, a, ch, spread), hi = e.RackChainMidiParamMax(t, a, ch, spread);
            e.RackAddMacroMapping(t, 0, a, RackMacroMapping.MidiDeviceBase - ch, spread, lo, hi);
            e.RackMacroSet(t, 0, 1f);
            yield return (spread >= 0 && Math.Abs(e.RackChainMidiParamGet(t, a, ch, spread) - hi) < 1e-3, "a macro drives a chain MIDI effect's parameter");

            // A held key survives an undoable edit: its note-off still reaches the chain.
            e.TrackNoteOn(t, 62, 0.8f);
            Render(e, 4);
            e.RackSetMacroName(t, 0, "Spread");
            e.RackChainMidiAdd(t, b, Chord); // b becomes a MIDI chain too
            e.TrackNoteOff(t, 62);
            Render(e, 16);
            yield return (Held(e, t, a) == "" && Held(e, t, b) == "", "no note hangs when the rack is edited while a key is held");

            // 3. The rack blob, duplication and undo keep the chain MIDI effects.
            byte[] blob = e.GetPluginState(t, -1);
            int t2 = e.AddInstrumentRackTrack();
            e.SetPluginState(t2, -1, blob);
            yield return (e.RackChainMidiCount(t2, 0) == 1 && e.RackChainMidiKind(t2, 0, 0) == Chord && e.RackChainMidiCount(t2, 1) == 1,
                "chain MIDI effects ride the rack blob (project and preset)");
            int dup = e.DuplicateTrack(t);
            yield return (dup > 0 && e.RackChainMidiCount(dup, 0) == 1, "duplicating the track keeps chain MIDI effects");
            int n = e.RackChainMidiCount(t, a);
            e.RackChainMidiAdd(t, a, Arp);
            e.Undo();
            yield return (e.RackChainMidiCount(t, a) == n, "adding a chain MIDI effect is one undo step");
            int dc = e.RackDuplicateChain(t, a);
            yield return (dc >= 0 && e.RackChainMidiCount(t, dc) == 1, "duplicating a chain copies its MIDI effects");

            // 4. MCP.
            var tools = new Nota.Mcp.Tools.RackTools(e, new SyncDispatch(), new NoRefresh());
            int added = tools.AddChainMidiFx(t, b, 2).Result;
            var list = tools.ListChainMidiFx(t, b).Result;
            yield return (added == 1 && list.Length == 2 && list[1].Kind == 2, "add_rack_chain_midi_fx / list_rack_chain_midi_fx");
        }

        // 5. A Drum Rack keeps its pads' note handling: no MIDI effects in pads.
        using (var e = new NotaEngine())
        {
            int t = e.AddDrumRackTrack();
            yield return (e.RackChainCount(t) == 0 || e.RackChainMidiAdd(t, 0, Arp) < 0, "a Drum Rack pad takes no MIDI effects");
        }
    }
}
