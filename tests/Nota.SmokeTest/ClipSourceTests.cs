// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Where a clip's audio came from (spec 092, #581): the engine wrapper remembers the file a
// clip or Session take was loaded from, a split keeps it, a project saves it next to its own
// copy and gives it back on reload, and neither a project's copy nor a render in the temp
// folder counts as a source. Part of `--browser-files`.

using System.Text.Json;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ClipSourceTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // Outside the temp folder: renders there are deliberately not sources.
        string lib = Path.Combine(AppContext.BaseDirectory, "nota092-src-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(lib);
        string wav = Path.Combine(lib, "Kick 01.wav");
        WavWriter.WriteSine(wav, 0.5, 110, 44100);
        string scratch = Path.Combine(Path.GetTempPath(), "nota092-render-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
        WavWriter.WriteSine(scratch, 0.5, 220, 44100);
        string bundle = Path.Combine(Path.GetTempPath(), "nota092-" + Guid.NewGuid().ToString("N")[..8] + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        try
        {
            using var src = new NotaEngine();
            int trk = src.AddAudioTrack();
            int c0 = src.AddAudioClip(trk, wav, 0);
            src.TryGetAudioClipInfo(trk, c0, out var a0);
            yield return (src.SampleSource(a0.SampleId) == Path.GetFullPath(wav), "source: an audio clip remembers the file it was made from");

            src.SplitClip(trk, c0, 0.25);
            bool both = src.TryGetAudioClipInfo(trk, 0, out var s0) && src.TryGetAudioClipInfo(trk, 1, out var s1)
                        && src.SampleSource(s0.SampleId) == wav && src.SampleSource(s1.SampleId) == wav;
            yield return (both, "source: both halves of a split still lead to it");

            int r = src.AddAudioClip(trk, scratch, 8);
            src.TryGetAudioClipInfo(trk, r, out var ri);
            yield return (src.SampleSource(ri.SampleId) == "", "source: a render in the temp folder isn't one");

            yield return (src.AddSessionAudioFile(trk, 0, wav) && src.TryGetSessionAudioSlot(trk, 0, out var sa) && src.SampleSource(sa.SampleId) == wav,
                "source: a Session take remembers its file too");

            var w = new List<string>();
            ProjectService.Save(ProjectService.Capture(src, transport, w), bundle, src);
            var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, ProjectService.ManifestName))).RootElement;
            var clips = json.GetProperty("tracks").EnumerateArray().SelectMany(t => t.TryGetProperty("audioClips", out var a) ? a.EnumerateArray() : Enumerable.Empty<JsonElement>()).ToList();
            int withSource = clips.Count(c => c.TryGetProperty("source", out var s) && s.GetString() == wav);
            int without = clips.Count(c => !c.TryGetProperty("source", out _));
            yield return (withSource == 2 && without == 1, $"save: project.json keeps the source of the two halves, none for the render ({withSource}/{without})");

            using var dst = new NotaEngine();
            var lw = ProjectService.Apply(ProjectService.Load(bundle), dst, bundle);
            int dTrk = -1;
            for (int i = 0; i < dst.TrackCount; i++) if (dst.TryGetTrackInfo(i, out var ti) && ti.Type == 0) { dTrk = ti.Id; break; }
            bool reloaded = dTrk > 0 && dst.TryGetAudioClipInfo(dTrk, 0, out var d0) && dst.SampleSource(d0.SampleId) == wav;
            yield return (lw.Count == 0 && reloaded, "load: a reopened project's clip still knows its file (not the bundle copy)");
            yield return (dTrk > 0 && dst.TryGetSessionAudioSlot(dTrk, 0, out var dsa) && dst.SampleSource(dsa.SampleId) == wav,
                "load: …and so does its Session take");
            yield return (dTrk > 0 && dst.TryGetAudioClipInfo(dTrk, 2, out var dr) && dst.SampleSource(dr.SampleId) == ""
                          && ClipFiles.BundleCopy(dst, dr.SampleId, bundle) is { } copy && File.Exists(copy) && copy.StartsWith(bundle, StringComparison.Ordinal),
                "locate: a clip with no source reveals the project's own copy");

            File.Delete(wav);
            yield return (dTrk > 0 && dst.TryGetAudioClipInfo(dTrk, 0, out var g0) && ClipFiles.Source(dst, g0.SampleId) is null,
                "locate: a source that was deleted is not offered");

            // A project from before spec 092: no "source" at all.
            var manifest = Path.Combine(bundle, ProjectService.ManifestName);
            File.WriteAllText(manifest, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(manifest), ",\\s*\"source\": \"[^\"]*\"", ""));
            using var old = new NotaEngine();
            var ow = ProjectService.Apply(ProjectService.Load(bundle), old, bundle);
            int oTrk = -1;
            for (int i = 0; i < old.TrackCount; i++) if (old.TryGetTrackInfo(i, out var ti) && ti.Type == 0) { oTrk = ti.Id; break; }
            yield return (oTrk > 0 && old.TryGetAudioClipInfo(oTrk, 0, out var o0) && old.SampleSource(o0.SampleId) == "",
                $"load: an older project without sources loads, with none ({ow.Count} warnings)");
        }
        finally
        {
            try { Directory.Delete(lib, true); } catch { }
            try { File.Delete(scratch); } catch { }
            try { Directory.Delete(bundle, true); } catch { }
        }
    }
}
