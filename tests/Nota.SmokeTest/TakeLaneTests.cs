// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Take lanes (#187, specs/067-take-lanes-comping): an Arrangement audio recording that wraps
// the loop keeps every pass as a take; the default comp sounds exactly as the single loop
// buffer did, including across the join a partial last pass makes; comping a swiped range,
// deleting a take and flattening are one undo step each; flatten sounds as the comp did; a
// tempo change keeps the takes and their comp on one mapping, and a comp cut or a recording
// stopped before the next audio block uses that mapping too; a reversed section that cannot
// flatten still meets its neighbour at full level afterwards; moving one half of a join onto
// another track fades the edges that used to meet; takes + comp survive save/reopen.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TakeLaneTests
{
    private const double Sr = 48000.0;
    private const int Fpb = 24000;               // frames per beat at 120 BPM
    private const int Loop = 4 * Fpb;            // 1-bar loop, beats 0–4

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in ThreePasses()) yield return r;
        foreach (var r in TempoChange()) yield return r;
        foreach (var r in CompBeforeBlock()) yield return r;
        foreach (var r in RecordBeforeBlock()) yield return r;
        foreach (var r in FlattenKeepsSeparateJoin()) yield return r;
        foreach (var r in MoveToTrackFadesExposedEdge()) yield return r;
        foreach (var r in DeactivatedSideFades()) yield return r;
        foreach (var r in UndoKeepsJoins()) yield return r;
        foreach (var r in NoTakes()) yield return r;
        foreach (var r in SaveReopen()) yield return r;
    }

    // Recording starts at beat 1 and runs 72000 + 96000 + 30000 frames: pass 0 covers loop
    // frames [24000, 96000), pass 1 the whole loop, pass 2 [0, 30000).
    private const int RecFrames = 72000 + 96000 + 30000;

    private static float Value(int pass, int loopFrame) => (pass + 1) * 0.1f + (loopFrame % 1000) * 1e-5f;

    private static float Expected(int[] passAt, int f) => passAt[f] < 0 ? float.NaN : Value(passAt[f], f);

    private static NotaEngine Rig(out int track)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetLoop(true, 0.0, 4.0);
        e.RenderOffline(new float[256 * 2], 256, Sr);
        track = e.AddAudioTrack();
        return e;
    }

    private static bool Record(NotaEngine e, int track, int frames, double startBeat)
    {
        var buf = new float[frames * 2];
        int origin = (int)Math.Round(startBeat * Fpb);
        for (int n = 0; n < frames; n++)
        {
            int g = origin + n;
            float v = Value(g / Loop, g % Loop);
            buf[n * 2] = v;
            buf[n * 2 + 1] = -v;
        }
        return e.TakeRecordTest(track, buf, frames, startBeat);
    }

    // What the track's clips play over the loop, frame by frame (left channel; NaN = nothing,
    // +inf = two clips on one frame), with the right channel checked as the mirror.
    private static float[] Timeline(NotaEngine e, int track, out bool mirrored)
    {
        var tl = new float[Loop];
        Array.Fill(tl, float.NaN);
        mirrored = true;
        int count = ClipCount(e, track);
        for (int c = 0; c < count; c++)
        {
            if (!e.TryGetAudioClipInfo(track, c, out var ac) || !e.TryGetSampleInfo(ac.SampleId, out var si)) continue;
            var data = e.ReadSample(ac.SampleId);
            int ch = si.Channels;
            long len = ac.LengthFrames > 0 ? ac.LengthFrames : si.Frames - (long)ac.SourceOffsetFrames;
            int s = (int)Math.Round(ac.StartBeat * Fpb), off = (int)Math.Round(ac.SourceOffsetFrames);
            for (int j = 0; j < len; j++)
            {
                int f = s + j;
                if (f < 0 || f >= Loop) continue;
                float l = data[(off + j) * ch] * ac.Gain;
                float r = ch > 1 ? data[(off + j) * ch + 1] * ac.Gain : -l;
                tl[f] = float.IsNaN(tl[f]) ? l : float.PositiveInfinity;
                if (r != -l) mirrored = false;
            }
        }
        return tl;
    }

    // What the engine plays over the loop, left channel, rendered from beat 0 with the
    // playback edge fades every clip gets.
    private static float[] Render(NotaEngine e)
    {
        var buf = new float[256 * 2];
        e.StopTransport();
        e.Seek(0.0);
        e.RenderOffline(buf, 256, Sr);   // drain the stop / seek
        e.RenderOffline(buf, 256, Sr);
        e.Play();
        var outL = new float[Loop];
        for (int done = 0; done < Loop; done += 256)
        {
            e.RenderOffline(buf, 256, Sr);
            for (int i = 0; i < 256; i++) outL[done + i] = buf[i * 2];
        }
        e.StopTransport();
        return outL;
    }

    private static int RenderMismatch(float[] a, float[] b)
    {
        for (int f = 0; f < Loop; f++)
            if (Math.Abs(a[f] - b[f]) > 1e-5f) return f;
        return -1;
    }

    private static int ClipCount(NotaEngine e, int track)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.Id == track) return ti.ClipCount;
        return 0;
    }

    private static int Mismatch(float[] tl, int[] passAt)
    {
        for (int f = 0; f < Loop; f++)
        {
            float x = Expected(passAt, f);
            if (!(float.IsNaN(x) ? float.IsNaN(tl[f]) : tl[f] == x)) return f;
        }
        return -1;
    }

    private static int[] DefaultComp()
    {
        var p = new int[Loop];
        for (int f = 0; f < Loop; f++) p[f] = f < 30000 ? 2 : 1;
        return p;
    }

    private static NotaTakeInfo[] Takes(NotaEngine e, int track)
    {
        var list = new List<NotaTakeInfo>();
        for (int k = 0; k < e.TakeCount(track); k++)
            if (e.TryGetTakeInfo(track, k, out var t)) list.Add(t);
        return list.ToArray();
    }

    private static IEnumerable<(bool, string)> ThreePasses()
    {
        using var e = Rig(out int track);
        yield return (Record(e, track, RecFrames, 1.0), "a 3-pass loop recording lands on the track");

        var takes = Takes(e, track);
        yield return (takes.Length == 3 && takes.Select(t => t.Number).SequenceEqual(new[] { 1, 2, 3 }),
            $"every loop pass is a take, numbered 1..3 ({takes.Length})");
        yield return (takes.Length == 3 && takes.All(t => t.Group == takes[0].Group && t.OriginBeat == 0.0 && t.LengthFrames > 0),
            "the takes share one group, frame 0 at the loop start");
        yield return (takes.Length == 3
                      && takes[0].SourceOffsetFrames == 24000 && takes[0].LengthFrames == 72000
                      && takes[1].SourceOffsetFrames == 0 && takes[1].LengthFrames == Loop
                      && takes[2].SourceOffsetFrames == 0 && takes[2].LengthFrames == 30000,
            "partial first and last passes keep only what they recorded");
        yield return (takes.Length == 3 && Math.Abs(takes[0].StartBeat - 1.0) < 1e-9 && Math.Abs(takes[2].LengthBeats - 1.25) < 1e-9,
            "take windows in beats match the recorded range");
        if (takes.Length != 3) yield break;

        var passAt = DefaultComp();
        var tl = Timeline(e, track, out bool mirrored);
        int bad = Mismatch(tl, passAt);
        yield return (bad < 0 && mirrored, $"default comp is sample-exact: the newest pass on every frame (first mismatch {bad})");
        int clips = ClipCount(e, track);
        bool allTagged = Enumerable.Range(0, clips).All(c => e.ClipTakeId(track, c) != 0);
        yield return (clips == 2 && allTagged, $"default comp is two take sections ({clips})");

        // The two sections meet at frame 30000. Playback used to fade both edges there (~6 ms
        // of silence). The outer head of the comp still fades, as one clip always did.
        // Render goes through the mixer, so compare the join to a full-level frame of the
        // same clip rather than to the raw sample.
        var played = Render(e);
        double scale = tl[200] != 0f ? played[200] / tl[200] : 0;
        yield return (Math.Abs(played[0]) < 1e-3
                      && scale > 0.5
                      && Math.Abs(played[29999] - tl[29999] * scale) < 1e-3
                      && Math.Abs(played[30000] - tl[30000] * scale) < 1e-3,
            $"a butt-joined take section does not dip (head {played[0]:G4}, join {played[29999]:G4}/{played[30000]:G4}, scale {scale:G4})");

        // Swipe take 1 over beats 1.5–2.5.
        int clipsBefore = clips;
        yield return (e.CompTake(track, takes[0].Id, 1.5, 2.5), "comp a swiped range of take 1");
        var compAt = (int[])passAt.Clone();
        for (int f = 36000; f < 60000; f++) compAt[f] = 0;
        bad = Mismatch(Timeline(e, track, out _), compAt);
        yield return (bad < 0, $"the comped range plays take 1, the rest is unchanged (first mismatch {bad})");

        yield return (e.Undo(), "undo the comp");
        bad = Mismatch(Timeline(e, track, out _), passAt);
        yield return (bad < 0 && ClipCount(e, track) == clipsBefore, $"one undo step restores the comp (first mismatch {bad})");
        yield return (e.Redo(), "redo the comp");
        bad = Mismatch(Timeline(e, track, out _), compAt);
        yield return (bad < 0, $"redo brings it back (first mismatch {bad})");

        // A range past what take 1 recorded clamps to its window (frames 24000..).
        yield return (e.CompTake(track, takes[0].Id, 0.0, 1.5), "comp a range wider than the take");
        for (int f = 24000; f < 36000; f++) compAt[f] = 0;
        bad = Mismatch(Timeline(e, track, out _), compAt);
        yield return (bad < 0, $"the comp clamps to what the take recorded (first mismatch {bad})");
        yield return (!e.CompTake(track, takes[2].Id, 3.0, 4.0), "a range the take never recorded is refused");

        // Delete take 3: its section keeps sounding, as a plain clip.
        yield return (e.DeleteTake(track, takes[2].Id), "delete a take");
        bad = Mismatch(Timeline(e, track, out _), compAt);
        clips = ClipCount(e, track);
        bool noRef = Enumerable.Range(0, clips).All(c => e.ClipTakeId(track, c) != takes[2].Id);
        yield return (e.TakeCount(track) == 2 && bad < 0 && noRef, $"deleting a take keeps the sound ({e.TakeCount(track)} takes, mismatch {bad})");
        e.Undo();
        yield return (e.TakeCount(track) == 3, "undo brings the deleted take back");

        // Flatten: one clip, no takes, and it renders as the comp did — including joins that
        // no longer fade.
        var compSound = Render(e);
        yield return (e.FlattenTakes(track), "flatten the takes");
        Timeline(e, track, out mirrored);
        bad = RenderMismatch(Render(e), compSound);
        clips = ClipCount(e, track);
        yield return (e.TakeCount(track) == 0 && clips == 1 && e.ClipTakeId(track, 0) == 0 && bad < 0 && mirrored,
            $"flatten leaves one clip that sounds as the comp did ({clips} clips, first mismatch {bad})");
        yield return (e.Undo() && e.TakeCount(track) == 3, "one undo step un-flattens");
        bad = Mismatch(Timeline(e, track, out _), compAt);
        yield return (bad < 0, $"...with the comp as it was (first mismatch {bad})");

        // A second loop recording adds a new group whose numbers continue.
        yield return (Record(e, track, Loop + 12000, 0.0), "record a second loop take");
        var more = Takes(e, track);
        yield return (more.Length == 5 && more[3].Number == 4 && more[4].Number == 5 && more[3].Group != more[0].Group,
            $"a new recording continues the take numbers in a new group ({more.Length})");
    }

    // Every take section sits where its take plays its first frame.
    private static bool SectionsOnTakes(NotaEngine e, int track)
    {
        var takes = Takes(e, track);
        int clips = ClipCount(e, track);
        for (int c = 0; c < clips; c++)
        {
            int id = e.ClipTakeId(track, c);
            if (id == 0) continue;
            if (!e.TryGetAudioClipInfo(track, c, out var ac)) return false;
            var t = takes.FirstOrDefault(k => k.Id == id);
            if (t.Id == 0 || t.LengthFrames <= 0) return false;
            double bpf = t.LengthBeats / t.LengthFrames;
            if (Math.Abs(ac.StartBeat - (t.OriginBeat + ac.SourceOffsetFrames * bpf)) > 1e-9) return false;
        }
        return clips > 0;
    }

    // Unwarped audio plays at its own rate, so a tempo change moves where a take's frames land;
    // the comp sections still where their take put them move with it.
    private static IEnumerable<(bool, string)> TempoChange()
    {
        using var e = Rig(out int track);
        Record(e, track, RecFrames, 1.0);
        var takes = Takes(e, track);
        if (takes.Length != 3) { yield return (false, $"tempo: a 3-pass recording makes 3 takes ({takes.Length})"); yield break; }
        e.CompTake(track, takes[0].Id, 1.5, 2.5);
        e.SetBpm(60);
        e.RenderOffline(new float[256 * 2], 256, Sr);   // the transport takes the new tempo
        var slow = Takes(e, track);
        yield return (Math.Abs(slow[0].StartBeat - 0.5) < 1e-9 && Math.Abs(slow[1].LengthBeats - 2.0) < 1e-9,
            $"at half tempo a take covers half the beats ({slow[0].StartBeat}, {slow[1].LengthBeats})");
        yield return (SectionsOnTakes(e, track), "after a tempo change the comp sections sit where their takes play them");
        yield return (e.Undo() && SectionsOnTakes(e, track), "...and after undoing an edit made at the old tempo");
        yield return (e.Redo() && SectionsOnTakes(e, track), "...and after redoing it");
        yield return (e.FlattenTakes(track) && ClipCount(e, track) == 1,
            $"flatten after a tempo change still merges the comp into one clip ({ClipCount(e, track)})");
    }

    private static bool ClipIs(NotaEngine e, int track, int takeId, double start, double offset, long length)
    {
        int n = ClipCount(e, track);
        for (int c = 0; c < n; c++)
        {
            if (e.ClipTakeId(track, c) != takeId) continue;
            if (!e.TryGetAudioClipInfo(track, c, out var ac)) continue;
            if (Math.Abs(ac.StartBeat - start) < 1e-6
                && Math.Abs(ac.SourceOffsetFrames - offset) < 1.0
                && ac.LengthFrames == length)
                return true;
        }
        return false;
    }

    // SetBpm updates the authoring tempo immediately and the transport only on the next block.
    // A comp in between must cut neighbours with the new samples-per-beat. At 60 BPM, take 1
    // (source frames 24000–96000) over beats 0.5–1.0 is frames 24000–48000; the partial last
    // pass keeps frames 0–24000 and the full pass continues from frame 48000.
    private static IEnumerable<(bool, string)> CompBeforeBlock()
    {
        using var e = Rig(out int track);
        if (!Record(e, track, RecFrames, 1.0))
        {
            yield return (false, "tempo cut: recording failed");
            yield break;
        }
        var takes = Takes(e, track);
        if (takes.Length != 3)
        {
            yield return (false, $"tempo cut: expected 3 takes ({takes.Length})");
            yield break;
        }
        e.SetBpm(60);
        bool shaped = e.CompTake(track, takes[0].Id, 0.5, 1.0)
                      && ClipIs(e, track, takes[0].Id, 0.5, 24000, 24000)
                      && ClipIs(e, track, takes[2].Id, 0.0, 0, 24000)
                      && ClipIs(e, track, takes[1].Id, 1.0, 48000, 48000)
                      && ClipCount(e, track) == 3;
        yield return (shaped, "comping after a tempo change, before the next block, cuts neighbours on the new tempo");
    }

    // 48,000 frames at 48 kHz is two beats on the transport's 120 BPM and one beat at the
    // tempo setBpm has stored. The new clip and the hole it cuts must both be that one beat.
    private static IEnumerable<(bool, string)> RecordBeforeBlock()
    {
        using var e = Rig(out int track);
        e.SetLoop(false, 0.0, 4.0);
        if (!Record(e, track, Loop, 0.0))
        {
            yield return (false, "record carve: the existing clip failed");
            yield break;
        }
        e.SetBpm(60);
        const int frames = 2 * Fpb; // 48,000
        if (!Record(e, track, frames, 0.0))
        {
            yield return (false, "record carve: the second recording failed");
            yield break;
        }
        bool neu = false, rest = false;
        int n = ClipCount(e, track);
        for (int c = 0; c < n; c++)
        {
            if (!e.TryGetAudioClipInfo(track, c, out var ac)) continue;
            // An untrimmed recording leaves lengthFrames at 0, meaning the whole buffer.
            bool whole = ac.LengthFrames == frames || ac.LengthFrames == 0;
            if (Math.Abs(ac.StartBeat) < 1e-6 && whole && Math.Abs(ac.SourceOffsetFrames) < 1.0
                && e.TryGetSampleInfo(ac.SampleId, out var si) && si.Frames == frames)
                neu = true;
            if (Math.Abs(ac.StartBeat - 1.0) < 1e-6 && ac.LengthFrames == frames
                && Math.Abs(ac.SourceOffsetFrames - frames) < 1.0)
                rest = true;
        }
        yield return (e.TakeCount(track) == 0 && n == 2 && neu && rest,
            "stopping a recording after a tempo change, before the next block, cuts one beat");
    }

    private static int ClipAt(NotaEngine e, int track, double startBeat)
    {
        int n = ClipCount(e, track);
        for (int c = 0; c < n; c++)
            if (e.TryGetAudioClipInfo(track, c, out var ac) && Math.Abs(ac.StartBeat - startBeat) < 1e-4)
                return c;
        return -1;
    }

    // Reverse keeps a section out of the merge. The join played full-level; flatten must too.
    private static IEnumerable<(bool, string)> FlattenKeepsSeparateJoin()
    {
        using var e = Rig(out int track);
        if (!Record(e, track, RecFrames, 1.0))
        {
            yield return (false, "flatten join: recording failed");
            yield break;
        }
        int left = ClipAt(e, track, 0.0);
        if (left < 0)
        {
            yield return (false, "flatten join: no section at beat 0");
            yield break;
        }
        e.SetClipReverse(track, left, true);
        var comp = Render(e);
        bool full = Math.Abs(comp[0]) < 1e-3 && Math.Abs(comp[29999]) > 0.05 && Math.Abs(comp[30000]) > 0.05;
        yield return (full,
            $"a reversed take section stays full-level at the join (head {comp[0]:G4}, join {comp[29999]:G4}/{comp[30000]:G4})");
        var before = Render(e);
        yield return (e.FlattenTakes(track) && e.TakeCount(track) == 0, "flatten with one section left reversed");
        int bad = RenderMismatch(Render(e), before);
        yield return (bad < 0, $"flatten keeps the full-level join (first mismatch {bad})");

        // The takes are gone, so only the saved project knows that join plays full-level.
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-join-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            var w = new List<string>();
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), w), dir, e);
            using var d = new NotaEngine();
            d.RenderOffline(new float[256 * 2], 256, Sr);
            var warnings = ProjectService.Apply(ProjectService.Load(dir), d, dir);
            bad = RenderMismatch(Render(d), before);
            yield return (w.Count == 0 && warnings.Count == 0 && bad < 0,
                $"the flattened join reopens full-level (first mismatch {bad}, {w.Count}+{warnings.Count} warnings)");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // A silent section joins nothing: the one that still plays fades where they met.
    private static IEnumerable<(bool, string)> DeactivatedSideFades()
    {
        using var e = Rig(out int track);
        if (!Record(e, track, RecFrames, 1.0))
        {
            yield return (false, "deactivate fade: recording failed");
            yield break;
        }
        var before = Render(e);
        int left = ClipAt(e, track, 0.0);
        if (left < 0)
        {
            yield return (false, "deactivate fade: no section at beat 0");
            yield break;
        }
        e.SetClipActive(track, left, false);
        var after = Render(e);
        bool faded = Math.Abs(before[30000]) > 0.05 && Math.Abs(after[30000]) < 1e-3
                     && Math.Abs(after[30200] - before[30200]) < 1e-3 && Math.Abs(after[100]) < 1e-6;
        yield return (faded,
            $"deactivating one half of a join fades the other half's shared edge ({after[30000]:G4})");
        e.SetClipActive(track, left, true);
        int bad = RenderMismatch(Render(e), before);
        yield return (bad < 0, $"reactivating it joins full-level again (first mismatch {bad})");
    }

    // Undo restores tracks a render may still be reading; their joins come back as they played.
    private static IEnumerable<(bool, string)> UndoKeepsJoins()
    {
        using var e = Rig(out int track);
        if (!Record(e, track, RecFrames, 1.0))
        {
            yield return (false, "undo joins: recording failed");
            yield break;
        }
        var before = Render(e);
        var takes = Takes(e, track);
        bool edited = e.CompTake(track, takes[0].Id, 1.5, 2.5);
        var comp = Render(e);
        edited &= e.FlattenTakes(track);
        var flat = Render(e);
        int bad = RenderMismatch(flat, comp);
        yield return (edited && bad < 0, $"comp then flatten plays the same (first mismatch {bad})");
        bool undone = e.Undo() && e.Undo();
        bad = RenderMismatch(Render(e), before);
        yield return (undone && bad < 0, $"undo twice plays the recording's joins full-level (first mismatch {bad})");
        bool redone = e.Redo() && e.Redo() && e.TakeCount(track) == 0;
        bad = RenderMismatch(Render(e), flat);
        yield return (redone && bad < 0, $"redo twice brings the flatten back as it played (first mismatch {bad})");
    }

    // The half that stays, and the half that moved, both get their edge fade back.
    private static IEnumerable<(bool, string)> MoveToTrackFadesExposedEdge()
    {
        using var e = Rig(out int track);
        if (!Record(e, track, RecFrames, 1.0))
        {
            yield return (false, "move fade: recording failed");
            yield break;
        }
        var before = Render(e);
        int left = ClipAt(e, track, 0.0);
        int other = e.AddAudioTrack();
        if (left < 0)
        {
            yield return (false, "move fade: no section at beat 0");
            yield break;
        }
        e.MoveClipToTrack(track, left, other, 8.0);
        var after = Render(e);
        bool faded = Math.Abs(before[30000]) > 0.05 && Math.Abs(after[30000]) < 1e-3
                     && Math.Abs(after[30200] - before[30200]) < 1e-3;
        yield return (faded,
            $"moving one half of a join onto another track fades the edge that was shared ({after[30000]:G4})");
    }

    private static IEnumerable<(bool, string)> NoTakes()
    {
        using (var e = Rig(out int track))
        {
            Record(e, track, 30000, 1.0);
            yield return (e.TakeCount(track) == 0 && ClipCount(e, track) == 1, "one pass makes a clip, no takes");
        }
        using (var e = Rig(out int track))
        {
            e.SetLoop(false, 0.0, 4.0);
            Record(e, track, RecFrames, 1.0);
            yield return (e.TakeCount(track) == 0 && ClipCount(e, track) == 1, "loop off makes a clip, no takes");
        }
    }

    private static IEnumerable<(bool, string)> SaveReopen()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-takes-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        try
        {
            float[] before;
            int[] numbers;
            using (var e = Rig(out int track))
            {
                Record(e, track, RecFrames, 1.0);
                var takes = Takes(e, track);
                e.CompTake(track, takes[0].Id, 1.5, 2.5);
                before = Timeline(e, track, out _);
                numbers = takes.Select(t => t.Number).ToArray();
                var w = new List<string>();
                ProjectService.Save(ProjectService.Capture(e, transport, w), dir, e);
                yield return (w.Count == 0, $"a project with takes saves without warnings ({w.Count})");
            }

            using var d = new NotaEngine();
            d.RenderOffline(new float[256 * 2], 256, Sr);
            var doc = ProjectService.Load(dir);
            yield return (doc.FormatVersion == ProjectService.CurrentFormatVersion, "saved as the current format");
            var warnings = ProjectService.Apply(doc, d, dir);
            int t2 = -1;
            for (int i = 0; i < d.TrackCount; i++)
                if (d.TryGetTrackInfo(i, out var ti) && ti.Type == 0) { t2 = ti.Id; break; }
            var reTakes = Takes(d, t2);
            yield return (warnings.Count == 0 && reTakes.Select(t => t.Number).SequenceEqual(numbers),
                $"takes reopen with their numbers ({warnings.Count} warnings, {reTakes.Length} takes)");
            var after = Timeline(d, t2, out _);
            int bad = -1;
            for (int f = 0; f < Loop && bad < 0; f++)
                if (!(float.IsNaN(before[f]) ? float.IsNaN(after[f]) : before[f] == after[f])) bad = f;
            yield return (bad < 0, $"the comp reopens sample-exact (first mismatch {bad})");
            int clips = ClipCount(d, t2);
            var ids = reTakes.Select(t => t.Id).ToHashSet();
            bool linked = Enumerable.Range(0, clips).All(c => ids.Contains(d.ClipTakeId(t2, c)));
            bool shared = Enumerable.Range(0, clips).All(c =>
                d.TryGetAudioClipInfo(t2, c, out var ac) && reTakes.Any(t => t.Id == d.ClipTakeId(t2, c) && t.SampleId == ac.SampleId));
            yield return (linked && shared, "comp sections re-link to their takes and share the take's audio");
            yield return (d.CompTake(t2, reTakes[1].Id, 0.0, 4.0) && d.FlattenTakes(t2) && d.TakeCount(t2) == 0,
                "reopened takes can be comped and flattened");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
