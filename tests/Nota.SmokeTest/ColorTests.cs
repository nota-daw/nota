// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ColorTests
{
    private const int Kind = 27, ActiveParams = 24, RetiredMode = 24;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int track = e.AddAudioTrack(), di = e.AddBuiltinDevice(track, Kind);
        yield return (di >= 0 && e.DeviceName(track, di) == "Nota Chroma" && e.DeviceParamCount(track, di) == ActiveParams &&
            e.TrackLatencySamples(track) == 1024, "Chroma exposes 24 HQ parameters and 1024 samples of latency");
        yield return (e.DeviceGetParam(track, di, 21) == 0 && e.DeviceGetParam(track, di, 22) == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 23) - .25f) < 1e-6, "Chroma Morph and Gate default off with -60 dBFS threshold");
        float[] expected = Enumerable.Range(0, ActiveParams).Select(p => (float)((p * 7 % 19) / 18.0)).ToArray();
        for (int p = 0; p < expected.Length; p++) e.DeviceSetParam(track, di, p, expected[p]);
        int clone = e.DuplicateTrack(track);
        bool Same(NotaEngine engine, int t, int d, int count) =>
            Enumerable.Range(0, count).All(p => Math.Abs(engine.DeviceGetParam(t, d, p) - expected[p]) < 1e-6);

        yield return (Same(e, clone, di, ActiveParams), "Chroma clone retains every active parameter");
        string dir = Path.Combine(Path.GetTempPath(), "nota-color-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>()), dir, e);
            using var restored = new NotaEngine();
            var warnings = ProjectService.Apply(ProjectService.Load(dir), restored, dir);
            bool same = restored.TryGetTrackInfo(0, out var info) && restored.TrackDeviceBuiltinKind(info.Id, 0) == Kind &&
                Same(restored, info.Id, 0, ActiveParams);
            yield return (warnings.Count == 0 && same, "Chroma project round-trip retains its parameter values");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        foreach (int count in new[] { 21, 24, 25 })
        {
            if (count == 25)
            {
                int lane = e.AddAutomationLane(track, AutomationTarget.DeviceParam, di, RetiredMode);
                e.SetAutomationPoints(track, lane, [new AutomationPoint(0, 1f)]);
            }
            var legacy = ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>());
            foreach (var sourceTrack in legacy.Tracks)
                foreach (var dev in sourceTrack.Devices.Where(d => d.BuiltinKind == Kind))
                    dev.Params = count == 25 ? [.. dev.Params, 1f] : dev.Params.Take(count).ToArray();
            using var old = new NotaEngine();
            var warnings = ProjectService.Apply(legacy, old, dir);
            bool ok = old.TryGetTrackInfo(0, out var tr) && Same(old, tr.Id, 0, Math.Min(count, ActiveParams)) &&
                string.IsNullOrEmpty(old.DeviceParamName(tr.Id, 0, RetiredMode)) &&
                old.TrackLatencySamples(tr.Id) == 1024;
            if (count == 21 && ok)
                ok = old.DeviceGetParam(tr.Id, 0, 21) == 0 && old.DeviceGetParam(tr.Id, 0, 22) == 0 &&
                    Math.Abs(old.DeviceGetParam(tr.Id, 0, 23) - .25f) < 1e-6;
            yield return (ok && warnings.Count == 0, $"Chroma loads legacy {count}-parameter state in HQ");
            if (count == 25)
                yield return (ok && old.AutomationLaneCount(tr.Id) == 1 &&
                    old.AutomationLaneInfo(tr.Id, 0) is { ParamIndex: RetiredMode, PointCount: 1 } &&
                    old.DeviceGetParam(tr.Id, 0, RetiredMode) == 0,
                    "Chroma preserves legacy Mode automation data at an inactive parameter index");
        }

        var catalog = new FactoryPresetCatalog();
        var presets = catalog.All().Where(p => p.Id.StartsWith("color/", StringComparison.Ordinal)).ToArray();
        yield return (presets.Length == 6 && presets.All(p => catalog.ApplyInPlace(e, p.Id, track, di).Length == 0),
            "Chroma applies all six factory presets");
        var positional = new PresetDocument { Type = "builtin-effect", BuiltinKind = Kind, Params = [.. expected, 1f] };
        yield return (PresetService.ApplyInPlace(positional, e, track, di).Length == 0 && Same(e, track, di, ActiveParams),
            "Chroma applies a legacy positional preset with Polyphonic selected");
        var named = new PresetDocument { Type = "builtin-effect", BuiltinKind = Kind, NamedParams = new() { ["Mode"] = 1f, ["Morph"] = .4f } };
        yield return (PresetService.ApplyInPlace(named, e, track, di).Length == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 21) - .4f) < 1e-6 && e.DeviceGetParam(track, di, RetiredMode) == 0,
            "Chroma ignores the retired named Mode while applying other parameters");

        foreach (var result in RenderHq()) yield return result;
    }

    private static IEnumerable<(bool, string)> RenderHq()
    {
        const int rate = 48000, blockSize = 512;
        const double source = 277.1826309768721, target = source * 242 / 256;
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-hq-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, rate, i =>
            {
                double sample = .1 * Math.Sin(Math.Tau * source * i / rate);
                return (sample, sample);
            });
            using var engine = new NotaEngine();
            int track = engine.AddAudioTrack();
            engine.AddAudioClip(track, wav, 0);
            int device = engine.AddBuiltinDevice(track, Kind);
            engine.DeviceSetParam(track, device, 3, 0);
            engine.DeviceSetParam(track, device, 1, 1);
            for (int p = 9; p < 21; p++) engine.DeviceSetParam(track, device, p, p == 9 ? 1 : 0);
            engine.Play();

            var block = new float[blockSize * 2];
            var targetSum = System.Numerics.Complex.Zero;
            var sourceSum = System.Numerics.Complex.Zero;
            bool finite = true;
            int count = 0;
            for (int b = 0; b < 90; b++)
            {
                engine.RenderOffline(block, blockSize, rate);
                finite &= block.All(float.IsFinite);
                if (b < 45) continue;
                for (int i = 0; i < blockSize; i++, count++)
                {
                    targetSum += block[2 * i] * System.Numerics.Complex.FromPolarCoordinates(1, Math.Tau * target * count / rate);
                    sourceSum += block[2 * i] * System.Numerics.Complex.FromPolarCoordinates(1, Math.Tau * source * count / rate);
                }
            }
            double tuned = 2 * targetSum.Magnitude / count, original = 2 * sourceSum.Magnitude / count;
            yield return (finite && tuned > .015 && original < .35 * tuned,
                $"Chroma HQ shifts C sharp down through the C ABI (tuned {tuned:F4}, source {original:F4})");
            var spectrum = new float[1026];
            yield return (engine.DeviceScope(track, device, spectrum, spectrum.Length) == 1026 && spectrum[1] == 1024 &&
                spectrum.AsSpan(2, 512).ToArray().Any(v => v > .3f) && spectrum.AsSpan(514, 512).ToArray().Any(v => v > .3f),
                "Chroma publishes live Dry and Tuned spectra through the C ABI");
        }
        finally { File.Delete(wav); }
    }
}
