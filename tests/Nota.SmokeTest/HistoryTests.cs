// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Project version history (.history/ inside the bundle): commits on save, no-op commits,
// branching from an older version, checkout restoring the manifest + sidecars, samples
// pinned by old versions surviving a save's prune, delete/collect, Save As carrying the
// history, erase, and a damaged history never costing data.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --history`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class HistoryTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string tmp = Path.GetTempPath();
        string wav = Path.Combine(tmp, "nota_smoke_history_sine.wav");
        WavWriter.WriteSine(wav, seconds: 1.0, freq: 220, sampleRate: 44100);
        string dir = Path.Combine(tmp, "nota-smoke-history-" + Guid.NewGuid().ToString("N") + ".nota");
        string copy = Path.Combine(tmp, "nota-smoke-history-copy-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        IProjectHistory history = new ProjectHistory("0.0.0-test");
        string manifest = Path.Combine(dir, ProjectService.ManifestName);
        string sections = Path.Combine(dir, "sections.json");

        void Save(NotaEngine e, string to) => ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), to, e);
        string[] Samples(string bundle) => Directory.Exists(Path.Combine(bundle, "samples"))
            ? Directory.GetFiles(Path.Combine(bundle, "samples"), "*.wav") : [];
        int AudioClips(string bundle)
        {
            using var e = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(bundle), e, bundle);
            int n = 0;
            for (int i = 0; i < e.TrackCount; i++)
                if (e.TryGetTrackInfo(i, out var ti) && ti.Type == 0) n += ti.ClipCount;
            return n;
        }

        try
        {
            yield return (history.Read(dir).Versions.Count == 0, "a bundle without history reads as empty");

            using var eng = new NotaEngine();
            eng.AddInstrumentTrack();
            Save(eng, dir);
            File.WriteAllText(sections, "[{\"start\":0,\"length\":8,\"name\":\"Intro\"}]");
            var v1 = history.Commit(dir, "first");
            yield return (v1 is { Parent: null, Note: "first", AppVersion: "0.0.0-test" } && history.Read(dir).Head == v1.Id,
                "first save records a root version and makes it the head");
            yield return (history.Commit(dir) is null, "saving without changes records nothing");

            eng.AddInstrumentTrack();
            Save(eng, dir);
            var v2 = history.Commit(dir);
            yield return (v2?.Parent == v1!.Id && v2.AddedBytes < 64 * 1024, $"an edit records a child version (+{v2?.AddedBytes} B)");

            int aTrk = eng.AddAudioTrack();
            eng.AddAudioClipEx(aTrk, wav, startBeat: 0, sourceOffsetFrames: 0, lengthFrames: 0, gain: 1f);
            File.Delete(sections);
            Save(eng, dir);
            var v3 = history.Commit(dir);
            long wavBytes = Samples(dir).Sum(f => new FileInfo(f).Length);
            yield return (v3 is not null && v3.AddedBytes >= wavBytes && wavBytes > 0,
                $"a version with new audio accounts for it (+{v3?.AddedBytes} B, sample {wavBytes} B)");
            string v3Manifest = File.ReadAllText(manifest);

            // Drop the audio track and save: the sample is no longer in the working copy, but v3 pins it.
            eng.RemoveTrack(aTrk);
            Save(eng, dir);
            yield return (Samples(dir).Length == 1, "a save keeps samples an older version needs");
            var v4 = history.Commit(dir);

            // Switch back to v3: manifest byte-identical, audio playable, the deleted sidecar stays absent.
            history.Checkout(dir, v3!.Id);
            yield return (File.ReadAllText(manifest) == v3Manifest && history.Read(dir).Head == v3.Id,
                "checkout restores the version's manifest and moves the head");
            yield return (!File.Exists(sections), "checkout removes a sidecar the version didn't have");
            yield return (AudioClips(dir) == 1, "the restored version reloads with its audio clip");

            history.Checkout(dir, v1.Id);
            yield return (File.Exists(sections) && File.ReadAllText(sections).Contains("Intro"), "checkout brings a sidecar back");

            // Save on top of v1 → a branch.
            using (var e1 = new NotaEngine())
            {
                ProjectService.Apply(ProjectService.Load(dir), e1, dir);
                e1.AddAudioTrack();
                Save(e1, dir);
            }
            var v5 = history.Commit(dir);
            var state = history.Read(dir);
            yield return (v5?.Parent == v1.Id && state.Versions.Count(v => v.Parent == v1.Id) == 2,
                "saving on top of an older version starts a branch");

            history.SetLabel(dir, v3.Id, "With audio");
            history.SetNote(dir, v3.Id, "  bass take  ");
            history.SetStarred(dir, v3.Id, true);
            var r3 = history.Read(dir).Versions.First(v => v.Id == v3.Id);
            yield return (r3 is { Label: "With audio", Note: "bass take", Starred: true }, "label, note and star persist");

            // Save As carries everything: history + the samples only old versions use.
            Directory.CreateDirectory(copy);
            using (var e2 = new NotaEngine())
            {
                ProjectService.Apply(ProjectService.Load(dir), e2, dir);
                Save(e2, copy);
            }
            history.CopyTo(dir, copy);
            yield return (history.Read(copy).Versions.Count == 5 && history.Read(copy).Head == v5!.Id,
                "Save As carries the whole history");
            history.Checkout(copy, v3.Id);
            yield return (AudioClips(copy) == 1, "an old version opens from the copy (its audio came along)");

            var size = history.Size(dir);
            yield return (size.Total > size.HistoryOnly && size.HistoryOnly >= wavBytes,
                $"size: {size.Total} B total, {size.HistoryOnly} B only for older versions");

            // Delete: not the head; children move up; the sample goes once nothing needs it.
            bool headRefused = false;
            try { history.Delete(dir, v5.Id); } catch (ProjectHistoryException) { headRefused = true; }
            yield return (headRefused, "the current version can't be deleted");
            history.Delete(dir, v2!.Id);
            yield return (history.Read(dir).Versions.First(v => v.Id == v3.Id).Parent == v1.Id,
                "deleting a version re-parents its children");
            history.Delete(dir, v4!.Id);
            long freed = history.Delete(dir, v3.Id);
            yield return (Samples(dir).Length == 0 && freed >= wavBytes, $"the last version using a sample frees it ({freed} B)");

            // A missing object: checkout refuses and leaves the working copy alone.
            string before = File.ReadAllText(manifest);
            foreach (var f in Directory.GetFiles(Path.Combine(dir, ".history", "objects"), "*", SearchOption.AllDirectories))
                File.Delete(f);
            bool refused = false;
            try { history.Checkout(dir, v1.Id); } catch (ProjectHistoryException) { refused = true; }
            yield return (refused && File.ReadAllText(manifest) == before, "a version with missing files isn't restored");

            // Damaged history: reads throw, and a save prunes nothing (the copy still has v3's audio).
            File.WriteAllText(Path.Combine(copy, ".history", "versions.json"), "{ not json");
            bool damaged = false;
            try { history.Read(copy); } catch (ProjectHistoryException) { damaged = true; }
            using (var e3 = new NotaEngine())
            {
                e3.AddInstrumentTrack();
                Save(e3, copy);
            }
            yield return (damaged && Samples(copy).Length == 1, "a damaged history is reported and protects its files");

            // Erase: history gone, only what the working copy uses is left.
            long erased = history.Erase(dir);
            yield return (!Directory.Exists(Path.Combine(dir, ".history")) && history.Read(dir).Versions.Count == 0 && erased > 0,
                $"erasing deletes the history ({erased} B)");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { Directory.Delete(copy, true); } catch { }
            try { File.Delete(wav); } catch { }
        }
    }
}
