// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Automation on a return track (specs/045-return-master-lanes): a return is a track to the
// engine, so its volume and device-parameter lanes play back and survive save/load like a
// track's. The arrangement footer edits them; this locks in the engine side it relies on.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --return-automation`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ReturnAutomationTests
{
    private const int Utility = 4;   // unity gain, no tail: the bus level is the send level

    public static IEnumerable<(bool ok, string label)> Run()
    {
        using var eng = new NotaEngine();
        int src = eng.AddInstrumentTrack();
        int clip = eng.AddMidiClip(src, 0.0, 16.0);
        eng.SetClipNotes(src, clip, new[] { new NotaNote(60, 0.0, 16.0, 0.9f) });
        int ret = eng.AddReturnTrack();
        eng.AddBuiltinDevice(ret, Utility);
        eng.SetTrackSend(src, 0, 0.8f);

        // Volume lane: silent for the first bar, full from beat 8 on.
        int vol = eng.AddAutomationLane(ret, AutomationTarget.Volume, -1, -1);
        yield return (vol >= 0, $"return automation: a volume lane on a return (lane {vol})");
        eng.SetAutomationPoints(ret, vol, new[]
        {
            new AutomationPoint(0.0, 0f), new AutomationPoint(4.0, 0f), new AutomationPoint(8.0, 1f),
        });
        float quiet = PeakAt(eng, ret, 1.0), loud = PeakAt(eng, ret, 10.0);
        yield return (quiet < 0.0005f && loud > 0.001f,
            $"return automation: the return follows its volume lane (beat 1 peak={quiet:F4}, beat 10 peak={loud:F4})");

        // Device-parameter lane on the return's chain: the device takes the lane's value.
        float lo = eng.DeviceParamMin(ret, 0, 0), hi = eng.DeviceParamMax(ret, 0, 0);
        float target = lo + (hi - lo) * 0.25f;
        int dev = eng.AddAutomationLane(ret, AutomationTarget.DeviceParam, 0, 0);
        eng.SetAutomationPoints(ret, dev, new[] { new AutomationPoint(0.0, target), new AutomationPoint(16.0, target) });
        PeakAt(eng, ret, 2.0);
        float got = eng.DeviceGetParam(ret, 0, 0);
        yield return (dev >= 0 && Math.Abs(got - target) < 1e-3f * Math.Max(1f, hi - lo),
            $"return automation: a device lane drives the return's device (want {target:F3}, got {got:F3})");

        // Undo takes the last point edit back.
        yield return (eng.Undo() && eng.GetAutomationPoints(ret, dev).Length == 0,
            "return automation: undo reverts a return lane edit");
        yield return (eng.Redo() && eng.GetAutomationPoints(ret, dev).Length == 2,
            "return automation: redo re-applies it");

        // Save/load keeps both lanes and their points on the return.
        string dir = Path.Combine(Path.GetTempPath(), "nota-return-auto-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, dir, eng);
            using var eng2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), eng2, dir);
            int ret2 = -1;
            for (int i = 0; i < eng2.TrackCount; i++)
                if (eng2.TryGetTrackInfo(i, out var t) && t.IsReturn) ret2 = t.Id;
            int lanes = ret2 > 0 ? eng2.AutomationLaneCount(ret2) : 0;
            bool volOk = false, devOk = false;
            for (int l = 0; l < lanes; l++)
            {
                var info = eng2.AutomationLaneInfo(ret2, l);
                var pts = eng2.GetAutomationPoints(ret2, l);
                if (info.Target == AutomationTarget.Volume) volOk = pts.Length == 3 && Math.Abs(pts[2].Value - 1f) < 1e-6f;
                if (info.Target == AutomationTarget.DeviceParam) devOk = pts.Length == 2 && Math.Abs(pts[0].Value - target) < 1e-4f;
            }
            yield return (lanes == 2 && volOk && devOk,
                $"return automation: save/load keeps the return's lanes ({lanes} lanes, volume {volOk}, device {devOk})");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // Peak of the return's meter over one block rendered from `beat`.
    private static float PeakAt(NotaEngine eng, int trackId, double beat)
    {
        var buf = new float[4096 * 2];
        eng.Seek(beat);
        eng.Play();
        eng.RenderOffline(buf, 4096);
        eng.StopTransport();
        eng.TryGetTrackMeter(trackId, out var m);
        return m.Peak;
    }
}
