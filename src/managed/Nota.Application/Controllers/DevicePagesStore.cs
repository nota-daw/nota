// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Edited device pages (specs/051, FR-018 / FR-018a, contracts/device-pages.md). Two files of the
// same shape: the user's global <DataDir>/controllers/pages.json, applied in every project, and
// the open project's copy, devicepages.json in its bundle, written on save for the device types
// it contains. While a project is open its copy wins — so it behaves the same on another machine
// — and opening one never touches the global file. An edit writes both; a reset removes the type
// from both, so the shipped pages (or a plug-in's default) come back.

using Nota.Application.Localization;

namespace Nota.Application.Controllers;

public sealed class DevicePagesStore(string globalPath) : IDevicePageSource
{
    public const string ProjectFileName = "devicepages.json";

    private Dictionary<string, IReadOnlyList<DevicePage>> _global = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<DevicePage>> _project = new(StringComparer.Ordinal);

    /// <summary>Why the last file read failed (shown in Preferences), or null.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    public IReadOnlyList<DevicePage>? PagesFor(string typeKey)
        => _project.TryGetValue(typeKey, out var p) ? p : _global.TryGetValue(typeKey, out var g) ? g : null;

    /// <summary>Whether <paramref name="typeKey"/> has edited pages (the editor's Reset button).</summary>
    public bool IsEdited(string typeKey) => _project.ContainsKey(typeKey) || _global.ContainsKey(typeKey);

    /// <summary>Read the global file (start-up, after editing it by hand).</summary>
    public void LoadGlobal()
    {
        _global = Read(globalPath) ?? new(StringComparer.Ordinal);
        Changed?.Invoke();
    }

    /// <summary>A project opened (null: a new one) — its copy, empty for projects without one.</summary>
    public void LoadProject(string? bundleDir)
    {
        _project = bundleDir is null ? new(StringComparer.Ordinal) : Read(Path.Combine(bundleDir, ProjectFileName)) ?? new(StringComparer.Ordinal);
        Changed?.Invoke();
    }

    /// <summary>The project is saved: its copy holds the edited pages of the types it contains.
    /// No file when it has none.</summary>
    public void SaveProject(string bundleDir, IEnumerable<string> typeKeysInProject)
    {
        var keep = new Dictionary<string, IReadOnlyList<DevicePage>>(StringComparer.Ordinal);
        foreach (var k in typeKeysInProject.Distinct())
            if (PagesFor(k) is { } pages) keep[k] = pages;
        _project = keep;
        string path = Path.Combine(bundleDir, ProjectFileName);
        try
        {
            if (keep.Count == 0) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllText(path, DevicePagesFile.Write(keep));
        }
        catch (IOException) { /* the project still saves; its pages fall back to the global ones */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Edit a type's pages: the global file and the open project's copy (FR-018).</summary>
    public void Edit(string typeKey, IReadOnlyList<DevicePage> pages)
    {
        _global[typeKey] = pages;
        _project[typeKey] = pages;
        WriteGlobal();
        Changed?.Invoke();
    }

    /// <summary>Back to the shipped / default pages: the type leaves both files.</summary>
    public void Reset(string typeKey)
    {
        bool g = _global.Remove(typeKey), p = _project.Remove(typeKey);
        if (g) WriteGlobal();
        if (g || p) Changed?.Invoke();
    }

    private void WriteGlobal()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(globalPath)!);
            File.WriteAllText(globalPath, DevicePagesFile.Write(_global));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Dictionary<string, IReadOnlyList<DevicePage>>? Read(string path)
    {
        Error = null;
        if (!File.Exists(path)) return null;
        try
        {
            var types = DevicePagesFile.Parse(File.ReadAllText(path), out var error);
            if (error is not null) Error = Loc.F("controllers.profile.warning", Path.GetFileName(path), error);
            return types;
        }
        catch (IOException ex) { Error = Loc.F("controllers.profile.warning", Path.GetFileName(path), ex.Message); return null; }
    }
}
