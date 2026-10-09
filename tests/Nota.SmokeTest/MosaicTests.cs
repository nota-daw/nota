// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic (instrument kind 17): identity and params; the program text round-trip (C# ↔
// engine); asynchronous loading (silent until ready, missing files counted); zone choice by
// key, velocity and round-robin; pitch from the root; release triggers quieter after a long
// hold; the sustain pedal; the polyphony and per-key limits; state / duplicate / swap; MPE
// bend; SFZ import (#define, #include, backslashes, CC articulations, ignored families);
// the file-name mapper (notes, layers, rr, release, octave check, duplicates, gaps).

using Nota.Application;
using Nota.Application.Mosaic;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MosaicTests
{
    private const int Sr = 48000;

    private static int Pi(NotaEngine e, int t, string id)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++) if (e.PluginParamId(t, -1, i) == id) return i;
        return -1;
    }
    private static void P(NotaEngine e, int t, string id, float v) => e.PluginParamSet(t, -1, Pi(e, t, id), v);

    private static float[] Render(NotaEngine e, int frames, int block = 512)
    {
        var all = new float[frames * 2];
        var b = new float[block * 2];
        for (int done = 0; done < frames;)
        {
            int m = Math.Min(block, frames - done);
            e.RenderOffline(b, m, Sr);
            Array.Copy(b, 0, all, done * 2, m * 2);
            done += m;
        }
        return all;
    }
    private static double Rms(float[] st, int from = 0, int to = -1)
    {
        if (to < 0) to = st.Length / 2;
        double s = 0;
        for (int i = from * 2; i < to * 2; i++) s += st[i] * (double)st[i];
        return Math.Sqrt(s / Math.Max(1, (to - from) * 2));
    }
    // The strongest of a few candidate frequencies (Goertzel, mono).
    private static double Energy(float[] st, double hz, int from = 0)
    {
        double w = 2 * Math.PI * hz / Sr, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = from * 2; i < st.Length; i += 2) { double s0 = (st[i] + st[i + 1]) * 0.5 + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }
    private static double Hz(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    // A sine file at a MIDI note (its root), mono, `sec` long with a short fade.
    private static string Tone(string dir, string name, double hz, double sec = 1.0, float amp = 0.5f)
    {
        string path = Path.Combine(dir, name);
        int n = (int)(sec * Sr);
        var d = new float[n * 2];
        for (int i = 0; i < n; i++)
        {
            float env = Math.Min(1f, Math.Min(i / 96f, (n - i) / 480f));
            float s = (float)(amp * env * Math.Sin(2 * Math.PI * hz * i / Sr));
            d[2 * i] = s; d[2 * i + 1] = s;
        }
        using (var w = new Nota.Infrastructure.WavWriter(path, Sr, 2, WavBitDepth.Pcm24)) w.WriteFrames(d, n);
        return path;
    }

    private static bool WaitReady(NotaEngine e, int t, int ms = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (e.TryGetMosaicStatus(t, out var s) && s.State == 2) { Render(e, 256); return true; }
            Thread.Sleep(5);
        }
        return false;
    }

    private static List<MosaicModel.Sounding> Sounding(NotaEngine e, int t)
    {
        var sc = new float[MosaicModel.ScopeLength];
        int n = e.InstrumentScope(t, sc);
        var snap = new MosaicModel.Snapshot();
        MosaicModel.Parse(sc.AsSpan(0, n), snap);
        return snap.Zones.ToList();
    }

    public static IEnumerable<(bool, string)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-mosaic-test-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try { foreach (var r in RunIn(dir)) yield return r; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static IEnumerable<(bool, string)> RunIn(string dir)
    {
        // ---- the samples: roots C4 / G4 × layers p / f, rr 1–2 on C4 f, a release at C4 -----
        string c4p = Tone(dir, "pno_c4_p.wav", Hz(60), 1.0, 0.2f);
        string c4f1 = Tone(dir, "pno_c4_f_rr1.wav", Hz(60), 1.0, 0.6f);
        string c4f2 = Tone(dir, "pno_c4_f_rr2.wav", Hz(60), 1.0, 0.6f);
        string g4p = Tone(dir, "pno_g4_p.wav", Hz(67), 1.0, 0.2f);
        string g4f = Tone(dir, "pno_g4_f.wav", Hz(67), 1.0, 0.6f);
        string rel = Tone(dir, "pno_c4_rel.wav", Hz(84), 0.3, 0.5f);

        var prog = new MosaicProgram { Name = "Test Piano", SourceKind = "files" };
        prog.Files.AddRange(new[] { c4p, c4f1, c4f2, g4p, g4f, rel });
        prog.Groups.Add(new MosaicGroup { Name = "Sustain · p" });
        prog.Groups.Add(new MosaicGroup { Name = "Sustain · f", SeqLen = 2, RrMode = 0 });
        prog.Groups.Add(new MosaicGroup { Name = "Release" });
        prog.Zones.Add(new MosaicZone { File = 0, Group = 0, Root = 60, KeyLo = 0, KeyHi = 63, VelLo = 0, VelHi = 63 });
        prog.Zones.Add(new MosaicZone { File = 1, Group = 1, Root = 60, KeyLo = 0, KeyHi = 63, VelLo = 64, VelHi = 127, Seq = 1 });
        prog.Zones.Add(new MosaicZone { File = 2, Group = 1, Root = 60, KeyLo = 0, KeyHi = 63, VelLo = 64, VelHi = 127, Seq = 2 });
        prog.Zones.Add(new MosaicZone { File = 3, Group = 0, Root = 67, KeyLo = 64, KeyHi = 127, VelLo = 0, VelHi = 63 });
        prog.Zones.Add(new MosaicZone { File = 4, Group = 1, Root = 67, KeyLo = 64, KeyHi = 127, VelLo = 64, VelHi = 127, Seq = 1 });
        prog.Zones.Add(new MosaicZone { File = 4, Group = 1, Root = 67, KeyLo = 64, KeyHi = 127, VelLo = 64, VelHi = 127, Seq = 2 });
        prog.Zones.Add(new MosaicZone { File = 5, Group = 2, Root = 72, KeyLo = 0, KeyHi = 127, Release = true });
        string text = prog.Serialize();

        // ---- the text round-trips ------------------------------------------------------------
        var back = MosaicProgram.Parse(text);
        yield return (back.Serialize() == text && back.Zones.Count == 7 && back.Groups[1].SeqLen == 2 && back.Zones[6].Release,
            "MosaicProgram text round-trips (zones, groups, rr, release)");
        var esc = new MosaicProgram { Name = "Grand 100% = Piano" };
        esc.Files.Add("samples:Downloaded/Osiris Piano/C4 soft.flac");
        esc.Zones.Add(new MosaicZone { File = 0 });
        var esc2 = MosaicProgram.Parse(esc.Serialize());
        yield return (esc2.Name == "Grand 100% = Piano" && esc2.Files[0] == "samples:Downloaded/Osiris Piano/C4 soft.flac",
            "names and paths with spaces, % and = survive the text");
        yield return (MosaicPaths.ToRef("/Users/x/Music/Nota Samples/Downloaded/P/a.wav", "/Users/x/Music/Nota Samples", null) == "samples:Downloaded/P/a.wav"
                      && MosaicPaths.Resolve("samples:Downloaded/P/a.wav", "/S", null) == Path.Combine("/S", "Downloaded", "P", "a.wav"),
            "sample references are relative to the Samples folder");

        using (var e = new NotaEngine())
        {
            int t = e.AddMosaicTrack();
            yield return (t > 0 && e.TrackInstrumentKind(t) == MosaicModel.Kind && e.DeviceName(t, -1) == "Nota Mosaic",
                $"AddMosaicTrack adds kind 17 Nota Mosaic (got {e.TrackInstrumentKind(t)} '{e.DeviceName(t, -1)}')");
            int pc = e.PluginParamCount(t, -1);
            var ids = new HashSet<string>();
            bool names = true;
            for (int i = 0; i < pc; i++) { ids.Add(e.PluginParamId(t, -1, i)); if (e.PluginParamName(t, -1, i).Length == 0) names = false; }
            yield return (pc == 31 && ids.Count == pc && names, $"Nota Mosaic exposes 31 params with unique ids and names (got {pc})");
            yield return (e.TrackInstrumentSupportsMpe(t), "Nota Mosaic reports MPE support");
            yield return (InstrumentView.Index(e, t) >= 0 && !InstrumentView.IsMini(e, t), "Nota Mosaic has an S / L view param and opens as L");
            yield return (MosaicModel.Poly(e.PluginParamGet(t, -1, Pi(e, t, "polyphony"))) == 64 && MosaicModel.PerKey(e.PluginParamGet(t, -1, Pi(e, t, "perkey"))) == 2,
                "defaults: Poly 64, 2 strikes per key");

            // Empty → silent.
            e.TrackNoteOn(t, 60, 0.8f);
            yield return (Rms(Render(e, 4096)) < 1e-6, "an empty Mosaic is silent");
            e.TrackNoteOff(t, 60);
            Render(e, 256);

            yield return (e.MosaicSetProgram(t, text), "the program is accepted");
            yield return (e.MosaicProgram(t) == text, "the engine returns the program text verbatim");
            bool ready = WaitReady(e, t);
            e.TryGetMosaicStatus(t, out var st);
            yield return (ready && st.FilesTotal == 6 && st.FilesDone == 6 && st.Missing == 0 && st.Zones == 7 && st.RamBytes > 0,
                $"the samples load in the background (state {st.State}, {st.FilesDone}/{st.FilesTotal}, {st.Zones} zones, {st.RamBytes} B)");

            // Soft C4: the p zone, at its own pitch.
            e.TrackNoteOn(t, 60, 0.3f);
            var b = Render(e, Sr / 4);
            var z = Sounding(e, t);
            yield return (z.Count == 1 && z[0].Zone == 0 && Rms(b) > 0.01, $"a soft C4 plays the p zone (zones {string.Join(",", z.Select(s => s.Zone))}, rms {Rms(b):F3})");
            yield return (Energy(b, Hz(60), 2000) > 20 * Energy(b, Hz(62), 2000), "the zone plays at its root's pitch on its root");
            e.TrackNoteOff(t, 60);
            Render(e, Sr / 2);

            // D4 from the C4 root: two semitones up.
            e.TrackNoteOn(t, 62, 0.3f);
            b = Render(e, Sr / 4);
            yield return (Energy(b, Hz(62), 2000) > 20 * Energy(b, Hz(60), 2000), "a key away from the root is transposed (D4 from a C4 sample)");
            e.TrackNoteOff(t, 62);
            Render(e, Sr / 2);

            // Loud C4: the f layer, round-robin 1 then 2.
            var seq = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                e.TrackNoteOn(t, 60, 0.9f);
                Render(e, 512);
                seq.Add(Sounding(e, t).Where(s => s.State == 0).Select(s => s.Zone).FirstOrDefault(-1));
                e.TrackNoteOff(t, 60);
                Render(e, Sr / 2);
            }
            yield return (seq.SequenceEqual(new[] { 1, 2, 1 }), $"loud C4 picks the f layer, sequential round-robin 1 → 2 → 1 (got {string.Join(" → ", seq)})");

            // Release trigger: sounds on key-up, quieter after a long hold. (The release sample,
            // an octave above its key, plays at half speed: 0.6 s — let the loop's ones finish.)
            P(e, t, "release", 0f);   // a short amp release so the release sample stands alone
            Render(e, Sr);
            e.TrackNoteOn(t, 60, 0.3f);
            Render(e, 1024);
            e.TrackNoteOff(t, 60);
            var rb = Render(e, Sr / 8);
            var rz = Sounding(e, t);
            double shortRel = Energy(rb, Hz(72));

            yield return (rz.Any(s => s.Zone == 6), $"key-up starts the release zone (zones {string.Join(",", rz.Select(s => s.Zone + ":" + s.State))})");
            Render(e, Sr);
            e.TrackNoteOn(t, 60, 0.3f);
            Render(e, Sr * 2 + 100);
            e.TrackNoteOff(t, 60);
            double longRel = Energy(Render(e, Sr / 8), Hz(72));
            double dropDb = 10 * Math.Log10(Math.Max(1e-12, longRel) / Math.Max(1e-12, shortRel));
            yield return (dropDb < -4.5 && dropDb > -8, $"after a 2 s hold the release sample is about 6 dB quieter ({dropDb:F1} dB)");
            Render(e, Sr);
            P(e, t, "relon", 0f);
            e.TrackNoteOn(t, 60, 0.3f); Render(e, 1024); e.TrackNoteOff(t, 60); Render(e, 256);
            yield return (!Sounding(e, t).Any(s => s.Zone == 6), "Release triggers off: no release zone");
            P(e, t, "relon", 1f);
            Render(e, Sr);

            // Pedal: key-up while CC64 is down keeps the note; lifting it releases.
            e.TrackNoteExpression(t, -1, NoteExpressionDim.Sustain, 1f);
            e.TrackNoteOn(t, 60, 0.3f); Render(e, 1024); e.TrackNoteOff(t, 60); Render(e, 2048);
            var ped = Sounding(e, t);
            var sc = new float[MosaicModel.ScopeLength]; e.InstrumentScope(t, sc);
            yield return (ped.Any(s => s.Zone == 0 && s.State == 1) && sc[MosaicModel.ScPedal] > 0.5f && !ped.Any(s => s.Zone == 6),
                $"with the pedal down a released key keeps sounding, no release sample yet ({string.Join(",", ped.Select(s => s.Zone + ":" + s.State))})");
            e.TrackNoteExpression(t, -1, NoteExpressionDim.Sustain, 0f);
            Render(e, 512);
            yield return (Sounding(e, t).Any(s => s.Zone == 6), "lifting the pedal fires the release sample");
            Render(e, Sr);

            // MPE bend: +2 semitones on a held note.
            e.TrackNoteOn(t, 60, 0.3f);
            e.TrackNoteExpression(t, 60, NoteExpressionDim.Bend, 2f);
            b = Render(e, Sr / 4);
            yield return (Energy(b, Hz(62), 4000) > 10 * Energy(b, Hz(60), 4000), "an MPE bend of +2 st retunes the held note");
            e.TrackNoteOff(t, 60);
            Render(e, Sr);

            // Per key: four strikes of one key ring at most two at a time.
            P(e, t, "relon", 0f);
            for (int k = 0; k < 4; k++) { e.TrackNoteOn(t, 60, 0.3f); Render(e, 256); }
            Render(e, 4096);
            int sameKey = e.InstrumentVoiceCount(t);
            yield return (sameKey == 2, $"Per key 2: four strikes of C4 leave 2 voices (got {sameKey})");
            e.TrackNoteOff(t, 60);
            Render(e, Sr);

            // Polyphony 16: 24 keys hold 16 voices.
            P(e, t, "polyphony", 0f);
            for (int k = 30; k < 54; k++) e.TrackNoteOn(t, k, 0.3f);
            Render(e, 4096);
            int poly = e.InstrumentVoiceCount(t);
            yield return (poly == 16, $"Poly 16: 24 keys keep 16 voices (got {poly})");
            for (int k = 30; k < 54; k++) e.TrackNoteOff(t, k);
            P(e, t, "polyphony", 2f / 3f); P(e, t, "relon", 1f);
            Render(e, Sr);

            // State, duplicate, swap.
            P(e, t, "cutoff", 0.4f);
            var state = e.GetPluginState(t, -1);
            int t2 = e.AddMosaicTrack();
            e.SetPluginState(t2, -1, state);
            yield return (e.MosaicProgram(t2) == text && Math.Abs(e.PluginParamGet(t2, -1, Pi(e, t2, "cutoff")) - 0.4f) < 1e-5 && WaitReady(e, t2),
                "the state carries params + program to another track, which loads it");
            int t3 = e.DuplicateTrack(t);
            yield return (t3 > 0 && e.MosaicProgram(t3) == text && e.TryGetMosaicStatus(t3, out var s3) && s3.State == 2,
                "duplicating the track shares the loaded samples (ready at once)");
            yield return (e.SetTrackBuiltinInstrument(e.AddInstrumentTrack(), 17), "a track's instrument can be swapped to Nota Mosaic");

            // A zone edit (same files) swaps in at once and keeps playing.
            e.TrackNoteOn(t, 60, 0.3f); Render(e, 1024);
            var edited = MosaicProgram.Parse(text); edited.Zones[0].GainDb = -6;
            e.MosaicSetProgram(t, edited.Serialize(), undoable: false);
            e.TryGetMosaicStatus(t, out var se);
            var eb = Render(e, 2048);
            yield return (se.State == 2 && Rms(eb) > 0.005, $"a zone edit is ready at once and the held note keeps sounding (state {se.State}, rms {Rms(eb):F3})");
            e.TrackNoteOff(t, 60);

            // An undoable edit: one undo step brings the old program back.
            var edited2 = MosaicProgram.Parse(text); edited2.Zones[0].Root = 61;
            e.MosaicSetProgram(t, edited2.Serialize());
            bool readyNow = e.TryGetMosaicStatus(t, out var su) && su.State == 2;
            bool changed = MosaicProgram.Parse(e.MosaicProgram(t)).Zones[0].Root == 61;
            e.Undo();
            yield return (readyNow && changed && MosaicProgram.Parse(e.MosaicProgram(t)).Zones[0].Root == 60,
                "an undoable program edit is ready at once and undoes in one step");

            // A missing file is counted; the rest plays.
            var miss = MosaicProgram.Parse(text); miss.Files[3] = Path.Combine(dir, "nope.wav");
            int tm = e.AddMosaicTrack();
            e.MosaicSetProgram(tm, miss.Serialize());
            WaitReady(e, tm);
            e.TryGetMosaicStatus(tm, out var sm);
            yield return (sm.State == 2 && sm.Missing == 1, $"a missing sample is reported (missing {sm.Missing})");

            // Roots resolve references.
            NotaEngine.SetPathRoot("samples", dir);
            var rooted = MosaicProgram.Single("samples:pno_g4_f.wav", 67, "Rooted");
            int tr = e.AddMosaicTrack();
            e.MosaicSetProgram(tr, rooted.Serialize());
            bool rr = WaitReady(e, tr);
            e.TrackNoteOn(tr, 67, 0.8f);
            var rbuf = Render(e, 4096);
            yield return (rr && Rms(rbuf) > 0.01, "a samples: reference resolves against the Samples root");
            e.TrackNoteOff(tr, 67);
        }

        // ---- loading is asynchronous: silent until ready ------------------------------------
        {
            string big = Tone(dir, "big_c4.wav", Hz(60), 30.0);
            using var e = new NotaEngine();
            int t = e.AddMosaicTrack();
            e.MosaicSetProgram(t, MosaicProgram.Single(big, 60, "Big").Serialize());
            e.TryGetMosaicStatus(t, out var s0);
            e.TrackNoteOn(t, 60, 0.8f);
            var early = Render(e, 256);
            bool loadedLater = WaitReady(e, t);
            yield return ((s0.State == 1 && Rms(early) < 1e-6 || s0.State == 2) && loadedLater, $"a program is silent while it loads, then ready (first state {s0.State})");
        }

        // ---- SFZ ---------------------------------------------------------------------------------
        {
            string sfzDir = Path.Combine(dir, "sfz");
            Directory.CreateDirectory(Path.Combine(sfzDir, "Samples"));
            Directory.CreateDirectory(Path.Combine(sfzDir, "Programs", "map"));
            Tone(Path.Combine(sfzDir, "Samples"), "c4_soft.wav", Hz(60), 0.3);
            Tone(Path.Combine(sfzDir, "Samples"), "c4_hard.wav", Hz(60), 0.3);
            Tone(Path.Combine(sfzDir, "Samples"), "c4_rel.wav", Hz(60), 0.2);
            Tone(Path.Combine(sfzDir, "Samples"), "sticks.wav", Hz(60), 0.2);
            Tone(Path.Combine(sfzDir, "Samples"), "brush.wav", Hz(60), 0.2);
            File.WriteAllText(Path.Combine(sfzDir, "Programs", "map", "keys.sfz"),
                "<group> seq_length=2 // round robin\n<region> sample=$S\\C4_SOFT.wav key=c4 hivel=63 seq_position=1 eg06_time=1\n"
                + "<region> sample=$S\\c4_hard.wav key=c4 lovel=64 seq_position=2 amp_velcurve_127=1\n"
                + "/* release */ <group> trigger=release\n<region> sample=$S\\c4_rel.wav lokey=c4 hikey=c#4 pitch_keycenter=60 volume=-9\n");
            File.WriteAllText(Path.Combine(sfzDir, "Programs", "main.sfz"),
                "<control> set_cc110=0 label_cc110=Artic\n#define $S ..\\Samples\n#include \"map/keys.sfz\"\n"
                + "<group> key=d4 group=1 off_by=2 tune_cc1=50\n<region> sample=$S\\sticks.wav locc110=0 hicc110=63\n<region> sample=$S\\brush.wav locc110=64 hicc110=127\n");
            var res = SfzImporter.Import(Path.Combine(sfzDir, "Programs", "main.sfz"));
            var rp = res.Report;
            yield return (rp.Imported == 4 && rp.Inactive == 1 && rp.Includes == 1 && rp.PathsFixed == 4 && rp.Missing == 0,
                $"SFZ import: #include, #define, backslashes, case, CC articulation ({rp.Imported} zones, {rp.Inactive} inactive, {rp.Includes} include, {rp.PathsFixed} paths, {rp.Missing} missing)");
            var zs = res.Program.Zones;
            yield return (zs.Count == 4 && zs[0].Seq == 1 && zs[1].Seq == 2 && res.Program.Groups[zs[0].Group].SeqLen == 2
                          && zs[2].Release && Math.Abs(zs[2].GainDb + 9) < 1e-6 && zs[2].KeyHi == 61 && zs[3].Root == 62 && zs[3].Excl == 1 && zs[3].OffBy == 2,
                "SFZ opcodes map to zones (rr, velocity, release, volume, keys, exclusive group)");
            yield return (rp.Ignored.ContainsKey("eg06*") && rp.Ignored.ContainsKey("tune_cc*") && rp.Ignored.ContainsKey("amp_velcurve*") && rp.CcState.ContainsKey(110),
                $"SFZ import reports ignored opcode families ({string.Join(", ", rp.Ignored.Keys)})");
            var brush = SfzImporter.Import(Path.Combine(sfzDir, "Programs", "main.sfz"), new Dictionary<int, int> { [110] = 100 });
            yield return (brush.Program.Files.Any(f => f.EndsWith("brush.wav")) && !brush.Program.Files.Any(f => f.EndsWith("sticks.wav")),
                "importing again with CC110 = 100 picks the other articulation");
            using var e = new NotaEngine();
            int t = e.AddMosaicTrack();
            e.MosaicSetProgram(t, res.Program.Serialize());
            bool ok = WaitReady(e, t);
            e.TrackNoteOn(t, 60, 0.3f);
            yield return (ok && Rms(Render(e, 4096)) > 0.01, "an imported SFZ plays");
        }

        // ---- factory multisamples: rendered once, mapped by their names, playable -----------------
        {
            var cat = new FactoryPresetCatalog();
            var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == MosaicModel.Kind).ToList();
            yield return (mine.Count >= 10 && mine.Any(p => p.DisplayName == "Init"), $"Nota Mosaic ships factory presets (got {mine.Count})");
            foreach (var src in Nota.Infrastructure.Mosaic.MosaicSources.All)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var fprog = Nota.Infrastructure.Mosaic.MosaicSourceLibrary.Program(src.Id);
                double ms = sw.Elapsed.TotalMilliseconds;
                int roots = fprog?.Roots.Count() ?? 0;
                yield return (fprog is not null && roots == src.Roots.Length && fprog.VelocityLayers == src.Layers.Length
                              && fprog.MaxRoundRobin == Math.Max(1, src.RoundRobin) && fprog.ReleaseZones == (src.Releases ? src.Roots.Length : 0),
                    $"factory multisample '{src.Name}': {fprog?.Summary()} ({ms:0} ms)");
            }
            using var e = new NotaEngine();
            NotaEngine.SetPathRoot("data", NotaPaths.DataDir);
            foreach (var fp in mine.Where(p => p.DisplayName != "Init"))
            {
                int before = e.TrackCount;
                string w = cat.Apply(e, fp.Id, -1);
                int t = 0;
                for (int i = 0; i < e.TrackCount; i++) if (e.TryGetTrackInfo(i, out var ti)) t = ti.Id;
                bool ok = w.Length == 0 && WaitReady(e, t, 10000);
                e.TrackNoteOn(t, 60, 0.8f);
                var b = Render(e, Sr / 2);
                e.TrackNoteOff(t, 60);
                Render(e, Sr);
                double peak = b.Max(Math.Abs);
                yield return (ok && b.All(float.IsFinite) && Rms(b) > 0.01 && peak < 1.2, $"factory preset '{fp.DisplayName}' plays C4 (rms {Rms(b):F3}, peak {peak:F2}{(w.Length > 0 ? ", " + w : "")})");
            }
        }

        // ---- the file-name mapper ------------------------------------------------------------------
        {
            var cases = new (string Name, string Stem, int Note, string Layer, int Rr, bool Rel)[]
            {
                ("trombone_gb4.wav", "trombone", 66, "", 0, false),
                ("trombone_f_c3_rr2.wav", "trombone", 48, "f", 2, false),
                ("trombone_p_n52.wav", "trombone", 52, "p", 0, false),
                ("trombone_a2_rel.wav", "trombone", 45, "", 0, true),
                ("bass_16_1_db2.wav", "bass_16_1", 37, "", 0, false),
                ("all_all_all_f4.wav", "all_all_all", 65, "", 0, false),
                ("Piano C#3 v2 rr3.wav", "piano", 49, "v2", 3, false),
                ("Str_cs4_mf.flac", "str", 61, "mf", 0, false),
                ("trombone_take3.wav", "trombone", -1, "", 0, false),
            };
            foreach (var c in cases)
            {
                var f = MultisampleMapper.ParseName(c.Name);
                yield return (f.Stem == c.Stem && f.NamedNote == c.Note && f.Layer == c.Layer && f.Rr == c.Rr && f.Release == c.Rel,
                    $"name '{c.Name}' → {c.Stem} {MosaicNames.NoteName(c.Note)} {c.Layer} rr{c.Rr}{(c.Rel ? " rel" : "")} (got {f.Stem} {MosaicNames.NoteName(f.NamedNote)} {f.Layer} rr{f.Rr}{(f.Release ? " rel" : "")})");
            }

            string md = Path.Combine(dir, "mapper");
            Directory.CreateDirectory(md);
            // Named one octave low (C3 = 60 convention): the files sound an octave above their names.
            foreach (var (n, midi) in new[] { ("tb_c3_p.wav", 60), ("tb_c3_f.wav", 60), ("tb_e3_p.wav", 64), ("tb_e3_f.wav", 64), ("tb_c4_p.wav", 72), ("tb_c4_f.wav", 72), ("tb_c4_f_copy.wav", 72), ("tb_c3_rel.wav", 60) })
                Tone(md, n, Hz(midi), 0.5);
            Tone(md, "tb_take3.wav", Hz(76), 0.5);   // no note in the name: pitch analysis
            double? Detect(string path)
            {
                var mono = NotaEngine.DecodeMono(path, 1.0, out double sr);
                if (mono is null) return null;
                double hz = AudioPitch.YinHz(mono, mono.Length / 4, 2048, sr);
                return hz > 0 ? 69 + 12 * Math.Log2(hz / 440.0) : null;
            }
            var prop = MultisampleMapper.MapFolder(md, null, Detect);
            var inst = prop.Instruments.FirstOrDefault(i => i.Stem == "tb");
            yield return (prop.OctaveShift == 12 && prop.OctaveWhy.Contains("+12"), $"the octave check shifts a C3 = 60 set by +12 ({prop.OctaveShift}: {prop.OctaveWhy})");
            yield return (inst is not null && inst.Files.Count(f => f.Duplicate) == 1 && inst.Files.Any(f => f.Duplicate && f.Name.Contains("copy")),
                "a second file for the same note / layer is a duplicate (the copy)");
            var take = inst?.Files.FirstOrDefault(f => f.Name == "tb_take3.wav");
            yield return (take is not null && take.NoteFrom == "analysis" && take.Note == 76 && take.Confidence < 80,
                $"a file without a note gets one from analysis, with low confidence ({take?.NoteText} {take?.Confidence})");
            yield return (inst is not null && inst.Layers.SequenceEqual(new[] { "p", "f" })
                          && inst.Files.First(f => f.Name == "tb_c3_p.wav").VelHi == 63 && inst.Files.First(f => f.Name == "tb_c3_f.wav").VelLo == 64,
                "layers split velocity in dynamic order (p 1–63, f 64–127)");
            if (inst is not null)
            {
                var built = MultisampleMapper.Build(inst);
                yield return (built.Groups.Count == 3 && built.Zones.Count(z => z.Release) == 1 && built.VelocityLayers == 2 && built.Zones.Where(z => !z.Release).All(z => z.KeyLo <= z.Root && z.Root <= z.KeyHi),
                    $"the mapped instrument builds a program ({built.Summary()})");
                using var e = new NotaEngine();
                int t = e.AddMosaicTrack();
                e.MosaicSetProgram(t, built.Serialize());
                bool ok = WaitReady(e, t);
                e.TrackNoteOn(t, 74, 0.9f);
                var b = Render(e, Sr / 4);
                yield return (ok && Energy(b, Hz(74), 2000) > 10 * Energy(b, Hz(72), 2000), "a mapped program plays the right pitch between roots");
            }
        }
        // ---- project, presets, packs, MCP -----------------------------------------------------------
        {
            using var e = new NotaEngine();
            int t = e.AddMosaicTrack();
            e.MosaicSetProgram(t, text);
            P(e, t, "cutoff", 0.3f);
            WaitReady(e, t);
            // A project keeps the program (references) and the params; nothing is bundled.
            string bundle = Path.Combine(dir, "proj.nota");
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>()), bundle, e);
            bool bundled = Directory.Exists(Path.Combine(bundle, "samples")) && Directory.EnumerateFiles(Path.Combine(bundle, "samples")).Any();
            using var e2 = new NotaEngine();
            var warns = ProjectService.Apply(ProjectService.Load(bundle), e2, bundle);
            int t2 = 0;
            for (int i = 0; i < e2.TrackCount; i++) if (e2.TryGetTrackInfo(i, out var ti) && e2.TrackInstrumentKind(ti.Id) == MosaicModel.Kind) t2 = ti.Id;
            yield return (t2 > 0 && e2.MosaicProgram(t2) == text && Math.Abs(e2.PluginParamGet(t2, -1, Pi(e2, t2, "cutoff")) - 0.3f) < 1e-5 && WaitReady(e2, t2) && !bundled,
                $"a project reopens its Mosaic: program, params, samples referenced not copied ({string.Join("; ", warns)})");

            // A user preset carries the program; applying it in place loads both.
            var doc = PresetService.Capture(e, t, -1, "Mine");
            int t3 = e.AddMosaicTrack();
            string w = doc is null ? "no doc" : PresetService.ApplyInPlace(doc, e, t3, -1);
            yield return (doc?.MosaicProgram == text && w.Length == 0 && e.MosaicProgram(t3) == text && Math.Abs(e.PluginParamGet(t3, -1, Pi(e, t3, "cutoff")) - 0.3f) < 1e-5,
                "a saved Mosaic preset carries its program and loads it in place");

            // Packs → presets: an SFZ pack and a pack of named files.
            string root = Path.Combine(dir, "presets");
            var lib = new Nota.Infrastructure.Mosaic.MosaicPackLibrary(root);
            int nSfz = lib.ScanPack(Path.Combine(dir, "sfz"), "test-sfz", "1");
            int nNamed = lib.ScanPack(Path.Combine(dir, "mapper"), "test-named", "1");
            int again = lib.ScanPack(Path.Combine(dir, "sfz"), "test-sfz", "1");
            var list = lib.Presets();
            yield return (nSfz == 1 && nNamed == 1 && again == 0 && list.Count == 2 && list.Any(p => p.Source == "sfz") && list.Any(p => p.Source == "folder"),
                $"installed packs become Mosaic presets once per version ({nSfz} sfz, {nNamed} named, rescan {again}; {string.Join(", ", list.Select(p => p.Folder + "/" + p.Name))})");
            var packProg = lib.Read(list.First(p => p.Source == "sfz").Path);
            yield return (packProg is not null && packProg.PackId == "test-sfz" && packProg.Zones.Count == 4, "a pack preset names its pack and holds the imported zones");
            int t4 = e.AddMosaicTrack();
            string w4 = lib.ApplyInPlace(e, list.First(p => p.Source == "folder").Path, t4);
            yield return (w4.Length == 0 && WaitReady(e, t4) && MosaicProgram.Parse(e.MosaicProgram(t4)).Zones.Count > 0, "a pack preset loads into a Mosaic");

            // MCP.
            var mcp = new Nota.Mcp.Tools.MosaicTools(e, new SyncDispatch(), new NoRefresh(), lib, new FactoryPresetCatalog());
            var inst = new Nota.Mcp.Tools.InstrumentTools(e, new SyncDispatch(), new NoRefresh());
            yield return (inst.ListInstrumentKinds().Any(k => k.Kind == 17 && k.Name == "Nota Mosaic") && inst.AddInstrumentTrack(17).Result is var mt && e.TrackInstrumentKind(mt) == 17,
                "MCP add_instrument_track(17) adds a Nota Mosaic");
            var r = mcp.ReadMosaic(t, includeZones: true).Result;
            yield return (r is not null && r.Zones == 7 && r.Groups.Length == 3 && r.ZoneList?.Length == 7 && r.Loading == "ready" && r.VoiceLimit == 64,
                $"read_mosaic reads the program ({r?.Summary})");
            string s1 = mcp.SetMosaic(t, polyphony: 32, perKey: 3, releaseVolumeDb: -12, velocityCurve: -40, filter: "LP", cutoffHz: 2000).Result;
            yield return (!s1.StartsWith("error") && MosaicModel.Poly(e.PluginParamGet(t, -1, Pi(e, t, "polyphony"))) == 32
                          && MosaicModel.PerKey(e.PluginParamGet(t, -1, Pi(e, t, "perkey"))) == 3 && Math.Abs(MosaicModel.RelVolDb(e.PluginParamGet(t, -1, Pi(e, t, "relvol"))) + 12) < 0.1
                          && MosaicModel.VelCurve(e.PluginParamGet(t, -1, Pi(e, t, "velcurve"))) == -40 && Math.Abs(MosaicModel.CutoffHz(e.PluginParamGet(t, -1, Pi(e, t, "cutoff"))) - 2000) < 5,
                $"set_mosaic sets polyphony, per key, release level, velocity curve and filter ({s1})");
            string s2 = mcp.SetMosaicZone(t, 0, root: "C#4", velHi: 50, gainDb: -3).Result;
            var z0 = MosaicProgram.Parse(e.MosaicProgram(t)).Zones[0];
            yield return (z0.Root == 61 && z0.VelHi == 50 && Math.Abs(z0.GainDb + 3) < 1e-6, $"set_mosaic_zone edits a zone ({s2})");
            string s3 = mcp.SetMosaicGroup(t, 1, roundRobinMode: "Random", gainDb: 2).Result;
            var g1 = MosaicProgram.Parse(e.MosaicProgram(t)).Groups[1];
            yield return (g1.RrMode == 1 && Math.Abs(g1.GainDb - 2) < 1e-6, $"set_mosaic_group edits a group ({s3})");
            var imp = mcp.ImportSfz(Path.Combine(dir, "sfz", "Programs", "main.sfz"), -1, "110=100").Result;
            yield return (imp.TrackId > 0 && imp.Imported == 4 && e.MosaicProgram(imp.TrackId).Contains("brush.wav"), $"import_sfz loads an SFZ with an articulation ({imp.Summary})");
            var dry = mcp.CreateMultisample(Path.Combine(dir, "mapper"), dryRun: true).Result;
            yield return (dry.Instruments.Length == 1 && dry.OctaveShift == 12 && dry.TrackId < 0, $"create_multisample previews ({dry.OctaveVerdict})");
            var made = mcp.CreateMultisample(Path.Combine(dir, "mapper"), dryRun: false, savePresets: true).Result;
            yield return (made.TrackId > 0 && made.Presets.Length == 1 && WaitReady(e, made.TrackId), "create_multisample loads the instrument and saves its preset");
            var presets = mcp.ListMosaicPresets().Result;
            yield return (presets.Any(p => p.Kind == "factory" && p.Name == "Felt Piano") && presets.Any(p => p.Kind == "sfz"), $"list_mosaic_presets lists factory and pack presets ({presets.Length})");
            int lt = mcp.LoadMosaicPreset("Glass Mallets").Result;
            yield return (lt > 0 && WaitReady(e, lt, 10000) && mcp.LoadMosaicPreset("No Such Thing").Result == -1, "load_mosaic_preset loads by name (and refuses an unknown one)");
        }

    }
}
