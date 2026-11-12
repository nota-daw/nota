// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Where the Files tab's browse view is (spec 092, #580): one folder, or the "Computer"
// level above every drive, plus web-browser style Back / Forward history. Kept out of the
// view so the smoke test can pin the navigation rules and the per-OS volume list.

using System.Collections.Generic;
using System.IO;

namespace Nota.Application.Browser;

public sealed class FileBrowseState
{
    /// <summary>The pseudo-location above the volume roots: a list of drives / volumes.</summary>
    public const string Computer = "";

    private const int HistoryCap = 100;
    private readonly List<string> _back = new();
    private readonly List<string> _forward = new();

    public FileBrowseState(string start) { Location = start ?? Computer; }

    /// <summary>The folder shown, or <see cref="Computer"/>.</summary>
    public string Location { get; private set; }

    public bool IsComputer => Location.Length == 0;
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => !IsComputer;

    /// <summary>Raised after <see cref="Location"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Opens <paramref name="location"/>: the current one goes on the Back list and
    /// the Forward list is cleared. Opening the current location again does nothing.</summary>
    public void Go(string location)
    {
        location ??= Computer;
        if (location.Length > 0) location = SampleFolderRules.Normalize(location);
        if (SampleFolderRules.PathComparer.Equals(location, Location)) return;
        Push(_back, Location);
        _forward.Clear();
        Location = location;
        Changed?.Invoke();
    }

    public void GoBack()
    {
        if (_back.Count == 0) return;
        Push(_forward, Location);
        Location = Pop(_back);
        Changed?.Invoke();
    }

    public void GoForward()
    {
        if (_forward.Count == 0) return;
        Push(_back, Location);
        Location = Pop(_forward);
        Changed?.Invoke();
    }

    /// <summary>The parent folder; from a volume root, the Computer level.</summary>
    public void Up()
    {
        if (IsComputer) return;
        Go(ParentOf(Location));
    }

    /// <summary>The folder above <paramref name="folder"/>, or <see cref="Computer"/> for a
    /// volume root (or a macOS volume under /Volumes).</summary>
    public static string ParentOf(string folder)
    {
        folder = SampleFolderRules.Normalize(folder);
        if (folder.Length == 0) return Computer;
        string root = Path.GetPathRoot(folder) ?? "";
        if (SampleFolderRules.PathComparer.Equals(folder, root)) return Computer;
        if (OperatingSystem.IsMacOS() && SampleFolderRules.PathComparer.Equals(Path.GetDirectoryName(folder), "/Volumes"))
            return Computer;
        return Path.GetDirectoryName(folder) ?? Computer;
    }

    /// <summary>The path bar's segments, outermost first: each with the folder it opens.</summary>
    public IReadOnlyList<(string Label, string Path)> Segments()
    {
        var list = new List<(string, string)>();
        if (IsComputer) return list;
        for (string? p = Location; !string.IsNullOrEmpty(p); p = ParentOf(p))
        {
            string name = Path.GetFileName(p);
            if (name.Length == 0) name = VolumeLabel(p);
            list.Insert(0, (name, p));
            if (ParentOf(p).Length == 0) break;
        }
        return list;
    }

    /// <summary>The drives / volumes the Computer level lists: (label, root path).</summary>
    public static IReadOnlyList<(string Label, string Path)> Volumes()
    {
        var list = new List<(string, string)>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
                    list.Add((label, d.RootDirectory.FullName));
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                list.Add((VolumeLabel("/"), "/"));
                if (Directory.Exists("/Volumes"))
                    foreach (var v in Directory.EnumerateDirectories("/Volumes"))
                    {
                        // The boot volume appears here too, as a symlink to "/".
                        var info = new DirectoryInfo(v);
                        if (info.LinkTarget is not null) continue;
                        if (info.Name.StartsWith('.')) continue;
                        list.Add((info.Name, v));
                    }
            }
            else
            {
                list.Add(("/", "/"));
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (home.Length > 0) list.Add((Path.GetFileName(home), home));
                string user = Environment.UserName;
                foreach (var parent in new[] { $"/media/{user}", $"/run/media/{user}", "/mnt" })
                    if (Directory.Exists(parent))
                        foreach (var m in Directory.EnumerateDirectories(parent)) list.Add((Path.GetFileName(m), m));
            }
        }
        catch { /* best effort: an unreadable mount list leaves what was found */ }
        return list;
    }

    // "/" on macOS shows the boot volume's name when it can be found (Macintosh HD).
    private static string VolumeLabel(string root)
    {
        if (OperatingSystem.IsMacOS() && root == "/")
        {
            try
            {
                if (Directory.Exists("/Volumes"))
                    foreach (var v in Directory.EnumerateDirectories("/Volumes"))
                        if (new DirectoryInfo(v).LinkTarget is "/") return Path.GetFileName(v);
            }
            catch { }
            return "/";
        }
        return root.TrimEnd('\\', '/') is { Length: > 0 } r ? r : root;
    }

    private static void Push(List<string> stack, string value)
    {
        stack.Add(value);
        if (stack.Count > HistoryCap) stack.RemoveAt(0);
    }

    private static string Pop(List<string> stack)
    {
        var v = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return v;
    }

    /// <summary>Tests: how many steps Back holds.</summary>
    public int BackCount => _back.Count;
}
