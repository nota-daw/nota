// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Browser preset audition: every factory preset (and every bare built-in device) renders an
// audible, finite phrase offline; renders cancel; the engine caches and replays them.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --audition`.

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AuditionTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        using var engine = new NotaEngine();
        var factory = new FactoryPresetCatalog();
        var audition = new PresetAudition(factory, new DrumKitService());

        // (plan, frames, rms, peak, ms) per subject.
        (AuditionPlan? Plan, long Frames, double Rms, float Peak, double Ms, string Why) Render(AuditionSubject s,
            string src = AuditionTrackIds.Auto, string? sample = null)
        {
            var plan = audition.Plan(s, src, sample, out var why);
            if (plan is null) return (null, 0, 0, 0, 0, why);
            var sw = Stopwatch.StartNew();
            using var rig = engine.CreateAuditionRig();
            if (!plan.Build(rig)) return (plan, 0, 0, 0, 0, "build failed");
            long frames = rig.Render(plan.Notes, plan.Bpm, plan.PhraseBeats, plan.TailSeconds, plan.Rolling);
            double ms = sw.Elapsed.TotalMilliseconds;
            var peaks = new float[1024];
            int n = rig.ReadPeaks(peaks, 512);
            double sum = 0; float peak = 0;
            bool finite = true;
            for (int i = 0; i < n * 2; i++)
            {
                if (!float.IsFinite(peaks[i])) finite = false;
                sum += peaks[i] * (double)peaks[i];
                peak = Math.Max(peak, Math.Abs(peaks[i]));
            }
            return (plan, frames, finite ? Math.Sqrt(sum / Math.Max(1, n * 2)) : double.NaN, peak, ms, "");
        }

        // Every factory preset renders something audible and finite, within a sane time.
        var silent = new List<string>();
        var slow = new List<string>();
        int count = 0;
        double total = 0, worst = 0;
        string worstId = "";
        // Silent by design: a mute, and freezes that hold an empty buffer (they sound once fed).
        var silentByDesign = new HashSet<string> { "util/Mute", "reverb/Stone Hold", "delay/Tape Hold" };
        foreach (var fp in factory.All())
        {
            if (silentByDesign.Contains(fp.Id)) continue;
            var r = Render(new AuditionSubject(AuditionSubjectKind.Preset, fp.BuiltinKind, "factory:" + fp.Id, fp.DisplayName));
            count++;
            if (r.Plan is null || r.Frames <= 0 || !double.IsFinite(r.Rms) || r.Peak < 0.003f)
                silent.Add($"{fp.Id} ({(r.Plan is null ? r.Why : $"peak {r.Peak:0.0000}")})");
            total += r.Ms;
            if (r.Ms > worst) { worst = r.Ms; worstId = fp.Id; }
            if (r.Ms > 1500) slow.Add($"{fp.Id} {r.Ms:0} ms");
        }
        yield return (silent.Count == 0,
            $"all {count} factory presets audition audibly ({silent.Count} silent: {string.Join(", ", silent.Take(8))})");
        yield return (slow.Count == 0,
            $"renders stay quick: avg {total / Math.Max(1, count):0} ms, worst {worst:0} ms ({worstId}){(slow.Count > 0 ? " · slow: " + string.Join(", ", slow.Take(5)) : "")}");

        // Bare devices audition their default sound; racks say why they can't.
        var devSilent = new List<string>();
        foreach (int k in new[] { 0, 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })
        {
            var r = Render(new AuditionSubject(AuditionSubjectKind.Instrument, k, "", ""));
            if (r.Frames <= 0 || r.Peak < 0.003f) devSilent.Add($"inst {k}");
        }
        for (int k = 0; k <= 25; k++)
        {
            if (k == 5) continue;
            var r = Render(new AuditionSubject(AuditionSubjectKind.AudioEffect, k, "", ""));
            if (r.Frames <= 0 || r.Peak < 0.003f) devSilent.Add($"fx {k}");
        }
        for (int k = 0; k <= 5; k++)
        {
            var r = Render(new AuditionSubject(AuditionSubjectKind.MidiEffect, k, "", ""));
            if (r.Frames <= 0 || r.Peak < 0.003f) devSilent.Add($"midi {k}");
        }
        yield return (devSilent.Count == 0, $"every built-in device auditions its default sound ({string.Join(", ", devSilent)})");
        var rack = audition.Plan(new AuditionSubject(AuditionSubjectKind.Instrument, 3, "", ""), AuditionTrackIds.Auto, null, out var rackWhy);
        yield return (rack is null && rackWhy.Length > 0, "the Instrument Rack declines with a reason");

        // Every factory kit plays its groove, as a Drum Rack and as a Nota Rhythm's voices.
        var kitSilent = new List<string>();
        double kitWorst = 0;
        foreach (var kit in Nota.Infrastructure.Kits.KitCatalog.All)
            foreach (var prefix in new[] { "kit:", "rhythmkit:" })
            {
                var r = Render(new AuditionSubject(AuditionSubjectKind.Preset, -1, prefix + kit.Id, kit.Name));
                kitWorst = Math.Max(kitWorst, r.Ms);
                if (r.Plan is null || r.Frames <= 0 || r.Peak < 0.01f) kitSilent.Add($"{prefix}{kit.Id} ({r.Why}{r.Peak:0.000})");
            }
        yield return (kitSilent.Count == 0,
            $"all {Nota.Infrastructure.Kits.KitCatalog.All.Count} kits audition as Drum Rack and Rhythm (worst {kitWorst:0} ms; silent: {string.Join(", ", kitSilent.Take(6))})");

        // Effect tracks: Auto picks per device; every track renders; the key tells them apart.
        var comp = audition.Plan(new AuditionSubject(AuditionSubjectKind.AudioEffect, 1, "", ""), AuditionTrackIds.Auto, null, out _);
        var verb = audition.Plan(new AuditionSubject(AuditionSubjectKind.AudioEffect, 2, "", ""), AuditionTrackIds.Auto, null, out _);
        var eq = audition.Plan(new AuditionSubject(AuditionSubjectKind.AudioEffect, 0, "", ""), AuditionTrackIds.Auto, null, out _);
        yield return (comp?.TrackId == "drums" && verb?.TrackId == "keys" && eq?.TrackId == "pop" && comp.IsEffect,
            "auto track: a compressor hears the drums, a reverb the keys, an EQ the pop mix");
        var keys = new HashSet<string>();
        var bad = new List<string>();
        foreach (var t in audition.Tracks)
        {
            var sw = Stopwatch.StartNew();
            var r = Render(new AuditionSubject(AuditionSubjectKind.AudioEffect, 2, "", ""), t.Id);
            if (r.Frames <= 0 || r.Peak < 0.05f || r.Plan!.TrackId != t.Id) bad.Add($"{t.Id} ({r.Peak:0.000})");
            keys.Add(r.Plan!.Key);
            Console.WriteLine($"     track {t.Id,-10} {r.Frames / 48000.0:0.0} s, first render {sw.Elapsed.TotalMilliseconds:0} ms");
        }
        var noSample = audition.Plan(new AuditionSubject(AuditionSubjectKind.AudioEffect, 2, "", ""), AuditionTrackIds.Sample, null, out _);
        yield return (bad.Count == 0 && keys.Count == audition.Tracks.Count && noSample?.TrackId == "keys",
            $"all {audition.Tracks.Count} demo tracks render under their own key; Sample without a sample falls back to Auto ({string.Join(", ", bad)})");
        {
            var sw = Stopwatch.StartNew();
            var r = Render(new AuditionSubject(AuditionSubjectKind.AudioEffect, 1, "", ""), "house");
            yield return (r.Frames > 0 && sw.Elapsed.TotalMilliseconds < 300,
                $"a demo track renders once and is reused (house again: {sw.Elapsed.TotalMilliseconds:0} ms)");
        }

        // A render cancels from another thread within a block.
        {
            var plan = audition.Plan(new AuditionSubject(AuditionSubjectKind.Preset, 20, "factory:chamber/" + factory.All().First(p => p.Id.StartsWith("chamber/")).DisplayName, ""),
                "pop", null, out _)!;
            using var rig = engine.CreateAuditionRig();
            plan.Build(rig);
            var t = Task.Run(() => rig.Render(plan.Notes, plan.Bpm, plan.PhraseBeats, 60, plan.Rolling));
            Thread.Sleep(2);
            rig.Cancel();
            bool done = t.Wait(2000);
            yield return (done && t.Result == -1, $"cancel stops a running render ({(done ? t.Result : "hung")})");
        }

        // The engine caches a rendered audition and plays it back by key.
        {
            var plan = audition.Plan(new AuditionSubject(AuditionSubjectKind.Preset, 0, "factory:synth/Warm Pad", "Warm Pad"),
                AuditionTrackIds.Auto, null, out _)!;
            using var rig = engine.CreateAuditionRig();
            plan.Build(rig);
            rig.Render(plan.Notes, plan.Bpm, plan.PhraseBeats, plan.TailSeconds, plan.Rolling);
            bool before = engine.IsAuditionCached(plan.Key);
            engine.StoreAudition(rig, plan.Key);
            bool after = engine.IsAuditionCached(plan.Key);
            bool plays = engine.PreviewAuditionAt(plan.Key, 0.5) && engine.IsPreviewActive;
            bool missing = !engine.PreviewAuditionAt("factory:nope", 0);
            engine.StopPreview();
            yield return (!before && after && plays && missing && plan.Phrase == "pad chord",
                "a stored audition replays by key (a pad plays its pad chord); an unknown key reports a miss");
        }
    }
}
