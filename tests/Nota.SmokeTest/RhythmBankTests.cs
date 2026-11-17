// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// specs/100 (#609): the Nota Rhythm bank as an automatable param. The bank and its switch mode
// are params; a Bank lane is read at each step (not at the block start), so a switch drawn on a
// bar line plays on the downbeat at any block size; Pattern mode waits for the pattern start;
// older blobs open on their bank; a block rendered in pieces (around note events) no longer
// re-fires its first steps.

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application;
using Nota.Infrastructure;

internal static class RhythmBankTests
{
    private const int Sr = 48000;
    private const double Spb = Sr / 2.0; // 120 BPM: samples per beat

    private static int Idx(NotaEngine e, int t, string id)
    {
        for (int i = 0; i < e.PluginParamCount(t, -1); i++) if (e.PluginParamId(t, -1, i) == id) return i;
        return -1;
    }

    // A Rhythm whose bank A is empty and bank B is a kick on every step, the param on A.
    private static (NotaEngine E, int T) Rig()
    {
        var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddRhythmTrack();
        for (int b = 0; b < RhythmModel.Banks; b++) e.InstrumentAction(t, RhythmModel.A_ClearBank, b, 0);
        e.InstrumentAction(t, RhythmModel.A_SelectBank, 1, 0);
        for (int s = 0; s < RhythmModel.Steps; s++) e.InstrumentAction(t, RhythmModel.A_ToggleStep, 0 * RhythmModel.Steps + s, 0);
        e.InstrumentAction(t, RhythmModel.A_SelectBank, 0, 0);
        return (e, t);
    }

    private static int Lane(NotaEngine e, int t, params (double Beat, int Bank)[] pts)
    {
        int lane = e.AddPluginAutomationLane(t, -1, RhythmModel.BankId);
        e.SetAutomationPoints(t, lane, pts.Select(p => new AutomationPoint(p.Beat, RhythmModel.BankNorm(p.Bank))).ToArray());
        return lane;
    }

    private static float[] Roll(NotaEngine e, double beats, int block)
    {
        int frames = (int)(beats * Spb);
        var o = new float[frames * 2];
        var tmp = new float[block * 2];
        e.Seek(0); e.Play();
        for (int done = 0; done < frames;)
        {
            int m = Math.Min(block, frames - done);
            e.RenderOffline(tmp, m, Sr);
            Array.Copy(tmp, 0, o, done * 2, m * 2);
            done += m;
        }
        e.StopTransport();
        return o;
    }

    // AutomationLane::valueAt, from the points the engine returns.
    private static float Eval(AutomationPoint[] p, double beat)
    {
        if (beat <= p[0].Beat) return p[0].Value;
        if (beat >= p[^1].Beat) return p[^1].Value;
        for (int i = 1; i < p.Length; i++)
            if (beat <= p[i].Beat)
            {
                double span = p[i].Beat - p[i - 1].Beat;
                if (span <= 0) return p[i].Value;
                return (float)Nota.Application.Editing.AutomationCurve.Lerp(p[i - 1].Value, p[i].Value, (beat - p[i - 1].Beat) / span, p[i - 1].Curve);
            }
        return p[^1].Value;
    }

    private static float Peak(float[] st, double fromBeat, double toBeat)
    {
        int a = (int)(fromBeat * Spb), b = Math.Min(st.Length / 2, (int)(toBeat * Spb));
        float p = 0;
        for (int i = a; i < b; i++) p = Math.Max(p, Math.Max(Math.Abs(st[2 * i]), Math.Abs(st[2 * i + 1])));
        return p;
    }

