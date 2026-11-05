// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface languages on this computer (spec 077, data-model § Installed language). Nota
// ships English only; a language downloaded from nota-translations is kept here, and a file a
// translator loads is remembered here by its path, so Nota starts in either with no network.
//
// Layout (<data>/languages, NOTA_DATA_DIR in tests):
//   <code>.json          a downloaded language file
//   <code>.meta.json     where it came from: { source: repository|local, path?, version?, commit?, sha256?, installed }
//   .staging-<code>.json a download in progress; left-overs are removed on start
// A download is written to its staging file and moved over the installed one only once it has
// been checked, so a failed or interrupted one never replaces a working language.

using System.Text.Json;
using System.Text.Json.Serialization;
using Nota.Application.Localization;

namespace Nota.Infrastructure;

/// <summary>Where an installed language came from.</summary>
public sealed record InstalledLanguageMeta(
    string Source, string? Path = null, int? Version = null, string? Commit = null, string? Sha256 = null, DateTime? Installed = null)
{
    public const string Repository = "repository";
    public const string Local = "local";

    [JsonIgnore] public bool IsLocal => Source == Local;
}

/// <summary>One language on this computer, as read at start-up.</summary>
public sealed record InstalledLanguage(string Code, InstalledLanguageMeta Meta, TranslationTable? Table, string? Error);

public sealed class InstalledLanguages
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public InstalledLanguages() : this(NotaPaths.SubDir("languages")) { }

    /// <param name="root">The languages folder (tests pass a temp dir).</param>
    public InstalledLanguages(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, ".staging-*").Concat(Directory.EnumerateFiles(root, "*.bak")))
                try { File.Delete(f); } catch { /* next start */ }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* an unreadable folder: List says so */ }
    }

    public string Root { get; }

    /// <summary>Every language with a meta file, read and parsed (a file that cannot be read or
    /// parsed is listed with its error and no table). Sorted by code.</summary>
    public IReadOnlyList<InstalledLanguage> List()
    {
        var list = new List<InstalledLanguage>();
        IEnumerable<string> metas;
        try { metas = Directory.EnumerateFiles(Root, "*.meta.json").Order(StringComparer.Ordinal).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return list; }
        foreach (var metaFile in metas)
        {
            var code = System.IO.Path.GetFileName(metaFile)[..^".meta.json".Length];
            if (Read(code) is { } l) list.Add(l);
        }
        return list;
    }

    /// <summary>One installed language, or null if <paramref name="code"/> has no meta file.</summary>
    public InstalledLanguage? Read(string code)
    {
        if (!IsSafeCode(code)) return null;
        var metaFile = MetaPath(code);
        if (!File.Exists(metaFile)) return null;
        InstalledLanguageMeta? meta;
        try { meta = JsonSerializer.Deserialize<InstalledLanguageMeta>(File.ReadAllText(metaFile), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { meta = null; }
        if (meta is null) return new InstalledLanguage(code, new InstalledLanguageMeta(InstalledLanguageMeta.Repository), null, "its record is unreadable");
        var file = meta.IsLocal ? meta.Path : FilePath(code);
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            return new InstalledLanguage(code, meta, null, meta.IsLocal ? $"the file {meta.Path} is missing" : "the language file is missing");
        string text;
        try { text = File.ReadAllText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new InstalledLanguage(code, meta, null, e.Message); }
        var table = TranslationTable.Parse(text, out var error);
        if (table is not null && table.Code != code) return new InstalledLanguage(code, meta, null, $"the file is for '{table.Code}', not '{code}'");
        return new InstalledLanguage(code, meta, table, error);
    }

    /// <summary>A file for a staging download of <paramref name="code"/>.</summary>
    public string StagingPath(string code) => System.IO.Path.Combine(Root, $".staging-{code}.json");

    /// <summary>Moves a checked download into place and records where it came from. If either
    /// write fails, the language as it was — file and record — is put back and the error thrown.</summary>
    public void InstallFrom(string staging, string code, InstalledLanguageMeta meta)
    {
        if (!IsSafeCode(code)) throw new ArgumentException($"not a language code: {code}");
        string file = FilePath(code), metaFile = MetaPath(code);
        string fileBak = file + ".bak", metaBak = metaFile + ".bak";
        bool hadFile = File.Exists(file), hadMeta = File.Exists(metaFile);
        if (hadFile) File.Copy(file, fileBak, overwrite: true);
        if (hadMeta) File.Copy(metaFile, metaBak, overwrite: true);
        try
        {
            File.Move(staging, file, overwrite: true);
            WriteMeta(code, meta with { Source = InstalledLanguageMeta.Repository, Path = null, Installed = DateTime.UtcNow });
        }
        catch
        {
            Restore(file, fileBak, hadFile);
            Restore(metaFile, metaBak, hadMeta);
            throw;
        }
        finally
        {
            foreach (var b in new[] { fileBak, metaBak }) try { File.Delete(b); } catch { /* next start */ }
        }
    }

    private static void Restore(string path, string backup, bool had)
    {
        try
        {
            if (had) File.Copy(backup, path, overwrite: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best effort: the error being thrown says what failed */ }
    }

    /// <summary>Remembers a translator's file by its path; it is read in place at each start
    /// and is never replaced by a download.</summary>
    public void InstallLocal(string path, string code)
    {
        if (!IsSafeCode(code)) throw new ArgumentException($"not a language code: {code}");
        var full = System.IO.Path.GetFullPath(path);
        // The record first: if it cannot be written, the installed language is untouched.
        WriteMeta(code, new InstalledLanguageMeta(InstalledLanguageMeta.Local, full, Installed: DateTime.UtcNow));
        // A downloaded copy is no longer used — unless it is the very file being loaded.
        var downloaded = FilePath(code);
        if (!string.Equals(System.IO.Path.GetFullPath(downloaded), full, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            try { File.Delete(downloaded); } catch { /* unused either way */ }
    }

    /// <summary>Forgets a language. A running one keeps working until the restart (its table is
    /// already in memory), then Nota is in English.</summary>
    public void Remove(string code)
    {
        if (!IsSafeCode(code)) return;
        foreach (var f in new[] { MetaPath(code), FilePath(code) })
            try { File.Delete(f); } catch { /* best effort */ }
    }

    /// <summary>A language code is also a file name here: refuse anything that could leave the folder.</summary>
    public static bool IsSafeCode(string code)
        => code.Length is >= 2 and <= 12 && code.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') && code[0] != '-';

    private string FilePath(string code) => System.IO.Path.Combine(Root, code + ".json");
    private string MetaPath(string code) => System.IO.Path.Combine(Root, code + ".meta.json");

    private void WriteMeta(string code, InstalledLanguageMeta meta)
        => RegistryClient.WriteAtomic(MetaPath(code), JsonSerializer.Serialize(meta, Json));
}
