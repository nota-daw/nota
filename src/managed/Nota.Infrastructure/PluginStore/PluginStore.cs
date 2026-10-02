// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Get Plug-ins: installs open-source VST3 plugins listed in the Nota plugin registry
// (github.com/nota-daw/nota-plugins-registry). The registry is one static index.json on
// GitHub Pages; assets download straight from each plugin's GitHub release and must match
// the size + sha256 pinned in the index before anything is unpacked (RegistryClient).
//
// Layout under <data>/plugins:
//   VST3/<id>/<Bundle>.vst3   installed bundles, one folder per plugin (the scan path)
//   registry-index.json       last fetched index (offline fallback, 24 h freshness)
//   installed.json            what was installed from where
// An install unpacks into a temp dir, copies the bundles into VST3/.staging-<id>, then
// swaps that folder in — a failed install never leaves a half-copied plugin behind.
// NOTA_PLUGIN_REGISTRY overrides the index URL (or points at a local index.json).

using System.Text.Json;

namespace Nota.Infrastructure;

public sealed class PluginStore : IPluginStore
{
    public const string DefaultIndexUrl = "https://nota-daw.github.io/nota-plugins-registry/index.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;
    private readonly IReadOnlyList<string> _platformKeys;
    private readonly RegistryClient _registry;
    private readonly object _gate = new();
    private IReadOnlyList<StorePlugin> _plugins = [];
    private List<InstalledStorePlugin> _installed;

    public PluginStore() : this(NotaPaths.SubDir("plugins")) { }

    /// <param name="root">Store folder (tests pass a temp dir).</param>
    /// <param name="indexSource">Index URL or local path; defaults to NOTA_PLUGIN_REGISTRY, then the public registry.</param>
    /// <param name="platformKeys">Asset keys to install, most specific first; defaults to the running platform.</param>
    public PluginStore(string root, string? indexSource = null, IReadOnlyList<string>? platformKeys = null, HttpMessageHandler? handler = null)
    {
        _root = root;
        var env = Environment.GetEnvironmentVariable("NOTA_PLUGIN_REGISTRY");
        _platformKeys = platformKeys ?? RegistryIndex.PlatformKeys();
        _registry = new RegistryClient(indexSource ?? (string.IsNullOrEmpty(env) ? DefaultIndexUrl : env),
                                       Path.Combine(root, "registry-index.json"), "plugin registry", "Nota-PluginStore", handler);
        PluginsDir = Path.Combine(root, "VST3");
        Directory.CreateDirectory(PluginsDir);
        _installed = LoadInstalled();
    }

    public string PluginsDir { get; }

    private string InstalledPath => Path.Combine(_root, "installed.json");

    public IReadOnlyList<InstalledStorePlugin> Installed
    {
        get { lock (_gate) return _installed.ToList(); }
    }

    // ---- registry ---------------------------------------------------------------------

    public async Task<IReadOnlyList<StorePlugin>> FetchAsync(bool refresh = false, CancellationToken ct = default)
    {
        var json = await _registry.FetchIndexAsync(refresh, j => RegistryIndex.Parse(j, _platformKeys), ct).ConfigureAwait(false);

        IReadOnlyList<StorePlugin> plugins;
        try { plugins = RegistryIndex.Parse(json, _platformKeys); }
        catch (JsonException e) { throw new PluginStoreException("The plugin registry index is damaged.", e); }
        lock (_gate) _plugins = plugins;
        return plugins;
    }

    public StorePlugin? FindProvider(string pluginIdentifier)
    {
        if (RegistryIndex.ParseIdentifier(pluginIdentifier) is not { Format: "VST3" } id) return null;
        lock (_gate)
            return _plugins.FirstOrDefault(p => p.Provides.Contains(id.Name, StringComparer.OrdinalIgnoreCase));
    }

    // ---- install / uninstall ----------------------------------------------------------

