// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parses the plugin registry's index.json (github.com/nota-daw/nota-plugins-registry —
// its README documents the manifest) and reduces each plugin to the asset the running
// platform can install.

using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nota.Infrastructure;

public static class RegistryIndex
{
    /// <summary>Highest index schema this build understands.</summary>
    public const int SupportedSchema = 1;

    /// <summary>Asset keys the running process can load, most specific first. A plugin must match
    /// the process architecture (an x64 Nota under Rosetta loads x64 plugins).</summary>
    public static IReadOnlyList<string> PlatformKeys(OSPlatform? os = null, Architecture? arch = null)
    {
        var a = arch ?? RuntimeInformation.ProcessArchitecture;
        bool Is(OSPlatform p) => os is { } o ? o == p : RuntimeInformation.IsOSPlatform(p);
        string cpu = a == Architecture.Arm64 ? "arm64" : a == Architecture.X64 ? "x64" : "";
        if (cpu.Length == 0) return [];
        if (Is(OSPlatform.OSX)) return [$"macos-{cpu}", "macos-universal"];
        if (Is(OSPlatform.Windows)) return [$"windows-{cpu}"];
        if (Is(OSPlatform.Linux)) return [$"linux-{cpu}"];
        return [];
    }

    public static IReadOnlyList<StorePlugin> Parse(string json, IReadOnlyList<string> platformKeys)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int schema = root.TryGetProperty("schema", out var s) && s.TryGetInt32(out var n) ? n : 0;
        if (schema is < 1 or > SupportedSchema)
            throw new PluginStoreException("The plugin registry needs a newer version of Nota.");

        var list = new List<StorePlugin>();
        foreach (var p in root.GetProperty("plugins").EnumerateArray())
        {
            try { list.Add(ParsePlugin(p, platformKeys)); }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // One malformed entry must not hide the rest of the registry.
            }
        }
        return list;
    }

    private static StorePlugin ParsePlugin(JsonElement p, IReadOnlyList<string> platformKeys)
    {
        var latest = p.GetProperty("versions")[0];
        StoreAsset? asset = null;
        var assets = latest.GetProperty("assets");
        foreach (var key in platformKeys)
        {
            if (!assets.TryGetProperty(key, out var a)) continue;
            string url = a.GetProperty("url").GetString()!;
            asset = new StoreAsset(
                key, url,
                a.GetProperty("sha256").GetString()!.ToLowerInvariant(),
                a.GetProperty("size").GetInt64(),
                Str(a, "archive") ?? ArchiveUnpacker.KindOf(url) ?? throw new FormatException("archive type"),
                Str(a, "inner"),
                a.GetProperty("bundles").EnumerateArray().Select(b => b.GetString()!).ToList());
            break;
        }

        return new StorePlugin(
            Id: p.GetProperty("id").GetString()!,
            Name: p.GetProperty("name").GetString()!,
            Developer: p.GetProperty("developer").GetString()!,
            Description: p.GetProperty("description").GetString()!,
            Kind: p.GetProperty("kind").GetString()!,
            License: p.GetProperty("license").GetString()!,
            Repo: p.GetProperty("repo").GetString()!,
            Homepage: Str(p, "homepage"),
            Tags: p.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToList() : [],
            Provides: p.GetProperty("provides").EnumerateArray()
                .Where(x => Str(x, "format") == "VST3").Select(x => x.GetProperty("name").GetString()!).ToList(),
            Version: latest.GetProperty("version").GetString()!,
            Asset: asset)
        {
            Notes = Str(p, "notes"),
            Platforms = assets.EnumerateObject().Select(a => a.Name).ToList(),
        };
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Splits a hosted-plugin catalog identifier ("VST3-Surge XT-1a2b3c-4d5e6f", i.e.
    /// format-name-pathhash-uid) into its format and name; null if it isn't one.</summary>
    public static (string Format, string Name)? ParseIdentifier(string identifier)
    {
        int first = identifier.IndexOf('-');
        int uid = identifier.LastIndexOf('-');
        int hash = uid > 0 ? identifier.LastIndexOf('-', uid - 1) : -1;
        if (first <= 0 || hash <= first) return null;
        return (identifier[..first], identifier[(first + 1)..hash]);
    }
}
