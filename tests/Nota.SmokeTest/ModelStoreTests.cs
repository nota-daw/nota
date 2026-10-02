// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AI models (IModelStore): install / uninstall end to end against local assets — a plain model
// file and a runtime in a tar.gz, as ONNX Runtime ships — including the runtime coming with
// the first model and going with the last, checksum and archive-content refusals, cancellation,
// and a pinned version change. Also runnable alone: `--models`.

using Nota.Application;
using Nota.Infrastructure;
using static Nota.SmokeTest.PluginStoreTests;

namespace Nota.SmokeTest;

internal static class ModelStoreTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (ModelCatalog.RuntimeFor("osx-arm64") is { Asset.Entry: "libonnxruntime.1.23.2.dylib" }
                      && ModelCatalog.RuntimeFor("win-x64") is { Asset.Archive: "zip", Asset.Entry: "onnxruntime.dll" }
                      && ModelCatalog.RuntimeFor("linux-aarch64") is { Asset.Entry: "libonnxruntime.so.1.23.2" }
                      && ModelCatalog.RuntimeFor("freebsd-x64") is null,
            "every platform Nota ships on has a runtime; others have none");
        yield return (ModelCatalog.RuntimeFor(ModelCatalog.Rid()) is not null, $"…including this one ({ModelCatalog.Rid()})");
        yield return (ModelCatalog.Models.All(m => m.Asset.Sha256.Length == 64 && m.Asset.Size > 0 && m.Asset.Url.StartsWith("https://")),
            "every model is pinned to an https URL, size and sha256");

        var tmp = Directory.CreateTempSubdirectory("nota-models-test-").FullName;
        try
        {
            foreach (var r in Flow(tmp)) yield return r;
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    private static IEnumerable<(bool, string)> Flow(string tmp)
    {
        var src = Directory.CreateDirectory(Path.Combine(tmp, "src")).FullName;
        var root = Path.Combine(tmp, "models");

        var modelFile = Path.Combine(src, "m.onnx");
        File.WriteAllText(modelFile, "model bytes");
        var rtArchive = Path.Combine(src, "rt.tar.gz");
        WriteTarGz(rtArchive, ("ort-1/lib/libort.so", "native"), ("ort-1/LICENSE", "MIT"), ("ort-1/include/ort.h", "header"));

        CatalogModel Model(string id, string file, string? sha = null)
            => new(new StoreModel(id, id, "Feature", "", "", "MIT", "", new FileInfo(file).Length, new FileInfo(file).Length),
                   new ModelAsset(file, sha ?? Sha(file), new FileInfo(file).Length, "file", [], "m.onnx"));
        var runtime = new CatalogModel(new StoreModel(AiModels.Runtime, "Runtime", "", "", "", "MIT", "", new FileInfo(rtArchive).Length, 100),
            new ModelAsset(rtArchive, Sha(rtArchive), new FileInfo(rtArchive).Length, "tar", ["ort-1/lib/libort.so", "ort-1/LICENSE"], "libort.so"));
        var a = Model("model-a", modelFile);
        var b = Model("model-b", modelFile);

        var store = new ModelStore(root, [a, b], runtime);
        int changed = 0;
        store.Changed += () => changed++;
        yield return (!store.IsInstalled("model-a") && store.RuntimePath is null && store.ModelPath("model-a") is null,
            "nothing is installed at first");
        yield return (store.DownloadSize("model-a") == a.Asset.Size + runtime.Asset.Size,
            "the first model's download includes the runtime");

        var stages = new List<string>();
        store.InstallAsync("model-a", new SyncProgress(p => stages.Add(p.Message))).GetAwaiter().GetResult();
        yield return (store.IsInstalled("model-a") && store.IsInstalled(AiModels.Runtime) && changed == 1,
            "installing a model installs the runtime with it");
        yield return (store.RuntimePath is { } rp && File.ReadAllText(rp) == "native"
                      && File.Exists(Path.Combine(root, AiModels.Runtime, "LICENSE"))
                      && !File.Exists(Path.Combine(root, AiModels.Runtime, "ort.h")),
            "…keeping only the library and its license out of the archive");
        yield return (store.ModelPath("model-a") is { } mp && File.ReadAllText(mp) == "model bytes",
            "…and the model file where ModelPath says");
        yield return (stages.Any(s => s.StartsWith("Downloading Runtime")) && stages.Any(s => s.StartsWith("Downloading model-a")),
            "progress names each download");
        yield return (store.DownloadSize("model-b") == b.Asset.Size, "the second model downloads only itself");
        yield return (!Directory.EnumerateDirectories(root, ".staging-*").Any(), "no staging folder is left behind");

        // A new pinned version of a model reads as not installed (and so is fetched again).
        var a2 = Model("model-a", modelFile) with { Asset = a.Asset with { Sha256 = new string('0', 64) } };
        var newer = new ModelStore(root, [a2, b], runtime);
        yield return (!newer.IsInstalled("model-a") && newer.IsInstalled(AiModels.Runtime),
            "a model whose pinned checksum changed is no longer installed");
        yield return (Throws(() => newer.InstallAsync("model-a").GetAwaiter().GetResult()) && !newer.IsInstalled("model-a")
                      && store.IsInstalled("model-a"),
            "…and a download that doesn't match the pin is refused, the old files untouched");

        var badKeep = new CatalogModel(runtime.Info, runtime.Asset with { Keep = ["ort-1/lib/missing.so"], Entry = "missing.so" });
        var fresh = new ModelStore(Path.Combine(tmp, "fresh"), [a], badKeep);
        yield return (Throws(() => fresh.InstallAsync("model-a").GetAwaiter().GetResult()) && !fresh.IsInstalled(AiModels.Runtime) && !fresh.IsInstalled("model-a"),
            "an archive missing the library installs nothing");

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var c = new ModelStore(Path.Combine(tmp, "cancel"), [a], runtime);
            bool cancelled = false;
            try { c.InstallAsync("model-a", null, cts.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            yield return (cancelled && !c.IsInstalled("model-a") && !c.IsInstalled(AiModels.Runtime),
                "a cancelled install leaves nothing");
        }

        yield return (Throws(() => new ModelStore(Path.Combine(tmp, "none"), [a], null).InstallAsync("model-a").GetAwaiter().GetResult()),
            "without a runtime for this computer, models can't be installed");

        // Uninstall: the runtime stays while a model needs it, and goes with the last one.
        store.InstallAsync("model-b").GetAwaiter().GetResult();
        store.Uninstall("model-a");
        yield return (!store.IsInstalled("model-a") && store.IsInstalled("model-b") && store.IsInstalled(AiModels.Runtime),
            "removing one model keeps the runtime for the other");
        store.Uninstall("model-b");
        yield return (!store.IsInstalled("model-b") && !store.IsInstalled(AiModels.Runtime) && !Directory.Exists(Path.Combine(root, AiModels.Runtime)),
            "removing the last model removes the runtime too");

        var stray = Directory.CreateDirectory(Path.Combine(root, "model-a")).FullName;
        File.WriteAllText(Path.Combine(stray, "m.onnx"), "user file");
        store.Uninstall("model-a");
        yield return (File.Exists(Path.Combine(stray, "m.onnx")), "uninstall leaves a folder without the store's marker alone");
    }

    /// <summary>Live: installs the real runtime + basic-pitch from their pinned URLs into
    /// <paramref name="root"/> and transcribes a tone (`--models-live <dir>`). htdemucs too when
    /// <paramref name="withStems"/> — 174 MB.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunLive(string root, bool withStems)
    {
        var (models, rt) = ModelCatalog.ForThisPlatform();
        var store = new ModelStore(root, models, rt);
        foreach (var id in withStems ? new[] { AiModels.Transcription, AiModels.Stems } : [AiModels.Transcription])
        {
            string? err = null;
            try { store.InstallAsync(id, new SyncProgress(_ => { })).GetAwaiter().GetResult(); }
            catch (StoreException e) { err = e.Message; }
            yield return (store.IsInstalled(id) && store.RuntimePath is not null, $"{id} and the runtime install from their pinned URLs{(err is null ? "" : $" ({err})")}");
        }
        if (store.RuntimePath is not { } runtime || store.ModelPath(AiModels.Transcription) is not { } model) yield break;

        var tone = new float[PitchTranscriber.SampleRate * 2];
        for (int i = 0; i < tone.Length; i++) tone[i] = 0.5f * (float)Math.Sin(2 * Math.PI * 440 * i / PitchTranscriber.SampleRate);
        using var tr = new PitchTranscriber(runtime, model);
        var notes = tr.Transcribe(tone);
        yield return (notes.Count >= 1 && notes.All(n => n.Pitch == 69), $"the downloaded runtime + basic-pitch hear an A4 tone as A4 ({string.Join(", ", notes.Select(n => n.Pitch))})");

        if (withStems && store.ModelPath(AiModels.Stems) is { } stems)
        {
            var l = new float[StemSeparator.SampleRate * 3];
            for (int i = 0; i < l.Length; i++) l[i] = 0.3f * (float)Math.Sin(2 * Math.PI * 220 * i / StemSeparator.SampleRate);
            using var sep = new StemSeparator(runtime, stems);
            var o = sep.Separate(l, l);
            yield return (o.Length == 4 && o[0][0].Length == l.Length, "the downloaded htdemucs separates");
        }
    }

    // Progress<T> posts to the thread pool; tests want the reports in order, now.
    private sealed class SyncProgress(Action<StoreProgress> report) : IProgress<StoreProgress>
    {
        public void Report(StoreProgress value) => report(value);
    }
}
