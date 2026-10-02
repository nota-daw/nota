// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The AI models Nota can download, pinned in code: there are only a few, and each goes with
// the C# that runs it (StemSeparator, PitchTranscriber), so a new model ships with a new Nota
// rather than through a registry index. Every asset is fixed to a URL, size and sha256.
//   ONNX Runtime 1.23.2 — microsoft/onnxruntime's own release archives. 1.23 is the last
//     release with builds for all six targets (Intel Macs included); only the library and its
//     license files are kept out of each archive.
//   htdemucs — Demucs v4 (Meta, MIT) exported to ONNX without its STFT by
//     nota-daw/nota-models/scripts/export_htdemucs.py, published on that repo's releases.
//   basic-pitch — nmp.onnx straight from spotify/basic-pitch at the v0.4.0 tag.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Where a model's bytes come from and what of them is installed.</summary>
/// <param name="Archive">"file" (the download is the model), "tar" or "zip".</param>
/// <param name="Keep">For archives: the paths inside it to install (flat, by file name).</param>
/// <param name="Entry">The installed file Nota loads (model or runtime library).</param>
public sealed record ModelAsset(string Url, string Sha256, long Size, string Archive, IReadOnlyList<string> Keep, string Entry);

public sealed record CatalogModel(StoreModel Info, ModelAsset Asset);

public static class ModelCatalog
{
    private const string OrtVersion = "1.23.2";
    private const string OrtRelease = "https://github.com/microsoft/onnxruntime/releases/download/v" + OrtVersion + "/";

    /// <summary>The models and this computer's runtime (null when ONNX Runtime has no build for it).</summary>
    public static (IReadOnlyList<CatalogModel> Models, CatalogModel? Runtime) ForThisPlatform()
        => (Models, RuntimeFor(Rid()));

    public static readonly IReadOnlyList<CatalogModel> Models =
    [
        new(new StoreModel(AiModels.Stems, "htdemucs", "Separate Stems",
                "Splits a mix into drums, bass, vocals and everything else, each on its own track.",
                "Meta AI (Demucs v4)", "MIT", "https://github.com/facebookresearch/demucs",
                174264717, 174264717),
            new ModelAsset("https://github.com/nota-daw/nota-models/releases/download/htdemucs-v1/htdemucs.onnx",
                "04c2b1a149788d048f710a6140446a72f7b4bab3171bc42189176a64ed609c78", 174264717, "file", [], "htdemucs.onnx")),
        new(new StoreModel(AiModels.Transcription, "basic-pitch", "Convert to MIDI",
                "Hears every note in a recording, chords included, for Convert Melody and Convert Harmony.",
                "Spotify", "Apache-2.0", "https://github.com/spotify/basic-pitch",
                230444, 230444),
            new ModelAsset("https://raw.githubusercontent.com/spotify/basic-pitch/v0.4.0/basic_pitch/saved_models/icassp_2022/nmp.onnx",
                "2c3c1d144bfa61ad236e92e169c13535c880469a12a047d4e73451f2c059a0ec", 230444, "file", [], "nmp.onnx")),
    ];

    /// <summary>"osx-arm64", "win-x64", "linux-aarch64", … — ONNX Runtime's archive names.</summary>
    public static string Rid()
    {
        string os = OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsWindows() ? "win" : "linux";
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => os == "linux" ? "aarch64" : "arm64",
            Architecture.X64 => os == "osx" ? "x86_64" : "x64",
            var a => a.ToString().ToLowerInvariant(),
        };
        return $"{os}-{arch}";
    }

    public static CatalogModel? RuntimeFor(string rid)
    {
        (string Sha, long Size, string Lib, long LibSize)? a = rid switch
        {
            "osx-arm64" => ("b4d513ab2b26f088c66891dbbc1408166708773d7cc4163de7bdca0e9bbb7856", 9999931, "lib/libonnxruntime.1.23.2.dylib", 35138784),
            "osx-x86_64" => ("d10359e16347b57d9959f7e80a225a5b4a66ed7d7e007274a15cae86836485a6", 11676322, "lib/libonnxruntime.1.23.2.dylib", 39742608),
            "win-x64" => ("0b38df9af21834e41e73d602d90db5cb06dbd1ca618948b8f1d66d607ac9f3cd", 78127794, "lib/onnxruntime.dll", 14186016),
            "win-arm64" => ("1cfe88b6435df3b5fb0e9f6bd7d6f5df1e887b6174de7f6e2a47bab956f3f168", 78932411, "lib/onnxruntime.dll", 14868512),
            "linux-x64" => ("1fa4dcaef22f6f7d5cd81b28c2800414350c10116f5fdd46a2160082551c5f9b", 8309231, "lib/libonnxruntime.so.1.23.2", 22326072),
            "linux-aarch64" => ("7c63c73560ed76b1fac6cff8204ffe34fe180e70d6582b5332ec094810241e5c", 7254068, "lib/libonnxruntime.so.1.23.2", 18693384),
            _ => null,
        };
        if (a is not { } r) return null;
        bool win = rid.StartsWith("win", StringComparison.Ordinal);
        string dir = $"onnxruntime-{rid}-{OrtVersion}/";
        string file = $"onnxruntime-{rid}-{OrtVersion}" + (win ? ".zip" : ".tgz");
        return new(new StoreModel(AiModels.Runtime, "ONNX Runtime", "AI runtime",
                "The library Nota's AI models run on. Installed with the first model.",
                "Microsoft", "MIT", "https://github.com/microsoft/onnxruntime",
                r.Size, r.LibSize + 340_000),
            new ModelAsset(OrtRelease + file, r.Sha, r.Size, win ? "zip" : "tar",
                [dir + r.Lib, dir + "LICENSE", dir + "ThirdPartyNotices.txt"], Path.GetFileName(r.Lib)));
    }
}