    public static IEnumerable<(bool, string)> Run()
    {
        // The params: appended after the macros, under Perform, with their own value text.
        using (var e = new NotaEngine())
        {
            int t = e.AddRhythmTrack();
            int bi = Idx(e, t, RhythmModel.BankId), si = Idx(e, t, RhythmModel.BankSwitchId);
            yield return (e.PluginParamCount(t, -1) == 95 && bi == 93 && si == 94
                          && e.PluginParamName(t, -1, bi) == "Perform Bank" && e.PluginParamName(t, -1, si) == "Perform Bank Switch",
                $"Rhythm has Bank (93) and Bank Switch (94) under Perform ({e.PluginParamCount(t, -1)} params)");
            e.PluginParamSet(t, -1, bi, RhythmModel.BankNorm(2));
            string c = e.PluginParamText(t, -1, bi);
            e.PluginParamSet(t, -1, si, 1f);
            string pat = e.PluginParamText(t, -1, si);
            var sc = new float[RhythmModel.ScopeLength];
            e.InstrumentScope(t, sc);
            var p = RhythmModel.Parse(e.GetPluginState(t, -1), e.PluginParamCount(t, -1));
            yield return (c == "C" && pat == "Pattern" && (int)sc[RhythmModel.S_Bank] == 2 && p.CurrentBank == 2,
                $"Bank reads A–D ('{c}'), Bank Switch Step / Pattern ('{pat}'); the stopped card and the blob follow the param");
            e.InstrumentAction(t, RhythmModel.A_SelectBank, 3, 0);
            yield return (RhythmModel.BankOf(e.PluginParamGet(t, -1, bi)) == 3, "select_rhythm_bank's action sets the Bank param");
            // The lane editor's levels: Bank has four named ones, Bank Switch two, a knob none.
            string names = string.Join(",", Enumerable.Range(0, 4).Select(k => e.PluginParamStepText(t, -1, bi, k)));
            yield return (e.PluginParamSteps(t, -1, bi) == 4 && names == "A,B,C,D" && e.PluginParamSteps(t, -1, si) == 2
                          && e.PluginParamStepText(t, -1, si, 1) == "Pattern" && e.PluginParamSteps(t, -1, Idx(e, t, "swing")) == 0
                          && e.PluginParamStepText(t, -1, bi, 4) == "",
                $"Bank lanes have four named levels ({names}), Bank Switch two, Swing none");
        }

        // A lane A → B with a vertical jump exactly on bar 2 (beat 4): the downbeat already plays B,
        // the bar before stays silent — at a block size that puts a block start just before beat 4.
        float[] whole;
        {
            var (e, t) = Rig();
            using (e)
            {
                Lane(e, t, (0, 0), (4, 0), (4, 1));
                whole = Roll(e, 6, 1000); // a block starts exactly on the downbeat (96000 samples)
            }
        }
        {
            var (e, t) = Rig();
            using (e)
            {
                Lane(e, t, (0, 0), (4, 0), (4, 1));
                var odd = Roll(e, 6, 333); // a block starts 96 samples before the downbeat
                float before = Peak(odd, 0, 4), downbeat = Peak(odd, 4, 4.2);
                yield return (before < 1e-6f && downbeat > 0.01f,
                    $"a Bank lane switching on a bar line plays the new bank from the downbeat (bar 1 peak {before:E1}, downbeat {downbeat:F3})");
                float diff = 0;
                for (int i = 0; i < odd.Length; i++) diff = Math.Max(diff, Math.Abs(odd[i] - whole[i]));
                yield return (diff < 1e-6f, $"the same lane renders identically at 333- and 1000-frame blocks (max diff {diff:E1})");
            }
        }

        // A change mid-bar (beat 5 = step 5 of bar 2): Step mode plays it from there, Pattern
        // mode keeps bar 2 whole and switches on bar 3.
        foreach (bool pattern in new[] { false, true })
        {
            var (e, t) = Rig();
            using (e)
            {
                e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankSwitchId), pattern ? 1f : 0f);
                Lane(e, t, (0, 0), (5, 0), (5, 1));
                var o = Roll(e, 10, 512);
                float barStart = Peak(o, 4, 4.9), mid = Peak(o, 5, 5.2), bar3 = Peak(o, 8, 8.2);
                yield return (pattern ? barStart < 1e-6f && mid < 1e-6f && bar3 > 0.01f : barStart < 1e-6f && mid > 0.01f,
                    pattern
                        ? $"Bank Switch Pattern: a mid-bar change waits for the next pattern start (beat 5 peak {mid:E1}, bar 3 {bar3:F3})"
                        : $"Bank Switch Step: a mid-bar change plays from its step (beat 4–4.9 peak {barStart:E1}, beat 5 {mid:F3})");
            }
        }

        // Pattern mode, no lane: a bank picked while playing (the card) is queued — the scope
        // reports the old bank until the pattern start.
        {
            var (e, t) = Rig();
            using (e)
            {
                e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankSwitchId), 1f);
                var tmp = new float[512 * 2];
                e.Seek(0); e.Play();
                for (int i = 0; i < 40; i++) e.RenderOffline(tmp, 512, Sr); // ~0.43 s: inside bar 1
                e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankId), RhythmModel.BankNorm(1));
                e.RenderOffline(tmp, 512, Sr);
                var sc = new float[RhythmModel.ScopeLength];
                e.InstrumentScope(t, sc);
                int queued = (int)sc[RhythmModel.S_Bank];
                for (int i = 0; i < 160; i++) e.RenderOffline(tmp, 512, Sr); // past bar 2's downbeat
                e.InstrumentScope(t, sc);
                int after = (int)sc[RhythmModel.S_Bank];
                e.StopTransport();
                yield return (queued == 0 && after == 1, $"Pattern mode queues a picked bank until the pattern start (playing {queued} → {after})");
            }
        }

        // A bank queued in Pattern mode survives the copy a sample load swaps in mid-pattern: it
        // still waits for the pattern start instead of playing at the copy's first step.
        {
            string wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-609-" + Guid.NewGuid().ToString("N") + ".wav");
            Nota.SmokeTest.WavWriter.WriteSine(wav, seconds: 0.1, freq: 440.0, sampleRate: 44100);
            var (e, t) = Rig();
            try
            {
                using (e)
                {
                    e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankSwitchId), 1f);
                    var tmp = new float[512 * 2];
                    e.Seek(0); e.Play();
                    for (int i = 0; i < 40; i++) e.RenderOffline(tmp, 512, Sr);   // inside bar 1
                    e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankId), RhythmModel.BankNorm(1));
                    for (int i = 0; i < 4; i++) e.RenderOffline(tmp, 512, Sr);
                    bool loaded = e.SetRhythmVoiceSample(t, 7, wav);               // swaps in a copy
                    var sc = new float[RhythmModel.ScopeLength];
                    for (int i = 0; i < 20; i++) e.RenderOffline(tmp, 512, Sr);   // still bar 1
                    e.InstrumentScope(t, sc);
                    int queued = (int)sc[RhythmModel.S_Bank];
                    for (int i = 0; i < 140; i++) e.RenderOffline(tmp, 512, Sr);  // past bar 2's downbeat
                    e.InstrumentScope(t, sc);
                    int after = (int)sc[RhythmModel.S_Bank];
                    e.StopTransport();
                    yield return (loaded && queued == 0 && after == 1,
                        $"a bank queued in Pattern mode stays queued across a sample load mid-pattern (playing {queued} → {after})");
                }
            }
            finally { try { System.IO.File.Delete(wav); } catch { } }
        }

        // A forward seek inside the pattern cycle (beat 0.5 → 2.5, where the lane holds B): Pattern
        // mode takes the destination's bank at the first step after it, not at the next bar.
        {
            var (e, t) = Rig();
            using (e)
            {
                e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankSwitchId), 1f);
                Lane(e, t, (0, 0), (2.5, 0), (2.5, 1));
                var tmp = new float[512 * 2];
                e.Seek(0); e.Play();
                for (int i = 0; i < 24; i++) e.RenderOffline(tmp, 512, Sr);   // ≈ beat 0.5
                e.Seek(2.5);
                for (int i = 0; i < 12; i++) e.RenderOffline(tmp, 512, Sr);   // ≈ beat 3, before bar 2
                var sc = new float[RhythmModel.ScopeLength];
                e.InstrumentScope(t, sc);
                int playing = (int)sc[RhythmModel.S_Bank];
                e.StopTransport();
                yield return (playing == 1, $"Pattern mode: a forward seek inside the pattern plays the destination's bank at once (playing {playing})");
            }
        }

        // A bank click while automation records (a Begin/Set/End write with no poll in between)
        // writes a step at the playhead — the bank before, then the new one — and plays back.
        {
            var (e, t) = Rig();
            using (e)
            {
                int bi = Idx(e, t, RhythmModel.BankId);
                var tmp = new float[512 * 2];
                e.SetAutomationRecord(true);
                e.Seek(0); e.Play();
                for (int i = 0; i < 47; i++) e.RenderOffline(tmp, 512, Sr);   // ≈ beat 1
                e.BeginAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, RhythmModel.BankId);
                e.PluginParamSet(t, -1, bi, RhythmModel.BankNorm(1));
                e.EndAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, RhythmModel.BankId);
                e.SetAutomationRecord(false);
                e.StopTransport();
                int lane = -1;
                for (int i = 0; i < e.AutomationLaneCount(t); i++) if (e.AutomationLaneParamId(t, i) == RhythmModel.BankId) lane = i;
                var pts = lane >= 0 ? e.GetAutomationPoints(t, lane) : Array.Empty<AutomationPoint>();
                bool step = pts.Length == 2 && Math.Abs(pts[0].Beat - pts[1].Beat) < 1e-9 && pts[0].Beat > 0.5 && pts[0].Beat < 1.5
                            && RhythmModel.BankOf(pts[0].Value) == 0 && RhythmModel.BankOf(pts[1].Value) == 1;
                // Played back: A before the click, B after it.
                e.PluginParamSet(t, -1, bi, 0f);
                var sc = new float[RhythmModel.ScopeLength];
                e.Seek(0); e.Play();
                for (int i = 0; i < 20; i++) e.RenderOffline(tmp, 512, Sr);   // ≈ beat 0.4
                e.InstrumentScope(t, sc); int early = (int)sc[RhythmModel.S_Bank];
                for (int i = 0; i < 60; i++) e.RenderOffline(tmp, 512, Sr);   // ≈ beat 1.7
                e.InstrumentScope(t, sc); int late = (int)sc[RhythmModel.S_Bank];
                e.StopTransport();
                yield return (step && early == 0 && late == 1,
                    $"a bank click while recording writes a step at the playhead and plays it back ({pts.Length} points{(pts.Length > 0 ? $" at beat {pts[0].Beat:0.##}" : "")}; bank {early} → {late})");
            }
        }

        // A click recorded inside a curved segment leaves the lane before it as it was: the piece up
        // to the click keeps its shape (its curve is cut to the piece), only the step is new.
        {
            var (e, t) = Rig();
            using (e)
            {
                int bi = Idx(e, t, RhythmModel.BankId);
                int lane = e.AddPluginAutomationLane(t, -1, RhythmModel.BankId);
                var orig = new[] { new AutomationPoint(0, 0f, 0.8f), new AutomationPoint(8, 1f) };
                e.SetAutomationPoints(t, lane, orig);
                var tmp = new float[512 * 2];
                e.SetAutomationRecord(true);
                e.Seek(0); e.Play();
                for (int i = 0; i < 70; i++) e.RenderOffline(tmp, 512, Sr);   // ≈ beat 1.5
                e.BeginAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, RhythmModel.BankId);
                e.PluginParamSet(t, -1, bi, RhythmModel.BankNorm(3));
                e.EndAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, RhythmModel.BankId);
                e.SetAutomationRecord(false);
                e.StopTransport();
                var pts = e.GetAutomationPoints(t, lane);
                double click = pts.Length == 4 ? pts[1].Beat : -1;
                float worst = 0;
                for (double b = 0; b < click - 1e-6; b += 0.01) worst = Math.Max(worst, Math.Abs(Eval(pts, b) - Eval(orig, b)));
                yield return (pts.Length == 4 && click > 1 && worst < 1e-4f && RhythmModel.BankOf(pts[2].Value) == 3,
                    $"a click recorded inside a curved lane leaves the lane before it unchanged ({pts.Length} points, click at beat {click:0.##}, max change {worst:E1})");
            }
        }

        // An RTH3 blob (before the bank params) opens on its saved bank with Bank Switch Step.
        {
            using var e = new NotaEngine();
            int t = e.AddRhythmTrack();
            e.InstrumentAction(t, RhythmModel.A_SelectBank, 2, 0);
            e.PluginParamSet(t, -1, Idx(e, t, RhythmModel.BankSwitchId), 1f);
            var v4 = e.GetPluginState(t, -1);
            var v3 = new List<byte>();
            v3.AddRange(BitConverter.GetBytes(0x33485452u));
            v3.AddRange(v4.Skip(4).Take(RhythmModel.ParamsV3 * 4));
            v3.AddRange(v4.Skip(4 + 95 * 4));
            using var o = new NotaEngine();
            int to = o.AddRhythmTrack();
            o.SetPluginState(to, -1, v3.ToArray());
            int bank = RhythmModel.BankOf(o.PluginParamGet(to, -1, Idx(o, to, RhythmModel.BankId)));
            float sw = o.PluginParamGet(to, -1, Idx(o, to, RhythmModel.BankSwitchId));
            var parsed = RhythmModel.Parse(v3.ToArray(), 95);
            yield return (BitConverter.ToUInt32(v4, 0) == 0x34485452u && bank == 2 && sw == 0f && parsed.CurrentBank == 2,
                $"an RTH3 blob (older projects) opens on its saved bank C with Bank Switch Step (bank {bank}, switch {sw})");
        }

        // A block rendered in pieces — a MIDI note mid-block splits it — plays the pattern exactly
        // as the whole block does (the second piece used to restart the clock and re-fire steps).
        {
            float[] Run(bool note)
            {
                using var e = new NotaEngine();
                e.SetBpm(120); e.SetTimeSignature(4, 4);
                int t = e.AddRhythmTrack();   // the default four-on-the-floor + off-beat hats
                if (note)
                {
                    e.AddMidiClip(t, 0.0, 8.0);
                    // An unmapped pitch: it plays no voice, it only splits the blocks it falls in.
                    e.SetClipNotes(t, 0, Enumerable.Range(0, 30).Select(i => new NotaNote(60, 0.1 + i * 0.27, 0.05, 1.0f)).ToArray());
                }
                return Roll(e, 8, 512);
            }
            var plain = Run(false);
            var split = Run(true);
            float d = 0;
            for (int i = 0; i < plain.Length; i++) d = Math.Max(d, Math.Abs(plain[i] - split[i]));
            yield return (d < 1e-6f, $"note events splitting the blocks don't re-fire the Rhythm's steps (max diff {d:E1})");
        }
    }
}
