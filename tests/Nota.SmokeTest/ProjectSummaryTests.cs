// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's Projects tab (spec 092, #582): what is read from a project.json without
// opening it (tempo, key, tracks, length — MIDI clips, warped and unwarped audio), the
// cache that keeps a folder of projects from being read twice, and the tab's order and
// open-project mark. Part of `--browser-files`.

using Nota.Application.Browser;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class ProjectSummaryTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota092p-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // 120 BPM; a MIDI clip to beat 16; a warped clip 20 → 28; an unwarped 2 s WAV at 30 (4 beats) → 34.
            string a = Bundle(dir, "Alpha", """
                { "transport": { "bpm": 120, "key": "Am" },
                  "tracks": [
                    { "type": 1, "midiClips": [ { "startBeat": 0, "lengthBeats": 16 } ] },
                    { "type": 0, "audioClips": [
                        { "sample": "samples/x.wav", "startBeat": 20, "warpEnabled": 1, "warpBeats": 32, "warpPlayStart": 2, "warpPlayEnd": 10 },
                        { "sample": "samples/x.wav", "startBeat": 30, "lengthFrames": 0 } ] },
                    { "type": 2 } ] }
                """);
            Directory.CreateDirectory(Path.Combine(a, "samples"));
            WavWriter.WriteSine(Path.Combine(a, "samples", "x.wav"), 2.0, 220, 44100);

            var s = ProjectSummaryReader.Read(a);
            yield return (s is { Bpm: 120, Key: "Am", TrackCount: 2 }, $"summary: tempo, key and tracks (returns aren't counted) [{s}]");
            yield return (s is not null && Math.Abs(s.LengthBeats - 34) < 0.01 && s.LengthText == "0:17",
                $"summary: length runs to the end of the last clip — MIDI, warped and unwarped audio ({s?.LengthBeats:0.##} beats, {s?.LengthText})");

            // The same 2 s clip an octave up plays in 1 s (2 beats), as the engine resamples it: → 32.
            string up = Bundle(dir, "Up", """
                { "transport": { "bpm": 120 },
                  "tracks": [ { "type": 0, "audioClips": [ { "sample": "samples/x.wav", "startBeat": 30, "lengthFrames": 0, "pitchSemitones": 12 } ] } ] }
                """);
            Directory.CreateDirectory(Path.Combine(up, "samples"));
            WavWriter.WriteSine(Path.Combine(up, "samples", "x.wav"), 2.0, 220, 44100);
            var su = ProjectSummaryReader.Read(up);
            yield return (su is not null && Math.Abs(su.LengthBeats - 32) < 0.01, $"summary: a transposed unwarped clip is as long as it plays ({su?.LengthBeats:0.##} beats)");
            Directory.Delete(up, true);   // the Projects list below counts this folder's bundles

            string bad = Bundle(dir, "Broken", "{ not json");
            yield return (ProjectSummaryReader.Read(bad) is null, "summary: an unreadable project gives none");

            string cacheFile = Path.Combine(dir, "cache.json");
            var reader = new ProjectSummaryReader(cacheFile);
            var first = reader.Get(a);
            var manifest = Path.Combine(a, ProjectDates.ManifestName);
            var stamp = File.GetLastWriteTimeUtc(manifest);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"bpm\": 120", "\"bpm\": 99.5"));
            File.SetLastWriteTimeUtc(manifest, stamp);
            // Same write time but a different size: read again.
            var changed = new ProjectSummaryReader(cacheFile).Get(a);
            yield return (first?.Bpm == 120 && changed?.Bpm == 99.5, "cache: a project that changed on disk is read again");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"bpm\": 99.5", "\"bpm\": 777"));
            File.SetLastWriteTimeUtc(manifest, stamp);
            var c1 = new ProjectSummaryReader(cacheFile).Get(a);   // reads 777, caches it under this stamp
            File.Delete(Path.Combine(a, "samples", "x.wav"));      // the cache needn't look at the audio again
            var c2 = new ProjectSummaryReader(cacheFile).Get(a);
            yield return (c1?.Bpm == 777 && c2 is { Bpm: 777 } && Math.Abs(c2.LengthBeats - c1.LengthBeats) < 0.001,
                "cache: an unchanged project is answered from the cache");

            // ---- the Projects tab ----
            string b = Bundle(dir, "Bravo", """{ "transport": { "bpm": 90 }, "tracks": [] }""");
            string c = Bundle(dir, "charlie", """{ "transport": { "bpm": 100 }, "tracks": [] }""");
            var now = DateTime.Now;
            File.SetLastWriteTime(Path.Combine(a, ProjectDates.ManifestName), now.AddHours(-3));
            File.SetLastWriteTime(Path.Combine(b, ProjectDates.ManifestName), now.AddHours(-1));
            File.SetLastWriteTime(Path.Combine(c, ProjectDates.ManifestName), now.AddHours(-2));
            File.SetLastWriteTime(Path.Combine(bad, ProjectDates.ManifestName), now.AddHours(-4));

            string settingsFile = Path.Combine(dir, "settings.json");
            File.WriteAllText(settingsFile, $"{{ \"ProjectsFolder\": {System.Text.Json.JsonSerializer.Serialize(dir)} }}");
            var settings = new SettingsService(settingsFile);
            var vm = new BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), new FactoryPresetCatalog(),
                settings: settings, projectSummaries: new ProjectSummaryReader(Path.Combine(dir, "cache2.json")));
            var names = vm.Projects.Select(p => p.Name).ToList();
            yield return (names.SequenceEqual(new[] { "Bravo", "charlie", "Alpha", "Broken" }), $"projects: newest first [{string.Join(", ", names)}]");
            yield return (vm.Projects[0].Sub == ProjectDates.Format(now.AddHours(-1)), $"projects: each row carries its save date ({vm.Projects[0].Sub})");

            vm.ProjectSummariesTask.Wait(TimeSpan.FromSeconds(10));
            yield return (vm.Projects.First(p => p.Name == "Bravo").Summary?.Bpm == 90 && vm.Projects.First(p => p.Name == "Broken").Summary is null,
                "projects: details fill in from the background read; a broken project keeps just its date");

            vm.ProjectsSort = ProjectsSortOrder.Name;
            var byName = vm.Projects.Select(p => p.Name).ToList();
            yield return (byName.SequenceEqual(new[] { "Alpha", "Bravo", "Broken", "charlie" }) && new SettingsService(settingsFile).Current.ProjectsSort == ProjectsSortOrder.Name,
                $"projects: sorting by name works and is remembered [{string.Join(", ", byName)}]");

            vm.OpenProjectPath = c + Path.DirectorySeparatorChar;
            yield return (vm.Projects.Single(p => p.IsOpen).Name == "charlie", "projects: the open project is marked");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static string Bundle(string dir, string name, string json)
    {
        var b = Path.Combine(dir, name + ".nota");
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(b, ProjectDates.ManifestName), json);
        return b;
    }
}
