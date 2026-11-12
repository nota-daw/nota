// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which file on disk an audio clip stands for (spec 092, #581): the file it was made from
// when Nota knows it and it is still there, else the project's own copy of its audio
// (samples/<hash>.wav in the saved bundle). Locate → In Finder shows that file.

namespace Nota.Infrastructure;

public static class ClipFiles
{
    /// <summary>The clip's source file when it is known and exists; null otherwise.</summary>
    public static string? Source(IAudioEngine engine, long sampleId)
        => sampleId > 0 && engine.SampleSource(sampleId) is { Length: > 0 } p && File.Exists(p) ? p : null;

    /// <summary>The bundle's copy of the sample's audio, when the project is saved and holds
    /// it; null otherwise. May hash the audio once (cached per sample).</summary>
    public static string? BundleCopy(IAudioEngine engine, long sampleId, string? bundleDir)
    {
        if (sampleId <= 0 || string.IsNullOrEmpty(bundleDir)) return null;
        if (!engine.TryGetSampleInfo(sampleId, out var info)) return null;
        if (BundleContent.SampleHash(engine, sampleId, info) is not { } hash) return null;
        var path = Path.Combine(bundleDir, BundleContent.SampleRel(hash));
        return File.Exists(path) ? path : null;
    }
}
