// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SC-001 for the Assistant (specs/098): 40 questions phrased the way users ask (20 English, 20
// Russian, not copied from headings), each with the manual pages that answer it. Prints the
// share whose reply quotes one of those pages — words only, and with the meaning model when a
// model folder is given. `--assistant-eval [<folder with model_quantized.onnx + tokenizer.json>]`.
// Targets: ≥ 80 % words only, ≥ 90 % with the model.

using System.Text.Json;
using Nota.Application.Assistant;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AssistantEval
{
    private sealed record Question(string q, string[] pages);

    public static int Run(string? modelDir)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Nota.sln"))) root = root.Parent;
        var qs = JsonSerializer.Deserialize<List<Question>>(File.ReadAllText(Path.Combine(root!.FullName, "tests/Nota.SmokeTest/Fixtures/assistant-questions.json")))!;
        var index = ManualIndexSource.ReadEmbedded();
        var search = new ManualSearch(index);

        TextEmbedder? emb = null;
        SectionVectorSet? vectors = null;
        if (modelDir is not null)
        {
            string ort = Environment.GetEnvironmentVariable("NOTA_ORT_LIB") ?? new ModelStore().RuntimePath
                         ?? throw new InvalidOperationException("no ONNX Runtime: set NOTA_ORT_LIB");
            emb = new TextEmbedder(ort, Path.Combine(modelDir, "model_quantized.onnx"), Path.Combine(modelDir, "tokenizer.json"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = new float[index.Sections.Count * TextEmbedder.Dim];
            for (int i = 0; i < index.Sections.Count; i += 16)
            {
                var batch = index.Sections.Skip(i).Take(16).Select(ManualMeaning.PassageText).ToList();
                var v = emb.EmbedBatch(batch, query: false);
                for (int k = 0; k < v.Length; k++) v[k].CopyTo(data, (i + k) * TextEmbedder.Dim);
            }
            vectors = new SectionVectorSet(index.Sections.Count, TextEmbedder.Dim, data);
            Console.WriteLine($"section vectors: {index.Sections.Count} in {sw.Elapsed.TotalSeconds:F1} s");
        }

        int words = 0, fused = 0;
        var qTimes = new List<double>();
        foreach (var q in qs)
        {
            bool w = Hit(AssistantReply.From(search.Search(q.q, "en")), q.pages);
            words += w ? 1 : 0;
            string f = "";
            if (emb is not null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var qv = emb.Embed(q.q, query: true);
                var r = search.Search(q.q, "en", queryVector: qv, vectors: vectors);
                qTimes.Add(sw.Elapsed.TotalMilliseconds);
                bool h = Hit(AssistantReply.From(r), q.pages);
                fused += h ? 1 : 0;
                f = h ? " ✓model" : " ✗model " + string.Join(", ", r.Hits.Where(ManualSearch.IsAnswer).Take(3).Select(x => x.Section.Id));
            }
            Console.WriteLine($"{(w ? "✓" : "✗")} {q.q}{f}" + (w ? "" : "  → " + string.Join(", ", search.Search(q.q, "en").Hits.Where(ManualSearch.IsAnswer).Take(3).Select(x => x.Section.Id))));
        }
        Console.WriteLine($"words only: {words}/{qs.Count} = {100.0 * words / qs.Count:F0} % (target ≥ 80 %)");
        if (emb is not null)
        {
            Console.WriteLine($"with the model: {fused}/{qs.Count} = {100.0 * fused / qs.Count:F0} % (target ≥ 90 %)");
            Console.WriteLine($"query time: median {qTimes.Order().ElementAt(qTimes.Count / 2):F0} ms, max {qTimes.Max():F0} ms");
        }
        emb?.Dispose();
        return 0;
    }

    private static bool Hit(ChatMessage reply, string[] pages) => reply.Quoted.Any(s => pages.Contains(s.Page));
}
