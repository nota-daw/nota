// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Recording a Session performance into the Arrangement (#971, specs/109): while an Arrangement take
// runs, every Session clip that plays is written onto its track's Arrangement lane where it played,
// replacing only what was there over that span; an armed instrument track's played notes join the
// written clip; scene tempos land on the Tempo lane; the pass is one undo step and ⌘. drops it.
// Device-free: offline render at 48 kHz, 120 BPM, launch quantize one bar.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SessionToArrangementTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0; // frames per beat at 120 BPM
    private const int Chunk = 256;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in SelfTest()) yield return r;
        foreach (var r in TwoTracks()) yield return r;
        foreach (var r in ReplaceOnlyPlayed()) yield return r;
        foreach (var r in MidPlayAndScene()) yield return r;
        foreach (var r in AudioSlotAndRender()) yield return r;
        foreach (var r in WarpedAudioSlot()) yield return r;
        foreach (var r in ArmedMerge()) yield return r;
        foreach (var r in UndoRedo()) yield return r;
        foreach (var r in Retry()) yield return r;
        foreach (var r in NothingArmed()) yield return r;
        foreach (var r in SceneTempo()) yield return r;
        foreach (var r in LoopPass()) yield return r;
        foreach (var r in StopEndsSession()) yield return r;
        foreach (var r in ShortAndMuted()) yield return r;
        foreach (var r in OneShotEnds()) yield return r;
        foreach (var r in HeldNoteAtRecord()) yield return r;
        foreach (var r in DisarmKeepsTake()) yield return r;
        foreach (var r in TakeControllers()) yield return r;
        foreach (var r in TransportStopWrites()) yield return r;
        foreach (var r in FullUndoHistory(0)) yield return r;
        foreach (var r in FullUndoHistory(140)) yield return r;
    }

    // A clock that renders to absolute beats from where the transport was started.
    private sealed class Clock
    {
        private readonly NotaEngine _e;
        private long _frames;
        public Clock(NotaEngine e) { _e = e; }
        public void To(double beat)
        {
            var b = new float[Chunk * 2];
            long target = (long)Math.Round(beat * Spb);
            while (_frames + Chunk <= target) { _e.RenderOffline(b, Chunk, Sr); _e.Poll(); _frames += Chunk; }
        }
        public double Beat => _frames / Spb;
    }

    private static NotaEngine Project(out int a, out int b, out int c)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        e.SetLaunchQuant(4.0);
        a = e.AddInstrumentTrack(); e.SetTrackBuiltinInstrument(a, 0);
        b = e.AddInstrumentTrack(); e.SetTrackBuiltinInstrument(b, 0);
        c = e.AddInstrumentTrack(); e.SetTrackBuiltinInstrument(c, 0);
        while (e.SceneCount < 3) e.AddScene();
        // a: a 2-bar slot, notes on beats 0 and 4. b: two 1-bar slots.
        e.AddSessionMidiClip(a, 0, 8.0);
        e.SetSessionNotes(a, 0, new[] { new NotaNote(48, 0.0, 1.0, 0.8f), new NotaNote(50, 4.0, 1.0, 0.8f) });
        e.SetSessionClipName(a, 0, "Drums");
        e.AddSessionMidiClip(b, 0, 4.0);
        e.SetSessionNotes(b, 0, new[] { new NotaNote(60, 0.0, 0.5, 0.8f) });
        e.AddSessionMidiClip(b, 1, 4.0);
        e.SetSessionNotes(b, 1, new[] { new NotaNote(64, 2.0, 0.5, 0.8f) });
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        return e;
    }

    private static List<NotaClipInfo> Clips(NotaEngine e, int t)
    {
        var l = new List<NotaClipInfo>();
        for (int k = 0; e.TryGetClipInfo(t, k, out var ci); k++) l.Add(ci);
        return l;
    }

    private static bool Near(double x, double y, double tol = 1e-6) => Math.Abs(x - y) <= tol;

    private static IEnumerable<(bool, string)> SelfTest()
    {
        using var e = new NotaEngine();
        int r = e.SessionPassSelfTest();
        yield return (r == 0, $"session pass self-test (failed check {r})");
    }

    // US1: launches land where they played, on grid beats, the slot repeated over the span.
    private static IEnumerable<(bool, string)> TwoTracks()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetTrackArmed(c, true); // an ordinary take on another track
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(a, 0);  // → beat 4
        clk.To(13);
        // While the take runs the Arrangement draws what has played so far: one open span on a.
        var live = new NotaPassSpan[8];
        int nLive = e.SessionPassSpans(live);
        bool liveOk = nLive == 1 && live[0].TrackId == a && live[0].Slot == 0 && Near(live[0].StartBeat, 4)
            && live[0].Open == 1 && Near(live[0].EndBeat, clk.Beat, 0.05);
        e.LaunchSlot(b, 0);  // → beat 16
        clk.To(29);
        e.StopSlot(b);       // → beat 32
        clk.To(48);
        e.SetRecording(false);
        e.StopTransport();
        yield return (liveOk, $"mid-take, the playing slot is reported for drawing ({nLive}: {(nLive > 0 ? $"{live[0].StartBeat}..{live[0].EndBeat:0.##} open {live[0].Open}" : "")})");
        yield return (e.SessionPassSpans(live) == 0, "after the take nothing is reported");
        var ca = Clips(e, a).Where(x => x.IsMidi).ToList();
        var cb = Clips(e, b).Where(x => x.IsMidi).ToList();
        yield return (ca.Count == 1 && Near(ca[0].StartBeat, 4) && Near(ca[0].LengthBeats, 44),
            $"the drum slot is written from beat 4 to 48 ({string.Join(",", ca.Select(x => $"{x.StartBeat}+{x.LengthBeats}"))})");
        var na = ca.Count == 1 ? e.GetClipNotes(a, 0) : Array.Empty<NotaNote>();
        yield return (na.Length == 11 && Near(na[0].StartBeat, 0) && Near(na[1].StartBeat, 4) && na[1].Pitch == 50,
            $"its notes repeat every 2 bars over the span ({na.Length} notes)");
        yield return (ca.Count == 1 && e.GetClipName(a, 0) == "Drums", "the written clip carries the slot's name");
        yield return (cb.Count == 1 && Near(cb[0].StartBeat, 16) && Near(cb[0].LengthBeats, 16) && e.GetClipNotes(b, 0).Length == 4,
            $"the bass slot covers beats 16-32, stopped on the grid ({string.Join(",", cb.Select(x => $"{x.StartBeat}+{x.LengthBeats}"))})");
    }

    // US2: only the played span replaces the track's own clip; other tracks are untouched.
    private static IEnumerable<(bool, string)> ReplaceOnlyPlayed()
    {
        using var e = Project(out int a, out int b, out int c);
        int ma = e.AddMidiClip(a, 0, 32);
        e.SetClipNotes(a, ma, Enumerable.Range(0, 32).Select(i => new NotaNote(72, i, 0.5, 0.7f)).ToArray());
        int mc = e.AddMidiClip(c, 0, 32);
        e.SetClipNotes(c, mc, new[] { new NotaNote(40, 1.0, 1.0, 0.7f) });
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(5);
        e.LaunchSlot(a, 0); // → 8
        clk.To(13);
        e.StopSlot(a);      // → 16
        clk.To(20);
        e.SetRecording(false);
        e.StopTransport();
        var ca = Clips(e, a).Where(x => x.IsMidi).OrderBy(x => x.StartBeat).ToList();
        yield return (ca.Count == 3 && Near(ca[0].StartBeat, 0) && Near(ca[0].LengthBeats, 8) && Near(ca[1].StartBeat, 8)
            && Near(ca[1].LengthBeats, 8) && Near(ca[2].StartBeat, 16) && Near(ca[2].LengthBeats, 16),
            $"the clip is split around the played span ({string.Join(",", ca.Select(x => $"{x.StartBeat}+{x.LengthBeats}"))})");
        int mid = -1;
        for (int k = 0; e.TryGetClipInfo(a, k, out var ci); k++) if (ci.IsMidi && Near(ci.StartBeat, 8)) mid = k;
        var mn = mid >= 0 ? e.GetClipNotes(a, mid) : Array.Empty<NotaNote>();
        yield return (mn.Length == 2 && mn.All(n => n.Pitch is 48 or 50), $"the span holds the slot's notes only ({mn.Length})");
        var cc = Clips(e, c).Where(x => x.IsMidi).ToList();
        yield return (cc.Count == 1 && Near(cc[0].StartBeat, 0) && Near(cc[0].LengthBeats, 32) && e.GetClipNotes(c, 0).Length == 1,
            "a track that played no Session clip keeps its clip");
        var cbn = Clips(e, b);
        yield return (cbn.Count == 0, "a track with nothing played and nothing armed gets nothing");
    }

    // US1 s6/s7: a slot already playing when Record starts is written from there with its offset;
    // a scene launch writes every track it starts on the same beat.
    private static IEnumerable<(bool, string)> MidPlayAndScene()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetTrackArmed(c, true);
        e.Play();
        var clk = new Clock(e);
        e.LaunchSlot(a, 0); // plays from beat 0 (transport just started)
        clk.To(10);         // Record at ≈ beat 10: the slot is at position ≈ 2
        double recAt = clk.Beat;
        e.SetRecording(true);
        clk.To(13);
        e.LaunchScene(1);   // → beat 16: a stops (empty slot, stop button), b plays slot 1
        clk.To(24);
        e.SetRecording(false);
        e.StopTransport();
        var ca = Clips(e, a).Where(x => x.IsMidi).ToList();
        bool okA = ca.Count == 1 && Near(ca[0].StartBeat, recAt, 0.02) && Near(ca[0].StartBeat + ca[0].LengthBeats, 16);
        yield return (okA, $"a slot playing at Record is written from the Record beat to the scene ({string.Join(",", ca.Select(x => $"{x.StartBeat}+{x.LengthBeats}"))})");
        var na = okA ? e.GetClipNotes(a, 0) : Array.Empty<NotaNote>();
        // position ≈2 at the start: the next notes are at slot beats 4 (→ 12) and 8=0 (→ 14+...)
        yield return (na.Length >= 1 && na[0].Pitch == 50 && Near(ca[0].StartBeat + na[0].StartBeat, 12, 0.01),
            $"its content starts at the slot position it had reached ({(na.Length > 0 ? $"{na[0].Pitch}@{ca[0].StartBeat + na[0].StartBeat}" : "none")})");
        var cb = Clips(e, b).Where(x => x.IsMidi).ToList();
        yield return (cb.Count == 1 && Near(cb[0].StartBeat, 16) && Near(cb[0].LengthBeats, 8),
            $"the scene's clip starts on the scene's beat ({string.Join(",", cb.Select(x => $"{x.StartBeat}+{x.LengthBeats}"))})");
    }

    // US1 s9 + SC-001: an audio slot is one clip per repetition; the Arrangement then renders as
    // the performance did.
    private static IEnumerable<(bool, string)> AudioSlotAndRender() => AudioSlotAndRender(false);

    // The same with a warped slot: each written piece needs its stretch cache, or it plays silence
    // and draws no waveform until a background build catches up.
    private static IEnumerable<(bool, string)> WarpedAudioSlot() => AudioSlotAndRender(true);

    private static IEnumerable<(bool, string)> AudioSlotAndRender(bool warped)
    {
        string tag = warped ? " (warped slot)" : "";
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        e.SetLaunchQuant(4.0);
        int t = e.AddAudioTrack();
        int arm = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(arm, 0);
        e.SetTrackArmed(arm, true);
        // A 1-bar decaying tone at 200 beats, turned into slot 0, then removed from the timeline.
        int frames = (int)(4 * Spb);
        var pcm = new float[frames * 2];
        for (int i = 0; i < frames; i++) { float v = (float)(0.5 * Math.Sin(i * 2 * Math.PI * 220 / Sr) * Math.Exp(-i / Sr)); pcm[2 * i] = v; pcm[2 * i + 1] = v; }
        int ci = e.PasteAudioFrames(t, pcm, frames, 200.0, "Tone");
        if (warped && ci >= 0) { e.SetClipWarp(t, ci, true, 0); e.SetClipWarpLength(t, ci, 4.0); }
        bool made = ci >= 0 && e.ArrangementAudioClipToSession(t, ci, 0);
        if (made) e.DeleteClip(t, ci);
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(t, 0); // → 4
        var live = new List<float>();
        var buf = new float[Chunk * 2];
        clk.To(4);
        for (int i = 0; i < (int)(12 * Spb) / Chunk; i++) { e.RenderOffline(buf, Chunk, Sr); e.Poll(); live.AddRange(buf); }
        clk = null!;
        e.StopSlot(t);
        e.SetRecording(false);
        e.StopTransport();
        var cl = Clips(e, t).Where(x => !x.IsMidi).OrderBy(x => x.StartBeat).ToList();
        // One clip per repetition of the slot (its length is the clip's), back to back from beat 4.
        bool backToBack = cl.Count >= 3 && Near(cl[0].StartBeat, 4);
        for (int k = 1; k < cl.Count; k++) backToBack &= Near(cl[k].StartBeat, cl[k - 1].StartBeat + cl[k - 1].LengthBeats, 1e-3);
        yield return (made && backToBack && Near(cl[^1].StartBeat + cl[^1].LengthBeats, 16, 0.05),
            $"an audio slot is written as one clip per repetition{tag} ({string.Join(",", cl.Select(x => $"{x.StartBeat:0.###}+{x.LengthBeats:0.###}"))})");
        // Back to the timeline, from beat 4: the same 12 beats.
        e.BackToArrangement();
        e.Seek(4);
        e.Play();
        var again = new List<float>();
        for (int i = 0; i < (int)(12 * Spb) / Chunk; i++) { e.RenderOffline(buf, Chunk, Sr); e.Poll(); again.AddRange(buf); }
        e.StopTransport();
        double Rms(List<float> s, int bar) { double acc = 0; int n = (int)(4 * Spb) * 2; int o = bar * n; for (int i = o; i < Math.Min(o + n, s.Count); i++) acc += s[i] * s[i]; return Math.Sqrt(acc / n); }
        bool same = true;
        var detail = new List<string>();
        for (int bar = 0; bar < 3; bar++)
        {
            double l = Rms(live, bar), r = Rms(again, bar);
            detail.Add($"{l:0.0000}/{r:0.0000}");
            if (l < 1e-4 || Math.Abs(20 * Math.Log10(r / l)) > 0.5) same = false;
        }
        yield return (same, $"the Arrangement renders as the performance did, bar by bar{tag} ({string.Join(" ", detail)})");
    }

    // US3 s2: an armed instrument track playing a slot keeps the played notes inside the written clip.
    private static IEnumerable<(bool, string)> ArmedMerge()
    {
        using var e = Project(out int a, out int b, out int c);
        int pre = e.AddMidiClip(a, 0, 32);
        e.SetClipNotes(a, pre, new[] { new NotaNote(80, 9.0, 0.5, 0.7f) }); // before the pass, inside the span
        e.SetTrackArmed(a, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(2);
        e.NoteOn(70, 0.8f); clk.To(2.5); e.NoteOff(70);  // before the launch: stays in the take
        clk.To(5);
        e.LaunchSlot(a, 0); // → 8
        clk.To(10);
        e.NoteOn(72, 0.8f); clk.To(10.5); e.NoteOff(72); // over the span: joins the written clip
        clk.To(13);
        e.StopSlot(a);      // → 16
        clk.To(18);
        e.SetRecording(false);
        e.StopTransport();
        int span = -1;
        var all = new List<(double at, int pitch)>();
        for (int k = 0; e.TryGetClipInfo(a, k, out var ci); k++)
        {
            if (!ci.IsMidi) continue;
            if (Near(ci.StartBeat, 8)) span = k;
            foreach (var n in e.GetClipNotes(a, k)) all.Add((ci.StartBeat + n.StartBeat, n.Pitch));
        }
        var sn = span >= 0 ? e.GetClipNotes(a, span) : Array.Empty<NotaNote>();
        yield return (sn.Any(n => n.Pitch == 72) && sn.Count(n => n.Pitch is 48 or 50) == 2,
            $"the played note joins the written clip ({string.Join(",", sn.Select(n => n.Pitch))})");
        yield return (all.Any(x => x.pitch == 70 && x.at < 8), "a note played before the launch stays in the take");
        yield return (!all.Any(x => x.pitch == 80), "a note the track had inside the span is replaced");
    }

    // US3 s3 + SC-004: the whole pass is one undo step, and redo brings it back.
    private static IEnumerable<(bool, string)> UndoRedo()
    {
        using var e = Project(out int a, out int b, out int c);
        int ma = e.AddMidiClip(a, 0, 32);
        e.SetClipNotes(a, ma, new[] { new NotaNote(72, 0, 0.5, 0.7f) });
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(2);
        e.NoteOn(66, 0.8f); clk.To(3); e.NoteOff(66);
        e.LaunchSlot(a, 0); // → 4
        e.LaunchSlot(b, 0); // → 4
        clk.To(14);
        e.SetRecording(false);
        e.StopTransport();
        int afterA = Clips(e, a).Count, afterB = Clips(e, b).Count, afterC = Clips(e, c).Count;
        e.Undo();
        var ua = Clips(e, a);
        bool restored = ua.Count == 1 && Near(ua[0].StartBeat, 0) && Near(ua[0].LengthBeats, 32) && Clips(e, b).Count == 0 && Clips(e, c).Count == 0;
        yield return (afterB == 1 && afterC == 1 && restored,
            $"one Undo removes the written clips and the take together (after {afterA}/{afterB}/{afterC}, undone {ua.Count}/{Clips(e, b).Count}/{Clips(e, c).Count})");
        e.Redo();
        yield return (Clips(e, a).Count == afterA && Clips(e, b).Count == afterB && Clips(e, c).Count == afterC, "Redo brings the whole pass back");
    }

    // FR-011: ⌘. drops what the pass heard; the retried pass writes its own launches only.
    private static IEnumerable<(bool, string)> Retry()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(b, 0); // → 4, then discarded
        clk.To(6);
        var r = e.RetryTake(); // the transport goes back to 0
        e.StopSlot(b);
        var clk2 = new Clock(e);
        clk2.To(1);
        e.LaunchSlot(a, 0); // → 4
        clk2.To(12);
        e.SetRecording(false);
        e.StopTransport();
        yield return (r == RetryTakeResult.Arrangement, $"the take was retried ({r})");
        var ca = Clips(e, a).Where(x => x.IsMidi).ToList();
        yield return (Clips(e, b).Count == 0 && ca.Count == 1 && Near(ca[0].StartBeat, 4),
            $"only the retried pass is written (b {Clips(e, b).Count}, a {string.Join(",", ca.Select(x => x.StartBeat))})");
    }

    // FR-015: nothing armed — a performance-only pass while the Session plays; else the old answer.
    private static IEnumerable<(bool, string)> NothingArmed()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetRecording(true);
        yield return (!e.IsRecording && e.RecordStartStatus == 1, $"silent Session, nothing armed: status 1 as before ({e.RecordStartStatus})");
        e.SetRecording(false);
        e.Play();
        var clk = new Clock(e);
        e.LaunchSlot(a, 0); // plays from 0
        clk.To(6);
        double recAt = clk.Beat;
        e.SetRecording(true);
        yield return (e.IsRecording && e.RecordStartStatus == 3, $"a Session clip plays: a performance-only pass (status {e.RecordStartStatus})");
        clk.To(13);
        e.StopSlot(a); // → 16
        clk.To(18);
        e.SetRecording(false);
        e.StopTransport();
        var ca = Clips(e, a).Where(x => x.IsMidi).ToList();
        yield return (ca.Count == 1 && Near(ca[0].StartBeat, recAt, 0.02) && Near(ca[0].StartBeat + ca[0].LengthBeats, 16),
            $"the performance is written ({string.Join(",", ca.Select(x => $"{x.StartBeat:0.###}+{x.LengthBeats:0.###}"))})");
        yield return (Clips(e, b).Count == 0 && Clips(e, c).Count == 0, "no input take anywhere");
    }

    // US5: a scene tempo lands on a new Tempo lane; with a lane already there nothing is written.
    private static IEnumerable<(bool, string)> SceneTempo()
    {
        {
            using var e = Project(out int a, out int b, out int c);
            e.SetSceneProps(1, new NotaSceneProps { Color = -1, Tempo = 100 });
            e.SetTrackArmed(c, true);
            var clk = new Clock(e);
            e.SetRecording(true);
            clk.To(13);
            e.LaunchScene(1); // → 16, tempo 100
            clk.To(20);
            e.SetRecording(false);
            e.StopTransport();
            bool auto = e.IsTempoAutomated;
            yield return (auto && Math.Abs(e.TempoAt(8) - 120) < 1e-3 && Math.Abs(e.TempoAt(16.5) - 100) < 1e-3,
                $"the scene tempo is written to the Tempo lane at its beat ({auto}, {e.TempoAt(8):0.##} → {e.TempoAt(16.5):0.##})");
        }
        {
            using var e = Project(out int a, out int b, out int c);
            int lane = e.AddAutomationLane(e.MasterTrackId, AutomationTarget.Tempo, -1, -1);
            e.SetAutomationPoints(e.MasterTrackId, lane, new[] { new AutomationPoint(0, 120, 0), new AutomationPoint(64, 120, 0) });
            e.SetSceneProps(1, new NotaSceneProps { Color = -1, Tempo = 100 });
            e.SetTrackArmed(c, true);
            var clk = new Clock(e);
            e.SetRecording(true);
            clk.To(13);
            e.LaunchScene(1);
            clk.To(20);
            e.SetRecording(false);
            e.StopTransport();
            var pts = e.GetAutomationPoints(e.MasterTrackId, lane);
            yield return (pts.Length == 2 && Math.Abs(e.TempoAt(17) - 120) < 1e-3, $"an existing Tempo lane is left alone ({pts.Length} points)");
        }
    }

    // FR-012: with the loop on, the last pass over the loop wins.
    private static IEnumerable<(bool, string)> LoopPass()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetLoop(true, 0, 16);
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(b, 0); // → 4, plays through the seam
        clk.To(18);         // second pass (beats 0..2 of it)
        e.LaunchSlot(b, 1); // → 4 of the second pass (timeline frames 20 beats in)
        clk.To(28);         // second pass at 12
        e.SetRecording(false);
        e.StopTransport();
        var cb = Clips(e, b).Where(x => x.IsMidi).OrderBy(x => x.StartBeat).ToList();
        // Second pass: slot 0 carried over the seam (0-4), slot 1 from 4 to where Record stopped
        // (12). The first pass's slot 0 stays only where the second pass did not play (12-16).
        bool ok = cb.Count == 3 && Near(cb[0].StartBeat, 0) && Near(cb[0].LengthBeats, 4) && Near(cb[1].StartBeat, 4)
            && Near(cb[1].LengthBeats, 8, 0.02) && Near(cb[2].StartBeat + cb[2].LengthBeats, 16);
        string first = cb.Count > 1 ? string.Join(",", e.GetClipNotes(b, 1).Select(n => n.Pitch)) : "";
        yield return (ok && first.Contains("64") && !first.Contains("60"),
            $"the second loop pass replaces the first where it played ({string.Join(",", cb.Select(x => $"{x.StartBeat:0.###}+{x.LengthBeats:0.###}"))}; middle {first})");
    }

    // US4: Stop stops the Session (#139), so the tracks play their new Arrangement at once.
    private static IEnumerable<(bool, string)> StopEndsSession()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(a, 0);
        clk.To(10);
        e.SetRecording(false);
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        e.Poll();
        yield return (e.SessionPlayingSlot(a) < 0 && Clips(e, a).Count == 1, "after Stop the track plays its Arrangement, which holds the performance");
    }

    // Edge cases: an unquantized half-beat play is written at its length; a muted track's slot is written.
    private static IEnumerable<(bool, string)> ShortAndMuted()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetLaunchQuant(0.0);
        e.SetTrackMute(b, true);
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(2);
        double on = clk.Beat;
        e.LaunchSlot(a, 0);
        e.LaunchSlot(b, 0);
        clk.To(2.5);
        double off = clk.Beat;
        e.StopSlot(a);
        clk.To(4);
        e.SetRecording(false);
        e.StopTransport();
        var ca = Clips(e, a).Where(x => x.IsMidi).ToList();
        yield return (ca.Count == 1 && Near(ca[0].StartBeat, on, 0.02) && Near(ca[0].LengthBeats, off - on, 0.02),
            $"a half-beat play is written at its length ({string.Join(",", ca.Select(x => $"{x.StartBeat:0.###}+{x.LengthBeats:0.###}"))})");
        yield return (Clips(e, b).Count(x => x.IsMidi) == 1, "a muted track's slot is written too");
    }

    // Review (#972): a one-shot that runs out ends its span there; the Arrangement after it stays.
    private static IEnumerable<(bool, string)> OneShotEnds()
    {
        using var e = Project(out int a, out int b, out int c);
        int ma = e.AddMidiClip(a, 0, 32);
        e.SetClipNotes(a, ma, Enumerable.Range(0, 32).Select(i => new NotaNote(72, i, 0.5, 0.7f)).ToArray());
        e.TryGetSessionClipProps(a, 0, out var p);
        p.Loop = 0;
        e.SetSessionClipProps(a, 0, p);
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(a, 0); // → 4, an 8-beat one-shot: ends at 12
        clk.To(24);
        e.SetRecording(false);
        e.StopTransport();
        var ca = Clips(e, a).Where(x => x.IsMidi).OrderBy(x => x.StartBeat).ToList();
        var written = ca.FirstOrDefault(x => Near(x.StartBeat, 4));
        bool keptAfter = ca.Any(x => x.StartBeat >= 12 - 0.02 && Near(x.StartBeat + x.LengthBeats, 32));
        yield return (Near(written.LengthBeats, 8, 0.02) && keptAfter,
            $"a one-shot is written where it played (4-12) and the clip after it stays ({string.Join(",", ca.Select(x => $"{x.StartBeat:0.###}+{x.LengthBeats:0.###}"))})");
    }

    // Review (#972): a slot note still sounding when Record is pressed keeps its remaining part.
    private static IEnumerable<(bool, string)> HeldNoteAtRecord()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetSessionNotes(a, 0, new[] { new NotaNote(55, 0.0, 6.0, 0.8f) });
        e.Play();
        var clk = new Clock(e);
        e.LaunchSlot(a, 0); // plays from 0
        clk.To(2);
        double recAt = clk.Beat;
        e.SetRecording(true); // nothing armed: a performance-only pass
        clk.To(13);
        e.StopSlot(a); // → 16
        clk.To(17);
        e.SetRecording(false);
        e.StopTransport();
        var notes = Clips(e, a).Count == 1 ? e.GetClipNotes(a, 0) : Array.Empty<NotaNote>();
        var held = notes.FirstOrDefault(n => n.Pitch == 55 && Near(n.StartBeat, 0));
        yield return (held.Pitch == 55 && Near(held.LengthBeats, 6 - recAt, 0.02) && notes.Count(n => n.Pitch == 55) == 2,
            $"the held note is written from the take start for its remaining length ({string.Join(",", notes.Select(n => $"{n.Pitch}@{n.StartBeat:0.###}+{n.LengthBeats:0.###}"))})");
    }

    // Review (#972): disarming a track mid-pass keeps its played notes in the written clip.
    private static IEnumerable<(bool, string)> DisarmKeepsTake()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(a, 0); // → 4
        clk.To(6);
        e.NoteOn(72, 0.8f); clk.To(6.5); e.NoteOff(72);
        clk.To(8);
        e.SetTrackArmed(a, false);
        clk.To(12);
        e.SetRecording(false);
        e.StopTransport();
        var all = new List<int>();
        for (int k = 0; e.TryGetClipInfo(a, k, out var ci); k++)
            if (ci.IsMidi) all.AddRange(e.GetClipNotes(a, k).Select(n => n.Pitch));
        yield return (all.Contains(72) && all.Contains(48), $"a note played before the disarm stays ({string.Join(",", all)})");
    }

    // Review (#972): an armed track's controller moves over a span join the written clip.
    private static IEnumerable<(bool, string)> TakeControllers()
    {
        using var e = Project(out int a, out int b, out int c);
        e.SetTrackArmed(a, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(1);
        e.LaunchSlot(a, 0); // → 4
        clk.To(6);
        for (int k = 0; k <= 10; k++) { e.SendController(MidiController.ModWheel, k / 10f); clk.To(6 + 0.2 * (k + 1)); }
        clk.To(12);
        e.SetRecording(false);
        e.StopTransport();
        int span = -1;
        for (int k = 0; e.TryGetClipInfo(a, k, out var ci); k++)
            if (ci.IsMidi && Near(ci.StartBeat, 4)) span = k;
        var mod = span >= 0 ? e.GetMidiClipController(a, span, MidiController.ModWheel) : Array.Empty<AutomationPoint>();
        yield return (mod.Length >= 2 && mod.Max(x => x.Value) > 0.9f,
            $"the mod-wheel moves are in the written clip ({mod.Length} points, max {(mod.Length > 0 ? mod.Max(x => x.Value) : 0):0.##})");
    }

    // Review (#972): a plain transport Stop (MCP, API) ends a performance-only pass and writes it.
    private static IEnumerable<(bool, string)> TransportStopWrites()
    {
        using var e = Project(out int a, out int b, out int c);
        e.Play();
        var clk = new Clock(e);
        e.LaunchSlot(a, 0);
        clk.To(4);
        e.SetRecording(true);
        clk.To(10);
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        e.Poll();
        yield return (!e.IsRecording && Clips(e, a).Count(x => x.IsMidi) == 1,
            $"Stop writes the performance and ends the take (recording {e.IsRecording}, clips {Clips(e, a).Count})");
    }

    // Review (#972): with the undo history full, the pass is still one step.
    private static IEnumerable<(bool, string)> FullUndoHistory(int edits)
    {
        using var e = Project(out int a, out int b, out int c);
        int mc = e.AddMidiClip(c, 0, 4);
        for (int i = 0; i < edits; i++) e.SetClipNotes(c, mc, new[] { new NotaNote(60 + i % 12, 0, 0.5, 0.7f) });
        e.SetTrackArmed(c, true);
        var clk = new Clock(e);
        e.SetRecording(true);
        clk.To(2);
        e.NoteOn(66, 0.8f); clk.To(2.5); e.NoteOff(66);
        e.LaunchSlot(a, 0); // → 4
        clk.To(10);
        e.SetRecording(false);
        e.StopTransport();
        int cAfter = Clips(e, c).Count;
        e.Undo();
        bool takeGone = !Enumerable.Range(0, Clips(e, c).Count).Any(k => e.GetClipNotes(c, k).Any(n => n.Pitch == 66));
        yield return (Clips(e, a).Count == 0 && takeGone,
            $"{edits} edits before: one Undo takes the written clips and the take together (a {Clips(e, a).Count}, c {cAfter}→{Clips(e, c).Count}, take gone {takeGone})");
    }
}
