// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Aurora custom wavetables (specs/053): how a file becomes a table, the undo and source
// rules, the state blob and its trailer, copies and duplicates, the sound (an imported copy
// of a built-in frame sounds like it; a bright table played high does not fold back), the
// project v29 round trip (bit-identical render, one file per table, missing tables, v27
// documents), presets, and the live-follow telemetry. An Aurora without a table keeps its
// old state blob byte for byte and the factory sounds pass the null gate.
// Also runnable alone: `--aurora-tables`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AuroraTableTests
{
    private const int Sr = 48000, Frame = 2048, Params = 156;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-aurora-" + Guid.NewGuid().ToString("N"));
        Dictionary<string, string> fx;
        try { fx = AuroraFixtures.Write(Path.Combine(dir, "fixtures")); }
        catch (Exception ex) { fx = new(); Console.WriteLine($"  (fixtures failed: {ex.Message})"); }
        yield return (fx.Count >= 10, $"aurora tables: fixtures written ({fx.Count})");
        if (fx.Count < 10) yield break;
        try
        {
            foreach (var r in Regression()) yield return r;
            foreach (var r in Conditioning(fx)) yield return r;
            foreach (var r in UndoAndSource(fx)) yield return r;
            foreach (var r in State(fx)) yield return r;
            foreach (var r in Sound()) yield return r;
            foreach (var r in Project(fx, dir)) yield return r;
            foreach (var r in Presets(fx)) yield return r;
            foreach (var r in Scope(fx)) yield return r;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---- T017: nothing changes for an Aurora without a table ----
    private static IEnumerable<(bool, string)> Regression()
    {
        using var e = new NotaEngine();
        int t = e.AddWavetableSynthTrack();
        var blob = e.GetPluginState(t, -1);
        yield return (blob.Length == Params * 4, $"aurora tables: a fresh Aurora's state is the {Params}-float layout ({blob.Length} bytes)");
        yield return (e.AuroraState(t, false).AsSpan().SequenceEqual(blob) && e.AuroraState(t, true).AsSpan().SequenceEqual(blob),
            "aurora tables: with no table the blob is the same with or without tables");
        bool paramsOnly = true;
        for (int i = 0; i < Params && paramsOnly; i++)
            paramsOnly = BitConverter.ToSingle(blob, i * 4) == e.PluginParamGet(t, -1, i);
        yield return (paramsOnly, "aurora tables: the blob is exactly the parameters, in order");

        var h = Build(e, Saw64(), 1, "saw");
        e.AuroraSetTable(t, 0, h); e.AuroraTableFree(h);
        e.AuroraClearTable(t, 0);
        yield return (e.GetPluginState(t, -1).AsSpan().SequenceEqual(blob),
            "aurora tables: import then remove leaves the blob byte-identical to before");

        // The built-in path is untouched: a table that is loaded but not played changes nothing.
        var a = RenderChord(setup: null);
        var b = RenderChord(setup: (eng, tr) =>
        {
            var th = Build(eng, AuroraFixtures.Morph(64), 64, "morph");
            eng.AuroraSetTable(tr, 0, th, undoable: false); eng.AuroraTableFree(th);
            eng.AuroraSetSource(tr, 0, false);
        });
        double res = ResidualDb(a, b);
        yield return (a.Length > 0 && res < -120, $"aurora tables: a held-but-unplayed table renders bit-identically to none (residual {res:0} dBFS)");
    }

    // ---- T025: files to tables ----
    private static IEnumerable<(bool, string)> Conditioning(Dictionary<string, string> fx)
    {
        using var e = new NotaEngine();
        (int code, AuroraTableInfo info, float[] frames) B(string name)
        {
            int r = e.AuroraTableBuild(fx[name], out var h);
            if (r != 0) return (r, default, []);
            try { return (r, e.AuroraTableInfoOf(h), e.AuroraTableFrames(h)); }
            finally { e.AuroraTableFree(h); }
        }

        var c = B("cycle-2048.wav");
        yield return (c.code == 0 && c.info is { Frames: 1, Origin: 1 }, $"aurora tables: a 2048-sample file is one cycle ({c.code}, {c.info.Frames} frames, origin {c.info.Origin})");
        var s6 = B("cycle-600.wav");
        yield return (s6.code == 0 && s6.info is { Frames: 1, Origin: 1 } && s6.frames.Length == Frame,
            $"aurora tables: a 600-sample cycle is resampled to one 2048 frame ({s6.info.Frames})");
        var t64 = B("table-64x2048.wav");
        yield return (t64.code == 0 && t64.info is { Frames: 64, Origin: 0, TruncatedFrames: false },
            $"aurora tables: a clm wavetable keeps its 64 frames ({t64.info.Frames}, origin {t64.info.Origin})");
        var t300 = B("table-300x2048.wav");
        yield return (t300.code == 0 && t300.info is { Frames: 256, TruncatedFrames: true },
            $"aurora tables: 300 frames are cut to 256 and say so ({t300.info.Frames}, truncated {t300.info.TruncatedFrames})");
        var t256 = B("table-256x2048.wav");
        yield return (t256.code == 0 && t256.info is { Frames: 256, Origin: 0, TruncatedFrames: false },
            $"aurora tables: 256 × 2048 samples with no clm chunk is a wavetable ({t256.info.Frames}, origin {t256.info.Origin})");
        var r40 = B("rec-40s-2048x.wav");
        yield return (r40.code == 0 && r40.info is { Frames: 16, Origin: 2, TruncatedLength: false },
            $"aurora tables: a 40 s recording on a multiple of 2048 is a recording, not 256 raw frames ({r40.info.Frames}, origin {r40.info.Origin})");
        var lng = B("long-90s.wav");
        yield return (lng.code == 0 && lng.info is { Frames: 16, Origin: 2, TruncatedLength: true },
            $"aurora tables: a 90 s recording gives 16 frames from its first 60 s ({lng.info.Frames}, origin {lng.info.Origin}, cut {lng.info.TruncatedLength})");
        var vow = B("vowel-2s.flac");
        yield return (vow.code == 0 && vow.info is { Frames: 16, Origin: 2, TruncatedLength: false },
            $"aurora tables: a stereo FLAC recording gives 16 frames ({vow.code}, {vow.info.Frames})");
        yield return (B("silence.wav").code == AuroraTableResult.ErrSilent, "aurora tables: silence is refused as silent");
        yield return (B("broken.wav").code == AuroraTableResult.ErrDecode, "aurora tables: a broken WAV is refused as unreadable");
        yield return (B("x.aiff").code == AuroraTableResult.ErrFormat, "aurora tables: AIFF is refused as an unsupported format");
        yield return (e.AuroraTableBuild(Path.Combine(Path.GetDirectoryName(fx["x.aiff"])!, "nope.wav"), out _) == AuroraTableResult.ErrDecode,
            "aurora tables: a file that is not there is refused as unreadable");

        foreach (var (name, r) in new[] { ("cycle", c), ("64-frame", t64), ("recording", lng), ("flac", vow) })
        {
            double worstMean = 0, peak = 0;
            int n = r.frames.Length / Frame;
            for (int f = 0; f < n; f++)
            {
                double m = 0;
                for (int i = 0; i < Frame; i++) { m += r.frames[f * Frame + i]; peak = Math.Max(peak, Math.Abs(r.frames[f * Frame + i])); }
                worstMean = Math.Max(worstMean, Math.Abs(m / Frame));
            }
            yield return (n > 0 && worstMean < 1e-6 && Math.Abs(peak - 1.0) < 1e-6,
                $"aurora tables: {name} frames have no DC and the table peaks at 1.0 (mean {worstMean:0.0e0}, peak {peak:0.000000})");
        }

        // A recording's loop seam is no bigger a step than the frame's own motion.
        var sw = B("sweep-3s.wav");
        bool seamOk = sw.code == 0;
        double worstRatio = 0;
        for (int f = 0; seamOk && f < sw.info.Frames; f++)
        {
            var steps = new double[Frame - 1];
            for (int i = 0; i < Frame - 1; i++) steps[i] = Math.Abs(sw.frames[f * Frame + i + 1] - sw.frames[f * Frame + i]);
            Array.Sort(steps);
            double median = steps[steps.Length / 2], max = steps[^1];
            double seam = Math.Abs(sw.frames[f * Frame] - sw.frames[f * Frame + Frame - 1]);
            worstRatio = Math.Max(worstRatio, seam / Math.Max(median, 1e-9));
            seamOk = seam <= Math.Max(2 * median, max);
        }
        yield return (seamOk, $"aurora tables: each recording frame loops without a jump (worst seam {worstRatio:0.0}× the median step)");
    }

    // ---- T026: undo, and the source switch ----
    private static IEnumerable<(bool, string)> UndoAndSource(Dictionary<string, string> fx)
    {
        using var e = new NotaEngine();
        int t = e.AddWavetableSynthTrack();
        Render(e, 512);
        e.AuroraTableBuild(fx["table-64x2048.wav"], out var h);
        bool set = e.AuroraSetTable(t, 0, h); e.AuroraTableFree(h);
        e.TryGetAuroraTableInfo(t, 0, out var i1);
        yield return (set && i1 is { Frames: 64, UseCustom: true } && i1.Name == "table-64x2048.wav",
            $"aurora tables: an import holds the table and plays it ({i1.Frames}, \"{i1.Name}\")");
        e.Undo();
        e.TryGetAuroraTableInfo(t, 0, out var i2);
        yield return (i2.Frames == 0 && !i2.UseCustom, "aurora tables: undo takes the import back");
        e.Redo();
        e.TryGetAuroraTableInfo(t, 0, out var i3);
        yield return (i3.Frames == 64 && i3.Serial == i1.Serial && i3.UseCustom, "aurora tables: redo brings the same table back");

        e.AuroraClearTable(t, 0);
        e.TryGetAuroraTableInfo(t, 0, out var i4);
        e.Undo();
        e.TryGetAuroraTableInfo(t, 0, out var i5);
        yield return (i4.Frames == 0 && i5.Frames == 64 && i5.Serial == i1.Serial, "aurora tables: remove is one undo step");

        // The source switch is in place: voices keep sounding and it adds no undo step.
        e.TrackNoteOn(t, 60, 0.8f);
        Render(e, 2048);
        int before = e.InstrumentVoiceCount(t);
        e.AuroraSetSource(t, 0, false);
        Render(e, 512);
        e.TryGetAuroraTableInfo(t, 0, out var i6);
        int after = e.InstrumentVoiceCount(t);
        yield return (!i6.UseCustom && i6.Frames == 64 && before > 0 && after == before,
            $"aurora tables: switching to the built-in keeps the table and the held voice ({before} → {after})");
        e.AuroraSetSource(t, 0, true);
        e.Undo();   // takes back the import itself: the switches added nothing to undo
        e.TryGetAuroraTableInfo(t, 0, out var i7);
        yield return (i7.Frames == 0, "aurora tables: the source switch adds no undo step");
        e.TrackNoteOff(t, 60);

        // Picking a built-in bank while the imported table plays: the bank and the switch
        // off the import are one undo step, and redo makes both again (the card's path).
        using var g = new NotaEngine();
        int u = g.AddWavetableSynthTrack();
        int tp = -1;
        for (int i = 0; i < g.PluginParamCount(u, -1); i++) if (g.PluginParamId(u, -1, i) == "table") tp = i;
        float bank0 = g.PluginParamGet(u, -1, tp);
        g.AuroraTableBuild(fx["table-64x2048.wav"], out var h2);
        g.AuroraSetTable(u, 0, h2); g.AuroraTableFree(h2);
        AuroraTableImporter.PickBank(g, u, 0, tp, "table", 1f);
        g.TryGetAuroraTableInfo(u, 0, out var p1);
        float bank1 = g.PluginParamGet(u, -1, tp);
        yield return (!p1.UseCustom && p1.Frames == 64 && bank1 == 1f,
            $"aurora tables: picking a bank leaves the imported table (custom {p1.UseCustom}, bank {bank1})");
        g.Undo();
        g.TryGetAuroraTableInfo(u, 0, out var p2);
        float bank2 = g.PluginParamGet(u, -1, tp);
        yield return (p2.UseCustom && p2.Frames == 64 && bank2 == bank0,
            $"aurora tables: undo brings back the old bank and the imported table together (custom {p2.UseCustom}, bank {bank2})");
        g.Redo();
        g.TryGetAuroraTableInfo(u, 0, out var p3);
        float bank3 = g.PluginParamGet(u, -1, tp);
        yield return (!p3.UseCustom && p3.Frames == 64 && bank3 == 1f,
            $"aurora tables: redo picks the bank and leaves the import again (custom {p3.UseCustom}, bank {bank3})");
        g.Undo(); g.Undo();
        g.TryGetAuroraTableInfo(u, 0, out var p4);
        yield return (p4.Frames == 0 && g.PluginParamGet(u, -1, tp) == bank0, "aurora tables: one more undo takes the import back");
        // The imported name in the drop-down is an undo step of its own.
        g.Redo(); g.Redo();
        g.AuroraSetSource(u, 0, true, undoable: true);
        g.TryGetAuroraTableInfo(u, 0, out var p5);
        g.Undo();
        g.TryGetAuroraTableInfo(u, 0, out var p6);
        yield return (p5.UseCustom && !p6.UseCustom && g.PluginParamGet(u, -1, tp) == 1f,
            $"aurora tables: going back to the imported table is one undo step ({p5.UseCustom} → {p6.UseCustom})");
    }

    // ---- T027: the state blob, copies and duplicates ----
    private static IEnumerable<(bool, string)> State(Dictionary<string, string> fx)
    {
        using var e = new NotaEngine();
        int t = e.AddWavetableSynthTrack();
        e.AuroraTableBuild(fx["table-64x2048.wav"], out var h);
        e.AuroraSetTable(t, 0, h); e.AuroraSetTable(t, 1, h); e.AuroraTableFree(h);
        e.AuroraSetSource(t, 1, false);
        e.TryGetAuroraTableInfo(t, 0, out var src0);
        var frames0 = e.AuroraTableRead(t, 0);
        var full = e.AuroraState(t, true);
        var lean = e.AuroraState(t, false);
        yield return (full.Length > Params * 4 + 64 * Frame * 4 && full.Length < Params * 4 + 2 * 64 * Frame * 4,
            $"aurora tables: one table in both slots is stored once ({full.Length} bytes)");
        yield return (lean.Length > Params * 4 && lean.Length < Params * 4 + 64,
            $"aurora tables: the project blob leaves the frames out ({lean.Length} bytes)");

        int t2 = e.AddWavetableSynthTrack();
        e.SetPluginState(t2, -1, full);
        e.TryGetAuroraTableInfo(t2, 0, out var d0);
        e.TryGetAuroraTableInfo(t2, 1, out var d1);
        yield return (d0.Hash == src0.Hash && d0.Frames == 64 && d0.UseCustom && d1.Hash == src0.Hash && !d1.UseCustom
                      && e.AuroraTableRead(t2, 0).AsSpan().SequenceEqual(frames0) && d0.Name == src0.Name,
            "aurora tables: the blob restores both tables, their frames, names and sources");
        yield return (e.GetPluginState(t2, -1).AsSpan().SequenceEqual(full), "aurora tables: a restored blob saves back byte for byte");

        var plain = full.AsSpan(0, Params * 4).ToArray();
        e.SetPluginState(t2, -1, plain);
        e.TryGetAuroraTableInfo(t2, 0, out var p0);
        yield return (p0.Frames == 0 && !p0.UseCustom, "aurora tables: a blob without a trailer puts both oscillators back on the built-in");

        e.SetPluginState(t2, -1, full);
        var future = (byte[])full.Clone();
        int sec = full.Length - 8 - BitConverter.ToInt32(full, full.Length - 8);
        future[sec + 4] = 2;   // version 2
        future[0] = 0; future[1] = 0; future[2] = 0; future[3] = 0;   // param 0 = 0.0
        e.PluginParamSet(t2, -1, 0, 0.5f);
        e.SetPluginState(t2, -1, future);
        e.TryGetAuroraTableInfo(t2, 0, out var f0);
        yield return (f0.Frames == 64 && e.PluginParamGet(t2, -1, 0) == 0f,
            "aurora tables: a newer trailer version is skipped: parameters load, the tables stay");

        int dup = e.DuplicateTrack(t);
        e.TryGetAuroraTableInfo(dup, 0, out var u0);
        e.TryGetAuroraTableInfo(dup, 1, out var u1);
        yield return (dup > 0 && u0.Hash == src0.Hash && u0.UseCustom && u1.Frames == 64 && !u1.UseCustom,
            "aurora tables: a duplicated track keeps both tables and sources");

        bool copied = e.CopyInstrument(t);
        int t3 = e.AddWavetableSynthTrack();
        int pasted = copied ? e.PasteInstrument(t3) : -1;
        e.TryGetAuroraTableInfo(t3, 0, out var c0);
        e.TryGetAuroraTableInfo(t3, 1, out var c1);
        yield return (copied && pasted == 0 && c0.Hash == src0.Hash && c0.UseCustom && !c1.UseCustom,
            $"aurora tables: a copied and pasted Aurora keeps its tables (paste {pasted})");

        // An Aurora grouped into an Instrument Rack keeps its table, through the rack's blob too.
        int rackAddr = e.GroupDevices(t3, 0, 0, true, 0, 0);
        var rackBlob = e.GetPluginState(t3, -1);
        int t4 = e.AddInstrumentRackTrack();
        e.SetPluginState(t4, -1, rackBlob);
        int un = e.UngroupRack(t4, DeviceFamily.Instrument, -1, out _);
        e.TryGetAuroraTableInfo(t4, 0, out var r0);
        yield return (rackAddr == -1 && e.TrackInstrumentKind(t3) == 3 && un >= 0 && r0.Hash == src0.Hash && r0.UseCustom,
            $"aurora tables: an Aurora in a rack chain keeps its table through the rack's state (group {rackAddr}, ungroup {un})");
    }

    // ---- T028: the sound ----
    private static IEnumerable<(bool, string)> Sound()
    {
        // The built-in Analog's last frame (harmonics 1/n up to 64), imported as a cycle,
        // sounds like the built-in at Position 1.
        var cycle = new float[Frame];
        double peak = 0;
        for (int i = 0; i < Frame; i++)
        {
            double s = 0, ph = (double)i / Frame;
            for (int n = 1; n <= 64; n++) s += 1.0 / n * Math.Sin(2 * Math.PI * n * ph);
            cycle[i] = (float)s; peak = Math.Max(peak, Math.Abs(s));
        }
        for (int i = 0; i < Frame; i++) cycle[i] = (float)(cycle[i] / peak);
        var builtin = RenderNote(48, (eng, t) => Set(eng, t, "position", 1f));
        var imported = RenderNote(48, (eng, t) =>
        {
            Set(eng, t, "position", 1f);
            var h = Build(eng, cycle, 1, "analog-15");
            eng.AuroraSetTable(t, 0, h, undoable: false); eng.AuroraTableFree(h);
        });
        double rel = RelativeDb(builtin, imported);
        yield return (rel < -40, $"aurora tables: an imported copy of a built-in frame sounds like it at C3 ({rel:0} dB difference)");

        // A bright table at C7, Warp Off: what folds back stays 60 dB under the harmonics.
        var hi = RenderNote(96, (eng, t) =>
        {
            Set(eng, t, "cutoff", 1f); Set(eng, t, "unison", 0f);
            var h = Build(eng, AuroraFixtures.Morph(64), 64, "bright");
            eng.AuroraSetTable(t, 0, h, undoable: false); eng.AuroraTableFree(h);
        });
        double alias = AliasDb(hi, 440.0 * Math.Pow(2, (96 - 69) / 12.0));
        yield return (alias <= -60, $"aurora tables: a bright table at C7 does not fold back ({alias:0} dB alias energy vs harmonics)");
    }

    // ---- T029: project v29 ----
    private static IEnumerable<(bool, string)> Project(Dictionary<string, string> fx, string root)
    {
        var transport = new TransportState(120.0, 1.0, false, false);
        string bundle = Path.Combine(root, "p.nota");
        float[] a, b;
        string[] wavs;
        int warnCount;
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int t1 = Chord(e), t2 = Chord(e);
            foreach (var (t, o, file) in new[] { (t1, 0, "vowel-2s.flac"), (t1, 1, "table-64x2048.wav"), (t2, 0, "vowel-2s.flac") })
            {
                e.AuroraTableBuild(fx[file], out var h);
                e.AuroraSetTable(t, o, h); e.AuroraTableFree(h);
            }
            Set(e, t1, "osc2on", 1f); Set(e, t1, "osc2level", 0.7f);
            e.AuroraSetSource(t2, 0, false);
            a = RenderSong(e);
            var w = new List<string>();
            var doc = ProjectService.Capture(e, transport, w);
            ProjectService.Save(doc, bundle, e);
            warnCount = w.Count;
            wavs = Directory.GetFiles(Path.Combine(bundle, "samples")).Select(Path.GetFileName).OfType<string>().ToArray();
        }
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(bundle, ProjectService.ManifestName)))!;
        yield return (json["formatVersion"]?.GetValue<int>() == 29 && warnCount == 0, $"aurora tables: projects save as format 29 ({json["formatVersion"]})");
        yield return (wavs.Length == 2, $"aurora tables: two Auroras with the same table share one file ({wavs.Length} tables for 3 slots)");
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            var w = ProjectService.Apply(ProjectService.Load(bundle), e, bundle);
            b = RenderSong(e);
            var ids = TrackIds(e);
            e.TryGetAuroraTableInfo(ids[1], 0, out var s2);
            yield return (w.Count == 0 && s2 is { Frames: 16, UseCustom: false },
                $"aurora tables: reopening restores tables and sources ({w.Count} warnings)");
        }
        double res = ResidualDb(a, b);
        yield return (a.Length > 0 && res < -120, $"aurora tables: import → save → reopen renders bit-identically (residual {res:0} dBFS)");

        // A table file gone missing: the project opens, says so, and plays the built-in.
        foreach (var f in Directory.GetFiles(Path.Combine(bundle, "samples"))) File.Delete(f);
        {
            using var e = new NotaEngine();
            var w = ProjectService.Apply(ProjectService.Load(bundle), e, bundle);
            var ids = TrackIds(e);
            e.TryGetAuroraTableInfo(ids[0], 0, out var m0);
            bool says = w.Any(x => x.Contains("Aurora Osc 1 table \"vowel-2s.flac\" is missing"));
            yield return (says && m0 is { Missing: true, Frames: 0, UseCustom: false } && m0.Name == "vowel-2s.flac",
                $"aurora tables: a missing table warns and plays the built-in ({w.Count} warnings: {w.FirstOrDefault()})");
            // Saving again keeps the name, so the warning repeats until the table is imported again.
            string again = Path.Combine(root, "p2.nota");
            ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), again, e);
            using var e2 = new NotaEngine();
            var w2 = ProjectService.Apply(ProjectService.Load(again), e2, again);
            yield return (w2.Count(x => x.Contains("is missing")) == 3, $"aurora tables: a re-saved project still names its missing tables ({w2.Count})");
        }

        // A v27 project (before custom tables) loads as it did and migrates.
        {
            using var e = new NotaEngine();
            Chord(e);
            string old = Path.Combine(root, "v27.nota");
            ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), old, e);
            string mf = Path.Combine(old, ProjectService.ManifestName);
            var node = JsonNode.Parse(File.ReadAllText(mf))!.AsObject();
            node["formatVersion"] = 27;
            File.WriteAllText(mf, node.ToJsonString());
            var doc = ProjectService.Load(old);
            using var e2 = new NotaEngine();
            var w = ProjectService.Apply(doc, e2, old);
            int id = TrackIds(e2)[0];
            e2.TryGetAuroraTableInfo(id, 0, out var v0);
            yield return (doc.FormatVersion == 29 && w.Count == 0 && v0.Frames == 0
                          && e2.GetPluginState(id, -1).Length == Params * 4,
                $"aurora tables: a v27 project loads unchanged and migrates to 29 ({doc.FormatVersion})");
        }
    }

    // ---- T030: presets ----
    private static IEnumerable<(bool, string)> Presets(Dictionary<string, string> fx)
    {
        using var e = new NotaEngine();
        int t = e.AddWavetableSynthTrack();
        e.AuroraTableBuild(fx["table-64x2048.wav"], out var h);
        e.AuroraSetTable(t, 1, h); e.AuroraTableFree(h);
        var frames = e.AuroraTableRead(t, 1);
        var doc = PresetService.Capture(e, t, -1, "Mine")!;
        yield return (doc.AuroraTables is { Count: 1 } && doc.AuroraTables[0] is { Osc: 1, Frames: 64 },
            $"aurora tables: a user preset carries its table ({doc.AuroraTables?.Count ?? 0})");
        string json = System.Text.Json.JsonSerializer.Serialize(doc);
        var back = System.Text.Json.JsonSerializer.Deserialize<PresetDocument>(json)!;

        int t2 = e.AddWavetableSynthTrack();
        PresetService.ApplyInPlace(back, e, t2, -1);
        e.TryGetAuroraTableInfo(t2, 1, out var i1);
        yield return (i1 is { Frames: 64, UseCustom: true } && e.AuroraTableRead(t2, 1).AsSpan().SequenceEqual(frames),
            "aurora tables: applying it restores the frames exactly");
        e.Undo();
        e.TryGetAuroraTableInfo(t2, 1, out var i2);
        yield return (i2.Frames == 0, "aurora tables: the preset apply is one undo step");
        e.Redo();

        var cat = new FactoryPresetCatalog();
        var factories = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 5 && p.DisplayName != "Init").Take(1).ToList();
        string warn = factories.Count == 0 ? "none" : cat.ApplyInPlace(e, factories[0].Id!, t2, -1);
        e.TryGetAuroraTableInfo(t2, 1, out var i3);
        yield return (factories.Count == 1 && warn.Length == 0 && i3.Frames == 0 && !i3.UseCustom,
            $"aurora tables: a factory preset plays the built-in tables ({(factories.Count == 1 ? factories[0].DisplayName : "none")})");
    }

    // ---- T047: live follow ----
    private static IEnumerable<(bool, string)> Scope(Dictionary<string, string> fx)
    {
        using var e = new NotaEngine();
        int t = e.AddWavetableSynthTrack();
        Set(e, t, "lfo1rate", 0.5f); Set(e, t, "lfo1depth", 1f); Set(e, t, "mtx2_2", 1f); Set(e, t, "position", 0.5f);
        var sc = new float[6];
        Render(e, 512);
        e.InstrumentScope(t, sc);
        yield return (sc[0] == -1f && sc[1] == -1f && sc[2] == 0f, $"aurora tables: with no note the scope says none ({sc[0]}, serial {sc[2]})");
        e.TrackNoteOn(t, 60, 0.8f);
        float lo = 2, hi = -2;
        bool inRange = true;
        for (int k = 0; k < 100; k++)   // about a second in 10 ms steps
        {
            Render(e, 480);
            e.InstrumentScope(t, sc);
            lo = Math.Min(lo, sc[0]); hi = Math.Max(hi, sc[0]);
            inRange &= sc[0] is >= 0 and <= 1 && sc[1] is >= 0 and <= 1;
        }
        yield return (inRange && hi - lo > 0.5 && sc[4] >= 1, $"aurora tables: a held note's played position moves with the LFO ({lo:0.00}..{hi:0.00})");
        e.AuroraTableBuild(fx["cycle-2048.wav"], out var h);
        e.AuroraSetTable(t, 1, h, undoable: false); e.AuroraTableFree(h);
        e.InstrumentScope(t, sc);
        e.TryGetAuroraTableInfo(t, 1, out var info);
        yield return (sc[3] > 0 && sc[3] == (info.Serial & 0xFFFFFF), $"aurora tables: the scope carries the table serial ({sc[3]})");
        float srcOn = sc[5];
        e.AuroraSetSource(t, 1, false);
        e.InstrumentScope(t, sc);
        yield return (srcOn == 2f && sc[5] == 0f, $"aurora tables: the scope says which oscillators play their imported table ({srcOn} → {sc[5]})");
        e.TrackNoteOff(t, 60);
    }

    // ---- helpers ----
    private static nint Build(NotaEngine e, float[] frames, int count, string name)
        => e.AuroraTableFromFrames(frames, count, name, 0, out var h) == 0 ? h : throw new InvalidOperationException("build");

    private static int[] TrackIds(NotaEngine e)
    {
        var ids = new List<int>();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti)) ids.Add(ti.Id);
        return ids.ToArray();
    }

    private static float[] Saw64() => AuroraFixtures.Saw(Frame, 1);

    private static void Render(NotaEngine e, int frames)
    {
        var buf = new float[frames * 2];
        e.RenderOffline(buf, frames, Sr);
    }

    private static int Chord(NotaEngine e)
    {
        int t = e.AddWavetableSynthTrack();
        int clip = e.AddMidiClip(t, 0, 4);
        e.SetClipNotes(t, clip, new[] { new NotaNote(48, 0, 4, 0.8f), new NotaNote(60, 0, 4, 0.7f), new NotaNote(67, 0, 4, 0.7f) });
        return t;
    }

    private static float[] RenderSong(NotaEngine e)
    {
        const int frames = 2 * Sr, block = 512;
        var buf = new float[frames * 2];
        var chunk = new float[block * 2];
        e.Seek(0);
        e.Play();
        for (int done = 0; done < frames; done += block)
        {
            e.RenderOffline(chunk, block, Sr);
            Array.Copy(chunk, 0, buf, done * 2, Math.Min(block, frames - done) * 2);
        }
        e.StopTransport();
        return buf;
    }

    private static float[] RenderChord(Action<NotaEngine, int>? setup)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = Chord(e);
        Set(e, t, "unison", 0.5f); Set(e, t, "unispread", 0.6f);
        setup?.Invoke(e, t);
        return RenderSong(e);
    }

    private static float[] RenderNote(int pitch, Action<NotaEngine, int> setup)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddWavetableSynthTrack();
        Set(e, t, "unison", 0f); Set(e, t, "cutoff", 1f); Set(e, t, "fxreverb", 0f); Set(e, t, "fxchorus", 0f);
        Set(e, t, "attack", 0f); Set(e, t, "sustain", 1f);
        setup(e, t);
        int clip = e.AddMidiClip(t, 0, 4);
        e.SetClipNotes(t, clip, new[] { new NotaNote(pitch, 0, 4, 0.8f) });
        return RenderSong(e);
    }

    private static void Set(NotaEngine e, int t, string id, float v)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++)
            if (e.PluginParamId(t, -1, i) == id) { e.PluginParamSet(t, -1, i, v); return; }
        throw new InvalidOperationException($"Aurora has no parameter '{id}'");
    }

    private static double ResidualDb(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double worst = 0;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return 20 * Math.Log10(Math.Max(worst, 1e-12));
    }

    // Energy of the difference relative to the reference, over the steady second half.
    private static double RelativeDb(float[] reference, float[] x)
    {
        double e = 0, d = 0;
        for (int i = reference.Length / 2; i < reference.Length; i++) { e += reference[i] * reference[i]; d += (x[i] - reference[i]) * (x[i] - reference[i]); }
        return 10 * Math.Log10(Math.Max(d, 1e-30) / Math.Max(e, 1e-30));
    }

    // Energy away from the harmonics of f0 against the energy on them: a windowed DFT of the
    // left channel's steady part, bins within ±3 of a harmonic counted as harmonic.
    private static double AliasDb(float[] stereo, double f0)
    {
        const int n = 32768;
        int start = Sr;   // frame Sr / 2: half a second into the held note
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double w = 0.35875 - 0.48829 * Math.Cos(2 * Math.PI * i / n) + 0.14128 * Math.Cos(4 * Math.PI * i / n) - 0.01168 * Math.Cos(6 * Math.PI * i / n);
            x[i] = stereo[start + 2 * i] * w;
        }
        var re = (double[])x.Clone(); var im = new double[n];
        Fft(re, im);
        double harm = 0, other = 0, binHz = (double)Sr / n;
        for (int k = 1; k < n / 2; k++)
        {
            double p = re[k] * re[k] + im[k] * im[k];
            double hz = k * binHz, nearest = Math.Round(hz / f0) * f0;
            if (nearest >= f0 && Math.Abs(hz - nearest) <= 4 * binHz) harm += p; else other += p;
        }
        return 10 * Math.Log10(Math.Max(other, 1e-30) / Math.Max(harm, 1e-30));
    }

    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < len / 2; k++)
                {
                    double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                    double ur = re[i + k], ui = im[i + k];
                    double vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi;
                    double vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                    re[i + k] = ur + vr; im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                }
        }
    }
}
