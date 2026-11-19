// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Every block destination must be cleared before any captured automation is inserted.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoBlockAutomationTests
{
    private static readonly AutomationTarget[] Targets = { AutomationTarget.Volume, AutomationTarget.Pan, AutomationTarget.DeviceParam };
    private static bool Has(AutomationPoint[] points, double beat, float value) =>
        points.Any(p => Math.Abs(p.Beat - beat) < 1e-6 && Math.Abs(p.Value - value) < 1e-6);
    private static bool SamePoints(AutomationPoint[][] a, AutomationPoint[][] b) =>
        a.Zip(b).All(p => p.First.SequenceEqual(p.Second));
    private static AutomationPoint[][] Points(NotaEngine e, int track) => Targets.Select(target =>
    {
        for (int lane = 0; lane < e.AutomationLaneCount(track); ++lane)
            if (e.AutomationLaneInfo(track, lane).Target == target) return e.GetAutomationPoints(track, lane);
        return Array.Empty<AutomationPoint>();
    }).ToArray();
    private static (double, double, long, long)[] Clips(NotaEngine e, int track)
    {
        var clips = new List<(double, double, long, long)>();
        for (int i = 0; e.TryGetAudioClipInfo(track, i, out var c); ++i)
            clips.Add((c.StartBeat, c.SourceOffsetFrames, c.LengthFrames, c.SampleId));
        return clips.OrderBy(c => c.Item1).ToArray();
    }

    public static IEnumerable<(bool, string)> Run()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota-block-automation-" + Guid.NewGuid().ToString("N") + ".wav");
        WavWriter.WriteStereo(wav, 2, 48000, _ => (.1, -.1));
        try
        {
            foreach (var (firstBpm, lastBpm, mapped) in new[] { (60.0, 120.0, true),
                (120.0, 60.0, true), (120.0, 120.0, false) })
            // duplicate, paste, range paste, move, matched cross-move, mismatched
            // cross-move, cross-paste, cross-paste with new lanes, shifted paste.
            foreach (int scenario in Enumerable.Range(0, 9))
            {
                AutomationPoint[][]? previousOrder = null;
                foreach (bool reverse in new[] { false, true })
                {
                    using var e = new NotaEngine(); e.SetBpm(firstBpm); e.RenderOffline(new float[2], 1, 48000);
                    double sourceLen = 2 * firstBpm / 60, blockEnd = 2 * sourceLen;
                    double destLen = 2 * lastBpm / 60;
                    if (mapped) e.SetTempoPoints(new[] { new TempoPoint(0, firstBpm), new TempoPoint(blockEnd, lastBpm) });
                    int src = e.AddAudioTrack(); e.AddBuiltinDevice(src, 0);
                    bool cross = scenario is >= 4 and <= 7, moving = scenario is >= 3 and <= 5;
                    bool missingLanes = scenario == 7;
                    int dst = cross ? e.AddAudioTrack() : src;
                    if (cross) e.AddBuiltinDevice(dst, scenario == 5 ? 1 : 0);
                    e.AddAudioClip(src, wav, 0); e.AddAudioClip(src, wav, sourceLen);
                    if (scenario == 8) e.AddAudioClip(src, wav, blockEnd); // forces a shared non-overlap shift
                    double destination = blockEnd + (scenario == 8 ? destLen : 0);
                    var carried = new[] { new AutomationPoint(sourceLen * .25, .1f), new AutomationPoint(sourceLen * .75, .2f),
                        new AutomationPoint(sourceLen * 1.25, .3f), new AutomationPoint(sourceLen * 1.75, .4f) };
                    var landing = new List<AutomationPoint> { new(destination + .25, .95f),
                        new(destination + sourceLen + destLen - .25, .96f), new(destination + sourceLen + destLen + .5, .97f) };
                    bool gap = destLen < sourceLen;
                    if (gap) landing.Add(new(destination + (destLen + sourceLen) * .5, .98f));
                    for (int lane = 0; lane < Targets.Length; ++lane)
                    {
                        int sourceLane = e.AddAutomationLane(src, Targets[lane], lane == 2 ? 0 : -1, lane == 2 ? 0 : -1);
                        e.SetAutomationPoints(src, sourceLane, cross ? carried : carried.Concat(landing).OrderBy(p => p.Beat).ToArray());
                        if (cross && !missingLanes)
                        {
                            int targetLane = e.AddAutomationLane(dst, Targets[lane], lane == 2 ? 0 : -1, lane == 2 ? 0 : -1);
                            e.SetAutomationPoints(dst, targetLane, landing.OrderBy(p => p.Beat).ToArray());
                        }
                    }
                    var selection = reverse ? new[] { (src, 1), (src, 0) } : new[] { (src, 0), (src, 1) };
                    var beforeSrc = Points(e, src); var beforeDst = Points(e, dst);
                    var beforeSrcClips = Clips(e, src); var beforeDstClips = Clips(e, dst);
                    int beforeLaneCount = e.AutomationLaneCount(dst);
                    bool copied = scenario == 2 ? e.CopyRange(new[] { src }, 0, blockEnd) : e.CopyClipBlock(selection);
                    e.ClearUndoHistory();
                    bool edited;
                    if (scenario == 0) edited = e.DuplicateClipBlock(selection) > 0;
                    else if (moving)
                        edited = e.MoveClipBlock(selection.Select(c => (src, c.Item2, dst, blockEnd + c.Item2 * sourceLen)).ToArray());
                    else edited = e.PasteClipBlock(blockEnd, dst) == 2;
                    var afterSrc = Points(e, src); var afterDst = Points(e, dst);
                    var afterSrcClips = Clips(e, src); var afterDstClips = Clips(e, dst);
                    int afterLaneCount = e.AutomationLaneCount(dst);
                    string label = $"scenario {scenario}, {firstBpm}→{lastBpm}, mapped {mapped}, reverse {reverse}";
                    bool Carries(int lane) => lane < 2 || !cross || scenario == 4;
                    yield return (copied && edited && Enumerable.Range(0, 3).All(lane => !Carries(lane) ||
                        carried.All(p => Has(afterDst[lane], destination + p.Beat, p.Value))),
                        $"block automation: retain every copied point ({label})");
                    yield return (Enumerable.Range(0, 3).All(lane => Carries(lane)
                        ? !afterDst[lane].Any(p => p.Value is .95f or .96f) && (missingLanes ||
                            Has(afterDst[lane], destination + sourceLen + destLen + .5, .97f)) &&
                            (!gap || missingLanes || Has(afterDst[lane], destination + (destLen + sourceLen) * .5, .98f))
                        : afterDst[lane].SequenceEqual(beforeDst[lane])),
                        $"block automation: clear only carried lanes and destination spans ({label})");
                    yield return (Enumerable.Range(0, 3).All(lane => carried.All(p =>
                        Has(afterSrc[lane], p.Beat, p.Value) == (!moving || !Carries(lane)))) &&
                        (!missingLanes || afterLaneCount == 2),
                        $"block automation: preserve source/filter rules and create only needed lanes ({label})");
                    if (previousOrder is not null)
                        yield return (SamePoints(previousOrder, afterDst),
                            $"block automation: both selection orders produce identical automation ({label})");
                    previousOrder = afterDst;
                    yield return (e.Undo() && !e.CanUndo && SamePoints(Points(e, src), beforeSrc) &&
                        SamePoints(Points(e, dst), beforeDst) && e.AutomationLaneCount(dst) == beforeLaneCount &&
                        Clips(e, src).SequenceEqual(beforeSrcClips) && Clips(e, dst).SequenceEqual(beforeDstClips) &&
                        e.Redo() && SamePoints(Points(e, src), afterSrc) && SamePoints(Points(e, dst), afterDst) &&
                        e.AutomationLaneCount(dst) == afterLaneCount && Clips(e, src).SequenceEqual(afterSrcClips) &&
                        Clips(e, dst).SequenceEqual(afterDstClips),
                        $"block automation: undo/redo restores the whole block in one step ({label})");
                }
            }
        }
        finally { File.Delete(wav); }
    }
}
