// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The AI models Nota can download, pinned in code: there are only a few, and each goes with
// the C# that runs it (StemSeparator, PitchTranscriber), so a new model ships with a new Nota
// rather than through a registry index. Every asset is fixed to a URL, size and sha256.
//   ONNX Runtime 1.23.2 — the native library alone, mirrored on nota-daw/nota-models releases
//     (tag onnxruntime-1.23.2) with its LICENSE and ThirdPartyNotices: microsoft/onnxruntime,
//     which hosted the release archives, is gone (#969). The files come from Microsoft's signed
//     NuGet package Microsoft.ML.OnnxRuntime 1.23.2 (runtimes/<rid>/native), byte for byte the
//     library sizes the old archives carried. 1.23 is the last release with builds for all six
//     targets (Intel Macs included).
//   htdemucs — Demucs v4 (Meta, MIT) exported to ONNX without its STFT by
//     nota-daw/nota-models/scripts/export_htdemucs.py, published on that repo's releases.
//   basic-pitch — nmp.onnx straight from spotify/basic-pitch at the v0.4.0 tag.
//   multilingual-e5-small — intfloat's model (MIT) as the int8 ONNX export and tokenizer published
//     at Xenova/multilingual-e5-small, pinned to a commit; the Assistant's Manual search (specs/098).

using System.Runtime.InteropServices;
using Nota.Application.Localization;

namespace Nota.Infrastructure;

/// <summary>Where a model's bytes come from and what of them is installed.</summary>
/// <param name="Archive">"file" (the download is the model), "tar" or "zip".</param>
/// <param name="Keep">For archives: the paths inside it to install (flat, by file name).</param>
/// <param name="Entry">The installed file Nota loads (model or runtime library).</param>
/// <param name="Legacy">Earlier pins of the same files from another source: an install whose
/// marker holds one of these still counts as installed, so a moved download doesn't take a
/// working model away (its Extra files are then not required).</param>
public sealed record ModelAsset(string Url, string Sha256, long Size, string Archive, IReadOnlyList<string> Keep, string Entry,
                                IReadOnlyList<ModelFile>? Extra = null, IReadOnlyList<string>? Legacy = null)
{
    public IReadOnlyList<ModelFile> Extras => Extra ?? [];
    public IReadOnlyList<string> LegacySha256 => Legacy ?? [];
    /// <summary>Everything the asset downloads: the model and its extra files.</summary>
    public long TotalSize => Size + Extras.Sum(e => e.Size);
}

/// <summary>A file installed beside a model, downloaded and verified on its own (a tokenizer).</summary>
public sealed record ModelFile(string Url, string Sha256, long Size, string Name);

public sealed record CatalogModel(StoreModel Info, ModelAsset Asset);

public static class ModelCatalog
{
    private const string OrtVersion = "1.23.2";
    private const string OrtRelease = "https://github.com/nota-daw/nota-models/releases/download/onnxruntime-" + OrtVersion + "/";
    private const string OrtLicenseSha = "c250d6278f0b47a6439fb7592b08b58a55eb9f535aa49a1db63211c3f982b674";
    private const long OrtLicenseSize = 1094;
    private const string OrtNoticesSha = "e00f828e0a33de591a355ae6606d2625f5758da7d2c844db7821c9dd3e3647b6";
    private const long OrtNoticesSize = 333022;

    /// <summary>The models and this computer's runtime (null when ONNX Runtime has no build for it).</summary>
    public static (IReadOnlyList<CatalogModel> Models, CatalogModel? Runtime) ForThisPlatform()
        => (Models, RuntimeFor(Rid()));

