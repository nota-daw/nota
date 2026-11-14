// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Downloads the AI models (ModelCatalog) and the ONNX Runtime library they run on. Like the
// plugin and sample stores, a download must match its pinned size + sha256 before anything is
// kept (RegistryClient.DownloadAsync), and only unpacks — nothing it fetches is executed.
//
// Layout (NOTA_MODELS_DIR relocates the root):
//   <data>/models/<id>/      the model file (or the runtime library + its license files)
//     .nota-model            marker holding the asset's sha256: a folder is installed only if
//                            its marker matches the catalog, so a new pinned version reads as
//                            not installed and is fetched again
// An install builds the folder as <root>/.staging-<id> and swaps it in, so a failed or
// cancelled install never leaves a half-written model behind.

using Nota.Application.Localization;

namespace Nota.Infrastructure;

public sealed class ModelStore : IModelStore
{
    public const string MarkerFile = ".nota-model";

    private readonly string _root;
    private readonly IReadOnlyList<CatalogModel> _models;
    private readonly CatalogModel? _runtime;
    private readonly RegistryClient _http;
    private readonly SemaphoreSlim _installGate = new(1, 1);

    public ModelStore() : this(DefaultRoot(), ModelCatalog.Models, ModelCatalog.ForThisPlatform().Runtime) { }

    /// <param name="root">Where models are installed (tests pass a temp dir).</param>
    public ModelStore(string root, IReadOnlyList<CatalogModel> models, CatalogModel? runtime, HttpMessageHandler? handler = null)
    {
        _root = root;
        _models = models;
        _runtime = runtime;
        Directory.CreateDirectory(root);
        // Only the downloader is used: models are pinned in code, there is no index to fetch.
        _http = new RegistryClient("", "", "AI models", "Nota-ModelStore", handler);
    }

    private static string DefaultRoot()
        => Environment.GetEnvironmentVariable("NOTA_MODELS_DIR") is { Length: > 0 } over ? over : NotaPaths.SubDir("models");

    public event Action? Changed;

    public IReadOnlyList<StoreModel> Models => _models.Select(m => m.Info).ToList();

    public StoreModel? Runtime => _runtime?.Info;

    public bool IsInstalled(string id) => Find(id) is { } m && Installed(m);

    public long DownloadSize(string id)
    {
        var m = Find(id) ?? throw new StoreException(Loc.F("model_store.unknown_ai_model", id));
        long size = Installed(m) ? 0 : m.Asset.TotalSize;
        if (id != AiModels.Runtime && _runtime is not null && !Installed(_runtime)) size += _runtime.Asset.TotalSize;
        return size;
    }

    public string? RuntimePath => _runtime is not null && Installed(_runtime) ? EntryPath(_runtime) : null;

    public string? ModelPath(string id) => Find(id) is { } m && Installed(m) ? EntryPath(m) : null;

    public string? ModelFile(string id, string name)
        => Find(id) is { } m && Installed(m) && m.Asset.Extras.Any(e => e.Name == name) ? Path.Combine(FolderOf(m), name) : null;

    public async Task InstallAsync(string id, IProgress<StoreProgress>? progress = null, CancellationToken ct = default)
    {
        var model = Find(id) ?? throw new StoreException(Loc.F("model_store.unknown_ai_model", id));
        if (id != AiModels.Runtime && _runtime is null)
            throw new StoreException(Loc.T("model_store.ai_models_arent_available_for"));
        await _installGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CheckFreeSpace(id);
            if (id != AiModels.Runtime && !Installed(_runtime!)) await InstallOneAsync(_runtime!, progress, ct).ConfigureAwait(false);
            if (!Installed(model)) await InstallOneAsync(model, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _installGate.Release();
        }
        Changed?.Invoke();
    }

    public void Uninstall(string id)
    {
        if (Find(id) is not { } m) return;
        Delete(m);
        // The runtime goes with the last model. On Windows a loaded DLL can't be deleted; it
        // stays until the next uninstall then.
        if (id != AiModels.Runtime && _runtime is not null && !_models.Any(Installed))
        {
            try { Delete(_runtime); } catch (StoreException) { }
        }
        Changed?.Invoke();
    }

    // ---- install ---------------------------------------------------------------------------

