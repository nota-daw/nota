// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// JSON-on-disk implementation of the ISettingsService port (contract lives in
// Application). Persists next to the engine's data, in NotaPaths.DataDir
// (settings.json).

using System.Text.Json;
using Nota.Application.Browser;

namespace Nota.Infrastructure;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public Settings Current { get; private set; } = new();

    public event Action? Changed;

    public event Action? SampleFoldersChanged;

    public SettingsService() : this(Path.Combine(NotaPaths.DataDir, "settings.json")) { }

    /// <summary>Tests: settings kept in <paramref name="path"/>.</summary>
    public SettingsService(string path)
    {
        _path = path;

        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings();
        }
        catch
        {
            Current = new Settings(); // corrupt/older file → defaults
        }
        RepairSampleFolders();
    }

    public void Save()
    {
        MirrorDefaultSampleFolder();
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch { /* best-effort; not fatal */ }
        Changed?.Invoke();
    }

    /// <summary>Where samples live when the user never chose a folder.</summary>
    public static string DefaultSamplesLocation() => Path.Combine(UserDirs.GetMusicDir(), "Nota Samples");

    public string ResolvedSamplesFolder()
    {
        FollowLegacyField();
        var def = Current.SampleFolders?.Find(e => e.IsDefault)?.Path;
        if (string.IsNullOrWhiteSpace(def)) def = string.IsNullOrWhiteSpace(Current.SamplesFolder) ? DefaultSamplesLocation() : Current.SamplesFolder;
        return EnsureDir(def);
    }

    // --- sample folders (spec 092) ---------------------------------------------------------

    // A pre-092 file has only SamplesFolder: it becomes the one (default) entry. Bad entries
    // (nested, duplicate) are dropped with a log line.
    private void RepairSampleFolders()
    {
        string legacy = string.IsNullOrWhiteSpace(Current.SamplesFolder) ? DefaultSamplesLocation() : Current.SamplesFolder;
        var (list, dropped) = SampleFolderRules.Repair(Current.SampleFolders, legacy);
        Current.SampleFolders = list;
        foreach (var d in dropped) Console.Error.WriteLine($"[settings] Sample folders: dropped '{d}' (empty, duplicate or nested in another folder)");
        MirrorDefaultSampleFolder();
    }

    // SamplesFolder stays the default folder's path, for older versions and Mosaic refs.
    private void MirrorDefaultSampleFolder()
    {
        var def = Current.SampleFolders?.Find(e => e.IsDefault);
        if (def is not null) Current.SamplesFolder = def.Path;
    }

    // Code that still sets the single field (older harnesses, scripts) moves the default
    // folder with it: a folder already in the list becomes the default, any other one
    // takes the default entry's place.
    private void FollowLegacyField()
    {
        var list = Current.SampleFolders;
        if (list is null || string.IsNullOrWhiteSpace(Current.SamplesFolder)) return;
        var legacy = SampleFolderRules.Normalize(Current.SamplesFolder);
        var def = list.Find(e => e.IsDefault);
        if (def is null || SampleFolderRules.PathComparer.Equals(def.Path, legacy)) return;
        var hit = list.Find(e => SampleFolderRules.PathComparer.Equals(e.Path, legacy));
        if (hit is not null) { foreach (var e in list) e.IsDefault = ReferenceEquals(e, hit); }
        else { def.Path = legacy; def.Name = null; }
    }

    private List<SampleFolderEntry> Folders
    {
        get
        {
            FollowLegacyField();
            return Current.SampleFolders ??= SampleFolderRules.Repair(null, DefaultSamplesLocation()).List;
        }
    }

    public IReadOnlyList<ResolvedSampleFolder> ResolvedSampleFolders()
    {
        var list = Folders;
        var result = new List<ResolvedSampleFolder>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            string path = e.IsDefault ? ResolvedSamplesFolder() : e.Path;   // only the default is created
            bool ok;
            try { ok = Directory.Exists(path); } catch { ok = false; }
            result.Add(new ResolvedSampleFolder(i, path, SampleFolderRules.Label(e), e.IsDefault, ok));
        }
        return result;
    }

    public SampleFolderResult AddSampleFolder(string path)
    {
        path = SampleFolderRules.Normalize(path);
        bool isDir;
        try { isDir = path.Length > 0 && Directory.Exists(path); } catch { isDir = false; }
        if (!isDir) return new(SampleFolderOutcome.NotADirectory);
        var r = SampleFolderRules.Check(Folders.ConvertAll(e => e.Path), path);
        if (!r.Ok) return r;
        Folders.Add(new SampleFolderEntry { Path = path });
        Save();
        SampleFoldersChanged?.Invoke();
        return r;
    }

    public bool RemoveSampleFolder(int index)
    {
        var list = Folders;
        if (list.Count <= 1 || index < 0 || index >= list.Count) return false;
        bool wasDefault = list[index].IsDefault;
        list.RemoveAt(index);
        if (wasDefault) list[Math.Min(index, list.Count - 1)].IsDefault = true;
        Save();
        SampleFoldersChanged?.Invoke();
        return true;
    }

    public void RenameSampleFolder(int index, string? name)
    {
        var list = Folders;
        if (index < 0 || index >= list.Count) return;
        list[index].Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Save();
        SampleFoldersChanged?.Invoke();
    }

    public void SetDefaultSampleFolder(int index)
    {
        var list = Folders;
        if (index < 0 || index >= list.Count || list[index].IsDefault) return;
        for (int i = 0; i < list.Count; i++) list[i].IsDefault = i == index;
        Save();
        SampleFoldersChanged?.Invoke();
    }

    public string ResolvedProjectsFolder()
        => EnsureDir(string.IsNullOrWhiteSpace(Current.ProjectsFolder)
            ? Path.Combine(UserDirs.GetDocumentsDir(), "Nota Projects") : Current.ProjectsFolder);

    public string PresetsFolder() => NotaPaths.SubDir("presets");

    private static string EnsureDir(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        return dir;
    }
}
