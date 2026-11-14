// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Sentence vectors from multilingual-e5-small (specs/098, research R4): "query: " before a
// question, "passage: " before a manual section, mean-pooled over the attention mask and
// normalised, so a dot product is the cosine. Runs on ONNX Runtime's CPU provider with two
// threads at most — the engine keeps its cores — and only ever off the UI and audio threads.

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nota.Infrastructure;

internal sealed class TextEmbedder : IDisposable
{
    public const int Dim = 384;
    public const int MaxTokens = 256;

    private readonly InferenceSession _session;
    private readonly UnigramTokenizer _tokenizer;
    private readonly bool _typeIds;
    private readonly string _output;
    private readonly object _gate = new();

    public TextEmbedder(string runtimePath, string modelPath, string tokenizerPath)
    {
        OnnxRuntimeLoader.Use(runtimePath);
        _tokenizer = UnigramTokenizer.Load(tokenizerPath);
        _session = OnnxRuntimeLoader.Session(modelPath, intraOpThreads: 2);
        _typeIds = _session.InputMetadata.ContainsKey("token_type_ids");
        _output = _session.OutputMetadata.Keys.First();
    }

    public UnigramTokenizer Tokenizer => _tokenizer;

    private bool _disposed;

    /// <summary>Waits for a run in progress (it holds the same lock), then frees the session; a
    /// later call throws ObjectDisposedException instead of touching a freed session.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }

    public float[] Embed(string text, bool query) => EmbedBatch([text], query)[0];

    /// <summary>One normalised vector per text.</summary>
    public float[][] EmbedBatch(IReadOnlyList<string> texts, bool query)
    {
        var encoded = texts.Select(t => _tokenizer.Encode((query ? "query: " : "passage: ") + t, MaxTokens)).ToList();
        int batch = encoded.Count, len = encoded.Max(e => e.Length);
        var ids = new DenseTensor<long>([batch, len]);
        var mask = new DenseTensor<long>([batch, len]);
        for (int b = 0; b < batch; b++)
            for (int i = 0; i < len; i++)
            {
                bool real = i < encoded[b].Length;
                ids[b, i] = real ? encoded[b][i] : _tokenizer.Pad;
                mask[b, i] = real ? 1 : 0;
            }
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        };
        if (_typeIds) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>([batch, len])));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var results = _session.Run(inputs);
            var hidden = results.First(r => r.Name == _output).AsTensor<float>();   // [batch, len, dim]
            int dim = hidden.Dimensions[2];
            var outs = new float[batch][];
            for (int b = 0; b < batch; b++)
            {
                var v = new float[dim];
                int n = encoded[b].Length;
                for (int i = 0; i < n; i++)
                    for (int d = 0; d < dim; d++) v[d] += hidden[b, i, d];
                double norm = 0;
                for (int d = 0; d < dim; d++) { v[d] /= n; norm += v[d] * v[d]; }
                float inv = norm > 0 ? (float)(1 / Math.Sqrt(norm)) : 0;
                for (int d = 0; d < dim; d++) v[d] *= inv;
                outs[b] = v;
            }
            return outs;
        }
    }
}
