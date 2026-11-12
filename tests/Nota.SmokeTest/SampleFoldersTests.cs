// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Sample folders (spec 092, #346 / #268): the list's rules (no duplicate, no folder inside
// another, one default, migration from the single Samples folder), the settings file
// keeping the old field as a mirror, the Files tree with one and with several folders, and
// the sample index scanning several roots. Runnable alone with the other spec 092 checks:
// `dotnet run --project tests/Nota.SmokeTest -- --browser-files`.

using Nota.Application.Browser;
using Nota.Application.Samples;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class SampleFoldersTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        foreach (var r in Rules()) yield return r;
        foreach (var r in Service()) yield return r;
        foreach (var r in Tree()) yield return r;
        foreach (var r in Index()) yield return r;
    }

    private static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "nota092-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- rules -------------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Rules()
    {
        // Expected paths go through the same normalising: on Windows "/x/A" becomes "C:\x\A".
        string xa = SampleFolderRules.Normalize("/x/A"), xb = SampleFolderRules.Normalize("/x/B");
        var (m1, _) = SampleFolderRules.Repair(null, "/x/A");
        yield return (m1.Count == 1 && m1[0].Path == xa && m1[0].IsDefault, "folders: an old settings file becomes one default folder");

        var (m2, dropped) = SampleFolderRules.Repair(new List<SampleFolderEntry>
        {
            new() { Path = "/x/A", IsDefault = true }, new() { Path = "/x/B/", IsDefault = true },
            new() { Path = "/x/A/Drums" }, new() { Path = "/x/A" },
        }, "/ignored");
        yield return (m2.Count == 2 && m2[1].Path == xb && m2.Count(e => e.IsDefault) == 1 && m2[0].IsDefault && dropped.Count == 2,
            "folders: on load, one default survives, trailing separators go, nested and duplicate entries are dropped");

        var (m3, _) = SampleFolderRules.Repair(new List<SampleFolderEntry> { new() { Path = "/x/A" }, new() { Path = "/x/B" } }, "/ignored");
        yield return (m3[0].IsDefault && !m3[1].IsDefault, "folders: a list with no default makes the first one the default");

        var list = new List<string> { "/x/A", "/x/B" };
        yield return (SampleFolderRules.Check(list, "/x/B/") is { Outcome: SampleFolderOutcome.AlreadyPresent, Other: 1 },
            "folders: adding a folder already in the list is refused");
        if (!OperatingSystem.IsLinux())
            yield return (SampleFolderRules.Check(list, "/X/a") is { Outcome: SampleFolderOutcome.AlreadyPresent },
                "folders: …in any letter case where the file system ignores it");
        yield return (SampleFolderRules.Check(list, "/x/A/Drums") is { Outcome: SampleFolderOutcome.Inside, Other: 0 },
            "folders: a folder inside one in the list is refused");
        yield return (SampleFolderRules.Check(list, "/x") is { Outcome: SampleFolderOutcome.Contains, Other: 0 },
            "folders: a folder around one in the list is refused");
        yield return (SampleFolderRules.Check(new List<string> { "/x/Drums" }, "/x/Drum").Ok
                      && !SampleFolderRules.IsSameOrInside("/x/Drums2/a", "/x/Drums") && SampleFolderRules.IsSameOrInside("/x/Drums/a", "/x/Drums"),
            "folders: /x/Drum is not inside /x/Drums (separator-aware)");
        yield return (SampleFolderRules.Label(new SampleFolderEntry { Path = "/x/Splice" }) == "Splice"
                      && SampleFolderRules.Label(new SampleFolderEntry { Path = "/x/Splice", Name = "My packs" }) == "My packs",
            "folders: the label is the name given, else the folder's name");
    }

    // ---- settings file -------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Service()
    {
        string dir = Temp();
        try
        {
            string a = Path.Combine(dir, "A"), b = Path.Combine(dir, "B"), c = Path.Combine(dir, "C");
            foreach (var d in new[] { a, b, c }) Directory.CreateDirectory(d);
            string file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, $"{{ \"SamplesFolder\": {System.Text.Json.JsonSerializer.Serialize(a)} }}");

            var s = new SettingsService(file);
            var f0 = s.ResolvedSampleFolders();
            yield return (f0.Count == 1 && f0[0].Path == a && f0[0].IsDefault && s.ResolvedSamplesFolder() == a,
                "settings: a pre-092 Samples folder migrates to the one default folder");

            int changed = 0;
            s.SampleFoldersChanged += () => changed++;
            var added = s.AddSampleFolder(b);
            string sub = Path.Combine(a, "Sub");
            Directory.CreateDirectory(sub);
            var nested = s.AddSampleFolder(sub);
            var missing = s.AddSampleFolder(Path.Combine(dir, "nope"));
            yield return (added.Ok && nested.Outcome == SampleFolderOutcome.Inside && missing.Outcome == SampleFolderOutcome.NotADirectory && changed == 1,
                "settings: Add takes a new folder, refuses a nested or missing one, and tells listeners once");

            s.AddSampleFolder(c);
            s.RenameSampleFolder(1, "  Splice  ");
            s.SetDefaultSampleFolder(1);
            var re = new SettingsService(file);
            var f1 = re.ResolvedSampleFolders();
            yield return (f1.Count == 3 && f1[1].Label == "Splice" && f1[1].IsDefault && re.ResolvedSamplesFolder() == b
                          && re.Current.SamplesFolder == b,
                "settings: names and the default survive a reload; SamplesFolder mirrors the default");

            yield return (re.RemoveSampleFolder(1) && re.ResolvedSampleFolders() is { Count: 2 } f2 && f2[1].IsDefault && f2[1].Path == c,
                "settings: removing the default makes the next folder the default");
            re.RemoveSampleFolder(0);
            yield return (!re.RemoveSampleFolder(0) && re.ResolvedSampleFolders().Count == 1, "settings: the last folder can't be removed");

            // Code that still writes the single old field moves the default with it.
            var legacy = new SettingsService(file);
            string dd = Path.Combine(dir, "D");
            Directory.CreateDirectory(dd);
            legacy.Current.SamplesFolder = dd;
            yield return (legacy.ResolvedSamplesFolder() == dd && legacy.ResolvedSampleFolders().Single(f => f.IsDefault).Path == dd,
                "settings: setting the old SamplesFolder field still moves the default folder");

            re.AddSampleFolder(b);
            Directory.Delete(b);
            var f3 = re.ResolvedSampleFolders();
            yield return (f3.Count == 2 && !f3[1].Available && f3[0].Available, "settings: a folder that went away stays listed, unavailable");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---- Files tree --------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Tree()
    {
        string dir = Temp();
        try
        {
            string a = Path.Combine(dir, "A"), b = Path.Combine(dir, "Bee");
            Directory.CreateDirectory(Path.Combine(a, "Drums"));
            Directory.CreateDirectory(Path.Combine(a, SamplePacks.FolderName, "Pack"));
            Directory.CreateDirectory(b);
            WavWriter.WriteSine(Path.Combine(a, "Drums", "kick.wav"), 0.1, 60, 44100);
            WavWriter.WriteSine(Path.Combine(a, SamplePacks.FolderName, "Pack", "p.wav"), 0.1, 60, 44100);
            WavWriter.WriteSine(Path.Combine(a, "top.wav"), 0.1, 60, 44100);
            WavWriter.WriteSine(Path.Combine(b, "loop.wav"), 0.1, 60, 44100);
            File.WriteAllText(Path.Combine(b, ".hidden.wav"), "x");

            string file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, $"{{ \"SamplesFolder\": {System.Text.Json.JsonSerializer.Serialize(a)} }}");
            var s = new SettingsService(file);
            var vm = new BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), new FactoryPresetCatalog(), settings: s);
            vm.RebuildSamples();
            yield return (vm.Samples.Count > 0 && vm.Samples[0].IsDownloads && vm.Samples.All(r => r.Depth == 0 && r.PlaceIndex < 0),
                "tree: one folder looks as before — its contents at the top, Downloaded first");

            s.AddSampleFolder(b);
            s.RenameSampleFolder(1, "Loops");
            vm.RebuildSamples();
            var roots = vm.Samples.Where(r => r.Depth == 0).ToList();
            yield return (roots.Count == 2 && roots[0].PlaceIndex == 0 && roots[0].Name == "A" && roots[1].PlaceIndex == 1 && roots[1].Name == "Loops",
                "tree: two folders are two top-level nodes, default first, with their labels");

            vm.ToggleExpand(roots[0]);
            vm.ToggleExpand(roots[1]);
            yield return (vm.Samples.Any(r => r.Name == "loop.wav" && r.Depth == 1) && !vm.Samples.Any(r => r.Name == ".hidden.wav")
                          && vm.Samples.SkipWhile(r => r.PlaceIndex != 0).Skip(1).First().IsDownloads,
                "tree: files sit under their folder, Downloaded leads inside the default one, hidden files stay out");

            vm.FilterTree(3, "loop");
            yield return (vm.Samples.Any(r => r.Name == "loop.wav") && !vm.Samples.Any(r => r.Name == "kick.wav"), "tree: search finds files in the second folder");
            vm.FilterTree(3, "");

            s.SetDefaultSampleFolder(1);
            vm.RebuildSamples();
            var swapped = vm.Samples.Where(r => r.Depth == 0).ToList();
            yield return (swapped.Count == 2 && swapped[0].Name == "Loops" && swapped[0].PlaceIndex == 1 && swapped[1].PlaceIndex == 0,
                "tree: a new default moves to the top and keeps its place in the list");
            s.SetDefaultSampleFolder(0);
            vm.RebuildSamples();

            Directory.Delete(b, true);
            vm.RebuildSamples();
            var gone = vm.Samples.FirstOrDefault(r => r.PlaceIndex == 1);
            yield return (gone is { Unavailable: true, HasChildren: false }, "tree: a missing folder shows as unavailable and empty");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---- index ---------------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Index()
    {
        string dir = Temp();
        try
        {
            string a = Path.Combine(dir, "A"), loops = Path.Combine(dir, "Loops");
            Directory.CreateDirectory(a); Directory.CreateDirectory(loops);
            string fa = Path.Combine(a, "tone.wav"), fb = Path.Combine(loops, "hit.wav");
            WavWriter.WriteSine(fa, 0.3, 220, 44100);
            WavWriter.WriteSine(fb, 0.3, 330, 44100);

            string file = Path.Combine(dir, "index.bin");
            var index = new SampleLibraryIndex(new SampleAnalyzer(), file);
            var done = new ManualResetEventSlim();
            index.Changed += () => { if (index.Progress.Total == 0 && index.Get(fa) is not null && index.Get(fb) is not null) done.Set(); };
            index.Watch(new[] { a, loops });
            bool ok = done.Wait(TimeSpan.FromSeconds(60));
            yield return (ok, "index: a scan over two folders analyses the files of both");
            yield return (index.Get(fb) is { } ib && ib.KindSource != SampleFactSource.Folder,
                $"index: hints are read below each folder — a folder named Loops doesn't make its files loops [{index.Get(fb)?.Describe()}]");

            Directory.Move(loops, loops + "-away");
            done.Reset();
            index.Changed += () => { if (index.Progress.Total == 0) done.Set(); };
            index.Watch(new[] { a, loops });
            done.Wait(TimeSpan.FromSeconds(30));
            Thread.Sleep(200);
            yield return (index.Get(fb) is not null && index.Get(fa) is not null, "index: an unplugged folder keeps its analyses");
            index.Dispose();
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
