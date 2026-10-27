// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic and the Drum Rack as Instrument Rack chain instruments (specs/041). A Mosaic
// chain loads a program, plays inside its chain's zone, follows a macro, keeps its program
// through the rack blob and undoes a program edit in one step. A Drum Rack nests one level:
// it goes in an Instrument Rack chain and nowhere else, is edited through its nested address
// (NOTA_RACK_NESTED_BASE − chain) and through NestedRackEngineProxy as the Drum Rack card
// sees it, plays its pads for the notes its chain lets through, undoes, and survives the
// blob and a chain copy. Sound is checked on generated sines (deterministic).
// Runs with the other rack sections: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

using Nota.Application;
using Nota.Application.Mosaic;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RackNestingTests
{
    private const int Rate = 48000, Block = 512, Sampler = 1, DrumRack = 4, InstrumentRack = 3, Mosaic = 17;
    private const int Nested = RackMacroMapping.NestedRackBase;

    /// <summary>The smoke-test sections, in run order.</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield return ("racks: Mosaic chain", MosaicChain);
        yield return ("racks: nested Drum Rack", NestedDrumRack);
    }

    private static string Sine(string name, double hz)
    {
        string path = Path.Combine(Path.GetTempPath(), $"nota-rack-nesting-{Environment.ProcessId}-{name}.wav");
        WavWriter.WriteSine(path, 1.0, hz, Rate);
        return path;
    }

    private static double Rms(NotaEngine e, int blocks = 12)
    {
        var buf = new float[Block * 2];
        double s = 0;
        for (int b = 0; b < blocks; b++)
        {
            e.RenderOffline(buf, Block, Rate);
            foreach (float v in buf) s += v * (double)v;
        }
        return Math.Sqrt(s / (blocks * Block * 2));
    }

    // Plays a note for a moment and lets everything ring out; returns the level while it sounds.
    private static double Hit(NotaEngine e, int t, int note)
    {
        e.TrackNoteOn(t, note, 0.9f);
        double r = Rms(e);
        e.TrackNoteOff(t, note);
        Rms(e, 200);
        return r;
    }

    private static bool WaitReady(NotaEngine e, int t, int di, int c)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            if (e.RackDevTryGetChainMosaicStatus(t, di, c, out var s) && s.State == 2) { Rms(e, 1); return true; }
            Thread.Sleep(5);
        }
        return false;
    }

    private static int ParamIndex(NotaEngine e, int t, int c, string id)
    {
        int n = e.RackChainInstrumentParamCount(t, c);
        for (int i = 0; i < n; i++) if (e.RackChainInstrumentParamId(t, c, i) == id) return i;
        return -1;
    }

    private static IEnumerable<(bool, string)> MosaicChain()
    {
        string tone = Sine("c4", 261.63);
        var prog = new MosaicProgram { Name = "Rack Piano", SourceKind = "files" };
        prog.Files.Add(tone);
        prog.Zones.Add(new MosaicZone { File = 0, Root = 60, KeyLo = 0, KeyHi = 127 });
        string text = prog.Serialize();

        using var e = new NotaEngine();
        int t = e.AddInstrumentRackTrack();
        e.RackSetChainMute(t, 0, true);   // the default Volt chain stays quiet
        int c = e.RackAddChain(t, Mosaic);
        yield return (c == 1 && e.RackChainInstrumentKind(t, c) == Mosaic && e.RackChainInstrumentName(t, c) == "Nota Mosaic",
            $"an Instrument Rack chain takes Nota Mosaic (chain {c}, kind {e.RackChainInstrumentKind(t, c)})");
        yield return (e.RackSetChainInstrument(t, 0, Mosaic) && e.RackChainInstrumentKind(t, 0) == Mosaic && e.Undo()
                      && e.RackChainInstrumentKind(t, 0) == 6, "a chain's instrument turns into Mosaic and back by undo");

        e.RackSetChainZone(t, c, 48, 71, 0, 127);
        yield return (e.RackDevChainMosaicSetProgram(t, -1, c, text, undoable: false) && WaitReady(e, t, -1, c),
            "the chain's Mosaic loads a program");
        yield return (e.RackDevChainMosaicProgram(t, -1, c) == text, "the chain's program reads back");
        long zid = e.RackDevChainMosaicZoneSampleId(t, -1, c, 0);
        yield return (zid > 0 && e.TryGetSampleInfo(zid, out var si) && si.Frames > 0,
            "a chain Mosaic's zone sample is found by id (the editor's waveform)");

        double inZone = Hit(e, t, 60), outZone = Hit(e, t, 84);
        yield return (inZone > 0.01 && outZone < 1e-5, $"the Mosaic chain plays inside its zone only (in {inZone:F4}, out {outZone:F6})");

        int vol = ParamIndex(e, t, c, "volume");
        e.RackAddMacroMapping(t, 2, c, -1, vol, 0f, 1f);
        e.RackMacroSet(t, 2, 0.25f);
        yield return (vol >= 0 && Math.Abs(e.RackChainInstrumentParamGet(t, c, vol) - 0.25f) < 1e-3,
            "a rack macro drives the chain Mosaic's volume");

        // The blob carries the program; a fresh rack reloads its samples from the paths.
        var blob = e.GetPluginState(t, -1);
        int t2 = e.AddInstrumentRackTrack();
        e.SetPluginState(t2, -1, blob);
        yield return (e.RackChainInstrumentKind(t2, c) == Mosaic && e.RackDevChainMosaicProgram(t2, -1, c) == text && WaitReady(e, t2, -1, c),
            "a Mosaic chain survives the rack blob (program and samples)");
        yield return (Math.Abs(e.RackChainInstrumentParamGet(t2, c, vol) - 0.25f) < 1e-3, "a Mosaic chain keeps its params through the blob");

        // An undoable program edit is one undo step.
        var p2 = MosaicProgram.Parse(text);
        p2.Name = "Rack Piano 2";
        string text2 = p2.Serialize();
        bool set = e.RackDevChainMosaicSetProgram(t, -1, c, text2, undoable: true);
        bool now = e.RackDevChainMosaicProgram(t, -1, c) == text2;
        yield return (set && now && e.Undo() && e.RackDevChainMosaicProgram(t, -1, c) == text,
            "a chain Mosaic program edit undoes in one step");
        yield return (WaitReady(e, t, -1, c) && Hit(e, t, 60) > 0.01, "the undone Mosaic still plays");

        // The editor's view of the chain: the Mosaic card's calls reach the chain.
        var proxy = RackChainEngineProxy.Create(e, c);
        yield return (proxy.TrackInstrumentKind(t) == Mosaic && proxy.MosaicProgram(t) == text
                      && proxy.TryGetMosaicStatus(t, out var ps) && ps.State == 2 && proxy.MosaicZoneSampleId(t, 0) == e.RackDevChainMosaicZoneSampleId(t, -1, c, 0),
            "the chain view gives the Mosaic editor the chain's program and status");

        int d = e.AddDrumRackTrack();
        int pad = e.RackAddChain(d, Mosaic);
        yield return (pad >= 0 && e.RackChainInstrumentKind(d, pad) == Mosaic, "a Drum Rack pad takes Nota Mosaic");
        try { File.Delete(tone); } catch { }
    }

    private static IEnumerable<(bool, string)> NestedDrumRack()
    {
        string kick = Sine("kick", 110), snare = Sine("snare", 330);
        using var e = new NotaEngine();
        int t = e.AddInstrumentRackTrack();
        e.RackSetChainMute(t, 0, true);
        int c = e.RackAddChain(t, DrumRack);
        int n = Nested - c;
        yield return (c == 1 && e.RackChainInstrumentKind(t, c) == DrumRack && e.RackDevChainCount(t, n) == 0,
            "an Instrument Rack chain takes a Drum Rack, empty");

        // One level only.
        int d = e.AddDrumRackTrack();
        yield return (e.RackAddChain(d, DrumRack) < 0 && e.RackAddChain(t, InstrumentRack) < 0 && e.RackDevAddChain(t, n, DrumRack) < 0,
            "racks nest one level: no Drum Rack on a pad, no Instrument Rack in a chain, nothing deeper");
        yield return (!e.RackSetChainInstrument(t, 0, InstrumentRack) && e.RackDevChainCount(t, Nested - 0) == 0,
            "a chain can't become an Instrument Rack, and a chain without a rack has no nested address");

        int p0 = e.RackDevAddSamplerChain(t, n, kick, 36, false);
        e.RackDevSetChainTriggerNote(t, n, p0, 36);
        int p1 = e.RackDevAddSamplerChain(t, n, snare, 38, false);
        e.RackDevSetChainTriggerNote(t, n, p1, 38);
        yield return (p0 == 0 && p1 == 1 && e.RackDevChainCount(t, n) == 2 && e.RackChainCount(t) == 2,
            "pads go into the nested Drum Rack, not the outer rack");

        e.RackSetChainZone(t, c, 36, 47, 0, 127);
        double pad = Hit(e, t, 36), empty = Hit(e, t, 40), outside = Hit(e, t, 60);
        yield return (pad > 0.01 && empty < 1e-5 && outside < 1e-5,
            $"a nested pad plays inside its chain's zone (pad {pad:F4}, empty note {empty:F6}, outside {outside:F6})");

        e.RackDevSetChainChoke(t, n, p0, 2);
        e.RackDevSetChainTune(t, n, p0, -3);
        e.RackDevSetChainDecay(t, n, p0, 0.4f);
        e.RackDevSetChainName(t, n, p0, "Kick");
        yield return (e.RackDevChainChoke(t, n, p0) == 2 && e.RackDevChainTune(t, n, p0) == -3
                      && Math.Abs(e.RackDevChainDecay(t, n, p0) - 0.4f) < 1e-4 && e.RackDevChainName(t, n, p0) == "Kick",
            "nested pad shaping and names read back");

        // Undo: a nested structural edit is one step and leaves the outer rack alone.
        int p2 = e.RackDevAddChain(t, n, Sampler);
        yield return (p2 == 2 && e.Undo() && e.RackDevChainCount(t, n) == 2 && e.RackChainCount(t) == 2 && e.RackChainInstrumentKind(t, c) == DrumRack,
            "adding a nested pad undoes in one step");
        yield return (Hit(e, t, 36) > 0.01, "the nested kit still plays after the undo");

        // Outer macro → the nested rack's macro 2 (its chain instrument param 1) → pad decay.
        int nm = e.RackDevAddMacroMapping(t, n, 1, p0, RackMacroMapping.PadControls, RackMacroMapping.PadDecay, 0f, 1f);
        int om = e.RackAddMacroMapping(t, 0, c, -1, 1, 0f, 1f);
        e.RackMacroSet(t, 0, 0.3f);
        yield return (nm >= 0 && om >= 0 && Math.Abs(e.RackDevMacroGet(t, n, 1) - 0.3f) < 1e-3 && Math.Abs(e.RackDevChainDecay(t, n, p0) - 0.3f) < 1e-3,
            "an outer macro drives a nested macro, which drives a pad");

        // The blob carries the nested kit, samples included.
        var blob = e.GetPluginState(t, -1);
        int t2 = e.AddInstrumentRackTrack();
        e.SetPluginState(t2, -1, blob);
        bool shape = e.RackDevChainCount(t2, n) == 2 && e.RackDevChainTriggerNote(t2, n, 1) == 38 && e.RackDevChainChoke(t2, n, 0) == 2
                     && e.RackDevChainName(t2, n, 0) == "Kick" && e.RackDevMappingCount(t2, n) == 1 && e.RackMappingCount(t2) == 1;
        bool audio = e.RackDevChainSamplerInfo(t2, n, 0, out var si) && si.SampleId > 0 && e.TryGetSampleInfo(si.SampleId, out var inf) && inf.Frames > 0;
        yield return (shape && audio, $"a nested Drum Rack survives the blob (shape {shape}, audio {audio})");

        // Copying the chain copies the kit.
        int c2 = e.RackDuplicateChain(t, c);
        yield return (c2 == c + 1 && e.RackChainInstrumentKind(t, c2) == DrumRack && e.RackDevChainCount(t, Nested - c2) == 2,
            "duplicating a Drum Rack chain copies its kit");

        // The Drum Rack card's view: its Rack* calls land on the nested rack.
        var proxy = NestedRackEngineProxy.Create(e, c);
        proxy.RackSetChainTune(t, p1, 5);
        bool redirected = proxy.RackChainCount(t) == 2 && e.RackDevChainTune(t, n, p1) == 5 && e.RackChainCount(t) == 3
                          && proxy.TrackInstrumentKind(t) == DrumRack && proxy.RackChainTriggerNote(t, p1) == 38;
        bool macros = Math.Abs(proxy.PluginParamGet(t, -1, 1) - e.RackDevMacroGet(t, n, 1)) < 1e-6 && proxy.PluginParamCount(t, -1) == 8;
        bool noTwin = proxy.RackChainMidiCount(t, 0) == 0 && !proxy.RackMono(t);
        yield return (redirected && macros && noTwin,
            $"the nested view sends the Drum Rack card's calls to the nested rack (calls {redirected}, macros {macros}, no-twin defaults {noTwin})");
        proxy.RackTryGetMapping(t, 0, out var mm);
        yield return (mm.Chain == p0 && mm.DeviceIndex == RackMacroMapping.PadControls, "out parameters come back through the nested view");

        // A chain that changes its instrument leaves no nested rack behind its address.
        e.RackSetChainInstrument(t, c2, Sampler);
        yield return (e.RackDevChainCount(t, Nested - c2) == 0 && e.RackDevAddChain(t, Nested - c2, Sampler) < 0,
            "a nested address with no Drum Rack behind it does nothing");
        try { File.Delete(kick); File.Delete(snare); } catch { }
    }
}
