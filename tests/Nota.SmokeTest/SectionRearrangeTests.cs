// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Section edits that carry the arrangement with them (specs/073, #192): moving a section
// reorders the song — its clips, automation and takes travel with it and the sections it
// passes close up — and duplicate / delete with content ripple the rest. Each is one undo
// step; overlapping sections refuse them.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SectionRearrangeTests
{
    private const double Eps = 1e-6;

    private static NotaNote N(int p, double s, double l) => new(p, s, l, 0.8f);
    private static AutomationPoint P(double b, float v) => new(b, v);

    // A · B · C on [0,4) [4,8) [8,12) ("C" automatic). Track a: one clip over all three with a
    // note in each (60 / 62 / 64); track b: a clip per section (the same notes) and one after
    // the sections at 12; a volume ramp 0 → 1 over [0,12) on b, master volume 0.2 → 0.8.
    private sealed record Song(NotaEngine E, int A, int B, int Lane);

    private static Song Build()
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        int a = e.AddInstrumentTrack();
        int b = e.AddInstrumentTrack();
        int ca = e.AddMidiClip(a, 0.0, 12.0);
        e.SetClipNotes(a, ca, new[] { N(60, 1, 1), N(62, 5, 1), N(64, 9, 1) });
        int[] pitches = { 60, 62, 64 };
        for (int i = 0; i < 3; i++)
        {
            int c = e.AddMidiClip(b, i * 4.0, 4.0);
            e.SetClipNotes(b, c, new[] { N(pitches[i], 1, 1) });
        }
        int tail = e.AddMidiClip(b, 12.0, 2.0);
        e.SetClipNotes(b, tail, new[] { N(72, 0, 1) });
        int lane = e.AddAutomationLane(b, AutomationTarget.Volume, -1, -1);
        e.SetAutomationPoints(b, lane, new[] { P(0, 0.0f), P(12, 1.0f) });
        e.SetMasterVolumeAutomation(new[] { P(0, 0.2f), P(12, 0.8f) });
        e.SetSections(new[] { new SectionInfo(0, 4, "A"), new SectionInfo(4, 4, "B"), new SectionInfo(8, 4, "") });
        return new Song(e, a, b, lane);
    }

    // Every note on a track as (pitch, absolute beat), in time order.
    private static (int pitch, double at)[] Notes(NotaEngine e, int track)
    {
        var all = new List<(int, double)>();
        for (int i = 0; e.TryGetClipInfo(track, i, out var ci); i++)
            foreach (var n in e.GetClipNotes(track, i))
                all.Add((n.Pitch, ci.StartBeat + n.StartBeat));
        return all.OrderBy(x => x.Item2).ToArray();
    }

    private static bool NotesAre(NotaEngine e, int track, params (int pitch, double at)[] want)
    {
        var got = Notes(e, track);
        return got.Length == want.Length && got.Zip(want).All(p => p.First.pitch == p.Second.pitch && Math.Abs(p.First.at - p.Second.at) < Eps);
    }

    private static bool SectionsAre(NotaEngine e, params (double start, string name)[] want)
    {
        var s = e.Sections;
        return s.Count == want.Length && s.Zip(want).All(p => Math.Abs(p.First.StartBeat - p.Second.start) < Eps && p.First.Name == p.Second.name);
    }

    // The lane's value at `beat` as the engine plays it: each segment shaped by its start point's
    // curve (t^e, e = 2^(−4·curve)); at a step, the later point wins.
    private static float At(AutomationPoint[] pts, double beat)
    {
        if (pts.Length == 0) return 0;
        if (beat <= pts[0].Beat) return pts[0].Value;
        for (int i = 1; i < pts.Length; i++)
            if (beat < pts[i].Beat)
            {
                double t = (beat - pts[i - 1].Beat) / (pts[i].Beat - pts[i - 1].Beat);
                double shaped = pts[i - 1].Curve == 0 ? t : Math.Pow(t, Math.Pow(2, -pts[i - 1].Curve * 4));
                return (float)(pts[i - 1].Value + (pts[i].Value - pts[i - 1].Value) * shaped);
            }
        return pts[^1].Value;
    }

    private static float Ramp(double beat) => (float)Math.Clamp(beat / 12.0, 0.0, 1.0);
    private static float MasterRamp(double beat) => 0.2f + 0.6f * (float)Math.Clamp(beat / 12.0, 0.0, 1.0);

    // Each probe (new beat → the original beat it should now play) matches the original ramp.
    private static bool LaneFollows(AutomationPoint[] pts, Func<double, float> original, params (double now, double was)[] probes)
        => probes.All(p => Math.Abs(At(pts, p.now) - original(p.was)) < 1e-3);

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // Move C before B: A · C · B; everything after the sections stays.
        {
            var s = Build();
            using var e = s.E;
            yield return (e.MoveSection(2, 1), "section move: C before B returns true");
            yield return (SectionsAre(e, (0, "A"), (4, ""), (8, "B")), "section move: order A · C · B, names and automatic mode kept");
            yield return (NotesAre(e, s.A, (60, 1), (64, 5), (62, 9)), "section move: a clip crossing the sections is cut and its parts follow them");
            yield return (NotesAre(e, s.B, (60, 1), (64, 5), (62, 9), (72, 12)), "section move: per-section clips follow, the clip after the sections stays");
            var pts = e.GetAutomationPoints(s.B, s.Lane);
            yield return (LaneFollows(pts, Ramp, (2, 2), (4.001, 8.001), (7.999, 11.999), (8.001, 4.001), (11.999, 7.999), (13, 13)),
                "section move: automation plays each section's own curve at its new place");
            yield return (LaneFollows(e.GetMasterVolumeAutomation(), MasterRamp, (1, 1), (5, 9), (9, 5), (13, 13)),
                "section move: master volume automation follows");
            yield return (!e.MoveSection(1, 1) && !e.MoveSection(1, 2) && !e.MoveSection(5, 0) && !e.MoveSection(0, 9),
                "section move: no-op and out-of-range targets are refused");
            yield return (e.Undo() && SectionsAre(e, (0, "A"), (4, "B"), (8, ""))
                && NotesAre(e, s.A, (60, 1), (62, 5), (64, 9)) && NotesAre(e, s.B, (60, 1), (62, 5), (64, 9), (72, 12))
                && LaneFollows(e.GetAutomationPoints(s.B, s.Lane), Ramp, (1, 1), (5, 5), (9, 9)),
                "section move: one undo restores clips, automation and sections (a refused move added no step)");
            yield return (e.Redo() && SectionsAre(e, (0, "A"), (4, ""), (8, "B")) && NotesAre(e, s.A, (60, 1), (64, 5), (62, 9)),
                "section move: redo repeats it");
        }

        // A curved automation segment cut by a move keeps its shape on both sides (a 0 → 1 ramp
        // over [0,16) with curve −0.25 is (b/16)²), and a cut clip keeps its deactivation and
        // its envelopes, re-based to each piece.
        {
            var s = Build();
            using var e = s.E;
            e.SetAutomationPoints(s.B, s.Lane, new[] { new AutomationPoint(0, 0f, -0.25f), new AutomationPoint(16, 1f) });
            float Sq(double b) => (float)Math.Pow(Math.Clamp(b, 0, 16) / 16.0, 2);
            e.SetClipActive(s.A, 0, false);
            e.SetMidiClipEnvelope(s.A, 0, MidiClipEnvelope.Volume, new[] { P(0, 0f), P(12, 1f) });
            string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_sections_env.wav");
            WavWriter.WriteSine(wav, seconds: 6.0, freq: 220.0, sampleRate: 44100);   // 12 beats at 120
            e.RenderOffline(new float[64 * 2], 64, 48000);   // a sample rate, so the clip has its length in beats
            int audio = e.AddAudioTrack();
            int ac = e.AddAudioClip(audio, wav, startBeat: 0.0);
            e.SetClipVolumeEnvelope(audio, ac, new[] { P(0, 0f), P(12, 1f) });
            bool moved = e.MoveSection(2, 1);   // C before B: cuts at 4, 8, 12
            yield return (moved && LaneFollows(e.GetAutomationPoints(s.B, s.Lane), Sq, (2, 2), (5, 9), (6, 10), (9, 5), (11, 7), (13, 13), (14, 14), (15.5, 15.5)),
                "section move: curved automation keeps its shape on both sides of every cut");
            var pieces = Enumerable.Range(0, 8).Select(i => e.TryGetClipInfo(s.A, i, out var ci) ? (ok: true, i, ci) : (ok: false, i, ci)).Where(x => x.ok).ToArray();
            var cPiece = pieces.FirstOrDefault(x => Math.Abs(x.ci.StartBeat - 4) < Eps);
            yield return (pieces.Length == 3 && pieces.All(x => !x.ci.IsActive)
                && Math.Abs(At(e.GetMidiClipEnvelope(s.A, cPiece.i, MidiClipEnvelope.Volume), 2) - 10 / 12f) < 1e-3,
                "section move: cut MIDI pieces stay deactivated and keep their part of the clip envelope");
            int audioC = Enumerable.Range(0, 8).FirstOrDefault(i => e.TryGetClipInfo(audio, i, out var ci) && Math.Abs(ci.StartBeat - 4) < Eps, -1);
            yield return (audioC >= 0 && Math.Abs(At(e.GetClipVolumeEnvelope(audio, audioC), 2) - 10 / 12f) < 1e-3,
                "section move: a cut audio clip's envelope is re-based to each piece");
        }

        // Move A after C: B · C · A.
        {
            var s = Build();
            using var e = s.E;
            yield return (e.MoveSection(0, 3) && SectionsAre(e, (0, "B"), (4, ""), (8, "A"))
                && NotesAre(e, s.A, (62, 1), (64, 5), (60, 9)) && NotesAre(e, s.B, (62, 1), (64, 5), (60, 9), (72, 12)),
                "section move: A after the last section gives B · C · A");
        }

        // Duplicate B with content: A · B · B · C, the rest later by 4.
        {
            var s = Build();
            using var e = s.E;
            yield return (e.DuplicateSectionWithContent(1) && SectionsAre(e, (0, "A"), (4, "B"), (8, "B"), (12, "")),
                "section duplicate: the copy follows B with its name, C moves later");
            yield return (NotesAre(e, s.A, (60, 1), (62, 5), (62, 9), (64, 13)) && NotesAre(e, s.B, (60, 1), (62, 5), (62, 9), (64, 13), (72, 16)),
                "section duplicate: B's content is copied, everything after it moves by its length");
            yield return (LaneFollows(e.GetAutomationPoints(s.B, s.Lane), Ramp, (5, 5), (9.5, 5.5), (13, 9), (17, 13)),
                "section duplicate: the automation curve is copied into the new section");
            yield return (e.Undo() && SectionsAre(e, (0, "A"), (4, "B"), (8, "")) && NotesAre(e, s.B, (60, 1), (62, 5), (64, 9), (72, 12)),
                "section duplicate: one undo restores it");
        }

        // Delete B with content: A · C, the rest earlier by 4.
        {
            var s = Build();
            using var e = s.E;
            yield return (e.DeleteSectionWithContent(1) && SectionsAre(e, (0, "A"), (4, "")),
                "section delete: B is gone, C moves up");
            yield return (NotesAre(e, s.A, (60, 1), (64, 5)) && NotesAre(e, s.B, (60, 1), (64, 5), (72, 8)),
                "section delete: B's content is removed (crossing clip cut), the rest moves earlier");
            yield return (LaneFollows(e.GetAutomationPoints(s.B, s.Lane), Ramp, (3, 3), (4.001, 8.001), (7, 11), (9, 13)),
                "section delete: automation closes the gap with each side's own values");
            yield return (e.Undo() && SectionsAre(e, (0, "A"), (4, "B"), (8, "")) && NotesAre(e, s.A, (60, 1), (62, 5), (64, 9)),
                "section delete: one undo restores it");
        }

        // Overlapping sections refuse with-content edits; others can still pass them intact.
        {
            var s = Build();
            using var e = s.E;
            e.SetSections(new[] { new SectionInfo(0, 4, "A"), new SectionInfo(3, 5, "B"), new SectionInfo(8, 4, "C") });
            yield return (e.SectionOverlaps(0) && e.SectionOverlaps(1) && !e.SectionOverlaps(2), "section overlap: detected on both sides only");
            yield return (!e.MoveSection(1, 3) && !e.DuplicateSectionWithContent(1) && !e.DeleteSectionWithContent(0)
                && SectionsAre(e, (0, "A"), (3, "B"), (8, "C")) && NotesAre(e, s.A, (60, 1), (62, 5), (64, 9)),
                "section overlap: move / duplicate / delete with content are refused and change nothing");
            yield return (!e.MoveSection(2, 1), "section overlap: no drop inside an overlap");
            yield return (e.MoveSection(2, 0) && SectionsAre(e, (0, "C"), (4, "A"), (7, "B")) && NotesAre(e, s.A, (64, 1), (60, 5), (62, 9)),
                "section overlap: another section moves past the overlapping pair, which shifts intact");
        }

        // The playhead keeps its time; a take travels with its section.
        {
            string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_sections_take.wav");
            WavWriter.WriteSine(wav, seconds: 1.0, freq: 330.0, sampleRate: 44100);
            var s = Build();
            using var e = s.E;
            int audio = e.AddAudioTrack();
            int take = e.RestoreTake(audio, wav, 1, 1, originBeat: 4.0, sourceOffsetFrames: 0, lengthFrames: 22050);
            e.RenderOffline(new float[64 * 2], 64, 48000);   // the first render sets the sample rate
            e.Seek(2.5);
            e.RenderOffline(new float[64 * 2], 64);   // the seek lands with the next block
            bool moved = e.MoveSection(1, 3);   // B after C: B now starts at 8
            e.RenderOffline(new float[64 * 2], 64);
            yield return (moved && Math.Abs(e.PositionBeats - 2.5) < Eps, "section move: the playhead does not move");
            yield return (take >= 0 && e.TryGetTakeInfo(audio, 0, out var k) && Math.Abs(k.StartBeat - 8.0) < Eps,
                "section move: a take lane in the section travels with it");
        }

        // Take lanes in the ripple edits: duplicate copies a take that starts in the section
        // (later ones move on); delete drops one that starts in it (later ones move up).
        {
            string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_sections_takes.wav");
            WavWriter.WriteSine(wav, seconds: 1.0, freq: 330.0, sampleRate: 44100);
            double[] Starts(NotaEngine e, int t)
                => Enumerable.Range(0, e.TakeCount(t)).Select(i => e.TryGetTakeInfo(t, i, out var k) ? k.StartBeat : -1).OrderBy(x => x).ToArray();
            {
                var s = Build();
                using var e = s.E;
                int audio = e.AddAudioTrack();
                e.RestoreTake(audio, wav, 1, 1, originBeat: 4.0, sourceOffsetFrames: 0, lengthFrames: 22050);
                e.RestoreTake(audio, wav, 2, 1, originBeat: 8.0, sourceOffsetFrames: 0, lengthFrames: 22050);
                var st = e.DuplicateSectionWithContent(1) ? Starts(e, audio) : Array.Empty<double>();
                yield return (st.Length == 3 && Math.Abs(st[0] - 4) < Eps && Math.Abs(st[1] - 8) < Eps && Math.Abs(st[2] - 12) < Eps,
                    $"section duplicate: a take in the section is copied under the copy, a later one moves on ({string.Join(", ", st)})");
            }
            {
                var s = Build();
                using var e = s.E;
                int audio = e.AddAudioTrack();
                e.RestoreTake(audio, wav, 1, 1, originBeat: 4.0, sourceOffsetFrames: 0, lengthFrames: 22050);
                e.RestoreTake(audio, wav, 2, 1, originBeat: 8.0, sourceOffsetFrames: 0, lengthFrames: 22050);
                var st = e.DeleteSectionWithContent(1) ? Starts(e, audio) : Array.Empty<double>();
                yield return (st.Length == 1 && Math.Abs(st[0] - 4) < Eps,
                    $"section delete: a take in the section goes with it, a later one moves up ({string.Join(", ", st)})");
                yield return (e.Undo() && e.TakeCount(audio) == 2, "section delete: one undo brings the take back");
            }
        }

        // Save → reopen: the rearranged song is what the project file holds.
        {
            var s = Build();
            using var e = s.E;
            e.MoveSection(2, 1);
            string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-sections-" + Guid.NewGuid().ToString("N") + ".nota");
            var transport = new TransportState(120.0, 1.0, false, false);
            bool ok;
            try
            {
                ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e);
                using var back = new NotaEngine();
                ProjectService.Apply(ProjectService.Load(dir), back, dir);
                int Track(NotaEngine x, int i) => x.TryGetTrackInfo(i, out var ti) ? ti.Id : -1;
                int a = Track(back, 0), b = Track(back, 1);
                int lane = back.AutomationLaneCount(b) > 0 ? 0 : -1;
                ok = SectionsAre(back, (0, "A"), (4, ""), (8, "B")) && NotesAre(back, a, (60, 1), (64, 5), (62, 9))
                     && NotesAre(back, b, (60, 1), (64, 5), (62, 9), (72, 12))
                     && lane == 0 && LaneFollows(back.GetAutomationPoints(b, lane), Ramp, (5, 9), (9, 5));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            yield return (ok, "section move: the rearranged song survives save and reopen");
        }

        // MCP: the same edits, with the reason when refused.
        {
            var s = Build();
            using var e = s.E;
            var proj = new Nota.Mcp.Tools.ProjectTools(e, new SyncDispatch(), new NoRefresh());
            var moved = proj.MoveSection(2, 1).GetAwaiter().GetResult();
            yield return (moved.Select(x => x.Name).SequenceEqual(new[] { "A", "Section 2", "B" }) && NotesAre(e, s.A, (60, 1), (64, 5), (62, 9)),
                "section MCP: move_section reorders with content and returns the new order");
            string? noop = null;
            try { proj.MoveSection(1, 1).GetAwaiter().GetResult(); } catch (Exception ex) { noop = ex.Message; }
            yield return (noop is not null && noop.Contains("already"), "section MCP: a move onto its own place is an error");
            var dup = proj.DuplicateSectionWithContent(0).GetAwaiter().GetResult();
            yield return (dup.Length == 4 && Math.Abs(dup[1].StartBeat - 4) < Eps && dup[1].Name == "A", "section MCP: duplicate_section_with_content");
            var del = proj.DeleteSectionWithContent(1).GetAwaiter().GetResult();
            yield return (del.Length == 3 && NotesAre(e, s.A, (60, 1), (64, 5), (62, 9)), "section MCP: delete_section_with_content");
            e.SetSections(new[] { new SectionInfo(0, 4, "A"), new SectionInfo(3, 5, "B") });
            string? overlap = null;
            try { proj.DeleteSectionWithContent(0).GetAwaiter().GetResult(); } catch (Exception ex) { overlap = ex.Message; }
            yield return (overlap is not null && overlap.Contains("overlaps"), "section MCP: an overlapping section is refused with the reason");
        }

        // Render: the moved sections sound where they land, and undo is exact (SC-002 / SC-003).
        foreach (var r in RenderChecks()) yield return r;
    }

    private static IEnumerable<(bool ok, string label)> RenderChecks()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);   // 4 beats = 2 s
        int t = e.AddAudioTrack();
        double[] freqs = { 220, 440, 880 };
        for (int i = 0; i < 3; i++)
        {
            string wav = Path.Combine(Path.GetTempPath(), $"nota_smoke_sections_{i}.wav");
            WavWriter.WriteSine(wav, seconds: 2.0, freq: freqs[i], sampleRate: 44100);
            e.AddAudioClip(t, wav, startBeat: i * 4.0);
        }
        e.SetSections(new[] { new SectionInfo(0, 4, ""), new SectionInfo(4, 4, ""), new SectionInfo(8, 4, "") });

        const int sr = 48000, per = sr * 2;   // frames per section
        float[] Render()
        {
            var all = new float[per * 3 * 2];
            var chunk = new float[1024 * 2];
            e.RenderOffline(new float[64 * 2], 64, sr);   // stopped: only sets the sample rate
            e.Seek(0);
            e.Play();
            for (int done = 0; done < per * 3; done += 1024)
            {
                int n = Math.Min(1024, per * 3 - done);
                e.RenderOffline(chunk, n);
                Array.Copy(chunk, 0, all, done * 2, n * 2);
            }
            e.StopTransport();
            return all;
        }
        // RMS of the difference between section `x` of one render and section `y` of another,
        // skipping a few ms at each edge (clip declicks).
        double Diff(float[] p, int x, float[] q, int y)
        {
            int skip = sr / 100;
            double sum = 0;
            int count = 0;
            for (int f = skip; f < per - skip; f++)
                for (int ch = 0; ch < 2; ch++)
                {
                    double d = p[(x * per + f) * 2 + ch] - q[(y * per + f) * 2 + ch];
                    sum += d * d;
                    count++;
                }
            return Math.Sqrt(sum / count);
        }

        var before = Render();
        bool moved = e.MoveSection(2, 1);   // A · C · B
        var after = Render();
        yield return (moved && Diff(after, 0, before, 0) < 1e-4 && Diff(after, 1, before, 2) < 1e-3 && Diff(after, 2, before, 1) < 1e-3,
            "section move render: each section sounds the same at its new place");
        e.Undo();
        var undone = Render();
        yield return (Diff(undone, 0, before, 0) < 1e-6 && Diff(undone, 1, before, 1) < 1e-6 && Diff(undone, 2, before, 2) < 1e-6,
            "section move render: undo renders exactly as before");
    }
}
