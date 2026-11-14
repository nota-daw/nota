// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Meaning search for the Assistant (specs/098, research R4/R10), when the Manual search model is
// installed. The manual's sections are embedded once per (index, model) in the background, at
// below-normal priority in batches of 16, and cached in the data folder; until that pass is done
// a question is answered by words alone. The model loads on first use and unloads after ten
// idle minutes. Nothing here runs on the UI or audio thread, and a failure of any kind falls
// back to words-only search silently (logged once).

using System.Security.Cryptography;
using Nota.Application.Assistant;

namespace Nota.Infrastructure;

public sealed class ManualMeaning : IMeaningSearch
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);
    private const int BatchSize = 16;
    private const string Magic = "NOTAVEC1";

    private static ManualMeaning? _shared;

    /// <summary>The app's instance over its model store (the one Preferences installs into, so
    /// an install or removal is seen at once).</summary>
    public static ManualMeaning Shared(IModelStore store) => _shared ??= new ManualMeaning(store, NotaPaths.SubDir("assistant"));

    private readonly IModelStore _store;
    private readonly string _cacheDir;
    private readonly object _gate = new();
    private TextEmbedder? _embedder;
    private Timer? _unload;
    private Task<SectionVectorSet?>? _vectors;
    private string _vectorsKey = "";
    private bool _logged;

    /// <summary>True while the transport records: the background pass waits (R10).</summary>
    public static Func<bool>? Busy { get; set; }

    public ManualMeaning(IModelStore store, string cacheDir)
    {
        _store = store;
        _cacheDir = cacheDir;
        _store.Changed += () => { lock (_gate) { _vectors = null; DropEmbedder(); } };
    }

    /// <summary>Whether the model is installed (the panel's search uses it then).</summary>
    public bool Available => _store.IsInstalled(AiModels.ManualSearch) && _store.RuntimePath is not null;

    /// <summary>Start the section pass early (the panel opened) when the model is there.</summary>
    public void Warm()
    {
        if (!Available) return;
        _ = Task.Run(async () =>
        {
            try { var s = await ManualIndexSource.LoadAsync().ConfigureAwait(false); _ = Vectors(s.Index); }
            catch (Exception e) { Log(e); }
        });
    }

    public async Task<(float[]? Query, SectionVectorSet? Sections)> ForQuestionAsync(string question, ManualIndex index, CancellationToken ct)
    {
        if (!Available || !index.IsAvailable) return (null, null);
        try
        {
            var vectorsTask = Vectors(index);
            // Not ready yet: answer by words now rather than make the user wait for the pass.
            if (!vectorsTask.IsCompleted) return (null, null);
            var vectors = await vectorsTask.ConfigureAwait(false);
            if (vectors is null) return (null, null);
            var q = await Task.Run(() => Embedder()?.Embed(question, query: true), ct).ConfigureAwait(false);
            return (q, vectors);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log(e);
            return (null, null);
        }
    }

    // ---- the model -------------------------------------------------------------------------

    private TextEmbedder? Embedder()
    {
        lock (_gate)
        {
            if (_embedder is null)
            {
                var runtime = _store.RuntimePath;
                var model = _store.ModelPath(AiModels.ManualSearch);
                var tok = _store.ModelFile(AiModels.ManualSearch, "tokenizer.json");
                if (runtime is null || model is null || tok is null) return null;
                _embedder = new TextEmbedder(runtime, model, tok);
            }
            _unload ??= new Timer(_ => { lock (_gate) DropEmbedder(); });
            _unload.Change(Idle, Timeout.InfiniteTimeSpan);
            return _embedder;
        }
    }

    private void DropEmbedder()
    {
        _embedder?.Dispose();
        _embedder = null;
    }

    // ---- section vectors -------------------------------------------------------------------

    private Task<SectionVectorSet?> Vectors(ManualIndex index)
    {
        string modelSha = _store.ModelPath(AiModels.ManualSearch) is { } p ? ShortHash(p) : "none";
        string key = index.Hash[..Math.Min(16, index.Hash.Length)] + "-" + modelSha;
        lock (_gate)
        {
            if (_vectors is not null && _vectorsKey == key) return _vectors;
            _vectorsKey = key;
            var t = new TaskCompletionSource<SectionVectorSet?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { t.SetResult(LoadOrBuild(index, key)); }
                catch (Exception e) { Log(e); t.SetResult(null); }
            })
            { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Assistant vectors" };
            thread.Start();
            return _vectors = t.Task;
        }
    }

    private SectionVectorSet? LoadOrBuild(ManualIndex index, string key)
    {
        Directory.CreateDirectory(_cacheDir);
        string path = Path.Combine(_cacheDir, $"vectors-{key}.bin");
        if (Read(path, index.Sections.Count) is { } cached) return cached;
        foreach (var stale in Directory.EnumerateFiles(_cacheDir, "vectors-*.bin"))
            try { File.Delete(stale); } catch (IOException) { }

        var embedder = Embedder();
        if (embedder is null) return null;
        int n = index.Sections.Count;
        var data = new float[n * TextEmbedder.Dim];
        for (int start = 0; start < n; start += BatchSize)
        {
            while (Busy?.Invoke() == true) Thread.Sleep(500);
            var batch = index.Sections.Skip(start).Take(BatchSize).Select(PassageText).ToList();
            var vecs = (Embedder() ?? embedder).EmbedBatch(batch, query: false);
            for (int i = 0; i < vecs.Length; i++) vecs[i].CopyTo(data, (start + i) * TextEmbedder.Dim);
        }
        var set = new SectionVectorSet(n, TextEmbedder.Dim, data);
        Write(path, set);
        return set;
    }

    /// <summary>What a section is embedded as: where it is, then what it says.</summary>
    public static string PassageText(ManualSection s) =>
        s.PageTitle + (s.Heading.Length > 0 ? " — " + s.Heading : "") + ". " + s.Markdown;

    private static SectionVectorSet? Read(string path, int count)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var br = new BinaryReader(File.OpenRead(path));
            if (new string(br.ReadChars(Magic.Length)) != Magic) return null;
            int n = br.ReadInt32(), dim = br.ReadInt32();
            if (n != count || dim != TextEmbedder.Dim) return null;
            var data = new float[n * dim];
            var bytes = br.ReadBytes(data.Length * 4);
            if (bytes.Length != data.Length * 4) return null;
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
            return new SectionVectorSet(n, dim, data);
        }
        catch (Exception e) when (e is IOException or EndOfStreamException) { return null; }
    }

    private static void Write(string path, SectionVectorSet set)
    {
        var tmp = path + ".tmp";
        using (var bw = new BinaryWriter(File.Create(tmp)))
        {
            bw.Write(Magic.ToCharArray());
            bw.Write(set.Count);
            bw.Write(set.Dim);
            var bytes = new byte[set.Data.Length * 4];
            Buffer.BlockCopy(set.Data, 0, bytes, 0, bytes.Length);
            bw.Write(bytes);
        }
        File.Move(tmp, path, overwrite: true);
    }

    // The installed model's marker holds its sha256; its first 8 hex digits key the cache.
    private static string ShortHash(string modelPath)
    {
        var marker = Path.Combine(Path.GetDirectoryName(modelPath)!, ModelStore.MarkerFile);
        try { return File.ReadAllText(marker).Trim()[..8]; }
        catch (Exception e) when (e is IOException or ArgumentOutOfRangeException)
        {
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(modelPath)))[..8].ToLowerInvariant();
        }
    }

    private void Log(Exception e)
    {
        if (_logged) return;
        _logged = true;
        Console.Error.WriteLine("[assistant] meaning search off: " + e.Message);
    }
}