    public static readonly IReadOnlyList<CatalogModel> Models =
    [
        new(new StoreModel(AiModels.Stems, "htdemucs", Loc.T("model_store.model_catalog.separate_stems"),
                Loc.T("model_store.model_catalog.splits_a_mix_into_drums"),
                "Meta AI (Demucs v4)", "MIT", "https://github.com/facebookresearch/demucs",
                174264717, 174264717),
            new ModelAsset("https://github.com/nota-daw/nota-models/releases/download/htdemucs-v1/htdemucs.onnx",
                "04c2b1a149788d048f710a6140446a72f7b4bab3171bc42189176a64ed609c78", 174264717, "file", [], "htdemucs.onnx")),
        new(new StoreModel(AiModels.Transcription, "basic-pitch", Loc.T("model_store.model_catalog.convert_to_midi"),
                Loc.T("model_store.model_catalog.hears_every_note_in_a"),
                "Spotify", "Apache-2.0", "https://github.com/spotify/basic-pitch",
                230444, 230444),
            new ModelAsset("https://raw.githubusercontent.com/spotify/basic-pitch/v0.4.0/basic_pitch/saved_models/icassp_2022/nmp.onnx",
                "2c3c1d144bfa61ad236e92e169c13535c880469a12a047d4e73451f2c059a0ec", 230444, "file", [], "nmp.onnx")),
        new(new StoreModel(AiModels.ManualSearch, "multilingual-e5-small", Loc.T("model_store.model_catalog.manual_search"),
                Loc.T("model_store.model_catalog.finds_manual_sections_by_meaning"),
                "intfloat (Microsoft)", "MIT", "https://huggingface.co/intfloat/multilingual-e5-small",
                E5Size + E5TokenizerSize, E5Size + E5TokenizerSize),
            new ModelAsset(E5Base + "onnx/model_quantized.onnx",
                "f80102d3f2a1229f387d3c81909990d8945513e347b0eab049f7de3c6f98c193", E5Size, "file", [], "model_quantized.onnx",
                [new ModelFile(E5Base + "tokenizer.json",
                    "0b44a9d7b51c3c62626640cda0e2c2f70fdacdc25bbbd68038369d14ebdf4c39", E5TokenizerSize, "tokenizer.json")])),
    ];

    private const string E5Base = "https://huggingface.co/Xenova/multilingual-e5-small/resolve/761b726dd34fb83930e26aab4e9ac3899aa1fa78/";
    private const long E5Size = 118308185, E5TokenizerSize = 17082730;

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
        // The mirrored file's sha256 and size, the name it is installed under, and the sha256 of
        // the microsoft/onnxruntime archive that installed the same library before #969 (Legacy:
        // those installs keep working; Manual search has no prompt to fetch the runtime again).
        (string Sha, long Size, string Ext, string Lib, string Old)? a = rid switch
        {
            "osx-arm64" => ("d306d2bc768540766c7ed8a1e0ff05d2870c77a934ebeee4a7bafa1b732ef299", 35138784, ".dylib", "libonnxruntime.1.23.2.dylib",
                            "b4d513ab2b26f088c66891dbbc1408166708773d7cc4163de7bdca0e9bbb7856"),
            "osx-x86_64" => ("8c9c78de65ea3786f987c0d980e9c1b13a3a5fbc6b3e2965ba05b450e6e4c054", 39742608, ".dylib", "libonnxruntime.1.23.2.dylib",
                             "d10359e16347b57d9959f7e80a225a5b4a66ed7d7e007274a15cae86836485a6"),
            "win-x64" => ("dec964ab1ee36cc9b0ae247d13b376627992fc57dec0454354017ab8fd84f1ea", 14186016, ".dll", "onnxruntime.dll",
                          "0b38df9af21834e41e73d602d90db5cb06dbd1ca618948b8f1d66d607ac9f3cd"),
            "win-arm64" => ("99a11bb077f723a81d100fd98099f332e4178bbb7025ac10fd40699a0b82c2f0", 14868512, ".dll", "onnxruntime.dll",
                            "1cfe88b6435df3b5fb0e9f6bd7d6f5df1e887b6174de7f6e2a47bab956f3f168"),
            "linux-x64" => ("13ab8084954fa4a47c777880180b90810d6020f021441395712b48a75b74c68b", 22326072, ".so", "libonnxruntime.so.1.23.2",
                            "1fa4dcaef22f6f7d5cd81b28c2800414350c10116f5fdd46a2160082551c5f9b"),
            "linux-aarch64" => ("648ffa64fbe027ae27139109410900cf776a030dec2dbbac51053318cc44c286", 18693384, ".so", "libonnxruntime.so.1.23.2",
                                "7c63c73560ed76b1fac6cff8204ffe34fe180e70d6582b5332ec094810241e5c"),
            _ => null,
        };
        if (a is not { } r) return null;
        string prefix = OrtRelease + "onnxruntime-" + OrtVersion + "-";
        var asset = new ModelAsset(prefix + rid + r.Ext, r.Sha, r.Size, "file", [], r.Lib,
            [new ModelFile(prefix + "LICENSE.txt", OrtLicenseSha, OrtLicenseSize, "license.txt"),
             new ModelFile(prefix + "ThirdPartyNotices.txt", OrtNoticesSha, OrtNoticesSize, "third-party-notices.txt")],
            [r.Old]);
        return new(new StoreModel(AiModels.Runtime, "ONNX Runtime", Loc.T("model_store.model_catalog.ai_runtime"),
                Loc.T("model_store.model_catalog.the_library_notas_ai_models"),
                "Microsoft", "MIT", "https://onnxruntime.ai",
                asset.TotalSize, asset.TotalSize),
            asset);
    }
}
