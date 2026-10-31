// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin (instrument kind 18, specs/055): identity, the A/B param table (both parts, both
// sides, the matrix), project state — round trip, an older shorter blob, a foreign blob — clone
// and replace; the voice (US1): Init level against Volt, pitch, the 12-voice pool and stealing,
// Unison and Mono, the voice cap, both filters' modes and self-oscillation, bounded extremes,
// and block-size independence (a clip renders the same at any block size); the factory presets;
// the morph (US2): exact endpoints, equal sides are silent, a sweep adds no jump of its own;
// expression (US3): per-note pressure and bend move only their note, channel pressure moves all,
// the pad's three axes, MPE support; the matrix (US4): the app's tables match the engine's, a
// zero-depth route is silent, depth morphs with the sides, a per-note source on the Morph moves
// only its note, adding into a full matrix fails, route edits undo in one step; the effects
// (US5): the section switch bypasses exactly, each effect is heard, a synced delay follows the
// tempo, a reverb tail rings past the note; the sequencer (US8): edits undo in one step, steps
// land on the grid at any tempo and block size, a stop releases, step record, transpose, the
// arpeggiator, patterns in the state and "To clip"; Split / Layer (US9): key routing, the voice
// share, both parts in Layer, a layout change releases.

using System.IO;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Twin;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TwinTests
{
    private const int Sr = 48000;
    private const int Params = 965; // globals 21 + 2 parts × (part 144 + 2 sides × 164)

    private static int Pi(NotaEngine e, int t, string id)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++) if (e.PluginParamId(t, -1, i) == id) return i;
        return -1;
    }
    private static float G(NotaEngine e, int t, string id) => e.PluginParamGet(t, -1, Pi(e, t, id));
    private static void P(NotaEngine e, int t, string id, float v) => e.PluginParamSet(t, -1, Pi(e, t, id), v);

    private static double Rms(float[] st, int from = 0)
    {
        double s = 0;
        for (int i = from * 2; i < st.Length; i++) s += st[i] * (double)st[i];
        return Math.Sqrt(s / Math.Max(1, st.Length - from * 2));
    }
    private static double Db(double x) => 20 * Math.Log10(Math.Max(x, 1e-12));
    private static double Peak(float[] st) { double p = 0; foreach (var v in st) p = Math.Max(p, Math.Abs(v)); return p; }
    // Energy at one frequency (Goertzel), mono sum.
    private static double At(float[] st, double hz, int from = 0)
    {
        double w = 2 * Math.PI * hz / Sr, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = from * 2; i < st.Length; i += 2) { double s0 = (st[i] + st[i + 1]) * 0.5 + c * s1 - s2; s2 = s1; s1 = s0; }
        return Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - c * s1 * s2));
    }

    // A fresh engine with one instrument track (Twin by default), notes held live, `frames` rendered.
    private static (float[] Out, int Voices) Hold(int[] notes, int frames, Action<NotaEngine, int>? setup = null, bool volt = false)
    {
        using var e = new NotaEngine();
        int t = volt ? e.AddVoltSynthTrack() : e.AddTwinTrack();
        setup?.Invoke(e, t);
        Render(e, 512);
        foreach (var n in notes) e.TrackNoteOn(t, n, 0.8f);
        var o = Render(e, frames);
        return (o, e.InstrumentVoiceCount(t));
    }

    // A clip-driven render (transport rolling) at a given block size, after an optional setup.
    private static float[] Play(int block, int frames, Action<NotaEngine, int>? setup = null)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddTwinTrack();
        if (setup is not null) setup(e, t);
        else
        {
            P(e, t, "lo.a.o2level", 0.7f); P(e, t, "lo.a.o1bal", 0.5f); P(e, t, "lo.a.route", 0.4f); P(e, t, "lo.a.drift", 0.8f);
            e.AddMidiClip(t, 0.0, 8.0);
            e.SetClipNotes(t, 0, new[] { new NotaNote(48, 0.0, 1.5, 0.8f), new NotaNote(55, 0.25, 1.0, 0.6f), new NotaNote(64, 1.0, 2.0, 0.9f), new NotaNote(43, 2.0, 1.0, 0.7f) });
        }
        var outv = new float[frames * 2];
        var tmp = new float[block * 2];
        e.Seek(0); e.Play();
        for (int done = 0; done < frames;) { int m = Math.Min(block, frames - done); e.RenderOffline(tmp, m, Sr); Array.Copy(tmp, 0, outv, done * 2, m * 2); done += m; }
        e.StopTransport();
        return outv;
    }

    // A two-sided patch: A dark and slow, B bright and plucked, differing in stepped params too.
    private static void TwoSided(NotaEngine e, int t)
    {
        foreach (var (id, a, b) in new[] { ("ldcut", 0.35f, 0.8f), ("o1wave", 0.5f, 1f), ("o2level", 0f, 0.6f), ("o2coarse", 0.5f, 0.75f),
                     ("aenva", 0.6f, 0.1f), ("aenvs", 1f, 0.3f), ("ldres", 0.1f, 0.5f), ("ldslope", 1f, 0f), ("sync", 0f, 1f), ("mmmode", 0f, 0.7f) })
        {
            P(e, t, "lo.a." + id, a);
            P(e, t, "lo.b." + id, b);
        }
    }
    // Copy one side of the Lower part onto the other.
    private static void CopySide(NotaEngine e, int t, char from, char to)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++)
        {
            string id = e.PluginParamId(t, -1, i);
            if (id.StartsWith($"lo.{to}.")) P(e, t, id, G(e, t, $"lo.{from}." + id[5..]));
        }
    }
    private static float[] Chord(Action<NotaEngine, int> setup, int frames = Sr * 2, Action<NotaEngine, int, int>? perBlock = null)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddTwinTrack();
        setup(e, t);
        e.AddMidiClip(t, 0.0, 8.0);
        e.SetClipNotes(t, 0, new[] { 48, 55, 60, 64 }.Select(n => new NotaNote(n, 0, 3.5, 0.8f)).ToArray());
        var outv = new float[frames * 2];
        var tmp = new float[128 * 2];
        e.Seek(0); e.Play();
        for (int done = 0, blk = 0; done < frames; blk++)
        {
            perBlock?.Invoke(e, t, blk);
            int m = Math.Min(128, frames - done);
            e.RenderOffline(tmp, m, Sr);
            Array.Copy(tmp, 0, outv, done * 2, m * 2);
            done += m;
        }
        return outv;
    }
    private static double MaxStep(float[] st, int from, int to)
    {
        double d = 0;
        for (int i = Math.Max(1, from); i < to; i++) d = Math.Max(d, Math.Max(Math.Abs(st[2 * i] - st[2 * i - 2]), Math.Abs(st[2 * i + 1] - st[2 * i - 1])));
        return d;
    }

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

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int t = e.AddTwinTrack();
        yield return (t > 0 && e.TrackInstrumentKind(t) == 18, "AddTwinTrack adds a kind 18 instrument");
        yield return (e.DeviceName(t, -1) == "Nota Twin", $"instrument is Nota Twin (got '{e.DeviceName(t, -1)}')");
        yield return (InstrumentView.Index(e, t) >= 0 && !InstrumentView.IsMini(e, t), "Nota Twin has an S / L view param and opens as L");

        int n = e.PluginParamCount(t, -1);
        yield return (n == Params, $"Nota Twin exposes {Params} params (got {n})");
        var ids = new HashSet<string>();
        bool named = true;
        for (int i = 0; i < n; i++)
        {
            ids.Add(e.PluginParamId(t, -1, i));
            named &= !string.IsNullOrEmpty(e.PluginParamName(t, -1, i));
        }
        yield return (ids.Count == n && !ids.Contains("") && named, "twin param ids are unique + non-empty, every param has a name");

        // Layout: globals, then per part its side-less block, side A, side B; Upper B's last route depth ends it.
        bool layout = Pi(e, t, "mode") == 0 && Pi(e, t, "lo.morph") == 21 && Pi(e, t, "up.b.m64amt") == n - 1
                      && Pi(e, t, "lo.b.ldcut") - Pi(e, t, "lo.a.ldcut") == 164
                      && Pi(e, t, "up.a.ldcut") - Pi(e, t, "lo.a.ldcut") == 144 + 2 * 164;
        yield return (layout, "twin params follow the [globals][part][A][B] × 2 layout");
        yield return (e.PluginParamName(t, -1, Pi(e, t, "up.b.ldcut")) == "Upper B · Ladder Cutoff",
            $"twin names carry part and side (got '{e.PluginParamName(t, -1, Pi(e, t, "up.b.ldcut"))}')");
        yield return (Math.Abs(G(e, t, "lo.a.ldcut") - G(e, t, "lo.b.ldcut")) < 1e-6 && Math.Abs(G(e, t, "lo.a.m7amt") - 0.5f) < 1e-6
                      && e.PluginParamText(t, -1, Pi(e, t, "lo.m1.src")) == "Pressure" && e.PluginParamText(t, -1, Pi(e, t, "lo.m1.dst")) == "Ladder Cutoff"
                      && e.PluginParamText(t, -1, Pi(e, t, "lo.m5.dst")) == "Morph" && e.PluginParamText(t, -1, Pi(e, t, "lo.m7.src")) == "None",
            "twin Init: both sides equal; slots 1–6 hold the default expression routes, 7 on are empty");

        // State round trip onto another track.
        P(e, t, "lo.b.ldcut", 0.25f); P(e, t, "up.a.o2wave", 0.9f); P(e, t, "lo.m3.src", 4f / 63f); P(e, t, "lo.morph", 0.4f);
        var st = e.GetPluginState(t, -1);
        int t2 = e.AddTwinTrack();
        e.SetPluginState(t2, -1, st);
        bool same = true;
        for (int i = 0; i < n; i++) same &= e.PluginParamGet(t2, -1, i) == e.PluginParamGet(t, -1, i);
        yield return (st.Length > 12 + 4 * n && same, $"twin state ({st.Length} bytes: params and patterns) restores every param on another track");

        // An older, shorter blob: its params load, the rest keep their defaults (FR-052).
        int t3 = e.AddTwinTrack();
        var shortBlob = new byte[8 + 4 * 30 + 4];
        Array.Copy(st, 0, shortBlob, 0, 4);
        BitConverter.GetBytes(30u).CopyTo(shortBlob, 4);
        Array.Copy(st, 8, shortBlob, 8, 4 * 30);
        e.SetPluginState(t3, -1, shortBlob);
        yield return (Math.Abs(G(e, t3, "lo.morph") - 0.4f) < 1e-6 && Math.Abs(G(e, t3, "lo.b.ldcut") - G(e, t3, "lo.a.ldcut")) < 1e-6,
            "a shorter twin blob loads its params and leaves the rest at defaults");

        // A blob that isn't Twin's is ignored.
        var foreign = (byte[])st.Clone();
        foreign[0] ^= 0xFF;
        int t4 = e.AddTwinTrack();
        e.SetPluginState(t4, -1, foreign);
        yield return (Math.Abs(G(e, t4, "lo.morph")) < 1e-6, "a twin blob with a foreign magic is ignored");

        int dup = e.DuplicateTrack(t);
        yield return (dup > 0 && Math.Abs(G(e, dup, "lo.b.ldcut") - 0.25f) < 1e-6 && Math.Abs(G(e, dup, "up.a.o2wave") - 0.9f) < 1e-6,
            "duplicate track clones the twin patch");

        int other = e.AddVoltSynthTrack();
        yield return (e.SetTrackBuiltinInstrument(other, 18) && e.TrackInstrumentKind(other) == 18, "replace swaps an instrument for Nota Twin");

        var silent = Render(e, 4096);
        yield return (silent.All(float.IsFinite), "the twin shell renders finite");

        // ---- the voice (US1) ----------------------------------------------------------------
        int[] chord = { 48, 55, 60, 64 };
        var (tw, _) = Hold(chord, Sr * 2);
        var (vo, _) = Hold(chord, Sr * 2, volt: true);
        double d = Db(Rms(tw, Sr / 2)) - Db(Rms(vo, Sr / 2));
        yield return (tw.All(float.IsFinite) && Math.Abs(d) <= 1.5, $"twin Init chord sits within ±1.5 dB of Volt's Init ({d:+0.00;-0.00} dB)");

        var (a3, v1) = Hold(new[] { 57 }, Sr);
        yield return (v1 == 1 && At(a3, 220, 4800) > 10 * At(a3, 233.08, 4800), "twin plays A3 at 220 Hz on one voice");

        var notes13 = Enumerable.Range(0, 13).Select(k => 40 + 3 * k).ToArray();
        var (o13, v13) = Hold(notes13, Sr / 2);
        yield return (v13 == 12 && o13.All(float.IsFinite), $"13 held notes sound on 12 voices ({v13})");
        var (o4, v4) = Hold(notes13, Sr / 2, (e2, t5) => P(e2, t5, "voices", 0f));
        yield return (v4 == 4, $"the voice cap holds the pool to 4 ({v4})");
        var (_, vu) = Hold(new[] { 48 }, Sr / 2, (e2, t5) => P(e2, t5, "lo.voicemode", 1f));
        yield return (vu == 12, $"Unison stacks every voice on one key ({vu})");
        var (_, vm) = Hold(new[] { 48, 60, 67 }, Sr / 2, (e2, t5) => P(e2, t5, "lo.voicemode", 1f / 3f));
        yield return (vm == 1, $"Mono plays one voice for a held chord ({vm})");

        var (lp, _) = Hold(new[] { 48 }, Sr, (e2, t5) => { P(e2, t5, "lo.a.o1bal", 0f); P(e2, t5, "lo.a.mmcut", 0.55f); P(e2, t5, "lo.a.mmmode", 0f); });
        var (hp, _) = Hold(new[] { 48 }, Sr, (e2, t5) => { P(e2, t5, "lo.a.o1bal", 0f); P(e2, t5, "lo.a.mmcut", 0.55f); P(e2, t5, "lo.a.mmmode", 0.667f); });
        yield return (At(lp, 130.81, 4800) > 10 * At(lp, 4186, 4800) && At(hp, 4186, 4800) > 3 * At(lp, 4186, 4800) && At(hp, 130.81, 4800) < 0.1 * At(lp, 130.81, 4800),
            "the multimode filter is a low-pass at mode 0 and a high-pass at 2/3");

        var (ls, _) = Hold(new[] { 60 }, Sr, (e2, t5) => { P(e2, t5, "lo.a.o1level", 0f); P(e2, t5, "lo.a.ldres", 1f); P(e2, t5, "lo.a.ldcut", 0.5f); P(e2, t5, "lo.a.ldenv", 0.5f); });
        var (ms, _) = Hold(new[] { 60 }, Sr, (e2, t5) => { P(e2, t5, "lo.a.o1level", 0f); P(e2, t5, "lo.a.o1bal", 0f); P(e2, t5, "lo.a.mmres", 1f); P(e2, t5, "lo.a.mmcut", 0.5f); });
        yield return (Db(Rms(ls, Sr / 2)) > -60 && Db(Rms(ms, Sr / 2)) > -60,
            $"both filters self-oscillate at full resonance with no source (ladder {Db(Rms(ls, Sr / 2)):0} dB, multimode {Db(Rms(ms, Sr / 2)):0} dB)");

        var (ex, _) = Hold(new[] { 36, 60, 84, 96 }, Sr * 2, (e2, t5) =>
        {
            foreach (var id in new[] { "lo.a.brute", "lo.a.mmres", "lo.a.ldres", "lo.a.drive", "lo.a.o2level", "lo.a.noiselevel", "lo.a.fm21", "lo.a.fmfilt", "lo.a.o1metal" }) P(e2, t5, id, 1f);
            P(e2, t5, "lo.a.sync", 1f); P(e2, t5, "lo.a.route", 0f); P(e2, t5, "lo.a.o1bal", 0.5f); P(e2, t5, "lo.a.o1wave", 0f);
        });
        yield return (ex.All(float.IsFinite) && Peak(ex) < 2.0, $"maximum Brute, resonance, drive, FM, sync and Metal stay finite and bounded (peak {Db(Peak(ex)):0.0} dB)");

        var pa = Play(512, Sr * 3);
        var pb = Play(77, Sr * 3);
        double md = 0;
        for (int i = 0; i < pa.Length; i++) md = Math.Max(md, Math.Abs(pa[i] - pb[i]));
        yield return (Rms(pa) > 1e-3 && md == 0, $"a twin clip renders bit-identically at blocks of 512 and 77 (max diff {md:G3})");

        static double Diff(float[] x, float[] y) { double d = 0; for (int i = 0; i < x.Length; i++) d = Math.Max(d, Math.Abs(x[i] - y[i])); return d; }

        // ---- factory presets ------------------------------------------------------------------
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 18).ToList();
        var folders = mine.Select(p => p.Category).Distinct().ToList();
        yield return (mine.Count >= 40 && folders.Count >= 9, $"Nota Twin ships ≥ 40 factory presets in ≥ 9 folders ({mine.Count} in {string.Join(", ", folders)})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !ids.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        yield return (bad.Count == 0, $"every Twin preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int tp = e.AddTwinTrack();
        int fails = 0;
        double slowest = 0;
        foreach (var p in mine)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (cat.ApplyInPlace(e, p.Id, tp, -1).Length != 0) fails++;
            slowest = Math.Max(slowest, clock.Elapsed.TotalMilliseconds);
        }
        yield return (fails == 0 && slowest < 50, $"every Twin preset applies in place, the slowest in {slowest:0.0} ms (< 50; {fails} failed)");
        // SC-011: Split / Layer presets, and one plays its parts where it says.
        var two = mine.Where(p => cat.Document(p.Id)!.NamedParams!.TryGetValue("mode", out var md) && md > 0.25f).ToList();
        var bassPad = two.FirstOrDefault(p => p.DisplayName == "Bass + Pad Split");
        bool splits = false;
        if (bassPad.Id is not null)
        {
            using var se = new NotaEngine();
            int bt = se.AddTwinTrack();
            cat.ApplyInPlace(se, bassPad.Id, bt, -1);
            se.TrackNoteOn(bt, 36, 0.8f); se.TrackNoteOn(bt, 60, 0.8f); se.TrackNoteOn(bt, 64, 0.8f);
            Render(se, 2048);
            var sc = new float[32];
            se.InstrumentScope(bt, sc);
            splits = sc[4] == 1 && sc[5] == 2;
        }
        yield return (two.Count >= 6 && splits, $"≥ 6 factory presets split or layer the parts ({two.Count}); Bass + Pad Split plays C1 on Lower and C4 / E4 on Upper");

        // The preset name and its "modified" mark (specs/048), and a user preset of the sound.
        const PresetSurface Inst = PresetSurface.Instrument;
        var first = mine[0];
        string pw = PresetEdit.Apply(e, tp, Inst, -1, first.Id, first.DisplayName, () => cat.ApplyInPlace(e, first.Id, tp, -1));
        bool tagged = pw == "" && PresetEdit.Name(e, tp, Inst, -1) == first.DisplayName && e.DevicePresetModified(tp, Inst, -1) == 0;
        float cut0 = G(e, tp, "lo.b.ldcut");
        P(e, tp, "lo.b.ldcut", cut0 > 0.5f ? 0.2f : 0.8f);
        bool marked = e.DevicePresetModified(tp, Inst, -1) == 1;
        P(e, tp, "lo.b.ldcut", cut0);
        yield return (tagged && marked && e.DevicePresetModified(tp, Inst, -1) == 0,
            "a Twin preset names the card; moving a side-B knob marks it modified, moving it back clears the mark");
        P(e, tp, "lo.b.o2wave", 0.33f); P(e, tp, "lo.m9.src", 3 / 63f); P(e, tp, "lo.m9.dst", 1 / 255f); P(e, tp, "lo.a.m9amt", 0.8f);
        var doc = PresetService.Capture(e, tp, -1, "Mine")!;
        int tq = e.AddTwinTrack();
        PresetService.ApplyInPlace(doc, e, tq, -1);
        int pcount = e.PluginParamCount(tp, -1), diffs = 0;
        for (int i = 0; i < pcount; i++) if (e.PluginParamId(tp, -1, i) != "view" && e.PluginParamGet(tp, -1, i) != e.PluginParamGet(tq, -1, i)) diffs++;
        yield return (doc.NamedParams!.Count >= pcount - 1 && diffs == 0, $"a user preset keeps both sides and the matrix ({doc.NamedParams.Count} params; {diffs} differ after applying it)");
        var off = new List<string>();
        int twoSided = 0;
        foreach (var p in mine)
        {
            float[]? sideA = null;
            foreach (float morph in new[] { 0f, 1f })
            {
                var b = Play(512, Sr * 2, (e2, t5) => { cat.ApplyInPlace(e2, p.Id, t5, -1); P(e2, t5, "lo.morph", morph); P(e2, t5, "up.morph", morph); e2.AddMidiClip(t5, 0.0, 8.0);
                    e2.SetClipNotes(t5, 0, new[] { 48, 52, 55, 60, 64, 67, 71, 72 }.Select(n => new NotaNote(n, 0, 3, 100f / 127f)).ToArray()); });
                double r = Rms(b), pk = Peak(b);
                if (!b.All(float.IsFinite) || r < 0.003 || pk >= 1.0) off.Add($"{p.DisplayName} {(morph == 0 ? "A" : "B")} ({Db(r):0} dB / peak {Db(pk):0.0} dB)");
                if (sideA is null) sideA = b;
                else if (Diff(sideA, b) > 0.01) twoSided++;
            }
        }
        yield return (off.Count == 0, $"every Twin preset sounds an 8-note chord at velocity 100 on both sides, finite, peak under 0 dBFS{(off.Count > 0 ? " — " + string.Join(", ", off) : "")}");
        yield return (twoSided == mine.Count, $"every Twin preset's side B differs from its side A ({twoSided}/{mine.Count})");

        // ---- the morph (US2) --------------------------------------------------------------------
        var atB = Chord((e2, t5) => { TwoSided(e2, t5); P(e2, t5, "lo.morph", 1f); });
        var bOnly = Chord((e2, t5) => { TwoSided(e2, t5); CopySide(e2, t5, 'b', 'a'); P(e2, t5, "lo.morph", 0f); });
        var atA = Chord((e2, t5) => { TwoSided(e2, t5); P(e2, t5, "lo.morph", 0f); });
        var aHalf = Chord((e2, t5) => { TwoSided(e2, t5); CopySide(e2, t5, 'a', 'b'); P(e2, t5, "lo.morph", 0.5f); });
        yield return (Rms(atB) > 1e-3 && Diff(atB, bOnly) == 0, $"Morph 1 renders exactly side B alone (max diff {Diff(atB, bOnly):G3})");
        yield return (Rms(atA) > 1e-3 && Diff(atA, aHalf) == 0, $"with both sides equal the morph changes nothing (max diff {Diff(atA, aHalf):G3})");
        yield return (Diff(atA, atB) > 0.01, "sides A and B sound different");
        int beat = Sr / 2;
        var sweep = Chord(TwoSided, perBlock: (e2, t5, blk) => P(e2, t5, "lo.morph", Math.Clamp((blk * 128 - Sr / 2) / (float)beat, 0f, 1f)));
        double own = Math.Max(MaxStep(atA, Sr / 2, Sr), MaxStep(atB, Sr / 2, Sr)), sw = MaxStep(sweep, Sr / 2, Sr);
        yield return (sweep.All(float.IsFinite) && sw <= own * 1.25, $"a one-beat Morph sweep adds no jump of its own (largest step {sw:0.000} vs the sides' {own:0.000})");

        // Side edits: A → B, then undo, then swap — each one undo step.
        using (var se = new NotaEngine())
        {
            int tt = se.AddTwinTrack();
            TwoSided(se, tt);
            se.ClearUndoHistory();
            float b0 = G(se, tt, "lo.b.ldcut"), a0 = G(se, tt, "lo.a.ldcut");
            int wrote = Nota.Application.Twin.TwinSides.Apply(se, tt, Nota.Application.Twin.TwinSides.Edit.CopyAToB);
            bool copied = wrote == 164 && G(se, tt, "lo.b.ldcut") == a0 && G(se, tt, "lo.b.sync") == G(se, tt, "lo.a.sync");
            bool canUndo = se.CanUndo, u = se.Undo();
            bool undone = u && G(se, tt, "lo.b.ldcut") == b0 && G(se, tt, "lo.b.o1wave") == 1f && !se.CanUndo;
            yield return (copied && canUndo && undone, $"A → B copies all 164 side params in one undo step (wrote {wrote})");
            Nota.Application.Twin.TwinSides.Apply(se, tt, Nota.Application.Twin.TwinSides.Edit.Swap);
            bool swapped = G(se, tt, "lo.a.ldcut") == b0 && G(se, tt, "lo.b.ldcut") == a0;
            yield return (swapped && se.Undo() && G(se, tt, "lo.a.ldcut") == a0 && !se.CanUndo, "Swap exchanges the sides in one undo step");
        }

        // ---- expression (US3) ---------------------------------------------------------------------
        yield return (e.TrackInstrumentSupportsMpe(t), "Nota Twin reports MPE support");
        int[] six = { 48, 52, 55, 58, 62, 65 };   // no note an octave of another
        double[] fund = six.Select(n => 440 * Math.Pow(2, (n - 69) / 12.0)).ToArray();
        float[] Expr(Action<NotaEngine, int>? after)
        {
            using var xe = new NotaEngine();
            int xt = xe.AddTwinTrack();
            P(xe, xt, "lo.a.ldcut", 0.4f); P(xe, xt, "lo.a.drift", 0f);
            Render(xe, 512);
            foreach (var n in six) xe.TrackNoteOn(xt, n, 0.8f);
            after?.Invoke(xe, xt);
            return Render(xe, Sr);
        }
        var plain = Expr(null);
        var pressed = Expr((xe, xt) => xe.TrackNoteExpression(xt, 55, NoteExpressionDim.Pressure, 1f));
        double worst = 0;
        for (int k = 0; k < six.Length; k++)
            if (six[k] != 55) worst = Math.Max(worst, Math.Abs(Db(At(pressed, fund[k], 4800)) - Db(At(plain, fund[k], 4800))));
        double self = Math.Abs(Db(At(pressed, 196 * 3, 4800)) - Db(At(plain, 196 * 3, 4800)));
        yield return (worst <= 0.5 && self > 1, $"pressure on one note of a 6-note chord opens only that note (others within {worst:0.00} dB, its 3rd harmonic {self:+0.0;-0.0} dB)");

        var bent = Expr((xe, xt) => xe.TrackNoteExpression(xt, 55, NoteExpressionDim.Bend, 2f));
        double bworst = 0;
        for (int k = 0; k < six.Length; k++)
            if (six[k] != 55) bworst = Math.Max(bworst, Math.Abs(Db(At(bent, fund[k], 4800)) - Db(At(plain, fund[k], 4800))));
        yield return (bworst <= 0.5 && At(bent, 220, 4800) > 4 * At(plain, 220, 4800) && At(bent, 196, 4800) < 0.5 * At(plain, 196, 4800),
            $"a per-note bend of +2 st moves G3 to A3 and leaves the others (within {bworst:0.00} dB)");

        var chan = Expr((xe, xt) => xe.TrackNoteExpression(xt, -1, NoteExpressionDim.Pressure, 1f));
        int brighter = 0;
        for (int k = 0; k < six.Length; k++) if (Db(At(chan, fund[k] * 3, 4800)) - Db(At(plain, fund[k] * 3, 4800)) > 1) brighter++;
        yield return (brighter == six.Length, $"channel pressure opens every voice ({brighter}/{six.Length} brighter)");

        var padX = Expr((xe, xt) => { P(xe, xt, "lo.a.o1bal", 0f); P(xe, xt, "lo.a.mmcut", 0.5f); P(xe, xt, "padx", 1f); });
        var padX0 = Expr((xe, xt) => { P(xe, xt, "lo.a.o1bal", 0f); P(xe, xt, "lo.a.mmcut", 0.5f); });
        var padY = Expr((xe, xt) => { P(xe, xt, "lo.b.ldcut", 0.9f); P(xe, xt, "pady", 1f); });
        var padY0 = Expr((xe, xt) => P(xe, xt, "lo.b.ldcut", 0.9f));
        yield return (Diff(padX, padX0) > 0.01 && Diff(padY, padY0) > 0.01, "the pad's X moves the multimode cutoff and its Y the morph");

        // ---- the matrix (US4) ---------------------------------------------------------------------
        using (var me = new NotaEngine())
        {
            int mt = me.AddTwinTrack();
            var srcBad = new List<string>();
            for (int k = 0; k < TwinMatrix.Sources.Length; k++)
            {
                P(me, mt, "lo.m64.src", k / 63f);
                if (me.PluginParamText(mt, -1, Pi(me, mt, "lo.m64.src")) != TwinMatrix.Sources[k]) srcBad.Add(TwinMatrix.Sources[k]);
            }
            var dstBad = new List<string>();
            for (int k = 0; k < TwinMatrix.DestinationCount; k++)
            {
                P(me, mt, "lo.m64.dst", k / 255f);
                string dn = me.PluginParamText(mt, -1, Pi(me, mt, "lo.m64.dst"));
                if (dn != TwinMatrix.DestinationName(me, mt, k)) dstBad.Add($"{k}:{dn}");
            }
            P(me, mt, "lo.m64.dst", TwinMatrix.DestinationCount / 255f);
            yield return (srcBad.Count == 0 && dstBad.Count == 0 && me.PluginParamText(mt, -1, Pi(me, mt, "lo.m64.dst")) == "",
                $"the app's matrix tables match the engine's ({TwinMatrix.Sources.Length} sources, {TwinMatrix.DestinationCount} destinations){(srcBad.Count + dstBad.Count > 0 ? " — " + string.Join(", ", srcBad.Concat(dstBad)) : "")}");

            P(me, mt, "lo.m64.src", 0f); P(me, mt, "lo.m64.dst", 0f);
            me.ClearUndoHistory();
            var before = TwinMatrix.Routes(me, mt).Count;
            int slot = TwinMatrix.Add(me, mt, 2, TwinMatrix.DestinationOfSide("o1wave"), 0.5f, -0.5f);
            bool added = slot == 7 && TwinMatrix.Routes(me, mt).Count == before + 1;
            bool undone = me.Undo() && TwinMatrix.Routes(me, mt).Count == before && !me.CanUndo;
            int filled = 0;
            while (TwinMatrix.Add(me, mt, 1, TwinMatrix.DestinationOfSide("ldcut"), 0.1f, 0.1f) > 0) filled++;
            yield return (added && undone && filled == 64 - before && TwinMatrix.Add(me, mt, 1, 1, 0.1f, 0.1f) == -1,
                $"a route lands in the first free slot and undoes in one step; the matrix fills at 64 and then refuses ({before} defaults + {filled})");
            TwinMatrix.Remove(me, mt, 30);
            yield return (TwinMatrix.Routes(me, mt).Count == 63 && TwinMatrix.Add(me, mt, 1, 1, 0.1f, 0.1f) == 30, "removing a route frees its slot");
        }

        // A route of depth 0 changes nothing; a depth on B only follows the morph.
        void Lfo2Cut(NotaEngine xe, int xt, float a, float b)
        {
            P(xe, xt, "lo.m7.src", 2 / 63f); P(xe, xt, "lo.m7.dst", TwinMatrix.DestinationOfSide("ldcut") / 255f);
            P(xe, xt, "lo.a.m7amt", 0.5f + 0.5f * a); P(xe, xt, "lo.b.m7amt", 0.5f + 0.5f * b);
            P(xe, xt, "lo.a.lfo2rate", 0.8f); P(xe, xt, "lo.b.lfo2rate", 0.8f);
        }
        var none = Chord((xe, xt) => P(xe, xt, "lo.a.ldcut", 0.5f));
        var zero = Chord((xe, xt) => { P(xe, xt, "lo.a.ldcut", 0.5f); Lfo2Cut(xe, xt, 0f, 0f); });
        yield return (Diff(none, zero) == 0, $"a route of depth 0 renders exactly like no route (max diff {Diff(none, zero):G3})");
        var onlyB0 = Chord((xe, xt) => { P(xe, xt, "lo.a.ldcut", 0.5f); P(xe, xt, "lo.b.ldcut", 0.5f); Lfo2Cut(xe, xt, 0f, 0.6f); });
        var onlyB1 = Chord((xe, xt) => { P(xe, xt, "lo.a.ldcut", 0.5f); P(xe, xt, "lo.b.ldcut", 0.5f); Lfo2Cut(xe, xt, 0f, 0.6f); P(xe, xt, "lo.morph", 1f); });
        yield return (Diff(onlyB0, none) == 0 && Diff(onlyB1, none) > 0.01, "a route deep on side B only moves the sound as the morph reaches B");

        // Pressure → Morph (the default cutoff routes cleared): only the pressed note morphs.
        float[] PressMorph(bool press) => Expr((xe, xt) =>
        {
            for (int r = 1; r <= 6; r++) P(xe, xt, $"lo.m{r}.dst", 0f);
            TwinMatrix.Add(xe, xt, 10, 1, 1f, 1f);
            P(xe, xt, "lo.b.ldcut", 0.95f); P(xe, xt, "lo.b.ldres", 0.4f);
            if (press) xe.TrackNoteExpression(xt, 55, NoteExpressionDim.Pressure, 1f);
        });
        var pm0 = PressMorph(false);
        var pm1 = PressMorph(true);
        double mworst = 0;
        for (int k = 0; k < six.Length; k++)
            if (six[k] != 55) mworst = Math.Max(mworst, Math.Abs(Db(At(pm1, fund[k], 4800)) - Db(At(pm0, fund[k], 4800))));
        double mself = Math.Abs(Db(At(pm1, 196 * 3, 4800)) - Db(At(pm0, 196 * 3, 4800)));
        yield return (mworst <= 0.5 && mself > 1, $"pressure routed to the Morph morphs only the pressed note (others within {mworst:0.00} dB, its 3rd harmonic {mself:+0.0;-0.0} dB)");

        // MCP: read the patch and set a route by name.
        using (var ce = new NotaEngine())
        {
            int ct = ce.AddTwinTrack();
            var mcp = new Nota.Mcp.Tools.TwinTools(ce, new SyncDispatch(), new NoRefresh());
            P(ce, ct, "lo.b.ldcut", 0.9f);
            string r1 = mcp.SetTwinRoute(ct, "lfo 3", "Osc1 Wave", 40, -20).Result;
            string r2 = mcp.SetTwinRoute(ct, "Nope", "Morph").Result;
            var rd = mcp.ReadTwin(ct).Result!;
            bool routeOk = r1.StartsWith("slot 7: LFO 3 → Osc1 Wave") && r2.StartsWith("error") && rd.Routes.Length == 7
                           && rd.Routes[^1] is { Source: "LFO 3", Destination: "Osc1 Wave", DepthAPercent: 40, DepthBPercent: -20 };
            bool readOk = rd.Sound.Any(x => x.Id == "ldcut" && x.Differs) && rd.Summary.Contains("1 of") && ce.Undo() && TwinMatrix.Routes(ce, ct).Count == 6;
            yield return (routeOk && readOk, $"MCP set_twin_route adds a named route in one undo step and read_twin reports sides and routes ({r1} | {r2})");
            yield return (mcp.ReadTwin(ce.AddVoltSynthTrack()).Result is null, "read_twin answers null for another instrument");
            P(ce, ct, "mode", 1f); P(ce, ct, "splitkey", 48f / 127f); P(ce, ct, "share", 0.25f);
            string cpart = mcp.CopyTwinPart(ct, "lower_to_upper").Result;
            var up = mcp.ReadTwin(ct, "up").Result!;
            bool partsOk = cpart.StartsWith("lower_to_upper:") && up.Parts is { Layout: "Split", SplitKey: "C3", LowerVoices: 4, UpperVoices: 8 }
                && up.Routes.Length == 6 && up.Summary.Contains("Split at C3 4 + 8") && up.Summary.Contains("Upper");
            yield return (partsOk && ce.Undo() && G(ce, ct, "up.b.ldcut") != 0.9f, $"MCP copy_twin_part copies a part in one undo step; read_twin reports the layout ({up.Summary})");
            P(ce, ct, "mode", 0f);
            string sm = mcp.SetTwinMorphSide(ct, "b", "o1wave=1; ldres=0.6, nope=1").Result;
            bool sideSet = sm.StartsWith("error: unknown or malformed: nope") && G(ce, ct, "lo.b.o1wave") == 1f && Math.Abs(G(ce, ct, "lo.b.ldres") - 0.6f) < 1e-6 && G(ce, ct, "lo.a.o1wave") != 1f;
            string cp = mcp.CopyTwinSide(ct, "b_to_a").Result;
            bool copiedA = cp.StartsWith("b_to_a: 164") && G(ce, ct, "lo.a.o1wave") == 1f && ce.Undo() && G(ce, ct, "lo.a.o1wave") != 1f;
            yield return (sideSet && copiedA, $"MCP set_twin_morph_side writes one side, copy_twin_side copies it in one undo step ({sm} | {cp})");
        }

        // ---- effects (US5) ------------------------------------------------------------------------
        float[] Fx(Action<NotaEngine, int> setup, int bpm = 120, double secs = 2.0, double noteBeats = 3.5)
        {
            using var fe = new NotaEngine();
            fe.SetBpm(bpm);
            int ft = fe.AddTwinTrack();
            setup(fe, ft);
            fe.AddMidiClip(ft, 0.0, 16.0);
            fe.SetClipNotes(ft, 0, new[] { 48, 55, 60, 64 }.Select(n => new NotaNote(n, 0, noteBeats, 0.8f)).ToArray());
            var o = new float[(int)(secs * Sr) * 2];
            var tmp = new float[256 * 2];
            fe.Seek(0); fe.Play();
            for (int done = 0; done < o.Length / 2;) { int m = Math.Min(256, o.Length / 2 - done); fe.RenderOffline(tmp, m, Sr); Array.Copy(tmp, 0, o, done * 2, m * 2); done += m; }
            return o;
        }
        void AllFx(NotaEngine fe, int ft) { P(fe, ft, "lo.a.modfxon", 1f); P(fe, ft, "lo.a.dlyon", 1f); P(fe, ft, "lo.a.revon", 1f); }
        var dry = Fx((fe, ft) => { });
        var bypassed = Fx((fe, ft) => { AllFx(fe, ft); P(fe, ft, "lo.fxon", 0f); });
        yield return (Diff(dry, bypassed) == 0, $"the effects section off renders exactly the voices alone (max diff {Diff(dry, bypassed):G3})");
        var heard = new List<string>();
        foreach (var (name, setup) in new (string, Action<NotaEngine, int>)[]
                 {
                     ("chorus", (fe, ft) => { P(fe, ft, "lo.a.modfxon", 1f); P(fe, ft, "lo.a.modfxtype", 0f); }),
                     ("flanger", (fe, ft) => { P(fe, ft, "lo.a.modfxon", 1f); P(fe, ft, "lo.a.modfxtype", 1f / 3f); }),
                     ("phaser", (fe, ft) => { P(fe, ft, "lo.a.modfxon", 1f); P(fe, ft, "lo.a.modfxtype", 2f / 3f); }),
                     ("ensemble", (fe, ft) => { P(fe, ft, "lo.a.modfxon", 1f); P(fe, ft, "lo.a.modfxtype", 1f); }),
                     ("delay", (fe, ft) => P(fe, ft, "lo.a.dlyon", 1f)),
                     ("reverb", (fe, ft) => P(fe, ft, "lo.a.revon", 1f)),
                 })
        {
            var w = Fx(setup);
            if (w.All(float.IsFinite) && Diff(w, dry) > 0.005 && Peak(w) < 1.0) heard.Add(name);
        }
        yield return (heard.Count == 6, $"every effect is heard, finite, under 0 dBFS ({string.Join(", ", heard)})");

        // A synced 1/4 delay of a short note: the echo lands a beat later at either tempo.
        double EchoAt(int bpm)
        {
            var o = Fx((fe, ft) => { P(fe, ft, "lo.a.dlyon", 1f); P(fe, ft, "lo.a.dlysync", 1f); P(fe, ft, "lo.a.dlydiv", 5f / 7f);
                P(fe, ft, "lo.a.dlyfb", 0f); P(fe, ft, "lo.a.dlymix", 0.5f); P(fe, ft, "lo.a.dlypingpong", 0f); P(fe, ft, "lo.a.aenvr", 0.05f); }, bpm, 1.5, 0.1);
            // From the direct sound's loudest window to the echo's: the note's own rise cancels out.
            int win = Sr / 200, split = (int)(0.25 * 60.0 / bpm * Sr);
            int Loudest(int from, int to)
            {
                int best = from; double bestE = -1;
                for (int at = from; at + win < to; at += win)
                {
                    double en = 0;
                    for (int i = at; i < at + win; i++) en += o[2 * i] * (double)o[2 * i];
                    if (en > bestE) { bestE = en; best = at; }
                }
                return best;
            }
            return (Loudest(split, o.Length / 2) - Loudest(0, split)) / (double)Sr;
        }
        double e120 = EchoAt(120), e90 = EchoAt(90);
        yield return (Math.Abs(e120 - 0.5) < 0.03 && Math.Abs(e90 - 2.0 / 3.0) < 0.03, $"a synced 1/4 delay follows the tempo (echo {e120:0.000} s after the note at 120 BPM, {e90:0.000} s at 90)");

        // A long reverb keeps ringing after the note: the instrument doesn't sleep through its tail.
        var tail = Fx((fe, ft) => { P(fe, ft, "lo.a.revon", 1f); P(fe, ft, "lo.a.revsize", 1f); P(fe, ft, "lo.a.revmix", 0.6f); P(fe, ft, "lo.a.aenvr", 0.05f); }, 120, 6.0, 0.5);
        var noRev = Fx((fe, ft) => P(fe, ft, "lo.a.aenvr", 0.05f), 120, 6.0, 0.5);
        double Win(float[] o, double t0, double t1) { double en = 0; int a = (int)(t0 * Sr), b = (int)(t1 * Sr); for (int i = a; i < b; i++) en += o[2 * i] * (double)o[2 * i]; return Math.Sqrt(en / (b - a)); }
        yield return (Win(tail, 1.0, 1.5) > 1e-3 && Win(noRev, 1.0, 1.5) < 1e-5, $"a reverb tail rings past the note ({Db(Win(tail, 1.0, 1.5)):0} dB 0.75 s after it, {Db(Win(noRev, 1.0, 1.5)):0} dB without)");


        // ---- the sequencer (US8) --------------------------------------------------------------------
        foreach (var r in SequencerChecks()) yield return r;

        // ---- Split / Layer (US9) ------------------------------------------------------------------
        foreach (var r in PartChecks()) yield return r;
        foreach (var r in EdgeChecks()) yield return r;
    }

    // Lower panned hard left, Upper hard right, so a channel says which part played.
    private static (NotaEngine E, int T) PartRig(int mode, float splitKey = 48f / 127f, float share = 0.5f)
    {
        var e = new NotaEngine();
        int t = e.AddTwinTrack();
        P(e, t, "mode", mode / 2f); P(e, t, "splitkey", splitKey); P(e, t, "share", share);
        P(e, t, "lo.pan", 0f); P(e, t, "up.pan", 1f);
        P(e, t, "lo.a.spread", 0f); P(e, t, "up.a.spread", 0f);
        Render(e, 512);
        return (e, t);
    }
    private static double Ch(float[] st, int ch) { double s = 0; for (int i = ch; i < st.Length; i += 2) s += st[i] * (double)st[i]; return Math.Sqrt(s / (st.Length / 2)); }

    private static IEnumerable<(bool, string)> PartChecks()
    {
        {
            var (e, t) = PartRig(2);
            using (e)
            {
                e.TrackNoteOn(t, 47, 0.8f);
                var lowKey = Render(e, Sr / 2);
                e.TrackNoteOff(t, 47); Render(e, Sr);
                e.TrackNoteOn(t, 48, 0.8f);
                var highKey = Render(e, Sr / 2);
                yield return (Ch(lowKey, 0) > 100 * Ch(lowKey, 1) && Ch(highKey, 1) > 100 * Ch(highKey, 0),
                    $"Split at C3: B2 plays the Lower part, C3 the Upper (B2 L {Db(Ch(lowKey, 0)):0} / R {Db(Ch(lowKey, 1)):0} dB; C3 L {Db(Ch(highKey, 0)):0} / R {Db(Ch(highKey, 1)):0} dB)");
            }
        }
        {
            var (e, t) = PartRig(2, 60f / 127f, 0.25f); // Lower 4, Upper 8
            using (e)
            {
                foreach (var n in new[] { 36, 38, 40, 41, 43, 45 }) e.TrackNoteOn(t, n, 0.8f);
                foreach (var n in new[] { 60, 62, 64, 65, 67, 69, 71, 72, 74 }) e.TrackNoteOn(t, n, 0.8f);
                Render(e, 4096);
                var sc = new float[32];
                e.InstrumentScope(t, sc);
                yield return (sc[4] == 4 && sc[5] == 8, $"a 4 + 8 share: six low keys sound on 4 voices, nine high ones on Upper's 8 ({sc[4]} + {sc[5]})");
            }
        }
        {
            var (e, t) = PartRig(1);
            using (e)
            {
                e.TrackNoteOn(t, 60, 0.8f);
                var both = Render(e, Sr / 2);
                yield return (e.InstrumentVoiceCount(t) == 2 && Ch(both, 0) > 1e-3 && Ch(both, 1) > 1e-3, "Layer: one key plays both parts, each through its own pan");
                foreach (var n in new[] { 48, 52, 55 }) e.TrackNoteOn(t, n, 0.8f);
                Render(e, 2048);
                P(e, t, "mode", 0f); // back to Single with keys down
                Render(e, 2048);
                foreach (var n in new[] { 48, 52, 55, 60 }) e.TrackNoteOff(t, n);
                var rest = Render(e, Sr * 2);
                yield return (e.InstrumentVoiceCount(t) == 0 && Rms(rest, Sr) < 1e-5, $"changing the layout with keys down leaves no note hanging ({e.InstrumentVoiceCount(t)} voices)");
            }
        }
        {
            // The sequencer on Upper only, in a Layer.
            var (e, t) = SeqRig(120, Enumerable.Repeat("C3:100:50%", 8).ToArray(), (x, y) =>
            {
                P(x, y, "mode", 0.5f); P(x, y, "seq.target", 0.5f); P(x, y, "lo.a.spread", 0f); P(x, y, "up.a.spread", 0f); P(x, y, "lo.pan", 0f); P(x, y, "up.pan", 1f);
                P(x, y, "up.a.o1level", 0f); P(x, y, "up.a.noiselevel", 1f); P(x, y, "up.a.aenva", 0f);
            });
            using (e)
            {
                var o = Roll(e, 1.0);
                yield return (Ch(o, 1) > 1e-3 && Ch(o, 0) < 1e-6, $"seq.target Upper: the pattern plays only the Upper part (L {Db(Ch(o, 0)):0} / R {Db(Ch(o, 1)):0} dB)");
            }
        }
        // Part edits (FR-075): copy Lower → Upper, then load a preset into Upper of a Single patch.
        using (var pe = new NotaEngine())
        {
            int pt = pe.AddTwinTrack();
            P(pe, pt, "lo.a.ldcut", 0.2f); P(pe, pt, "lo.b.o1wave", 1f); P(pe, pt, "lo.voicemode", 1f);
            pe.ClearUndoHistory();
            int wrote = Nota.Application.Twin.TwinParts.Apply(pe, pt, Nota.Application.Twin.TwinParts.Edit.CopyLowerToUpper);
            bool copied = G(pe, pt, "up.a.ldcut") == 0.2f && G(pe, pt, "up.b.o1wave") == 1f && G(pe, pt, "up.voicemode") == 1f;
            bool undone = pe.Undo() && G(pe, pt, "up.a.ldcut") != 0.2f && !pe.CanUndo;
            yield return (copied && undone, $"Copy Lower → Upper moves the whole part in one undo step (wrote {wrote})");

            var preset = new Dictionary<string, float> { ["lo.a.ldcut"] = 0.8f, ["lo.morph"] = 0.3f, ["lo.m1.src"] = 0f };
            string warn = Nota.Application.Twin.TwinParts.LoadIntoPart(pe, pt, preset, "up");
            bool loaded = warn == "" && G(pe, pt, "up.a.ldcut") == 0.8f && G(pe, pt, "up.morph") == 0.3f && G(pe, pt, "lo.a.ldcut") == 0.2f
                && G(pe, pt, "mode") == 0.5f && G(pe, pt, "up.b.o1wave") == pe.InstrumentParamDefault(pt, Pi(pe, pt, "up.b.o1wave"));
            bool back = pe.Undo() && G(pe, pt, "mode") == 0f && G(pe, pt, "up.a.ldcut") != 0.8f && !pe.CanUndo;
            yield return (loaded && back, "Load into Upper of a Single patch: Upper takes the preset, the current sound stays Lower, Layer — one undo step");

            // A saved user preset loads into a part the same way (FR-075).
            string pdir = Path.Combine(Path.GetTempPath(), "nota-twin-presets-" + Guid.NewGuid().ToString("N"));
            try
            {
                int src = pe.AddTwinTrack();
                P(pe, src, "lo.a.ldcut", 0.37f); P(pe, src, "lo.b.o2wave", 1f);
                var store = new PresetStore();
                bool saved = store.Save(pe, src, -1, "My Twin", pdir);
                var file = new PresetLibrary().List(pdir).FirstOrDefault(x => x.DisplayName == "My Twin");
                var prm = file.Path is null ? null : store.InstrumentParams(file.Path);
                bool ok = saved && prm is not null && store.Describe(file.Path!) is { BuiltinKind: 18 }
                    && TwinParts.LoadIntoPart(pe, pt, prm, "up") == "" && G(pe, pt, "up.a.ldcut") == 0.37f && G(pe, pt, "up.b.o2wave") == 1f;
                yield return (ok, "a saved Twin preset loads into the Upper part (the card's Parts › Load into Upper lists it)");
            }
            finally { try { Directory.Delete(pdir, true); } catch (IOException) { } }
        }
    }

    // A Twin with a clicky test voice (noise through an open ladder, instant attack, quick release,
    // no drift, no effects), the sequencer on at 1/16 playing `rows`, the transport rolling.
    private static (NotaEngine E, int T) SeqRig(int bpm, string[] rows, Action<NotaEngine, int>? setup = null)
    {
        var e = new NotaEngine();
        e.SetBpm(bpm);
        int t = e.AddTwinTrack();
        foreach (var (id, v) in new[] { ("lo.a.o1level", 0f), ("lo.a.noiselevel", 1f), ("lo.a.ldcut", 1f), ("lo.a.aenva", 0f), ("lo.a.aenvr", 0f),
                     ("lo.a.drift", 0f), ("seq.on", 1f), ("seq.rate", 0.6f) })
            P(e, t, id, v);
        TwinPatterns.WritePattern(e, t, 0, rows);
        setup?.Invoke(e, t);
        return (e, t);
    }
    private static float[] Roll(NotaEngine e, double secs, int block = 512, Action<int>? atBlock = null)
    {
        var o = new float[(int)(secs * Sr) * 2];
        var tmp = new float[block * 2];
        e.Seek(0); e.Play();
        for (int done = 0, b = 0; done < o.Length / 2; b++) { atBlock?.Invoke(b); int m = Math.Min(block, o.Length / 2 - done); e.RenderOffline(tmp, m, Sr); Array.Copy(tmp, 0, o, done * 2, m * 2); done += m; }
        return o;
    }
    // Sample indices where the sound starts after at least `gap` samples of silence.
    private static List<int> Onsets(float[] st, int gap = 64)
    {
        var l = new List<int>();
        int quiet = gap;
        for (int i = 0; i < st.Length / 2; i++)
        {
            bool loud = Math.Abs(st[2 * i]) > 1e-7f || Math.Abs(st[2 * i + 1]) > 1e-7f;
            if (loud && quiet >= gap) l.Add(i);
            quiet = loud ? 0 : quiet + 1;
        }
        return l;
    }

    // ---- determinism and edge cases (T082, T083) ----------------------------------------------
    // A patch that uses everything that could drift: two layered parts, drift, the Random source,
    // effects, a sequencer pattern with a motion lane, and a clip.
    private static void Rich(NotaEngine e, int t)
    {
        P(e, t, "mode", 0.5f);
        foreach (var part in new[] { "lo", "up" })
        {
            P(e, t, $"{part}.a.drift", 0.8f); P(e, t, $"{part}.a.o2level", 0.7f); P(e, t, $"{part}.b.ldcut", 0.85f); P(e, t, $"{part}.morph", 0.4f);
            P(e, t, $"{part}.a.dlyon", 1f); P(e, t, $"{part}.a.revon", 1f); P(e, t, $"{part}.a.modfxon", 1f);
            TwinMatrix.Add(e, t, Array.IndexOf(TwinMatrix.Sources, "Random"), TwinMatrix.DestinationOfSide("mmcut"), 0.4f, 0.4f, part);
        }
        P(e, t, "up.octave", 0.75f);
        P(e, t, "seq.on", 1f); P(e, t, "seq.target", 0f);
        TwinPatterns.WritePattern(e, t, 0, new[] { "C3 G3:110:50%", ".", "A2:80:tie", "A2:70:30%", "D#3:90:60%", ".", "G3:100:40%", "F3:90:50%" });
        TwinPatterns.Do(e, t, TwinPatterns.Act.MotionTarget, Pi(e, t, "lo.morph"));
        for (int k = 0; k < 8; k++) TwinPatterns.Do(e, t, TwinPatterns.Act.MotionSet, k, k / 7f);
        e.AddMidiClip(t, 0.0, 8.0);
        e.SetClipNotes(t, 0, new[] { new NotaNote(72, 0.5, 1.5, 0.8f), new NotaNote(76, 2.0, 1.0, 0.6f) });
        e.ClearUndoHistory();
    }

    private static float[] Take(NotaEngine e, int frames, double sr = Sr, int block = 512)
    {
        var o = new float[frames * 2];
        var tmp = new float[block * 2];
        e.Seek(0); e.Play();
        for (int done = 0; done < frames;) { int m = Math.Min(block, frames - done); e.RenderOffline(tmp, m, sr); Array.Copy(tmp, 0, o, done * 2, m * 2); done += m; }
        e.StopTransport();
        return o;
    }

    // Transport stopped until the voices and the effects' tails are gone: the next start is a
    // silent one, which re-seeds the instrument (so it renders as a fresh one would).
    private static void Settle(NotaEngine e, double sr = Sr)
    {
        var tmp = new float[4096 * 2];
        for (int k = 0; k < 6 * (int)sr / 4096; k++) e.RenderOffline(tmp, 4096, sr);
    }

    private static IEnumerable<(bool, string)> EdgeChecks()
    {
        // SC-006: an idle Twin (no notes, tails gone; a stopped sequencer included) costs what an
        // empty track does. Render time, best of five, transport stopped.
        double Cost(Action<NotaEngine>? setup)
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            setup?.Invoke(e);
            var tmp = new float[512 * 2];
            for (int k = 0; k < 8 * Sr / 512; k++) e.RenderOffline(tmp, 512, Sr); // let every tail die
            double best = double.MaxValue;
            for (int run = 0; run < 5; run++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int k = 0; k < 5 * Sr / 512; k++) e.RenderOffline(tmp, 512, Sr);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        double empty = Cost(e => e.AddAudioTrack());
        double idleTwin = Cost(e => { int t = e.AddTwinTrack(); Rich(e, t); e.TrackNoteOn(t, 60, 0.8f); e.RenderOffline(new float[1024], 512, Sr); e.TrackNoteOff(t, 60); });
        double busy = Cost(e => { int t = e.AddTwinTrack(); foreach (var n in new[] { 48, 52, 55, 59 }) e.TrackNoteOn(t, n, 0.8f); });
        yield return (idleTwin <= empty * 1.5 + 2.0 && busy > idleTwin * 5,
            $"SC-006: an idle Twin with a stopped sequencer costs what an empty track does ({idleTwin:0.0} vs {empty:0.0} ms per 5 s; a held chord {busy:0} ms)");

        // FR-030: the release velocity of a note-off reaches the matrix ("Release velocity").
        double Tail(float? relVel)
        {
            using var e = new NotaEngine();
            int t = e.AddTwinTrack();
            P(e, t, "lo.a.aenvr", 0.25f);
            TwinMatrix.Add(e, t, Array.IndexOf(TwinMatrix.Sources, "Release velocity"), TwinMatrix.DestinationOfSide("aenvr"), 1f, 1f);
            Render(e, 512);
            e.TrackNoteOn(t, 60, 0.8f);
            Render(e, Sr / 4);
            if (relVel is float rv) e.TrackNoteOffVelocity(t, 60, rv); else e.TrackNoteOff(t, 60);
            Render(e, Sr / 10);
            return Rms(Render(e, Sr / 2));
        }
        // …and through an Instrument Rack to a Twin on a chain.
        double RackTail(float relVel)
        {
            using var e = new NotaEngine();
            int r = e.AddInstrumentRackTrack();
            e.RackAddChain(r, 18);
            void C(string id, float v)
            {
                for (int i = 0; i < e.RackChainInstrumentParamCount(r, 0); i++)
                    if (e.RackChainInstrumentParamId(r, 0, i) == id) { e.RackChainInstrumentParamSet(r, 0, i, v); return; }
            }
            C("lo.a.aenvr", 0.25f); C("lo.b.aenvr", 0.25f);
            C("lo.m7.src", Array.IndexOf(TwinMatrix.Sources, "Release velocity") / 63f);
            C("lo.m7.dst", TwinMatrix.DestinationOfSide("aenvr") / 255f);
            C("lo.a.m7amt", 1f); C("lo.b.m7amt", 1f);
            Render(e, 512);
            e.TrackNoteOn(r, 60, 0.8f);
            Render(e, Sr / 4);
            e.TrackNoteOffVelocity(r, 60, relVel);
            Render(e, Sr / 10);
            return Rms(Render(e, Sr / 2));
        }
        double rSoft = RackTail(0.05f), rHard = RackTail(1f);
        yield return (Db(rHard) - Db(rSoft) > 6, $"release velocity reaches a Twin on an Instrument Rack chain ({Db(rSoft):0} / {Db(rHard):0} dB)");
        double soft = Tail(0.05f), hard = Tail(1f), none = Tail(null);
        yield return (Db(hard) - Db(soft) > 6 && Math.Abs(Db(none) - Db(soft)) < 3,
            $"release velocity drives a route: a hard release rings longer ({Db(soft):0} / {Db(hard):0} dB; no velocity {Db(none):0} dB)");

        const int N = Sr * 3;
        float[] first;
        using (var e = new NotaEngine()) { e.SetBpm(120); int t = e.AddTwinTrack(); Rich(e, t); first = Take(e, N); }
        float[] again;
        using (var e = new NotaEngine()) { e.SetBpm(120); int t = e.AddTwinTrack(); Rich(e, t); again = Take(e, N, block: 333); }
        yield return (Rms(first) > 1e-3 && Diff(first, again) == 0, $"SC-008: a project with drift, Random, FX and the sequencer renders identically twice (maxdiff {Diff(first, again):G3})");

        // Save / load: the reloaded project renders the same.
        string dir = Path.Combine(Path.GetTempPath(), "nota-twin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var transport = new TransportState(120.0, 1.0, false, false);
            using (var e = new NotaEngine()) { e.SetBpm(120); int t = e.AddTwinTrack(); Rich(e, t); ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e); }
            using var r = new NotaEngine();
            var warn = ProjectService.Apply(ProjectService.Load(dir), r, dir);
            r.SetBpm(120);
            var back = Take(r, N);
            yield return (warn.Count == 0 && Diff(first, back) == 0, $"SC-008: after save / load the project renders identically ({warn.Count} warnings, maxdiff {Diff(first, back):G3})");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        // Another sample rate: the patch sounds the same, and coming back renders identically.
        using (var e = new NotaEngine())
        {
            e.SetBpm(120); int t = e.AddTwinTrack(); Rich(e, t);
            var hi = Take(e, N * 2, 96000);
            Settle(e, 96000);
            var back = Take(e, N);
            double dl = Db(Rms(hi)) - Db(Rms(first));
            yield return (hi.All(float.IsFinite) && Math.Abs(dl) < 1.0 && Diff(first, back) == 0,
                $"at 96 kHz the patch sounds the same ({dl:+0.00;-0.00} dB), and back at 48 kHz it renders identically (maxdiff {Diff(first, back):G3})");
        }

        // Freeze: the frozen audio is the live render.
        using (var e = new NotaEngine())
        {
            e.SetBpm(120); int t = e.AddTwinTrack(); Rich(e, t);
            var live = Take(e, N);
            Settle(e);
            e.Stop(); e.SetLoop(false, 0, 0); e.SetMetronome(false); e.StopTransport();
            long total = e.BeginFreeze(t, 6.0);
            e.StopTransport(); e.Seek(0); e.Play();
            var chunk = new float[4096 * 2];
            for (long rem = total; rem > 0;) { int m = (int)Math.Min(4096, rem); e.RenderOffline(chunk, m); rem -= m; }
            e.StopTransport(); e.EndFreeze(t);
            Settle(e);
            var frozen = Take(e, N);
            yield return (e.IsTrackFrozen(t) && Diff(live, frozen) < 1e-4, $"a frozen Twin plays its live render (maxdiff {Diff(live, frozen):G3})");
        }

        // Copy / paste the instrument, and a Twin on an Instrument Rack chain duplicated with its track.
        using (var e = new NotaEngine())
        {
            e.SetBpm(120); int t = e.AddTwinTrack(); Rich(e, t);
            int dst = e.AddInstrumentTrack();
            bool copied = e.CopyDevices(t, Array.Empty<int>(), true, Array.Empty<int>()) == 1;
            e.PasteDevices(dst, -1, -1, out _);
            bool same = e.TrackInstrumentKind(dst) == 18 && e.GetPluginState(dst, -1).SequenceEqual(e.GetPluginState(t, -1));
            yield return (copied && same, "copy / paste of a Twin brings both parts, both sides, the matrix, the effects and the patterns");
        }
        using (var e = new NotaEngine())
        {
            int rack = e.AddInstrumentRackTrack();
            bool set = e.RackAddChain(rack, 18) == 0 && e.RackChainInstrumentKind(rack, 0) == 18; // a new rack starts empty
            int ci = -1;
            for (int i = 0; i < e.RackChainInstrumentParamCount(rack, 0); i++) if (e.RackChainInstrumentParamId(rack, 0, i) == "lo.b.ldcut") ci = i;
            if (ci >= 0) e.RackChainInstrumentParamSet(rack, 0, ci, 0.123f);
            int dup = e.DuplicateTrack(rack);
            bool kept = set && ci >= 0 && dup > 0 && e.RackChainInstrumentKind(dup, 0) == 18 && Math.Abs(e.RackChainInstrumentParamGet(dup, 0, ci) - 0.123f) < 1e-6;
            e.TrackNoteOn(rack, 60, 0.8f);
            var rb = Render(e, Sr / 2);
            yield return (kept && Rms(rb) > 1e-3 && rb.All(float.IsFinite),
                $"a Twin on an Instrument Rack chain plays, and duplicating the track keeps its side B ({Db(Rms(rb)):0} dB)");
        }
    }

    private static IEnumerable<(bool, string)> SequencerChecks()
    {
        // Edits are undo steps; the state carries the patterns.
        using (var e = new NotaEngine())
        {
            int t = e.AddTwinTrack();
            e.ClearUndoHistory();
            TwinPatterns.Do(e, t, TwinPatterns.Act.AddStepNote, 3 << 8 | 60, 0.7f);
            var after = TwinPatterns.FromState(e.GetPluginState(t, -1))!;
            bool added = after[0].Steps[3].Notes.SequenceEqual(new[] { 60 }) && after[0].Steps[3].Velocity == 89;
            bool undone = e.Undo() && TwinPatterns.FromState(e.GetPluginState(t, -1))![0].Steps[3].IsRest && !e.CanUndo;
            bool redone = e.Redo() && TwinPatterns.FromState(e.GetPluginState(t, -1))![0].Steps[3].Notes.Count == 1;
            yield return (added && undone && redone, "a pattern edit is one undo step, and redo brings it back");
            string err = TwinPatterns.WritePattern(e, t, 2, new[] { "C3 E3 G3:100:50%", ".", "A2:90:tie", "A2:90:25%" });
            int t2 = e.AddTwinTrack();
            e.SetPluginState(t2, -1, e.GetPluginState(t, -1));
            var p2 = TwinPatterns.FromState(e.GetPluginState(t2, -1))![2];
            yield return (err == "" && p2.Length == 4 && TwinPatterns.StepText(p2.Steps[0]) == "C3 E3 G3:100:50%" && p2.Steps[1].IsRest
                          && p2.Steps[2].Gate == TwinPatterns.GateTie,
                $"patterns round-trip through the text form and the state ({TwinPatterns.StepText(p2.Steps[0])} | {TwinPatterns.StepText(p2.Steps[2])})");
        }

        // SC-009: over 64 bars, at 60, 120 and 200 BPM, every 1/16 step lands within one sample
        // of the grid (onsets found while rendering, so the long runs keep no audio). A square
        // wave, not noise: its first sample is never near zero, so the detector can't be late.
        var rows = Enumerable.Repeat("C4:100:25%", 16).ToArray();
        foreach (int bpm in new[] { 60, 120, 200 })
        {
            var (e, t) = SeqRig(bpm, rows, (x, y) => { P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.o1wave", 1f); });
            using (e)
            {
                double spb = 60.0 * Sr / bpm;
                long frames = (long)Math.Ceiling(64 * 4 * spb) - 1;
                var tmp = new float[512 * 2];
                long first = -1, worst = 0, count = 0, quiet = 64;
                e.Seek(0); e.Play();
                for (long done = 0; done < frames;)
                {
                    int m = (int)Math.Min(512, frames - done);
                    e.RenderOffline(tmp, m, Sr);
                    for (int i = 0; i < m; i++)
                    {
                        bool loud = Math.Abs(tmp[2 * i]) > 1e-7f || Math.Abs(tmp[2 * i + 1]) > 1e-7f;
                        if (loud && quiet >= 64)
                        {
                            long at = done + i;
                            if (first < 0) first = at;
                            long expect = first + (long)Math.Ceiling(count * 0.25 * spb - 1e-6);
                            worst = Math.Max(worst, Math.Abs(at - expect));
                            count++;
                        }
                        quiet = loud ? 0 : quiet + 1;
                    }
                    done += m;
                }
                yield return (count == 64 * 16 && worst <= 1, $"SC-009: at {bpm} BPM, 64 bars of 1/16 steps land on the grid ({count} onsets, worst {worst} sample off)");
            }
        }

        // The same at any block size; a stop releases everything.
        {
            var (e1, t1) = SeqRig(120, new[] { "C3 G3:100:60%", "E3:80:tie", "E3:80:30%", "." });
            var (e2, t2) = SeqRig(120, new[] { "C3 G3:100:60%", "E3:80:tie", "E3:80:30%", "." });
            using (e1) using (e2)
            {
                var a = Roll(e1, 2.0, 512);
                var b = Roll(e2, 2.0, 77);
                double md = 0;
                for (int i = 0; i < a.Length; i++) md = Math.Max(md, Math.Abs(a[i] - b[i]));
                yield return (Rms(a) > 1e-3 && md == 0, $"a running pattern renders bit-identically at blocks of 512 and 77 (max diff {md:G3})");
                e1.StopTransport();
                var tail = new float[Sr / 2 * 2];
                e1.RenderOffline(tail, Sr / 2, Sr);
                yield return (e1.InstrumentVoiceCount(t1) == 0 && Rms(tail, Sr / 4) < 1e-5, $"stopping the transport releases the sequencer's notes ({e1.InstrumentVoiceCount(t1)} voices)");
            }
        }

        // Step record: a chord held and lifted is one step; the cursor moves on.
        using (var e = new NotaEngine())
        {
            int t = e.AddTwinTrack();
            TwinPatterns.Do(e, t, TwinPatterns.Act.StepRecordArm, 1);
            var blk = new float[512 * 2];
            foreach (var n in new[] { 48, 52, 55 }) e.TrackNoteOn(t, n, 0.9f);
            e.RenderOffline(blk, 512, Sr);
            foreach (var n in new[] { 48, 52, 55 }) e.TrackNoteOff(t, n);
            e.RenderOffline(blk, 512, Sr);
            e.TrackNoteOn(t, 60, 0.5f); e.RenderOffline(blk, 512, Sr); e.TrackNoteOff(t, 60); e.RenderOffline(blk, 512, Sr);
            e.ClearUndoHistory();
            TwinPatterns.Do(e, t, TwinPatterns.Act.StepRecordPoll);
            var pat = TwinPatterns.FromState(e.GetPluginState(t, -1))![0];
            bool rec = pat.Steps[0].Notes.OrderBy(x => x).SequenceEqual(new[] { 48, 52, 55 }) && pat.Steps[1].Notes.SequenceEqual(new[] { 60 });
            var sc = new float[32];
            e.InstrumentScope(t, sc);
            yield return (rec && sc[24] == 2, $"step record: a held chord becomes one step and the cursor moves on (cursor {sc[24]}; steps {TwinPatterns.StepText(pat.Steps[0])} | {TwinPatterns.StepText(pat.Steps[1])})");
            TwinPatterns.Do(e, t, TwinPatterns.Act.StepRecordArm, 0);
        }

        // A held key transposes the pattern from its first note.
        {
            var (e, t) = SeqRig(120, Enumerable.Repeat("C3:100:80%", 8).ToArray(), (x, y) => { P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.6f); });
            var (f, u) = SeqRig(120, Enumerable.Repeat("C3:100:80%", 8).ToArray(), (x, y) => { P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.6f); });
            using (e) using (f)
            {
                var plain = Roll(e, 1.0);
                var moved = Roll(f, 1.0, 512, b => { if (b == 2) f.TrackNoteOn(u, 52, 0.8f); });
                yield return (At(moved, 164.81, Sr / 2) > 4 * At(plain, 164.81, Sr / 2) && At(moved, 130.81, Sr / 2) < 0.5 * At(plain, 130.81, Sr / 2),
                    "holding E3 transposes a C3 pattern to E3 (the key itself doesn't sound)");
            }
        }

        // The arpeggiator walks the held chord, one note at a time, two octaves up.
        {
            var (e, t) = SeqRig(120, new[] { "." }, (x, y) => { P(x, y, "seq.kind", 1f); P(x, y, "arp.oct", 1f / 3f); P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.7f); });
            using (e)
            {
                int most = 0;
                var o = Roll(e, 2.0, 512, b =>
                {
                    if (b == 1) foreach (var n in new[] { 48, 52, 55 }) e.TrackNoteOn(t, n, 0.8f);
                    if (b > 4) most = Math.Max(most, e.InstrumentVoiceCount(t));
                });
                yield return (Rms(o, Sr / 2) > 1e-3 && most <= 2 && At(o, 261.63, Sr / 2) > 0.1 * At(o, 130.81, Sr / 2),
                    $"the arpeggiator plays the held chord a note at a time over two octaves (≤ {most} voices sounding)");
            }
        }

        // A motion lane on the Morph moves the sound step by step; recording the lane pauses it.
        {
            var rowsM = Enumerable.Repeat("C3:100:80%", 8).ToArray();
            void Lane(NotaEngine x, int y, bool arm)
            {
                P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.3f); P(x, y, "lo.b.ldcut", 0.95f);
                TwinPatterns.Do(x, y, TwinPatterns.Act.MotionTarget, 0 << 16 | Pi(x, y, "lo.morph"));
                for (int k = 0; k < 8; k++) TwinPatterns.Do(x, y, TwinPatterns.Act.MotionSet, 0 << 8 | k, k % 2);
                if (arm) TwinPatterns.Do(x, y, TwinPatterns.Act.MotionRecordArm, 0);
            }
            var (a, ta) = SeqRig(120, rowsM, (x, y) => { P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.3f); P(x, y, "lo.b.ldcut", 0.95f); });
            var (b, tb) = SeqRig(120, rowsM, (x, y) => Lane(x, y, false));
            var (c, tc) = SeqRig(120, rowsM, (x, y) => Lane(x, y, true));
            using (a) using (b) using (c)
            {
                var plain = Roll(a, 1.0);
                var moving = Roll(b, 1.0);
                var paused = Roll(c, 1.0);
                yield return (Diff(plain, moving) > 0.01 && Diff(plain, paused) == 0,
                    $"a motion lane on the Morph moves the sound; while the lane records, its playback pauses (diff {Diff(plain, paused):G3})");
            }
        }
        // A motion lane can play the expression pad too (FR-063): Pad X → the ladder cutoff.
        {
            var rowsP = Enumerable.Repeat("C3:100:80%", 8).ToArray();
            void Rig(NotaEngine x, int y, bool lane)
            {
                P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.4f);
                TwinMatrix.Add(x, y, Array.IndexOf(TwinMatrix.Sources, "Pad X"), TwinMatrix.DestinationOfSide("ldcut"), 0.5f, 0.5f);
                if (!lane) return;
                TwinPatterns.Do(x, y, TwinPatterns.Act.MotionTarget, 0 << 16 | Pi(x, y, "padx"));
                for (int k = 0; k < 8; k++) TwinPatterns.Do(x, y, TwinPatterns.Act.MotionSet, 0 << 8 | k, k % 2);
            }
            var (a, ta) = SeqRig(120, rowsP, (x, y) => Rig(x, y, false));
            var (b, tb) = SeqRig(120, rowsP, (x, y) => Rig(x, y, true));
            using (a) using (b)
            {
                var still = Roll(a, 1.0);
                var played = Roll(b, 1.0);
                yield return (Diff(still, played) > 0.01 && Math.Abs(G(b, tb, "padx") - 0.5f) < 1e-6,
                    "a motion lane on Pad X plays the pad's routes step by step and leaves the pad itself where it is");
            }
        }

        // Presets that play: the factory ones with the sequencer or the arpeggiator bring their pattern.
        {
            var cat = new FactoryPresetCatalog();
            var seqPresets = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 18 && cat.Document(p.Id)!.NamedParams!.TryGetValue("seq.on", out var on) && on > 0.5f).ToList();
            using var pe = new NotaEngine();
            int pt = pe.AddTwinTrack();
            var ms = seqPresets.First(p => p.DisplayName == "Morph Sequence");
            bool brings = cat.ApplyInPlace(pe, ms.Id, pt, -1) == ""
                          && TwinPatterns.FromState(pe.GetPluginState(pt, -1))![0] is { Length: 8 } mp && mp.Lanes[0].Target == Pi(pe, pt, "lo.morph");
            yield return (seqPresets.Count >= 8 && brings, $"≥ 8 factory presets play the sequencer or the arpeggiator ({seqPresets.Count}), and a preset brings its pattern and motion lane");
            P(pe, pt, "lo.b.o2wave", 0.4f);
            var doc = PresetService.Capture(pe, pt, -1, "Mine")!;
            int pu = pe.AddTwinTrack();
            PresetService.ApplyInPlace(doc, pe, pu, -1);
            yield return (TwinPatterns.FromState(pe.GetPluginState(pu, -1))![0].Length == 8, "a user preset keeps the patterns");
            var mcp = new Nota.Mcp.Tools.TwinTools(pe, new SyncDispatch(), new NoRefresh());
            pe.ClearUndoHistory();
            string set = mcp.SetTwinPattern(pu, "C3 E3:100:50% | . | G3:80:tie | G3:80:30%", 3).Result;
            var got = mcp.GetTwinPattern(pu, 3).Result!;
            bool oneUndo = pe.Undo() && mcp.GetTwinPattern(pu, 3).Result!.Length == 16 && !pe.CanUndo;
            string bad = mcp.SetTwinPattern(pu, "C3 | H9").Result;
            yield return (set == "pattern 3: 4 steps" && got.Rows.SequenceEqual(new[] { "C3 E3:100:50%", ".", "G3:80:tie", "G3:80:30%" }) && got.SequencerOn
                          && oneUndo && bad.StartsWith("error: row 2"),
                $"MCP set_twin_pattern / get_twin_pattern round-trip a pattern in one undo step ({set} | {bad})");
        }

        // "To clip": the pattern as a MIDI clip plays like the sequencer did.
        {
            var rowsC = new[] { "C3 G3:110:50%", ".", "E3:80:tie", "E3:80:40%", "A2:100:75%", ".", "G3:70:50%", "." };
            var (e, t) = SeqRig(120, rowsC, (x, y) => { P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.6f); });
            var (f, u) = SeqRig(120, rowsC, (x, y) => { P(x, y, "lo.a.o1level", 1f); P(x, y, "lo.a.noiselevel", 0f); P(x, y, "lo.a.ldcut", 0.6f); });
            using (e) using (f)
            {
                var live = Roll(e, 1.0);
                f.ClearUndoHistory();
                int clip = TwinPatterns.ToClip(f, u, 0, 0.0);
                bool off = G(f, u, "seq.on") < 0.5f;
                var fromClip = Roll(f, 1.0);
                double d = Math.Abs(Db(Rms(live)) - Db(Rms(fromClip)));
                yield return (clip >= 0 && off && Diff(live, fromClip) == 0,
                    $"SC-009: \"To clip\" writes the pattern as a clip that renders identically (maxdiff {Diff(live, fromClip):G3}, level {d:0.00} dB) and turns the sequencer off");
                yield return (f.Undo() && G(f, u, "seq.on") > 0.5f, "undoing \"To clip\" removes the clip and turns the sequencer back on");
            }
        }
    }

    private static double Diff(float[] x, float[] y) { double d = 0; for (int i = 0; i < Math.Min(x.Length, y.Length); i++) d = Math.Max(d, Math.Abs(x[i] - y[i])); return d; }

    // How far apart two onset lists are (the largest gap between matched onsets, in samples).
    private static int Diff2(List<int> a, List<int> b)
    {
        if (a.Count != b.Count) return int.MaxValue;
        int w = 0;
        for (int i = 0; i < a.Count; i++) w = Math.Max(w, Math.Abs(a[i] - b[i]));
        return w;
    }
}
