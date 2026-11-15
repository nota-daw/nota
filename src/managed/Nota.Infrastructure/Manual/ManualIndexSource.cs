// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The manual index shipped inside this build (specs/098): embedded in this assembly from
// assets/manual/manual-index.json.gz, read once on a background thread the first time the
// Assistant panel opens — never at startup — and kept with its word-search tables.

using Nota.Application.Assistant;

namespace Nota.Infrastructure;

public static class ManualIndexSource
{
    public const string ResourceName = "Nota.Manual.manual-index.json.gz";

    private static readonly object Gate = new();
    private static Task<ManualSearch>? _load;

    /// <summary>The index with its search tables; started on first call, shared after.</summary>
    public static Task<ManualSearch> LoadAsync()
    {
        lock (Gate) return _load ??= Task.Run(() => new ManualSearch(ReadEmbedded()));
    }

    /// <summary>Reads the embedded index now (the smoke test and the loader).</summary>
    public static ManualIndex ReadEmbedded()
    {
        using var stream = typeof(ManualIndexSource).Assembly.GetManifestResourceStream(ResourceName);
        var index = stream is null ? ManualIndex.Failed(ResourceName + " missing") : ManualIndex.Read(stream);
        if (!index.IsAvailable) Console.Error.WriteLine("[assistant] manual index unavailable: " + index.Unavailable);
        return index;
    }
}
