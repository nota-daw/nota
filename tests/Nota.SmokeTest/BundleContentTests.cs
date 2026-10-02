// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Content-addressed `.nota` bundles: samples and plugin states are named by a hash of
// their content, identical content shares a file, a re-save skips what's on disk, names
// survive a reload, legacy `sample-N.wav` bundles migrate, and stale files are pruned.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --bundle`.

using System.Text.RegularExpressions;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class BundleContentTests
{
    private static readonly Regex HashWav = new(@"^[0-9a-f]{32}\.wav$");
    private static readonly Regex HashBin = new(@"^[0-9a-f]{32}\.bin$");

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string tmp = Path.GetTempPath();
        string wav = Path.Combine(tmp, "nota_smoke_bundle_sine.wav");
        WavWriter.WriteSine(wav, seconds: 0.5, freq: 330, sampleRate: 44100);
        string dir = Path.Combine(tmp, "nota-smoke-bundle-" + Guid.NewGuid().ToString("N") + ".nota");
        string legacy = Path.Combine(tmp, "nota-smoke-bundle-legacy-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        try
        {
            using var src = new NotaEngine();
            int aTrk = src.AddAudioTrack();
            src.AddAudioClipEx(aTrk, wav, startBeat: 0, sourceOffsetFrames: 0, lengthFrames: 0, gain: 1f);
            src.AddAudioClipEx(aTrk, wav, startBeat: 4, sourceOffsetFrames: 0, lengthFrames: 0, gain: 1f);
            src.AddSamplerTrack(wav, rootNote: 60, loop: false);
            src.AddInstrumentTrack();
            src.AddInstrumentTrack();   // two default Synths: identical state blobs

            var w = new List<string>();
            var doc = ProjectService.Capture(src, transport, w);
            yield return (w.Count == 0, $"capture without warnings ({w.Count})");
            ProjectService.Save(doc, dir, src);

            string samples = Path.Combine(dir, "samples"), states = Path.Combine(dir, "plugin-states");
            var wavs = Directory.GetFiles(samples).Select(Path.GetFileName).ToArray();
            yield return (wavs.Length == 1 && HashWav.IsMatch(wavs[0]!),
                $"one decode of one file → one content-named sample ({string.Join(", ", wavs)})");
            var bins = Directory.Exists(states) ? Directory.GetFiles(states).Select(Path.GetFileName).ToArray() : [];
            yield return (bins.Length >= 1 && bins.All(b => HashBin.IsMatch(b!)), $"plugin states are content-named ({bins.Length})");
            int synthRefs = Regex.Matches(File.ReadAllText(Path.Combine(dir, ProjectService.ManifestName)), "plugin-states/").Count;
            yield return (synthRefs > bins.Length, $"identical states share a file ({synthRefs} refs → {bins.Length} files)");

            // Re-save with nothing changed: the binaries are not rewritten.
            string wavPath = Path.Combine(samples, wavs[0]!);
            var stamp = DateTime.UtcNow.AddHours(-1);
            File.SetLastWriteTimeUtc(wavPath, stamp);
            ProjectService.Save(ProjectService.Capture(src, transport, new List<string>()), dir, src);
            yield return (File.GetLastWriteTimeUtc(wavPath) == stamp, "re-save skips a sample already on disk");

            // Stray files: an old-style sample and a temp are pruned, a foreign file is kept.
            File.WriteAllText(Path.Combine(samples, "sample-99.wav"), "x");
            File.WriteAllText(Path.Combine(samples, wavs[0] + ".tmp"), "x");
            File.WriteAllText(Path.Combine(samples, "notes.txt"), "x");
            ProjectService.Save(ProjectService.Capture(src, transport, new List<string>()), dir, src);
            yield return (!File.Exists(Path.Combine(samples, "sample-99.wav")) && !File.Exists(Path.Combine(samples, wavs[0] + ".tmp")),
                "unreferenced samples and temp files are pruned");
            yield return (File.Exists(Path.Combine(samples, "notes.txt")), "files a save didn't write are left alone");

            // Reload, re-save: names are reused (seeded from the file names), nothing new appears.
            string manifestBefore = File.ReadAllText(Path.Combine(dir, ProjectService.ManifestName));
            using (var dst = new NotaEngine())
            {
                var aw = ProjectService.Apply(ProjectService.Load(dir), dst, dir);
                yield return (aw.Count == 0 && dst.TrackCount == 4, $"content-named bundle reloads ({aw.Count} warnings, {dst.TrackCount} tracks)");
                ProjectService.Save(ProjectService.Capture(dst, transport, new List<string>()), dir, dst);
            }
            string manifestAfter = File.ReadAllText(Path.Combine(dir, ProjectService.ManifestName));
            yield return (Regex.Matches(manifestAfter, "samples/[0-9a-f]{32}\\.wav").Cast<Match>().Select(m => m.Value).Distinct()
                    .SequenceEqual(Regex.Matches(manifestBefore, "samples/[0-9a-f]{32}\\.wav").Cast<Match>().Select(m => m.Value).Distinct()),
                "sample names survive a reload + re-save");
            yield return (Directory.GetFiles(samples, "*.wav").Length == 1, "reload + re-save adds no sample files");

            // Legacy bundle (sample-N.wav, no seeding possible): the audio hashes back to the same name.
            CopyDir(dir, legacy);
            string lSamples = Path.Combine(legacy, "samples");
            File.Move(Path.Combine(lSamples, wavs[0]!), Path.Combine(lSamples, "sample-7.wav"));
            string lManifest = Path.Combine(legacy, ProjectService.ManifestName);
            File.WriteAllText(lManifest, File.ReadAllText(lManifest).Replace("samples/" + wavs[0], "samples/sample-7.wav"));
            using (var le = new NotaEngine())
            {
                var lw = ProjectService.Apply(ProjectService.Load(legacy), le, legacy);
                yield return (lw.Count == 0, $"legacy-named bundle loads ({lw.Count} warnings)");
                ProjectService.Save(ProjectService.Capture(le, transport, new List<string>()), legacy, le);
            }
            var lWavs = Directory.GetFiles(lSamples, "*.wav").Select(Path.GetFileName).ToArray();
            yield return (lWavs.Length == 1 && lWavs[0] == wavs[0],
                $"legacy sample migrates to the same content name ({string.Join(", ", lWavs)})");

            // A change-detection capture is never saved.
            var cheap = ProjectService.Capture(src, transport, new List<string>(), contentNames: false);
            bool threw = false;
            try { ProjectService.Save(cheap, Path.Combine(tmp, "nota-smoke-never-" + Guid.NewGuid().ToString("N")), src); }
            catch (InvalidOperationException) { threw = true; }
            yield return (threw, "saving a session-named (change-detection) capture is refused");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { Directory.Delete(legacy, true); } catch { }
            try { File.Delete(wav); } catch { }
        }
    }

    private static void CopyDir(string from, string to)
    {
        foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(d.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(from, to));
    }
}
