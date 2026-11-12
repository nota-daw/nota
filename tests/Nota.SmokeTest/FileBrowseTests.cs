// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Files tab's browse view (spec 092, #580): Back / Forward / Up like a web browser, the
// Computer level above the drives, what a typed or pasted path opens, and which entries a
// folder lists. Part of `--browser-files`.

using Nota.Application.Browser;
using Nota.Application.Samples;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class FileBrowseTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota092b-" + Guid.NewGuid().ToString("N")[..8]);
        string a = Path.Combine(dir, "A"), b = Path.Combine(a, "B"), c = Path.Combine(dir, "C");
        Directory.CreateDirectory(b); Directory.CreateDirectory(c);
        try
        {
            // ---- history ----
            var st = new FileBrowseState(a);
            st.Go(b); st.Go(c);
            st.GoBack();
            bool backOk = st.Location == b && st.CanGoForward;
            st.GoForward();
            bool fwdOk = st.Location == c && !st.CanGoForward;
            st.GoBack(); st.GoBack();
            st.Go(c);
            yield return (backOk && fwdOk && st.Location == c && !st.CanGoForward && st.CanGoBack,
                "browse: Back and Forward walk the visited folders; opening a folder after Back clears Forward");

            st.Go(c);
            yield return (st.BackCount == 1, "browse: opening the folder already shown adds no history");

            var cap = new FileBrowseState(a);
            for (int i = 0; i < 150; i++) cap.Go(i % 2 == 0 ? b : c);
            yield return (cap.BackCount == 100, "browse: history keeps the last 100 steps");

            // ---- up ----
            var up = new FileBrowseState(b);
            up.Up();
            bool parent = up.Location == a;
            string root = Path.GetPathRoot(a)!;
            up.Go(root);
            up.Up();
            yield return (parent && up.IsComputer && !up.CanGoUp, "browse: Up goes to the parent, from a drive root to Computer, then stops");
            yield return (FileBrowseState.Volumes() is { Count: > 0 } v
                          && (OperatingSystem.IsWindows() ? v.All(x => Path.GetPathRoot(x.Path) == x.Path) : v.Any(x => x.Path == "/")),
                "browse: Computer lists the drives (the system disk among them)");
            var seg = new FileBrowseState(b).Segments();
            yield return (seg.Count >= 3 && seg[^1].Path == b && seg[^2].Path == a, "browse: the path bar has a segment per folder, outermost first");

            // ---- typed / pasted paths ----
            string wav = Path.Combine(a, "x.wav");
            WavWriter.WriteSine(wav, 0.05, 220, 44100);
            yield return (PathInput.Parse(a) == (a, null) && PathInput.Parse(a + Path.DirectorySeparatorChar) == (a, null),
                "path: a folder opens itself, trailing separator or not");
            yield return (PathInput.Parse(wav) == (a, wav), "path: a file opens its folder with the file selected");
            yield return (PathInput.Parse($"\"{a}\"") == (a, null) && PathInput.Parse($"'{a}'") == (a, null), "path: quoted pasted paths are unquoted");
            yield return (PathInput.Parse(new Uri(wav).AbsoluteUri) == (a, wav), "path: file:// URLs are understood");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return (PathInput.Parse("~")?.Folder == SampleFolderRules.Normalize(home), "path: ~ is the home folder");
            yield return (PathInput.Parse(Path.Combine(dir, "nope")) is null && PathInput.Parse("relative/path") is null && PathInput.Parse("  ") is null,
                "path: a missing, relative or empty path opens nothing");

            // ---- listing ----
            File.WriteAllText(Path.Combine(a, "notes.txt"), "x");
            File.WriteAllText(Path.Combine(a, ".secret.wav"), "x");
            Directory.CreateDirectory(Path.Combine(a, ".git"));
            TestFiles.Hide(Path.Combine(a, ".secret.wav"));
            TestFiles.Hide(Path.Combine(a, ".git"));
            Directory.CreateDirectory(Path.Combine(a, "a-first"));
            WavWriter.WriteSine(Path.Combine(a, "Zed.wav"), 0.05, 220, 44100);
            var (rows, state) = BrowserViewModel.ListBrowse(a);
            var names = rows.Select(r => r.Name).ToList();
            yield return (state == BrowseListState.Ok && names.SequenceEqual(new[] { "a-first", "B", "notes.txt", "x.wav", "Zed.wav" }),
                $"browse: folders first, then every file, each by name; hidden entries left out [{string.Join(", ", names)}]");
            var notes = rows.Single(r => r.Name == "notes.txt");
            var xw = rows.Single(r => r.Name == "x.wav");
            yield return (notes.Kind == BrowserItemKind.OtherFile && notes.Size == 1 && xw.Kind == BrowserItemKind.Sample && xw.Size == new FileInfo(wav).Length
                          && rows.Where(r => r.Kind == BrowserItemKind.Folder).All(r => r.Size is null),
                "browse: a file Nota can't add is marked as such; files carry their size, folders none");
            yield return (BrowserViewModel.ListBrowse(c).State == BrowseListState.Empty
                          && BrowserViewModel.ListBrowse(Path.Combine(dir, "nope")).State == BrowseListState.Unreadable,
                "browse: an empty folder and a missing one say so");

            var vm = new BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), new FactoryPresetCatalog());
            vm.BrowseTo(a);
            vm.RefreshBrowseAsync().GetAwaiter().GetResult();
            vm.FilterTree(3, "zed");
            yield return (vm.BrowseItems.Count == 1 && vm.BrowseItems[0].Name == "Zed.wav", "browse: search filters the folder shown");
            vm.FilterTree(3, "");
            vm.SetLocatedPath(wav);
            yield return (vm.BrowseItems.Single(r => r.Located).Path == wav, "browse: the selected clip's file is marked in the list");

            string sfile = Path.Combine(dir, "settings.json");
            File.WriteAllText(sfile, $"{{ \"SamplesFolder\": {System.Text.Json.JsonSerializer.Serialize(b)} }}");
            var hvm = new BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), new FactoryPresetCatalog(), settings: new SettingsService(sfile));
            hvm.BrowseTo(c);
            bool away = !hvm.BrowseAtHome;
            hvm.BrowseHome();
            bool atHome = hvm.Browse.Location == b && hvm.BrowseAtHome;
            hvm.Browse.GoBack();
            yield return (away && atHome && hvm.Browse.Location == c, "browse: Home opens the default sample folder; Back returns");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
