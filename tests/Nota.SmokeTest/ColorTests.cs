// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using Nota.Application;
using Nota.Infrastructure;
namespace Nota.SmokeTest;
internal static class ColorTests
{
    private const int Mode = 24;
    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int track = e.AddAudioTrack(), di = e.AddBuiltinDevice(track, 27);
        yield return (di >= 0 && e.DeviceName(track, di) == "Nota Chroma" && e.DeviceParamCount(track, di) == 25 &&
            e.DeviceParamName(track, di, Mode) == "Mode" && e.DeviceGetParam(track, di, Mode) == 0,
            "Chroma appends Mode with Spectral as the default");
        yield return (e.DeviceGetParam(track, di, 21) == 0 && e.DeviceGetParam(track, di, 22) == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 23) - .25f) < 1e-6, "Color spectral controls default off with -60 dBFS threshold");
        float[] expected = Enumerable.Range(0, 25).Select(p => (float)((p * 7 % 19) / 18.0)).ToArray();
        expected[Mode] = 1;
        for (int p = 0; p < expected.Length; p++) e.DeviceSetParam(track, di, p, expected[p]);
        int clone = e.DuplicateTrack(track);
        yield return (Enumerable.Range(0, 25).All(p => Math.Abs(e.DeviceGetParam(clone, di, p) - expected[p]) < 1e-6), "Chroma clone retains every parameter");
        string dir = Path.Combine(Path.GetTempPath(), "nota-color-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>()), dir, e);
            using var restored = new NotaEngine();
            var warnings = ProjectService.Apply(ProjectService.Load(dir), restored, dir);
            bool same = restored.TryGetTrackInfo(0, out var info) && restored.TrackDeviceBuiltinKind(info.Id, 0) == 27 &&
                Enumerable.Range(0, 25).All(p => Math.Abs(restored.DeviceGetParam(info.Id, 0, p) - expected[p]) < 1e-6);
            yield return (warnings.Count == 0 && same, "Color project round-trip retains kind and complete custom mask");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        foreach (int count in new[] { 21, 24 })
        {
            var legacy = ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>());
            foreach (var sourceTrack in legacy.Tracks)
                foreach (var dev in sourceTrack.Devices.Where(d => d.BuiltinKind == 27)) dev.Params = dev.Params.Take(count).ToArray();
            using var old = new NotaEngine();
            var warnings = ProjectService.Apply(legacy, old, dir);
            bool ok = old.TryGetTrackInfo(0, out var tr) &&
                Enumerable.Range(0, count).All(p => Math.Abs(old.DeviceGetParam(tr.Id, 0, p) - expected[p]) < 1e-6) &&
                old.DeviceGetParam(tr.Id, 0, Mode) == 0;
            if (count == 21 && ok)
                ok = old.DeviceGetParam(tr.Id, 0, 21) == 0 && old.DeviceGetParam(tr.Id, 0, 22) == 0 &&
                    Math.Abs(old.DeviceGetParam(tr.Id, 0, 23) - .25f) < 1e-6;
            yield return (ok && warnings.Count == 0, $"Chroma loads legacy {count}-parameter state in Spectral");
        }
        var catalog = new FactoryPresetCatalog();
        var presets = catalog.All().Where(p => p.Id.StartsWith("color/", StringComparison.Ordinal)).ToArray();
        yield return (presets.Length == 6, "Chroma has six factory presets");
        yield return (presets.All(p =>
        {
            e.DeviceSetParam(track, di, Mode, 1);
            return catalog.ApplyInPlace(e, p.Id, track, di).Length == 0 && e.DeviceGetParam(track, di, Mode) == 0;
        }), "Chroma factory presets select Spectral");
        e.DeviceSetParam(track, di, Mode, 1);
        var positional = new PresetDocument { Type = "builtin-effect", BuiltinKind = 27, Params = expected.Take(24).ToArray() };
        yield return (PresetService.ApplyInPlace(positional, e, track, di).Length == 0 && e.DeviceGetParam(track, di, Mode) == 0,
            "Chroma legacy positional presets select Spectral");
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, 48000, i => { double s = .1 * Math.Sin(2 * Math.PI * 440 * i / 48000); return (s, s); });
            using var render = new NotaEngine(); int t = render.AddAudioTrack(); render.AddAudioClip(t, wav, 0); int device = render.AddBuiltinDevice(t, 27);
            render.DeviceSetParam(t, device, 3, 0); // Isolate tuning from resonance.
            render.Play(); var block = new float[512 * 2]; double energy = 0; bool finite = true;
            double cosine = 0, sine = 0, steadyEnergy = 0;
            int samples = 0;
            for (int n = 0; n < 90; n++)
            {
                render.RenderOffline(block, 512, 48000);
                foreach (float v in block) { finite &= float.IsFinite(v); energy += v * v; }
                if (n < 45) continue;
                for (int i = 0; i < 512; i++)
                {
                    double v = block[2 * i], phase = Math.Tau * 415.30469758 * samples++ / 48000;
                    cosine += v * Math.Cos(phase);
                    sine += v * Math.Sin(phase);
                    steadyEnergy += v * v;
                }
            }
            yield return (finite && energy > 1, "Color renders audible finite audio through the C ABI");
            yield return (2 * (cosine * cosine + sine * sine) > .97 * samples * steadyEnergy,
                "Chroma holds the lower C minor note for a 440 Hz boundary tone through the C ABI");
            var spectrum = new float[1026]; int count = render.DeviceScope(t, device, spectrum, spectrum.Length);
            yield return (count == 1026 && spectrum.Skip(2).Any(v => v > 0.1f), "Color publishes its live input spectrum through the C ABI");
        }
        finally { File.Delete(wav); }
        foreach (var result in RenderChord()) yield return result;
    }

    private static IEnumerable<(bool, string)> RenderChord()
    {
        const int rate = 48000, blockSize = 512;
        double Freq(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);
        double[] source = [Freq(60), Freq(64), Freq(67)];
        double[] check = [Freq(60), Freq(63), Freq(64), Freq(67)];
        string wav = Path.Combine(Path.GetTempPath(), "nota-poly-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, rate, i =>
            {
                double sum = 0;
                foreach (double f in source)
                    for (int h = 1; h <= 5; h++) sum += .045 / h * Math.Sin(Math.Tau * f * h * i / rate);
                return (sum, sum);
            });
            using var engine = new NotaEngine();
            int track = engine.AddAudioTrack();
            engine.AddAudioClip(track, wav, 0);
            int device = engine.AddBuiltinDevice(track, 27);
            engine.DeviceSetParam(track, device, Mode, 1);
            engine.Play();
            var block = new float[blockSize * 2];
            var cosine = new double[check.Length];
            var sine = new double[check.Length];
            int count = 0;
            for (int b = 0; b < 90; b++)
            {
                engine.RenderOffline(block, blockSize, rate);
                if (b < 45) continue;
                for (int i = 0; i < blockSize; i++, count++)
                    for (int k = 0; k < check.Length; k++)
                    {
                        double phase = Math.Tau * check[k] * count / rate;
                        cosine[k] += Math.Cos(phase) * block[2 * i];
                        sine[k] += Math.Sin(phase) * block[2 * i];
                    }
            }
            double Level(int k) => 2 * Math.Sqrt(cosine[k] * cosine[k] + sine[k] * sine[k]) / count;
            yield return (Level(0) > .025 && Level(1) > .025 && Level(2) < .010 && Level(3) > .025,
                $"Chroma Polyphonic renders C–Eb–G through the C ABI (C {Level(0):F3}, Eb {Level(1):F3}, E {Level(2):F3}, G {Level(3):F3})");
            var spectrum = new float[1026];
            yield return (engine.DeviceScope(track, device, spectrum, spectrum.Length) == 1026 &&
                spectrum[1] == 2048, "Chroma Polyphonic reports the same stereo scope and latency");
        }
        finally { File.Delete(wav); }
    }
}
