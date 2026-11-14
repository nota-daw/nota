// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Destination spans and partial-take anchors must follow the map after authoring edits.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoMapCopyTakeTests
{
    private const int Sr = 48000;
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
    private static bool Has(AutomationPoint[] points, double beat, float value) =>
        points.Any(p => Near(p.Beat, beat) && Math.Abs(p.Value - value) < 1e-6);

    private static NotaEngine Engine(double first, double last, double boundary = 4, bool mapped = true,
        TempoShape shape = TempoShape.Step)
    {
        var e = new NotaEngine(); e.SetBpm(first); e.RenderOffline(new float[2], 1, Sr);
        if (mapped) e.SetTempoPoints(new[] { new TempoPoint(0, first, shape), new TempoPoint(boundary, last) });
        e.SplitFades = false;
        return e;
    }

    private static (double Start, double Offset, long Frames, long Sample, int Take)[] Clips(NotaEngine e, int track)
    {
        var result = new List<(double, double, long, long, int)>();
        for (int i = 0; e.TryGetAudioClipInfo(track, i, out var c); ++i)
            result.Add((c.StartBeat, c.SourceOffsetFrames, c.LengthFrames, c.SampleId, e.ClipTakeId(track, i)));
        return result.OrderBy(c => c.Item1).ToArray();
    }

    private static NotaTakeInfo[] Takes(NotaEngine e, int track) => Enumerable.Range(0, e.TakeCount(track))
        .Select(i => { e.TryGetTakeInfo(track, i, out var k); return k; }).OrderBy(k => k.Id).ToArray();

    public static IEnumerable<(bool, string)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-tempo-copy-takes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string two = Path.Combine(dir, "two.wav"), four = Path.Combine(dir, "four.wav");
            WavWriter.WriteStereo(two, 2, Sr, i => (.1 + i / (100.0 * Sr), -.1));
            WavWriter.WriteStereo(four, 4, Sr, i => (.2 + i / (100.0 * Sr), -.2));
            foreach (var r in DuplicateRanges(two, four)) yield return r;
            foreach (var r in PasteAutomation(two)) yield return r;
            foreach (int rate in new[] { 44100, 48000 })
            {
                string take = Path.Combine(dir, $"take-{rate}.wav");
                WavWriter.WriteStereo(take, 12, rate, i => (.1 + i / (100.0 * rate), -.1));
                foreach (var r in SectionTakes(take, rate)) yield return r;
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static IEnumerable<(bool, string)> DuplicateRanges(string two, string four)
    {
        foreach (bool slowSource in new[] { true, false })
        foreach (bool reverse in new[] { false, true })
        foreach (bool warped in new[] { false, true })
        {
            using var e = Engine(slowSource ? 60 : 120, slowSource ? 120 : 60);
            int track = e.AddAudioTrack();
            e.AddAudioClip(track, slowSource ? four : two, 0);
            e.SetClipReverse(track, 0, reverse);
            if (warped) { e.SetClipWarp(track, 0, true, 0); e.SetClipWarpLength(track, 0, 4); }
            double neighborStart = slowSource ? 8 : 7;
            int neighbor = e.AddAudioClip(track, two, neighborStart);
            e.TryGetAudioClipInfo(track, neighbor, out var originalNeighbor);
            var before = Clips(e, track); e.ClearUndoHistory();
            double range = e.DuplicateRange(new[] { track }, 0, 4);
            var after = Clips(e, track);
            double copyEnd = warped ? 8 : slowSource ? 12 : 6;
            var copy = after.Single(c => Near(c.Start, 4));
            var remainder = after.Where(c => c.Sample == originalNeighbor.SampleId).ToArray();
            // Expanded copies remove this neighbor; compressed copies leave it alone.
            bool neighborCorrect = copyEnd >= neighborStart + (slowSource ? 4 : 2)
                ? remainder.Length == 0
                : remainder.Length == 1 && Near(remainder[0].Start, Math.Max(copyEnd, neighborStart)) &&
                    Near(remainder[0].Offset, Math.Max(0, e.BeatToSeconds(copyEnd) - e.BeatToSeconds(neighborStart)) * Sr);
            yield return (Near(range, 4) && neighborCorrect,
                $"tempo copies: duplicate clears the actual landing span (slow source {slowSource}, reverse {reverse}, warped {warped})");
            yield return (Near(copy.Offset, before[0].Offset) && copy.Frames == before[0].Frames &&
                after.Any(c => c == before[0]), "tempo copies: range copy retains source frames and original clip");
            yield return (e.Undo() && !e.CanUndo && Clips(e, track).SequenceEqual(before) &&
                e.Redo() && Clips(e, track).SequenceEqual(after), "tempo copies: range duplication is one undo/redo step");
        }
        // An unchanged beat span keeps the existing range semantics: silence in
        // the selected range also overwrites content in the destination gap.
        foreach (bool mapped in new[] { false, true })
        {
            using var e = Engine(120, 120, mapped: mapped);
            int track = e.AddAudioTrack();
            int a = e.AddAudioClip(track, two, 0); e.TrimClip(track, a, 0, 1);
            int b = e.AddAudioClip(track, two, 2); e.TrimClip(track, b, 2, 1);
            int neighbor = e.AddAudioClip(track, four, 5.25); e.TrimClip(track, neighbor, 5.25, .5);
            var before = Clips(e, track); e.ClearUndoHistory();
            e.DuplicateRange(new[] { track }, 0, 4);
            var after = Clips(e, track);
            yield return (after.Select(c => c.Start).SequenceEqual(new[] { 0.0, 2, 4, 6 }),
                $"tempo copies: constant-tempo range gaps retain their overwrite behavior, mapped {mapped}");
            yield return (e.Undo() && !e.CanUndo && Clips(e, track).SequenceEqual(before) &&
                e.Redo() && Clips(e, track).SequenceEqual(after),
                $"tempo copies: fixed-span gap overwrite restores in one undo/redo step, mapped {mapped}");
        }
    }

    private static IEnumerable<(bool, string)> PasteAutomation(string wav)
    {
        foreach (bool slowSource in new[] { false, true })
        foreach (int method in new[] { 0, 1, 2 }) // block paste, range paste, block duplicate
        foreach (bool crossTrack in new[] { false, true })
        {
            if (method == 2 && crossTrack) continue;
            double sourceLen = slowSource ? 2 : 4, destLen = slowSource ? 4 : 2;
            using var e = Engine(slowSource ? 60 : 120, slowSource ? 120 : 60, sourceLen);
            int src = e.AddAudioTrack(), dst = crossTrack ? e.AddAudioTrack() : src;
            int clip = e.AddAudioClip(src, wav, 0);
            int srcLane = e.AddAutomationLane(src, AutomationTarget.Volume, -1, -1);
            int dstLane = crossTrack ? e.AddAutomationLane(dst, AutomationTarget.Volume, -1, -1) : srcLane;
            double destination = method == 2 ? sourceLen : 8;
            var carried = new[] { new AutomationPoint(0, .1f), new AutomationPoint(sourceLen * .25, .2f),
                new AutomationPoint(sourceLen * .75, .3f) };
            var landing = new[] { new AutomationPoint(destination + .25, .99f),
                new AutomationPoint(destination + destLen - .25, .98f),
                new AutomationPoint(destination + destLen + .5, .77f) };
            e.SetAutomationPoints(src, srcLane, crossTrack ? carried : carried.Concat(landing).OrderBy(p => p.Beat).ToArray());
            if (crossTrack) e.SetAutomationPoints(dst, dstLane, landing);
            var beforeSrc = e.GetAutomationPoints(src, srcLane); var beforeDst = e.GetAutomationPoints(dst, dstLane);
            var beforeClips = Clips(e, dst);
            bool copied = method == 1 ? e.CopyRange(new[] { src }, 0, sourceLen)
                : e.CopyClipBlock(new[] { (src, clip) });
            e.ClearUndoHistory();
            bool placed = method == 2 ? e.DuplicateClipBlock(new[] { (src, clip) }) > 0
                : e.PasteClipBlock(destination, dst) >= 0;
            var afterSrc = e.GetAutomationPoints(src, srcLane); var afterDst = e.GetAutomationPoints(dst, dstLane);
            var afterClips = Clips(e, dst);
            string label = $"method {method}, slow source {slowSource}, cross-track {crossTrack}";
            yield return (copied && placed && !afterDst.Any(p => p.Value is .99f or .98f) &&
                Has(afterDst, destination + destLen + .5, .77f),
                $"tempo copies: clear automation only within the destination span ({label})");
            yield return (carried.All(p => Has(afterDst, destination + p.Beat, p.Value)) &&
                carried.All(p => Has(afterSrc, p.Beat, p.Value)),
                $"tempo copies: paste retains beat-relative offsets and source automation ({label})");
            yield return (e.Undo() && !e.CanUndo && e.GetAutomationPoints(src, srcLane).SequenceEqual(beforeSrc) &&
                e.GetAutomationPoints(dst, dstLane).SequenceEqual(beforeDst) && Clips(e, dst).SequenceEqual(beforeClips) &&
                e.Redo() && e.GetAutomationPoints(src, srcLane).SequenceEqual(afterSrc) &&
                e.GetAutomationPoints(dst, dstLane).SequenceEqual(afterDst) && Clips(e, dst).SequenceEqual(afterClips),
                $"tempo copies: paste undo/redo restores audio and automation together ({label})");
        }
    }

    private static IEnumerable<(bool, string)> SectionTakes(string wav, int rate)
    {
        foreach (var (first, last, mapped, shape) in new[] { (120.0, 60.0, true, TempoShape.Step),
            (60.0, 120.0, true, TempoShape.Step), (120.0, 60.0, true, TempoShape.Linear),
            (120.0, 120.0, false, TempoShape.Step) })
        foreach (string operation in new[] { "move later", "move earlier", "duplicate", "delete earlier" })
        {
            using var e = Engine(first, last, mapped: mapped, shape: shape);
            int track = e.AddAudioTrack();
            long offset = (long)Math.Round(e.BeatToSeconds(5) * rate);
            long laterOffset = (long)Math.Round(e.BeatToSeconds(9) * rate);
            int take = e.RestoreTake(track, wav, 1, 1, 0, offset, rate / 2);
            int later = e.RestoreTake(track, wav, 2, 2, 0, laterOffset, rate / 2);
            double initialStart = e.SecondsToBeat(offset / (double)rate), laterStart = e.SecondsToBeat(laterOffset / (double)rate);
            bool comped = e.CompTake(track, take, initialStart, initialStart + .25) &&
                e.CompTake(track, later, laterStart, laterStart + .25);
            e.SetSections(new[] { new SectionInfo(0, 4, "A"), new SectionInfo(4, 4, "B"), new SectionInfo(8, 4, "C") });
            var beforeTakes = Takes(e, track); var beforeClips = Clips(e, track);
            e.ClearUndoHistory();
            bool edited = operation switch { "move later" => e.MoveSection(1, 3), "move earlier" => e.MoveSection(1, 0),
                "duplicate" => e.DuplicateSectionWithContent(1), _ => e.DeleteSectionWithContent(0) };
            var afterTakes = Takes(e, track); var afterClips = Clips(e, track);
            double expectedFirst = initialStart + (operation == "move later" ? 4 : operation == "duplicate" ? 0 : -4);
            double expectedLater = laterStart + (operation == "move later" || operation == "delete earlier" ? -4
                : operation == "duplicate" ? 4 : 0);
            var original = afterTakes.Single(k => k.Id == take); var tail = afterTakes.Single(k => k.Id == later);
            string label = $"{operation}, {first}→{last} {shape}, mapped {mapped}, {rate} Hz";
            yield return (comped && edited && Near(original.StartBeat, expectedFirst) && Near(tail.StartBeat, expectedLater),
                $"tempo takes: partial takes follow their moved first frame ({label})");
            var target = operation == "duplicate" ? afterTakes.Single(k => k.Id != take && k.Id != later) : original;
            double targetStart = operation == "duplicate" ? initialStart + 4 : expectedFirst;
            yield return (Near(target.StartBeat, targetStart) && target.SourceOffsetFrames == offset &&
                target.LengthFrames == rate / 2 && (operation != "duplicate" || target.Group != original.Group) &&
                afterClips.Any(c => c.Take == target.Id && Near(c.Start, targetStart) && Near(c.Offset, offset)),
                $"tempo takes: comp clips and independent copied takes share their anchor ({label})");
            yield return (e.Undo() && !e.CanUndo && Takes(e, track).SequenceEqual(beforeTakes) &&
                Clips(e, track).SequenceEqual(beforeClips) && e.Redo() && Takes(e, track).SequenceEqual(afterTakes) &&
                Clips(e, track).SequenceEqual(afterClips), $"tempo takes: section undo/redo restores take and comp snapshots ({label})");
            double start = targetStart + .1, end = targetStart + .2;
            bool swiped = e.CompTake(track, target.Id, start, end);
            double expectedOffset = Math.Round(offset + (e.BeatToSeconds(start) - e.BeatToSeconds(targetStart)) * rate);
            var selected = Clips(e, track).Where(c => c.Take == target.Id && Near(c.Offset, expectedOffset)).ToArray();
            yield return (swiped && selected.Length == 1 && Math.Abs(selected[0].Start - start) <= 2.0 / rate,
                $"tempo takes: comp swipe selects the original recorded samples after section edit ({label})");
        }
    }
}
