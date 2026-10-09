// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Keys (instrument kind 16): identity, params, state / clone, every model sounding, the
// pickup curve's bark growing with velocity, damper / pedal (the param and a keyboard's CC64),
// re-strikes reusing the ringing voice, the voice limit, the tremolo's stereo pan, every effect
// and cabinet finite, block-size null test, freeze == live, automation, a 64-voice stress run,
// the 40 factory presets and the MCP tools.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class KeysTests
{
    private const int Sr = 48000;

    private static int Pi(NotaEngine e, int t, string id)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++) if (e.PluginParamId(t, -1, i) == id) return i;
        return -1;
    }
    private static void P(NotaEngine e, int t, string id, float v) => e.PluginParamSet(t, -1, Pi(e, t, id), v);

    // Interleaved stereo, `frames` long, live notes only (no transport).
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
    private static double RmsCh(float[] st, int ch, int from, int to)
    {
        double s = 0;
        for (int i = from; i < to; i++) s += st[i * 2 + ch] * (double)st[i * 2 + ch];
        return Math.Sqrt(s / Math.Max(1, to - from));
    }
    // Energy at one frequency (Goertzel), mono sum.
    private static double At(float[] st, double hz, int from = 0)
    {
        double w = 2 * Math.PI * hz / Sr, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = from * 2; i < st.Length; i += 2) { double s0 = (st[i] + st[i + 1]) * 0.5 + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }
    private static double MaxDiff(float[] a, float[] b) { double d = 0; for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d = Math.Max(d, Math.Abs(a[i] - b[i])); return d; }

    private static (NotaEngine E, int T) Fresh(Action<NotaEngine, int>? setup = null)
    {
        var e = new NotaEngine();
        int t = e.AddKeysTrack();
        setup?.Invoke(e, t);
        Render(e, 1024);
        return (e, t);
    }

    // A clip-driven render (transport rolling), for the null and freeze tests.
    private static float[] Play(Action<NotaEngine, int> setup, NotaNote[] notes, int frames, int block)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddKeysTrack();
        setup(e, t);
        e.AddMidiClip(t, 0.0, 16.0);
        e.SetClipNotes(t, 0, notes);
        var outv = new float[frames * 2]; var tmp = new float[block * 2];
        e.Seek(0); e.Play();
        for (int done = 0; done < frames;) { int m = Math.Min(block, frames - done); e.RenderOffline(tmp, m); Array.Copy(tmp, 0, outv, done * 2, m * 2); done += m; }
        e.StopTransport();
        return outv;
    }

    public static IEnumerable<(bool, string)> Run()
    {
        // ---- identity, params, state ------------------------------------------------
        var (ke, t) = Fresh();
        using (ke)
        {
            yield return (t > 0 && ke.TrackInstrumentKind(t) == KeysModel.Kind, $"AddKeysTrack adds kind 16 (got {ke.TrackInstrumentKind(t)})");
            yield return (ke.DeviceName(t, -1) == "Nota Keys", $"instrument is Nota Keys (got '{ke.DeviceName(t, -1)}')");
            int pc = ke.PluginParamCount(t, -1);
            var ids = new HashSet<string>();
            bool names = true;
            for (int i = 0; i < pc; i++) { ids.Add(ke.PluginParamId(t, -1, i)); if (ke.PluginParamName(t, -1, i).Length == 0) names = false; }
            yield return (pc == 36 && ids.Count == pc && !ids.Contains("") && names, $"Nota Keys exposes 36 params with unique ids and names (got {pc})");
            yield return (ke.TrackInstrumentSupportsMpe(t), "Nota Keys reports MPE support");
            yield return (InstrumentView.Index(ke, t) >= 0 && !InstrumentView.IsMini(ke, t), "Nota Keys has an S / L view param and opens as L");

            P(ke, t, "dist", 0.17f); P(ke, t, "model", 1f); P(ke, t, "tremon", 1f);
            var state = ke.GetPluginState(t, -1);
            int t2 = ke.AddKeysTrack();
            ke.SetPluginState(t2, -1, state);
            yield return (Math.Abs(ke.PluginParamGet(t2, -1, Pi(ke, t2, "dist")) - 0.17f) < 1e-4 && ke.PluginParamGet(t2, -1, Pi(ke, t2, "model")) > 0.99f,
                "keys state restores on another track");
            int t3 = ke.DuplicateTrack(t);
            yield return (t3 > 0 && Math.Abs(ke.PluginParamGet(t3, -1, Pi(ke, t3, "dist")) - 0.17f) < 1e-4 && ke.TrackInstrumentKind(t3) == 16,
                "duplicating the track clones the keys patch");
            yield return (ke.SetTrackBuiltinInstrument(ke.AddInstrumentTrack(), 16), "a track's instrument can be swapped to Nota Keys");
        }

        // ---- every model sounds -----------------------------------------------------
        foreach (var (model, name) in new[] { (0f, "Tine"), (1 / 3f, "Suitcase"), (2 / 3f, "Reed"), (1f, "Clav") })
        {
            var (e, tr) = Fresh((e, tr) => P(e, tr, "model", model));
            using (e)
            {
                foreach (var n in new[] { 48, 55, 60, 64 }) e.TrackNoteOn(tr, n, 0.8f);
                var b = Render(e, Sr / 2);
                int voices = e.InstrumentVoiceCount(tr);
                yield return (b.All(float.IsFinite) && Rms(b) > 0.01 && b.Max(Math.Abs) < 1.5f && voices == 4,
                    $"Nota Keys {name}: a 4-note chord is audible, finite, not clipping, 4 voices (rms {Rms(b):F3}, peak {b.Max(Math.Abs):F2}, {voices})");
            }
        }

        // ---- velocity drives the pickup harder: louder and more bark (H2 / H1) -------
        {
            double Bark(float vel, out double rms)
            {
                var (e, tr) = Fresh((e, tr) => { P(e, tr, "preon", 0f); P(e, tr, "cab", 0f); P(e, tr, "noise", 0f); P(e, tr, "dist", 0.2f); });
                using (e)
                {
                    e.TrackNoteOn(tr, 45, vel);
                    var b = Render(e, Sr / 2);
                    rms = Rms(b, 2400);
                    return At(b, 220, 2400) / Math.Max(1e-12, At(b, 110, 2400));
                }
            }
            double soft = Bark(0.25f, out var rSoft), hard = Bark(1f, out var rHard);
            yield return (rHard > rSoft * 1.5 && hard > soft * 2, $"harder notes are louder and bark more (rms {rSoft:F3} → {rHard:F3}, H2/H1 energy {soft:G2} → {hard:G2})");
        }

        // ---- damper and pedal -------------------------------------------------------
        double TailAfterRelease(Action<NotaEngine, int> setup, Action<NotaEngine, int>? beforeOff = null, Action<NotaEngine, int>? afterOff = null)
        {
            var (e, tr) = Fresh(setup);
            using (e)
            {
                beforeOff?.Invoke(e, tr);
                e.TrackNoteOn(tr, 60, 0.8f);
                Render(e, Sr / 4);
                e.TrackNoteOff(tr, 60);
                Render(e, Sr / 4);
                afterOff?.Invoke(e, tr);
                return Rms(Render(e, Sr / 4));
            }
        }
        double damped = TailAfterRelease((e, tr) => P(e, tr, "damper", 0.9f));
        double free = TailAfterRelease((e, tr) => P(e, tr, "damper", 0.9f), (e, tr) => P(e, tr, "pedal", 1f));
        double cc64 = TailAfterRelease((e, tr) => P(e, tr, "damper", 0.9f), (e, tr) => { e.TrackNoteExpression(tr, -1, NoteExpressionDim.Sustain, 1f); Render(e, 512); });
        double lifted = TailAfterRelease((e, tr) => P(e, tr, "damper", 0.9f), (e, tr) => P(e, tr, "pedal", 1f), (e, tr) => { P(e, tr, "pedal", 0f); Render(e, Sr / 4); });
        yield return (free > damped * 20, $"the pedal (param) holds a released note (tail rms {damped:G2} damped → {free:G2} held)");
        yield return (cc64 > damped * 20, $"a keyboard's CC64 holds a released note (tail rms {damped:G2} → {cc64:G2})");
        yield return (lifted < free * 0.1, $"lifting the pedal drops the dampers (tail rms {free:G2} → {lifted:G2})");
        {
            bool threw = false;
            using var e = new NotaEngine();
            int tr = e.AddKeysTrack();
            try { e.TrackNoteExpression(tr, 60, NoteExpressionDim.Sustain, 1f); } catch (NotaEngineException) { threw = true; }
            yield return (threw, "the sustain dimension is instrument-wide only (a per-note one is refused)");
        }

        // ---- voices: re-strike reuses, the limit holds --------------------------------
        {
            var (e, tr) = Fresh();
            using (e)
            {
                e.TrackNoteOn(tr, 57, 0.8f); Render(e, 2048); e.TrackNoteOff(tr, 57); Render(e, 512);
                e.TrackNoteOn(tr, 57, 0.8f); Render(e, 2048);
                yield return (e.InstrumentVoiceCount(tr) == 1, $"re-striking a ringing key reuses its voice ({e.InstrumentVoiceCount(tr)} voice)");
            }
        }
        {
            var (e, tr) = Fresh((e, tr) => P(e, tr, "voices", 0f));
            using (e)
            {
                for (int n = 0; n < 12; n++) { e.TrackNoteOn(tr, 48 + n, 0.8f); Render(e, 256); }
                Render(e, 2048);
                var sc = new float[KeysModel.ScopeLength]; e.InstrumentScope(tr, sc);
                yield return (e.InstrumentVoiceCount(tr) == 8 && (int)sc[KeysModel.ScLimit] == 8,
                    $"the voice limit holds at 8 with 12 keys down ({e.InstrumentVoiceCount(tr)} voices, scope limit {sc[KeysModel.ScLimit]})");
            }
        }

        // ---- tremolo: stereo pans, mono pumps -------------------------------------------
        {
            var (e, tr) = Fresh((e, tr) => { P(e, tr, "tremon", 1f); P(e, tr, "tremmode", 1f); P(e, tr, "tremdepth", 1f); P(e, tr, "tremrate", 0.2f); P(e, tr, "decay", 1f); });
            using (e)
            {
                e.TrackNoteOn(tr, 60, 0.9f);
                var b = Render(e, Sr);
                // Over ~1.6 Hz the left and right gains swap; count windows where one side clearly leads.
                int lLead = 0, rLead = 0;
                for (int w = 4; w < 40; w++)
                {
                    double l = RmsCh(b, 0, w * 1200, (w + 1) * 1200), r = RmsCh(b, 1, w * 1200, (w + 1) * 1200);
                    if (l > r * 1.5) lLead++; else if (r > l * 1.5) rLead++;
                }
                yield return (lLead >= 3 && rLead >= 3, $"stereo tremolo pans between the sides (left leads {lLead}, right leads {rLead} windows)");
            }
        }

        // ---- every effect and cabinet stays finite and audible --------------------------
        foreach (var (label, set) in new (string, Action<NotaEngine, int>)[]
        {
            ("phaser", (e, tr) => { P(e, tr, "phaseron", 1f); P(e, tr, "phaserdepth", 1f); }),
            ("chorus", (e, tr) => { P(e, tr, "choruson", 1f); P(e, tr, "chorusmix", 1f); }),
            ("mono trem", (e, tr) => { P(e, tr, "tremon", 1f); P(e, tr, "tremmode", 0f); }),
            ("max drive", (e, tr) => { P(e, tr, "drive", 1f); P(e, tr, "bass", 1f); P(e, tr, "treble", 1f); }),
            ("cab off", (e, tr) => P(e, tr, "cab", 0f)),
            ("cab suitcase", (e, tr) => P(e, tr, "cab", 1 / 3f)),
            ("cab combo", (e, tr) => P(e, tr, "cab", 2 / 3f)),
            ("everything at max", (e, tr) => { for (int i = 0; i < e.PluginParamCount(tr, -1) - 1; i++) e.PluginParamSet(tr, -1, i, 1f); }),
        })
        {
            var (e, tr) = Fresh(set);
            using (e)
            {
                foreach (var n in new[] { 36, 60, 72, 96 }) e.TrackNoteOn(tr, n, 1f);
                var b = Render(e, Sr / 2);
                yield return (b.All(float.IsFinite) && Rms(b) > 0.005 && b.Max(Math.Abs) < 4f, $"Nota Keys {label}: finite and audible (rms {Rms(b):F3}, peak {b.Max(Math.Abs):F2})");
            }
        }

        // ---- determinism: block-size null test, freeze == live ------------------------------
        {
            void Setup(NotaEngine e, int tr) { P(e, tr, "age", 0.6f); P(e, tr, "tremon", 1f); P(e, tr, "tremsync", 1f); P(e, tr, "choruson", 1f); P(e, tr, "phaseron", 1f); }
            NotaNote[] notes = { new(57, 0, 1.5, 0.8f), new(64, 0.25, 1, 0.6f), new(69, 0.5, 1.5, 1f), new(57, 1.0, 0.5, 0.9f) };
            var a = Play(Setup, notes, 44100, 4096); var b = Play(Setup, notes, 44100, 128); var c = Play(Setup, notes, 44100, 777);
            yield return (Rms(a) > 0.001 && MaxDiff(a, b) < 1e-6 && MaxDiff(a, c) < 1e-6,
                $"Nota Keys null test: identical at blocks 4096/128/777 (maxdiff {Math.Max(MaxDiff(a, b), MaxDiff(a, c)):G3})");
        }
        {
            using var fe = new NotaEngine(); fe.SetBpm(120);
            int ft = fe.AddKeysTrack();
            P(fe, ft, "tremon", 1f); P(fe, ft, "tremsync", 1f); P(fe, ft, "age", 0.5f);
            fe.AddMidiClip(ft, 0.0, 4.0);
            fe.SetClipNotes(ft, 0, new[] { new NotaNote(57, 0.0, 3.0, 0.9f), new NotaNote(64, 0.5, 2.0, 0.7f) });
            const int N = 8192 * 4;
            fe.Seek(0); fe.Play(); var live = new float[N * 2]; fe.RenderOffline(live, N); fe.StopTransport();
            fe.Stop(); fe.SetLoop(false, 0, 0); fe.SetMetronome(false); fe.StopTransport();
            long total = fe.BeginFreeze(ft, 4.0);
            fe.StopTransport(); fe.Seek(0); fe.Play();
            var chunk = new float[4096 * 2];
            for (long rem = total; rem > 0;) { int m = (int)Math.Min(4096, rem); fe.RenderOffline(chunk, m); rem -= m; }
            fe.StopTransport(); fe.EndFreeze(ft);
            fe.Seek(0); fe.Play(); var frozen = new float[N * 2]; fe.RenderOffline(frozen, N); fe.StopTransport();
            double md = MaxDiff(live, frozen);
            yield return (fe.IsTrackFrozen(ft) && Rms(live) > 1e-3 && md < 1e-4, $"Nota Keys freeze == live render (maxdiff {md:G3})");
        }

        // ---- automation -----------------------------------------------------------------
        {
            using var e = new NotaEngine(); e.SetBpm(120);
            int ta = e.AddKeysTrack();
            foreach (var id in new[] { "dist", "tremdepth", "pedal" })
            {
                int fi = Pi(e, ta, id);
                e.PluginParamSet(ta, -1, fi, 0.1f);
                int lane = e.AddPluginAutomationLane(ta, -1, id);
                e.SetAutomationPoints(ta, lane, new[] { new AutomationPoint(0.0, 0.1f, 0f), new AutomationPoint(2.0, 0.9f, 0f) });
                var abuf = new float[4096 * 2];
                e.Seek(1.99); e.Play(); e.RenderOffline(abuf, 4096); e.StopTransport();
                yield return (lane >= 0 && e.PluginParamGet(ta, -1, fi) > 0.7f, $"automation drives Keys {id} ({e.PluginParamGet(ta, -1, fi):F2})");
            }
        }

        // ---- stress: 64 Clav voices (24 partials each) + every effect in real time ------------
        {
            var (e, tr) = Fresh((e, tr) =>
            {
                P(e, tr, "model", 1f); P(e, tr, "voices", 1f); P(e, tr, "decay", 1f); P(e, tr, "damper", 0f);
                P(e, tr, "tremon", 1f); P(e, tr, "phaseron", 1f); P(e, tr, "choruson", 1f);
            });
            using (e)
            {
                for (int n = 0; n < 64; n++) e.TrackNoteOn(tr, 28 + n, 0.9f);
                var blk = new float[128 * 2];
                for (int i = 0; i < 20; i++) e.RenderOffline(blk, 128, Sr);
                var sw = System.Diagnostics.Stopwatch.StartNew(); const int blocks = 400;
                for (int i = 0; i < blocks; i++) e.RenderOffline(blk, 128, Sr);
                sw.Stop();
                double budget = 128 * 1000.0 / Sr, avg = sw.Elapsed.TotalMilliseconds / blocks;
                yield return (e.InstrumentVoiceCount(tr) == 64 && avg < budget * 0.5,
                    $"Nota Keys stress: 64 Clav voices + FX avg {avg:0.000} ms per 128-frame block (budget {budget:0.00} ms, {e.InstrumentVoiceCount(tr)} voices)");
            }
        }

        // ---- factory presets -------------------------------------------------------------
        {
            var cat = new FactoryPresetCatalog();
            var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == KeysModel.Kind).ToList();
            yield return (mine.Count == 40, $"Nota Keys ships 40 factory presets (got {mine.Count})");
            using var pe = new NotaEngine();
            int pt = pe.AddKeysTrack();
            var ids = new HashSet<string>();
            for (int i = 0; i < pe.PluginParamCount(pt, -1); i++) ids.Add(pe.PluginParamId(pt, -1, i));
            var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !ids.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
            yield return (bad.Count == 0, $"every Keys preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
            int fails = mine.Count(p => cat.ApplyInPlace(pe, p.Id, pt, -1).Length != 0);
            yield return (fails == 0, $"every Keys preset applies in place ({fails} failed)");
            var quiet = new List<string>();
            foreach (var p in mine)
            {
                var b = Play((e, tr) => cat.ApplyInPlace(e, p.Id, tr, -1),
                    new[] { new NotaNote(48, 0, 1.5, 0.8f), new NotaNote(55, 0, 1.5, 0.8f), new NotaNote(64, 0, 1.5, 0.8f) }, 44100, 1024);
                double r = Rms(b); float pk = b.Max(Math.Abs);
                if (!b.All(float.IsFinite) || r < 0.005 || pk > 1.5f) quiet.Add($"{p.DisplayName} ({r:F3}/{pk:F2})");
            }
            yield return (quiet.Count == 0, $"every Keys preset sounds a chord, finite and not clipping{(quiet.Count > 0 ? " — " + string.Join(", ", quiet) : "")}");
            var folders = mine.Select(p => p.Category).Distinct().ToList();
            yield return (folders.Count == 6, $"Keys presets sit in 6 folders ({string.Join(", ", folders)})");
        }

        // ---- MCP --------------------------------------------------------------------------
        {
            using var me = new NotaEngine();
            var mcp = new Nota.Mcp.Tools.InstrumentTools(me, new SyncDispatch(), new NoRefresh());
            int mt = mcp.AddInstrumentTrack(16).Result;
            yield return (mt > 0 && me.TrackInstrumentKind(mt) == 16, "MCP add_instrument_track(16) adds a Nota Keys");
            yield return (mcp.ListInstrumentKinds().Any(k => k.Kind == 16 && k.Name == "Nota Keys"), "MCP list_instrument_kinds includes Nota Keys");
            var r0 = mcp.ReadKeys(mt).Result;
            yield return (r0 is not null && r0.Model == "Tine" && r0.VoiceLimit == 32 && r0.HarmonicsDb.Length == 8 && r0.Guide.Contains("tremrate"),
                $"read_keys reads a fresh Keys ({r0?.Summary})");
            string s = mcp.SetKeys(mt, model: "Reed", tremolo: "stereo", tremoloRate: "1/8", voices: 16, pedal: true, chorusMixPercent: 40).Result;
            var r1 = mcp.ReadKeys(mt).Result;
            yield return (!s.StartsWith("error") && r1!.Model == "Reed" && r1.VoiceLimit == 16 && s.Contains("trem stereo 1/8") && s.Contains("cab Combo") && s.Contains("pedal down"),
                $"set_keys shapes the patch in musical terms ({s})");
            yield return (mcp.SetKeys(mt, cabinet: "Marshall").Result.StartsWith("error"), "set_keys names a value it cannot use");
            yield return (mcp.ReadKeys(me.AddInstrumentTrack()).Result is null, "read_keys returns null for another instrument");
        }
    }
}
