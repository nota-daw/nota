// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// JSON-on-disk implementation of the ISettingsService port (contract lives in
// Application). Persists next to the engine's data, in NotaPaths.DataDir
// (settings.json).

using System.Text.Json;

namespace Nota.Infrastructure;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public Settings Current { get; private set; } = new();

    public event Action? Changed;

    public SettingsService()
    {
        _path = Path.Combine(NotaPaths.DataDir, "settings.json");

        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings();
        }
        catch
        {
            Current = new Settings(); // corrupt/older file → defaults
        }
    }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch { /* best-effort; not fatal */ }
        Changed?.Invoke();
    }

    private static string Home => NotaPaths.Home;

    public string ResolvedSamplesFolder()
        => EnsureDir(string.IsNullOrWhiteSpace(Current.SamplesFolder)
            ? Path.Combine(Home, "Music", "Nota Samples") : Current.SamplesFolder);

    public string ResolvedProjectsFolder()
        => EnsureDir(string.IsNullOrWhiteSpace(Current.ProjectsFolder)
            ? Path.Combine(Home, "Documents", "Nota Projects") : Current.ProjectsFolder);

    public string PresetsFolder() => NotaPaths.SubDir("presets");

    private static string EnsureDir(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        return dir;
    }
}
