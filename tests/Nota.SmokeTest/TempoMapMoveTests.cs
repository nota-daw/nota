// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoMapMoveTests
{
    private static bool At(AutomationPoint[] points, double beat, float value) =>
        points.Any(p => Math.Abs(p.Beat - beat) < 1e-6 && Math.Abs(p.Value - value) < 1e-5);

    public static IEnumerable<(bool, string)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-tempo-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string wav = Path.Combine(dir, "two-seconds.wav");
            WavWriter.WriteStereo(wav, 2, 48000, _ => (.1, .1));
            foreach (int method in new[] { 0, 1, 2, 3 }) // single, cross-track, block, cross-track block
            foreach (bool slowSource in new[] { false, true })
            foreach (bool locked in new[] { false, true })
            {
                using var e = new NotaEngine(); e.SetBpm(120); e.RenderOffline(new float[2], 1, 48000);
                e.SetTempoPoints(new[] { new TempoPoint(0, 120), new TempoPoint(4, 60) });
                double source = slowSource ? 8 : 0, destination = slowSource ? 0 : 8;
                double sourceLen = slowSource ? 2 : 4, destLen = slowSource ? 4 : 2;
                int src = e.AddAudioTrack(), dst = method % 2 == 0 ? src : e.AddAudioTrack();
                int clip = e.AddAudioClip(src, wav, source);
                int srcLane = e.AddAutomationLane(src, AutomationTarget.Volume, -1, -1);
                int dstLane = dst == src ? srcLane : e.AddAutomationLane(dst, AutomationTarget.Volume, -1, -1);
                var sourcePoints = new[] {
                    new AutomationPoint(source, .1f), new AutomationPoint(source + sourceLen * .25, .2f),
                    new AutomationPoint(source + sourceLen * .75, .3f), new AutomationPoint(source + sourceLen, .4f),
                    new AutomationPoint(source + sourceLen + 1, .5f) };
                var destinationPoints = new[] { new AutomationPoint(destination + .25, .99f),
                    new AutomationPoint(destination + destLen + .5, .88f) };
                e.SetAutomationPoints(src, srcLane, dst == src
                    ? sourcePoints.Concat(destinationPoints).OrderBy(p => p.Beat).ToArray() : sourcePoints);
                if (dst != src) e.SetAutomationPoints(dst, dstLane, destinationPoints);
                var beforeSrc = e.GetAutomationPoints(src, srcLane);
                var beforeDst = e.GetAutomationPoints(dst, dstLane);
                e.SetAutomationLock(locked); e.ClearUndoHistory();
                if (method == 0) e.MoveClip(src, clip, destination);
                else if (method == 1) e.MoveClipToTrack(src, clip, dst, destination);
                else e.MoveClipBlock(new[] { (src, clip, dst, destination) });
                var afterSrc = e.GetAutomationPoints(src, srcLane);
                var afterDst = e.GetAutomationPoints(dst, dstLane);
                string label = $"method {method}, slow source {slowSource}, locked {locked}";
                if (locked)
                    yield return (afterSrc.SequenceEqual(beforeSrc) && afterDst.SequenceEqual(beforeDst),
                        $"tempo move: locked automation stays unchanged ({label})");
                else
                {
                    yield return (!afterSrc.Any(p => p.Beat >= source - 1e-6 && p.Beat <= source + sourceLen + 1e-6)
                        && At(afterSrc, source + sourceLen + 1, .5f),
                        $"tempo move: lift the whole source span and keep its neighbor ({label})");
                    yield return (sourcePoints.Take(4).All(p => At(afterDst, destination + p.Beat - source, p.Value))
                        && !afterDst.Any(p => p.Value == .99f) && At(afterDst, destination + destLen + .5, .88f),
                        $"tempo move: carry every point and clear only the destination span ({label})");
                }
                yield return (e.Undo() && e.GetAutomationPoints(src, srcLane).SequenceEqual(beforeSrc)
                    && e.GetAutomationPoints(dst, dstLane).SequenceEqual(beforeDst)
                    && e.Redo() && e.GetAutomationPoints(src, srcLane).SequenceEqual(afterSrc)
                    && e.GetAutomationPoints(dst, dstLane).SequenceEqual(afterDst),
                    $"tempo move: undo and redo restore both automation snapshots ({label})");
            }
            foreach (bool block in new[] { false, true })
            {
                using var e = new NotaEngine(); e.SetBpm(120); e.RenderOffline(new float[2], 1, 48000);
                e.SetTempoPoints(new[] { new TempoPoint(0, 120), new TempoPoint(4, 60) });
                int src = e.AddAudioTrack(), dst = e.AddAudioTrack(); e.AddBuiltinDevice(src, 0);
                int clip = e.AddAudioClip(src, wav, 0);
                int volume = e.AddAutomationLane(src, AutomationTarget.Volume, -1, -1);
                int device = e.AddAutomationLane(src, AutomationTarget.DeviceParam, 0, 0);
                var points = new[] { new AutomationPoint(1, .2f), new AutomationPoint(3, .3f), new AutomationPoint(5, .5f) };
                e.SetAutomationPoints(src, volume, points); e.SetAutomationPoints(src, device, points);
                if (block) e.MoveClipBlock(new[] { (src, clip, dst, 8.0) });
                else e.MoveClipToTrack(src, clip, dst, 8);
                yield return (e.GetAutomationPoints(src, device).SequenceEqual(points) && e.LastMoveKeptDeviceAutomation()
                    && !At(e.GetAutomationPoints(src, volume), 3, .3f) && At(e.GetAutomationPoints(dst, 0), 11, .3f),
                    $"tempo move: mismatched layouts carry the complete mix span and retain device automation, block {block}");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