    private async Task InstallOneAsync(CatalogModel m, IProgress<StoreProgress>? progress, CancellationToken ct)
    {
        if (!RegistryClient.IsValidId(m.Info.Id)) throw new StoreException(Loc.F("model_store.invalid_model_id", m.Info.Id));
        var work = Directory.CreateTempSubdirectory("nota-model-").FullName;
        var staging = Path.Combine(_root, ".staging-" + m.Info.Id);
        try
        {
            var a = m.Asset;
            var file = Path.Combine(work, a.Archive == "file" ? a.Entry : "asset" + RegistryClient.ExtensionOf(a.Url, a.Archive));
            await _http.DownloadAsync(a.Url, a.Size, a.Sha256, file, m.Info.Name, progress, ct).ConfigureAwait(false);

            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            if (a.Archive == "file")
            {
                File.Move(file, Path.Combine(staging, a.Entry));
            }
            else
            {
                progress?.Report(new StoreProgress(-1, Loc.F("common.unpacking", m.Info.Name)));
                var unpacked = Path.Combine(work, "x");
                await Task.Run(() => ArchiveUnpacker.Unpack(file, a.Archive, unpacked, ct), ct).ConfigureAwait(false);
                foreach (var keep in a.Keep)
                {
                    var src = RegistryClient.Resolve(unpacked, keep);
                    if (!File.Exists(src)) throw new StoreException(Loc.F("model_store.the_download_of_doesnt_contain", m.Info.Name, keep));
                    File.Copy(src, Path.Combine(staging, Path.GetFileName(src)));
                }
            }
            foreach (var extra in a.Extras)
            {
                if (!RegistryClient.IsValidId(Path.GetFileNameWithoutExtension(extra.Name)) || extra.Name.Contains('/') || extra.Name.Contains('\\'))
                    throw new StoreException(Loc.F("model_store.invalid_model_id", extra.Name));
                var dest = Path.Combine(staging, extra.Name);
                await _http.DownloadAsync(extra.Url, extra.Size, extra.Sha256, dest, m.Info.Name, progress, ct).ConfigureAwait(false);
            }
            if (!File.Exists(Path.Combine(staging, a.Entry))) throw new StoreException(Loc.F("model_store.the_download_of_doesnt_contain", m.Info.Name, a.Entry));
            File.WriteAllText(Path.Combine(staging, MarkerFile), a.Sha256);

            var final = FolderOf(m);
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(staging, final);
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { /* best-effort */ }
            throw;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp; best-effort */ }
        }
    }

    // The download and the installed files both sit in temp / the models folder at once for a
    // moment; checked up front for everything this install needs, with 5% headroom.
    private void CheckFreeSpace(string id)
    {
        long need = 0;
        foreach (var m in new[] { Find(id), id == AiModels.Runtime ? null : _runtime })
            if (m is not null && !Installed(m)) need += m.Asset.TotalSize + m.Info.UnpackedSize;
        need += need / 20;
        if (FreeBytes(_root) is { } free && free < need)
            throw new StoreException(Loc.F("model_store.installing_this_model_needs_of", Mb(need), Mb(free)));
    }

    private static long? FreeBytes(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static string Mb(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1 << 20):0} MB";

    // ---- state -----------------------------------------------------------------------------

    private CatalogModel? Find(string id)
        => id == AiModels.Runtime ? _runtime : _models.FirstOrDefault(m => m.Info.Id == id);

    private string FolderOf(CatalogModel m) => Path.Combine(_root, m.Info.Id);

    private string EntryPath(CatalogModel m) => Path.Combine(FolderOf(m), m.Asset.Entry);

    private bool Installed(CatalogModel m)
    {
        try
        {
            var marker = Path.Combine(FolderOf(m), MarkerFile);
            return File.Exists(marker) && File.ReadAllText(marker).Trim() == m.Asset.Sha256 && File.Exists(EntryPath(m))
                   && m.Asset.Extras.All(e => File.Exists(Path.Combine(FolderOf(m), e.Name)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Delete(CatalogModel m)
    {
        var dir = FolderOf(m);
        if (!File.Exists(Path.Combine(dir, MarkerFile))) return;   // only ever a folder this store made
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new StoreException(Loc.F("model_store.couldnt_remove_it_may_be", m.Info.Name), e);
        }
    }
}
