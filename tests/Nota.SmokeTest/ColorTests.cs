// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using Nota.Application;
using Nota.Infrastructure;
namespace Nota.SmokeTest;
internal static class ColorTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int track = e.AddAudioTrack(), di = e.AddBuiltinDevice(track, 27);
        yield return (di >= 0 && e.DeviceName(track, di) == "Nota Chroma" && e.DeviceParamCount(track, di) == 24, "Color identity and parameter contract");
        yield return (e.DeviceGetParam(track, di, 21) == 0 && e.DeviceGetParam(track, di, 22) == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 23) - .25f) < 1e-6, "Color spectral controls default off with -60 dBFS threshold");
        float[] expected = Enumerable.Range(0, 24).Select(p => (float)((p * 7 % 19) / 18.0)).ToArray();
        for (int p = 0; p < expected.Length; p++) e.DeviceSetParam(track, di, p, expected[p]);
        int clone = e.DuplicateTrack(track);
        yield return (Enumerable.Range(0, 24).All(p => Math.Abs(e.DeviceGetParam(clone, di, p) - expected[p]) < 1e-6), "Color clone retains every parameter");
        string dir = Path.Combine(Path.GetTempPath(), "nota-color-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>()), dir, e);
            using var restored = new NotaEngine();
            var warnings = ProjectService.Apply(ProjectService.Load(dir), restored, dir);
            bool same = restored.TryGetTrackInfo(0, out var info) && restored.TrackDeviceBuiltinKind(info.Id, 0) == 27 &&
                Enumerable.Range(0, 24).All(p => Math.Abs(restored.DeviceGetParam(info.Id, 0, p) - expected[p]) < 1e-6);
            yield return (warnings.Count == 0 && same, "Color project round-trip retains kind and complete custom mask");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        var legacy = ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>());
        foreach (var tr in legacy.Tracks)
            foreach (var dev in tr.Devices.Where(d => d.BuiltinKind == 27)) dev.Params = dev.Params.Take(21).ToArray();
        using (var old = new NotaEngine())
        {
            var warnings = ProjectService.Apply(legacy, old, dir);
            bool ok = old.TryGetTrackInfo(0, out var tr) &&
                Enumerable.Range(0, 21).All(p => Math.Abs(old.DeviceGetParam(tr.Id, 0, p) - expected[p]) < 1e-6) &&
                old.DeviceGetParam(tr.Id, 0, 21) == 0 && old.DeviceGetParam(tr.Id, 0, 22) == 0 &&
                Math.Abs(old.DeviceGetParam(tr.Id, 0, 23) - .25f) < 1e-6;
            yield return (ok && warnings.Count == 0, "Color loads legacy 21-parameter state with spectral controls off");
        }
        var catalog = new FactoryPresetCatalog();
        var presets = catalog.All().Where(p => p.Id.StartsWith("color/", StringComparison.Ordinal)).ToArray();
        yield return (presets.Length == 3, "Color has three factory presets");
        yield return (presets.All(p => catalog.ApplyInPlace(e, p.Id, track, di).Length == 0), "Color presets apply");
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, 48000, i => { double s = .1 * Math.Sin(2 * Math.PI * 440 * i / 48000); return (s, s); });
            using var render = new NotaEngine(); int t = render.AddAudioTrack(); render.AddAudioClip(t, wav, 0); int device = render.AddBuiltinDevice(t, 27);
            render.Play(); var block = new float[512 * 2]; double energy = 0; bool finite = true;
            for (int n = 0; n < 90; n++) { render.RenderOffline(block, 512, 48000); foreach (float v in block) { finite &= float.IsFinite(v); energy += v * v; } }
            yield return (finite && energy > 1, "Color renders audible finite audio through the C ABI");
            var spectrum = new float[1026]; int count = render.DeviceScope(t, device, spectrum, spectrum.Length);
            yield return (count == 1026 && spectrum.Skip(2).Any(v => v > 0.1f), "Color publishes its live input spectrum through the C ABI");
        }
        finally { File.Delete(wav); }
    }
}
