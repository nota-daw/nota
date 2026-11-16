// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// specs/099 US4: the master's other automation targets — Pan (a stage after the master
// devices, before the master fader) and the parameters of master devices — play, export,
// save, and follow device removal + undo like a track's.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MasterAutomationTests
{
    private const int Sr = 48000;   // 120 BPM: one beat = 0.5 s

    private static float[] Render(NotaEngine e, int frames)
    {
        var buf = new float[frames * 2];
        e.RenderOffline(buf, frames, Sr);
        return buf;
    }

    // RMS of one channel between two times (seconds).
    private static double Rms(float[] stereo, int ch, double from, double to)
    {
        int a = (int)(from * Sr), b = Math.Min((int)(to * Sr), stereo.Length / 2);
        double sum = 0;
        for (int i = a; i < b; i++) sum += stereo[i * 2 + ch] * (double)stereo[i * 2 + ch];
        return b > a ? Math.Sqrt(sum / (b - a)) : 0;
    }

    private static double Db(double ratio) => 20 * Math.Log10(Math.Max(ratio, 1e-9));

    private static float[] ReadFloatWav(string path)
    {
        var raw = File.ReadAllBytes(path);
        int pos = 12;   // walk the chunks to "data"
        while (pos + 8 <= raw.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
            int size = BitConverter.ToInt32(raw, pos + 4);
            if (id == "data")
            {
                var s = new float[size / 4];
                for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToSingle(raw, pos + 8 + i * 4);
                return s;
            }
            pos += 8 + size + (size & 1);
        }
        return Array.Empty<float>();
    }

    private static int ParamIndex(NotaEngine e, int track, int dev, string name)
    {
        for (int i = 0; i < e.DeviceParamCount(track, dev); i++)
            if (e.DeviceParamName(track, dev, i) == name) return i;
        return -1;
    }

    public static IEnumerable<(bool, string)> Run()
    {
        using (var self = new NotaEngine())
        {
            int failed = self.MasterAutomationSelfTest();
            yield return (failed == 0, $"master automation: engine self-test (failed check {failed})");
        }

        string temp = Path.Combine(Path.GetTempPath(), "nota-master-automation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string wav = Path.Combine(temp, "sine.wav");
            WavWriter.WriteSine(wav, 10, 440, Sr);
            using var e = new NotaEngine();
            e.SetBpm(120); Render(e, 1);
            int m = e.MasterTrackId;
            int track = e.AddAudioTrack();
            e.AddAudioClip(track, wav, 0);

            // Centre pan is pass-through: the master pan stage never changes an unpanned mix.
            e.Seek(0); e.Play();
            var plain = Render(e, Sr);
            e.StopTransport();
            double refL = Rms(plain, 0, .2, .8), refR = Rms(plain, 1, .2, .8);
            yield return (refL > .1 && Math.Abs(refL - refR) < 1e-6, "master pan: centre leaves the mix untouched");

            // Pan −1 → 1 over 16 beats (8 s): Balance law, centre at unity, near side never boosted.
            int pan = e.AddAutomationLane(m, AutomationTarget.Pan, -1, -1);
            e.SetAutomationPoints(m, pan, new[] { new AutomationPoint(0, -1, 0), new AutomationPoint(16, 1, 0) });
            e.Seek(0); e.Play();
            var swept = Render(e, Sr * 8);
            e.StopTransport();
            // At 2 s the pan is −0.5: L stays at unity, R = √2·sin(π/8) ≈ 0.541.
            double l2 = Rms(swept, 0, 1.95, 2.05) / refL, r2 = Rms(swept, 1, 1.95, 2.05) / refR;
            double lStart = Rms(swept, 0, .4, .5) / refL, rStart = Rms(swept, 1, .4, .5) / refR;   // pan ≈ −0.89
            double lMid = Rms(swept, 0, 3.98, 4.02) / refL, rMid = Rms(swept, 1, 3.98, 4.02) / refR;
            double lEnd = Rms(swept, 0, 7.85, 7.95) / refL, rEnd = Rms(swept, 1, 7.85, 7.95) / refR;
            yield return (Math.Abs(l2 - 1) < .01 && Math.Abs(r2 - .541) < .03 && rStart < .2 && lStart > .99
                          && Math.Abs(lMid - rMid) < .03 && lEnd < .05 && rEnd > .99,
                $"master pan: a −1 → 1 lane sweeps the balance (start L {lStart:0.00}/R {rStart:0.00}, −0.5 L {l2:0.00}/R {r2:0.00}, end L {lEnd:0.00}/R {rEnd:0.00})");

            // A master EQ's Output lane −12 → 0 dB changes the level of the mix (no boost: the master
            // clamps at ±1).
            int eq = e.AddBuiltinDevice(m, 0);
            int output = ParamIndex(e, m, eq, "Output");
            int devLane = e.AddAutomationLane(m, AutomationTarget.DeviceParam, eq, output);
            e.SetAutomationPoints(m, devLane, new[] { new AutomationPoint(0, -12, 0), new AutomationPoint(16, 0, 0) });
            e.RemoveAutomationLane(m, pan);   // level only from here
            devLane = e.AutomationLaneCount(m) - 1;
            e.SetMasterPan(0);
            e.Seek(0); e.Play();
            var leveled = Render(e, Sr * 8);
            e.StopTransport();
            double early = Db(Rms(leveled, 0, .2, .4) / refL), late = Db(Rms(leveled, 0, 7.6, 7.8) / refL);
            yield return (eq >= 0 && output >= 0 && devLane >= 0 && early < -9 && late > -2 && late - early > 8,
                $"master device: an EQ Output lane moves the master level ({early:0.0} dB → {late:0.0} dB)");

            // Export renders the same master automation.
            string exportPath = Path.Combine(temp, "export.wav");
            new AudioExporter().ExportMaster(e, new ExportRequest(exportPath, 16, 120, Sr, WavBitDepth.Float32,
                RestoreLoop: false, RestoreMetronome: false));
            var exported = ReadFloatWav(exportPath);
            // Compared past the start: the EQ glides from where the last render left it.
            double mid = Db(Rms(leveled, 0, 3.9, 4.1) / refL);
            double xMid = Db(Rms(exported, 0, 3.9, 4.1) / refL), xLate = Db(Rms(exported, 0, 7.6, 7.8) / refL);
            yield return (Math.Abs(xMid - mid) < .5 && Math.Abs(xLate - late) < .5,
                $"master device: export matches playback (mid {xMid:0.0}/{mid:0.0} dB, end {xLate:0.0}/{late:0.0} dB)");

            // Save / reload: master lanes and the master pan knob value.
            int pan2 = e.AddAutomationLane(m, AutomationTarget.Pan, -1, -1);
            e.SetAutomationPoints(m, pan2, new[] { new AutomationPoint(0, .5f, 0) });
            e.SetMasterPan(-.25f);
            string bundle = Path.Combine(temp, "master.nota");
            var doc = ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, bundle, e);
            using var restored = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(bundle), restored, bundle);
            int rm = restored.MasterTrackId;
            var targets = Enumerable.Range(0, restored.AutomationLaneCount(rm))
                .Select(i => restored.AutomationLaneInfo(rm, i).Target).ToList();
            yield return (Math.Abs(restored.MasterPan + .25f) < 1e-6 && restored.TrackDeviceCount(rm) == 1
                          && targets.Contains(AutomationTarget.Pan) && targets.Contains(AutomationTarget.DeviceParam),
                $"master automation: save/reload keeps the master pan ({restored.MasterPan:0.00}) and its Pan + device lanes");

            // Removing the master EQ drops its lane; undo brings back device and lane.
            int before = e.AutomationLaneCount(m);
            e.RemoveDevice(m, eq);
            int removed = e.AutomationLaneCount(m);
            bool undone = e.Undo();
            yield return (removed == before - 1 && e.TrackDeviceCount(m) == 1 && undone && e.AutomationLaneCount(m) == before,
                $"master device: removing it drops its lane, undo restores both ({before} → {removed} → {e.AutomationLaneCount(m)})");

            // An export starts where it starts: no ramp from the pan the last playback left behind.
            e.RemoveAutomationLane(m, e.AutomationLaneCount(m) - 1);   // the Pan lane: the knob leads
            e.SetMasterPan(1f);
            e.Seek(0); e.Play(); Render(e, Sr / 2); e.StopTransport();
            e.SetMasterPan(0f);
            string centred = Path.Combine(temp, "centred.wav");
            new AudioExporter().ExportMaster(e, new ExportRequest(centred, 2, 120, Sr, WavBitDepth.Float32,
                RestoreLoop: false, RestoreMetronome: false));
            var cx = ReadFloatWav(centred);
            double cl = Rms(cx, 0, .01, .1), cr = Rms(cx, 1, .01, .1);
            yield return (cl > .01 && Math.Abs(cl - cr) / cr < .01,
                $"master pan: an export at centre starts centred after right-panned playback (L {cl:0.000} / R {cr:0.000})");

            // Master lanes follow a loop wrap inside one render call: loop beats 0–4 (2 s) with a
            // −1 → 1 pan lane; just after the wrap the pan is back near −1.
            int wrapLane = e.AddAutomationLane(m, AutomationTarget.Pan, -1, -1);
            e.SetAutomationPoints(m, wrapLane, new[] { new AutomationPoint(0, -1, 0), new AutomationPoint(4, 1, 0) });
            e.SetLoop(true, 0, 4);
            e.Seek(0); e.Play();
            var looped = Render(e, Sr * 3);
            e.StopTransport(); e.SetLoop(false, 0, 4);
            double wl = Rms(looped, 0, 2.15, 2.25) / refL, wr = Rms(looped, 1, 2.15, 2.25) / refR;
            // R/L: the master EQ's Output lane still moves the level, the balance is the pan's.
            yield return (wl > .05 && wr / wl < .3,
                $"master pan: a lane follows the loop wrap inside one render (after the wrap R/L {wr / wl:0.00}, ≈0.17 expected)");
            e.RemoveAutomationLane(m, wrapLane);

            // MCP: master pan and master lanes.
            var disp = new SyncDispatch(); var refresh = new NoRefresh();
            var automation = new Nota.Mcp.Tools.AutomationTools(e, disp, refresh);
            var transport = new Nota.Mcp.Tools.TransportTools(e, disp, refresh);
            transport.SetMasterPan(.75f).Wait();
            int volumeLane = automation.AddAutomationLane(m, "volume").Result;
            int mcpPan = automation.AddAutomationLane(m, "pan").Result;
            var lanes = automation.ListAutomation(m).Result;
            lanes = automation.ListAutomation(m).Result;
            yield return (Math.Abs(e.MasterPan - .75f) < 1e-6 && volumeLane == -1 && mcpPan >= 0
                          && lanes.Any(l => l.Target == "pan") && lanes.Any(l => l.Target == "device_param"),
                "master automation: MCP sets the master pan and lists the master's pan and device lanes");
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
