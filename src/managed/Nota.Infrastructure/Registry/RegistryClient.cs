// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What the plugin and sample stores share: one static index.json per registry (on GitHub
// Pages), cached on disk for a day and used as the offline fallback, and downloads that must
// match the size + sha256 pinned in that index before anything is unpacked. Sources may be
// URLs or local paths (tests, and the NOTA_*_REGISTRY overrides).

using System.Security.Cryptography;
using System.Text.Json;

namespace Nota.Infrastructure;

internal sealed class RegistryClient
{
    private static readonly TimeSpan CacheFreshFor = TimeSpan.FromHours(24);

    private readonly string _indexSource;
    private readonly string _cachePath;
    private readonly string _what;   // "plugin registry" / "sample registry", for messages

    public RegistryClient(string indexSource, string cachePath, string what, string userAgent, HttpMessageHandler? handler)
    {
        _indexSource = indexSource;
        _cachePath = cachePath;
        _what = what;
        Http = handler is null ? new HttpClient() : new HttpClient(handler);
        Http.Timeout = Timeout.InfiniteTimeSpan;   // big assets; each request carries its own token
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public HttpClient Http { get; }

    /// <summary>The index JSON: the cache while it's fresh (unless <paramref name="refresh"/>),
    /// else the network, falling back to the cache when offline. <paramref name="validate"/>
    /// parses a download before it replaces the cache.</summary>
    public async Task<string> FetchIndexAsync(bool refresh, Action<string> validate, CancellationToken ct)
    {
        if (IsLocal(_indexSource, out var localPath))
            return await File.ReadAllTextAsync(localPath, ct).ConfigureAwait(false);
        if (!refresh && File.Exists(_cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath) < CacheFreshFor)
            return await File.ReadAllTextAsync(_cachePath, ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var json = await Http.GetStringAsync(_indexSource, cts.Token).ConfigureAwait(false);
            validate(json);   // don't cache something unparseable
            WriteAtomic(_cachePath, json);
            return json;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested
                                  || e is JsonException)
        {
            if (!File.Exists(_cachePath))
                throw new StoreException($"Couldn't reach the {_what}. Check your internet connection.", e);
            return await File.ReadAllTextAsync(_cachePath, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Downloads <paramref name="url"/> to <paramref name="file"/>, reporting progress, and
    /// refuses it unless it is exactly <paramref name="size"/> bytes with the given sha256.</summary>
    public async Task DownloadAsync(string url, long size, string sha256, string file, string name,
                                    IProgress<StoreProgress>? progress, CancellationToken ct)
    {
        string label = $"Downloading {name}…";
        progress?.Report(new StoreProgress(0, label));
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            Stream src;
            HttpResponseMessage? resp = null;
            if (IsLocal(url, out var localPath)) src = File.OpenRead(localPath);
            else
            {
                resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }
            using (resp)
            await using (src)
            await using (var dst = File.Create(file))
            {
                var buf = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > size)
                        throw new StoreException($"The download of {name} is larger than the registry says — not installing it.");
                    sha.AppendData(buf, 0, n);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Report(new StoreProgress((double)total / size, label));
                }
            }
        }
        catch (HttpRequestException e)
        {
            throw new StoreException($"Couldn't download {name}: {e.Message}", e);
        }

        var digest = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (total != size || digest != sha256)
            throw new StoreException($"The download of {name} doesn't match the registry's checksum — not installing it.");
    }

    /// <summary>A manifest-relative path ("a/b.vst3") under root; refuses paths that escape it.</summary>
    public static string Resolve(string root, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new StoreException($"Unsafe path in the registry entry: {rel}");
        return full;
    }

    /// <summary>The download's file extension (".tar.gz", ".zip", …), for the unpackers.</summary>
    public static string ExtensionOf(string url, string archive)
    {
        var name = url[(url.LastIndexOf('/') + 1)..].ToLowerInvariant();
        foreach (var ext in new[] { ".tar.gz", ".tar.xz", ".tar.bz2", ".tgz", ".txz", ".tar", ".zip", ".dmg", ".pkg", ".deb" })
            if (name.EndsWith(ext)) return ext;
        return "." + archive;
    }

    /// <summary>Registry ids are [a-z0-9-]; anything else could walk out of a store folder.</summary>
    public static bool IsValidId(string id)
        => id.Length > 0 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static bool IsLocal(string source, out string path)
    {
        if (source.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) { path = new Uri(source).LocalPath; return true; }
        path = source;
        return !source.Contains("://");
    }

    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
