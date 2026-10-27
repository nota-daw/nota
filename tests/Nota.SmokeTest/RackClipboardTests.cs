// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Moving, copying and pasting devices in rack chains (specs/042). A rack chain device moves
// within its section and onto another chain of the same rack, keeping its macro mappings. It
// copies, pastes and duplicates through the engine's one device clipboard, the track chain's
// too, so a device crosses between the track chain and any rack chain with its settings.
// Every edit is one undo step, and what a section can't take is refused with a reason code.
// Runs with the other rack sections: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RackClipboardTests
{
    private const int Eq3 = 16, Compressor = 1, Reverb = 2, Delay = 3, Arp = 0, Chord = 1, Pentad = 14;
    private const int MidiRack = 6;

    /// <summary>The smoke-test sections, in run order.</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield return ("racks: chain device clipboard", Run);
        yield return ("racks: review fixes (#348)", Review);
    }

    // PR #348 review: a rack's in-place edits undo after a checkpoint, and undo puts back what a
    // nested Drum Rack's macros drive.
    private static IEnumerable<(bool, string)> Review()
    {
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int mi = e.AddMidiEffect(t, MidiRack);
            e.ClearUndoHistory();
            int tr0 = e.MidiRackChainTranspose(t, mi, 0);
            e.RackDevCheckpoint(t, -1000 - mi);
            e.MidiRackSetChainTranspose(t, mi, 0, tr0 + 7);
            bool set = e.MidiRackChainTranspose(t, mi, 0) == tr0 + 7;
            e.Undo();
            yield return (set && e.MidiRackChainTranspose(t, mi, 0) == tr0, "a MIDI Effect Rack value edit after a checkpoint is one undo step");

            int it = e.AddInstrumentRackTrack();
            e.ClearUndoHistory();
            float v0 = e.RackVolume(it);
            e.RackDevCheckpoint(it, -1);
            e.RackSetVolume(it, v0 * 0.5f);
            e.Undo();
            yield return (Math.Abs(e.RackVolume(it) - v0) < 1e-5, "an Instrument Rack's volume after a checkpoint undoes");
        }

        // A nested Drum Rack: a mapping range edit, then Undo, puts the pad's decay back.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            int c = e.RackAddChain(t, 4);
            int n = RackMacroMapping.NestedRackBase - c;
            int pad = e.RackDevAddChain(t, n, 1);
            int map = e.RackDevAddMacroMapping(t, n, 0, pad, RackMacroMapping.PadControls, RackMacroMapping.PadDecay, 0.2f, 0.4f);
            e.RackDevMacroSet(t, n, 0, 1f);
            float before = e.RackDevChainDecay(t, n, pad);
            e.ClearUndoHistory();
            e.RackDevSetMappingRange(t, n, map, 0.2f, 0.9f);
            e.RackDevMacroSet(t, n, 0, 1f);
            float edited = e.RackDevChainDecay(t, n, pad);
            e.Undo();
            float after = e.RackDevChainDecay(t, n, pad);
            yield return (Math.Abs(before - 0.4f) < 1e-3 && Math.Abs(edited - 0.9f) < 1e-3 && Math.Abs(after - before) < 1e-3,
                $"undo of a nested mapping edit puts the pad back ({before:F2} → {edited:F2} → {after:F2})");
            yield return (e.RackDevChainId(t, -1, c) != 0 && e.RackDevChainId(t, -1, c) != e.RackDevChainId(t, -1, e.RackAddChain(t, 14)),
                "each chain has its own id");
        }

        // A chain keeps its id through an edit inside its nested Drum Rack (which forks it) and
        // when the chain in front of it goes.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            e.RackAddChain(t, 14);
            int c = e.RackAddChain(t, 4);   // chain 1
            long id = e.RackDevChainId(t, -1, c);
            e.RackDevAddChain(t, RackMacroMapping.NestedRackBase - c, 1);
            bool keptOnEdit = e.RackDevChainId(t, -1, c) == id;
            e.RackRemoveChain(t, 0);       // the Drum Rack moves up to chain 0
            int now = Enumerable.Range(0, e.RackChainCount(t)).Where(i => e.RackDevChainId(t, -1, i) == id).DefaultIfEmpty(-1).First();
            yield return (c == 1 && keptOnEdit && now == 0 && e.RackChainInstrumentKind(t, now) == 4,
                $"a chain keeps its id through nested edits and moves (edit {keptOnEdit}, now chain {now})");
        }

        // An edit inside a nested Drum Rack doesn't re-prepare its playing pad effects: a reverb
        // tail rings on through it.
        using (var e = new NotaEngine())
        {
            string wav = Path.Combine(Path.GetTempPath(), "nota-rack-review-hit.wav");
            WavWriter.WriteSine(wav, 0.08, 220.0, 48000);
            int t = e.AddInstrumentRackTrack();
            int c = e.RackAddChain(t, 4);
            int n = RackMacroMapping.NestedRackBase - c;
            int pad = e.RackDevAddSamplerChain(t, n, wav, 36, false);
            e.RackDevSetChainTriggerNote(t, n, pad, 36);
            e.RackDevAddChainDevice(t, n, pad, Reverb);
            var buf = new float[512 * 2];
            double Rms() { e.RenderOffline(buf, 512, 48000); double q = 0; foreach (var x in buf) q += x * x; return Math.Sqrt(q / buf.Length); }
            e.TrackNoteOn(t, 36, 1f);
            for (int i = 0; i < 6; i++) Rms();
            e.TrackNoteOff(t, 36);
            for (int i = 0; i < 6; i++) Rms();   // the sample is over: only the tail is left
            double before = Rms();
            e.RackDevAddChain(t, n, 1);          // a nested edit: the Drum Rack forks
            double after = Rms();
            yield return (before > 1e-4 && after > before * 0.3, $"a reverb tail rings on through a nested edit ({before:E1} → {after:E1})");
        }

        // A factory Grain preset applied to a chain loads its source sample into the chain's Grain.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            int c = e.RackAddChain(t, 10);
            var factory = new FactoryPresetCatalog();
            var grain = factory.All().FirstOrDefault(p => p.IsInstrument && p.BuiltinKind == 10 && p.DisplayName != "Init");
            string warn = grain.Id is null ? "no Grain preset" : factory.ApplyInPlace(RackChainEngineProxy.Create(e, c), grain.Id, t, -1);
            yield return (warn == "" && e.RackChainInstrumentKind(t, c) == 10, $"a Grain preset in a chain loads its sample ({(warn == "" ? "loaded" : warn)})");
        }
    }

    // A device's params as one comparable line.
    private static string Params(NotaEngine e, int t, int di, int c, int d)
    {
        int n = e.RackDevChainDeviceParamCount(t, di, c, d);
        return string.Join(";", Enumerable.Range(0, n).Select(p => e.RackDevChainDeviceParamGet(t, di, c, d, p).ToString("F4")));
    }

    private static string TrackParams(NotaEngine e, int t, int d)
        => string.Join(";", Enumerable.Range(0, e.DeviceParamCount(t, d)).Select(p => e.DeviceGetParam(t, d, p).ToString("F4")));

    private static string Names(NotaEngine e, int t, int di, int c)
        => string.Join(",", Enumerable.Range(0, e.RackDevChainDeviceCount(t, di, c)).Select(d => e.RackDevChainDeviceName(t, di, c, d).Replace("Nota ", "")));

    // Nudge every param of a chain device off its default so a copy is recognisable.
    private static void Tweak(NotaEngine e, int t, int di, int c, int d)
    {
        for (int p = 0; p < e.RackDevChainDeviceParamCount(t, di, c, d); p++)
        {
            float lo = e.RackDevChainDeviceParamMin(t, di, c, d, p), hi = e.RackDevChainDeviceParamMax(t, di, c, d, p);
            e.RackDevChainDeviceParamSet(t, di, c, d, p, lo + (hi - lo) * 0.37f);
        }
    }

    private static IEnumerable<(bool, string)> Run()
    {
        // ---- US1: move within a chain and to another chain, mappings follow ----
        using (var e = new NotaEngine())
        {
            int t = e.AddAudioTrack();
            int di = e.AddBuiltinDevice(t, 5);
            while (e.RackDevChainCount(t, di) > 0) e.RackDevRemoveChain(t, di, 0);
            int a = e.RackDevAddChain(t, di, -1), b = e.RackDevAddChain(t, di, -1);
            e.RackDevAddChainDevice(t, di, a, Eq3);
            int comp = e.RackDevAddChainDevice(t, di, a, Compressor);
            e.RackDevAddChainDevice(t, di, a, Reverb);
            int thr = Enumerable.Range(0, e.RackDevChainDeviceParamCount(t, di, a, comp)).First(p => e.RackDevChainDeviceParamName(t, di, a, comp, p).StartsWith("Thresh"));
            float lo = e.RackDevChainDeviceParamMin(t, di, a, comp, thr), hi = e.RackDevChainDeviceParamMax(t, di, a, comp, thr);
            e.RackDevAddMacroMapping(t, di, 0, a, comp, thr, lo, hi);
            e.ClearUndoHistory();

            e.RackDevMoveChainDevice(t, di, a, 2, 0);
            string moved = Names(e, t, di, a);
            e.Undo();
            yield return (moved == "Reverb,EQ-3,Compressor" && Names(e, t, di, a) == "EQ-3,Compressor,Reverb",
                $"a chain device moves within its chain, one undo step ({moved})");

            e.RackDevMoveChainDevice(t, di, a, 1, 2);   // Compressor last
            e.RackDevMacroSet(t, di, 0, 1f);
            yield return (Math.Abs(e.RackDevChainDeviceParamGet(t, di, a, 2, thr) - hi) < 1e-3, "a macro still drives a moved device");

            string before = Params(e, t, di, a, 2);
            int to = e.RackDevChainDeviceMoveTo(t, di, a, 2, b);
            e.RackDevMacroSet(t, di, 0, 0f);
            bool there = to == 0 && Names(e, t, di, b) == "Compressor" && Names(e, t, di, a) == "EQ-3,Reverb";
            bool driven = Math.Abs(e.RackDevChainDeviceParamGet(t, di, b, 0, thr) - lo) < 1e-3;
            yield return (there && driven, $"a device moves to another chain with its mapping ({Names(e, t, di, a)} | {Names(e, t, di, b)}, driven {driven})");
            e.Undo();
            yield return (Names(e, t, di, a) == "EQ-3,Reverb,Compressor" && Names(e, t, di, b) == "",
                "moving to another chain is one undo step");
            yield return (e.RackDevChainDeviceMoveTo(t, di, a, 0, a) == -1, "a device can't move to its own chain");
        }

        // MIDI effects between Instrument Rack chains; MIDI effects stay in the first 16 chains.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            int a = e.RackAddChain(t, Pentad), b = e.RackAddChain(t, Pentad);
            e.RackChainMidiAdd(t, a, Arp);
            e.RackChainMidiAdd(t, a, Chord);
            int j = e.RackDevChainMidiMoveTo(t, -1, a, 0, b);
            yield return (j == 0 && e.RackChainMidiCount(t, a) == 1 && e.RackChainMidiKind(t, b, 0) == Arp,
                "a chain MIDI effect moves to another chain");
            while (e.RackChainCount(t) < 17) e.RackAddChain(t, Pentad);
            yield return (e.RackDevChainMidiMoveTo(t, -1, a, 0, 16) == -5, "a MIDI effect can't move past chain 16 (−5)");
        }

        // ---- US2: copy, paste, duplicate and refusals inside racks ----
        using (var e = new NotaEngine())
        {
            int t = e.AddAudioTrack();
            int di = e.AddBuiltinDevice(t, 5);
            while (e.RackDevChainCount(t, di) > 0) e.RackDevRemoveChain(t, di, 0);
            int a = e.RackDevAddChain(t, di, -1), b = e.RackDevAddChain(t, di, -1);
            int rv = e.RackDevAddChainDevice(t, di, a, Reverb);
            Tweak(e, t, di, a, rv);
            int bd = e.RackDevAddChainDevice(t, di, b, Eq3);
            e.RackDevAddMacroMapping(t, di, 1, b, bd, 0, 0f, 1f);
            e.ClearUndoHistory();

            bool copied = e.RackDevChainDeviceCopy(t, di, a, rv);
            int p1 = e.RackDevChainDevicePaste(t, di, b, -1);
            yield return (copied && p1 == 1 && Params(e, t, di, b, 1) == Params(e, t, di, a, rv) && e.DeviceClipboardKind() == 1,
                "a chain device copies into another chain with its settings");
            int maps = e.RackDevMappingCount(t, di);
            e.Undo();
            yield return (e.RackDevChainDeviceCount(t, di, b) == 1 && maps == 1, "a paste is one undo step and brings no mappings");

            int p0 = e.RackDevChainDevicePaste(t, di, b, 0);
            e.RackDevMacroSet(t, di, 1, 1f);
            bool shifted = p0 == 0 && Names(e, t, di, b) == "Reverb,EQ-3" && e.RackDevTryGetMapping(t, di, 0, out var mm) && mm.Chain == b && mm.DeviceIndex == 1;
            yield return (shifted, "pasting in front shifts the chain's mappings with their devices");

            int dup = e.RackDevChainDeviceDuplicate(t, di, b, 0);
            yield return (dup == 1 && Names(e, t, di, b) == "Reverb,Reverb,EQ-3" && Params(e, t, di, b, 1) == Params(e, t, di, b, 0) && e.DeviceClipboardKind() == 1,
                "duplicate puts a copy right after the device and leaves the clipboard");
            e.Undo();
            yield return (Names(e, t, di, b) == "Reverb,EQ-3", "duplicate is one undo step");

            // Refusals: the other family, a rack in a rack.
            int it = e.AddInstrumentTrack();
            e.AddMidiEffect(it, Arp);
            e.CopyMidiEffect(it, 0);
            yield return (e.RackDevChainDevicePaste(t, di, a, -1) == -2 && e.RackDevChainMidiPaste(t, di, a, -1) == -2,
                "a MIDI effect doesn't paste into an Audio Effect Rack (−2)");
            e.CopyDevice(t, di);
            yield return (e.RackDevChainDevicePaste(t, di, a, -1) == -3, "a rack doesn't paste into a rack chain (−3)");
        }

        // A MIDI Effect Rack: its Arp copies between its chains.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int mi = e.AddMidiEffect(t, MidiRack);
            e.MidiRackAddChain(t, mi);
            int arp = e.MidiRackChainDeviceAdd(t, mi, 0, Arp);
            e.MidiRackChainDeviceParamSet(t, mi, 0, arp, 0, e.MidiRackChainDeviceParamMax(t, mi, 0, arp, 0));
            int rack = -1000 - mi;
            e.RackDevChainMidiCopy(t, rack, 0, arp);
            int p = e.RackDevChainMidiPaste(t, rack, 1, -1);
            bool same = p == 0 && Enumerable.Range(0, e.MidiRackChainDeviceParamCount(t, mi, 0, arp))
                .All(q => Math.Abs(e.MidiRackChainDeviceParamGet(t, mi, 0, arp, q) - e.MidiRackChainDeviceParamGet(t, mi, 1, 0, q)) < 1e-5);
            yield return (same, "an Arp copies between MIDI Effect Rack chains with its settings");
            e.CopyDevice(t, -1);   // nothing there: the clipboard keeps the Arp
            e.AddBuiltinDevice(t, Delay);
            e.CopyDevice(t, 0);
            yield return (e.RackDevChainDevicePaste(t, rack, 0, -1) == -2, "an audio effect doesn't paste into a MIDI Effect Rack (−2)");
        }

        // ---- US3: across the track chain and rack chains ----
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            int c = e.RackAddChain(t, Pentad);
            int d = e.AddBuiltinDevice(t, Delay);
            for (int p = 0; p < e.DeviceParamCount(t, d); p++)
                e.DeviceSetParam(t, d, p, e.DeviceParamMin(t, d, p) + (e.DeviceParamMax(t, d, p) - e.DeviceParamMin(t, d, p)) * 0.41f);
            e.CopyDevice(t, d);
            int r = e.RackDevChainDevicePaste(t, -1, c, -1);
            yield return (r == 0 && Params(e, t, -1, c, 0) == TrackParams(e, t, d), "a track-chain effect pastes into a rack chain with its settings");

            int eq = e.RackAddChainDevice(t, c, Eq3);
            Tweak(e, t, -1, c, eq);
            e.RackDevChainDeviceCopy(t, -1, c, eq);
            int back = e.PasteDevice(t, -1);
            yield return (back == 1 && TrackParams(e, t, back) == Params(e, t, -1, c, eq), "a rack chain effect pastes onto the track chain with its settings");

            int arp = e.AddMidiEffect(t, Arp);
            e.MidiEffectSetParam(t, arp, 0, e.MidiEffectParamMax(t, arp, 0));
            e.CopyMidiEffect(t, arp);
            int rm = e.RackDevChainMidiPaste(t, -1, c, -1);
            yield return (rm == 0 && Math.Abs(e.RackChainMidiParamGet(t, c, 0, 0) - e.MidiEffectGetParam(t, arp, 0)) < 1e-5,
                "a track MIDI effect pastes into an Instrument Rack chain");

            // A keyed compressor loses its key in a chain; the source keeps it.
            int src = e.AddAudioTrack();
            int k = e.AddBuiltinDevice(t, Compressor);
            e.SetDeviceSidechainSource(t, k, src);
            e.CopyDevice(t, k);
            bool keyed = e.DeviceClipboardSidechain();
            int kc = e.RackDevChainDevicePaste(t, -1, c, -1);
            yield return (keyed && kc >= 0 && e.DeviceSidechainSource(t, k) == src, "a keyed effect pastes into a chain without its key; the source keeps it");

            // Duplicate on the track chain: one undo step each.
            e.ClearUndoHistory();
            int n = e.TrackDeviceCount(t), m = e.TrackMidiEffectCount(t);
            int dd = e.DuplicateDevice(t, d);
            int dm = e.DuplicateMidiEffect(t, arp);
            bool dupOk = dd == d + 1 && dm == arp + 1 && e.TrackDeviceCount(t) == n + 1 && e.TrackMidiEffectCount(t) == m + 1;
            e.Undo(); e.Undo();
            yield return (dupOk && e.TrackDeviceCount(t) == n && e.TrackMidiEffectCount(t) == m, "duplicate on the track chain, one undo step each");
        }
    }
}