    public async Task InstallAsync(StorePlugin plugin, IProgress<StoreProgress>? progress = null, CancellationToken ct = default)
    {
        var asset = plugin.Asset ?? throw new PluginStoreException($"{plugin.Name} has no build for this computer.");
        var work = Directory.CreateTempSubdirectory("nota-plugin-").FullName;
        try
        {
            var file = Path.Combine(work, "asset" + RegistryClient.ExtensionOf(asset.Url, asset.Archive));
            await _registry.DownloadAsync(asset.Url, asset.Size, asset.Sha256, file, plugin.Name, progress, ct).ConfigureAwait(false);

            progress?.Report(new StoreProgress(-1, $"Unpacking {plugin.Name}…"));
            var bundleRoot = await Task.Run(() => Expand(asset, file, work, ct), ct).ConfigureAwait(false);

            progress?.Report(new StoreProgress(-1, $"Installing {plugin.Name}…"));
            var bundles = await Task.Run(() => Place(plugin.Id, asset, bundleRoot, ct), ct).ConfigureAwait(false);

            lock (_gate)
            {
                _installed.RemoveAll(i => i.Id == plugin.Id);
                _installed.Add(new InstalledStorePlugin(plugin.Id, plugin.Version, asset.Platform, asset.Sha256, bundles, DateTimeOffset.UtcNow));
                SaveInstalled();
            }
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp; best-effort */ }
        }
    }

    public void Uninstall(string id)
    {
        var dir = PluginDir(id);
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PluginStoreException("Couldn't remove the plugin's files — close any project that uses it and try again.", e);
        }
        lock (_gate)
        {
            if (_installed.RemoveAll(i => i.Id == id) > 0) SaveInstalled();
        }
    }

    private string PluginDir(string id)
    {
        if (!RegistryClient.IsValidId(id))
            throw new PluginStoreException($"Invalid plugin id \"{id}\".");
        return Path.Combine(PluginsDir, id);
    }

    private static string Expand(StoreAsset asset, string file, string work, CancellationToken ct)
    {
        var outer = Path.Combine(work, "outer");
        ArchiveUnpacker.Unpack(file, asset.Archive, outer, ct);
        if (asset.Inner is not { } inner) return outer;

        var nested = RegistryClient.Resolve(outer, inner);
        var kind = ArchiveUnpacker.KindOf(inner) ?? throw new PluginStoreException("Unknown nested archive type.");
        if (!File.Exists(nested) && !Directory.Exists(nested))
            throw new PluginStoreException($"The download doesn't contain {inner}.");
        var innerRoot = Path.Combine(work, "inner");
        ArchiveUnpacker.Unpack(nested, kind, innerRoot, ct);
        return innerRoot;
    }

    // Copies the manifest's bundles into VST3/.staging-<id>, then swaps it in as VST3/<id>.
    private List<string> Place(string id, StoreAsset asset, string bundleRoot, CancellationToken ct)
    {
        var final = PluginDir(id);
        var staging = Path.Combine(PluginsDir, ".staging-" + id);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            var placed = new List<string>();
            foreach (var rel in asset.Bundles)
            {
                ct.ThrowIfCancellationRequested();
                var src = RegistryClient.Resolve(bundleRoot, rel);
                if (!Directory.Exists(src) && !File.Exists(src))
                    throw new PluginStoreException($"The download doesn't contain {rel}.");
                var name = Path.GetFileName(src);
                ArchiveUnpacker.CopyBundle(src, Path.Combine(staging, name), ct);
                placed.Add(name);
            }
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(staging, final);
            return placed;
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { /* best-effort */ }
            throw;
        }
    }

    // ---- installed.json ---------------------------------------------------------------

    private sealed record InstalledFile(List<InstalledStorePlugin> Plugins);

    private List<InstalledStorePlugin> LoadInstalled()
    {
        try
        {
            if (!File.Exists(InstalledPath)) return [];
            var list = JsonSerializer.Deserialize<InstalledFile>(File.ReadAllText(InstalledPath), Json)?.Plugins ?? [];
            // Drop entries whose files were deleted by hand.
            return list.Where(i => Directory.Exists(Path.Combine(PluginsDir, i.Id))).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    private void SaveInstalled() => RegistryClient.WriteAtomic(InstalledPath, JsonSerializer.Serialize(new InstalledFile(_installed), Json));
}
